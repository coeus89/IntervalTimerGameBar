using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IntervalTimer.Core;
using Microsoft.Gaming.XboxGameBar;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace IntervalTimerWidget
{
    public sealed partial class WidgetPage : Page
    {
        private const string RunningKey = "IsRunning";
        private const string NextFireKey = "NextFireUtcTicks";

        private const double WidgetWidth = 300;

        // Standalone fallback for the <Size> bounds Game Bar supplies when it hosts us.
        private const double MinWindowHeight = 88;
        private const double MaxWindowHeight = 440;

        private static readonly Brush PanelBrush =
            new SolidColorBrush(Color.FromArgb(0xEE, 0x1B, 0x1B, 0x1F));
        private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);

        /// <summary>Friendly labels for <see cref="AnnouncementMode"/>, in display order.</summary>
        private static readonly AnnouncementModeItem[] AnnouncementModeItems =
        {
            new AnnouncementModeItem(AnnouncementMode.Sound, "Sound only"),
            new AnnouncementModeItem(AnnouncementMode.Speech, "Speak only"),
            new AnnouncementModeItem(AnnouncementMode.SoundThenSpeech, "Sound, then speak"),
        };

        private readonly AppDataSettingsStore _store = new AppDataSettingsStore();
        private readonly IntervalTimerEngine _engine = new IntervalTimerEngine();
        private readonly UwpSoundPlayer _sound = new UwpSoundPlayer();
        private readonly CustomSoundStore _customSounds = new CustomSoundStore();
        private readonly WindowsSpeechProvider _speechProvider = new WindowsSpeechProvider();
        private readonly AlertPlayer _alert;

        private XboxGameBarWidget _widget;
        private TimerSettings _settings = new TimerSettings();
        private IReadOnlyList<SoundInfo> _availableSounds = Array.Empty<SoundInfo>();
        // True until OnLoaded applies the saved settings. The slider's initial value
        // otherwise fires ValueChanged during InitializeComponent and would persist 50.
        private bool _loading = true;
        private int _announceCount;

        public WidgetPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;

            _alert = new AlertPlayer(_sound, _speechProvider);

            _engine.Tick += (s, e) => RunOnUi(() => UpdateUi(e.Remaining, e.IsRunning));
            _engine.Elapsed += (s, e) => RunOnUi(async () =>
            {
                _announceCount++;
                var context = new AnnouncementContext(_engine.Interval, _announceCount, DateTime.Now);
                await _alert.PlayAsync(_settings, context);
                PersistRunState();
            });
        }

        /// <summary>Wraps an <see cref="AnnouncementMode"/> with a user-facing label for the ComboBox.</summary>
        private sealed class AnnouncementModeItem
        {
            public AnnouncementModeItem(AnnouncementMode mode, string label)
            {
                Mode = mode;
                Label = label;
            }

            public AnnouncementMode Mode { get; }
            public string Label { get; }

            public override string ToString() => Label;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _widget = e.Parameter as XboxGameBarWidget;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            _settings = TimerSettings.Load(_store);

            IntervalBox.Text = _settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
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

            await LoadSoundsAsync();

            ApplyBackground();
            // Start collapsed so the settings aren't on screen during a game.
            SettingsPanel.Visibility = Visibility.Collapsed;
            ResizeToFitContent();

            // Resume a schedule that was running before the widget was suspended.
            object runningObj, nextObj;
            var wasRunning = _store.TryGet(RunningKey, out runningObj) && runningObj is bool && (bool)runningObj;
            if (wasRunning && _store.TryGet(NextFireKey, out nextObj))
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

            UpdateUi(_engine.Remaining, _engine.IsRunning);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CommitSpeechText();
            SaveSettings();
            PersistRunState();
            _engine.Dispose();
            _alert.Dispose();
            _sound.Dispose();
            _speechProvider.Dispose();
        }

        /// <summary>Re-reads the custom-sounds folder and rebuilds the sound ComboBox (built-ins first, then imports).</summary>
        private async Task LoadSoundsAsync()
        {
            IReadOnlyList<SoundInfo> custom;
            try { custom = await _customSounds.ListAsync(_settings.SoundKey); }
            catch { custom = Array.Empty<SoundInfo>(); }

            // Built-ins always stay in the list. The custom folder contributes at most one extra entry.
            var imported = custom.Where(s => s.IsCustom).Take(1);
            _availableSounds = SoundCatalog.All.Concat(imported).ToList();

            _loading = true;
            SoundBox.ItemsSource = _availableSounds;
            SoundBox.SelectedItem = SoundCatalog.Resolve(_settings.SoundKey, custom);
            _loading = false;

            UpdateDeleteSoundButtonState();
        }

        private void UpdateDeleteSoundButtonState()
        {
            var selected = SoundBox.SelectedItem as SoundInfo;
            DeleteSoundButton.IsEnabled = selected != null && selected.IsCustom;
        }

        private void UpdateSpeechControlsEnabled()
        {
            var enabled = _settings.AnnouncementMode != AnnouncementMode.Sound;
            VoiceBox.IsEnabled = enabled;
            SpeechTextBox.IsEnabled = enabled;
            SpeechRateSlider.IsEnabled = enabled;
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.HasThreadAccess) action();
            else _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        private void UpdateUi(TimeSpan remaining, bool running)
        {
            CountdownText.Text = Format(remaining);
            StartStopButton.Content = running ? "Stop" : "Start";
        }

        private static string Format(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format("{0:00}:{1:00}", t.Minutes, t.Seconds);
        }

        private void ApplyBackground()
        {
            RootGrid.Background = _settings.TransparentBackground ? ClearBrush : PanelBrush;
        }

        /// <summary>
        /// Grows/shrinks the Game Bar window to whatever the current content needs, so opening
        /// the settings panel reveals it instead of clipping it.
        /// </summary>
        private async void ResizeToFitContent()
        {
            try
            {
                // Game Bar's frame already has a bounded width (manifest min/max). A Start-menu
                // window does not — Windows restores the last size, which can be most of the
                // screen — so standalone always uses the compact design width.
                double width = WidgetWidth;
                if (_widget != null)
                {
                    var window = Window.Current;
                    if (window != null && window.Bounds.Width > 0) width = window.Bounds.Width;
                }

                // The live window is still at the old height, so ActualHeight would just report
                // the clipped size - measure unconstrained to get the height the content wants.
                RootGrid.UpdateLayout();
                RootGrid.Measure(new Size(width, double.PositiveInfinity));
                var height = Math.Ceiling(RootGrid.DesiredSize.Height);

                // Hosted: Game Bar publishes the manifest's <Size> bounds. Standalone: use the
                // same numbers so both hosts land on the same compact layout.
                var min = _widget != null ? _widget.MinWindowSize.Height : MinWindowHeight;
                var max = _widget != null ? _widget.MaxWindowSize.Height : MaxWindowHeight;
                if (max > 0) height = Math.Min(height, max);
                if (min > 0) height = Math.Max(height, min);

                var size = new Size(width, height);
                if (_widget != null)
                {
                    // Game Bar hosts us in its own frame, where ApplicationView.TryResizeView is
                    // ignored - only the widget SDK can move that window.
                    await _widget.TryResizeWindowAsync(size);
                }
                else
                {
                    var view = ApplicationView.GetForCurrentView();
                    if (view != null) view.TryResizeView(size);
                }
            }
            catch
            {
                // Resize is best-effort - a refused request just leaves the current size.
            }
        }

        private void PersistRunState()
        {
            _store.Set(RunningKey, _engine.IsRunning);
            _store.Set(NextFireKey, _engine.NextFireUtc.Ticks);
        }

        private void SaveSettings()
        {
            if (_loading) return;
            _settings.Save(_store);
        }

        // ---- handlers ----

        private void OnSettingsToggled(object sender, RoutedEventArgs e)
        {
            var open = SettingsToggle.IsChecked == true;
            SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ResizeToFitContent();
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

        private void OnStartStop(object sender, RoutedEventArgs e)
        {
            if (!_engine.IsRunning) _announceCount = 0;
            _engine.Toggle();
            PersistRunState();
        }

        private void CommitInterval(int seconds)
        {
            seconds = Math.Max(1, Math.Min(seconds, (int)IntervalTimerEngine.MaxInterval.TotalSeconds));
            _loading = true;
            IntervalBox.Text = seconds.ToString(CultureInfo.InvariantCulture);
            _loading = false;

            _settings.IntervalSeconds = seconds;
            _engine.IntervalSeconds = seconds;
            SaveSettings();
            PersistRunState();
        }

        private int CurrentIntervalInput()
        {
            int v;
            return int.TryParse(IntervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? v
                : _settings.IntervalSeconds;
        }

        private void OnIntervalCommitted(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CommitInterval(CurrentIntervalInput());
        }

        private void OnIntervalKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitInterval(CurrentIntervalInput());
                e.Handled = true;
            }
        }

        private void OnIntervalUp(object sender, RoutedEventArgs e) => CommitInterval(CurrentIntervalInput() + 1);

        private void OnIntervalDown(object sender, RoutedEventArgs e) => CommitInterval(CurrentIntervalInput() - 1);

        /// <summary>
        /// Game Bar can leave real OS keyboard focus on the hosting game even while a pinned
        /// widget's pointer-driven controls (buttons, sliders) keep working fine - mouse
        /// clicks are delivered by hit-testing alone, but typed characters are only delivered
        /// to whichever window currently owns keyboard focus. That mismatch is what makes the
        /// "+/-" buttons work while typing into a text box silently does nothing.
        /// Re-activating our window on every tap/click into a text entry forces Windows to
        /// hand keyboard focus back to the widget before the user starts typing.
        /// See: https://learn.microsoft.com/en-us/xbox/game-bar/guide/click-through
        /// </summary>
        private void OnTextEntryPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try { Window.Current?.Activate(); } catch { /* best-effort */ }
        }

        private void OnTextEntryGotFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.SelectAll();
        }

        private void OnSoundChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = SoundBox.SelectedItem as SoundInfo;
            if (_loading || s == null) return;
            _settings.SoundKey = s.Key;
            SaveSettings();
            UpdateDeleteSoundButtonState();
        }

        private void OnVolumeChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            _settings.Volume = e.NewValue / 100.0;
            SaveSettings();
        }

        private void OnTestSound(object sender, RoutedEventArgs e) =>
            _sound.Play(SoundBox.SelectedItem as SoundInfo ?? _settings.Sound, _settings.Volume);

        private async void OnImportSound(object sender, RoutedEventArgs e)
        {
            try
            {
                var imported = await _customSounds.ImportAsync();
                if (imported == null) return; // user cancelled the picker

                await LoadSoundsAsync();

                _loading = true;
                SoundBox.SelectedItem = _availableSounds.FirstOrDefault(s => s.Key == imported.Key);
                _loading = false;

                _settings.SoundKey = imported.Key;
                SaveSettings();
                UpdateDeleteSoundButtonState();
                ResizeToFitContent();
            }
            catch (InvalidOperationException ex)
            {
                var dialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Can't use that sound");
                await dialog.ShowAsync();
            }
            catch
            {
                // A picker or copy failure leaves the current sound in place.
            }
        }

        private async void OnDeleteSound(object sender, RoutedEventArgs e)
        {
            var selected = SoundBox.SelectedItem as SoundInfo;
            if (selected == null || !selected.IsCustom) return;

            await _customSounds.DeleteAsync(selected);

            if (string.Equals(_settings.SoundKey, selected.Key, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SoundKey = SoundCatalog.Default.Key;
                SaveSettings();
            }

            await LoadSoundsAsync();
            ResizeToFitContent();
        }

        private void OnAnnouncementModeChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = AnnouncementModeBox.SelectedItem as AnnouncementModeItem;
            if (_loading || item == null) return;
            _settings.AnnouncementMode = item.Mode;
            SaveSettings();
            UpdateSpeechControlsEnabled();
        }

        private void OnVoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            var voice = VoiceBox.SelectedItem as VoiceOption;
            if (_loading || voice == null) return;
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

        private void OnSpeechRateChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            _settings.SpeechRate = e.NewValue / 100.0;
            SaveSettings();
        }

        private async void OnTestAnnouncement(object sender, RoutedEventArgs e)
        {
            var context = new AnnouncementContext(_engine.Interval, Math.Max(_announceCount, 1), DateTime.Now);
            await _alert.PlayAsync(_settings, context);
        }
    }
}
