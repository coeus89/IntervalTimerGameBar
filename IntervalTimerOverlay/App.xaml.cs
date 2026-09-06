using Microsoft.UI.Xaml;

namespace IntervalTimerOverlay
{
    public partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
        }

        public static MainWindow? Window { get; private set; }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Single-instance redirection happens in Program.Main before we get here; this
            // guard only covers OnLaunched somehow firing twice in one process.
            if (Window is not null)
            {
                BringToFront();
                return;
            }

            Window = new MainWindow();
            Window.Activate();
        }

        /// <summary>Show + focus the window. Safe to call from any thread.</summary>
        public static void BringToFront()
        {
            var w = Window;
            if (w is null) return;
            w.DispatcherQueue.TryEnqueue(() =>
            {
                w.AppWindow.Show();
                w.Activate();
                w.AppWindow.MoveInZOrderAtTop();
            });
        }
    }
}
