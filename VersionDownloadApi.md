# Minecraft 实例版本下载功能调用文档

SFT-Launcher 的 Minecraft 实例版本下载功能：从 Mojang 官方元数据安装完整的、可启动的
原版实例（客户端 JAR + 依赖库 + 资源索引 + 游戏资源 + 版本 JSON）。

实现参考：[NyaLauncher-avalonia](https://github.com/redstore-noob/NyaLauncher-avalonia) 的
`MinecraftVersionInstaller` / `DownloadSourceProvider`。

## 文件结构

| 文件 | 说明 |
|------|------|
| `Utils/MinecraftVersionInstaller.cs` | 后端：版本清单获取、下载源切换、分阶段安装器 |
| `Pages/VersionDownloadPage.xaml` / `.xaml.cs` | 前端：版本选择、安装目录、进度显示、下载历史 |

不依赖任何第三方包。产物为标准 Mojang 目录结构，`MinecraftLauncher` 以
`versions/<版本>/<版本>.jar` 判定实例存在。

## 后端 API：`SFTLauncher.Utils`

### 1. 获取版本清单 — `MinecraftVersionInstaller.GetVersionsAsync`（静态）

```csharp
static Task<List<MinecraftVersionEntry>> GetVersionsAsync(CancellationToken cancellationToken = default)
```

- 返回**全部**版本（正式版 / 快照 / Beta / Alpha），按发布时间倒序；
- `IsLatestRelease == true` 的条目为当前最新正式版（前端默认选中它）；
- 对应接口：`GET /v2/mc/game/version_manifest_v2.json`（经下载源改写）。

```csharp
var versions = await MinecraftVersionInstaller.GetVersionsAsync();
var latest = versions.First(v => v.IsLatestRelease);
Console.WriteLine($"{latest.Id}（{latest.TypeDisplay} · {latest.ReleaseTime:yyyy-MM-dd}）");
```

### 2. 安装版本 — `MinecraftVersionInstaller.InstallAsync`

```csharp
Task InstallAsync(
    string versionId,
    string metadataUrl,
    string minecraftDirectory,
    IProgress<MinecraftInstallProgress>? progress = null,
    CancellationToken cancellationToken = default)
```

| 参数 | 说明 |
|------|------|
| `versionId` | 版本 ID（来自 `MinecraftVersionEntry.Id`），同时是 `versions/` 下的目录名 |
| `metadataUrl` | 版本元数据 URL（来自 `MinecraftVersionEntry.Url`） |
| `minecraftDirectory` | Minecraft 根目录，不存在自动创建 |
| `progress` | 进度回调（在创建 `Progress<T>` 的线程上下文上触发） |
| `cancellationToken` | 取消令牌 |

安装的 7 个阶段：

| 阶段 | 名称 | 内容 |
|------|------|------|
| 1 | 获取版本元数据 | 下载版本描述 JSON |
| 2 | 分析下载清单 | 解析客户端 / 依赖库 / 资源索引清单 |
| 3 | 下载游戏客户端 | → `versions/<id>/<id>.jar` |
| 4 | 下载依赖库 | 按 OS / 架构规则过滤，含 natives → `libraries/<maven 路径>` |
| 5 | 下载资源索引 | → `assets/indexes/<assetsId>.json` |
| 6 | 下载游戏资源 | 按索引 hash → `assets/objects/<hash 前 2 位>/<hash>` |
| 7 | 完成校验与安装 | 原子写入 `versions/<id>/<id>.json` |

关键行为：

- **SHA-1 校验**：每个文件下载后先校验 sha1（无 sha1 时校验大小），通过后才从
  临时文件 `<目标>.sft-download` 原子移动到位；
- **已有文件复用**：目标文件已存在且 sha1 匹配时直接复用（重复安装秒级完成，
  也用于修复缺失/损坏文件）；
- **并发下载**：单阶段内最多 8 个文件并行，进度上报按 120ms 节流；
- **失败重试**：每个文件在主源尝试 2 次，主源失败自动切换回退源再试 2 次；
- **规则过滤**：按版本 JSON 的 `rules`（os.name / os.arch / features）筛选库，
  `${arch}` 占位符按系统位数展开；旧版（1.7.x 及更早）无 `downloads` 字段时
  回退到 Maven 坐标推导；Maven 坐标重复声明的库按目标路径去重；
- **安全**：版本 ID 拒绝路径分隔符与 `.`/`..`（防路径穿越）；库文件目标路径
  强制位于 Minecraft 目录内；所有下载地址强制 HTTPS。

异常：`ArgumentException`（参数为空 / 版本 ID 不安全）、`InvalidDataException`
（清单损坏 / SHA1 校验失败 / URL 非 HTTPS）、`HttpRequestException`（网络/HTTP 错误）、
`OperationCanceledException`（取消）。

### 3. 下载源切换 — `DownloadSourceProvider`（静态）

所有下载 URL 以官方源为准书写，运行时按 `Active` 源改写域名；请求失败自动回退
`Fallback` 源。

```csharp
DownloadSourceProvider.Active   // 当前源，默认 Official
DownloadSourceProvider.Fallback // 回退源，默认 BMCLAPI；设为 null 关闭回退
```

| 源 | 清单 / 元数据 | 依赖库 | 资源文件 |
|----|--------------|--------|----------|
| `DownloadSources.Official` | piston-meta.mojang.com | libraries.minecraft.net | resources.download.minecraft.net |
| `DownloadSources.Bmcl`（国内镜像） | bmclapi2.bangbang93.com | bmclapi2.bangbang93.com/maven | bmclapi2.bangbang93.com/assets |

```csharp
// 切换为国内镜像优先，官方源回退
DownloadSourceProvider.Active = DownloadSources.Bmcl;
DownloadSourceProvider.Fallback = DownloadSources.Official;
```

未识别的域名（如 `launcher.mojang.com`）原样直连。切换只影响后续请求，可随时改。

## 数据模型

### `MinecraftVersionEntry`（版本清单条目）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Id` | `string` | 版本 ID（安装用） |
| `Type` | `string` | `release` / `snapshot` / `old_beta` / `old_alpha` |
| `Url` | `string` | 版本元数据 URL（安装用） |
| `ReleaseTime` | `DateTime` | 发布时间 |
| `IsLatestRelease` | `bool` | 是否为最新正式版 |
| `TypeDisplay` / `TypeIcon` | `string` | 中文类型名 / 图标 emoji |
| `ToString()` | | `"1.21.1（正式版 · 2026-01-01）"`，可直接作 ComboBox 显示 |

### `MinecraftInstallProgress`（安装进度）

| 属性 | 类型 | 说明 |
|------|------|------|
| `StageIndex` / `StageName` | | 当前阶段 1–7 及名称 |
| `Detail` | `string` | 当前动作描述（如"正在处理 6 个文件 · lwjgl-2.9.0.jar"） |
| `CompletedBytes` / `TotalBytes` | `long` | 已完成 / 总字节。资源阶段的总数要等索引下载完才统计，**总字节数会在安装中增长，百分比非单调** |
| `CompletedFiles` / `TotalFiles` | `int` | 文件计数 |
| `BytesPerSecond` / `SpeedDisplay` | | 速度 |
| `Percentage` | `double` | 0–100 |

## 前端界面：`Pages/VersionDownloadPage`

1. **版本选择**：打开页面自动加载完整清单（917+ 个版本），默认选中最新正式版，
   每项显示 `版本号（类型 · 发布日期）`；清单加载失败时回退到内置常用版本列表
   （此时仅展示，下载会提示需重新联网）；
2. **安装位置**：默认自动检测（`MinecraftLauncher.DetectMinecraftDirectory()`，
   兜底 `~/.minecraft`），可"浏览"修改，首次下载会记录到 `%APPDATA%\SFTLauncher\config.json`；
3. **下载进度**：进度条 + `[阶段名] 详情` + `文件 n/m · 速度`，7 阶段依次推进；
4. **停止**：真实取消（CancellationToken），取消后记录失败历史；
5. **下载历史**：页面会话内保留最近 5 条记录。

## 完整示例：安装一个版本

```csharp
var installer = new MinecraftVersionInstaller();

var versions = await MinecraftVersionInstaller.GetVersionsAsync();
var version = versions.First(v => v.Id == "1.21.1");

var progress = new Progress<MinecraftInstallProgress>(p =>
    Console.WriteLine($"[{p.StageName}] {p.Percentage:F0}% {p.Detail}"));

using var cts = new CancellationTokenSource();
await installer.InstallAsync(
    version.Id, version.Url,
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft"),
    progress, cts.Token);
```

## 接口映射

| 功能 | 端点 |
|------|------|
| 版本清单 | `GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` |
| 版本元数据 | `GET https://piston-meta.mojang.com/v1/packages/<sha1>/<id>.json` |
| 客户端 JAR | `GET https://piston-data.mojang.com/...`（元数据 `downloads.client.url`） |
| 依赖库 | `GET https://libraries.minecraft.net/<maven 路径>` |
| 资源索引 / 资源 | `GET https://resources.download.minecraft.net/<hash 前 2 位>/<hash>` |

Mojang 文档：https://minecraft.wiki/w/Client.json
