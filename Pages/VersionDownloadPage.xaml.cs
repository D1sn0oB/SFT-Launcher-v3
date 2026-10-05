using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SFTLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        private static readonly HttpClient client = new HttpClient();
        private bool isDownloading = false;
        private List<DownloadRecord> downloadHistory = new List<DownloadRecord>();
        private bool isFirstDownload = true;
        private List<string> availableVersions = new List<string>();

        public VersionDownloadPage()
        {
            InitializeComponent();
            try
            {
                LoadVersionsFromApi();
                InstallPathBox.Text = "";
                PathStatus.Text = "请先选择安装目录";
                PathStatus.Foreground = (Brush)FindResource("TextMutedBrush");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Init failed: " + ex.Message;
                StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            }
        }

        private async void LoadVersionsFromApi()
        {
            try
            {
                UpdateStatus("Loading version list...", "#A78BFA");
                
                var manifestResponse = await client.GetAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
                manifestResponse.EnsureSuccessStatusCode();
                string manifestJson = await manifestResponse.Content.ReadAsStringAsync();
                
                using var manifestDoc = JsonDocument.Parse(manifestJson);
                var manifest = manifestDoc.RootElement;
                
                var versions = manifest.GetProperty("versions");
                availableVersions.Clear();
                
                foreach (var version in versions.EnumerateArray())
                {
                    string type = version.GetProperty("type").GetString();
                    if (type == "release")
                    {
                        string id = version.GetProperty("id").GetString();
                        if (id.StartsWith("1.") || id == "26.3" || id == "26.2" || id == "26.1")
                        {
                            availableVersions.Add(id);
                        }
                    }
                }
                
                availableVersions.Sort((a, b) => b.CompareTo(a));
                if (availableVersions.Count > 50)
                {
                    availableVersions = availableVersions.GetRange(0, 50);
                }
                
                VersionComboBox.ItemsSource = availableVersions;
                if (VersionComboBox.Items.Count > 0)
                    VersionComboBox.SelectedIndex = 0;
                
                VersionStatus.Text = "\u2713";
                VersionStatus.Foreground = (Brush)FindResource("SuccessBrush");
                UpdateStatus("Ready", "#34D399");
            }
            catch (Exception ex)
            {
                VersionStatus.Text = "\u2717";
                VersionStatus.Foreground = (Brush)FindResource("ErrorBrush");
                UpdateStatus("Failed to load versions: " + ex.Message, "#F87171");
                
                var fallbackVersions = new[] {
                    "1.20.4", "1.20.3", "1.20.2", "1.20.1", "1.20",
                    "1.19.4", "1.19.3", "1.19.2", "1.19.1", "1.19",
                    "1.18.2", "1.18.1", "1.18",
                    "1.17.1", "1.16.5", "1.12.2"
                };
                VersionComboBox.ItemsSource = fallbackVersions;
                if (VersionComboBox.Items.Count > 0)
                    VersionComboBox.SelectedIndex = 0;
            }
        }

        private void CheckMinecraftDirectory()
        {
            try
            {
                string mcDir = InstallPathBox.Text;
                if (!string.IsNullOrEmpty(mcDir) && Directory.Exists(mcDir))
                {
                    PathStatus.Text = "Directory exists";
                    PathStatus.Foreground = (Brush)FindResource("SuccessBrush");
                }
                else if (!string.IsNullOrEmpty(mcDir))
                {
                    PathStatus.Text = "Will auto-create";
                    PathStatus.Foreground = (Brush)FindResource("WarningBrush");
                }
                else
                {
                    PathStatus.Text = "请先选择安装目录";
                    PathStatus.Foreground = (Brush)FindResource("TextMutedBrush");
                }
            }
            catch { }
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用 WPF 的 OpenDialog 选择文件夹
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Title = "选择 Minecraft 安装目录";
                dialog.Filter = "文件夹|*.*";
                
                if (dialog.ShowDialog() == true)
                {
                    string selectedPath = Path.GetDirectoryName(dialog.FileName);
                    if (string.IsNullOrEmpty(selectedPath))
                    {
                        selectedPath = dialog.FileName;
                    }
                    InstallPathBox.Text = selectedPath;
                    CheckMinecraftDirectory();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InstallPathBox.Text))
            {
                MessageBox.Show("请先选择安装目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (isDownloading) return;
            var downloadBtn = sender as Button;
            if (downloadBtn == null) return;

            isDownloading = true;
            downloadBtn.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            UpdateStatus("Initializing...", "#A78BFA");
            ProgressBar.Visibility = Visibility.Visible;
            ProgressText.Text = "0%";

            try
            {
                string version = VersionComboBox.Text ?? "1.20.4";
                string minecraftDir = InstallPathBox.Text;

                if (!Directory.Exists(minecraftDir))
                {
                    UpdateStatus("Creating Minecraft directories...", "#A78BFA");
                    EnsureDirectoryExists(minecraftDir);
                }
                
                string[] subDirs = { "versions", "libraries", "assets", "resourcepacks", "saves", "mods", "config" };
                foreach (string subDir in subDirs)
                {
                    string fullPath = Path.Combine(minecraftDir, subDir);
                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                }
                string versionsDir = Path.Combine(minecraftDir, "versions", version);
                if (!Directory.Exists(versionsDir))
                {
                    Directory.CreateDirectory(versionsDir);
                }

                if (isFirstDownload)
                {
                    SaveDefaultPath(minecraftDir);
                    isFirstDownload = false;
                }

                UpdateStatus("Fetching version info...", "#A78BFA");
                string manifestUrl = $"https://launchermeta.mojang.com/mc/game/{version}/version_manifest_v2.json";
                var manifestResponse = await client.GetAsync(manifestUrl);
                manifestResponse.EnsureSuccessStatusCode();
                string manifestJson = await manifestResponse.Content.ReadAsStringAsync();

                using var manifestDoc = JsonDocument.Parse(manifestJson);
                var manifest = manifestDoc.RootElement;

                string jarUrl = manifest.GetProperty("downloads")
                    .GetProperty("client")
                    .GetProperty("url")
                    .GetString();
                string jarPath = Path.Combine(minecraftDir, "versions", version, version + ".jar");

                UpdateStatus($"Downloading Minecraft {version}...", "#A78BFA");
                await DownloadFile(jarUrl, jarPath, UpdateProgress);

                if (manifest.TryGetProperty("assetIndex", out var assetIndex))
                {
                    string assetsIndex = assetIndex.GetProperty("url").GetString();
                    string assetsDir = Path.Combine(minecraftDir, "assets", "indexes");
                    if (!Directory.Exists(assetsDir))
                    {
                        Directory.CreateDirectory(assetsDir);
                    }
                    string assetsId = assetIndex.GetProperty("id").GetString();
                    string assetsIndexPath = Path.Combine(assetsDir, assetsId + ".json");

                    if (!File.Exists(assetsIndexPath))
                    {
                        UpdateStatus("Downloading asset index...", "#A78BFA");
                        var assetsResponse = await client.GetAsync(assetsIndex);
                        assetsResponse.EnsureSuccessStatusCode();
                        await File.WriteAllBytesAsync(assetsIndexPath, await assetsResponse.Content.ReadAsByteArrayAsync());
                    }
                }

                downloadHistory.Insert(0, new DownloadRecord { Version = version, Path = minecraftDir, Time = DateTime.Now, Success = true });
                UpdateDownloadHistory();
                UpdateStatus("\u2713 Download complete!", "#34D399");
                VersionStatus.Text = "\u2713";
                VersionStatus.Foreground = (Brush)FindResource("SuccessBrush");

                MessageBox.Show($"Minecraft {version} downloaded!\n\nLocation: {minecraftDir}",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("\u2717 Download failed: " + ex.Message, "#F87171");
                VersionStatus.Text = "\u2717";
                VersionStatus.Foreground = (Brush)FindResource("ErrorBrush");
                downloadHistory.Insert(0, new DownloadRecord
                {
                    Version = VersionComboBox.Text ?? "unknown",
                    Path = InstallPathBox.Text,
                    Time = DateTime.Now,
                    Success = false,
                    Error = ex.Message
                });
                UpdateDownloadHistory();
                MessageBox.Show("Download failed: " + ex.Message + "\n\nPlease check your network connection.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isDownloading = false;
                downloadBtn.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateProgress(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = progress;
                ProgressText.Text = progress.ToString("F0") + "%";
            });
        }

        private void UpdateStatus(string text, string colorKey)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = text;
                try
                {
                    var brush = (Brush)FindResource(colorKey + "Brush");
                    StatusText.Foreground = brush;
                }
                catch
                {
                    StatusText.Foreground = (Brush)FindResource("AccentBrush");
                }
            });
        }

        private async Task DownloadFile(string url, string savePath, Action<double> progressCallback)
        {
            string dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 200_000_000;
            var buffer = new byte[8192];
            long totalRead = 0;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    double progress = totalBytes > 0 ? (double)totalRead / totalBytes * 100 : 0;
                    progressCallback?.Invoke(progress);
                }
            }
        }

        private void EnsureDirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (Directory.Exists(path)) return;
            
            string parentDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                EnsureDirectoryExists(parentDir);
            }
            
            Directory.CreateDirectory(path);
        }

        private void SaveDefaultPath(string path)
        {
            try
            {
                string configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SFTLauncher", "config.json");
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                var config = new { defaultPath = path };
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void UpdateDownloadHistory()
        {
            Dispatcher.Invoke(() =>
            {
                DownloadHistoryPanel.Children.Clear();
                if (downloadHistory.Count > 0)
                {
                    foreach (var record in downloadHistory.Take(5))
                    {
                        var border = new Border
                        {
                            Background = (Brush)FindResource("BackgroundSecondaryBrush"),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16),
                            Margin = new Thickness(0, 0, 0, 8)
                        };
                        var grid = new Grid();
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var stackPanel = new StackPanel();
                        var titleBlock = new TextBlock
                        {
                            Text = record.Success ? "\u2713 Minecraft " + record.Version : "\u2717 Minecraft " + record.Version,
                            Foreground = record.Success ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("ErrorBrush"),
                            FontSize = 13,
                            FontWeight = FontWeights.Bold
                        };
                        var timeBlock = new TextBlock
                        {
                            Text = "Time: " + record.Time.ToString("yyyy-MM-dd HH:mm"),
                            Foreground = (Brush)FindResource("TextMutedBrush"),
                            FontSize = 11,
                            Margin = new Thickness(0, 4, 0, 0)
                        };
                        stackPanel.Children.Add(titleBlock);
                        stackPanel.Children.Add(timeBlock);
                        Grid.SetColumn(stackPanel, 0);

                        var pathBlock = new TextBlock
                        {
                            Text = Path.GetFileName(record.Path),
                            Foreground = (Brush)FindResource("TextMutedBrush"),
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(pathBlock, 1);

                        grid.Children.Add(stackPanel);
                        grid.Children.Add(pathBlock);
                        border.Child = grid;
                        DownloadHistoryPanel.Children.Add(border);
                    }
                }
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            isDownloading = false;
            DownloadButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            UpdateStatus("下载已取消", "#FBBF24");
        }

        private class DownloadRecord
        {
            public string Version { get; set; }
            public string Path { get; set; }
            public DateTime Time { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; } = "";
        }
    }
}
