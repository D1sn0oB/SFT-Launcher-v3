using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SFTLauncher.Utils
{
    public static class MinecraftLauncher
    {
        // Minecraft 版本信息
        public class VersionInfo
        {
            public string Id { get; set; }
            public string Type { get; set; } // release, snapshot
            public string Url { get; set; }
            public string JavaArgs { get; set; }
        }

        // 实例信息
        public class InstanceInfo
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public string Loader { get; set; } // vanilla, fabric, forge
            public string LoaderVersion { get; set; }
            public string ModCount { get; set; }
            public string LastPlayed { get; set; }
        }

        /// <summary>
        /// 检测 Minecraft 安装目录
        /// </summary>
        public static string DetectMinecraftDirectory()
        {
            // Windows 默认路径
            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".minecraft");

            if (Directory.Exists(defaultPath))
                return defaultPath;

            // 检查其他可能的位置
            string[] possiblePaths = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "MinecraftWin64"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Curse", "Minecraft", "Instances")
            };

            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// 检测 Java 安装
        /// </summary>
        public static string DetectJavaExecutable()
        {
            // 检查环境变量
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string javaExe = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaExe))
                    return javaExe;
            }

            // 检查常见安装位置
            string[] possiblePaths = new string[]
            {
                "C:\\\\Program Files\\\\Java\\\\jdk-17\\\\bin\\\\java.exe",
                "C:\\\\Program Files\\\\Java\\\\jdk-21\\\\bin\\\\java.exe",
                "C:\\\\Program Files (x86)\\\\Java\\\\jdk-17\\\\bin\\\\java.exe",
                "C:\\\\Program Files\\\\Eclipse Adoptium\\\\jdk-17.x.x-hotspot\\\\bin\\\\java.exe",
                "C:\\\\Program Files\\\\Eclipse Adoptium\\\\jdk-21.x.x-hotspot\\\\bin\\\\java.exe"
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // 尝试通过 where 命令查找
            try
            {
                var process = new Process();
                process.StartInfo.FileName = "where";
                process.StartInfo.Arguments = "java";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    string[] lines = output.Split('\n');
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && trimmed.EndsWith("java.exe"))
                            return trimmed;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 获取 Java 版本
        /// </summary>
        public static string GetJavaVersion(string javaPath)
        {
            try
            {
                var process = new Process();
                process.StartInfo.FileName = javaPath;
                process.StartInfo.Arguments = "-version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardError = true;
                process.Start();

                string output = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // 输出格式: java version "17.0.1"
                if (output.Contains("version"))
                {
                    int start = output.IndexOf('"') + 1;
                    int end = output.IndexOf('"', start);
                    if (start > 0 && end > start)
                        return output.Substring(start, end - start);
                }
            }
            catch { }

            return "Unknown";
        }

        /// <summary>
        /// 构建启动命令
        /// </summary>
        public static string BuildLaunchCommand(InstanceInfo instance, string minecraftDir, string javaPath, int memoryMb)
        {
            var args = new StringBuilder();

            // Java 参数
            args.Append($"-Xmx{memoryMb}M -Xms{memoryMb/2}M ");

            // Minecraft 版本
            args.Append($"-Dfml.ignoreInvalidMinecraftCertificates=true -Dfml.ignorePatchDisagreements=true ");

            // 游戏参数
            args.Append("\"-jar\" ");
            args.Append($"\"{GetMinecraftJar(minecraftDir, instance.Version)}\" ");

            // 加载器特定参数
            if (instance.Loader == "fabric")
            {
                args.Append("-Dfabric.version.json=");
                args.Append($"\"{Path.Combine(minecraftDir, "versions", instance.Version, "fabric-loader.json")}\" ");
            }
            else if (instance.Loader == "forge")
            {
                args.Append("-Dforge.version=");
                args.Append($"{instance.LoaderVersion} ");
            }

            return args.ToString().Trim();
        }

        /// <summary>
        /// 获取 Minecraft jar 路径
        /// </summary>
        private static string GetMinecraftJar(string minecraftDir, string version)
        {
            return Path.Combine(minecraftDir, "versions", version, $"{version}.jar");
        }

        /// <summary>
        /// 启动游戏
        /// </summary>
        public static bool LaunchGame(InstanceInfo instance, string minecraftDir, string javaPath, int memoryMb, out string error)
        {
            error = null;

            // 验证参数
            if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            {
                error = "Java 可执行文件未找到。请在设置中配置 Java 路径。";
                return false;
            }

            if (string.IsNullOrEmpty(minecraftDir) || !Directory.Exists(minecraftDir))
            {
                error = "Minecraft 目录未找到。请在设置中配置 Minecraft 路径。";
                return false;
            }

            // 检查 jar 文件
            string jarPath = GetMinecraftJar(minecraftDir, instance.Version);
            if (!File.Exists(jarPath))
            {
                error = $"游戏文件不存在：{jarPath}\n请先在 Minecraft 启动器中下载游戏。";
                return false;
            }

            try
            {
                // 构建启动命令
                var startInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = BuildLaunchCommand(instance, minecraftDir, javaPath, memoryMb),
                    WorkingDirectory = minecraftDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // 启动进程
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "无法启动游戏进程。";
                        return false;
                    }

                    // 不等待进程结束（游戏会持续运行）
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"启动游戏时出错：{ex.Message}";
                return false;
            }
        }
    }
}
