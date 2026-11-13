using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
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
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XPower
{
    public sealed partial class MainPage : Page
    {
        private const int MaxRequestLineLength = 2048;
        private StreamSocketListener _httpProbeListener;
        private readonly ObservableCollection<ExplorerItem> _explorerItems = new ObservableCollection<ExplorerItem>();
        private readonly List<ExplorerItem> _allExplorerItems = new List<ExplorerItem>();
        private readonly Stack<StorageFolder> _folderHistory = new Stack<StorageFolder>();
        private StorageFolder _currentFolder;
        private string _listenerPort = String.Empty;
        private string _healthUrl = String.Empty;
        private StorageFile _currentTrack;
        private StorageFile _selectedExplorerFile;
        private IRandomAccessStream _currentTrackStream;
        private string _currentSection = "Home";
        private bool _backHandlerAttached;
        private bool _loadingSettings;
        private bool _explorerRootsLoaded;
        private bool _startingHealthService;

        public MainPage()
        {
            InitializeComponent();
            ExplorerListView.ItemsSource = _explorerItems;
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            HomeNavButton.Focus(FocusState.Programmatic);
            if (!_backHandlerAttached)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += SystemNavigationManager_BackRequested;
                _backHandlerAttached = true;
            }

            // Keep first-frame startup local and lightweight. Optional storage and network work
            // starts only when the corresponding section is opened.
            try
            {
                LoadSettings();
                ApplyTheme(ThemeToggle.IsOn);
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => ApplyWallpaper(WallpaperComboBox.SelectedIndex));
            }
            catch (Exception ex)
            {
                SettingsStatusText.Text = "Preferences could not be loaded; defaults are active. " + ex.Message;
                try { ApplyTheme(false); } catch { }
            }
        }

        private async void StartHealthService()
        {
            if (_httpProbeListener != null || _startingHealthService)
                return;
            _startingHealthService = true;
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
            finally
            {
                _startingHealthService = false;
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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            StorageFile activatedFile = e.Parameter as StorageFile;
            if (activatedFile != null && ExplorerSummaryText != null)
            {
                ExplorerPathText.Text = "OPENED FILE";
                ExplorerSummaryText.Text = activatedFile.Name + " · " + activatedFile.DisplayType + " · file activation received";
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
            if (section == "Files" && !_explorerRootsLoaded)
            {
                _explorerRootsLoaded = true;
                LoadExplorerRootsAsync();
            }
            if (section == "Phone Transfer")
            {
                StartHealthService();
                BuildHealthUrl();
            }
        }

        private void FocusSection(string section)
        {
            Control target = HomeNavButton;
            if (section == "Files") target = AppStorageButton;
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

        private async System.Threading.Tasks.Task LoadExplorerRootsAsync()
        {
            _folderHistory.Clear();
            _currentFolder = null;
            ExplorerUpButton.IsEnabled = false;
            ExplorerPathText.Text = "STORAGE LOCATIONS";
            _explorerItems.Clear();
            _allExplorerItems.Clear();
            _selectedExplorerFile = null;
            UpdateExplorerActions();
            if (ExplorerSearchBox != null) ExplorerSearchBox.Text = String.Empty;
            _explorerItems.Add(new ExplorerItem { Name = "X-Power App Storage", Description = "Private app folder", Glyph = "\uE8B7", Folder = ApplicationData.Current.LocalFolder, Trailing = "\uE76C" });
            try
            {
                var devices = await KnownFolders.RemovableDevices.GetFoldersAsync();
                foreach (StorageFolder device in devices)
                    _explorerItems.Add(new ExplorerItem { Name = device.Name, Description = "Removable USB device", Glyph = "\uE88E", Folder = device, Trailing = "\uE76C" });
                ExplorerSummaryText.Text = devices.Count + " USB device(s) detected · App storage available";
            }
            catch (Exception ex)
            {
                ExplorerSummaryText.Text = "USB storage unavailable: " + ex.Message;
            }
            _allExplorerItems.AddRange(_explorerItems);
        }

        private async System.Threading.Tasks.Task OpenExplorerFolderAsync(StorageFolder folder)
        {
            if (folder == null) return;
            _folderHistory.Push(_currentFolder);
            _currentFolder = folder;
            ExplorerPathText.Text = folder.Name;
            ExplorerUpButton.IsEnabled = true;
            await RefreshExplorerFolderAsync();
        }

        private async System.Threading.Tasks.Task RefreshExplorerFolderAsync()
        {
            if (_currentFolder == null) { await LoadExplorerRootsAsync(); return; }
            _explorerItems.Clear();
            _allExplorerItems.Clear();
            _selectedExplorerFile = null;
            UpdateExplorerActions();
            if (ExplorerSearchBox != null) ExplorerSearchBox.Text = String.Empty;
            try
            {
                var folders = await _currentFolder.GetFoldersAsync();
                var files = await _currentFolder.GetFilesAsync();
                int sort = SortComboBox.SelectedIndex;
                IEnumerable<StorageFolder> orderedFolders = sort == 1 ? folders.OrderByDescending(v => v.Name, StringComparer.OrdinalIgnoreCase) : folders.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);
                if (sort != 2)
                {
                    foreach (StorageFolder folder in orderedFolders)
                        _explorerItems.Add(new ExplorerItem { Name = folder.Name, Description = "Folder", Glyph = "\uE8B7", Folder = folder, Trailing = "\uE76C" });
                }
                IEnumerable<StorageFile> orderedFiles = sort == 1 ? files.OrderByDescending(v => v.Name, StringComparer.OrdinalIgnoreCase) : files.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);
                foreach (StorageFile file in orderedFiles)
                {
                    string name = file.Name;
                    if (!ShowExtensionsToggle.IsOn && !String.IsNullOrEmpty(file.FileType))
                        name = System.IO.Path.GetFileNameWithoutExtension(name);
                    string glyph = file.FileType == ".jpg" || file.FileType == ".jpeg" || file.FileType == ".png" || file.FileType == ".gif" ? "\uEB9F"
                        : file.FileType == ".mp3" || file.FileType == ".wav" || file.FileType == ".wma" || file.FileType == ".m4a" ? "\uE8D6"
                        : file.FileType == ".mp4" || file.FileType == ".m4v" ? "\uE714"
                        : file.FileType == ".pdf" ? "\uEA90"
                        : file.FileType == ".zip" ? "\uF012" : "\uE8A5";
                    _explorerItems.Add(new ExplorerItem { Name = name, Description = file.FileType + " · " + file.DisplayType, Glyph = glyph, File = file });
                }
                if (sort == 2)
                    foreach (StorageFolder folder in folders.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
                        _explorerItems.Insert(0, new ExplorerItem { Name = folder.Name, Description = "Folder", Glyph = "\uE8B7", Folder = folder, Trailing = "\uE76C" });
                _allExplorerItems.AddRange(_explorerItems);
                ApplyExplorerFilter();
                ExplorerSummaryText.Text = folders.Count + " folder(s) · " + files.Count + " file(s)";
            }
            catch (Exception ex)
            {
                ExplorerSummaryText.Text = "Cannot enumerate this location: " + ex.Message;
            }
        }

        private async void AppStorage_Click(object sender, RoutedEventArgs e)
        {
            _folderHistory.Clear();
            _currentFolder = null;
            await OpenExplorerFolderAsync(ApplicationData.Current.LocalFolder);
        }

        private async void UsbStorage_Click(object sender, RoutedEventArgs e)
        {
            await LoadExplorerRootsAsync();
            if (_explorerItems.Count > 0) _explorerItems.RemoveAt(0);
            if (_allExplorerItems.Count > 0) _allExplorerItems.RemoveAt(0);
            ExplorerPathText.Text = "USB DEVICES";
        }

        private async void ExplorerUp_Click(object sender, RoutedEventArgs e)
        {
            if (_folderHistory.Count == 0) { await LoadExplorerRootsAsync(); return; }
            StorageFolder previous = _folderHistory.Pop();
            _currentFolder = previous;
            if (previous == null) await LoadExplorerRootsAsync();
            else
            {
                ExplorerUpButton.IsEnabled = _folderHistory.Count > 0;
                await RefreshExplorerFolderAsync();
            }
        }

        private async void ExplorerRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFolder == null) await LoadExplorerRootsAsync();
            else await RefreshExplorerFolderAsync();
        }

        private async void ExplorerListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ExplorerItem item = e.ClickedItem as ExplorerItem;
            if (item == null) return;
            if (item.Folder != null)
            {
                _selectedExplorerFile = null;
                UpdateExplorerActions();
                await OpenExplorerFolderAsync(item.Folder);
                return;
            }
            if (item.File == null) return;
            _selectedExplorerFile = item.File;
            UpdateExplorerActions();
            try
            {
                var properties = await item.File.GetBasicPropertiesAsync();
                ExplorerSummaryText.Text = item.File.Name + " · " + item.File.DisplayType
                    + " · " + FormatFileSize(properties.Size)
                    + " · Modified " + properties.DateModified.ToLocalTime().ToString("g");
            }
            catch
            {
                ExplorerSummaryText.Text = "Selected: " + item.File.Name + " · " + item.File.DisplayType;
            }
        }

        private void ExplorerSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyExplorerFilter();
        }

        private void ApplyExplorerFilter()
        {
            string query = ExplorerSearchBox == null || ExplorerSearchBox.Text == null
                ? String.Empty : ExplorerSearchBox.Text.Trim();
            _explorerItems.Clear();
            foreach (ExplorerItem item in _allExplorerItems)
            {
                if (String.IsNullOrEmpty(query)
                    || item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _explorerItems.Add(item);
            }
            if (!String.IsNullOrEmpty(query))
                ExplorerSummaryText.Text = "Search results in the current folder · " + _explorerItems.Count + " match(es).";
        }

        private void UpdateExplorerActions()
        {
            bool hasFile = _selectedExplorerFile != null;
            CopyExplorerFileButton.IsEnabled = hasFile;
            string extension = hasFile ? _selectedExplorerFile.FileType.ToLowerInvariant() : String.Empty;
            PreviewExplorerFileButton.IsEnabled = hasFile
                && (extension == ".txt" || extension == ".log" || extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif");
            PlayExplorerFileButton.IsEnabled = hasFile
                && (extension == ".mp3" || extension == ".wav" || extension == ".wma" || extension == ".m4a");
        }

        private static string FormatFileSize(ulong bytes)
        {
            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return size.ToString("0.##") + " " + units[unit];
        }

        private async void PreviewExplorerFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExplorerFile == null) return;
            try
            {
                string extension = _selectedExplorerFile.FileType.ToLowerInvariant();
                var dialog = new ContentDialog
                {
                    Title = _selectedExplorerFile.Name,
                    PrimaryButtonText = "Close",
                    DefaultButton = ContentDialogButton.Primary
                };
                if (extension == ".txt" || extension == ".log")
                {
                    var properties = await _selectedExplorerFile.GetBasicPropertiesAsync();
                    if (properties.Size > 1024 * 1024)
                    {
                        ExplorerSummaryText.Text = "Preview is limited to text files up to 1 MB.";
                        return;
                    }
                    string content = await FileIO.ReadTextAsync(_selectedExplorerFile);
                    dialog.Content = new ScrollViewer
                    {
                        MaxHeight = 520,
                        Content = new TextBlock
                        {
                            Text = content,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 14,
                            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
                        }
                    };
                }
                else
                {
                    var bitmap = new BitmapImage();
                    using (var stream = await _selectedExplorerFile.OpenAsync(FileAccessMode.Read))
                        await bitmap.SetSourceAsync(stream);
                    dialog.Content = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        MaxWidth = 900,
                        MaxHeight = 520
                    };
                }
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ExplorerSummaryText.Text = "Preview unavailable: " + ex.Message;
            }
        }

        private async void CopyExplorerFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExplorerFile == null) return;
            try
            {
                StorageFile copied = await _selectedExplorerFile.CopyAsync(
                    ApplicationData.Current.LocalFolder,
                    _selectedExplorerFile.Name,
                    NameCollisionOption.GenerateUniqueName);
                ExplorerSummaryText.Text = "Copied into X-Power App Storage: " + copied.Name;
            }
            catch (Exception ex)
            {
                ExplorerSummaryText.Text = "Copy failed: " + ex.Message;
            }
        }

        private async void PlayExplorerFile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedExplorerFile == null) return;
            try
            {
                await LoadTrackAsync(_selectedExplorerFile);
                ShowSection("Music");
                AudioPlayer.Play();
                TrackStatusText.Text = "Playing from File Explorer.";
            }
            catch (Exception ex)
            {
                ExplorerSummaryText.Text = "Unable to play this file: " + ex.Message;
            }
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
                await LoadTrackAsync(file);
                AudioPlayer.Play();
            }
            catch (Exception ex)
            {
                TrackStatusText.Text = "Unable to load audio: " + ex.Message;
            }
        }

        private async System.Threading.Tasks.Task LoadTrackAsync(StorageFile file)
        {
            IRandomAccessStream newStream = null;
            try
            {
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
                TrackStatusText.Text = "Ready · " + file.FileType.ToUpperInvariant();
            }
            catch
            {
                if (newStream != null) newStream.Dispose();
                throw;
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
            SaveSettings();
        }

        private void AccentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || AccentComboBox == null) return;
            ApplyAccent(AccentComboBox.SelectedIndex);
            SaveSettings();
        }

        private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings) return;
            SaveSettings();
            if (_currentSection == "Files" && _currentFolder != null)
                await RefreshExplorerFolderAsync();
        }

        private void ShowExtensionsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loadingSettings) SaveSettings();
        }

        private void WallpaperComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || WallpaperComboBox == null) return;
            ApplyWallpaper(WallpaperComboBox.SelectedIndex);
            SaveSettings();
        }

        private void ApplyWallpaper(int index)
        {
            string[] files =
            {
                "aurora-core.jpg", "deep-current.jpg", "violet-pulse.jpg", "solar-ember.jpg",
                "arctic-grid.jpg", "emerald-drift.jpg", "crimson-orbit.jpg", "midnight-circuit.jpg"
            };
            int safeIndex = Math.Max(0, Math.Min(index, files.Length - 1));
            try
            {
                var image = new BitmapImage(new Uri("ms-appx:///Assets/Wallpapers/" + files[safeIndex], UriKind.Absolute));
                HomeHeroPanel.Background = new ImageBrush
                {
                    ImageSource = image,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.48
                };
            }
            catch
            {
                HomeHeroPanel.Background = (Brush)Application.Current.Resources["ThemeColor06Brush"];
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            ThemeToggle.IsOn = false;
            AccentComboBox.SelectedIndex = 0;
            SortComboBox.SelectedIndex = 0;
            ShowExtensionsToggle.IsOn = true;
            WallpaperComboBox.SelectedIndex = 0;
            ApplyTheme(false);
            ApplyAccent(0);
            ApplyWallpaper(0);
            SaveSettings();
            SettingsStatusText.Text = "Preferences reset to defaults.";
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            var values = ApplicationData.Current.LocalSettings.Values;
            object theme = values["ThemeLight"];
            object accent = values["AccentIndex"];
            object sort = values["SortIndex"];
            object extensions = values["ShowExtensions"];
            object wallpaper = values["WallpaperIndex"];
            ThemeToggle.IsOn = theme is bool && (bool)theme;
            AccentComboBox.SelectedIndex = accent is int ? (int)accent : 0;
            SortComboBox.SelectedIndex = sort is int ? (int)sort : 0;
            ShowExtensionsToggle.IsOn = !(extensions is bool) || (bool)extensions;
            WallpaperComboBox.SelectedIndex = wallpaper is int ? Math.Max(0, Math.Min(7, (int)wallpaper)) : 0;
            _loadingSettings = false;
            ApplyAccent(AccentComboBox.SelectedIndex);
        }

        private void SaveSettings()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values["ThemeLight"] = ThemeToggle.IsOn;
            values["AccentIndex"] = AccentComboBox.SelectedIndex;
            values["SortIndex"] = SortComboBox.SelectedIndex;
            values["ShowExtensions"] = ShowExtensionsToggle.IsOn;
            values["WallpaperIndex"] = WallpaperComboBox.SelectedIndex;
            SettingsStatusText.Text = "Preferences saved on this console.";
        }

        private void ApplyAccent(int index)
        {
            string[] colors = { "#107C10", "#52E0D0", "#A99AFF", "#65A8FF" };
            SetBrush("AccentBrush", colors[Math.Max(0, Math.Min(index, colors.Length - 1))]);
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
            ApplyAccent(AccentComboBox.SelectedIndex);

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

        private sealed class ExplorerItem
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Glyph { get; set; }
            public string Trailing { get; set; }
            public StorageFolder Folder { get; set; }
            public StorageFile File { get; set; }
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