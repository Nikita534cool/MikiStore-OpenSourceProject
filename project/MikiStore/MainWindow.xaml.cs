using Microsoft.Toolkit.Uwp.Notifications;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Media.Protection.PlayReady;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Path = System.IO.Path;

namespace MikiStore
{
    
    public partial class MainWindow : Window
    {
        private DispatcherTimer _autoRefreshTimer;
        private DispatcherTimer _listRefreshTimer;
        private List<MikiApp> fullCatalog = new List<MikiApp>();
        private bool isDownloading = false;
        private bool isMikiStoreOffline = false;
        private bool isSearching = false;
        List<string> updateNotified = new List<string>();
        private static readonly HttpClient _client = new HttpClient();
        private readonly string versionString = "";
        public static readonly string serverUrl = "http://localhost:8000";
        public static readonly string appDataFolder = ".mikistoreopen";
        public MainWindow()
        {
            InitializeComponent();
            InitializeMikiEnvironment();

            _autoRefreshTimer = new DispatcherTimer();
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(30);
            _autoRefreshTimer.Tick += async (s, e) =>
            {
                await LoadAppsIntoUI();
            };

            _autoRefreshTimer.Start();

            _listRefreshTimer = new DispatcherTimer();
            _listRefreshTimer.Interval = TimeSpan.FromSeconds(60);
            _listRefreshTimer.Tick += async (s, e) =>
            {
                await refreshList();
            };

            _listRefreshTimer.Start();

            _ = LoadAppsIntoUI();
        }

        private async Task refreshList()
        {
            updateNotified.Clear();
        }

