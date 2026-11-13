using System;
using System.Text;
using Windows.Networking.Sockets;
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
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainPage_Loaded;
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

            string section = button.Tag as string ?? "Home";
            PageTitle.Text = section == "Home" ? "Welcome to X-Power" : section;
            if (section == "Files")
            {
                // The real StorageFolder/file-picker workflow is intentionally deferred
                // until the foundation build is verified on Xbox hardware.
                PageTitle.Text = "Files — foundation view";
            }
        }
    }
}
