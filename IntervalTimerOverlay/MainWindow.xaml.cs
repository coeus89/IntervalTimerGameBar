using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntervalTimer.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;

namespace IntervalTimerOverlay
{
    public sealed partial class MainWindow : Window
    {
        private const string PosXKey = "WindowX";
        private const string PosYKey = "WindowY";
        private const string RunningKey = "IsRunning";
        private const string NextFireKey = "NextFireUtcTicks";

        // Design size in DIPs; AppWindow works in physical pixels, so these get scaled by DPI.
        private const double WindowWidth = 320;
        private const double MinWindowHeight = 88;
        private const double MaxWindowHeight = 440;

        private static readonly Brush PanelBrush =
            new SolidColorBrush(Windows.UI.Color.FromArgb(0xEE, 0x1B, 0x1B, 0x1F));
        private static readonly Brush ClearBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        /// <summary>Friendly labels for <see cref="AnnouncementMode"/>, in display order.</summary>
        private static readonly AnnouncementModeItem[] AnnouncementModeItems =
        {
            new(AnnouncementMode.Sound, "Sound only"),
            new(AnnouncementMode.Speech, "Speak only"),
            new(AnnouncementMode.SoundThenSpeech, "Sound, then speak"),
        };

        private readonly JsonSettingsStore _store = new();
        private readonly IntervalTimerEngine _engine = new();
        private readonly MediaSoundPlayer _sound = new();
        private readonly CustomSoundStore _customSounds = new();
        private readonly WindowsSpeechProvider _speechProvider = new();
        private readonly AlertPlayer _alert;
        private readonly DispatcherQueue _ui;

        private AppWindow _appWindow = null!;
        private TimerSettings _settings = new();
        private IReadOnlyList<SoundInfo> _availableSounds = Array.Empty<SoundInfo>();
        // True until LoadSettings applies the saved values. Sliders with a XAML Value fire
        // ValueChanged during InitializeComponent and would otherwise persist their defaults.
        private bool _loading = true;
        private int _announceCount;

        /// <summary>Wraps an <see cref="AnnouncementMode"/> with a user-facing label for the ComboBox.</summary>
        private sealed record AnnouncementModeItem(AnnouncementMode Mode, string Label)
        {
            public override string ToString() => Label;
        }

        // drag state — tracked in screen pixels so moving the window doesn't feed back
        // into the pointer position (which is what caused the jitter).
        private bool _dragging;
        private PointInt32 _dragWindowOrigin;
        private POINT _dragCursorOrigin;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        private const uint MonitorDefaultToNull = 0;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        public MainWindow()
        {
            this.InitializeComponent();
            _ui = this.DispatcherQueue;
            _alert = new AlertPlayer(_sound, _speechProvider);

            ConfigureWindow();
            LoadSettings();
            WireEngine();
            WireDrag();

            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(ico))
                TrayIcon.IconSource = new BitmapImage(new Uri(ico));
            TrayIcon.ForceCreate();

            ApplyBackground();
            Root.Loaded += (_, _) =>
            {
                ResizeToFitContent();
                // Moving to a monitor with a different scale changes the pixel size we need.
                Root.XamlRoot.Changed += (_, _) => ResizeToFitContent();
            };

            RestoreRunState();
            UpdateUi(_engine.Remaining, _engine.IsRunning);
        }

        /// <summary>
        /// Resume a schedule that was running when the app last exited, so the countdown picks up
        /// where it left off instead of restarting; otherwise honour AutoStart.
        /// </summary>
        private void RestoreRunState()
        {
            var wasRunning = _store.TryGet(RunningKey, out var runningObj) && runningObj is true;
            if (wasRunning && _store.TryGet(NextFireKey, out var nextObj))
            {
                try
                {
                    var next = new DateTime(Convert.ToInt64(nextObj), DateTimeKind.Utc);
                    if (next > DateTime.UtcNow)
                        _engine.ResumeAt(next);
                    else
                        _engine.Start();
                }
                catch { _engine.Start(); }
            }
            else if (_settings.AutoStart)
            {
                _engine.Start();
            }
        }

        private void PersistRunState()
        {
            _store.Set(RunningKey, _engine.IsRunning);
            _store.Set(NextFireKey, _engine.NextFireUtc.Ticks);
        }

