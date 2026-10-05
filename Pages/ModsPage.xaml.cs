using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SFTLauncher.Pages
{
    public partial class ModsPage : Page
    {
        private static readonly string[] CommonGameVersions =
        {
            "26.3", "26.2", "26.1",
            "1.21.11", "1.21.8", "1.21.5", "1.21.4", "1.21.1", "1.21",
            "1.20.6", "1.20.4", "1.20.1", "1.20",
            "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.12.2"
        };

        private readonly Utils.ModrinthApi api = new();

        private List<Utils.ModrinthApi.ProjectInfo> currentMods = new List<Utils.ModrinthApi.ProjectInfo>();
        private bool isLoading = false;
        private bool isPlaceholder = true;

        // 下载面板状态
        private Utils.ModrinthApi.ProjectInfo? downloadProject = null;
        private List<Utils.ModrinthApi.VersionInfo> downloadVersions = new List<Utils.ModrinthApi.VersionInfo>();
        private CancellationTokenSource? downloadCts = null;
        private bool isDownloading = false;

        public ModsPage()
        {
            InitializeComponent();

            // 搜索栏占位符
            SearchBox.GotFocus += SearchBox_GotFocus;
            SearchBox.LostFocus += SearchBox_LostFocus;
            SearchBox.TextChanged += SearchBox_TextChanged;

            // 游戏版本筛选下拉框
            GameVersionFilter.Items.Add(new ComboBoxItem { Content = "全部版本", Tag = "", IsSelected = true });
            foreach (var version in CommonGameVersions)
                GameVersionFilter.Items.Add(new ComboBoxItem { Content = version, Tag = version });

            // 保存目录默认值
            SavePathBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft", "mods");
        }

        #region 搜索栏占位符

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (isPlaceholder)
            {
                SearchBox.Text = "";
                SearchBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
                isPlaceholder = false;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "输入名称搜索 Modrinth 资源...";
                SearchBox.Foreground = (Brush)FindResource("TextMutedBrush");
                isPlaceholder = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 用户手动输入时，确保不显示 placeholder
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                isPlaceholder = false;
                SearchBox.Foreground = (Brush)FindResource("TextPrimaryBrush");
            }
        }

        private string GetSearchQuery()
        {
            return isPlaceholder ? "" : SearchBox.Text.Trim();
        }

        private static string GetComboTag(ComboBox combo)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        }

        #endregion

        #region 搜索

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            isLoading = true;
            SearchButton.IsEnabled = false;
            SearchButton.Content = "搜索中...";

            var query = GetSearchQuery();
            StatusText.Text = string.IsNullOrEmpty(query) ? "正在加载资源列表..." : $"正在搜索 '{query}'...";
            StatusText.Foreground = (Brush)FindResource("AccentBrush");
            ModsList.Children.Clear();

            try
            {
                currentMods = await api.SearchAsync(
                    query: query,
                    projectType: GetComboTag(TypeFilter),
                    gameVersion: GetComboTag(GameVersionFilter),
                    index: GetComboTag(SortFilter),
                    limit: 20);

                if (currentMods.Count == 0)
                {
                    StatusText.Text = "未找到相关资源";
                    StatusText.Foreground = (Brush)FindResource("WarningBrush");
                }
                else
                {
                    StatusText.Text = $"找到 {currentMods.Count} 个资源";
                    StatusText.Foreground = (Brush)FindResource("SuccessBrush");

                    foreach (var mod in currentMods)
                        ModsList.Children.Add(CreateModCard(mod));
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "搜索失败: " + ex.Message;
                StatusText.Foreground = (Brush)FindResource("ErrorBrush");
            }
            finally
            {
                isLoading = false;
                SearchButton.IsEnabled = true;
                SearchButton.Content = "搜索";
            }
        }

        private Border CreateModCard(Utils.ModrinthApi.ProjectInfo mod)
        {
            var border = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 类型图标
            var iconBlock = new TextBlock
            {
                Text = mod.TypeIcon,
                FontSize = 26,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(iconBlock, 0);

            // 中间：标题、描述、元信息
            var infoPanel = new StackPanel();

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = mod.Title,
                Style = (Style)FindResource("LabelHeading"),
                FontSize = 15
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = "  " + mod.TypeDisplay,
                Style = (Style)FindResource("LabelMuted"),
                VerticalAlignment = VerticalAlignment.Center
            });
            infoPanel.Children.Add(titleRow);

            var descText = mod.Description ?? "";
            if (descText.Length > 80) descText = descText.Substring(0, 80) + "...";
            infoPanel.Children.Add(new TextBlock
            {
                Text = descText,
                Style = (Style)FindResource("LabelMuted"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            var updatedText = mod.DateModified.HasValue
                ? $"更新于 {mod.DateModified.Value.ToLocalTime():yyyy-MM-dd}"
                : "";
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"{mod.Author} · {mod.DownloadsDisplay} · {mod.LoadersDisplay} · {updatedText}",
                Style = (Style)FindResource("LabelSecondary"),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            });
            Grid.SetColumn(infoPanel, 1);

            // 下载按钮
            var downloadBtn = new Button
            {
                Style = (Style)FindResource("AccentButton"),
                Height = 34,
                Width = 80,
                Content = "下载",
                VerticalAlignment = VerticalAlignment.Center
            };
            downloadBtn.Click += (s, e) => DownloadButton_Click(mod);
            Grid.SetColumn(downloadBtn, 2);

            grid.Children.Add(iconBlock);
            grid.Children.Add(infoPanel);
            grid.Children.Add(downloadBtn);

            border.Child = grid;
            return border;
        }

        #endregion

        #region 下载面板

        private async void DownloadButton_Click(Utils.ModrinthApi.ProjectInfo mod)
        {
            if (isDownloading)
            {
                MessageBox.Show("当前有下载任务进行中，请等待完成或取消后再试", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 展示面板并加载版本列表
            downloadProject = mod;
            DownloadPanel.Visibility = Visibility.Visible;
            DownloadTitle.Text = mod.Title;
            DownloadVersionCombo.ItemsSource = null;
            DownloadGameVersionCombo.ItemsSource = null;
            DownloadLoaderCombo.ItemsSource = null;
            StartDownloadButton.IsEnabled = false;
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            DownloadProgressBar.Value = 0;
            DownloadStatusText.Text = "正在获取版本列表...";
            DownloadStatusText.Foreground = (Brush)FindResource("TextMutedBrush");

            try
            {
                downloadVersions = await api.GetVersionsAsync(mod.ProjectId);

                if (downloadVersions.Count == 0)
                {
                    DownloadStatusText.Text = "该资源没有可用版本";
                    DownloadStatusText.Foreground = (Brush)FindResource("WarningBrush");
                    return;
                }

                // 游戏版本下拉框（按版本号从新到旧）
                var gameVersions = downloadVersions
                    .SelectMany(v => v.GameVersions)
                    .Distinct()
                    .OrderByDescending(v => v, Comparer<string>.Create(Utils.ModrinthApi.CompareVersionStrings))
                    .ToList();
                DownloadGameVersionCombo.ItemsSource = new List<string> { "全部" }.Concat(gameVersions).ToList();
                DownloadGameVersionCombo.SelectedIndex = 0;

                // 加载器下拉框
                var loaders = downloadVersions
                    .SelectMany(v => v.Loaders)
                    .Distinct()
                    .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                DownloadLoaderCombo.ItemsSource = new List<string> { "全部" }.Concat(loaders).ToList();
                DownloadLoaderCombo.SelectedIndex = 0;

                DownloadStatusText.Text = $"共 {downloadVersions.Count} 个版本，请选择版本与保存目录";
            }
            catch (Exception ex)
            {
                DownloadStatusText.Text = "获取版本列表失败: " + ex.Message;
                DownloadStatusText.Foreground = (Brush)FindResource("ErrorBrush");
            }
        }

        private void DownloadFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (downloadVersions.Count == 0) return;

            var gameVersion = DownloadGameVersionCombo.SelectedItem as string ?? "全部";
            var loader = DownloadLoaderCombo.SelectedItem as string ?? "全部";

            var filtered = downloadVersions
                .Where(v => (gameVersion == "全部" || v.GameVersions.Contains(gameVersion)) &&
                            (loader == "全部" || v.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            DownloadVersionCombo.ItemsSource = filtered;
            DownloadVersionCombo.SelectedIndex = filtered.Count > 0 ? 0 : -1;

            DownloadStatusText.Text = filtered.Count > 0
                ? $"符合条件的版本 {filtered.Count} 个"
                : "没有符合筛选条件的版本";
        }

        private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Title = "选择保存目录";
                dialog.Filter = "文件夹|*.*";
                dialog.CheckFileExists = false;

                if (dialog.ShowDialog() == true)
                {
                    var selectedPath = Path.GetDirectoryName(dialog.FileName);
                    if (string.IsNullOrEmpty(selectedPath))
                        selectedPath = dialog.FileName;
                    SavePathBox.Text = selectedPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseDownloadPanel_Click(object sender, RoutedEventArgs e)
        {
            if (isDownloading)
            {
                downloadCts?.Cancel();
                isDownloading = false;
            }
            DownloadPanel.Visibility = Visibility.Collapsed;
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (isDownloading) return;

            var version = DownloadVersionCombo.SelectedItem as Utils.ModrinthApi.VersionInfo;
            if (version == null)
            {
                MessageBox.Show("请先选择资源版本", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var file = version.PrimaryFile;
            if (file == null || string.IsNullOrEmpty(file.Url))
            {
                MessageBox.Show("该版本没有可下载的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDir = SavePathBox.Text.Trim();
            if (string.IsNullOrEmpty(saveDir))
            {
                MessageBox.Show("请先选择保存目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targetPath = Path.Combine(saveDir, file.Filename);

            isDownloading = true;
            downloadCts = new CancellationTokenSource();
            StartDownloadButton.IsEnabled = false;
            CancelDownloadButton.Visibility = Visibility.Visible;
            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            DownloadStatusText.Text = $"正在下载 {file.Filename}...";
            DownloadStatusText.Foreground = (Brush)FindResource("AccentBrush");

            // Progress<T> 会在捕获的 UI 线程上下文上回调
            var progress = new Progress<Utils.ModrinthApi.DownloadProgress>(p =>
            {
                DownloadProgressBar.Value = p.Percent;
                DownloadStatusText.Text = p.TotalBytes > 0
                    ? $"正在下载 {file.Filename}... {FormatBytes(p.DownloadedBytes)} / {FormatBytes(p.TotalBytes)}（{p.Percent:F0}%）"
                    : $"正在下载 {file.Filename}... {FormatBytes(p.DownloadedBytes)}";
            });

            try
            {
                await api.DownloadFileAsync(file.Url, targetPath, progress, downloadCts.Token);

                DownloadProgressBar.Value = 100;
                DownloadStatusText.Text = "下载完成: " + targetPath;
                DownloadStatusText.Foreground = (Brush)FindResource("SuccessBrush");
                MessageBox.Show($"下载完成！\n\n{targetPath}", "下载完成",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                DownloadStatusText.Text = "下载已取消";
                DownloadStatusText.Foreground = (Brush)FindResource("WarningBrush");
            }
            catch (Exception ex)
            {
                DownloadStatusText.Text = "下载失败: " + ex.Message;
                DownloadStatusText.Foreground = (Brush)FindResource("ErrorBrush");
                MessageBox.Show("下载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isDownloading = false;
                downloadCts?.Dispose();
                downloadCts = null;
                StartDownloadButton.IsEnabled = true;
                CancelDownloadButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            downloadCts?.Cancel();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1048576) return $"{bytes / 1048576.0:0.1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }

        #endregion
    }
}
