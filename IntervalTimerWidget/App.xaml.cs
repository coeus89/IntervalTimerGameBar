using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.Gaming.XboxGameBar;

namespace IntervalTimerWidget
{
    sealed partial class App : Application
    {
        private XboxGameBarWidget _widget;

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;

            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as IProtocolActivatedEventArgs;
                if (protocolArgs != null && protocolArgs.Uri.Scheme.Equals("ms-gamebarwidget"))
                {
                    widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
                }
            }

            if (widgetArgs == null)
                return;

            if (widgetArgs.IsLaunchActivation)
            {
                var rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;

                // Bootstraps the connection with Game Bar; must live for the process lifetime.
                _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);

                rootFrame.Navigate(typeof(WidgetPage), _widget);

                Window.Current.Closed += OnWidgetWindowClosed;
                Window.Current.Activate();
            }
            // else: a repeat activation for an already-running widget — nothing to do here.
        }

        private void OnWidgetWindowClosed(object sender, Windows.UI.Core.CoreWindowEventArgs e)
        {
            _widget = null;
            Window.Current.Closed -= OnWidgetWindowClosed;
        }

        /// <summary>
        /// Launched from Start rather than through Game Bar. Same page, ordinary app window,
        /// no <see cref="XboxGameBarWidget"/> - WidgetPage treats a null widget as standalone.
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }
            if (!e.PrelaunchActivated)
            {
                if (rootFrame.Content == null)
                    rootFrame.Navigate(typeof(WidgetPage), null);

                // UWP's default minimum view is 500x320 - far bigger than the timer needs, and
                // it would block the page from sizing itself down to the compact layout.
                ApplicationView.GetForCurrentView().SetPreferredMinSize(new Size(260, 88));
                ApplicationView.PreferredLaunchViewSize = new Size(300, 160);
                ApplicationView.PreferredLaunchWindowingMode =
                    ApplicationViewWindowingMode.PreferredLaunchViewSize;

                Window.Current.Activate();
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            _widget = null;
            deferral.Complete();
        }
    }
}
