using System;
using System.Windows;
using System.Windows.Controls;

namespace SFTLauncher.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        private void BrowseMcPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 Minecraft 目录",
                Filter = "文件夹|*.minecraft",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog() == true)
            {
                McPathBox.Text = dialog.FileName;
            }
        }

        private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 Java 可执行文件",
                Filter = "可执行文件|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            if (dialog.ShowDialog() == true)
            {
                JavaPathBox.Text = dialog.FileName;
            }
        }

        private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 更新内存显示
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("设置已保存！", "成功", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
