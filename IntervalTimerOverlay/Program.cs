using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// Custom entry point (the XAML-generated Main is disabled via DISABLE_XAML_GENERATED_MAIN).
    /// It resolves single-instance *before* the XAML runtime starts, so a second launch
    /// redirects to the running copy and exits cleanly - no half-initialized App to tear down.
    /// </summary>
    public static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            if (RedirectToExistingInstance())
                return;

            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }

        private static bool RedirectToExistingInstance()
        {
            var keyInstance = AppInstance.FindOrRegisterForKey("IntervalTimerOverlay");
            if (keyInstance.IsCurrent)
            {
                // Future launches will raise this; bring our window forward instead.
                keyInstance.Activated += (_, _) => App.BringToFront();
                return false;
            }

            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance.RedirectActivationToAsync(activationArgs).AsTask().GetAwaiter().GetResult();
            return true;
        }
    }
}
