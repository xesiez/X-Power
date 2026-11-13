using System;
using System.Collections.ObjectModel;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
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
        private const int MaxRequestLineLength = 2048;
        private StreamSocketListener _httpProbeListener;
        private readonly ObservableCollection<StorageFile> _selectedFiles = new ObservableCollection<StorageFile>();
        private string _listenerPort = String.Empty;
        private string _healthUrl = String.Empty;
        private StorageFile _currentTrack;
        private IRandomAccessStream _currentTrackStream;
        private string _currentSection = "Home";
        private bool _backHandlerAttached;

        public MainPage()
        {
            InitializeComponent();
            FileListView.ItemsSource = _selectedFiles;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
            ApplyTheme(false);
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            HomeNavButton.Focus(FocusState.Programmatic);
            if (!_backHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += SystemNavigationManager_BackRequested;
                _backHandlerAttached = true;
            }

            if (_httpProbeListener != null)
                return;

            try
            {
                _httpProbeListener = new StreamSocketListener();
                _httpProbeListener.ConnectionReceived += HttpProbeListener_ConnectionReceived;
                await _httpProbeListener.BindEndpointAsync(null, String.Empty);
                _listenerPort = _httpProbeListener.Information.LocalPort;

                NetworkProbeStatus.Text = "Listening on dynamic TCP port " + _listenerPort + " · health endpoint only";
                PhoneServiceDot.Fill = (Brush)Application.Current.Resources["SuccessBrush"];
                PhoneServiceLabel.Text = "HEALTH SERVICE LISTENING";
                PhoneStatusText.Text = "Health service is listening. Enter the Xbox IPv4 address shown in Network Settings to build its URL.";
                BuildHealthUrl();
            }
            catch (Exception ex)
            {
                NetworkProbeStatus.Text = "Local health service unavailable: " + ex.Message;
                PhoneServiceDot.Fill = (Brush)Application.Current.Resources["DangerBrush"];
                PhoneServiceLabel.Text = "HEALTH SERVICE UNAVAILABLE";
                PhoneStatusText.Text = "The local health service could not start. No phone connection is available in this session.";
                if (_httpProbeListener != null)
                {
                    _httpProbeListener.ConnectionReceived -= HttpProbeListener_ConnectionReceived;
                    _httpProbeListener.Dispose();
                    _httpProbeListener = null;
                }
                _listenerPort = String.Empty;
                _healthUrl = String.Empty;
                CopyAddressButton.IsEnabled = false;
            }
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_backHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested -= SystemNavigationManager_BackRequested;
                _backHandlerAttached = false;
            }

            if (_httpProbeListener != null)
            {
                _httpProbeListener.ConnectionReceived -= HttpProbeListener_ConnectionReceived;
                _httpProbeListener.Dispose();
                _httpProbeListener = null;
            }
            _listenerPort = String.Empty;
            _healthUrl = String.Empty;
            AudioPlayer.Stop();
            if (_currentTrackStream != null)
            {
                _currentTrackStream.Dispose();
                _currentTrackStream = null;
            }
        }

        private void MainPage_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // D-pad and left-stick directions map to arrow keys for native XY focus navigation.
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape || e.Key == VirtualKey.Back)
                e.Handled = NavigateBack();
        }

        private void SystemNavigationManager_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NavigateBack())
                e.Handled = true;
        }

        private bool NavigateBack()
        {
            if (_currentSection == "Home")
                return false;
            ShowSection("Home");
            return true;
        }

        private async void HttpProbeListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            StreamSocket socket = args.Socket;
            try
            {
                string requestLine = null;
                using (var reader = new DataReader(socket.InputStream))
                {
                    reader.InputStreamOptions = InputStreamOptions.Partial;
                    var requestBuilder = new StringBuilder();
                    while (requestBuilder.Length < MaxRequestLineLength)
                    {
                        uint loaded = await reader.LoadAsync(256);
                        if (loaded == 0)
                            break;
                        requestBuilder.Append(reader.ReadString(loaded));
                        string requestText = requestBuilder.ToString();
                        int lineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
                        if (lineEnd >= 0)
                        {
                            requestLine = requestText.Substring(0, lineEnd);
                            break;
                        }
                    }
                    reader.DetachStream();
                }

                string[] requestParts = String.IsNullOrEmpty(requestLine)
                    ? new string[0]
                    : requestLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool validHttpLine = requestParts.Length == 3
                    && (requestParts[2] == "HTTP/1.0" || requestParts[2] == "HTTP/1.1");
                bool isHealthRequest = validHttpLine && requestParts[0] == "GET" && requestParts[1] == "/health";

                string status;
                string body;
                if (!validHttpLine)
                {
                    status = "400 Bad Request";
                    body = "{\"error\":\"Bad request\"}";
                }
                else if (requestParts[0] != "GET")
                {
                    status = "405 Method Not Allowed";
                    body = "{\"error\":\"Method not allowed\",\"allowed\":\"GET\"}";
                }
                else if (!isHealthRequest)
                {
                    status = "404 Not Found";
                    body = "{\"error\":\"Not found\",\"available\":\"GET /health\"}";
                }
                else
                {
                    status = "200 OK";
                    body = "{\"service\":\"X-Power\",\"transport\":\"HTTP\",\"status\":\"ok\",\"purpose\":\"LAN health probe only\"}";
                }

                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                string headers = "HTTP/1.1 " + status + "\r\n"
                    + "Content-Type: application/json; charset=utf-8\r\n"
                    + "Content-Length: " + bodyBytes.Length + "\r\n"
                    + "Cache-Control: no-store\r\n"
                    + "X-Content-Type-Options: nosniff\r\n"
                    + "Connection: close\r\n\r\n";
                using (var writer = new DataWriter(socket.OutputStream))
                {
                    writer.WriteBytes(Encoding.UTF8.GetBytes(headers));
                    writer.WriteBytes(bodyBytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
            }
            catch
            {
                // Malformed or interrupted LAN probes must not terminate the app.
            }
            finally
            {
                socket.Dispose();
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
            if (section != "Home" && section != "Files" && section != "Phone Transfer" && section != "Music" && section != "Settings")
                section = "Home";
            _currentSection = section;

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

            PageTitle.Text = section == "Home" ? "Command center"
                : section == "Phone Transfer" ? "Phone link"
                : section == "Music" ? "Media player"
                : section == "Settings" ? "Preferences" : "File Explorer";
            BreadcrumbText.Text = "X-POWER   /   " + (section == "Phone Transfer" ? "PHONE LINK"
                : section == "Music" ? "MEDIA PLAYER" : section.ToUpperInvariant());

            FocusSection(section);
            if (section == "Phone Transfer")
                BuildHealthUrl();
        }

        private void FocusSection(string section)
        {
            Control target = HomeNavButton;
            if (section == "Files") target = BrowseFilesButton;
            else if (section == "Phone Transfer") target = XboxIpTextBox;
            else if (section == "Music") target = SelectTrackButton;
            else if (section == "Settings") target = ThemeToggle;
            if (target != null && target.Visibility == Visibility.Visible && target.IsEnabled)
                target.Focus(FocusState.Programmatic);
        }

        private void SetNavigationState(Button button, bool selected)
        {
            button.Background = selected
                ? (Brush)Application.Current.Resources["ThemeColor03Brush"]
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

                int added = 0;
                foreach (StorageFile file in files)
                {
                    bool alreadySelected = false;
                    foreach (StorageFile selected in _selectedFiles)
                    {
                        if (selected.IsEqual(file))
                        {
                            alreadySelected = true;
                            break;
                        }
                    }
                    if (!alreadySelected)
                    {
                        _selectedFiles.Add(file);
                        added++;
                    }
                }
                FileSummaryText.Text = _selectedFiles.Count + " file(s) selected in this session · " + added + " new";
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
            BuildHealthUrl();
        }

        private void XboxIpTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _healthUrl = String.Empty;
            CopyAddressButton.IsEnabled = false;
            PhoneAddressText.Text = "Build the health URL after entering the Xbox IPv4 address.";
        }

        private void BuildHealthUrl()
        {
            if (String.IsNullOrEmpty(_listenerPort))
            {
                _healthUrl = String.Empty;
                CopyAddressButton.IsEnabled = false;
                PhoneAddressText.Text = "The local health service is not listening.";
                return;
            }

            string address = XboxIpTextBox.Text == null ? String.Empty : XboxIpTextBox.Text.Trim();
            if (String.IsNullOrEmpty(address))
            {
                _healthUrl = String.Empty;
                CopyAddressButton.IsEnabled = false;
                PhoneAddressText.Text = "Enter the Xbox IPv4 address from Settings > Network > Advanced settings.";
                PhoneStatusText.Text = "The Xbox UWP app cannot retrieve its local IPv4 address automatically; enter the address shown in console Network Settings.";
                return;
            }

            try
            {
                var host = new HostName(address);
                if (host.Type != HostNameType.Ipv4)
                    throw new ArgumentException("Enter an IPv4 address, for example 192.168.1.25.");
                _healthUrl = "http://" + host.CanonicalName + ":" + _listenerPort + "/health";
                PhoneAddressText.Text = _healthUrl;
                CopyAddressButton.IsEnabled = true;
                PhoneStatusText.Text = "HTTP health check only. Open this address on a phone connected to the same LAN. No pairing or file transfer is active.";
            }
            catch (Exception ex)
            {
                _healthUrl = String.Empty;
                CopyAddressButton.IsEnabled = false;
                PhoneAddressText.Text = "Invalid IPv4 address.";
                PhoneStatusText.Text = ex.Message;
            }
        }

        private void CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrEmpty(_healthUrl))
            {
                PhoneStatusText.Text = "Enter a valid Xbox IPv4 address and build the URL first.";
                return;
            }
            var package = new DataPackage();
            package.SetText(_healthUrl);
            Clipboard.SetContent(package);
            PhoneStatusText.Text = "Health URL copied. This does not pair a phone or enable file transfer.";
        }

        private async void SelectTrack_Click(object sender, RoutedEventArgs e)
        {
            IRandomAccessStream newStream = null;
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Downloads
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

                AudioPlayer.Stop();
                if (_currentTrackStream != null)
                {
                    _currentTrackStream.Dispose();
                    _currentTrackStream = null;
                }
                newStream = await file.OpenAsync(FileAccessMode.Read);
                AudioPlayer.SetSource(newStream, file.ContentType);
                _currentTrackStream = newStream;
                _currentTrack = file;
                newStream = null;
                TrackTitleText.Text = file.DisplayName;
                TrackStatusText.Text = "Playing · " + file.FileType.ToUpperInvariant();
                AudioPlayer.Play();
            }
            catch (Exception ex)
            {
                if (newStream != null)
                    newStream.Dispose();
                TrackStatusText.Text = "Unable to load audio: " + ex.Message;
            }
        }

        private void PlayTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null || _currentTrackStream == null)
            {
                TrackStatusText.Text = "Choose a track first.";
                return;
            }
            AudioPlayer.Play();
            TrackStatusText.Text = "Playback started.";
        }

        private void PauseTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null)
            {
                TrackStatusText.Text = "Choose a track first.";
                return;
            }
            AudioPlayer.Pause();
            TrackStatusText.Text = "Playback paused.";
        }

        private void StopTrack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null)
            {
                TrackStatusText.Text = "Choose a track first.";
                return;
            }
            AudioPlayer.Stop();
            TrackStatusText.Text = "Playback stopped.";
        }

        private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeToggle.IsOn);
        }

        private void ApplyTheme(bool light)
        {
            SetBrush("AppBackgroundBrush", light ? "#EEF3F8" : "#080D18");
            SetBrush("SidebarBrush", light ? "#DDE6F0" : "#0D1524");
            SetBrush("PanelBrush", light ? "#FFFFFF" : "#111D2E");
            SetBrush("PanelAltBrush", light ? "#E4ECF5" : "#17263A");
            SetBrush("BorderBrush", light ? "#C5D2E1" : "#263A52");
            SetBrush("PrimaryTextBrush", light ? "#152238" : "#F1F6FF");
            SetBrush("SecondaryTextBrush", light ? "#52657D" : "#93A6BF");
            SetBrush("AccentBrush", light ? "#087E83" : "#52E0D0");
            SetBrush("AccentAltBrush", light ? "#286CC1" : "#65A8FF");
            SetBrush("SuccessBrush", light ? "#18794E" : "#70E0A5");
            SetBrush("WarningBrush", light ? "#8A5A00" : "#F2C879");
            SetBrush("DangerBrush", light ? "#B42332" : "#FF7E8A");

            string[] dark = new string[] {
                "#153C45","#2C7779","#173B43","#15372F","#2A5C4B","#122A38","#286071","#07141D",
                "#214658","#34717D","#3B8D91","#1A4A54","#173D47","#282B50","#A99AFF","#173C34",
                "#132B2A","#2B5C50","#0B1422","#554C8B","#171C31","#3D4267","#B8ACFF","#102A2B",
                "#28615B","#1A4A48","#3B8F83","#1D5148"
            };
            string[] lightColors = new string[] {
                "#D5E8EA","#8CB8BA","#D7EAE8","#DDEFE5","#A9CDBB","#DDEBF2","#A9C8D4","#FFFFFF",
                "#D5E6EF","#A5C8CE","#80BFC0","#D6EAEC","#D7E8EC","#E8E5F7","#6955B8","#D8EEE4",
                "#E2F1ED","#A6CDBF","#F4F7FB","#6552A8","#EBECF5","#C4C9DF","#6955B8","#E1F1EF",
                "#A4CEC8","#CDE9E3","#83BFB3","#1D5148"
            };
            for (int i = 0; i < dark.Length; i++)
                SetBrush("ThemeColor" + (i + 1).ToString("00") + "Brush", light ? lightColors[i] : dark[i]);
        }

        private void SetBrush(string key, string hex)
        {
            var brush = Application.Current.Resources[key] as SolidColorBrush;
            if (brush == null || String.IsNullOrEmpty(hex) || hex.Length != 7)
                return;
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            brush.Color = Color.FromArgb(255, r, g, b);
        }
    }
}