using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SFTLauncher.Utils
{
    /// <summary>
    /// Modrinth API v2 客户端：搜索资源、查询版本列表、下载文件。
    /// 实现参考 NyaLauncher（https://github.com/redstore-noob/NyaLauncher-avalonia）的 Modrinth 模块。
    /// </summary>
    public class ModrinthApi
    {
        private const string BaseUrl = "https://api.modrinth.com/v2";

        /// <summary>API 请求客户端（搜索 / 版本查询）。</summary>
        private static readonly HttpClient ApiClient = new() { Timeout = TimeSpan.FromSeconds(100) };

        /// <summary>
        /// 下载客户端。Modrinth CDN 文件走 Cloudflare，缺失 User-Agent 会被拒绝；
        /// 超时设长是因为 HttpClient.Timeout 会连带取消响应流的读取。
        /// </summary>
        private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        static ModrinthApi()
        {
            ApiClient.DefaultRequestHeaders.UserAgent.ParseAdd("SFTLauncher/3.0");
            DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("SFTLauncher/3.0");
        }

        #region 数据模型

        /// <summary>Modrinth 搜索结果条目（/search 的 hit）。</summary>
        public class ProjectInfo
        {
            [JsonPropertyName("project_id")]
            public string ProjectId { get; set; } = string.Empty;

            [JsonPropertyName("slug")]
            public string Slug { get; set; } = string.Empty;

            [JsonPropertyName("title")]
            public string Title { get; set; } = string.Empty;

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("author")]
            public string Author { get; set; } = string.Empty;

            /// <summary>分类列表（其中混有加载器名，如 fabric / forge）。</summary>
            [JsonPropertyName("categories")]
            public List<string> Categories { get; set; } = new();

            [JsonPropertyName("project_type")]
            public string ProjectType { get; set; } = string.Empty;

            [JsonPropertyName("downloads")]
            public long Downloads { get; set; }

            [JsonPropertyName("follows")]
            public long Follows { get; set; }

            [JsonPropertyName("icon_url")]
            public string IconUrl { get; set; } = string.Empty;

            [JsonPropertyName("versions")]
            public List<string> Versions { get; set; } = new();

            [JsonPropertyName("date_modified")]
            public DateTime? DateModified { get; set; }

            /// <summary>网页地址。</summary>
            [JsonIgnore]
            public string Url => $"https://modrinth.com/{ProjectType}/{Slug}";

            /// <summary>格式化下载量（如 236.8M）。</summary>
            [JsonIgnore]
            public string DownloadsDisplay => Downloads >= 1_000_000
                ? $"{Downloads / 1_000_000.0:F1}M 下载"
                : Downloads >= 1_000
                    ? $"{Downloads / 1_000.0:F1}K 下载"
                    : $"{Downloads} 下载";

            /// <summary>项目类型的中文显示名。</summary>
            [JsonIgnore]
            public string TypeDisplay => ProjectType switch
            {
                "mod" => "Mod",
                "modpack" => "整合包",
                "shader" => "光影包",
                "resourcepack" => "材质包",
                _ => ProjectType
            };

            /// <summary>项目类型对应的图标。</summary>
            [JsonIgnore]
            public string TypeIcon => ProjectType switch
            {
                "mod" => "🧩",
                "modpack" => "📦",
                "shader" => "☀️",
                "resourcepack" => "🎨",
                _ => "📄"
            };

            /// <summary>支持的加载器（从 categories 中筛出，如 "Fabric / Forge"）。</summary>
            [JsonIgnore]
            public string LoadersDisplay
            {
                get
                {
                    var loaders = Categories
                        .Where(c => KnownLoaders.ContainsKey(c))
                        .Select(c => KnownLoaders[c])
                        .Distinct()
                        .ToList();
                    return loaders.Count > 0 ? string.Join(" / ", loaders) : "通用";
                }
            }
        }

        /// <summary>Modrinth 版本条目（/project/{id}/version 的元素）。</summary>
        public class VersionInfo
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("project_id")]
            public string ProjectId { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("version_number")]
            public string VersionNumber { get; set; } = string.Empty;

            [JsonPropertyName("changelog")]
            public string? Changelog { get; set; }

            [JsonPropertyName("game_versions")]
            public List<string> GameVersions { get; set; } = new();

            [JsonPropertyName("loaders")]
            public List<string> Loaders { get; set; } = new();

            [JsonPropertyName("date_published")]
            public DateTime DatePublished { get; set; }

            [JsonPropertyName("downloads")]
            public long Downloads { get; set; }

            /// <summary>release / beta / alpha。</summary>
            [JsonPropertyName("version_type")]
            public string VersionType { get; set; } = string.Empty;

            [JsonPropertyName("files")]
            public List<VersionFileInfo> Files { get; set; } = new();

            /// <summary>主文件（primary 优先，否则取第一个）。</summary>
            [JsonIgnore]
            public VersionFileInfo? PrimaryFile =>
                Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();

            [JsonIgnore]
            public string DisplayName => string.IsNullOrWhiteSpace(Name) ? VersionNumber : Name;

            [JsonIgnore]
            public string DateDisplay => DatePublished.ToLocalTime().ToString("yyyy-MM-dd");

            /// <summary>加载器显示名（如 "Fabric"）。</summary>
            [JsonIgnore]
            public string LoaderDisplay
            {
                get
                {
                    var loaders = Loaders
                        .Where(l => KnownLoaders.ContainsKey(l))
                        .Select(l => KnownLoaders[l])
                        .ToList();
                    return loaders.Count > 0 ? string.Join(" / ", loaders) : string.Join(" / ", Loaders);
                }
            }

            /// <summary>支持的 MC 版本（最多展示 3 个）。</summary>
            [JsonIgnore]
            public string GameVersionsDisplay =>
                GameVersions.Count > 0 ? string.Join(", ", GameVersions.Take(3)) : "";

            /// <summary>ComboBox 显示文本。</summary>
            public override string ToString() =>
                $"{DisplayName}（{LoaderDisplay} · {GameVersionsDisplay} · {PrimaryFile?.SizeDisplay}）";
        }

        /// <summary>版本对应的单个可下载文件。</summary>
        public class VersionFileInfo
        {
            [JsonPropertyName("url")]
            public string Url { get; set; } = string.Empty;

            [JsonPropertyName("filename")]
            public string Filename { get; set; } = string.Empty;

            /// <summary>文件大小（字节）。</summary>
            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("primary")]
            public bool Primary { get; set; }

            [JsonIgnore]
            public string SizeDisplay => Size switch
            {
                >= 1048576 => $"{Size / 1048576.0:0.1} MB",
                >= 1024 => $"{Size / 1024.0:0.0} KB",
                _ => $"{Size} B"
            };
        }

        /// <summary>下载进度（字节 + 百分比）。</summary>
        public class DownloadProgress
        {
            public DownloadProgress(long downloadedBytes, long totalBytes)
            {
                DownloadedBytes = downloadedBytes;
                TotalBytes = totalBytes;
            }

            public long DownloadedBytes { get; }

            /// <summary>总字节数；服务器未返回 Content-Length 时为 -1。</summary>
            public long TotalBytes { get; }

            public double Percent => TotalBytes > 0 ? DownloadedBytes * 100.0 / TotalBytes : 0;
        }

        private class SearchResult
        {
            [JsonPropertyName("hits")]
            public List<ProjectInfo> Hits { get; set; } = new();
        }

        /// <summary>加载器名 → 显示名。</summary>
        private static readonly Dictionary<string, string> KnownLoaders = new()
        {
            ["fabric"] = "Fabric",
            ["forge"] = "Forge",
            ["neoforge"] = "NeoForge",
            ["quilt"] = "Quilt",
            ["rift"] = "Rift",
            ["liteloader"] = "LiteLoader",
            ["modloader"] = "ModLoader"
        };

        #endregion

        #region 搜索

        /// <summary>
        /// 搜索 Modrinth 资源。
        /// </summary>
        /// <param name="query">关键词，可为空（空 = 浏览全部）。</param>
        /// <param name="projectType">项目类型：mod / modpack / shader / resourcepack。</param>
        /// <param name="gameVersion">按 MC 版本过滤（如 "1.21.1"），null 或空 = 不过滤。</param>
        /// <param name="limit">返回数量上限。</param>
        /// <param name="index">排序：relevance / downloads / follows / updated / newest。</param>
        public async Task<List<ProjectInfo>> SearchAsync(
            string query = "", string projectType = "mod",
            string? gameVersion = null, int limit = 20, string index = "relevance")
        {
            // facets 语法：外层数组之间为 AND，单个内层数组内为 OR，
            // 因此 project_type 与 versions 必须各自成组，否则会变成 OR 过滤
            var facetGroups = new List<string> { $"[\"project_type:{projectType}\"]" };
            if (!string.IsNullOrWhiteSpace(gameVersion))
                facetGroups.Add($"[\"versions:{gameVersion}\"]");

            var facets = Uri.EscapeDataString($"[{string.Join(",", facetGroups)}]");
            var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? "")}&facets={facets}&limit={limit}&index={index}";

            var json = await ApiClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<SearchResult>(json);
            return result?.Hits ?? new List<ProjectInfo>();
        }

        #endregion

        #region 版本查询

        /// <summary>
        /// 获取指定项目的版本列表，可按 MC 版本和加载器过滤，按发布时间倒序。
        /// </summary>
        /// <param name="projectId">项目 ID 或 slug（如 "AANobbMI" / "sodium"）。</param>
        /// <param name="gameVersion">按 MC 版本过滤，null 或空 = 不过滤。</param>
        /// <param name="loader">按加载器过滤（fabric / forge / neoforge / quilt），null 或空 = 不过滤。</param>
        public async Task<List<VersionInfo>> GetVersionsAsync(
            string projectId, string? gameVersion = null, string? loader = null)
        {
            var url = $"{BaseUrl}/project/{projectId}/version";
            var queryParams = new List<string>();
            if (!string.IsNullOrWhiteSpace(gameVersion))
                queryParams.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");
            if (!string.IsNullOrWhiteSpace(loader))
                queryParams.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");
            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            var json = await ApiClient.GetStringAsync(url);
            var versions = JsonSerializer.Deserialize<List<VersionInfo>>(json) ?? new List<VersionInfo>();
            return versions.OrderByDescending(v => v.DatePublished).ToList();
        }

        /// <summary>
        /// 分段数值比较 MC 版本号（如 1.10.2 &gt; 1.9.4），用于版本过滤列表排序。
        /// </summary>
        /// <returns>大于 0 表示 a 更新，小于 0 表示 b 更新，0 表示相同。</returns>
        public static int CompareVersionStrings(string? a, string? b)
        {
            if (a is null) return b is null ? 0 : -1;
            if (b is null) return 1;
            var aParts = a.Split('.', '-', '_');
            var bParts = b.Split('.', '-', '_');
            var length = Math.Max(aParts.Length, bParts.Length);
            for (var i = 0; i < length; i++)
            {
                var aPart = i < aParts.Length ? aParts[i] : "0";
                var bPart = i < bParts.Length ? bParts[i] : "0";
                if (int.TryParse(aPart, out var aNum) && int.TryParse(bPart, out var bNum))
                {
                    var diff = aNum.CompareTo(bNum);
                    if (diff != 0) return diff;
                }
                else
                {
                    var diff = string.CompareOrdinal(aPart, bPart);
                    if (diff != 0) return diff;
                }
            }
            return 0;
        }

        #endregion

        #region 下载

        /// <summary>
        /// 下载文件到指定路径。先写入临时文件（&lt;目标路径&gt;.downloading），
        /// 全部完成后再移动到目标路径；失败或取消时清理临时文件。
        /// </summary>
        /// <param name="url">文件下载 URL（来自 VersionFileInfo.Url）。</param>
        /// <param name="targetPath">保存的完整文件路径（含文件名）。</param>
        /// <param name="progress">进度回调（已下载字节 / 总字节）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task DownloadFileAsync(
            string url, string targetPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("下载地址不能为空", nameof(url));
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("保存路径不能为空", nameof(targetPath));

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempPath = targetPath + ".downloading";
            try
            {
                using var response = await DownloadClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloaded = 0;
                var buffer = new byte[64 * 1024];

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    64 * 1024, useAsync: true);

                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    progress?.Report(new DownloadProgress(downloaded, totalBytes));
                }
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // 清理失败不影响主流程，残留的 .downloading 文件会被下次下载覆盖。
            }
        }

        #endregion
    }
}
