using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

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
            // Guard against OnLaunched firing more than once in this process.
            if (Window is not null)
            {
                BringToFront();
                return;
            }

            // Single instance: if another copy is already running, hand it this
            // activation and quit so we don't stack a second window.
            var main = AppInstance.FindOrRegisterForKey("IntervalTimerOverlay");
            if (!main.IsCurrent)
            {
                var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
                main.RedirectActivationToAsync(activated).AsTask().Wait();
                Process.GetCurrentProcess().Kill();
                return;
            }

            main.Activated += (_, _) => Window?.DispatcherQueue.TryEnqueue(BringToFront);

            Window = new MainWindow();
            Window.Activate();
        }

        private static void BringToFront()
        {
            var w = Window;
            if (w is null) return;
            w.AppWindow.Show();
            w.Activate();
            w.AppWindow.MoveInZOrderAtTop();
        }
    }
}
