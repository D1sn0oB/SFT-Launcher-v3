using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SFTLauncher.Pages
{
    public partial class VersionDownloadPage : Page
    {
        private readonly Utils.MinecraftVersionInstaller installer = new();
        private bool isDownloading = false;
        private CancellationTokenSource? downloadCts = null;
        private List<DownloadRecord> downloadHistory = new List<DownloadRecord>();
        private bool isFirstDownload = true;
        private List<Utils.MinecraftVersionEntry> availableVersions = new List<Utils.MinecraftVersionEntry>();

        public VersionDownloadPage()
        {
            InitializeComponent();
            LoadVersionsFromApi();
            InstallPathBox.Text = Utils.MinecraftLauncher.DetectMinecraftDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
            CheckMinecraftDirectory();
        }

        private async void LoadVersionsFromApi()
        {
            try
            {
                UpdateStatus("Loading version list...", "#A78BFA");

                availableVersions = await Utils.MinecraftVersionInstaller.GetVersionsAsync();
                VersionComboBox.ItemsSource = availableVersions;

                // 默认选中最新正式版
                var latestRelease = availableVersions.FirstOrDefault(v => v.IsLatestRelease);
                if (latestRelease != null)
                    VersionComboBox.SelectedItem = latestRelease;
                else if (VersionComboBox.Items.Count > 0)
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

                // 网络失败时回退到内置常用版本列表（仅展示，下载需重新联网加载）
                var fallbackVersions = new[] {
                    "1.20.4", "1.20.3", "1.20.2", "1.20.1", "1.20",
                    "1.19.4", "1.19.3", "1.19.2", "1.19.1", "1.19",
                    "1.18.2", "1.18.1", "1.18",
                    "1.17.1", "1.16.5", "1.12.2"
                }.Select(id => new Utils.MinecraftVersionEntry
                {
                    Id = id,
                    Type = "release",
                    Url = ""
                }).ToList();
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
            if (isDownloading) return;

            if (!(VersionComboBox.SelectedItem is Utils.MinecraftVersionEntry version))
            {
                MessageBox.Show("请先选择要下载的版本！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(version.Url))
            {
                MessageBox.Show("版本列表未加载完整，无法获取该版本的下载地址。\n请检查网络后重新打开本页面。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(InstallPathBox.Text))
            {
                MessageBox.Show("请先选择安装目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var downloadBtn = sender as Button;
            if (downloadBtn == null) return;

            isDownloading = true;
            downloadBtn.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            UpdateStatus("Initializing...", "#A78BFA");
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressText.Text = "0%";

            string minecraftDir = InstallPathBox.Text;
            downloadCts = new CancellationTokenSource();

            var progress = new Progress<Utils.MinecraftInstallProgress>(p =>
            {
                ProgressBar.Value = p.Percentage;
                ProgressText.Text = p.Percentage.ToString("F0") + "%";
                UpdateStatus($"[{p.StageName}] {p.Detail}", "#A78BFA");
                InfoText.Text = p.TotalFiles > 0
                    ? $"文件 {p.CompletedFiles}/{p.TotalFiles} · {p.SpeedDisplay}"
                    : p.SpeedDisplay;
            });

            try
            {
                if (!Directory.Exists(minecraftDir))
                {
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

                if (isFirstDownload)
                {
                    SaveDefaultPath(minecraftDir);
                    isFirstDownload = false;
                }

                await installer.InstallAsync(version.Id, version.Url, minecraftDir, progress, downloadCts.Token);

                downloadHistory.Insert(0, new DownloadRecord { Version = version.Id, Path = minecraftDir, Time = DateTime.Now, Success = true });
                UpdateDownloadHistory();
                UpdateStatus("\u2713 Download complete!", "#34D399");
                VersionStatus.Text = "\u2713";
                VersionStatus.Foreground = (Brush)FindResource("SuccessBrush");

                MessageBox.Show($"Minecraft {version.Id} downloaded!\n\nLocation: {minecraftDir}",
                    "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("下载已取消", "#FBBF24");
                downloadHistory.Insert(0, new DownloadRecord
                {
                    Version = version.Id,
                    Path = minecraftDir,
                    Time = DateTime.Now,
                    Success = false,
                    Error = "已取消"
                });
                UpdateDownloadHistory();
            }
            catch (Exception ex)
            {
                UpdateStatus("\u2717 Download failed: " + ex.Message, "#F87171");
                VersionStatus.Text = "\u2717";
                VersionStatus.Foreground = (Brush)FindResource("ErrorBrush");
                downloadHistory.Insert(0, new DownloadRecord
                {
                    Version = version.Id,
                    Path = minecraftDir,
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
                downloadCts?.Dispose();
                downloadCts = null;
                downloadBtn.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
            }
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
            downloadCts?.Cancel();
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
