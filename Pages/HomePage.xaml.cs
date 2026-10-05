using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SFTLauncher.Pages
{
    public partial class HomePage : Page
    {
        private static readonly HttpClient client = new HttpClient();

        public HomePage()
        {
            InitializeComponent();
        }

        private async void StartGame_Click(object sender, RoutedEventArgs e)
        {
            // 检测 Java
            string javaPath = Utils.MinecraftLauncher.DetectJavaExecutable();
            
            if (string.IsNullOrEmpty(javaPath))
            {
                // Java 未找到，询问用户是否下载
                var result = MessageBox.Show(
                    "未检测到 Java 安装。\n\nMinecraft 需要 Java 17 或更高版本才能运行。\n\n是否现在下载并安装 Java？",
                    "需要 Java",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallJava();
                }
                return;
            }

            // 检测 Minecraft 目录
            string minecraftDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".minecraft");

            if (!Directory.Exists(minecraftDir))
            {
                MessageBox.Show(
                    "未找到 Minecraft 目录。\n\n请先在官方 Minecraft 启动器中下载游戏。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 创建实例信息
            var instance = new Utils.MinecraftLauncher.InstanceInfo
            {
                Name = "Vanilla 1.20.4",
                Version = "1.20.4",
                Loader = "vanilla",
                LoaderVersion = "",
                ModCount = "0",
                LastPlayed = DateTime.Now.ToString("g")
            };

            // 启动游戏
            string error = null;
            bool success = Utils.MinecraftLauncher.LaunchGame(
                instance, minecraftDir, javaPath, 4096, out error);

            if (success)
            {
                MessageBox.Show("游戏启动成功！\n\n游戏应该在新的窗口中打开。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"启动失败：{error}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DownloadAndInstallJava()
        {
            try
            {
                // 使用 Eclipse Adoptium (Eclipse Temurin) 的官方下载链接
                // Windows x64 JDK 17
                string downloadUrl = "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk";
                string savePath = Path.Combine(Path.GetTempPath(), "temurin-jdk-17-x64.msi");

                MessageBox.Show("正在下载 Java 17...\n\n这可能需要几分钟时间。",
                    "下载中", MessageBoxButton.OK, MessageBoxImage.Information);

                // 下载
                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 500_000_000; // 估计大小
                    var buffer = new byte[8192];
                    long totalRead = 0;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            // 可以在这里更新进度条
                        }
                    }
                }

                MessageBox.Show($"Java 已下载到: {savePath}\n\n请手动运行安装程序完成安装。",
                    "下载完成", MessageBoxButton.OK, MessageBoxImage.Information);

                // 尝试打开下载的文件
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = savePath,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载 Java 失败：{ex.Message}\n\n请手动从 https://adoptium.net 下载并安装 Java 17。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
