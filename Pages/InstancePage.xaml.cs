using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SFTLauncher.Utils;

namespace SFTLauncher.Pages
{
    public partial class InstancePage : Page
    {
        public InstancePage()
        {
            InitializeComponent();
        }

        private void NewInstance_Click(object sender, RoutedEventArgs e)
        {
            // 跳转到版本下载页面并更新侧边栏状态
            Application.Current.MainWindow?.Dispatcher.Invoke(() =>
            {
                (Application.Current.MainWindow as MainWindow)?.NavigateToExternal("downloads");
            });
        }

        private void StartInstance_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            // 获取实例名称
            string instanceName = "Unknown";
            if (button.Parent is Grid grid)
            {
                var nameBlock = grid.Children[0] as StackPanel;
                if (nameBlock?.Children.Count > 0 && nameBlock.Children[0] is TextBlock tb)
                {
                    instanceName = tb.Text;
                }
            }

            // 解析实例信息
            var instance = ParseInstance(instanceName);
            
            // 检测 Minecraft 目录
            string minecraftDir = MinecraftLauncher.DetectMinecraftDirectory();
            if (string.IsNullOrEmpty(minecraftDir))
            {
                MessageBox.Show("未检测到 Minecraft 安装。\n\n请安装 Minecraft 或在设置中配置路径。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检测 Java
            string javaPath = MinecraftLauncher.DetectJavaExecutable();
            if (string.IsNullOrEmpty(javaPath))
            {
                MessageBox.Show("未检测到 Java 安装。\n\n请安装 Java 17 或更高版本，或在设置中配置 Java 路径。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 尝试启动游戏
            string error = null;
            bool success = MinecraftLauncher.LaunchGame(
                instance, minecraftDir, javaPath, 4096, out error);

            if (success)
            {
                MessageBox.Show($"游戏 '{instanceName}' 启动成功！\n\n游戏应该在新窗口中打开。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"启动失败：{error}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MinecraftLauncher.InstanceInfo ParseInstance(string name)
        {
            // 根据名称返回对应的实例信息
            // 实际项目中应该从配置文件或数据库读取
            return new MinecraftLauncher.InstanceInfo
            {
                Name = name,
                Version = name.Contains("1.20.4") ? "1.20.4" : 
                          name.Contains("1.20.1") ? "1.20.1" : "1.20.4",
                Loader = name.Contains("Fabric") ? "fabric" : 
                         name.Contains("Forge") ? "forge" : "vanilla",
                LoaderVersion = "",
                ModCount = "0",
                LastPlayed = ""
            };
        }
    }
}
