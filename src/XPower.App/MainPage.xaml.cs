using System;
using System.Text;
using Windows.Networking.Sockets;
using Windows.System;
using Windows.UI.Core;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XPower
{
    public sealed partial class MainPage : Page
    {
        private const string HttpProbePort = "8765";
        private StreamSocketListener _httpProbeListener;

        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainPage_Loaded;
            // Xbox is a controller-first surface. Keep focus navigation visible and suppress the desktop-style cursor.
            Window.Current.CoreWindow.PointerCursor = null;
            HomeNavButton.Focus(FocusState.Programmatic);
            try
            {
                _httpProbeListener = new StreamSocketListener();
                _httpProbeListener.ConnectionReceived += HttpProbeListener_ConnectionReceived;
                await _httpProbeListener.BindServiceNameAsync(HttpProbePort);
                NetworkProbeStatus.Text = "HTTP LAN probe listening · port " + HttpProbePort;
            }
            catch (Exception ex)
            {
                NetworkProbeStatus.Text = "HTTP probe unavailable · " + ex.Message;
                if (_httpProbeListener != null)
                {
                    _httpProbeListener.Dispose();
                    _httpProbeListener = null;
                }
            }
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_httpProbeListener != null)
            {
                _httpProbeListener.ConnectionReceived -= HttpProbeListener_ConnectionReceived;
                _httpProbeListener.Dispose();
                _httpProbeListener = null;
            }
        }

        private void MainPage_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Keep B as a predictable back-to-home action. D-pad and A use the native XAML focus system.
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape || e.Key == VirtualKey.Back)
            {
                ShowSection("Home");
                HomeNavButton.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private async void HttpProbeListener_ConnectionReceived(
            StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                string requestLine = String.Empty;
                using (var reader = new DataReader(args.Socket.InputStream))
                {
                    reader.InputStreamOptions = InputStreamOptions.Partial;
                    uint loaded = await reader.LoadAsync(4096);
                    if (loaded > 0)
                    {
                        string request = reader.ReadString(loaded);
                        int lineEnd = request.IndexOf("\r\n", StringComparison.Ordinal);
                        requestLine = lineEnd >= 0 ? request.Substring(0, lineEnd) : request;
                    }
                }

                bool isHealthRequest = requestLine.StartsWith("GET /health ", StringComparison.Ordinal);
                string body = isHealthRequest
                    ? "{\"service\":\"X-Power\",\"transport\":\"HTTP\",\"status\":\"ok\",\"purpose\":\"LAN connectivity probe only\"}"
                    : "{\"error\":\"Not found\",\"available\":\"GET /health\"}";
                string status = isHealthRequest ? "200 OK" : "404 Not Found";
                string response = "HTTP/1.1 " + status + "\r\n"
                    + "Content-Type: application/json; charset=utf-8\r\n"
                    + "Content-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n"
                    + "Cache-Control: no-store\r\n"
                    + "Connection: close\r\n\r\n"
                    + body;

                using (var writer = new DataWriter(args.Socket.OutputStream))
                {
                    writer.WriteBytes(Encoding.UTF8.GetBytes(response));
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                args.Socket.Dispose();
            }
            catch
            {
                args.Socket.Dispose();
            }
        }

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            ShowSection(button.Tag as string ?? "Home");
        }

        private void ShowSection(string section)
        {
            HomeNavButton.Background = section == "Home" ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 54, 74)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            FilesNavButton.Background = section == "Files" ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 54, 74)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            PhoneNavButton.Background = section == "Phone Transfer" ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 54, 74)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            MusicNavButton.Background = section == "Music" ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 54, 74)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            SettingsNavButton.Background = section == "Settings" ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 54, 74)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);

            HomeNavButton.Foreground = section == "Home" ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"] : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
            FilesNavButton.Foreground = section == "Files" ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"] : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
            PhoneNavButton.Foreground = section == "Phone Transfer" ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"] : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            MusicNavButton.Foreground = section == "Music" ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"] : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            SettingsNavButton.Foreground = section == "Settings" ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"] : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];

            PageTitle.Text = section == "Home" ? "Console workspace" : section;
            switch (section)
            {
                case "Home":
                    SectionFeedback.Text = "Dashboard · Xbox controller navigation enabled";
                    break;
                case "Files":
                    SectionFeedback.Text = "File Explorer foundation · real storage browsing is the next implementation step";
                    break;
                case "Phone Transfer":
                    SectionFeedback.Text = "Phone Transfer · secure pairing and file transfer are not enabled yet";
                    break;
                case "Music":
                    SectionFeedback.Text = "Music · playback controls are not implemented yet";
                    break;
                case "Settings":
                    SectionFeedback.Text = "Settings · configuration controls are not implemented yet";
                    break;
                default:
                    SectionFeedback.Text = "Section unavailable";
                    break;
            }
        }
    }
}