        public void InitializeMikiStore()
        {
            if (!IsGoogleAlive())
            {
                MessageBox.Show("You're offline, check your internet connection.",
                                "Miki Store | Connection Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                Application.Current.Shutdown();
            }
            else
            {
                return;
            }
        }

        private bool IsGoogleAlive()
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply reply = p.Send("8.8.8.8", 2000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }
        public void appRun()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string mikiPath = Path.Combine(appData, appDataFolder);

            if (!Directory.Exists(mikiPath))
            {
                Directory.CreateDirectory(mikiPath);
                File.SetAttributes(mikiPath, FileAttributes.Hidden);
            }

        }
        private void InitializeMikiEnvironment()
        {
            if (versionString != "")
            {
                VersionLabel.Content = "Ver: " + versionString;
            }
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string mikiPath = Path.Combine(appData, appDataFolder);

                if (!Directory.Exists(mikiPath))
                {
                    Directory.CreateDirectory(mikiPath);
                }
                File.SetAttributes(mikiPath, FileAttributes.Hidden);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Miki Forge failed to start: " + ex.Message);
            }
        }
        private async Task LoadAppsIntoUI()
        {
            if (isDownloading) return;
            if (isMikiStoreOffline) return;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string mikiDir = Path.Combine(appData, appDataFolder);
            if (!Directory.Exists(mikiDir)) Directory.CreateDirectory(mikiDir);

            string json = "";

            try
            {
                json = await _client.GetStringAsync(serverUrl + "/catalog.json");
            }
            catch (Exception)
            {
                if (IsGoogleAlive())
                {
                    isMikiStoreOffline = true;
                    MessageBox.Show("Miki Store server is offline. Be back soon.\nERROR CODE: 60",
                                    "Miki Store | Server Error",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show("You're offline, check your internet connection.\nERROR CODE: 61",
                                "Miki Store | Connection Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                }

                    Application.Current.Shutdown();
            }

            var apps = JsonSerializer.Deserialize<List<MikiApp>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var localVersions = LoadInstalledVersions();

            foreach (var app in apps)
            {
                app.isIconInstalling = true;
                if (!string.IsNullOrEmpty(app.IconUrl) && app.IconUrl.StartsWith("/"))
                    app.IconUrl = serverUrl + app.IconUrl;

                if (!string.IsNullOrEmpty(app.Url) && app.Url.StartsWith("/"))
                    app.Url = serverUrl + app.Url;

                if (app.Status?.ToLower() == "pending")
                {
                    app.ButtonText = "Pending";
                    app.ButtonColor = "#888888";
                    continue;
                }

                string installDir = Path.Combine(mikiDir, "apps", app.Id);
                bool physicallyExists = Directory.Exists(installDir) &&
                                        Directory.GetFiles(installDir, "*.exe", SearchOption.AllDirectories).Any();

                var localRecord = localVersions.FirstOrDefault(v => v.Id == app.Id);

                if (physicallyExists)
                {
                    if (localRecord != null && localRecord.Version != app.Version)
                    {
                        app.ButtonText = "Update";
                        app.ButtonColor = "#E67E22";
                        ShowUpdateNotification(app);
                    }
                    else
                    {
                        app.ButtonText = "Launch";
                        app.ButtonColor = "#28A745";
                    }
                }
                else
                {
                    app.ButtonText = "Install";
                    app.ButtonColor = "#007ACC";
                }
            }

            fullCatalog = apps.OrderBy(a => a.Name).ToList();

            AppList.ItemsSource = null;

            if (isSearching)
            {
                SearchBox_TextChanged(null, null);
            }
            else
            {
                AppList.ItemsSource = fullCatalog;
            }
        }
        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var app = (button.Tag as MikiApp) ?? (button.DataContext as MikiApp);

            if (app.Status != "available")
            {
                MessageBox.Show("This app is pending review. Check back in serveral minutes/hours.\nERROR CODE: 62",
                                "Miki Store | Install Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            if (app == null || app.IsDownloading) return;
            if (isDownloading)
            {
                MessageBox.Show("You can only download one app at once to save bandwidth.\nERROR CODE: 64",
                "Miki Store | Install Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
                return;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string baseDir = System.IO.Path.Combine(localAppData, appDataFolder);
            string tempDir = System.IO.Path.Combine(baseDir, "temp");
            string installDir = System.IO.Path.Combine(baseDir, "apps", app.Id);

            if (app.ButtonText == "Launch")
            {
                string[] files = System.IO.Directory.GetFiles(installDir, "*.exe", System.IO.SearchOption.AllDirectories);
                string gameExe = files.FirstOrDefault(f => !f.Contains("UnityCrashHandler"));

                if (gameExe != null && System.IO.File.Exists(gameExe))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(gameExe)
                    {
                        WorkingDirectory = System.IO.Path.GetDirectoryName(gameExe),
                        UseShellExecute = true
                    });
                }
                else
                {
                    System.Diagnostics.Process.Start("explorer.exe", installDir);
                }
                return;
            }

            try
            {
                app.IsDownloading = true;
                isDownloading = true;
                app.IsIntermediate = true;
                app.ButtonText = "Pending...";
                
                string finalDownloadUrl = app.Url;
                if (app.Url.StartsWith("/"))
                {

                    finalDownloadUrl = serverUrl + app.Url;
                }

                bool isZip = finalDownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string downloadPath = isZip
                    ? System.IO.Path.Combine(tempDir, $"{app.Id}.zip")
                    : System.IO.Path.Combine(installDir, $"{app.Id}.exe");

                System.IO.Directory.CreateDirectory(tempDir);
                System.IO.Directory.CreateDirectory(installDir);

                string encodedUrl = finalDownloadUrl.Replace(" ", "%20");

                var response = await _client.GetAsync(encodedUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new System.IO.FileStream(downloadPath, System.IO.FileMode.Create))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        app.IsIntermediate = false;
                        if (isZip)
                        {
                            app.ButtonText = "Downloading...";
                        }
                        else
                        {
                            app.ButtonText = "Installing...";
                        }
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes != -1) app.DownloadProgress = (double)totalRead / totalBytes * 100;
                    }
                }

                if (isZip)
                {
                    app.IsIntermediate = true;
                    app.ButtonText = "Installing...";
                    await Task.Run(() => {
                        if (System.IO.Directory.Exists(installDir))
                            System.IO.Directory.Delete(installDir, true);
                        System.IO.Directory.CreateDirectory(installDir);

                        System.IO.Compression.ZipFile.ExtractToDirectory(downloadPath, installDir);
                    });

                    System.IO.File.Delete(downloadPath);
                }

                try
                {
                    string[] files = System.IO.Directory.GetFiles(installDir, "*.exe", System.IO.SearchOption.AllDirectories);
                    string mainExe = files.FirstOrDefault(f => !f.Contains("UnityCrashHandler"));

                    if (mainExe != null)
                    {
                        string startMenuPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Apps from Miki Store");
                        if (!System.IO.Directory.Exists(startMenuPath)) System.IO.Directory.CreateDirectory(startMenuPath);

                        string shortcutPath = System.IO.Path.Combine(startMenuPath, app.Name + ".lnk");

                        string psCommand = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{shortcutPath}');" +
                                         $"$s.TargetPath='{mainExe}';" +
                                         $"$s.WorkingDirectory='{System.IO.Path.GetDirectoryName(mainExe)}';" +
                                         "$s.Save()";

                        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-Command \"{psCommand}\"")
                        {
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                }
                catch {}

                app.IsDownloading = false;
                app.ButtonText = "Launch";
                app.ButtonColor = "#28A745";
                isDownloading = false;
                SaveInstalledVersion(app.Id, app.Version);
                ShowNotificationWithLocalIcon(app);
            }
            catch (Exception ex)
            {
                app.IsDownloading = false;
                isDownloading = false;
                app.ButtonText = "Error";
                MessageBox.Show($"Miki Forge Error: {ex.Message}");
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var app = (button.Tag as MikiApp) ?? (button.DataContext as MikiApp);

            if (app == null) return;

            var result = MessageBox.Show($"Are you sure you want to remove {app.Name}?", "Miki Store", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string installDir = System.IO.Path.Combine(appData, appDataFolder, "apps", app.Id);

                    if (System.IO.Directory.Exists(installDir))
                        System.IO.Directory.Delete(installDir, true);

                    try
                    {
                        string startMenuFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Apps from Miki Store");
                        string shortcutPath = System.IO.Path.Combine(startMenuFolder, app.Name + ".lnk");

                        if (System.IO.File.Exists(shortcutPath))
                            System.IO.File.Delete(shortcutPath);

                        if (System.IO.Directory.Exists(startMenuFolder) &&
                            System.IO.Directory.GetFiles(startMenuFolder).Length == 0 &&
                            System.IO.Directory.GetDirectories(startMenuFolder).Length == 0)
                        {
                            System.IO.Directory.Delete(startMenuFolder);
                        }
                    }
                    catch {}

                    app.ButtonText = "Install";
                    app.ButtonColor = "#007ACC";

                    var versions = LoadInstalledVersions();
                    versions.RemoveAll(x => x.Id == app.Id);
                    System.IO.File.WriteAllText(GetVersionFilePath(), JsonConvert.SerializeObject(versions));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message);
                }
            }
        }

        private void AppCard_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var selectedApp = border.DataContext as MikiApp;

            if (selectedApp != null)
            {
                MainRoot.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 5 };

                DetailsPanel.DataContext = selectedApp;
                DetailsPanel.Visibility = Visibility.Visible;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            DetailsPanel.Visibility = Visibility.Collapsed;
            MainRoot.Effect = null;
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(query))
            {
                isSearching = false;
                AppList.ItemsSource = fullCatalog;
            }
            else
            {
                isSearching = true;
                var filtered = fullCatalog.Where(a =>
                    a.Name.ToLower().Contains(query) ||
                    a.Description.ToLower().Contains(query)
                ).ToList();

                AppList.ItemsSource = filtered;
            }
        }
        private string GetVersionFilePath() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appDataFolder, "apps", "installed_apps_versions.json");

        private void SaveInstalledVersion(string appId, string version)
        {
            var path = GetVersionFilePath();
            List<InstalledAppVersion> installed = LoadInstalledVersions();

            var existing = installed.FirstOrDefault(x => x.Id == appId);
            if (existing != null) existing.Version = version;
            else installed.Add(new InstalledAppVersion { Id = appId, Version = version });

            File.WriteAllText(path, JsonConvert.SerializeObject(installed));
        }

        private List<InstalledAppVersion> LoadInstalledVersions()
        {
            var path = GetVersionFilePath();
            if (!File.Exists(path)) return new List<InstalledAppVersion>();
            return JsonConvert.DeserializeObject<List<InstalledAppVersion>>(File.ReadAllText(path));
        }
        private async Task ShowNotificationWithLocalIcon(MikiApp app)
        {
            bool hasIcon = !string.IsNullOrEmpty(app.IconUrl);
            string localPath = "";

            if (hasIcon)
            {
                string tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appDataFolder, "temp");
                Directory.CreateDirectory(tempFolder);
                localPath = Path.Combine(tempFolder, $"{app.Id}_icon.png");

                try
                {
                    var bytes = await _client.GetByteArrayAsync(app.IconUrl);
                    await File.WriteAllBytesAsync(localPath, bytes);
                }
                catch { hasIcon = false; }
            }

            var toast = new ToastContentBuilder()
                .AddArgument("action", "launchApp")
                .AddArgument("appId", app.Id)
                .AddText($"{app.Name} is ready!")
                .AddText("Your app was successfully installed! Click this notification to launch your freshly installed app.");

            if (hasIcon && File.Exists(localPath))
            {
                toast.AddAppLogoOverride(new Uri(Path.GetFullPath(localPath)), ToastGenericAppLogoCrop.None);
            }

            toast.Show();

            if (hasIcon)
            {
                await Task.Delay(5000);
                if (File.Exists(localPath)) File.Delete(localPath);
            }
        }
        private async Task ShowUpdateNotification(MikiApp app)
        {
            if (updateNotified.Contains(app.Id)) return;
            bool hasIcon = !string.IsNullOrEmpty(app.IconUrl);
            string localPath = "";

            if (hasIcon)
            {
                string tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appDataFolder, "temp");
                Directory.CreateDirectory(tempFolder);
                localPath = Path.Combine(tempFolder, $"{app.Id}_icon.png");

                try
                {
                    var bytes = await _client.GetByteArrayAsync(app.IconUrl);
                    await File.WriteAllBytesAsync(localPath, bytes);
                }
                catch { hasIcon = false; }
            }

            var toast = new ToastContentBuilder()
                .AddText($"An update is avalaible for {app.Name}")
                .AddText($"Launch Miki Store and update {app.Name}.");

            if (hasIcon && File.Exists(localPath))
            {
                toast.AddAppLogoOverride(new Uri(Path.GetFullPath(localPath)), ToastGenericAppLogoCrop.None);
            }

            toast.Show();

            if (hasIcon)
            {
                await Task.Delay(5000);
                if (File.Exists(localPath)) File.Delete(localPath);
            }
            updateNotified.Add(app.Id);
        }
        private async void Icon_Loaded_Trigger(object sender, RoutedEventArgs e)
        {
            var img = sender as Image;
            var app = img?.DataContext as MikiApp;
            if (app == null) return;

            if (img.Source != null && img.Source is BitmapSource bs && !bs.IsDownloading)
            {
                app.isIconInstalling = false;
                return;
            }

            int attempts = 0;
            while (attempts < 100)
            {
                if (img.Source is BitmapSource bitmap && !bitmap.IsDownloading)
                {
                    app.isIconInstalling = false;
                    break;
                }

                await Task.Delay(100);
                attempts++;
            }
        }
    }

    public class MikiApp : INotifyPropertyChanged
    {
        public string Id { get; set; }       
        public string Name { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Url { get; set; }       
        public string IconUrl { get; set; }
        public string Status { get; set; }
        public string UploadedBy { get; set; }

        private string _buttonText = "Install";
        public string ButtonText
        {
            get => _buttonText;
            set { _buttonText = value; OnPropertyChanged(nameof(ButtonText)); }
        }

        private string _buttonColor = "#007ACC";
        public string ButtonColor
        {
            get => _buttonColor;
            set { _buttonColor = value; OnPropertyChanged(nameof(ButtonColor)); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(nameof(IsDownloading)); }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(nameof(DownloadProgress)); }
        }

        private bool _isIconInstalling = true;
        public bool isIconInstalling
        {
            get => _isIconInstalling;
            set { _isIconInstalling = value; OnPropertyChanged(nameof(isIconInstalling)); }
        }

        private bool _isIntermediate = false;
        public bool IsIntermediate
        {
            get => _isIntermediate;
            set { _isIntermediate = value; OnPropertyChanged(nameof(IsIntermediate)); }
        }

        private BitmapSource _downloadedIcon;
        public BitmapSource DownloadedIcon
        {
            get => _downloadedIcon;
            set { _downloadedIcon = value; OnPropertyChanged(nameof(DownloadedIcon)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public class InstalledAppVersion
    {
        public string Id { get; set; }
        public string Version { get; set; }
    }
    public class LengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string input = value as string;
            if (string.IsNullOrEmpty(input)) return "";

            string flatInput = input.Replace("\r", "").Replace("\n", " ");

            while (flatInput.Contains("  "))
            {
                flatInput = flatInput.Replace("  ", " ");
            }

            flatInput = flatInput.Trim();

            if (flatInput.Length <= 100)
            {
                return flatInput;
            }
            else
            {
                return flatInput.Substring(0, 97) + "...";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
