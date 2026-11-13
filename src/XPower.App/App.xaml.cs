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
            UnhandledException += App_UnhandledException;
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Xbox navigation uses the XAML XY focus system by default.

            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += (sender, args) =>
                {
                    args.Handled = true;
                    ShowRecoveryScreen("Page navigation failed", args.Exception == null ? "Unknown navigation error." : args.Exception.ToString());
                };
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
                rootFrame.Navigate(typeof(MainPage), e.Arguments);

            Window.Current.Activate();
        }

        protected override void OnFileActivated(FileActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += (sender, args) =>
                {
                    args.Handled = true;
                    ShowRecoveryScreen("File activation failed", args.Exception == null ? "Unknown activation error." : args.Exception.ToString());
                };
                Window.Current.Content = rootFrame;
            }
            object parameter = e.Files != null && e.Files.Count > 0 ? e.Files[0] : null;
            rootFrame.Navigate(typeof(MainPage), parameter);
            Window.Current.Activate();
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ShowRecoveryScreen("X-Power recovered from an application error", e.Exception == null ? e.Message : e.Exception.ToString());
        }

        private void ShowRecoveryScreen(string title, string details)
        {
            try
            {
                var panel = new StackPanel
                {
                    Spacing = 18,
                    Padding = new Thickness(56),
                    Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 8, 13, 24))
                };
                panel.Children.Add(new TextBlock
                {
                    Text = "X-POWER / RECOVERY",
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.Bold,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LightGreen)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 28,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White)
                });
                panel.Children.Add(new TextBlock
                {
                    Text = details,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 190, 205, 225))
                });
                Window.Current.Content = new ScrollViewer { Content = panel };
                Window.Current.Activate();
            }
            catch
            {
                // Keep the original exception from causing a second recovery failure.
            }
        }

        private void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Persisted settings and background transfer services will be added with their features.
        }
    }
}