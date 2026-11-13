using System;
using System.Collections.ObjectModel;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XPower
{
    public sealed partial class MainPage : Page
    {
        private const string HttpProbePort = "8765";
        private StreamSocketListener _httpProbeListener;
        private readonly ObservableCollection<string> _selectedFiles = new ObservableCollection<string>();
        private string _healthUrl = String.Empty;
        private StorageFile _currentTrack;

        public MainPage()
        {
            InitializeComponent();
            Window.Current.CoreWindow.PointerCursor = null;
            FileListView.ItemsSource = _selectedFiles;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainPage_Loaded;
            Window.Current.CoreWindow.PointerCursor = null;
            HomeNavButton.Focus(FocusState.Programmatic);

            try
            {
                _httpProbeListener = new StreamSocketListener();
                _httpProbeListener.ConnectionReceived += HttpProbeListener_ConnectionReceived;
                await _httpProbeListener.BindServiceNameAsync(HttpProbePort);
                NetworkProbeStatus.Text = "Listening on local TCP port " + HttpProbePort + " · health endpoint only";
                RefreshNetworkAddress();
            }
            catch (Exception ex)
            {
                NetworkProbeStatus.Text = "Local service unavailable: " + ex.Message;
                PhoneStatusText.Text = "The local health service could not start. Check network permissions and restart the app.";
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
            AudioPlayer.Stop();
        }

        private void MainPage_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape || e.Key == VirtualKey.Back)
            {
                ShowSection("Home");
                HomeNavButton.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private async void HttpProbeListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
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
                    ? "{\"service\":\"X-Power\",\"transport\":\"HTTP\",\"status\":\"ok\",\"purpose\":\"LAN health probe only\"}"
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
            if (button != null)
                ShowSection(button.Tag as string ?? "Home");
        }

        private void ShowSection(string section)
        {
            HomeView.Visibility = section == "Home" ? Visibility.Visible : Visibility.Collapsed;
            FilesView.Visibility = section == "Files" ? Visibility.Visible : Visibility.Collapsed;
            PhoneView.Visibility = section == "Phone Transfer" ? Visibility.Visible : Visibility.Collapsed;
            MusicView.Visibility = section == "Music" ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;

            SetNavigationState(HomeNavButton, section == "Home");
            SetNavigationState(FilesNavButton, section == "Files");
            SetNavigationState(PhoneNavButton, section == "Phone Transfer");
            SetNavigationState(MusicNavButton, section == "Music");
            SetNavigationState(SettingsNavButton, section == "Settings");

            PageTitle.Text = section == "Home" ? "Command center" : section == "Phone Transfer" ? "Phone link" : section == "Music" ? "Media player" : section == "Settings" ? "Preferences" : "File Explorer";
            BreadcrumbText.Text = "X-POWER   /   " + (section == "Phone Transfer" ? "PHONE LINK" : section == "Music" ? "MEDIA PLAYER" : section.ToUpperInvariant());
            if (section == "Phone Transfer")
                RefreshNetworkAddress();
        }

        private void SetNavigationState(Button button, bool selected)
        {
            button.Background = selected
                ? new SolidColorBrush(Color.FromArgb(255, 23, 59, 67))
                : new SolidColorBrush(Colors.Transparent);
            button.Foreground = selected
                ? (Brush)Application.Current.Resources["AccentBrush"]
                : (Brush)Application.Current.Resources["SecondaryTextBrush"];
        }

        private async void BrowseFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Downloads
                };
                picker.FileTypeFilter.Add("*");
                var files = await picker.PickMultipleFilesAsync();
                if (files == null || files.Count == 0)
                {
                    FileSummaryText.Text = "No files selected. Your existing list is unchanged.";
                    return;
                }

                foreach (StorageFile file in files)
                {
                    if (!_selectedFiles.Contains(file.Name))
                        _selectedFiles.Add(file.Name);
                }
                FileSummaryText.Text = _selectedFiles.Count + " file(s) selected in this session.";
            }
            catch (Exception ex)
            {
                FileSummaryText.Text = "File picker could not open: " + ex.Message;
            }
        }

        private void ClearFiles_Click(object sender, RoutedEventArgs e)
        {
            _selectedFiles.Clear();
            FileSummaryText.Text = "Selection list cleared. No files were changed on storage.";
        }

        private void RefreshNetwork_Click(object sender, RoutedEventArgs e)
        {
            RefreshNetworkAddress();
        }

        private void RefreshNetworkAddress()
        {
            string address = String.Empty;
            try
            {
                var hostNames = NetworkInformation.GetHostNames();
                foreach (HostName host in hostNames)
                {
                    if (host.Type == HostNameType.Ipv4 && host.IPInformation != null && host.IPInformation.NetworkAdapter != null)
                    {
                        address = host.CanonicalName;
                        break;
                    }
                }
            }
            catch
            {
                address = String.Empty;
            }

            if (String.IsNullOrEmpty(address))
            {
                PhoneAddressText.Text = "No IPv4 LAN address found. Confirm the Xbox is connected to your network.";
                _healthUrl = String.Empty;
                PhoneStatusText.Text = "Network address unavailable. The health listener may still be active.";
                return;
            }

            _healthUrl = "http://" + address + ":" + HttpProbePort + "/health";
            PhoneAddressText.Text = _healthUrl;
            PhoneStatusText.Text = "Health-check only. Open this address from a device on the same LAN to test reachability. File transfer and secure pairing are not active.";
        }

        private void CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(_healthUrl))
            {
                PhoneStatusText.Text = "Refresh the LAN address first.";
                return;
            }

            var package = new DataPackage();
            package.SetText(_healthUrl);
            Clipboard.SetContent(package);
            PhoneStatusText.Text = "Health URL copied. This does not pair a phone or enable file transfer.";
        }

        private async void SelectTrack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Music
                };
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".wma");
                picker.FileTypeFilter.Add(".m4a");
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    TrackStatusText.Text = "No track selected.";
                    return;
                }

                _currentTrack = file;
                var stream = await file.OpenAsync(FileAccessMode.Read);
                AudioPlayer.SetSource(stream, file.ContentType);
                TrackTitleText.Text = file.DisplayName;
                TrackStatusText.Text = "Ready · " + file.FileType.ToUpperInvariant();
                AudioPlayer.Play();
                TrackStatusText.Text = "Playing · " + file.FileType.ToUpperInvariant();
            }
            catch (Exception ex)
            {
                TrackStatusText.Text = "Unable to load audio: " + ex.Message;
            }
        }

        private void PlayTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null)
            {
                TrackStatusText.Text = "Choose a track first.";
                return;
            }
            AudioPlayer.Play();
            TrackStatusText.Text = "Playback started.";
        }

        private void PauseTrack_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Pause();
            TrackStatusText.Text = "Playback paused.";
        }

        private void StopTrack_Click(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Stop();
            TrackStatusText.Text = "Playback stopped.";
        }

        private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            bool light = ThemeToggle.IsOn;
            SetBrush("AppBackgroundBrush", light ? "#EEF3F8" : "#080D18");
            SetBrush("SidebarBrush", light ? "#DDE6F0" : "#0D1524");
            SetBrush("PanelBrush", light ? "#FFFFFF" : "#111D2E");
            SetBrush("PanelAltBrush", light ? "#E4ECF5" : "#17263A");
            SetBrush("BorderBrush", light ? "#C5D2E1" : "#263A52");
            SetBrush("PrimaryTextBrush", light ? "#152238" : "#F1F6FF");
            SetBrush("SecondaryTextBrush", light ? "#52657D" : "#93A6BF");
            SetBrush("AccentBrush", light ? "#087E83" : "#52E0D0");
            SetBrush("AccentAltBrush", light ? "#286CC1" : "#65A8FF");
        }

        private void SetBrush(string key, string hex)
        {
            var brush = Application.Current.Resources[key] as SolidColorBrush;
            if (brush == null) return;
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            brush.Color = Color.FromArgb(255, r, g, b);
        }
    }
}