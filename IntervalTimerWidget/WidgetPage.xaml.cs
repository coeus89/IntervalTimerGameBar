using System;
using System.Globalization;
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

        private const double CollapsedHeight = 96;
        private const double ExpandedHeight = 340;
        private const double WidgetWidth = 300;

        private static readonly Brush PanelBrush =
            new SolidColorBrush(Color.FromArgb(0xEE, 0x1B, 0x1B, 0x1F));
        private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);

        private readonly AppDataSettingsStore _store = new AppDataSettingsStore();
        private readonly IntervalTimerEngine _engine = new IntervalTimerEngine();
        private readonly UwpSoundPlayer _sound = new UwpSoundPlayer();

        private XboxGameBarWidget _widget;
        private TimerSettings _settings = new TimerSettings();
        private bool _loading;

        public WidgetPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;

            _engine.Tick += (s, e) => RunOnUi(() => UpdateUi(e.Remaining, e.IsRunning));
            _engine.Elapsed += (s, e) => RunOnUi(() =>
            {
                _sound.Play(_settings.Sound, _settings.Volume);
                PersistRunState();
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _widget = e.Parameter as XboxGameBarWidget;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            _settings = TimerSettings.Load(_store);

            IntervalBox.Text = _settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
            VolumeSlider.Value = _settings.Volume * 100.0;
            SoundBox.ItemsSource = SoundCatalog.All;
            SoundBox.SelectedItem = _settings.Sound;
            TransparentCheck.IsChecked = _settings.TransparentBackground;
            AutoStartCheck.IsChecked = _settings.AutoStart;

            _engine.IntervalSeconds = _settings.IntervalSeconds;
            _loading = false;

            ApplyBackground();
            // Start collapsed so the settings aren't on screen during a game.
            SettingsPanel.Visibility = Visibility.Collapsed;
            RequestResize(false);

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
            PersistRunState();
            _engine.Dispose();
            _sound.Dispose();
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

        private void RequestResize(bool expanded)
        {
            try
            {
                ApplicationView.GetForCurrentView()?.TryResizeView(
                    new Size(WidgetWidth, expanded ? ExpandedHeight : CollapsedHeight));
            }
            catch
            {
                // No view (e.g. running before activation) - Game Bar sizes from the manifest.
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
            RequestResize(open);
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
                CommitInterval(CurrentIntervalInput());
        }

        private void OnIntervalUp(object sender, RoutedEventArgs e) => CommitInterval(CurrentIntervalInput() + 1);

        private void OnIntervalDown(object sender, RoutedEventArgs e) => CommitInterval(CurrentIntervalInput() - 1);

        private void OnSoundChanged(object sender, SelectionChangedEventArgs e)
        {
            var s = SoundBox.SelectedItem as SoundInfo;
            if (_loading || s == null) return;
            _settings.SoundKey = s.Key;
            SaveSettings();
        }

        private void OnVolumeChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            _settings.Volume = e.NewValue / 100.0;
            SaveSettings();
        }

        private void OnTestSound(object sender, RoutedEventArgs e) =>
            _sound.Play(_settings.Sound, _settings.Volume);
    }
}
