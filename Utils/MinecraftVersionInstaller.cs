using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SFTLauncher.Utils
{
    #region 下载源

    /// <summary>一个完整的下载源定义，包含需要替换的基础 URL。</summary>
    public class DownloadSource
    {
        public DownloadSource(string name, string launcherMeta, string meta,
            string libraries, string resources)
        {
            Name = name;
            LauncherMeta = launcherMeta;
            Meta = meta;
            Libraries = libraries;
            Resources = resources;
        }

        public string Name { get; }
        public string LauncherMeta { get; }
        public string Meta { get; }
        public string Libraries { get; }
        public string Resources { get; }
    }

    /// <summary>内置下载源定义。</summary>
    public static class DownloadSources
    {
        public static DownloadSource Official { get; } = new(
            "Official",
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            "https://piston-meta.mojang.com",
            "https://libraries.minecraft.net",
            "https://resources.download.minecraft.net");

        /// <summary>BMCLAPI 国内镜像（bangbang93 提供）。</summary>
        public static DownloadSource Bmcl { get; } = new(
            "BMCL",
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json",
            "https://bmclapi2.bangbang93.com",
            "https://bmclapi2.bangbang93.com/maven",
            "https://bmclapi2.bangbang93.com/assets");

        public static IReadOnlyList<DownloadSource> All { get; } = new[] { Official, Bmcl };
    }

    /// <summary>
    /// 下载源提供器：所有下载 URL 以官方源为准书写，运行时按 Active 源改写域名；
    /// 请求失败自动回退 Fallback 源。实现参考 NyaLauncher 的 DownloadSourceProvider。
    /// </summary>
    public static class DownloadSourceProvider
    {
        /// <summary>当前活跃下载源（默认官方，可切换 BMCLAPI）。</summary>
        public static DownloadSource Active { get; set; } = DownloadSources.Official;

        /// <summary>自动回退源；设为 null 则不回退。</summary>
        public static DownloadSource? Fallback { get; set; } = DownloadSources.Bmcl;

        private static readonly HttpClient SharedClient = new()
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

        static DownloadSourceProvider()
        {
            // 部分 CDN/镜像会拒绝缺失 User-Agent 的请求
            SharedClient.DefaultRequestHeaders.UserAgent.ParseAdd("SFTLauncher/3.0");
        }

        /// <summary>将官方源 URL 改写为 Active 源对应地址（Active 为官方源时原样返回）。</summary>
        public static string Resolve(string officialUrl)
        {
            if (string.IsNullOrWhiteSpace(officialUrl) || ReferenceEquals(Active, DownloadSources.Official))
                return officialUrl;
            return ReplaceBaseUrl(officialUrl, Active);
        }

        /// <summary>获取 Fallback 源对应的 URL；无 Fallback 时返回 null。</summary>
        public static string? ResolveFallback(string officialUrl)
        {
            if (Fallback is null || string.IsNullOrWhiteSpace(officialUrl))
                return null;
            return ReplaceBaseUrl(officialUrl, Fallback);
        }

        /// <summary>GET 文本，失败自动回退 Fallback 源。</summary>
        public static async Task<string> GetStringAsync(string officialUrl, CancellationToken cancellationToken = default)
        {
            var primaryUrl = ValidateHttpsUrl(Resolve(officialUrl));
            var fallbackUrl = ResolveFallback(officialUrl);
            try
            {
                return await SharedClient.GetStringAsync(primaryUrl, cancellationToken);
            }
            catch (Exception) when (IsFallbackEligible(fallbackUrl, primaryUrl, cancellationToken))
            {
                return await SharedClient.GetStringAsync(ValidateHttpsUrl(fallbackUrl!), cancellationToken);
            }
        }

        /// <summary>GET 字节，失败自动回退 Fallback 源。</summary>
        public static async Task<byte[]> GetBytesAsync(string officialUrl, CancellationToken cancellationToken = default)
        {
            var primaryUrl = ValidateHttpsUrl(Resolve(officialUrl));
            var fallbackUrl = ResolveFallback(officialUrl);
            try
            {
                return await SharedClient.GetByteArrayAsync(primaryUrl, cancellationToken);
            }
            catch (Exception) when (IsFallbackEligible(fallbackUrl, primaryUrl, cancellationToken))
            {
                return await SharedClient.GetByteArrayAsync(ValidateHttpsUrl(fallbackUrl!), cancellationToken);
            }
        }

        private static bool IsFallbackEligible(string? fallbackUrl, string primaryUrl, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            fallbackUrl is not null &&
            !string.Equals(fallbackUrl, primaryUrl, StringComparison.OrdinalIgnoreCase);

        /// <summary>所有出站请求统一强制 HTTPS。</summary>
        private static string ValidateHttpsUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"下载地址不是有效的 HTTPS URL：{url}");
            }
            return url;
        }

        /// <summary>
        /// 将官方源各域名的 URL 替换为目标源对应地址；未识别的域名原样返回（直连）。
        /// </summary>
        private static string ReplaceBaseUrl(string url, DownloadSource target)
        {
            if (url.Contains("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
                return url.Replace("https://libraries.minecraft.net", target.Libraries, StringComparison.OrdinalIgnoreCase);

            if (url.Contains("resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase))
                return url.Replace("https://resources.download.minecraft.net", target.Resources, StringComparison.OrdinalIgnoreCase);

            if (url.Contains("piston-meta.mojang.com", StringComparison.OrdinalIgnoreCase))
                return url.Replace("https://piston-meta.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

            if (url.Contains("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase))
                return url.Replace("https://piston-data.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

            if (url.Contains("launchermeta.mojang.com", StringComparison.OrdinalIgnoreCase))
                return url.Replace("https://launchermeta.mojang.com", target.Meta, StringComparison.OrdinalIgnoreCase);

            return url;
        }
    }

    #endregion

    #region 进度与版本模型

    /// <summary>安装进度快照：阶段、字节数与文件数，百分比由字节数推导。</summary>
    public class MinecraftInstallProgress
    {
        public MinecraftInstallProgress(int stageIndex, string stageName, string detail,
            long completedBytes, long totalBytes, int completedFiles, int totalFiles, double bytesPerSecond)
        {
            StageIndex = stageIndex;
            StageName = stageName;
            Detail = detail;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            BytesPerSecond = bytesPerSecond;
        }

        public int StageIndex { get; }
        public string StageName { get; }
        public string Detail { get; }
        public long CompletedBytes { get; }
        /// <summary>资源文件清单要等资源索引下载完才能统计，因此总字节数可能在安装过程中增长。</summary>
        public long TotalBytes { get; }
        public int CompletedFiles { get; }
        public int TotalFiles { get; }
        public double BytesPerSecond { get; }

        public double Percentage => TotalBytes <= 0
            ? 0
            : Math.Clamp(CompletedBytes * 100d / TotalBytes, 0, 100);

        public string SpeedDisplay => BytesPerSecond >= 1048576
            ? $"{BytesPerSecond / 1048576.0:0.0} MB/s"
            : BytesPerSecond >= 1024
                ? $"{BytesPerSecond / 1024.0:0} KB/s"
                : $"{BytesPerSecond:0} B/s";
    }

    /// <summary>版本清单条目（来自 Mojang version_manifest_v2）。</summary>
    public class MinecraftVersionEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("releaseTime")]
        public DateTime ReleaseTime { get; set; }

        /// <summary>是否为最新正式版。</summary>
        [JsonIgnore]
        public bool IsLatestRelease { get; set; }

        [JsonIgnore]
        public string TypeDisplay => Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照版",
            "old_beta" => "Beta",
            "old_alpha" => "Alpha",
            _ => Type
        };

        [JsonIgnore]
        public string TypeIcon => Type switch
        {
            "release" => "📦",
            "snapshot" => "🧪",
            "old_beta" => "🔶",
            "old_alpha" => "🔷",
            _ => "📄"
        };

        public override string ToString() =>
            $"{Id}（{TypeDisplay} · {ReleaseTime.ToLocalTime():yyyy-MM-dd}）";
    }

    internal class VersionManifestModel
    {
        [JsonPropertyName("latest")]
        public LatestVersionsModel? Latest { get; set; }

        [JsonPropertyName("versions")]
        public List<MinecraftVersionEntry> Versions { get; set; } = new();
    }

    internal class LatestVersionsModel
    {
        [JsonPropertyName("release")]
        public string Release { get; set; } = string.Empty;

        [JsonPropertyName("snapshot")]
        public string Snapshot { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>
    /// 从 Mojang 官方元数据安装原版 Minecraft 版本：
    /// 客户端 JAR + 依赖库 + 资源索引 + 游戏资源 + 版本 JSON，共 7 个阶段。
    /// SHA-1 匹配的已有文件直接复用；下载先写临时文件、校验通过后才移动到位。
    /// 实现参考 NyaLauncher（https://github.com/redstore-noob/NyaLauncher-avalonia）的 MinecraftVersionInstaller。
    /// </summary>
    public class MinecraftVersionInstaller
    {
        public const int StageCount = 7;
        private const int BufferSize = 128 * 1024;
        private const int MaximumParallelDownloads = 8;

        public static readonly string[] StageNames =
        {
            "获取版本元数据",
            "分析下载清单",
            "下载游戏客户端",
            "下载依赖库",
            "下载资源索引",
            "下载游戏资源",
            "完成校验与安装"
        };

        /// <summary>流式下载客户端（元数据等小 JSON 走 DownloadSourceProvider 的共享客户端）。</summary>
        private static readonly HttpClient DownloadClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        static MinecraftVersionInstaller()
        {
            DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("SFTLauncher/3.0");
        }

        /// <summary>进度上报节流间隔。</summary>
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(120);
        private static readonly long ProgressIntervalStopwatchTicks =
            (long)(Stopwatch.Frequency * ProgressInterval.TotalSeconds);
        private static long _lastReportTicks;

        #region 版本清单

        /// <summary>
        /// 获取 Minecraft 版本清单（按发布时间倒序，含正式版/快照/远古版本）。
        /// </summary>
        public static async Task<List<MinecraftVersionEntry>> GetVersionsAsync(
            CancellationToken cancellationToken = default)
        {
            var json = await DownloadSourceProvider.GetStringAsync(
                    DownloadSources.Official.LauncherMeta, cancellationToken);
            var manifest = JsonSerializer.Deserialize<VersionManifestModel>(json);
            if (manifest?.Versions is not { Count: > 0 })
                throw new InvalidDataException("版本清单响应为空。");

            var versions = manifest.Versions;
            foreach (var version in versions)
            {
                if (manifest.Latest is { } latest && version.Id == latest.Release)
                    version.IsLatestRelease = true;
            }

            return versions.OrderByDescending(v => v.ReleaseTime).ToList();
        }

        #endregion

        #region 安装

        /// <summary>
        /// 安装指定版本到 Minecraft 目录（标准 Mojang 目录结构：versions/ libraries/ assets/）。
        /// </summary>
        /// <param name="versionId">版本 ID（来自 <see cref="MinecraftVersionEntry.Id"/>）。</param>
        /// <param name="metadataUrl">版本元数据 URL（来自 <see cref="MinecraftVersionEntry.Url"/>）。</param>
        /// <param name="minecraftDirectory">Minecraft 根目录（自动创建）。</param>
        /// <param name="progress">进度回调（在创建 <see cref="Progress{T}"/> 的线程上下文上触发）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task InstallAsync(
            string versionId,
            string metadataUrl,
            string minecraftDirectory,
            IProgress<MinecraftInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(versionId))
                throw new ArgumentException("版本 ID 不能为空", nameof(versionId));
            if (string.IsNullOrWhiteSpace(metadataUrl))
                throw new ArgumentException("版本元数据 URL 不能为空", nameof(metadataUrl));
            if (string.IsNullOrWhiteSpace(minecraftDirectory))
                throw new ArgumentException("Minecraft 目录不能为空", nameof(minecraftDirectory));

            ValidateVersionId(versionId);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(minecraftDirectory));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "versions"));

            Report(progress, 1, "获取版本元数据", $"正在读取 Minecraft {versionId} 的版本描述", 0, 0, 0, 0, 0);
            var metadataBytes = await DownloadSourceProvider.GetBytesAsync(metadataUrl, cancellationToken);

            await InstallFromMetadataBytesAsync(versionId, root, metadataBytes, progress, cancellationToken);
        }

        /// <summary>从已有元数据字节流完成安装（internal：供测试传入合成元数据）。</summary>
        internal async Task InstallFromMetadataBytesAsync(
            string versionId,
            string root,
            byte[] metadataBytes,
            IProgress<MinecraftInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateVersionId(versionId);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "versions"));

            Report(progress, 2, "分析下载清单", "正在整理客户端、依赖库与资源索引", 0, 0, 0, 0, 0);
            using var metadata = JsonDocument.Parse(metadataBytes);
            var versionDirectory = Path.Combine(root, "versions", versionId);
            Directory.CreateDirectory(versionDirectory);

            // 阶段 3-5 的清单在安装前即可确定；阶段 6 的资源文件清单要等资源索引下载完才能生成
            var clientFiles = CreateClientPlan(metadata.RootElement, versionId, versionDirectory);
            var libraryFiles = CreateLibraryPlan(metadata.RootElement, root);
            var assetIndexFile = CreateAssetIndexPlan(metadata.RootElement, root);

            var stopwatch = Stopwatch.StartNew();
            var counters = new InstallCounters();
            counters.AddTotalBytes(clientFiles.Concat(libraryFiles).Sum(f => Math.Max(0, f.Size)));
            counters.AddTotalFiles(clientFiles.Count + libraryFiles.Count);
            if (assetIndexFile is not null)
            {
                counters.AddTotalBytes(Math.Max(0, assetIndexFile.Size));
                counters.AddTotalFiles(1);
            }

            await DownloadStageAsync(3, "下载游戏客户端", clientFiles, progress, counters, stopwatch, cancellationToken);
            await DownloadStageAsync(4, "下载依赖库", libraryFiles, progress, counters, stopwatch, cancellationToken);

            if (assetIndexFile is not null)
            {
                await DownloadStageAsync(5, "下载资源索引", new[] { assetIndexFile }, progress, counters, stopwatch, cancellationToken);
            }

            var assetFiles = assetIndexFile is not null
                ? await CreateAssetPlanAsync(assetIndexFile.TargetPath, root, cancellationToken)
                : new List<DownloadFile>();
            counters.AddTotalBytes(assetFiles.Sum(f => Math.Max(0, f.Size)));
            counters.AddTotalFiles(assetFiles.Count);
            await DownloadStageAsync(6, "下载游戏资源", assetFiles, progress, counters, stopwatch, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 7, "完成校验与安装", "正在写入版本描述并完成安装",
                counters.CompletedBytes, counters.TotalBytes, counters.CompletedFiles, counters.TotalFiles,
                CalculateSpeed(counters.NetworkBytes, stopwatch));
            var versionJsonPath = Path.Combine(versionDirectory, $"{versionId}.json");
            await WriteAllBytesAtomicallyAsync(versionJsonPath, metadataBytes, cancellationToken);
            Report(progress, 7, "完成校验与安装", $"Minecraft {versionId} 已安装完成",
                counters.TotalBytes, counters.TotalBytes, counters.TotalFiles, counters.TotalFiles,
                CalculateSpeed(counters.NetworkBytes, stopwatch));
        }

        #endregion

        #region 下载清单生成

        internal static List<DownloadFile> CreateClientPlan(JsonElement root, string versionId, string versionDirectory)
        {
            if (!root.TryGetProperty("downloads", out var downloads) ||
                !downloads.TryGetProperty("client", out var client))
            {
                // 无 client 下载信息（如 Loader 版本 JSON 通过 inheritsFrom 继承），正常跳过
                return new List<DownloadFile>();
            }

            return new List<DownloadFile>
            {
                CreateDownloadFile(client, Path.Combine(versionDirectory, $"{versionId}.jar"), "客户端文件")
            };
        }

        internal static List<DownloadFile> CreateLibraryPlan(JsonElement root, string minecraftDirectory)
        {
            var result = new List<DownloadFile>();
            if (!root.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            // 同一目标路径只保留最后一条，去重 Maven 坐标重复声明的库
            var byTarget = new Dictionary<string, DownloadFile>(StringComparer.OrdinalIgnoreCase);
            var features = MinecraftRuleEvaluator.CreateDefaultFeatures(hasCustomResolution: true);
            foreach (var library in libraries.EnumerateArray())
            {
                if (!MinecraftRuleEvaluator.IsAllowed(library, features))
                    continue;

                if (TryPlanDownloadsArtifact(library, byTarget, minecraftDirectory))
                    continue;

                if (TryPlanLegacyNatives(library, byTarget, minecraftDirectory))
                    continue;

                PlanCoordinateLibrary(library, byTarget, minecraftDirectory);
            }

            result.AddRange(byTarget.Values);
            return result;
        }

        /// <summary>新版本格式：downloads.artifact / downloads.classifiers + natives。</summary>
        private static bool TryPlanDownloadsArtifact(
            JsonElement library, IDictionary<string, DownloadFile> result, string minecraftDirectory)
        {
            if (!library.TryGetProperty("downloads", out var downloads))
                return false;

            if (downloads.TryGetProperty("artifact", out var artifact))
                AddLibraryDownload(result, artifact, minecraftDirectory, "依赖库");

            if (downloads.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.ValueKind == JsonValueKind.Object &&
                library.TryGetProperty("natives", out var natives) &&
                natives.TryGetProperty(MinecraftRuleEvaluator.GetOperatingSystemName(), out var nativeClassifierElement) &&
                nativeClassifierElement.ValueKind == JsonValueKind.String)
            {
                if (classifiers.TryGetProperty(ExpandArchPlaceholder(nativeClassifierElement.GetString()!), out var nativeArtifact))
                {
                    AddLibraryDownload(result, nativeArtifact, minecraftDirectory, "原生依赖库");
                }
            }

            return true;
        }

        /// <summary>旧版本（1.7.x 及更早）natives 回退：无 downloads 字段时用 name + classifier 拼 natives JAR。</summary>
        private static bool TryPlanLegacyNatives(
            JsonElement library, IDictionary<string, DownloadFile> result, string minecraftDirectory)
        {
            if (!library.TryGetProperty("natives", out var legacyNatives) ||
                !legacyNatives.TryGetProperty(MinecraftRuleEvaluator.GetOperatingSystemName(), out var legacyClassifierElement) ||
                legacyClassifierElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var nativeName = library.TryGetProperty("name", out var legacyNameElement)
                ? legacyNameElement.GetString()
                : null;
            var nativeRelPath = CreateNativeMavenPath(nativeName, legacyClassifierElement.GetString());
            if (nativeRelPath is null)
                return false;

            var nativeBaseUrl = library.TryGetProperty("url", out var legacyUrlElement)
                ? legacyUrlElement.GetString() ?? "https://libraries.minecraft.net/"
                : "https://libraries.minecraft.net/";
            var nativeUrl = $"{nativeBaseUrl.TrimEnd('/')}/{nativeRelPath.Replace('\\', '/')}";
            var nativeTarget = ResolveRelativePath(Path.Combine(minecraftDirectory, "libraries"), nativeRelPath);
            result[nativeTarget] = new DownloadFile(nativeUrl, nativeTarget, null, 0, "原生依赖库");
            return true;
        }

        /// <summary>更早的纯坐标格式：由 name（Maven 坐标）+ url 推导下载地址。</summary>
        private static void PlanCoordinateLibrary(
            JsonElement library, IDictionary<string, DownloadFile> result, string minecraftDirectory)
        {
            if (!library.TryGetProperty("name", out var nameElement))
                return;
            var libraryName = nameElement.GetString();
            var relativePath = CreateMavenPath(libraryName);
            if (relativePath is null)
                return;
            var baseUrl = library.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : "https://libraries.minecraft.net/";
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = "https://libraries.minecraft.net/";
            var url = $"{baseUrl.TrimEnd('/')}/{relativePath.Replace('\\', '/')}";
            var target = ResolveRelativePath(Path.Combine(minecraftDirectory, "libraries"), relativePath);
            result[target] = new DownloadFile(url, target, null, 0, "依赖库");
        }

        /// <summary>natives classifier 中的 ${arch} 占位符按当前系统位数展开。</summary>
        private static string ExpandArchPlaceholder(string classifier) =>
            classifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);

        internal static DownloadFile? CreateAssetIndexPlan(JsonElement root, string minecraftDirectory)
        {
            if (!root.TryGetProperty("assetIndex", out var assetIndex))
                return null;
            var id = assetIndex.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : root.TryGetProperty("assets", out var assetsElement)
                    ? assetsElement.GetString()
                    : null;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("资源索引缺少 ID。");

            return CreateDownloadFile(
                assetIndex,
                Path.Combine(minecraftDirectory, "assets", "indexes", $"{id}.json"),
                "资源索引");
        }

        internal static async Task<List<DownloadFile>> CreateAssetPlanAsync(
            string indexPath, string minecraftDirectory, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(indexPath);
            using var index = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<DownloadFile>();
            if (!index.RootElement.TryGetProperty("objects", out var objects) ||
                objects.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            var byTarget = new Dictionary<string, DownloadFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in objects.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!asset.Value.TryGetProperty("hash", out var hashElement))
                    continue;
                var hash = hashElement.GetString();
                if (!IsSha1(hash))
                    continue;
                var size = asset.Value.TryGetProperty("size", out var sizeElement) &&
                           sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;
                var relativePath = Path.Combine(hash![..2], hash);
                var target = ResolveRelativePath(
                    Path.Combine(minecraftDirectory, "assets", "objects"), relativePath);
                byTarget[target] = new DownloadFile(
                    $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                    target, hash, size, asset.Name);
            }

            result.AddRange(byTarget.Values);
            return result;
        }

        private static DownloadFile CreateDownloadFile(JsonElement element, string targetPath, string displayName)
        {
            var url = element.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidDataException($"{displayName}缺少下载地址。");
            var sha1 = element.TryGetProperty("sha1", out var sha1Element)
                ? sha1Element.GetString()
                : null;
            var size = element.TryGetProperty("size", out var sizeElement) &&
                       sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            return new DownloadFile(url, targetPath, sha1, size, displayName);
        }

        private static void AddLibraryDownload(
            IDictionary<string, DownloadFile> result, JsonElement element,
            string minecraftDirectory, string displayName)
        {
            if (!element.TryGetProperty("path", out var pathElement))
                return;
            var relativePath = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(relativePath))
                return;
            var target = ResolveRelativePath(Path.Combine(minecraftDirectory, "libraries"), relativePath);
            result[target] = CreateDownloadFile(element, target, Path.GetFileName(target) ?? displayName);
        }

        /// <summary>由 Maven 坐标推导相对路径（支持 @extension 后缀，如 org.lwjgl:lwjgl:3.2.3@jar）。</summary>
        internal static string? CreateMavenPath(string? coordinate)
        {
            if (string.IsNullOrWhiteSpace(coordinate))
                return null;

            var extension = "jar";
            var name = coordinate;
            var extensionSeparator = coordinate.IndexOf('@');
            if (extensionSeparator >= 0)
            {
                extension = coordinate[(extensionSeparator + 1)..];
                name = coordinate[..extensionSeparator];
            }

            var parts = name.Split(':');
            if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
                return null;
            var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
            var classifier = parts.Length == 4 ? $"-{parts[3]}" : string.Empty;
            return Path.Combine(groupPath, parts[1], parts[2],
                $"{parts[1]}-{parts[2]}{classifier}.{extension}");
        }

        /// <summary>旧版本 natives 的 Maven 路径：name + classifier 拼出 natives JAR 相对路径。</summary>
        internal static string? CreateNativeMavenPath(string? coordinate, string? classifier)
        {
            if (string.IsNullOrWhiteSpace(coordinate) || string.IsNullOrWhiteSpace(classifier))
                return null;
            var parts = coordinate.Split(':');
            if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
                return null;
            return Path.Combine(
                parts[0].Replace('.', Path.DirectorySeparatorChar),
                parts[1], parts[2],
                $"{parts[1]}-{parts[2]}-{classifier}.jar");
        }

        /// <summary>
        /// 校验版本 ID 只能作为 versions/ 下的单个文件夹名使用，
        /// 拒绝路径分隔符与 "."/".."（防止路径穿越）。
        /// </summary>
        internal static void ValidateVersionId(string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId))
                throw new ArgumentException("版本 ID 不能为空。", nameof(versionId));
            if (versionId is "." or ".." ||
                versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                versionId.Contains('/', StringComparison.Ordinal) ||
                versionId.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException($"版本 ID 包含不安全字符：{versionId}", nameof(versionId));
            }
        }

        private static string ResolveRelativePath(string root, string relativePath)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var target = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!target.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
            {
                throw new InvalidDataException($"下载路径超出 Minecraft 目录：{relativePath}");
            }
            return target;
        }

        private static bool IsSha1(string? value) =>
            value is { Length: 40 } && value.All(Uri.IsHexDigit);

        #endregion

        #region 分阶段并发下载

        private static async Task DownloadStageAsync(
            int stageIndex, string stageName, IReadOnlyList<DownloadFile> files,
            IProgress<MinecraftInstallProgress>? progress, InstallCounters counters,
            Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            if (files.Count == 0)
            {
                Report(progress, stageIndex, stageName, "此阶段没有需要下载的文件",
                    counters.CompletedBytes, counters.TotalBytes, counters.CompletedFiles, counters.TotalFiles,
                    CalculateSpeed(counters.NetworkBytes, stopwatch));
                return;
            }

            using var semaphore = new SemaphoreSlim(MaximumParallelDownloads);
            var currentFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            var tasks = files.Select(file => Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    currentFiles.TryAdd(file.DisplayName, 0);
                    var reusedBytes = await DownloadFileAsync(
                        file, counters.AddProgressBytes,
                        () => ReportThrottled(progress, stageIndex, stageName, file,
                            currentFiles.Count, counters, stopwatch),
                        cancellationToken);
                    if (reusedBytes > 0)
                        counters.AddCompleted(reusedBytes);
                    counters.IncrementCompletedFiles();
                }
                finally
                {
                    currentFiles.TryRemove(file.DisplayName, out _);
                    semaphore.Release();
                }
            }, cancellationToken));

            await Task.WhenAll(tasks);
            Report(progress, stageIndex, stageName, $"{stageName}完成",
                counters.CompletedBytes, counters.TotalBytes, counters.CompletedFiles, counters.TotalFiles,
                CalculateSpeed(counters.NetworkBytes, stopwatch));
        }

        /// <summary>按固定时间间隔节流的中途进度上报。</summary>
        private static void ReportThrottled(
            IProgress<MinecraftInstallProgress>? progress, int stageIndex, string stageName,
            DownloadFile file, int activeFileCount, InstallCounters counters, Stopwatch stopwatch)
        {
            var now = stopwatch.ElapsedTicks;
            var previous = Interlocked.Read(ref _lastReportTicks);
            if (now - previous < ProgressIntervalStopwatchTicks ||
                Interlocked.CompareExchange(ref _lastReportTicks, now, previous) != previous)
            {
                return;
            }

            Report(progress, stageIndex, stageName,
                $"正在处理 {activeFileCount} 个文件 · {file.DisplayName}",
                counters.CompletedBytes, counters.TotalBytes, counters.CompletedFiles, counters.TotalFiles,
                CalculateSpeed(counters.NetworkBytes, stopwatch));
        }

        /// <summary>
        /// 下载单个文件：已存在且校验通过则复用；否则经主源/回退源下载（每源最多两次）。
        /// 返回复用文件的字节数（0 表示本次实际下载）。
        /// </summary>
        private static async Task<long> DownloadFileAsync(
            DownloadFile file, Action<long> addProgressBytes, Action reportProgress,
            CancellationToken cancellationToken)
        {
            if (await IsExistingFileValidAsync(file, cancellationToken))
                return file.Size > 0 ? file.Size : new FileInfo(file.TargetPath).Length;

            var directory = Path.GetDirectoryName(file.TargetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var temporaryPath = $"{file.TargetPath}.sft-download";

            var resolvedUrl = DownloadSourceProvider.Resolve(file.Url);
            var fallbackUrl = DownloadSourceProvider.ResolveFallback(file.Url);
            var urls = fallbackUrl is not null &&
                       !string.Equals(resolvedUrl, fallbackUrl, StringComparison.OrdinalIgnoreCase)
                ? new[] { resolvedUrl, fallbackUrl }
                : new[] { resolvedUrl };

            Exception? lastException = null;
            foreach (var url in urls)
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var attemptBytes = new AttemptBytes();
                    try
                    {
                        await DownloadToTemporaryAsync(url, temporaryPath, attemptBytes, addProgressBytes, reportProgress, cancellationToken);
                        ValidateTemporaryFile(file, temporaryPath);
                        File.Move(temporaryPath, file.TargetPath, overwrite: true);
                        return 0;
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // 回滚本次尝试已累计的字节，避免重试导致进度双计
                        var written = Interlocked.Read(ref attemptBytes.Value);
                        if (written > 0)
                        {
                            try { addProgressBytes(-written); } catch { }
                        }
                        lastException = exception;
                        TryDeleteFile(temporaryPath);
                    }
                }
            }

            throw lastException ?? new InvalidOperationException($"下载失败：{file.DisplayName}");
        }

        /// <summary>记录单次尝试已写入的字节数（失败重试时用于回滚进度）。</summary>
        private sealed class AttemptBytes
        {
            public long Value;
        }

        private static async Task DownloadToTemporaryAsync(
            string url, string temporaryPath, AttemptBytes attemptBytes,
            Action<long> addProgressBytes, Action reportProgress,
            CancellationToken cancellationToken)
        {
            using var response = await DownloadClient.GetAsync(
                ValidateHttpsUrl(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                Interlocked.Add(ref attemptBytes.Value, read);
                addProgressBytes(read);
                reportProgress();
            }
        }

        /// <summary>校验临时文件：sha1 匹配 + 字节数与清单一致，失败抛 InvalidDataException。</summary>
        private static void ValidateTemporaryFile(DownloadFile file, string temporaryPath)
        {
            if (!MatchesSha1(temporaryPath, file.Sha1))
                throw new InvalidDataException($"下载文件校验失败：{file.DisplayName}");
            if (file.Size > 0 && new FileInfo(temporaryPath).Length != file.Size)
                throw new InvalidDataException($"下载文件大小不匹配：{file.DisplayName}");
        }

        private static async Task<bool> IsExistingFileValidAsync(DownloadFile file, CancellationToken cancellationToken)
        {
            if (!File.Exists(file.TargetPath))
                return false;
            if (file.Size > 0 && new FileInfo(file.TargetPath).Length != file.Size)
                return false;
            return await Task.Run(() => MatchesSha1(file.TargetPath, file.Sha1), cancellationToken);
        }

        private static bool MatchesSha1(string path, string? expectedSha1)
        {
            if (string.IsNullOrWhiteSpace(expectedSha1))
                return true;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            var hash = SHA1.HashData(stream);
            return string.Equals(
                Convert.ToHexString(hash),
                expectedSha1,
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task WriteAllBytesAtomicallyAsync(
            string path, byte[] bytes, CancellationToken cancellationToken)
        {
            var temporaryPath = $"{path}.sft-download";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                File.Move(temporaryPath, path, overwrite: true);
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }
        }

        private static Uri ValidateHttpsUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"下载地址不是有效的 HTTPS URL：{url}");
            }
            return uri;
        }

        private static double CalculateSpeed(long completedBytes, Stopwatch stopwatch) =>
            stopwatch.Elapsed.TotalSeconds <= 0
                ? 0
                : completedBytes / stopwatch.Elapsed.TotalSeconds;

        private static void Report(
            IProgress<MinecraftInstallProgress>? progress, int stageIndex, string stageName, string detail,
            long completedBytes, long totalBytes, long completedFiles, long totalFiles, double speed) =>
            progress?.Report(new MinecraftInstallProgress(
                stageIndex, stageName, detail, completedBytes, totalBytes,
                (int)completedFiles, (int)totalFiles, speed));

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 残留的临时文件可被下一次修复性下载覆盖。
            }
        }

        /// <summary>跨阶段共享的安装计数器（并发更新，Interlocked 保证可见性）。</summary>
        private sealed class InstallCounters
        {
            private long _completedBytes;
            private long _networkBytes;
            private long _completedFiles;
            private long _totalBytes;
            private long _totalFiles;

            public long CompletedBytes => Volatile.Read(ref _completedBytes);
            public long NetworkBytes => Volatile.Read(ref _networkBytes);
            public long CompletedFiles => Volatile.Read(ref _completedFiles);
            public long TotalBytes => Volatile.Read(ref _totalBytes);
            public long TotalFiles => Volatile.Read(ref _totalFiles);

            public void AddProgressBytes(long value)
            {
                Interlocked.Add(ref _networkBytes, value);
                Interlocked.Add(ref _completedBytes, value);
            }

            public void AddCompleted(long value) => Interlocked.Add(ref _completedBytes, value);
            public void IncrementCompletedFiles() => Interlocked.Increment(ref _completedFiles);
            public void AddTotalBytes(long value) => Interlocked.Add(ref _totalBytes, value);
            public void AddTotalFiles(int value) => Interlocked.Add(ref _totalFiles, value);
        }

        internal sealed record DownloadFile(string Url, string TargetPath, string? Sha1, long Size, string DisplayName);

        #endregion
    }

    /// <summary>
    /// 版本 JSON 规则评估：按 os / arch / features 判断库条目是否适用于当前系统。
    /// </summary>
    internal static class MinecraftRuleEvaluator
    {
        public static Dictionary<string, bool> CreateDefaultFeatures(bool hasCustomResolution = false) =>
            new(StringComparer.Ordinal)
            {
                ["has_custom_resolution"] = hasCustomResolution,
                ["is_demo_user"] = false,
                ["has_quick_plays_support"] = false,
                ["is_quick_play_singleplayer"] = false,
                ["is_quick_play_multiplayer"] = false,
                ["is_quick_play_realms"] = false
            };

        public static bool IsAllowed(JsonElement item, IReadOnlyDictionary<string, bool> features)
        {
            if (!item.TryGetProperty("rules", out var rules) ||
                rules.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            var allowed = false;
            foreach (var rule in rules.EnumerateArray())
            {
                if (!Matches(rule, features))
                    continue;

                allowed = rule.TryGetProperty("action", out var action) &&
                          action.GetString() == "allow";
            }

            return allowed;
        }

        private static bool Matches(JsonElement rule, IReadOnlyDictionary<string, bool> features)
        {
            if (rule.TryGetProperty("os", out var os) && !MatchesOperatingSystem(os))
                return false;

            if (!rule.TryGetProperty("features", out var requiredFeatures) ||
                requiredFeatures.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            foreach (var feature in requiredFeatures.EnumerateObject())
            {
                var required = feature.Value.ValueKind == JsonValueKind.True;
                if (!features.TryGetValue(feature.Name, out var actual))
                    actual = false;
                if (actual != required)
                    return false;
            }

            return true;
        }

        private static bool MatchesOperatingSystem(JsonElement os)
        {
            if (os.TryGetProperty("name", out var name) &&
                !string.Equals(name.GetString(), GetOperatingSystemName(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (os.TryGetProperty("arch", out var arch) &&
                !MatchesArchitecture(arch.GetString()))
            {
                return false;
            }

            return true;
        }

        public static string GetOperatingSystemName()
        {
            if (OperatingSystem.IsWindows()) return "windows";
            if (OperatingSystem.IsMacOS()) return "osx";
            return "linux";
        }

        private static bool MatchesArchitecture(string? expected)
        {
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            var actual = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x86_64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "aarch64",
                _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
            };

            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ||
                   expected == "x64" && actual == "x86_64" ||
                   expected == "arm64" && actual == "aarch64";
        }
    }
}
