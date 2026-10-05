# Modrinth 功能调用文档

SFT-Launcher 的 Modrinth 资源库功能，基于 [Modrinth API v2](https://docs.modrinth.com/)（免费开源，无需 API Key）。

实现参考：[NyaLauncher-avalonia](https://github.com/redstore-noob/NyaLauncher-avalonia) 的 Modrinth 模块。

## 文件结构

| 文件 | 说明 |
|------|------|
| `Utils/ModrinthApi.cs` | 后端 API 客户端：搜索、版本查询、文件下载 |
| `Pages/ModsPage.xaml` / `.xaml.cs` | 前端界面：搜索栏、筛选栏、资源列表、下载面板 |

不依赖任何第三方包，序列化使用内置 `System.Text.Json`。

## 后端 API：`SFTLauncher.Utils.ModrinthApi`

```csharp
var api = new ModrinthApi();
```

### 1. 搜索资源 — `SearchAsync`

```csharp
Task<List<ProjectInfo>> SearchAsync(
    string query = "",
    string projectType = "mod",
    string? gameVersion = null,
    int limit = 20,
    string index = "relevance")
```

| 参数 | 说明 |
|------|------|
| `query` | 搜索关键词，可为空字符串（空 = 浏览全部） |
| `projectType` | 项目类型：`mod` / `modpack`（整合包）/ `shader`（光影）/ `resourcepack`（材质包） |
| `gameVersion` | 按 MC 版本过滤（如 `"1.21.1"`），`null` 或空 = 不过滤 |
| `limit` | 返回数量上限（默认 20） |
| `index` | 排序：`relevance`（相关度）/ `downloads`（下载量）/ `follows`（关注）/ `updated`（更新时间）/ `newest`（最新发布） |

对应接口：`GET /v2/search`。facets 按外层 AND、内层 OR 的语法分组合并 `project_type` 与 `versions` 条件。

```csharp
// 搜索支持 1.21.1 的 Fabric mod，按下载量排序
var mods = await api.SearchAsync("performance", "mod", "1.21.1", 20, "downloads");
foreach (var m in mods)
    Console.WriteLine($"{m.TypeIcon} {m.Title} - {m.Author} - {m.DownloadsDisplay} - {m.LoadersDisplay}");
```

### 2. 查询版本列表 — `GetVersionsAsync`

```csharp
Task<List<VersionInfo>> GetVersionsAsync(
    string projectId,
    string? gameVersion = null,
    string? loader = null)
```

| 参数 | 说明 |
|------|------|
| `projectId` | 项目 ID 或 slug（如 `"AANobbMI"` 或 `"sodium"`） |
| `gameVersion` | 按 MC 版本过滤，`null` 或空 = 不过滤 |
| `loader` | 按加载器过滤：`fabric` / `forge` / `neoforge` / `quilt` 等，`null` 或空 = 不过滤 |

返回版本列表，按发布时间倒序。对应接口：`GET /v2/project/{id}/version`。

```csharp
var versions = await api.GetVersionsAsync("AANobbMI", "1.21.1", "fabric");
var latest = versions[0];
Console.WriteLine($"{latest.DisplayName} ({latest.LoaderDisplay}, {latest.DateDisplay}, {latest.PrimaryFile?.SizeDisplay})");
```

### 3. 下载文件 — `DownloadFileAsync`

```csharp
Task DownloadFileAsync(
    string url,
    string targetPath,
    IProgress<DownloadProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

| 参数 | 说明 |
|------|------|
| `url` | 下载地址，来自 `VersionFileInfo.Url` |
| `targetPath` | 保存的完整文件路径（含文件名），目录不存在会自动创建 |
| `progress` | 进度回调（已在调用线程同步上下文上触发，如 UI 线程） |
| `cancellationToken` | 取消令牌 |

行为：

- 先写入临时文件 `<目标路径>.downloading`，完成后原子移动到目标路径；
- 下载失败或取消时自动清理临时文件；
- `HttpClient.Timeout` 为 10 分钟（下载客户端与 API 客户端分离，互不影响）；
- 请求均携带 `User-Agent: SFTLauncher/3.0`（Modrinth CDN 要求，缺失会被拒绝）。

异常：`ArgumentException`（参数为空）、`HttpRequestException`（HTTP 错误，如 404）、`OperationCanceledException`（取消）。

```csharp
var file = latest.PrimaryFile!;
var progress = new Progress<DownloadProgress>(p =>
    Console.Write($"\r{p.Percent:F0}%  {p.DownloadedBytes}/{p.TotalBytes}"));

using var cts = new CancellationTokenSource();
await api.DownloadFileAsync(file.Url, Path.Combine(modsDir, file.Filename), progress, cts.Token);
```

### 4. 版本号比较 — `CompareVersionStrings`（静态）

```csharp
static int CompareVersionStrings(string? a, string? b)
```

分段数值比较 MC 版本号（避免字符串比较把 `1.9.4` 排在 `1.10.2` 之后）。常用于把项目的可用 MC 版本从新到旧排序：

```csharp
var gameVersions = versions
    .SelectMany(v => v.GameVersions)
    .Distinct()
    .OrderByDescending(v => v, Comparer<string>.Create(ModrinthApi.CompareVersionStrings));
```

## 数据模型

### `ProjectInfo`（搜索结果条目）

| 属性 | 类型 | 说明 |
|------|------|------|
| `ProjectId` | `string` | 项目 ID（查询版本用） |
| `Slug` | `string` | URL 标识 |
| `Title` / `Description` / `Author` | `string` | 标题 / 简介 / 作者 |
| `Categories` | `List<string>` | 分类（其中混有加载器名） |
| `ProjectType` | `string` | `mod` / `modpack` / `shader` / `resourcepack` |
| `Downloads` / `Follows` | `long` | 下载量 / 关注数 |
| `IconUrl` | `string` | 图标地址 |
| `Versions` | `List<string>` | 支持的 MC 版本列表 |
| `DateModified` | `DateTime?` | 最近更新时间 |
| `Url` | `string` | 网页地址（计算属性） |
| `DownloadsDisplay` | `string` | 格式化下载量，如 `"236.8M 下载"` |
| `TypeDisplay` / `TypeIcon` | `string` | 类型中文名 / 图标 emoji |
| `LoadersDisplay` | `string` | 支持的加载器，如 `"Fabric / NeoForge"` |

### `VersionInfo`（版本条目）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 版本 ID |
| `Name` / `VersionNumber` | `string` | 版本名 / 版本号 |
| `Changelog` | `string?` | 更新日志 |
| `GameVersions` | `List<string>` | 支持的 MC 版本 |
| `Loaders` | `List<string>` | 支持的加载器（小写） |
| `DatePublished` | `DateTime` | 发布时间 |
| `VersionType` | `string` | `release` / `beta` / `alpha` |
| `Files` | `List<VersionFileInfo>` | 文件列表 |
| `PrimaryFile` | `VersionFileInfo?` | 主文件（primary 优先，否则第一个） |
| `DisplayName` / `DateDisplay` / `LoaderDisplay` / `GameVersionsDisplay` | `string` | 展示用计算属性 |
| `ToString()` | | ComboBox 友好文本：`名称（加载器 · MC版本 · 大小）` |

### `VersionFileInfo`（版本文件）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Url` | `string` | CDN 下载地址 |
| `Filename` | `string` | 文件名（保存时使用） |
| `Size` | `long` | 字节数 |
| `Primary` | `bool` | 是否为主文件 |
| `SizeDisplay` | `string` | 格式化大小，如 `"1.5 MB"` |

### `DownloadProgress`（下载进度）

| 属性 | 类型 | 说明 |
|------|------|------|
| `DownloadedBytes` / `TotalBytes` | `long` | 已下载 / 总字节（总字节未知时为 -1） |
| `Percent` | `double` | 百分比 0–100 |

## 前端界面：`Pages/ModsPage`

界面流程（全部走上面的后端 API）：

1. **搜索区**：关键词输入框 + 资源类型 / MC 版本 / 排序三个下拉框，点击"搜索"调用 `SearchAsync`；
2. **资源列表**：每张卡片显示类型图标、标题、简介、作者、下载量、加载器、更新时间，以及"下载"按钮；
3. **下载面板**：点击卡片"下载"后展开——
   - 调用 `GetVersionsAsync(projectId)` 获取全部版本；
   - "游戏版本" / "加载器"两个下拉框级联过滤版本列表；
   - 选择版本（默认最新）和保存目录（默认 `%APPDATA%\.minecraft\mods`，可"浏览"修改）；
   - 点击"⬇ 下载"调用 `DownloadFileAsync`，进度条实时更新，支持"取消"；
   - ✕ 关闭面板（下载中关闭会同时取消下载）。

## 错误处理约定

- 后端方法**不吞异常**：网络错误、HTTP 错误、参数错误原样抛出，由调用方决定如何提示（UI 层统一 try/catch + 状态文本/MessageBox）；
- 搜索无结果时返回**空列表**而非异常；
- `PrimaryFile` 在版本无文件时为 `null`，下载前需判空。

## 完整示例：把一个 mod 下载到 mods 目录

```csharp
var api = new ModrinthApi();

// 1. 搜索
var mods = await api.SearchAsync("sodium", "mod");
var mod = mods[0];

// 2. 选版本（1.21.1 + fabric）
var versions = await api.GetVersionsAsync(mod.ProjectId, "1.21.1", "fabric");
var file = versions[0].PrimaryFile!;

// 3. 下载（带进度与取消）
var modsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    ".minecraft", "mods");
using var cts = new CancellationTokenSource();
await api.DownloadFileAsync(file.Url, Path.Combine(modsDir, file.Filename),
    new Progress<DownloadProgress>(p => Console.WriteLine($"{p.Percent:F0}%")), cts.Token);
```

## 接口映射

| 功能 | Modrinth API v2 端点 |
|------|----------------------|
| `SearchAsync` | `GET /v2/search` |
| `GetVersionsAsync` | `GET /v2/project/{id}/version` |
| `DownloadFileAsync` | `GET https://cdn.modrinth.com/...`（文件直链，来自版本数据） |

文档：https://docs.modrinth.com/#tag/search/operation/searchProjects
