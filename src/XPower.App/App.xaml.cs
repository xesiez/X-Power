using System;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XPower
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += RootFrame_NavigationFailed;
                Window.Current.Content = rootFrame;
            }

            try
            {
                if (rootFrame.Content == null && !rootFrame.Navigate(typeof(MainPage), e.Arguments))
                    throw new InvalidOperationException("The main page could not be created.");
                Window.Current.Activate();
            }
            catch (Exception ex)
            {
                ShowStartupRecovery(ex);
            }
        }

        protected override void OnFileActivated(FileActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += RootFrame_NavigationFailed;
                Window.Current.Content = rootFrame;
            }

            try
            {
                object parameter = e.Files != null && e.Files.Count > 0 ? e.Files[0] : null;
                if (!rootFrame.Navigate(typeof(MainPage), parameter))
                    throw new InvalidOperationException("File activation navigation failed.");
                Window.Current.Activate();
            }
            catch (Exception ex)
            {
                ShowStartupRecovery(ex);
            }
        }

        private void RootFrame_NavigationFailed(object sender, Windows.UI.Xaml.Navigation.NavigationFailedEventArgs e)
        {
            e.Handled = true;
            ShowStartupRecovery(e.Exception);
        }

        private void ShowStartupRecovery(Exception exception)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(48),
                Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new TextBlock
            {
                Text = "X-POWER STARTUP RECOVERY",
                FontSize = 30,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "The app could not open its main screen. The diagnostic details are shown below instead of closing silently.",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = exception == null ? "Unknown startup error." : exception.ToString(),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
            Window.Current.Content = new ScrollViewer { Content = panel };
            Window.Current.Activate();
        }

        private void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Persisted settings and background transfer services will be added with their features.
        }
    }
}