        private void ApplyBackground()
        {
            var transparent = _settings.TransparentBackground;
            Root.Background = transparent ? ClearBrush : PanelBrush;
            if (transparent)
            {
                if (SystemBackdrop is not TransparentBackdrop)
                    SystemBackdrop = new TransparentBackdrop();
            }
            else
            {
                SystemBackdrop = null;
            }
        }

        /// <summary>
        /// Grows/shrinks the window to whatever the current content needs, so opening the
        /// settings panel reveals it instead of clipping it.
        /// </summary>
        private void ResizeToFitContent()
        {
            var xamlRoot = Root.XamlRoot;
            if (xamlRoot is null) return;

            // The live window is still at the old height, so ActualHeight would just report
            // the clipped size - measure unconstrained to get the height the content wants.
            ContentGrid.UpdateLayout();
            ContentGrid.Measure(new Windows.Foundation.Size(WindowWidth, double.PositiveInfinity));
            var height = Math.Ceiling(ContentGrid.DesiredSize.Height);
            height = Math.Clamp(height, MinWindowHeight, MaxWindowHeight);

            var scale = xamlRoot.RasterizationScale;
            _appWindow.Resize(new SizeInt32(
                (int)Math.Ceiling(WindowWidth * scale),
                (int)Math.Ceiling(height * scale)));
        }

        private void ConfigureWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(id);

            _appWindow.Resize(new SizeInt32(320, 148));
            _appWindow.Title = "Interval Timer";

            if (_appWindow.Presenter is OverlappedPresenter p)
            {
                p.IsAlwaysOnTop = true;
                p.IsResizable = false;
                p.IsMaximizable = false;
                p.IsMinimizable = false;
                p.SetBorderAndTitleBar(false, false);
            }

            _appWindow.IsShownInSwitchers = false;

            // Restore last position, but only if it still overlaps a connected display.
            // AppWindow.Move succeeds for off-screen coordinates, and this window has no
            // taskbar button, so a removed monitor would otherwise leave it unreachable.
            if (_store.TryGet(PosXKey, out var x) && _store.TryGet(PosYKey, out var y))
            {
                try
                {
                    var px = Convert.ToInt32(x);
                    var py = Convert.ToInt32(y);
                    var size = _appWindow.Size;
                    if (OverlapsADisplay(px, py, size.Width, size.Height))
                        _appWindow.Move(new PointInt32(px, py));
                }
                catch { /* corrupt saved coordinates */ }
            }

