using System;
using System.IO;
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

        private readonly JsonSettingsStore _store = new();
        private readonly IntervalTimerEngine _engine = new();
        private readonly MediaSoundPlayer _sound = new();
        private readonly DispatcherQueue _ui;

        private AppWindow _appWindow = null!;
        private TimerSettings _settings = new();
        private bool _loading;

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

            SoundBox.ItemsSource = SoundCatalog.All;
            SoundBox.SelectedItem = _settings.Sound;

            _engine.IntervalSeconds = _settings.IntervalSeconds;
            _loading = false;
        }

        private void WireEngine()
        {
            _engine.Tick += (_, e) => _ui.TryEnqueue(() => UpdateUi(e.Remaining, e.IsRunning));
            _engine.Elapsed += (_, _) => _ui.TryEnqueue(() =>
            {
                _sound.Play(_settings.Sound, _settings.Volume);
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
            _sound.Play(_settings.Sound, _settings.Volume);

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
            // Persist before stopping, so a timer that was running resumes on next launch.
            PersistRunState();
            _engine.Stop();
            _engine.Dispose();
            _sound.Dispose();
            TrayIcon.Dispose();
            Application.Current.Exit();
        }
    }
}