            // The window never really closes; the tray "Exit" item ends the process.
            _appWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                _appWindow.Hide();
            };
        }

        private static bool OverlapsADisplay(int x, int y, int width, int height)
        {
            var rect = new RECT
            {
                Left = x,
                Top = y,
                Right = x + Math.Max(width, 1),
                Bottom = y + Math.Max(height, 1)
            };
            return MonitorFromRect(ref rect, MonitorDefaultToNull) != IntPtr.Zero;
        }

        private void LoadSettings()
        {
            _loading = true;
            _settings = TimerSettings.Load(_store);

            IntervalBox.Value = _settings.IntervalSeconds;
            VolumeSlider.Value = _settings.Volume * 100.0;
            TransparentCheck.IsChecked = _settings.TransparentBackground;
            AutoStartCheck.IsChecked = _settings.AutoStart;

            AnnouncementModeBox.ItemsSource = AnnouncementModeItems;
            AnnouncementModeBox.SelectedItem =
                AnnouncementModeItems.FirstOrDefault(i => i.Mode == _settings.AnnouncementMode) ?? AnnouncementModeItems[0];

            VoiceBox.ItemsSource = _speechProvider.Voices;
            VoiceBox.SelectedItem =
                _speechProvider.Voices.FirstOrDefault(v => v.Id == _settings.VoiceId) ?? _speechProvider.Voices.FirstOrDefault();

            SpeechTextBox.Text = _settings.SpeechText;
            SpeechRateSlider.Value = _settings.SpeechRate * 100.0;
            UpdateSpeechControlsEnabled();

            _engine.IntervalSeconds = _settings.IntervalSeconds;
            _loading = false;

            LoadSounds();
        }

        /// <summary>Re-reads the custom-sounds folder and rebuilds the sound ComboBox (built-ins first, then the import).</summary>
        private void LoadSounds()
        {
            IReadOnlyList<SoundInfo> custom;
            try { custom = _customSounds.List(_settings.SoundKey); }
            catch { custom = Array.Empty<SoundInfo>(); }

            _availableSounds = SoundCatalog.All.Concat(custom.Where(s => s.IsCustom).Take(1)).ToList();

            _loading = true;
            SoundBox.ItemsSource = _availableSounds;
            SoundBox.SelectedItem = SoundCatalog.Resolve(_settings.SoundKey, custom);
            _loading = false;

            UpdateDeleteSoundButtonState();
        }

        /// <summary>The sound to play: the saved key resolved against built-ins plus the current import.</summary>
        private SoundInfo CurrentSound => SoundCatalog.Resolve(_settings.SoundKey, _availableSounds);

        private void UpdateDeleteSoundButtonState() =>
            DeleteSoundButton.IsEnabled = SoundBox.SelectedItem is SoundInfo { IsCustom: true };

        private void UpdateSpeechControlsEnabled()
        {
            var enabled = _settings.AnnouncementMode != AnnouncementMode.Sound;
            VoiceBox.IsEnabled = enabled;
            SpeechTextBox.IsEnabled = enabled;
            SpeechRateSlider.IsEnabled = enabled;
        }

        private void WireEngine()
        {
            _engine.Tick += (_, e) => _ui.TryEnqueue(() => UpdateUi(e.Remaining, e.IsRunning));
            _engine.Elapsed += (_, _) => _ui.TryEnqueue(async () =>
            {
                _announceCount++;
                var context = new AnnouncementContext(_engine.Interval, _announceCount, DateTime.Now);
                await _alert.PlayAsync(_settings, CurrentSound, context);
                PersistRunState();
            });
        }

        private void WireDrag()
        {
            Header.PointerPressed += (s, e) =>
            {
                var el = (UIElement)s;
                if (!e.GetCurrentPoint(el).Properties.IsLeftButtonPressed) return;
                GetCursorPos(out _dragCursorOrigin);
                _dragWindowOrigin = _appWindow.Position;
                _dragging = true;
                el.CapturePointer(e.Pointer);
            };
            Header.PointerMoved += (s, e) =>
            {
                if (!_dragging) return;
                GetCursorPos(out var cur);
                _appWindow.Move(new PointInt32(
                    _dragWindowOrigin.X + (cur.X - _dragCursorOrigin.X),
                    _dragWindowOrigin.Y + (cur.Y - _dragCursorOrigin.Y)));
            };

            void EndDrag(object s, PointerRoutedEventArgs e)
            {
                if (!_dragging) return;
                _dragging = false;
                ((UIElement)s).ReleasePointerCapture(e.Pointer);
                var pos = _appWindow.Position;
                _store.Set(PosXKey, pos.X);
                _store.Set(PosYKey, pos.Y);
            }
            Header.PointerReleased += EndDrag;
            Header.PointerCaptureLost += EndDrag;
        }

        private void UpdateUi(TimeSpan remaining, bool running)
        {
            CountdownText.Text = Format(remaining);
            StartStopButton.Content = running ? "Stop" : "Start";
            TrayToggleItem.Text = running ? "Stop" : "Start";
        }

        private static string Format(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        private void SaveSettings()
        {
            if (_loading) return;
            _settings.Save(_store);
        }

        // ---- control handlers ----

        private void OnStartStop(object sender, RoutedEventArgs e) => ToggleTimer();

        private void OnTrayToggle(object sender, RoutedEventArgs e) => ToggleTimer();

        private void ToggleTimer()
        {
            if (!_engine.IsRunning) _announceCount = 0;
            _engine.Toggle();
            PersistRunState();
        }

        private void OnIntervalChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
        {
            if (_loading || double.IsNaN(e.NewValue)) return;
            var secs = Math.Max(1, (int)Math.Round(e.NewValue));
            _settings.IntervalSeconds = secs;
            _engine.IntervalSeconds = secs;
            SaveSettings();
            PersistRunState();
        }

        private void OnSoundChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SoundBox.SelectedItem is not SoundInfo s) return;
            _settings.SoundKey = s.Key;
            SaveSettings();
            UpdateDeleteSoundButtonState();
        }

        private async void OnImportSound(object sender, RoutedEventArgs e)
        {
            try
            {
                var imported = await _customSounds.ImportAsync(WindowNative.GetWindowHandle(this));
                if (imported == null) return; // user cancelled the picker

                _settings.SoundKey = imported.Key;
                SaveSettings();
                LoadSounds();
                ResizeToFitContent();
            }
            catch (InvalidOperationException ex)
            {
                await new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Title = "Can't use that sound",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
            catch
            {
                // A picker or copy failure leaves the current sound in place.
            }
        }

        private void OnDeleteSound(object sender, RoutedEventArgs e)
        {
            if (SoundBox.SelectedItem is not SoundInfo { IsCustom: true } selected) return;

            _customSounds.Delete(selected);
            if (string.Equals(_settings.SoundKey, selected.Key, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SoundKey = SoundCatalog.Default.Key;
                SaveSettings();
            }

            LoadSounds();
            ResizeToFitContent();
        }

        private void OnAnnouncementModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || AnnouncementModeBox.SelectedItem is not AnnouncementModeItem item) return;
            _settings.AnnouncementMode = item.Mode;
            SaveSettings();
            UpdateSpeechControlsEnabled();
        }

        private void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || VoiceBox.SelectedItem is not VoiceOption voice) return;
            _settings.VoiceId = voice.Id;
            SaveSettings();
        }

        private void OnSpeechTextChanged(object sender, TextChangedEventArgs e) => CommitSpeechText();

        private void OnSpeechTextCommitted(object sender, RoutedEventArgs e)
        {
            CommitSpeechText();
            if (_loading) return;
            if (string.IsNullOrWhiteSpace(SpeechTextBox.Text))
                SpeechTextBox.Text = _settings.SpeechText;
        }

        private void CommitSpeechText()
        {
            if (_loading) return;
            var text = SpeechTextBox.Text;
            _settings.SpeechText = string.IsNullOrWhiteSpace(text) ? AnnouncementFormatter.DefaultTemplate : text;
            SaveSettings();
        }

        private void OnSpeechRateChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            _settings.SpeechRate = e.NewValue / 100.0;
            SaveSettings();
        }

        private async void OnTestAnnouncement(object sender, RoutedEventArgs e)
        {
            var context = new AnnouncementContext(_engine.Interval, Math.Max(_announceCount, 1), DateTime.Now);
            await _alert.PlayAsync(_settings, CurrentSound, context);
        }

        private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            _settings.Volume = e.NewValue / 100.0;
            SaveSettings();
        }

        private void OnTransparentChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.TransparentBackground = TransparentCheck.IsChecked == true;
            ApplyBackground();
            SaveSettings();
        }

        private void OnAutoStartChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.AutoStart = AutoStartCheck.IsChecked == true;
            SaveSettings();
        }

        private void OnTestSound(object sender, RoutedEventArgs e) =>
            _sound.Play(SoundBox.SelectedItem as SoundInfo ?? CurrentSound, _settings.Volume);

        private void OnSettingsToggled(object sender, RoutedEventArgs e)
        {
            var open = SettingsToggle.IsChecked == true;
            SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ResizeToFitContent();
        }

        private void OnHideToTray(object sender, RoutedEventArgs e) => _appWindow.Hide();

        private void OnClose(object sender, RoutedEventArgs e) => ExitApp();

        private void OnTrayShowHide(object sender, RoutedEventArgs e)
        {
            if (_appWindow.IsVisible) _appWindow.Hide();
            else { _appWindow.Show(); _appWindow.MoveInZOrderAtTop(); }
        }

        private void OnTrayExit(object sender, RoutedEventArgs e) => ExitApp();

        private void ExitApp()
        {
            CommitSpeechText();
            // Persist before stopping, so a timer that was running resumes on next launch.
            PersistRunState();
            _engine.Stop();
            _engine.Dispose();
            _alert.Dispose();
            _sound.Dispose();
            _speechProvider.Dispose();
            TrayIcon.Dispose();
            Application.Current.Exit();
        }
    }
}
