# AGENTS.md — DdraigBench

> 面向在此仓库工作的编码 agent / 协作者。定位：单一事实来源（SSOT）。
> 规则：架构冲突时以本文档为准；文档过时则先改文档再改代码。
> 依赖事实全部经 NuGet nuspec / GitHub API 实查（基线 2026-09-18），非记忆。

---

## 1. 产品定义

**DdraigBench** = Avalonia + FreeSql 的跨平台数据库 GUI 客户端（对标 Navicat / DBeaver）。
名字：威尔士语 *y ddraig*（红龙 Y Ddraig Goch → 亚瑟 → Avalon → Avalonia）+ Bench。双写 dd 是威尔士语定冠词后的软化变位，拼写必须保持 **Ddraig**，勿"纠正"为 Draig。

技术基线（2026-10-08 按 csproj 实查校正，勿再写记忆里的旧版本）：net10.0 · Avalonia 12.1.3 · FluentAvaloniaUI 3.1.0（Fluent/WinUI 主题）· FluentIcons.Avalonia.Fluent 2.1.343 · ReactiveUI.Avalonia 12.1.6（→ ReactiveUI 24.2.0）· FreeSql 3.5.311 · xunit.v3 4.0.1。

**架构总原则：FreeSql 只当「方言引擎 + 元数据引擎」，不当 ORM 用。** FreeSql 负责 22 方言的元数据 SQL / 类型映射 / 分页；我们负责 GUI 语义（会话、树、网格、编辑器）。FreeSql 覆盖不到的（触发器/函数/分区/事件元数据），抄对应 Provider 的 MIT 源码自建 dialect 层补齐。

```
┌──────────────────────────────────────────────┐
│ DdraigBench (Avalonia 壳)                     │
│  Views/ViewModels/Controls                    │
└──────────────┬───────────────────────────────┘
               │ 仅依赖接口（Splat/容器注入）
┌──────────────▼───────────────────────────────┐
│ DdraigBench.Core (无 Avalonia 引用, 可单测)    │
│  Connections / Sessions / Metadata / Query    │
│  Dialects (每库一个元数据插件)                 │
└──────────────┬───────────────────────────────┘
               │ IFreeSql (DbFirst + Ado)
        FreeSql.Provider.*  →  底层 ADO.NET 驱动
```

---

## 2. NuGet 依赖分析（新工程实查）

### 2.1 主工程 DdraigBench.csproj 现状

| 包 | 版本 | 作用 | 判定 |
|---|---|---|---|
| Avalonia / .Desktop / .Themes.Fluent / .Fonts.Inter | 12.1.3 | 框架四件套 | ✅ 统一 12.1.x |
| **FluentAvaloniaUI** | 3.1.0 | WinUI3 风格控件 + Fluent v2 主题（取代 `<FluentTheme/>`） | ✅ MIT，nuspec 钉 Avalonia ≥12.1.0；详见 §2.6 |
| **FluentIcons.Avalonia.Fluent** | 2.1.343 | Fluent UI System Icons 图标控件（配对 FluentAvalonia） | ✅ MIT，nuspec 钉 FluentAvaloniaUI ≥3.0.0；详见 §2.6 |
| Avalonia.Controls.DataGrid | 12.1.2 | 结果集网格（D1） | ✅ 显式引用，家族统一 12.1.x |
| **Avalonia.AvaloniaEdit** | 12.0.0 | SQL 编辑器（M3-3，官方 AvaloniaUI 移植） | ✅ MIT，nuspec 钉 Avalonia ≥12.0.0；详见 §2.7 |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 调试面板 | ✅ 已正确限定 Debug-only |
| **FreeSql** | 3.5.311 | ORM 核心 | ✅ **零传递依赖**（nuspec 实查为空），GPL 风险已随 FreeSql.All 移除而消除 |
| **FreeSql.Provider.Sqlite** | 3.5.311 | SQLite 方言 + 驱动（M1 黄金通道）→ 钉 System.Data.SQLite.Core 1.0.119（自带原生 e_sqlite3） | ✅ MIT；主工程 / Core / 测试工程三处均引用 |
| **FreeSql.Provider.MySqlConnector** | 3.5.311 | MySQL/MariaDB 方言 + 驱动（M2）→ 钉 MySqlConnector 2.5.0 | ✅ MIT；主/测/Core 三处均引用 |
| **FreeSql.Provider.PostgreSQL** | 3.5.311 | PostgreSQL 方言 + 驱动（M2）→ 钉 Npgsql.LegacyPostgis / Npgsql.NetTopologySuite 5.0.18 + Newtonsoft.Json 13.0.1 | ✅ MIT；主/测/Core 三处均引用 |
| **FreeSql.Provider.SqlServer** | 3.5.311 | SQL Server 2005+ 方言 + 驱动 → 钉 Microsoft.Data.SqlClient 5.2.2 | ✅ MIT；主/测/Core 三处均引用（M2-c，2026-10-11 靶机实测） |
| ReactiveUI.Avalonia | 12.1.6 | MVVM 桥 → ReactiveUI 24.2.0 + Splat 21.0.0 | ✅ 保留 |
| ~~ReactiveUI.Avalonia.Autofac~~ | ~~12.1.6~~ | Autofac 容器桥 → Splat.Autofac | ❌ **已移除**（2026-10-11，C5）：从未接线（Program/App 无任何 Autofac 配置），按 §2.4 D2 取 b 删包，DI 一律走 Splat 默认定位器 |

### 2.2 Provider 引入策略（M1 SQLite；M2 MySQL / PostgreSQL / SQL Server）

FreeSql 核心包不含任何驱动，连接真实数据库前必须按需引入 Provider。**按 milestone 逐个加**，当前批准清单（均为 MIT，nuspec 实查）：

```xml
<!-- M1 起步：SQLite 黄金通道（System.Data.SQLite.Core 自带原生库，零配置） -->
<PackageReference Include="FreeSql.Provider.Sqlite" Version="3.5.311" />
<!-- M2 多方言：MySQL 用 MySqlConnector 版（MIT），禁用 Provider.MySql（MySql.Data 是 GPL-2.0） -->
<PackageReference Include="FreeSql.Provider.MySqlConnector" Version="3.5.311" />
<PackageReference Include="FreeSql.Provider.PostgreSQL" Version="3.5.311" />
<PackageReference Include="FreeSql.Provider.SqlServer" Version="3.5.311" />
<!-- 后续按需评估：Dameng / KingbaseES / ClickHouse / Duckdb / Odbc 等 -->
```

**红线**：`Provider.MySql` 与 `Provider.MySqlConnector` 注册同一个 `DataType.MySql`，**不得同时安装**；仓库任何位置出现 `FreeSql.All` / `Provider.MySql` / `MySql.Data` 即视为事故。

**SQL Server 已接入（M2-c，2026-10-11）**：`FreeSql.Provider.SqlServer` 3.5.311 已在 Core/主/测试工程引用；连接对话框提供 SQL Server（默认 `Encrypt=True`、`TrustServerCertificate=False`，需自签证书时用户显式勾选信任）。基础连接/查询/表元数据已用 SQL Server 2022 Developer 靶机实测。M5-3 首批目录实现新增用户模式、DML 触发器、函数、存储过程清单及定义，以及原始视图定义；已在同一靶机上实测通过（随机 schema + 表/视图/函数/过程/触发器建删全链路）。表 DDL 仍由 `DdlGenerator` 重建；服务端分页只对顶层 `ORDER BY` 简单查询直接追加 `OFFSET/FETCH`，不包装派生表，且已在 SQL Server 2022 Developer 靶机验证；无序及带 `TOP` / `OPTION` / `FOR` / 已分页 / 多语句的查询 fail-closed，详见 §8 M2-c 与 M5-3。

### 2.3 Provider → 驱动钉版表（选型前先看）

Provider 的驱动版本被 FreeSql 钉死，上游不动则无法升级：

| Provider | 钉住的驱动 | 版本 | 备注 |
|---|---|---|---|
| Sqlite | System.Data.SQLite.Core | 1.0.119 | 自带原生 e_sqlite3，**M1 首选** |
| MySqlConnector | MySqlConnector | 2.5.0 | MySQL 标准通道（MIT） |
| PostgreSQL | Npgsql.LegacyPostgis / Npgsql.NetTopologySuite + Newtonsoft.Json | 5.0.18 / 13.0.1 | 由 Provider 传递（nuspec 实查） |
| SqlServer | Microsoft.Data.SqlClient | 5.2.2 | 老系统变体 SqlServerForSystem 钉 System.Data.SqlClient 4.9.0 |
| SqliteCore | Microsoft.Data.Sqlite.Core | 10.0.0 | ⚠️ 不带原生库，需自加 SQLitePCLRaw.bundle，**不用** |

兜底：极端场景经 `fsql.Ado.MasterPool.Get()` 拿原始 `DbConnection` 自管；或 fork Provider 换驱动（MIT 允许）。

### 2.4 待决点

- **D1 结果网格控件**：✅ **已解决**（2026-09-18）。显式引入 `Avalonia.Controls.DataGrid 12.1.2`（家族统一 12.1.x）。FluentAvaloniaUI 亦传递依赖它（≥12.1.0），显式引用同时满足"用了哪个控件就显式引用哪个包"。
- **D2 DI 容器**：✅ **已解决（2026-10-11，取 b）**——`ReactiveUI.Avalonia.Autofac` 已从 `DdraigBench.csproj` 删除（C5），服务注册一律走 Splat 默认定位器 `Locator.CurrentMutable.RegisterConstant(...)`。原备选 a) 仍保留：服务多于 ~20 个再迁 Autofac。
- **D3 主题与图标栈**：✅ **已解决**（2026-09-18）。取 FluentAvaloniaUI 3.1.0 + FluentIcons.Avalonia.Fluent 2.1.343，考查表、决策与备选见 §2.6。

### 2.5 Test 工程

**xunit.v3 4.0.1** / Test.Sdk 18.10.1 / coverlet.collector 10.1.0 ✅（2026-09-18 由 `xunit 2.9.3` 升为 `xunit.v3 4.0.1`，与 Pathfinder1eHelper 对齐）。`xunit.v3` 要求测试工程 `<OutputType>Exe</OutputType>`，并 `NoWarn xUnit1051` 以保零告警。**已增补（M1-1）**：`FreeSql` + `FreeSql.Provider.Sqlite` 3.5.311，以及对 `DdraigBench.Core` 的 ProjectReference——集成测试用临时文件 SQLite（`Path.GetTempPath`），无需 docker。另增对 `DdraigBench` 壳工程的 ProjectReference，以便纯逻辑测 ViewModel（§7，不启 Avalonia）。

> ⚠️ 测试工程是 **MTP 应用**（`xunit.v3` + `Microsoft.Testing.Platform`），必须走 MTP 模式（§9 的 `global.json`）。**2026-10-09 实测（dotnet 10.0.401）**：`dotnet test DdraigBench.slnx` 与 `dotnet test --solution DdraigBench.slnx` **都可以**（输出 MTP 的"测试运行摘要"）；早前"`dotnet test <sln>` 旧写法会报错"的说法已不成立，勿再照抄。Pathfinder1eHelper 则走 VSTest 通道（其测试工程设 `IsTestingPlatformApplication=false`）——两种通道**互斥**，本项目选 MTP + `global.json`。

另增 **`Avalonia.Headless 12.1.3`**（仅测试工程；基础包、无 xunit 耦合）+ xUnit v3 `AssemblyFixture` 启动 headless 平台，用于控件级 UI 单测（§7 / `DdraigBench.Test/TestAppFixture.cs`）。**2026-10-16 起**：fixture 在**专用线程**（`ddb-headless-ui`）上 `SetupWithoutStarting()` 并运行该线程的 `Dispatcher.UIThread.MainLoop`，测试体经 `DdraigBench.Test/TestUiThread.cs` 的 `Run/RunAsync` 编组到同一条线程——于是 `TextEditor` / `QueryTabView` / `Window`（含 `Show()`）都能真构造。

### 2.6 主题与图标栈（已定，2026-09-18 nuspec 实查）

考查对象为 NuGet 上的 Fluent 风格主题包与图标包：

| 包 | 版本 | 许可证 | 依赖的 Avalonia 线 | 判定 |
|---|---|---|---|---|
| Avalonia.Themes.Fluent | 12.1.3 | MIT | 内置（框架四件套） | 基线保留，但 `<FluentTheme/>` 元素已移除（见下） |
| **FluentAvaloniaUI**（amwx） | 3.1.0 | MIT | **≥12.1.0** ✅ | ✅ **采用**。WinUI3 控件：NavigationView / TabView / ContentDialog / SettingsExpander / TaskDialog（⚠️ **InfoBar 实测解析不到**：3.1.0 的 `FluentAvalonia.UI.Controls` 下无该类型，XAML 编译报 AVLN2000——2026-10-04 实测，故告警条用普通 `Border` 自绘） |
| **FluentIcons.Avalonia.Fluent**（davidxuang） | 2.1.343 | MIT | 钉 FluentAvaloniaUI **≥3.0.0** ✅ | ✅ **采用**。微软 Fluent UI System Icons（Regular/Filled/Color），与主题同设计语言 |
| FluentIcons.Avalonia | 2.1.343 | MIT | ≥12.0.0 ✅ | 备选：仅用内置 FluentTheme 时选它（非 `.Fluent` 变体） |
| Material.Icons.Avalonia | 3.0.2 | MIT | ≥12.0.0 ✅ | 兼容但属 Material 体系，与 Fluent 混用风格割裂，不用 |
| Lucide.Avalonia | 0.2.22 | MIT | ≥11.3.17 | 0.x 早期项目、笔画风格，非 Fluent，不用 |
| Projektanker.Icons.Avalonia | 9.6.2 | MIT | ≥11.2.8，netstandard2.0 | ⚠️ 为 Avalonia 11 编写、2025-05 后停更，**不用** |
| IconPacks.Avalonia.*（MahApps 新系列） | 2.0.0 | MIT | ≥11.0.13 | ⚠️ 仍钉 Avalonia 11 线，**不用** |

**决策与硬性规则**：

1. `<Application.Styles>` 里只有 `<sty:FluentAvaloniaTheme/>`，**禁止再放 `<FluentTheme/>` / `SimpleTheme` / 其他基础主题**（官方文档：并存会产生画刷冲突与视觉劣化）。**DataGrid 也不要额外 `StyleInclude`**——FluentAvaloniaTheme 已自带完整 DataGrid 主题（FluentAvalonia.dll 内含 `DataGridStyles`/`DataGridCellStyles`/`DataGridRowStyles`/`DataGridColumnHeaderStyles` 等）；再叠加 stock `Avalonia.Controls.DataGrid/Themes/Fluent.xaml` 会覆盖它、并在缺 `FluentTheme` 资源键时把网格渲染搞空（M1 实测）。但**FluentAvalonia 不管的控件，其自带主题必须显式引入**：`Avalonia.AvaloniaEdit` 需 `<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"/>`（官方 README 标 required，M3-3）。
2. 图标全工程只用 FluentIcons 一家，禁止混用第二套图标字体（笔画/圆角不一致 = 视感崩坏）。
3. 图标包与主题包必须配对：上 FluentAvalonia 就用 `.Fluent` 变体（它钉 FluentAvaloniaUI ≥3.0.0）；只用内置 FluentTheme 才用裸 `FluentIcons.Avalonia`。
4. `Avalonia.Themes.Fluent` 包保留在框架四件套（FluentAvaloniaTheme 未声明依赖它，属可精简项，留待后续评估，勿擅动）。
5. 主题/图标只允许出现在 `DdraigBench` 壳工程（View 层），Core 保持零 UI 依赖（§3）。
6. 已知传递依赖 `Avalonia.Controls.ColorPicker 12.1.0`（FluentAvalonia 钉 ≥12.1.0）与主族 12.1.3 存在 patch 级差，semver 兼容，暂不处理。
7. 自定义控件若派生自主题化控件（`DataGrid` 等），**必须** `protected override Type StyleKeyOverride => typeof(基类);`——否则 `ControlTheme` 按派生类型查不到，模板缺失、整块不渲染且无报错（M1 实测）。能不改派生就优先用**附加属性**（M1 的结果网格即如此，见 `Controls/ResultColumns.cs`）。

### 2.7 SQL 编辑器包（M3 候选，2026-09-18 nuspec 实查）

| 包 | 版本 | 许可证 | 依赖的 Avalonia 线 | 判定 |
|---|---|---|---|---|
| **Avalonia.AvaloniaEdit**（AvaloniaUI 官方移植） | 12.0.0 | MIT | **≥12.0.0**（net10.0） | ✅ **采用**（M3-3 已落地，与 12.1.3 兼容），仅额外依赖 `Avalonia`；需显式引入其主题（§2.6） |
| AvaloniaEdit.TextMate | 12.0.0 | MIT | 钉 Avalonia.AvaloniaEdit 12.0.0 + Avalonia ≥12.0.0 | 备选：SQL 语法高亮（会再引 TextMateSharp 2.0.3 + TextMateSharp.Grammars 2.0.3），留待后续打磨 |

> §8 要求"引入前先验证与 Avalonia 12.1 兼容版本"，本节即该核查结果。

### 2.8 口令/密钥库包（M4-4：2026-10-01 Linux 落地；2026-10-04 macOS 落地，均为实查）

存放连接串口令的主密钥需要系统密钥库；三平台各有一条路：

| 平台 | 机制 | 实现 | 依赖 |
|---|---|---|---|
| Windows | DPAPI（CurrentUser） | `DpapiSecretProtector` | ✅ 已有 `System.Security.Cryptography.ProtectedData` 10.0.12（MIT） |
| Linux/FreeBSD | freedesktop Secret Service（gnome-keyring / KWallet） | `SecretServiceSecretProtector` + `SecretServiceKeyProvider` | ✅ 新增 `Ace4896.DBus.Services.Secrets` 1.6.0（MIT） |
| macOS | Keychain（传统 login keychain，`kSecClassGenericPassword`） | `MacKeychainSecretProtector` + `MacKeychainKeyProvider` | ✅ **零新依赖**：自研 `Security.framework` / `CoreFoundation` P/Invoke（系统自带框架，无原生库拷贝、无许可证风险） |

考查过的 Secret Service 访问方式：

| 方案 | 版本 | 许可证 | 编程模型 | 判定 |
|---|---|---|---|---|
| **Ace4896.DBus.Services.Secrets**（`DBus.Services.Secrets`） | 1.6.0 | MIT（nuspec 实查） | 托管，异步，**API 极薄**（Connect/默认集合/CreateItem/GetSecret） | ✅ **采用**。tfm 有 net6.0/net8.0/**net10.0** 资产；传递依赖 `Tmds.DBus.Protocol`（MIT）。**无原生库依赖** |
| Tmds.DBus.Protocol（直连） | 0.9x | MIT | 托管，需自行实现 Secret Service 协议（约 200–400 行） | 备选：只引一个更主流可信的包，代价是自写底层 |
| libsecret（C 库 P/Invoke / `secret-tool` 子进程） | — | **LGPL-2.1-or-later**（库本体；GirCore 绑定包 MIT） | 有 `*_sync` 版也有 `GAsyncReadyCallback` 异步版（异步要 GLib 主循环派发）；Linux 运行期需 `libsecret-1.so.0` + `libgobject-2.0.so.0` + `libgio-2.0.so.0` + `libglib-2.0.so.0` 共 **4 个 .so** | ❌ 不采用（零独有能力、4 个原生依赖、异步契约要 GLib 主循环；详见下方 2026-10-15 复核） |
| GnomeStack.Os.Secrets（0.1.3 **MIT**）/ OutWit.* / SyntaxCircus.* | 0.1.x / 1.0.0 | MIT 或未标注 | 同样 P/Invoke libsecret 或自建 | ❌ 不采用（维护者小众、功能过剩、仍带原生依赖） |

考查过的 macOS Keychain 访问方式（NuGet `q=keychain+macos` 共 19 个命中，**无 Microsoft/dotnet 官方包**）：

| 方案 | 版本 | 许可证 | 判定 |
|---|---|---|---|
| **自研 `Security.framework` P/Invoke**（`SecItemCopyMatching/Add/Update/Delete` + CF 字典） | — | 系统自带框架 | ✅ **采用**。零新依赖、API 面 4 个函数、键名常量由 `dlsym` 取自框架（不硬编码字面量） |
| Latchkey | 0.1.0 | MIT（有 net10.0 资产） | ❌ 传递依赖 `CliWrap` 3.10.2、0.1.0 单维护者、1271 下载 |
| GnomeStack.Os.Secrets / GnomeStack.Secrets.OperatingSystem | 0.1.3 / 0.1.6 | 0.1.3 为 **MIT**（2026-10-15 复核更正，原记「未标注」） | ❌ 小维护者、且 P/Invoke libsecret；TFM 只有 net6.0/net7.0/netstandard2.0，无 net8/net10 资产 |
| ktsu.CredentialCache / OutWit.Shared.Secrets.Provider.MacOS / SyntaxCircus.Credentials / Keytar.Core | 1.3.x / 1.0.0 / 0.1.1 / 1.0.0 | 未标注 | ❌ 同一"小维护者 + 包 libsecret"模式 |
| `security` CLI 子进程 | — | — | ❌ 每次加解密起进程、`-A` 放开 ACL、二进制 key 过命令行 |

**决策与硬性规则**：

1. **只有 `Ace4896.DBus.Services.Secrets` 可进 Core**（Linux 路径）；macOS 只允许自研 P/Invoke，**不得引第三方钥匙库包**。若日后要换实现，先更新本节并跑通 §7 的密钥库集成测试。
2. **信封加密**（`SecretKeyEnvelope`）：密钥库只存**一把 32 字节主密钥**，连接串用 **AES-256-GCM** 加密后留在 JSON 文件——避免每连接一个密钥库条目（重命名/删除会留孤儿），且文件自描述、易备份。Linux 与 macOS 的保护器共用抽象基类 `EnvelopeSecretProtector`（子���只给标识名 + 平台守卫）。
3. 主密钥由 `SecretServiceKeyProvider` 以 D-Bus **DH 会话**（`EncryptionType.Dh`）存取，绝不走明文会话。
4. **无密钥库即回退明文**（`PlainTextSecretProtector`，`IsEncrypted=false`）——服务器/容器/CI 无会话总线时不得崩，交 UI 告警。macOS 侧的"不可用"判定见 §4 的 OSStatus 分类；探测用 `suppressUi: true` 实例，**正式使用才允许弹授权框**。**回退必须说清原因**（2026-10-15）：探测失败时把原因带回保护器（`ISecretProtector.FallbackReason`，默认 `null`，只有 `PlainTextSecretProtector` 携带），由 `SecretStoreFallback.Describe` 翻成可操作中文（删钥匙串条目重来 / 解锁钥匙串 / 改用签名构建 / 换到桌面会话），`MainViewModel.PlaintextFallbackWarning` 有原因就替掉笼统的"未检测到可用的系统密钥库"。
5. **不得擅自 chmod 既有目录**：`ConnectionStore` 只在"本次新建目录"时收紧到 0700（配置路径可能落在 `/tmp`、`~` 上，见 §4 实测）。
6. **macOS 附加规则**：
   - 用**传统文件钥匙串**（login keychain）。**不设** `kSecAttrAccessible`（SDK 头 `SecItem.h:89` 明确：macOS 上仅当配 `kSecAttrSynchronizable` 或 `kSecUseDataProtectionKeychain` 才生效）；**不用**数据保护钥匙串（未签名构建会 `errSecMissingEntitlement` = -34018）。
   - 条目标识沿用同一套：`service=DdraigBench`（= `SecretProtectors.ServiceName`）、`account=master-key`、`label="DdraigBench master key"`（与 freedesktop 的 `application=DdraigBench purpose=master-key` 对位），便于用户理解与手工清理。
   - 保护器名 `"macos-keychain"`（写入 `connections.json` 的 `protector` 字段，`SecretProtectors.CreateByName` 认它）。
   - `Core.csproj` 需 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`：.NET 的 `[LibraryImport]` 源生成器要求（SYSLIB1062）。业务代码本身**不使用指针语法**（钉数组走 `GCHandle`），该开关只服务于 P/Invoke 桩。
   - **授权/解锁类失败一律不静默重生成主密钥**：条目长度异常要抛 `CryptographicException`（否则旧连接串密文永久报废），授权类状态码抛 `KeychainUnavailableException`（供未来 UI 接线精确捕获后回退明文 + 告警）。

**libsecret 直连复核（2026-10-15，只读调研 + 实查；结论：维持 D-Bus，不改为直连 libsecret）**

- **可用的 .NET libsecret 绑定只有一个成熟选项**：`GirCore.Secret-1` 0.8.1（2026-07-12，MIT，TFM net8.0/net9.0/net10.0，传递依赖 `GirCore.GObject-2.0` + `GirCore.Gio-2.0` → `GirCore.GLib-2.0`，均 MIT）。nupkg 里只有托管 dll，但里面硬编码 `libsecret-1.so.0`，Linux 运行期需要 `libsecret-1.so.0` + `libgobject-2.0.so.0` + `libgio-2.0.so.0` + `libglib-2.0.so.0` 共 **4 个原生库**（没有 `GirCore` 元包，得逐个引用）。`GnomeStack.Os.Secrets` 0.1.3（**MIT**）同样 P/Invoke `libsecret-1.so.0` + `libglib-2.0.so.0`，且只有 net6.0/net7.0/netstandard2.0 资产。`iTrooz.FreeDesktopSecrets` 1.0.1 是 LGPL 的**纯托管 D-Bus 客户端**，不是 libsecret 封装（引它等于换一条 D-Bus 库，不解决任何 libsecret 相关问题）。
- **许可证更正**：libsecret 库本体是 **LGPL-2.1-or-later**（`COPYING` 全文 + 源文件头 + `meson.build`；不是 GPL）。LGPL-2.1 §6(b) 允许以"合适的共享库机制"（运行期取用户机器上已有的库）在自选许可下分发，只需显著声明并附许可证副本——**我们不随包分发该 .so、只用系统副本时不产生分发义务**，只有自包含分发（如 AppImage 内嵌）才触发。所以许可证不是本次的否决理由，**原生依赖与契约才是**。
- **能力更正**：libsecret **不是纯同步 API**——`secret-password.h` / `secret-service.h` 同时提供 `GAsyncReadyCallback` 异步版与 `*_sync` 版，异步版要 GLib 主循环派发，这正是 .NET 侧的真实负担。而 collection 管理、别名（`CreateCollection` / `ReadAlias` / `SetAlias`）、`SearchItems`、`Lock` / `Unlock`、DH 会话、Prompt 全都是 **Secret Service D-Bus 规范自身的能力**，libsecret 只是便利封装（其文档也建议需要精细控制时直接用 D-Bus）。唯一 D-Bus 不具备的是 libsecret 0.21.x 的 file-backend（借 `org.freedesktop.portal.Secret` 的 per-app 主密钥加密文件），需要 Flatpak portal，与本项目"自研 AES-256-GCM 信封 + 明文回退"的能力**重复**。
- **判据（5 条 → 保持 D-Bus）**：① 原生依赖 0 vs 4；② libsecret 无任何我们缺的能力；③ 我们的契约定在 async（`ISecretProtector` 全异步），libsecret 的同步版要额外线程、异步版要 GLib 主循环，两种都更差；④ 可用性边界相同——两者都需要会话总线 + 一个 Secret Service 提供者，改用 libsecret **不能**让无密钥库环境变得可用；⑤ 避免将来自包含分发时凭空多出的 LGPL 声明与随包义务。
- **须在文档里写明的限制**（也适用于现行 D-Bus 路线）：仅支持 freedesktop Secret Service（gnome-keyring / KWallet 的 Secret Service 后端）；无会话总线 / 无提供者 / 密钥环锁定时回退明文（`IsEncrypted=false`）并 UI 告警；不支持 sandbox 的 per-app 主密钥 portal；**不覆盖 KWallet 的非 Secret Service 路径**。

---

## 3. 工程结构

```
DdraigBench.slnx
global.json                     # test.runner = Microsoft.Testing.Platform（见 §9）
├── DdraigBench/                  # Avalonia 壳（唯一引用 Avalonia 的工程）
│   ├── Program.cs / App.axaml
│   ├── Assets/
│   ├── Services/                # IFilePicker（开+存）+ StorageProviderFilePicker + IConnectionDialog + IExportDialog + IDdlViewer（M1-6 / M2-b / M4-2 / M4-3）+ IConnectionStoreFactory/SecretConnectionStoreFactory（M4-4 接线）+ ThemeMode/IUiSettingsStore/UiSettingsStore（界面偏好：主题 + 上次连接名同住 ui-settings.json，2026-10-13）
│   ├── Controls/                # 附加属性：ResultColumns（DataGrid 动态列 + Cells/Sort（表头排序按钮与列类型对齐））+ EditorText（TextEditor 文本同步）+ WorkbenchLayout（导航器/编辑区折叠还原）+ 树/连接双击命令桥接（ConnectionActivation / TreeItemActivation）
│   ├── Views/                   # axaml + code-behind（仅 InitializeComponent）
│   │   ├── MainWindow.axaml     # Navicat-inspired 工作台：命令栏 | 左连接 Navigator + 对象树 | 中工作区 Tab | 底状态栏
│   │   ├── QueryTabView.axaml   # 查询编辑器/结果可调上下分区；表/视图只读数据浏览分页网格
│   │   └── ConnectionDialogView.axaml   # 按 Provider 出参数表（M2-b）
│   │   ├── ExportDialogView.axaml      # 导出对话框（M4-2）
│   │   └── DdlView.axaml               # DDL 预览窗口（M4-3）
│   └── ViewModels/              # 全部 ReactiveObject，构造注入 Core 接口
│       ├── ViewModelBase.cs     # ReactiveObject 基类
│       ├── MainViewModel        # 树 + Tab 集合 + 全局命令 + DDL 预览 + 已保存连接清单/明文告警（M1-6 / M4-2 / M4-3 / M4-4 / M5-2/3 模式层接线）
│       ├── ConnectionTreeNodeViewModel  # 左树节点模型（惰性加载子节点；Container = 库名或 PG/SQL Server 模式名，M1-6 / M4-3 / M5-2 改名）
│       ├── QueryTabViewModel    # 语句执行、取消、事务、结果集/状态、导出、表头排序（下推服务端，跨页；M1-6 / M3 / M4-2 / 第三批 / 第四批）
│       ├── ConnectionDialogViewModel    # Provider 参数表 + 校验 + BuildProfile（M2-b）
│       ├── ExportDialogViewModel        # 导出保存路径 + INSERT 目标表名（M4-2）
│       └── DdlViewModel                 # DDL 只读展示（M4-3）
├── DdraigBench.Core/            # ★ 新建类库：零 UI 依赖，可完整单测
│   ├── Connections/
│   │   ├── ConnectionProfile.cs       # 连接定义（Name / DataType / ConnectionString，M1-3 已落地）
│   │   ├── ConnectionParameters.cs / ConnectionStringBuilder.cs   # 按 Provider 出参数 → 连接串（M2-b）；ConnectionStringParser.cs 为逆运算（连接串 → 参数，编辑已保存连接时回填，2026-10-13）
│   │   ├── IConnectionStore.cs        # 连接清单存储契约（壳层注入，便于 VM 单测，M4-4 接线）
│   │   ├── ConnectionStore.cs         # 连接清单持久化：JSON + 口令保护（连接串加密，名称/方言明文）（M4-4）
│   │   ├── FreeSqlFactory.cs          # ConnectionProfile → IFreeSql（每连接单例，M1-3 已落地）
│   │   └── IConnectionTester.cs / FreeSqlConnectionTester.cs      # 连接测试（M2-b）
│   ├── Secrets/
│   │   ├── ISecretProtector.cs        # 异步 Protect/Unprotect 抽象（含 IsEncrypted 供 UI 告警）（M4-4）
│   │   ├── DpapiSecretProtector.cs    # Windows DPAPI（CurrentUser 作用域）（M4-4）
│   │   ├── PlainTextSecretProtector.cs # 回退明文（前缀 plain:，供读旧文件/无密钥库平台）
│   │   ├── IMasterKeyProvider.cs      # 主密钥存取抽象（M4-4 Linux + macOS）
│   │   ├── EnvelopeSecretProtector.cs # 信封保护器基类（主密钥 + AES-GCM；Linux/macOS 共用，M4-4）
│   │   ├── SecretServiceKeyProvider.cs # 主密钥存 freedesktop Secret Service（gnome-keyring/KWallet，M4-4 Linux）
│   │   ├── SecretServiceSecretProtector.cs # Linux/FreeBSD 保护器：keyring 主密钥 + AES-GCM 信封（M4-4 Linux）
│   │   ├── MacKeychainKeyProvider.cs  # 主密钥存 macOS Keychain（Security.framework P/Invoke，M4-4 macOS）
│   │   ├── MacKeychainSecretProtector.cs # macOS 保护器：钥匙串主密钥 + AES-GCM 信封（M4-4 macOS）
│   │   ├── MacSecurityFramework.cs    # SecItem*/CoreFoundation P/Invoke（internal；键名走 dlsym）
│   │   ├── KeychainUnavailableException.cs # 钥匙串不可用/未授权（供 UI 回退明文 + 告警）
│   │   ├── SecretKeyEnvelope.cs       # AES-256-GCM 信封加解密（纯逻辑，internal，可单测）
│   │   ├── SecretStoreFallback.cs     # 回退原因分类 + 可操作中文文案（未知/无密钥库/平台不支持/服务不可达/拒绝/锁定/缺授权/用户取消，2026-10-15）
│   │   └── SecretProtectors.cs        # CreateDefaultAsync 按平台探测 / CreateByName 取历史实现
│   ├── Sessions/
│   │   ├── DbSession.cs              # 一个打开的连接标签 = 一个会话（M1-4 已落地）
│   │   ├── ConnectionLease.cs        # MasterPool 取还句柄（M1-4 已落地）
│   │   ├── ISessionManager.cs        # 会话生命周期契约：活动会话 / 并发上限 / 逐个或全部关闭（2026-10-15）
│   │   └── SessionManager.cs         # 默认实现：上限（GUI 为 1）+ 确定性处置，状态操作同锁（2026-10-15）
│   │                                 # （2026-10-11 曾更正"ISessionManager 从未创建"——该缺口已于
│   │                                 #   2026-10-15 补上，见 §4 会话生命周期与 §5）
│   ├── Metadata/
│   │   ├── IMetadataExplorer.cs      # 树的统一元数据 API（签名见 §4，M1-4 已落地）
│   │   ├── DbObjectKind.cs / DbObjectInfo.cs / DbColumnInfo.cs   # 元数据模型（M1-4；Definition 为 M5-1、Schema 为 M5-2、SQL Server 定义/Schema 映射为 M5-3 追加）
│   │   ├── FreeSqlMetadataExplorer.cs# 两条通道：IDbFirst（表/视图/列，M1-4）+ 目录查询（模式/触发器/例程/原始定义，M5-1/2/3）
│   │   ├── CatalogQueryExecutor.cs   # 目录 SQL 执行 + 按列名映射（internal，M5-1）
│   │   └── DdlGenerator.cs       # DbTableInfo → CREATE TABLE/INDEX（M4-3，纯逻辑；作原始定义取不到时的回落）
│   ├── Querying/
│   │   ├── QueryResult.cs            # Columns + Rows(object[][]) + Affected + 时长 + Truncated
│   │   ├── QueryRunner.cs            # MasterPool 原始连接执行 + 流式分批读取（见 §4 实测）
│   │   └── PagedSql.cs               # 服务端分页 + 表头排序下推：包派生表 + 方言 LIMIT/OFFSET + ORDER BY（M3-5，纯逻辑）
│   │                                 # └── ResultCellKinds.cs  # 结果列类型推断/比较/稳定排序（第三批；无 UI，供列类型对齐与表头排序）
│   ├── Exporting/
│   │   └── ResultExporter.cs         # CSV / INSERT 脚本渲染（M4-2，纯逻辑；与 Querying 平级，不在其下）
│   └── Dialects/
│       ├── ISqlDialect.cs / SqlDialectBase.cs   # 方言钩子：标识符引用 / 分页 / 字面量渲染（M4-1）
│       ├── SqliteDialect.cs / MySqlDialect.cs / PostgreSqlDialect.cs / SqlServerDialect.cs
│       ├── SqlDialects.cs            # DataType → ISqlDialect 工厂
│       └── CatalogQueries/           # ★ IDbFirst 缺口的补齐 SQL（M5-1）：每库一组，只造 SQL 不执行
│           ├── ICatalogQueries.cs    # 能力矩阵 + 对象清单/视图定义/表定义 SQL（SQL Server M5-3，列名契约见类注释）
│           ├── SqliteCatalogQueries.cs / MySqlCatalogQueries.cs / PostgreSqlCatalogQueries.cs / SqlServerCatalogQueries.cs
│           └── CatalogQuerySets.cs   # DataType → ICatalogQueries 工厂（与 SqlDialects 同构）
└── DdraigBench.Test/            # Core 的全部单测/集成测（SQLite 黄金通道）
```

**新建 Core 的 csproj 要点**：`net10.0` + `Nullable enable` + `ImplicitUsings enable` + 仅引用 `FreeSql` 与所选 Provider；**禁止引用任何 Avalonia.* / ReactiveUI.* / Splat**。（唯一附加开关：`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`，因 macOS 钥匙串的 `[LibraryImport]` 源生成器要求，见 §2.8 规则 6）

> M1-1（2026-09-18）已落地：`DdraigBench.Core` 建成（仅 `FreeSql` + `FreeSql.Provider.Sqlite`）并加入 `DdraigBench.slnx`，主/测工程均已 ProjectReference 之。Core 内部各类文件随 M1-2..M1-5 逐个补齐。

---

## 4. 关键服务契约

```csharp
// 元数据：左侧树的唯一数据源。任何树节点懒加载都走它。
public interface IMetadataExplorer
{
    // 可浏览的「库」。SQLite/MySQL 走 IDbFirst.GetDatabases()；PostgreSQL / SQL Server 收窄为当前库
    //   （连接只能浏览所连库；SQL Server 的 IDbFirst.GetDatabases() 实测为空，改用 SELECT DB_NAME()）。
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession s, CancellationToken ct = default);
    // Table/View ← IDbFirst；Schema/Trigger/StoredProcedure/Function/Event ← 目录查询（M5-1 / M5-2 / M5-3）：
    //   方言没有这类对象时返回「空集」而非报错（如 SQLite 的 Schema/存储过程）；
    //   不是"容器级对象集合"的 kind（Column/Index/ForeignKey/Database）抛 NotSupportedException。
    //   ⚠️ database 参数实为「容器」：库级对象传库名，PG / SQL Server 的模式级对象传模式名。
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(DbSession s, string database,
        DbObjectKind kind, CancellationToken ct = default);
    // M1-4 追加：列不属于数据库级签名，单列一法（对应 §6.3 的"表→列"级）
    Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(DbSession s, string database, string table,
        CancellationToken ct = default);
    // M4-3 追加、M5-1 改优先级：DDL 预览。**原始定义 > 按元数据重建**——
    //   表：方言能给出原始建表语句就用它（当前 SQLite），否则回落 DdlGenerator（SQL Server 也走此重建）；
    //   视图：目录查询取真实定义（SQLite/MySQL/PG/SQL Server），取不到才回落占位；
    //   触发器/函数/存储过程/事件：按名在目录里找，取到 definition 就返回，否则 null；MySQL/MariaDB 支持计划事件，SQL Server 当前支持 DML 触发器、函数和存储过程。对象不存在返回 null。
    Task<string?> GetDdlAsync(DbSession s, string database, string table, CancellationToken ct = default);
}

// 执行：SQL 编辑器 → 网格的唯一通道。永远返回无类型结果。
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    long AffectedRows,
    TimeSpan Elapsed,
    string? Error,
    bool Truncated = false);   // M3-4 追加：取满 maxRows 后多读一行判定是否被截断

// 会话：GUI 的"一个连接标签"。事务态、变量态都挂在这里。
public sealed class DbSession : IAsyncDisposable
{
    public ConnectionProfile Profile { get; }
    public IFreeSql Fsql { get; }                                    // 元数据/快查通道
    // 修正：返回 ConnectionLease（内含 MasterPool 的 Object<DbConnection>）；
    // 裸 DbConnection 会丢失包装对象 → 连接无法归还（见下实测）
    public ValueTask<ConnectionLease> LeaseRawAsync(CancellationToken ct = default);
}
```

**会话语义（设计决定，勿违背）**：
- 默认读操作走 `Fsql.Ado`（走它的池）。
- **事务/多语句脚本/会话变量**：`LeaseRawAsync()` 从 `MasterPool` 拿 `DbConnection` 自己开事务、自己 finally 归还——ORM 的池语义和 GUI 会话语义不同，不让池替我们做事务主。
- **M1 实测结论（2026-09-18，SQLite 临时文件）**：`Ado.MasterPool` 类型为 `FreeSql.Internal.ObjectPool.IObjectPool<DbConnection>`；`Get()` 返回 `Object<DbConnection>`，连接取自其 `.Value`；`Object<T> : IDisposable`，`Dispose()` 内部调用 `Pool.Return(this)` 归还。故 `using (var o = pool.Get()) { /* o.Value */ }` 即正确的取还语义（另有 `Get(TimeSpan?)` 超时重载与 `GetAsync(CancellationToken)`；亦可 `pool.Return(o)` 显式归还）。`MasterPool` 在未配置连接串等场景可能为 `null`，使用前必须判空。冒烟测试见 `DdraigBench.Test/SqliteSmokeTests.cs`。
- **M1 实测结论（2026-09-18）**：`Ado.ExecuteReader` 系列**不能用于通用 SQL Runner**——① 回调进入时 reader 已被预读、定位在第 1 行（直接 `Read()` 会吞掉首行，须"先取当前行、再 `Read()`"）；② 对 INSERT/UPDATE/DELETE **回调根本不触发**，拿不到 `RecordsAffected`。故 `QueryRunner` 改走 `Ado.MasterPool` 的原始 `DbConnection`：列名 / 流式分批（`MaxRows` 上限）/ affected / 取消全可控。单测见 `DdraigBench.Test/QueryRunnerTests.cs`。
- **M1 实测结论（2026-09-18）**：`LeaseRawAsync` **不能**返回裸 `DbConnection`——`MasterPool.Get()` 的归还依赖 `Object<T>` 包装对象（`Object<T>.Dispose()` → `Pool.Return`），丢掉包装即连接池泄漏。故返回自定义 `ConnectionLease`（`IDisposable`/`IAsyncDisposable`，只暴露 `.Connection`）。单测见 `DdraigBench.Test/DbSessionTests.cs`。
- **M2 实测结论（2026-09-18）**：`Ado.MasterPool.Get()` **会真正打开连接**（FreeSql 自带的 `DbConnectionStringPool.Get` 内部调用 `DbConnection.Open()`）——故不能用"取连接看类型"做离线断言；GPL 驱动校验改为**程序集加载探测**（`Assembly.Load("MySqlConnector")` 成功、`MySql.Data` 失败）。多方言集成测试在探测不可达时 `Assert.Skip`。
- **M3-2 事务实现（2026-09-18）**：事务态挂在 `QueryTabViewModel`（**每标签独立**）——`BeginTransactionAsync()` 用 `DbSession.LeaseRawAsync()` 持有**一条**连接并 `BeginTransaction`；执行时把该 `DbTransaction` 传给 `QueryRunner.ExecuteAsync(..., transaction:)`，SQL 便跑在事务那条连接上（**不走池路径**）；`Commit`/`Rollback`/标签处置都会归还租约。注意：`Cancel()` 只中断客户端读取，**不会**提交或回滚事务。
- **会话生命周期与并发上限（2026-10-15，`ISessionManager` 落地）**：会话不再由 VM 手写 `new DbSession(...)` / `DisposeAsync()`——统一走 `Core/Sessions/SessionManager`。`MainViewModel` 只保留一个只读别名 `private DbSession? Session => _sessions.Active`；`ConnectCoreAsync` 的策略是**先 `CloseAllAsync()` 再 `OpenAsync(profile)`**，因为默认上限 `MaxOpenSessions = 1`：忘了关旧会话就会抛 `InvalidOperationException`（"会话并发上限为 1…"），把连接泄漏变成响亮失败而不是静默多挂一个连接。`OpenAsync` 超限不改状态、`CloseAsync` 对不属于自己的会话返回 `false`、`CloseAllAsync` 幂等且**后开先关**；`MainViewModel` 实现 `IAsyncDisposable`，`App` 在 `Desktop.ShutdownRequested` 里先 `e.Cancel = true`、`await viewModel.DisposeAsync()` 之后再 `desktop.Shutdown()`（第二次请求靠 `_shutdownRequested` 放行）——否则退出时 FreeSql 连接池不会释放。`MainViewModel.Sessions` 是公开属性，供 `App` 与测试使用。
- **M3-5 服务端分页实测（SQLite/MySQL/PG：2026-10-05；SQL Server：2026-10-12）**：SQLite/MySQL/PG 判定"有没有下一页"的**多读那一行必须由 SQL 的 `LIMIT pageSize+1` 产生**，不能指望 `QueryRunner` 的读取上限——它的 `Truncated` 语义是"取满 `maxRows` 之外还有一行"，把 `maxRows=pageSize+1` 时恒为 false（初版踩坑，`HasNextPage` 一直 false、翻页点击无效）。另：`ExecuteAsync()` 必须**回到第 1 页**，而翻页要另走私有 `RunAsync()`，否则翻页命令里的 `CurrentPage++` 立刻被重置。SQLite/MySQL/PG 分页 SQL 包派生表（`SELECT * FROM (…) AS "ddb_page" LIMIT …`）；SQL Server 不包派生表，只对顶层 `ORDER BY` 且无冲突语法的简单查询直加 `OFFSET/FETCH`，其余 fail-closed。两类扫描都只认**顶层**关键字（括号深度 + 字符串/引用标识符状态机），避免误伤子查询、字面量与注释。SQL Server 实测覆盖无序、有序首/末页、已有分页、多语句、`TOP` / `OPTION` / `FOR`。**2026-10-16 修正（第八批）**：包装方言不再因用户 SQL 自带顶层 `LIMIT/OFFSET` 而拒绝改写（`HasTopLevelPaging` 只在 `RequiresTopLevelOrderByForPaging` 的 SQL Server 分支拒绝）；未下推的原因改由 `PagedSql.Plan(...).SortFallback`（`SortFallbackReason`：无服务端分页 / 语句不可改写 / SQL Server 保留用户 ORDER BY / 列名不可排序）说清，UI 状态栏按此给文案；列名唯一性改为**忽略大小写**判定（`ResultSort.For`），避免 `ID`/`id` 并存时生成歧义 SQL。
- **M1 实测结论（2026-09-18，SQLite）**：`IDbFirst.GetDatabases()` 返回 `[main]`；`GetTablesByDatabase("main")` 同时含表与视图，`DbTableInfo.Type` 为 `FreeSql.DatabaseModel.DbTableType.TABLE/VIEW`，`Schema` 为空串。故 SQLite 的"库→表→列"可直接映射、无需特判；`GetObjectsAsync` 目前仅实现 Table/View，其余 kind 抛 `NotSupportedException`。单测见 `DdraigBench.Test/MetadataExplorerTests.cs`。
- **M4-4 密钥库实测结论（2026-10-01，Linux + gnome-keyring）**：
  - `Ace4896.DBus.Services.Secrets` 的 `SecretService.SearchItemsAsync` 返回 **(unlocked, locked) 元组**，而 `Collection.SearchItemsAsync` 返回 `Item[]`——搜索"本应用的主密钥条目"要用前者并取元组两半；`Item.GetSecretAsync()` 会自动解锁条目。
  - `Collection.CreateItemAsync(..., replace: true)` 在"同属性条目已存在"时替换而非新增（幂等），故主密钥创建可安全重试；条目属性 `application=<本应用/测试作用域>, purpose=master-key`。
  - 密钥环**锁定或提示被取消**时 `CreateItemAsync`/`GetSecretAsync` 会返回 null 或抛异常 → `IsAvailableAsync` 统一吞成 `false`，由工厂回退明文，**不得让启动崩掉**。
  - **`File.SetUnixFileMode` 作用于既有目录时**：非属主会抛 `UnauthorizedAccessException`（如 `/tmp` 属 root）→ `ConnectionStore` 只在"本次新建目录"时收紧 0700，并对权限失败一律吞掉不阻断落盘；回归测试 `Existing_directory_permissions_are_left_untouched` 守着这条线。
  - `Path.GetFileName` 在 Linux 上**不把 `\` 当分隔符**（既有 `ConnectionStringBuilder.SuggestName` 因此对 Windows 路径失效）——已改为把两种分隔符归一化后取名。
- **M4-4 密钥库实测结论（2026-10-04，macOS 12.7.6 + Security.framework）**：
  - **查询字典的键/值回调绝不可传 NULL**：`CFDictionaryCreate` 的 `keyCallBack`/`valueCallBack` 传 NULL 时，建出的字典**不 retain 键与值**；随后 `CFRelease` 那些 CFString/CFData 会让 `SecItem*` 读到野指针，表现为**永久卡死而非崩溃**（无任何报错，CI 里表现为测试挂住——本轮实际踩到，靠"二分查询字典 + `CFCFStringGetCString`/`CFGetTypeID` 自检"定位）。必须传 CoreFoundation 导出的 `kCFTypeDictionaryKeyCallBacks` / `kCFTypeDictionaryValueCallBacks`（取**结构体地址**）。
  - **键名/类名常量不必硬编码，也查不到字面量**：`SecItem.h` 只声明 `extern const CFStringRef`（"class"/"genp"/"svce"/"v_Data" 等键值在任何公开头文件里都没有）。实查 `Security.framework.tbd` / `CoreFoundation.tbd` 确认这些符号**都导出**，故 `MacSecurityFramework` 一律 `dlsym` 取值：`kSecClass`、`kSecClassGenericPassword`、`kSecAttrService/Account/Label`、`kSecValueData`、`kSecMatchLimit(One)`、`kSecReturnData`、`kSecUseAuthenticationUI(Allow|Fail)`、`kCFBooleanTrue`（`CFBooleanCreate` 未导出，只能取常量；实测 `CFGetTypeID(kCFBooleanTrue)=21` = CFBoolean，`CFGetTypeID(CFString)=7`）。**dlsym 对数据符号返回变量地址，要再解引用一次**。
  - OSStatus 实测：条目不存在 `errSecItemNotFound=-25300`、重复 `errSecDuplicateItem=-25299`、未授权 `-25293`、禁交互 `-25308`、缺授权组 `-34018`（见 §2.8 规则 6 的分类表）。
  - `kSecUseAuthenticationUI=Fail`（= `kSecUseAuthenticationUIFail`，`SecItem.h:1066` 起 deprecated 但仍可用）能让"条目被 ACL 拦下"立刻失败而不是等授权框；**注意它只抑制条目授权、不抑制钥匙串解锁**，故钥匙串被锁时 SecItem 仍可能阻塞调用线程——壳工程接线时**必须在后台线程调用**。
  - 本机 `security find/add/delete-generic-password` 与我们的 SecItem 实现**互为独立参照**：集成测试 `Item_is_discoverable_by_the_security_cli` 用 CLI 反查我们写入的条目（校验 service/account/label 真按系统常量落库，用户能在「钥匙串访问」里搜到并删除）。反过来，**不要用 `security add -U` 改写我们创建的条目**去模拟损坏——它会重建条目并换掉 ACL，随后 SecItem 返回值不可预期（实测得到 `errSecItemNotFound` 而非 DuplicateItem/AuthFailed）；造损坏条目要用我们自己的 `SecItemUpdate`。
  - `kSecAttrAccessible` 在传统（文件）钥匙串上**不生效**（`SecItem.h:89`：macOS 上仅当配 `kSecAttrSynchronizable` 或 `kSecUseDataProtectionKeychain` 才生效），故不设；数据保护钥匙串则需要 keychain-access-groups 授权，未签名构建直接 `errSecMissingEntitlement`。
- **M5-1 目录查询实测结论（2026-10-08）**：
  - **FreeSql 的 PostgreSQL `GetTablesByDatabase(x)` 要的是「库名」，不是模式名**（2026-10-12 用真 PG 13 靶机更正；早前"按 `pg_namespace.nspname` 过滤"的说法是**错的**）。
    实查方式：`Aop.CommandAfter` 抓真实 SQL——该实现是 `pg_tables` union `pg_class`（`relkind in ('m','v')`）的**固定查询，完全不带 `x` 参数**（`[] ` 空参数列表），只在客户端用 `x` 去匹配返回的 `nspname || '.' || relname` 前缀。
    行为因此是：传**库名** → 返回所连库全部模式的对象（`DbTableInfo.Schema` 是真实模式名）；传**模式名** → 无任何库叫 `public`，**静默返回空集**，PG 的表/视图就一张也列不出来。
    后果与修法（M5-3 修）：`FreeSqlMetadataExplorer.GetIdbFirstDatabaseAsync` 对「连接绑定单库」的方言统一把容器换成 `BuildCurrentDatabaseQuery()` 的结果（PG = `current_database()`、SQL Server = `DB_NAME()`），随后由 `MatchesSchemaContainer` 按 `DbTableInfo.Schema == container` 收敛到调用方要的模式。
    回归测：`MultiDialectIntegrationTests.RunCatalogAsync` 的建表/列/DDL 断言（去掉该转换即红）。
    `GetDatabases()` = `select datname from pg_database where datname not in ('template1','template0')`（**库名**）——与 `GetTablesByDatabase` 期望的语义一致，早前把两者说成"不同源"也是那次误判的产物。
    > M5-2 已把 PG 的库清单收窄为当前库并补上模式层，见下；M5-3 才修掉"容器=模式名"直接喂给 IDbFirst 的错配。
  - MySQL 用 `information_schema.TRIGGERS / ROUTINES / VIEWS`，PG 用 `pg_trigger / pg_proc / pg_get_viewdef`。PG 的 `pg_proc.prokind`（`'f'`=函数 / `'p'`=存储过程）**要求 PG ≥ 11**（10 及更早是 `proisagg`/`proiswindow`）。
  - **SQLite 的 `sqlite_master.sql` 是原始语句**（建表/视图/触发器都在内），故 SQLite 的定义类缺口一次读原文全解决：`AUTOINCREMENT`、内联 `UNIQUE`、`CHECK`、`COLLATE` 都保留（§6.2 的①②③）。**保真度高于按元数据重建**，所以 `GetDdlAsync` 改成"原始定义优先"。副作用：SQLite 的表 DDL 预览不再输出 `DdlGenerator` 的加引号重建版（那反而是有损的）。原始语句通常**不带结尾分号**，需补上才能直接复制执行。
  - `sqlite_master` 可加附加库限定：`"main".sqlite_master` 合法（本层所有 SQLite 查询都这样写，`main` 来自 `QuoteIdentifier`）。
  - **MySQL 的字面量必须转义反斜杠**：默认 `sql_mode` 下 `\` 是转义符（`NO_BACKSLASH_ESCAPES` 未开）。原先 `SqlDialectBase.Quote` 只翻倍单引号，含 `\` 的数据（Windows 路径、JSON 里的 `\n`）会在导出 INSERT 与库名过滤里被**静默改写**——M5-1 修（`MySqlDialect` 覆写 `Quote`），并加了回归测。假设：`NO_BACKSLASH_ESCAPES` 未开启。
  - `QueryRunner` 把 SQL 异常收敛进 `QueryResult.Error`（不抛）。**元数据场景必须把它重新抛出去**（`CatalogQueryExecutor` 负责），否则树会"静默空"、用户看不出是权限不足还是真没对象。
  - 树节点展开是 **fire-and-forget**（`ConnectionTreeNodeViewModel` 里 `_ = _loadChildren(this)`），异常会变成未观察的任务异常被吞。故 `MainViewModel` 的各 loader 都就地 try/catch 收敛成状态栏文案；回归测 `Expand_failure_is_reported_instead_of_crashing_the_tree`。
- **M5-2 模式层实测结论（2026-10-08）**：
  - **「库」这一层在不同方言的含义差异极大**，必须分开处理，否则树形就是错的：
    SQLite 一个连接可 `ATTACH` 多个库（`IDbFirst.GetDatabases()` 给 main + 附加库）；MySQL 的库 == 模式且**可跨库**（`information_schema` 与 `GetTablesByDatabase(table_schema=…)` 都不受"当前库"限制，`USE` 可切换）；**PG 一个连接只能看到所连库内**的对象（跨库要换连接或 dblink/FDW）。
    故在 M5-2 实现时，`ICatalogQueries.ConnectionIsBoundToSingleDatabase` 只对 PG 为真，`GetDatabasesAsync` 收窄为 `SELECT current_database()`——避免把"看得见打不开"的库列进树。SQL Server 的单库行为与 `SELECT DB_NAME()` 回退为后续 M5-3 工作。
  - 模式层作为**普通对象种类**接入（`DbObjectKind.Schema`），复用 M5-1 的目录查询通道（`Supports`/`BuildObjectsQuery`），因此**不需要给 `IMetadataExplorer` 加新方法**。M5-2（2026-10-08）仅 PostgreSQL 实现此层；SQLite/MySQL 返回空集。SQL Server 的当前库模式清单与树接线由 M5-3 首批代码补入并已靶机实测通过。
  - PG 模式查询滤掉系统命名空间：`pg_catalog` / `information_schema` / `pg_toast`，以及 `pg_temp%` / `pg_toast_temp%`（临时对象的模式，形如 `pg_temp_3`）。SQL Server M5-3 查询另滤 `sys` / `INFORMATION_SCHEMA`。
  - `pg_namespace` 本就只含当前库的模式，因此 PG 的模式清单 SQL 与 `database` 参数无关（实现里刻意忽略它，并有测试守着这一点）；SQL Server 的模式清单则以 `DB_NAME()` 限定当前库。
  - 树节点的容器字段由 `Database` **改名 `Container`**：PG 下对象的容器是**模式名**而非库名；SQL Server M5-3 也沿用模式名作为对象容器，避免 DDL 预览入参传错。VM 侧只有 `CanPreviewDdl` / `PreviewDdlAsync` / 三个 loader 用到，XAML 未绑定它。
  - 树的层级逻辑**不依赖靶机也能测透**：`ScriptedMetadataExplorer` 替身直接编排「库→模式→对象」的形状，两条分支（有模式 / 无模式）都有 VM 级回归。

---

## 5. MVVM 与线程规则（ReactiveUI）

- `ViewModelBase : ReactiveObject`（已有）。绑定属性用 `this.RaiseAndSetIfChanged`；集合用 `ObservableCollection`。
- **一切 DB 调用不得跑在 UI 线程**：`Observable.FromAsync(...).SubscribeOn(RxApp.TaskpoolScheduler).ObserveOn(RxApp.MainThreadScheduler)` 或 `await Task.Run` + `Dispatcher.UIThread.Post`。
- ViewModels **禁止** `using Avalonia.*`（允许 ReactiveUI）。引用控件/颜色的 ViewModel 视为 bug。
- View code-behind 只留 `InitializeComponent`；逻辑进 ViewModel 或 Converter。
- 命令用 `ReactiveCommand.CreateFromTask`，长任务必须接 `CancellationToken`（窗口关闭即取消）。
- **ReactiveUI 24 已不依赖 `System.Reactive`**：命令用 `ReactiveCommand.Create*` 实现、以 `ICommand` 暴露属性（避免 `Unit` 来源问题），测试直接 await VM 的公开 async 方法。
- ⚠️ **ReactiveUI 24 的 `RaiseAndSetIfChanged` 返回新值（`TRet`），不是"是否变化"的 `bool`**（实测：对非 bool 属性写 `if (RaiseAndSetIfChanged(...))` 直接编译不过；对 bool 属性会**侥幸**编译但语义是"新值"）。要判"是否变化"须自己先取旧值或比较。
- 交互能力（文件选择等）以接口注入 VM（如 `IFilePicker`/`StorageProviderFilePicker`），保持 VM 零 Avalonia 依赖且可单测。
- 结果网格用**普通 `DataGrid` + 附加属性** `ResultColumns.Headers`（`Controls/ResultColumns.cs`）按列头动态生成列；单元格用 `DataGridTemplateColumn` + `FuncDataTemplate<object?[]>` 从行数组直取（**不经绑定引擎**）。**不派生 `DataGrid`**。
- （教训）**派生自 Avalonia 主题化控件的自定义控件必须 override `StyleKeyOverride`**，否则 `ControlTheme` 按派生类型查不到 → 模板缺失 → **整块不渲染且无报错**。M1 首选改用**附加属性**正是为了避开这类派生陷阱。
- 工作台借鉴 Navicat 的连接 Navigator / 对象树 / 标签工作区交互，但视觉继续使用 FluentAvalonia + FluentIcons，不增加第二套主题或图标包；密集工具栏、导航器与工作区之间应有清晰边界，可通过 splitter 调整导航器宽度并收起。
- 应用外观支持「跟随系统 / 浅色 / 深色」；默认跟随操作系统。界面偏好（**主题 + 上次连接的连接名**）持久化到 LocalApplicationData 下单独的 `ui-settings.json`，由 `IUiSettingsStore`/`UiSettingsStore` **整份读写**——拆成两个各写自己那半边的存储会让"切主题"顺手抹掉"上次连接名"（有红测 `Theme_switch_keeps_the_auto_reconnect_record` 守着）；旧版只存主题的 PascalCase JSON 仍读得出来。工作台自定义面板/边框/文字/状态色必须使用跟随 `RequestedThemeVariant` 更新的语义资源，禁止硬编码单一主题表面色。
- **连接 UX 仍为单活动连接**：左侧保存连接清单双击激活，标题栏可**编辑**（回填字段、保存时就地替换原条目）或移除；**启动时按 `ui-settings.json` 里记的连接名自动重连上次用过的连接**（只有连接成功才记名字；名字已不在清单里就安静跳过并清掉记录；清单整体读不出来时保留记录）；切换连接会关闭旧会话及其所有标签，不暗示多个连接并行存在。会话的持有与处置统一走 `ISessionManager`（默认上限 1）：VM 里没有 `new DbSession`，`MainViewModel.Sessions` 供 `App` 在退出路径上处置；`App` 收到 `ShutdownRequested` 时先 `e.Cancel = true`，`await viewModel.DisposeAsync()` 之后再 `desktop.Shutdown()`（见 §4 会话生命周期）。**密钥库不可用而回退明文时必须说清失败原因**（2026-10-15）：`SecretStoreFallback.Describe` 把探测失败翻成可操作中文（删钥匙串条目重来 / 解锁 / 改用签名构建 / 换到桌面会话），`MainViewModel.PlaintextFallbackWarning` 有原因就用原因，**不写笼统的"未检测到可用的系统密钥库"**（见 §2.8 规则 4 与 §8 第六批）。
- 对象树层级：SQLite/MySQL「库 → 非空对象类型组 → 对象 → 子结构（表下有列与 MySQL 分区）」；PostgreSQL/SQL Server「当前库 → 模式 → 非空对象类型组 → 对象 → 列」。分区是表级子结构，不是 `DbObjectKind` 容器级对象。类型组是壳层展示节点（`Kind=null`、`GroupLabel` 有值），不是 `DbObjectKind` 对象；DDL/浏览命令必须以真实对象节点的 Kind 判定。
- 对象树筛选只检查**已加载节点**，不查询数据库、不展开惰性节点；过滤时保留含匹配后代的祖先，清空筛选恢复已加载树。
- 表/视图双击或上下文命令打开只读分页数据标签，使用 dialect 分别引用容器与对象标识符；表有主键元数据时以主键作为稳定默认排序，否则依赖数据库返回顺序。相同活动连接/容器/类型/对象复用原标签且保留页码。不做行编辑、插入、删除或筛选；结果网格**表头点击可排序**——能下推就下推到服务端（`PagedSql.Plan(...)`：SQLite/MySQL/PG 包派生表后在外层加 `ORDER BY 序号`，**用户自带的顶层 `LIMIT/OFFSET` 也一起关进派生表**，排序作用于整条语句，翻页仍全局有序；SQL Server 无顶层 `ORDER BY` 时直接补 `ORDER BY 列名` + `OFFSET/FETCH`，已有则保分页不排序），下推不成立时退回"只排当前页已读回的行"并把**具体原因**写进状态栏（`SortFallbackReason` 五种：无服务端分页 / 语句不可改写 / SQL Server 保留用户 ORDER BY / 列名不可排序；文案由 `SortFallbackText()` 组装，另含"事务进行中不重跑查询"与"编辑器内容已改动，未重跑查询"），列类型由已读回的值推断（数字/日期右对齐）。**排序目标一律经 `ResultSort.For(Columns, index, descending)` 构造**（列名唯一性忽略大小写）。**翻页前核对编辑器文本**：`_executedSql` 与当前 `Sql` 不一致时不改页码、直接提示先执行（F5）。连接切换照旧处置全部标签。
- 查询标签编辑器/结果网格上下可拖动分割；数据浏览标签隐藏编辑器，只呈现只读网格与分页。快捷键：`Ctrl+N` 新建查询、`F5` 执行选中查询标签、`Esc` 取消正在执行的查询、`Ctrl+R` 刷新数据浏览标签并回到第一页（查询标签不受 Ctrl+R 影响）。
- `TextEditor.Text`（AvaloniaEdit）**不是 `AvaloniaProperty`**，不能直接绑定（README 也只演示字面量）→ 用附加属性 `Controls/EditorText.Text` 双向同步（M3-3）。
- 左树展开是 **fire-and-forget**（`ConnectionTreeNodeViewModel` 里 `_ = _loadChildren(this)`）→ loader 内部**必须** try/catch 并把失败写进 `Status`（M5-1），否则元数据查询失败会变成未观察的任务异常被静默吞掉，用户只看到"树是空的"。

§4 的服务契约经 Splat 默认定位器注入 ViewModel 构造函数（Autofac 已按 §2.4 D2 取 b 移除）。

---

## 6. 元数据层策略

1. **能用 IDbFirst 就不手写**：`GetDatabasesAsync` → `IDbFirst.GetDatabases()`；表/列/主键/索引/外键/视图/存储过程 → `IDbFirst.GetTablesByDatabase()` 的 `DbTableInfo`（含 `IndexesDict` / `ForeignsDict`）。这是白嫖 22 方言的最大头。
2. **IDbFirst 缺口 → `Dialects/CatalogQueries/`**（M5-1 落地；早前 §3 曾把该目录写为 `Metadata/CatalogQueries/`，**以 §6.2 为准，实际在 `Dialects/` 下**——它绑方言、用 `ISqlDialect` 引用，放方言侧才对）。
   - 设计：**只造 SQL 不执行**（`CatalogQueryExecutor` 执行 + 按列名映射）。好处是无靶机也能对 SQL 形状/能力矩阵/转义做纯逻辑单测。
   - 容器参数语义与 `kind` 有关：SQLite = 附加库名，MySQL = schema；PG 的对象级目录查询传 schema（PG 的 `GetDatabases()` 返回库名，两者不同源，M5-2 起由「当前库 + 模式层」处理）。SQL Server 的 `Schema` 清单查询传当前库名，`Trigger` / `Function` / `StoredProcedure` 查询传 schema 名；其当前库由 `DB_NAME()` 取，见 §4。
3. **缺口状态（截至 2026-10-12；M5-3 已完成，相关方言查询均经靶机验证）**：
   - ✅ ① **视图定义**：SQLite 读 `sqlite_master.sql` 原文；MySQL 用 `information_schema.VIEWS.VIEW_DEFINITION` + `ComposeViewDdl` 补前缀；PG 用 `pg_get_viewdef()` + 补前缀；SQL Server `sys.sql_modules.definition` 返回完整 `CREATE VIEW`（已靶机实测）。
   - ✅ ② **SQLite `AUTOINCREMENT`**：改走"原始定义优先"，不再依赖 `IsIdentity`（`INTEGER PRIMARY KEY`（无 AUTOINCREMENT）**不得**输出 `AUTOINCREMENT`——那会改变 rowid 复用语义）。
   - ✅ ③ **唯一约束**：内联 `UNIQUE` / `CHECK` / `COLLATE` 随原始语句一并保真（按元数据重建必丢，因为 SQLite 把它们落在 `sqlite_autoindex_*` 里而 `DdlGenerator` 要跳过内部索引）。
   - ✅ **触发器 / 函数 / 存储过程清单**：SQLite/MySQL/PG/SQL Server 均有目录清单；**四方言的定义也都已接通**（2026-10-12）：SQLite 触发器直接读 `sqlite_master.sql`；MySQL 清单用 `CONCAT` 拼 `CREATE` 前缀 + `information_schema.PARAMETERS` 参数表（`ORDINAL_POSITION > 0`，0 号是函数返回值行）+ `RETURNS`/`DETERMINISTIC` + `ACTION_STATEMENT`；PG 用 `pg_get_triggerdef(t.oid, true)` 与 `pg_get_functiondef(p.oid)`；SQL Server 用 `sys.sql_modules.definition` / `OBJECT_DEFINITION`。MySQL/PG 均已在真靶机（MariaDB 10.5.29 / PostgreSQL 13.23）跑通建→列→取定义→`GetDdlAsync`→清理全链路。
   - ⚠️ **定义可能取不到，这不算失败**：MySQL 无权限时 `ROUTINE_DEFINITION` 为 NULL（实现用 `CASE` 把整条定义折成 NULL，不吐半截语句）；PG 的 `pg_get_functiondef` **对聚合函数与 `LANGUAGE internal`/`c` 的函数会直接报错**，故清单加 `l.lanname IN ('sql','plpgsql') AND pg_aggregate.aggfnoid IS NULL` 守卫，把这类行折成 NULL——**一条坏行会让整条清单查询失败、树上就一个例程都看不到**（2026-10-12 靶机实测踩到）。
   - ✅ **物化视图定义（PG，M5-3，2026-10-12）**：`BuildViewDefinitionQuery` 原写死 `relkind='v'`，而 IDbFirst 把 `relkind='m'` 也当 VIEW 列进树 → 物化视图的 DDL 预览落空。现按 `relkind IN ('v','m')` 收窄，并在 SQL 里用 `CASE c.relkind WHEN 'm' THEN 'CREATE MATERIALIZED VIEW ' ELSE 'CREATE OR REPLACE VIEW ' END || quote_ident(...)` 直接拼成完整语句（`ComposeViewDdl` 退化为只规整尾分号）。**为什么必须分开**：物化视图若拼成 `CREATE OR REPLACE VIEW`，用户执行这份 DDL 会得到一个**普通视图**，物化语义（`REFRESH MATERIALIZED VIEW`）被静默丢掉。回归测：`CatalogQueryTests.PostgreSql_view_definition_uses_pg_get_viewdef`（形状断言）与 `MultiDialectIntegrationTests.RunCatalogAsync` 的 PG 物化视图建→取 DDL→删断言——把 `relkind` 改回只收 `'v'` 即红（2026-10-12 实测）。
   - ✅ **MySQL 原始表定义（M5-3，2026-10-12）**：走 `SHOW CREATE TABLE`——MySQL 没有别的通道能拿到原始建表语句（`information_schema` 只有列/索引的拆解元数据，重建必丢 `AUTO_INCREMENT` 起始值、字符集/排序规则、`ON UPDATE`、生成列表达式）。它**不是 `SELECT`**，且返回两列（名字 + `Create Table`）、列名随对象类型变（视图是 `Create View`），所以本契约的「单列单行」约定为此放宽：`CatalogQueryExecutor.ReadDefinitionAsync` 按列名挑定义——优先名为 `definition` 的列，否则取唯一一个以 `Create ` 开头的列，**命中多列即抛错**（猜错会把列名当 DDL 返回，用户复制执行必炸）。标识符经 `MySqlDialect.QuoteIdentifier`（反引号翻倍）引用，不用字面量拼接。回归测：`CatalogQueryTests.Raw_table_definitions_come_from_sqlite_and_mysql`、`CatalogQueryTests`/`MetadataExplorerTests` 的定义列选取用例，以及 `MultiDialectIntegrationTests.RunCatalogAsync` 的 MySQL 段（建表 → 改 `COLLATE`/`COMMENT` → 断言 DDL 含 `ENGINE=` / 排序规则 / 注释 → 删；按元数据重建的 `DdlGenerator` 根本不输出这几个，故该断言只有拿到原始语句才可能过——2026-10-12 红绿验证过）。
   - ✅ **MySQL 计划事件（M5-3，2026-10-12）**：新增 `DbObjectKind.Event` 与「事件」树组；从 `information_schema.EVENTS` 逐列重建完整 `CREATE EVENT`（一次性 `AT` / 周期 `EVERY`、可选 `STARTS`/`ENDS`、`ON COMPLETION`、`ENABLED`/`DISABLED`/`SLAVESIDE_DISABLED`、注释与 DO 体）。刻意不写 `DEFINER`：跨服务器时原账号可能不存在，普通用户也未必有权指定他人 definer。MariaDB 10.5.29 靶测覆盖周期和一次性事件建→列→`GetDdlAsync`→删除与定义重执行，重建后 `STATUS`、`ON_COMPLETION` 保真（`MultiDialectIntegrationTests.MySql_events_come_back_from_the_catalog`）。
   - ✅ **MySQL/MariaDB 表节点下的分区子树（M5-3，2026-10-12）**：分区是表级子结构，不伪装成容器级 `DbObjectKind` 集合；新增 `IMetadataExplorer.GetPartitionsAsync(container, table)` 与 `DbPartitionInfo`，MySQL 用 `information_schema.PARTITIONS` 按库/表筛选，返回分区名、方法、表达式、边界，并在展开表节点时惰性加载 Partition 叶节点（非 MySQL 方言返回空集）。纯逻辑 SQL 测检查表名/库名双重过滤、`PARTITION_NAME IS NOT NULL` 和序号排序；离线 ViewModel 测守住「列 + 分区同为表子节点」。MariaDB 10.5.29 靶测创建 RANGE 分区表，实得 `p0`/`pmax`、`RANGE`、表达式 `` `id` ``、边界 `10`/`MAXVALUE`；表达式保留服务器返回的反引号。
   - 📌 **产品边界（非 M5-3 未完成项）**：SQL Server 不持久化原始 `CREATE TABLE` 文本，表 DDL 由 `DdlGenerator` 重建；SQL Server 服务端分页只覆盖顶层有序简单查询，复杂/不安全形状 fail-closed，已于 2026-10-12 靶测通过（详见 §7）。MySQL `SHOW CREATE TABLE` 保留 `PARTITION BY`，独立分区子树已靶测。
   - 📌 **文档更正（2026-10-12）**：早前本节与 §4 记的「MySQL/PG 定义仍缺」「PG 的 `GetTablesByDatabase` 按模式名过滤」均已过时/有误，见上文与 §4。
   - ✅ **模式层（M5-2 + SQL Server M5-3）**：`DbObjectKind.Schema` + PG `pg_namespace` / SQL Server `sys.schemas` 清单；PG / SQL Server 都只浏览当前库并展现模式层，SQLite/MySQL 不加这一层。SQL Server 新查询及树接线已靶机实测。
4. 树节点惰性加载：展开才查子级。当前壳层展示层级：SQLite/MySQL = 「库 → 非空对象类型组 → 对象 → 列」，PostgreSQL/SQL Server = 「当前库 → 模式 → 非空对象类型组 → 对象 → 列」（SQL Server M5-3 首批查询代码已写并靶机验证通过）。类型组只是壳层节点，不属于 `DbObjectKind`；Core 元数据契约仍按容器取对象。

---

##  AGENTS 工作规则（先读这个）

0. **验证优先**：任何声称"完成"的交付，必须先跑通 `dotnet build DdraigBench.slnx` 与 `dotnet test`，把真实输出（通过数/警告数）贴在交付说明里。不接受口头保证。
1. **小步提交**：每个 milestone 内的子任务独立可编译、可测试。禁止"半成品大爆炸"式提交。
2. **不引新包先读 §2**：任何新增 PackageReference 必须先确认许可证与 Avalonia 12.1.x 兼容性，并更新 §2 表格。
3. **DdraigBench.Core 零 Avalonia 依赖**（§3），违反即重构。
4. 遇到本文档未覆盖的决策：在提交说明里写出决策与理由，并回写本文档。
5. 每个新增 `.cs` 文件顶部保持文件头注释：`// DdraigBench — <模块名>`（可追踪性）。

---

## 7. 测试策略（每轮交付的硬门槛）

| 层 | 方式 |
|---|---|
| Core.Services | xunit 单测；DB 依赖用 **SQLite 临时文件**（`Path.GetTempFileName` 建库），不 mock IFreeSql（集成式更真） |
| GPL 防回归 | 静态断言 + **驱动集真测试**（`GplRegressionTests.cs`）。覆盖边界（勿夸大）：`No_forbidden_package_references_in_any_csproj` 扫**全仓** `*.csproj`（排除 `bin`/`obj`）；`No_forbidden_assemblies_in_test_output` 只查**测试工程输出目录**（`AppContext.BaseDirectory`），壳/Core 的 `bin` 未查；`CanLoadAssembly` 断言 `MySqlConnector`/`Npgsql` 可加载、`MySql.Data`/`FreeSql.Provider.MySql` 加载失败；`MySql_and_PostgreSql_data_types_are_registered` 只建 MySQL/PG 两个 `IFreeSql`（SQLite 注册由 `SqliteSmokeTests` 覆盖） |
| 多方言集成 | 靶机连接串走环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG` / `DDRAIGBENCH_SQLSERVER`；未设置或 `Ado.ExecuteConnectTest()` 不可达即 `Assert.Skip`（无靶机时全绿）。**凭据不进仓库**（`MultiDialectIntegrationTests.cs`）。MySQL/PG 跑目录清单、视图定义与**触发器/例程定义**靶测（`MultiDialectIntegrationTests.RunRoutineDefinitionAsync`：随机命名建表 + 函数 + 过程 + 触发器，逐 kind 断言 `DbObjectInfo.Definition` 非空且 `GetDdlAsync` 含 `CREATE`，finally 清理；PG 触发器须 `DROP TRIGGER … ON <table>`）；SQL Server 基础靶测跑 SELECT、当前库与表元数据/DDL 重建；新增分页靶测创建三行表，检查无序查询 fail-closed、顶层有序查询的 OFFSET/FETCH 语法与首末页结果。新增 SQL Server M5-3 集成测会建随机 schema、表、视图、函数、过程、DML trigger，验证列表、schema 映射、定义和 DDL，再清理；须设置 `DDRAIGBENCH_SQLSERVER` 才真正执行。**2026-10-13 门槛（新增编辑已保存连接用例后）**：离线通道（三个靶机变量都不设）**344 / 成功 312 / 跳过 32 / 失败 0**；接入 `DDRAIGBENCH_MYSQL`+`DDRAIGBENCH_PG`（MariaDB 10.5.29 / PostgreSQL 13.23）**344 / 成功 319 / 跳过 25 / 失败 0**；接入三个靶机变量（另含 SQL Server 2022 Developer）**344 / 成功 321 / 跳过 23 / 失败 0**。**2026-10-13 门槛（新增启动自动重连用例后）**：离线 **356 / 成功 324 / 跳过 32 / 失败 0**；接入 `DDRAIGBENCH_MYSQL`+`DDRAIGBENCH_PG` **356 / 成功 331 / 跳过 25 / 失败 0**；接入三个靶机变量（另含 SQL Server 2022 Developer）**356 / 成功 333 / 跳过 23 / 失败 0**；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误。**2026-10-13 门槛（新增跨页/服务端排序用例后）**：离线 **370 / 成功 338 / 跳过 32 / 失败 0**；接入 `DDRAIGBENCH_MYSQL`+`DDRAIGBENCH_PG` **370 / 成功 345 / 跳过 25 / 失败 0**；接入三个靶机变量（另含 SQL Server 2022 Developer）**370 / 成功 347 / 跳过 23 / 失败 0**；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误。**2026-10-15 门槛（会话并发上限与生命周期落地后）**：离线 **380 / 成功 348 / 跳过 32 / 失败 0**；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误。⚠️ **靶机通道本轮未跑成**：2026-10-15 靶宿主机 192.168.17.130 的 5432/3306/1433 三个端口 **TCP 可建立但协议层全部超时**（原始 PG `SSLRequest` 无任何应答、MySQL 无问候包、SQL Server 无 prelogin 响应），SSH 也握手失败（`Error reading SSH protocol banner` / `No existing session`），9 个 `MultiDialectIntegrationTests` 全部按设计 `Assert.Skip`——**故本轮"接入靶机"的数字与离线一致，不能当作靶机验证**；宿主机恢复后必须补跑 MySQL+PG 与三靶机通道。**2026-10-15 门槛（密钥库回退原因提示落地后）**：离线 **407 / 成功 375 / 跳过 32 / 失败 0**（380→407，新增 `SecretStoreFallbackTests` 26 例 + `MainViewModelTests` 1 例）；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误。⚠️ **靶机通道仍未跑成**（同一宿主机故障延续：3306/5432/1433 协议层无应答），故本轮"接入靶机"的数字与离线一致、**不作为靶机验证**；宿主机恢复后必须补跑。**2026-10-15 靶机补跑（宿主机部分恢复）**：接入 `DDRAIGBENCH_MYSQL`+`DDRAIGBENCH_PG`（MariaDB 10.5.29 / PostgreSQL 13.23）**407 / 成功 382 / 跳过 25 / 失败 0**；三靶机通道**仍不可用**——192.168.17.130 的 1433（SQL Server）与 1434（SQL Browser）均拒绝连接，只有 3306/5432 恢复（MySQL 问候包 `5.5.5-10.5.x`、PG 对 SSLRequest 正确回 `N`）；设置 `DDRAIGBENCH_SQLSERVER` 后 SQL Server 用例按设计连接超时并 `Assert.Skip`，整轮仍为 **407 / 成功 382 / 跳过 25 / 失败 0**（耗时 1m01s，无挂起、无失败）。SQL Server 2022 靶机恢复后必须补跑该档。**2026-10-15 稍后补跑完成（三靶机齐活）**：SSH 登入靶机后发现 SQL Server 是 rootless **podman 容器 `ddb-sqlserver`**（镜像 `mcr.microsoft.com/mssql/server:2022-latest`，映射 `0.0.0.0:1433->1433/tcp`），当时状态为 `Exited (0) 3 days ago`——所谓"1433 超时"其实是容器退出，MySQL/PG 则是原生 systemd 服务（故一直在线）。`podman start ddb-sqlserver` 后约 30 秒日志出现 `Recovery is complete`，1433 恢复监听。随后接入三个靶机变量 → **407 / 成功 384 / 跳过 23 / 失败 0**（19s，9 个方言集成测全部真跑）。**运维经验**：3306/5432 在线而 1433 不在时，先 `podman ps -a` 看 `ddb-sqlserver` 是否 `Exited`，再 `podman start ddb-sqlserver`（无需 sudo，rootless），等 `Recovery is complete` 再跑测试。**2026-10-16 靶机 SQL Server 改为原生安装（弃用 podman）**：按用户要求把 `192.168.17.130` 上的 SQL Server 从 rootless podman 容器换成微软原生包。`dnf` 直装反复失败是因为到 `packages.microsoft.com` 的链路既慢又不稳（HTTPS 频繁 `OpenSSL SSL_connect: SSL_ERROR_ZERO_RETURN`，`dnf` 报 `Curl error (18): Transferred a partial file`，单连接实测仅 20–30 kB/s 且会被中途截断）；最终改成 **HTTP + 4 路并行分段下载**（`curl -sSL -r <start>-<end>`，每段 8 MiB、只追加不覆盖，实测合计 ~110 kB/s）取回 `mssql-server-16.0.4305.5-1.x86_64.rpm`（284,179,645 字节）。首次拼装校验失败（`Payload SHA256 ALT digest: BAD`）——根因是早前用 `curl -C -` 续传留下的坏前缀，重下前 63,519,537 字节再与尾部拼接后 `rpm -Kv` 六项全 OK（`头V4 RSA/SHA256 Signature, 密钥 ID be1229cf: OK` / `Payload SHA256 digest: OK`）。安装与配置：`dnf -y install /var/tmp/mssql-server.rpm` → `MSSQL_PID=Developer MSSQL_SA_PASSWORD=… /opt/mssql/bin/mssql-conf -n setup accept-eula`；服务 `mssql-server.service` 为 `enabled` + `active`，约 20 秒后 1433 监听。原生实例沿用与容器相同的 SA 口令，故 `DDRAIGBENCH_SQLSERVER` 无需改动。旧容器 `ddb-sqlserver` 已 `podman stop` 并保留作回滚（它本就没有任何开机自启配置，不会自己启动；彻底清理可 `podman rm ddb-sqlserver`）。随后三靶机通道复跑 → **407 / 成功 384 / 跳过 23 / 失败 0**，与容器版结果一致。**2026-10-12 M5-3 门槛**：离线 **324 / 292 / 32 / 0**；MySQL+PG **324 / 297 / 27 / 0**；三靶机 **324 / 301 / 23 / 0**。分区子树靶测建 RANGE 表并展开，核对分区名、方法、表达式、边界后清理；其中 `MySql_events_come_back_from_the_catalog` 对周期/一次性事件都做定义重执行并验证状态保真；该测试建随机表与事件、校验对象清单和 DDL 预览，并在 `finally` 清理。`DDRAIGBENCH_SQLSERVER`（SQL Server 2022 Developer）2026-10-11 历史门槛为 **309 / 成功 282 / 跳过 27 / 失败 0**（当时尚未加入后续 MySQL/PG 测试）。故 M5-3 的 SQL Server 首批目录、MySQL/PG 触发器/例程定义、PG 物化视图、MySQL 原始表定义与计划事件均已靶机验证通过。SQL Server 顶层有序简单查询的直接 OFFSET/FETCH 分页已在 2026-10-12 用 SQL Server 2022 Developer 靶机验证；不安全形状 fail-closed。表 DDL 因 SQL Server 不保留原始 CREATE TABLE 文本，继续使用元数据重建。**2026-10-16 测试债清零门槛（headless UI 线程 + 线程归属红测）**：离线 **415 / 成功 383 / 跳过 32 / 失败 0**（407→415：`EditorTextTests` 5 例 + `WindowTests` 2 例 + `SessionThreadAffinityTests` 1 例），同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误。⚠️ **靶机通道本轮未能复跑**：`192.168.17.130` 当日 `ping` 100% 丢包、SSH 报 `Connection timed out during banner exchange`，3306/5432/1433 虽能被 TCP 应答但协议层全无响应（MySQL 无问候包、PG 对 `SSLRequest` 无回、SQL Server 无 prelogin），两个靶机通道的方言集成测全部按设计 `Assert.Skip`（得到的数字与离线一致，**不作为靶机验证**）；宿主机恢复后须按同一批 415 个用例补跑。**2026-10-16 门槛（第八批排序边界打磨后）**：离线 **425 / 成功 393 / 跳过 32 / 失败 0**（415→425：`PagedSqlTests` 净增 6 例、`QueryTabViewModelTests` 净增 4 例，另各改写 1 例旧契约）；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误；靶机通道本轮未复跑（改动全在纯逻辑层与壳层 VM 层，离线真 SQLite 已覆盖），宿主机可按同一批 425 个用例补跑。**2026-10-16 门槛（第九批：双击桥接控件级测试 + 死代码清理）**：离线 **436 / 成功 404 / 跳过 32 / 失败 0**（425→436：新增 `ActivationBridgeTests` 11 例）；同一轮 `dotnet build DdraigBench.slnx -c Release` 0 警告 0 错误；靶机通道本轮未复跑（改动全在壳层控件与测试，Core 一行未动），宿主机可按同一批 436 个用例补跑。**本机靶机凭据走 `/tmp/ddb/env.sh`（0600，勿 cat），用 `set -a && . /tmp/ddb/env.sh && set +a` 注入** |
| 目录查询（纯逻辑） | `CatalogQueryTests`：方言路由、能力矩阵（含 `Schema`）、SQL 形状（目标系统表/收窄条件）、限定名与**转义**（SQLite `''`、MySQL `\\`+`` `` ``、PG 双引号、SQL Server Unicode 字面量与方括号别名）、单库绑定标志——**不依赖任何数据库即可全绿** |
| 密钥库集成 | **Linux**：真访问本机 freedesktop Secret Service（`SecretServiceKeyProviderTests.cs`），**用唯一 `application` 属性隔离并结束即删**，不污染用户真实凭据；无会话总线/服务即 `Assert.Skip`。**macOS**：真访问 login keychain（`MacKeychainKeyProviderTests.cs`），唯一 `service` 隔离 + `suppressUi`（禁授权框）+ 结束即删；非 macOS `Assert.Skip`。信封加密本身一律用**假主密钥**（`FakeMasterKeyProvider`）纯单测，CI 无密钥库也全绿 |
| ViewModels | 纯逻辑测（不启 Avalonia）；测试工程 ProjectReference 壳工程；交互留到 Avalonia.Headless（M3 再引入）。**替身分层**：`StubXxx`（对话框/选择器）与 `ScriptedMetadataExplorer`（可编排「库→模式→对象」形状，让树的多层逻辑无需靶机即可测透，M5-2） |
| 交付门槛 | `dotnet build DdraigBench.slnx` 零警告零错误 + `dotnet test` 全绿，输出贴进交付说明 |

> ✅ **Headless UI 测试已全通（2026-09-18 接通，2026-10-16 补齐 Dispatcher 线程）**：**`Avalonia.Headless 12.1.3`（基础包，无 xunit 耦合）+ xUnit v3 `AssemblyFixture`**（`DdraigBench.Test/TestAppFixture.cs`）。
> - **2026-10-16 的改动（关键）**：fixture 不再是"在 assembly-fixture 线程上 `SetupWithoutStarting()`"了事——它起一条**专用线程**（`IsBackground = true`、`Name = "ddb-headless-ui"`），在**那条线程上**完成 `AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting()`（于是 `Dispatcher.UIThread` 与 Compositor 都归它），再用 `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)` 通知就绪，然后调 `Dispatcher.UIThread.MainLoop(_shutdown.Token)`；`Dispose()` 里 `_shutdown.Cancel()` + `_uiThread.Join(TimeSpan.FromSeconds(10))`。测试体一律经 `DdraigBench.Test/TestUiThread.cs` 的 `Run(Action)/Run<T>`（→ `Dispatcher.UIThread.Invoke`）或 `RunAsync(...)`（→ `InvokeAsync(...).GetTask()`）编组到这条线程。
> - **之前为什么不行**：旧写法下那条 UI 线程**没有消息循环**，`InvokeAsync` 排进去的任务永远没人执行（这就是 `HeadlessUnitTestSession` 在 MTP + fixture 组合下"挂死"的真因），而测试体跑在线程池线程上、Compositor 却归 fixture 线程，`VerifyAccess` 自然抛异常。
> - **现在能用**：除既有的 `ResultColumns` 动态列 / 列类型对齐 / 表头排序按钮（`HeadlessUiTests`）外，`EditorTextTests`（5 例：附加属性推入编辑器、编辑器输入回流、重复赋值不打环、`editor.Bind(EditorText.TextProperty, new Binding(nameof(QueryTabViewModel.Sql)) { Mode = TwoWay })` 真双向绑定、真实 `QueryTabView` 单例断言）与 `WindowTests`（2 例：`new Window{...}.Show()`/`Close()`、窗口内挂 `QueryTabView`）已纳入回归。
> - ✅ **已解决（2026-10-16）**：需要 UI 线程 Dispatcher 的控件（`TextEditor`）现在可以构造并测透——`EditorTextTests` 5 例覆盖附加属性推入/回流、重复赋值不打环、`Binding` 真双向绑定与真实 `QueryTabView`。历史坑仍在，**别踩**：`Avalonia.Headless.XUnit 12.1.2` 钉 `xunit.v3.extensibility.core 3.2.2`，与本工程的 `xunit.v3 4.0.1` 不兼容（discovery 阶段即 `MissingMethodException: TestIntrospectionHelper.GetTestCaseDetails`）——故不走它的 `[AvaloniaFact]`；`HeadlessUnitTestSession.StartNew(...).Dispatch(...)` 也不必再用（`TestUiThread` 更直接），它当年在 MTP + fixture 组合下实测两次挂死，真因是那条线程没有消息循环。
> - ✅ **`Window`/`TopLevel` 的问题已在 2026-10-16 解决**：fixture 把平台初始化搬进**自己的专用线程**并运行该线程的 `MainLoop` 之后，`new Window{...}` / `Show()` / 窗口内挂 `QueryTabView` 都能跑（`WindowTests` 2 例）。历史记录（2026-10-11 实测两次）：`new MainWindow()` 立即抛 `System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.`（栈：`Dispatcher.VerifyAccess` ← `Compositor.RegisterForSerialization` ← `CompositionVisual.InitializeDefaults` ← `PresentationSource..ctor` ← `TopLevel..ctor` ← `Window..ctor`）——根因是那条初始化线程**没有消息循环**、Compositor 归它而测试体在线程池线程，**不是并发偶然**；红绿：把 `TestUiThread.Run` 改成直接调用 → `EditorTextTests`/`WindowTests` 恰好 7 个全红。注意**视觉验收仍是人工项**：自动化只能证明"能构造、能 `Show()`、控件在树里"，像素级外观仍需人工截图。
> - ✅ **外观/强调色可以在 headless 里守住**（2026-10-11 起）：`DdraigBench.Test/ThemeContrastTests.cs` 走「样式表断言 + 继承机制 + WCAG 对比度」三路——查 `Application.Current.Styles` 里有没有隐式 `TextBlock` 样式带 `Foreground` setter、`Style Selector="Window"` 是否带 `Window.ForegroundProperty`、按钮 `Foreground` 是否继承到内容 `TextBlock`（`Assert.Same(button, label.Parent)`）、以及 `TryFindResource(key, ThemeVariant.Light, …)` 取色算对比度 ≥ 4.5，全程不构造窗口。

---

## 8. Milestones 与验收

- [x] **M1 黄金闭环（SQLite）**：Provider.Sqlite 进主工程+测试工程 → Core 工程（Connections/Metadata/Querying）→ MainWindow 连 SQLite 文件 → 左树展开 → `SELECT 1` 出网格。验收：xunit 全绿 ✅ + GPL 防回归测试进仓库 ✅ + 手动截图 ✅（2026-10-11 人工验收通过）。
- [x] **M2 多方言**：SQLite、MySQL/MariaDB、PostgreSQL、SQL Server 均已接入连接层；MySQL/PG 和 SQL Server 有靶机集成覆盖。
  - [x] M2-a：MySqlConnector / PostgreSQL 接入并接靶机实测通过；GPL 防回归升级为真测试（§7）。（"MasterPool 冒烟测试回写 §4" 已在 M1-1 完成。）
  - [x] M2-b：连接对话框按 Provider 出参数表 + Provider 友好名 + 测试连接按钮。
  - [x] M2-c（2026-10-11）：SQL Server Provider、连接对话框与连接串/TLS 选项接入；新增 SQL Server dialect/catalog routing，查询、当前库读取、表元数据/DDL 重建已用 SQL Server 2022 Developer 靶机验证。自动服务端分页关闭；当时目录对象和原始定义未实现；M5-3 随后补齐首批 SQL Server schema/DML trigger/例程/视图定义代码，并已在同一 SQL Server 2022 Developer 靶机实测通过（随机 schema + 表/视图/函数/过程/触发器建删全链路，2026-10-11）。
- [x] **M3 编辑器与会话**：SQL 编辑器（AvaloniaEdit 12.0.0，§2.7 实查通过）；事务模式（LeaseRawAsync）；取消执行；结果网格**服务端分页 + 行虚拟化**（M3-1..M3-4 均已完成，2026-10-05）。
- [x] **M4 生产力功能**：连接凭据加密存储（三平台 + UI 接线）、导出 CSV/INSERT、DDL 预览、ISqlDialect 落地。**已全部完成**（2026-10-05，含 M4-4 的壳工程接线）。
- [ ] **M5 元数据完整性**（2026-10-08 起）：把 IDbFirst 的缺口补齐到"用户可见、可测"。
  - [x] **M5-1 目录查询层 ✅（2026-10-08）**：`Dialects/CatalogQueries/`（`ICatalogQueries` + SQLite/MySQL/PG 三实现 + `CatalogQuerySets` 工厂，只造 SQL）+ `Metadata/CatalogQueryExecutor`（执行 + 按列名映射）+ `DbObjectInfo.Definition`；`FreeSqlMetadataExplorer` 改为"IDbFirst + 目录查询"双通道，`GetObjectsAsync` 支持 Trigger/StoredProcedure/Function（方言没有即空集，不报错），`GetDdlAsync` 改为**原始定义优先**（表：SQLite 保留 AUTOINCREMENT/UNIQUE/CHECK/COLLATE；视图：三方真实定义；触发器/例程：按名取定义）。壳层：库节点平铺触发器/例程、`CanPreviewDdl` 放开这三类、展开失败收敛成状态栏（fire-and-forget 异常不再被吞）。顺带修 **MySQL 字面量反斜杠未转义**（导出 INSERT 会静默改写含 `\` 的数据）。测试 **218 个（204 通过 / 14 跳过 / 0 失败）**，构建 0 警告 0 错误。
  - [x] **M5-2 模式层与 PG 库清单 ✅（2026-10-08）**：`DbObjectKind.Schema` + `ICatalogQueries` 增 `ConnectionIsBoundToSingleDatabase` / `BuildCurrentDatabaseQuery`；`FreeSqlMetadataExplorer.GetDatabasesAsync` 对 PG 收窄为 `SELECT current_database()`（原先会把「看得见打不开」的库列进树）、`GetObjectsAsync` 支持 `Schema`（SQLite/MySQL 空集）；PG 模式清单滤掉 `pg_catalog`/`information_schema`/`pg_toast`/`pg_temp%`。壳层树变为「库 → 模式 → 对象」（有模式才加层，无模式保持 M5-1 原样），`ConnectionTreeNodeViewModel.Database` **改名 `Container`**（PG 下对象容器是模式名）。**修的既有缺陷**：PG 下库名被当作模式名查表，树通常展不开。测试（该轮实测）**227 个（213 通过 / 14 跳过 / 0 失败）**，构建 0 警告 0 错误。
  - [x] **全仓代码审计修复 ✅（2026-10-09）**：对整个仓库做只读审查（1 critical / 5 high / 10 medium / 14 low / 7 nit + 一组文档漂移）后按序修复，全部带红测对照（停用修复即红、恢复即绿）。**本轮最终实测**：`dotnet build DdraigBench.slnx -c Debug` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Debug` → **总计 271 / 成功 244 / 跳过 27 / 失败 0**（修复前基线 236 / 212 / 24 / 0；该轮走 Debug 通道，第二批收口后 Release 通道为 281 / 254 / 27 / 0、第三批后为 293 / 266 / 27 / 0，见下）。逐条：
    · **C1** `MainViewModel` 读取连接清单失败后不再保留半成品 store（catch 里 `_store = null;`），杜绝用空清单覆盖 `connections.json` 抹掉读不出的密文条目；`MainViewModelTests.Failed_load_disables_persistence_instead_of_overwriting_the_file` 钉住。
    · **H1** 查询执行重入守卫（`QueryTabViewModel.CanRun` + `RunAsync` 顶部 `if (IsExecuting) return;` + `QueryTabView.axaml`/`MainWindow.axaml` 的 `IsEnabled`），防并行两次执行把 `_cts` 抢掉。
    · **H2** 连接并发守卫（`MainViewModel._isConnecting` + `ConnectCoreAsync` 拆分）与**连接失败清理会话**（dispose + 置 null + 刷新 `IsConnected`/`ActiveConnectionName`/`CanPreviewDdl`），不再留死会话。
    · **H3** 主题偏好读取移出崩溃路径（置到 `InitializeAsync` 末尾），坏 `ui-settings.json` 不再穿透 `App` 的 `async void`。
    · **H4** `DdlGenerator` 列注释不再吞掉行尾逗号/FK 行（`RenderBody` + `(Definition, Comment)` 元组），DDL 重新可执行。
    · **H5** 四处冒充跳过的 `if (!OperatingSystem.IsWindows()) return;` 改为真断言/真跳过，不再产生零断言假绿。
    · **M1/M2** `PagedSql` 单遍状态机重写：识别 `--`/`/* */` 注释与引号内分号，多语句脚本不再被包派生表（`SELECT 1; SELECT 2` 原样放行）。
    · **M3** 目录清单静默截断改为抛 `InvalidOperationException`（`CatalogQueryExecutor.MaxRows` 上限，不再"树静默少一截"）。
    · **M4** 事务开/提交/回滚移到后台线程（`Task.Run`），并把 `DisposeAsync` 纳入 `try` —— 顺带修掉租约漏还；**唯一无红测**（`DbSession` sealed、无线程观测缝），靠既有真 SQLite 事务往返测兜底。
    · **M5** `ConnectionStore` 校验 `Version`（高版本拒绝加载而非覆写）+ `"connections": null` 不再 NRE。
    · **M6** `ConnectionStringBuilder` 对含 `;`/`'`/引号/首尾空白的值加引号并翻倍 `'`，用真 MySqlConnector/Npgsql/SQLite 驱动反解验证（含"值里塞 `;Uid=evil` 偷改关键字"用例）。
    · **M7** 连接清单探测/读取（含 macOS 同步 P/Invoke、逐条 Unprotect）移出 UI 线程（`Task.Run` + UI 线程取快照），自建非池线程调用断言线程归属。
    · **M10** 取消与错误契约分离：`QueryRunner` 的 `catch` 加 `when (ex is not OperationCanceledException)` + 读取后 `ct.ThrowIfCancellationRequested()`，取消不再被吞成 `QueryResult.Error`。
    · **文档回写**：README（测试数/包版本/三条"未实现"/`dotnet test` 写法/目录路径）与本文档 §3/§7/§2.5 已同步；`GplRegressionTests` 的方法名不再声称覆盖 SQLite。
    · **当轮未纳入**（2026-10-11 的**第二批**已把其中不需靶机者全部清掉，见下条）：M8 删除已保存连接无 UI 入口 ✅、M9 树节点展开失败后不可重试 ✅、A3 `WorkbenchLayout` 折叠再展开行高比例漂移 ✅、C7 BLOB 显示为 `System.Byte[]` ✅、C4 `SetThemeCommand` 死命令 ✅、C5 Autofac 死重 ✅、A2 `App.axaml` 隐式 `TextBlock` 样式压掉强调按钮文字色 ✅（当轮先核实为真缺陷）。**仍未纳入**：树/网格的更多视觉打磨（2026-10-11 的**第三批**又把 C6 与 C9 清掉、并实现结果网格的列类型感知与当前页排序，见下条）。
    · **A2 证据（2026-10-11）**：浅色主题下「运行」按钮（`MainWindow.axaml:72`）内文字 `TextBlock.Foreground` 实测 `#FF1B2430`，而该 `Button.Foreground` 是 `White`、`Background` 是 `#FF155A9C` —— `App.axaml:15-17` 的隐式 `Style Selector="TextBlock"` 盖掉了从 Button 继承的强调色文字，蓝底压深灰、对比度约 2.2:1。
  - [x] **第二批修复（2026-10-11）✅**：清掉第一批"未纳入"里**不需要靶机**的项，全部带红测对照（临时回退修复 → 见红 → 恢复 → 全绿）。逐条：
    · **A2 强调按钮文字色**：`App.axaml` 去掉隐式 `Style Selector="TextBlock"`，把 `Foreground="{DynamicResource WorkbenchTextBrush}"` 并入 `Style Selector="Window"`——按钮自身 `Foreground` 在继承链上更近而胜出。回归 `DdraigBench.Test/ThemeContrastTests.cs`（4 测）：隐式 TextBlock 样式不得带 Foreground setter（恢复旧样式即红）、`Window` 样式必须带 `Window.ForegroundProperty`、按钮 `Foreground` 会继承到内容 `TextBlock`（`Assert.Same(button, label.Parent)`）、浅色主题强调文字对比度 ≥ 4.5（自带 WCAG 计算，旧配色实测约 2.2:1）。
    · **C2 删除已保存连接入口（M8）**：`MainWindow.axaml` 左栏标题栏改 `Grid ColumnDefinitions="*,Auto,Auto"`，新增 `Delete` 图标按钮绑 `ForgetSavedConnectionCommand` + `IsEnabled="{Binding CanForgetSavedConnection}"`（未选中时禁用）。
    · **C3 树展开失败可重试（M9）**：`ConnectionTreeNodeViewModel` 增 `LoadFailed` / `ReportLoadFailure()`（`_loaded = false` + 丢掉失败前塞进去的半个子树 + 置 `LoadFailed`）/ `ReloadAsync()`；`MainViewModel` 三处 loader catch 调 `ReportLoadFailure()`，新增 `RetryLoadCommand`/`RetryLoadAsync(node)`；对象树右键菜单增「重试加载」（`IsVisible="{ReflectionBinding LoadFailed}"`）。VM 回归 2 个（折叠后重新展开即重试、右键重试并丢弃半成品子树；用新增替身 `FlakyMetadataExplorer` 编排"前 N 次失败"）。
    · **A3 折叠再展开的比例漂移**：`WorkbenchLayout` 的 `NavigatorState` 增 `WorkspaceWidth`、`QueryState` 增 `ResultsHeight`——`GridSplitter` 拖动后参与的行/列都会变成绝对像素，只还原前两行/列就会漂移；现折叠时记三行/三列、展开时全还原（折叠态把末行/末列置 `1*` 吸收空间）。Headless 回归 2 个（`RowDefinitions "421,5,334"` / `ColumnDefinitions "320,5,845"`）。
    · **C7 BLOB 显示**：`ResultColumns` 单元格改走 `Format(object?)` —— `byte[]` 显示 `0x` + 十六进制，超 32 字节只截前缀并标注 `…（共 N 字节）`，不再显示 `System.Byte[]`。Headless 回归 2 个。
    · **C4/C5 清理**：删除 `SetThemeCommand`（与主题下拉框重复的死命令，`SetThemeAsync` 保留）；从 `DdraigBench.csproj` 删除 `ReactiveUI.Avalonia.Autofac` 12.1.6（§2.1 / §2.4 / §5 已同步）。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 281 / 成功 254 / 跳过 27 / 失败 0**（第一批后基线 271 / 244 / 27 / 0）。README 的已知限制 3/4/5/6/7 与路线图已同步。
  - [x] **第三批修复（2026-10-11）✅**：清掉第二批"仍未纳入"里**不需要靶机**的剩余项，并补上结果网格的两个能力；全部带红测对照（临时回退修复 → 见红 → 恢复 → 全绿）。逐条：
    · **C6 导出默认路径**：`ExportDialogViewModel` 的默认值由裸文件名（相对进程工作目录，快捷方式启动时会落到 `System32` 之类）改为 `DefaultPath(...)`——「文档」→ 用户目录 → 回落文件名的**绝对**路径；`BrowseAsync()` 交给 `IFilePicker` 的第 4 参改 `Path.GetFileName(FilePath)`（它进的是 `FilePickerSaveOptions.SuggestedFileName`，传整条路径会把目录显示到「文件名」框里）。回归 3 个（`ExportFlowTests`：两条默认路径断言改「根路径 + 文件名」，新增 `Default_export_path_is_rooted_so_it_does_not_land_in_the_process_directory`；红测：改回裸文件名 → 2 红）。
    · **C9 清理**：删掉 `DdraigBench.Test/UnitTest1.cs`（只有一个空 `Test1()`，零断言假绿）；删掉空目录 `DdraigBench/Models/` 与 csproj 里的 `<Folder Include="Models\"/>`。
    · **列类型感知（结果网格）**：新增 `Core/Querying/ResultCellKinds.cs`（`Infer`/`Classify`/`Compare`/`Sorted`/`Cell`，纯逻辑、可单测）——`QueryResult` 本身不带类型元数据，只能按**已读回的值**推断：数字与日期右对齐、文本左对齐（单元格 `TextBlock` 要 `HorizontalAlignment = Stretch`，否则缩到文字宽度、对齐看不出效果）；两种类型混在同一列就退回纯文本比较。
    · **表头点击排序**：`ResultColumns` 增 `Cells`/`Sort` 附加属性——`Sort` 非空时表头换成透明 `Button`（`CommandParameter = 列号`）绑 `QueryTabViewModel.SortCommand`，为 null 时保持纯文本（不假装能排序）。比较规则：null **两个方向都排最后**、数字按数值（`Convert.ToDecimal`，溢出退 `double`）、日期按时间点（ticks）、`byte[]` 逐字节再比长度、同分稳定排序。**只排当前页已读回的行**——服务端分页下手里没有整表；故 DataGrid 保持 `CanUserSortColumns="False"`，不用它自带的排序（`DataGridTemplateColumn` 无 `SortMemberPath`，索引式 `object?[]` 行也进不了 `DataGridCollectionView`）。重新执行 / 翻页即清排序（`ResetSort()` 放在唯一让结果落地的私有 `RunAsync` 里）。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 293 / 成功 266 / 跳过 27 / 失败 0**（第二批后基线 281 / 254 / 27 / 0；本批 +12 测 = `ResultCellKinds` 7 + `QueryTabViewModel` 2 + `HeadlessUi` 3，− C9 的 1 个空测）。README 的徽章 / 用例数 / 查询段特性 / 已知限制 5·7·8 / 路线图已同步。
  - [x] **第四批改进（2026-10-13）✅**：把结果网格的表头排序从"只排当前页"升级为**跨页/服务端排序**，并补齐启动自动重连与编辑已保存连接两项连接清单交互（后两者见 §5 与 §8 M4-4 接线）。全部带红测对照。逐条：
    · **排序下推（`PagedSql` + `ResultSort`）**：新增 `Core/Querying/ResultSort.cs`（`readonly record struct ResultSort(int ColumnIndex, string ColumnName, bool Descending, bool ColumnNameIsUnique = true)`，列名唯一性服务于 SQL Server 的按名回退）。`PagedSql.Build` 增 5 参重载 `Build(sql, dialect, pageSize, offset, ResultSort? sort, out bool sortApplied)`；原 4 参重载委托给它以保持兼容。SQLite/MySQL/PG 把原语句包派生表后在**外层**追加 `ORDER BY <序号> ASC/DESC`（排序先于 `LIMIT/OFFSET`，故翻页仍全局有序）；SQL Server 无顶层 `ORDER BY` 时直接补 `ORDER BY [列名] ASC/DESC` + `OFFSET/FETCH`（T-SQL 的 `ORDER BY 1` 是常量不是列序号，故 `SqlServerDialect.SupportsOrdinalOrderBy => false`），已有顶层 `ORDER BY` 时保持用户语义**只分页不排序**；列名重复或形如 `COUNT(*)` 的表达式列名（`IsSimpleColumnName` 挡掉）宁可不排序也不产出会报错的 SQL。`sortApplied` 为 `false` 时调用方必须退回客户端排序。`ISqlDialect` 增 `SupportsOrdinalOrderBy`（`SqlDialectBase` 默认 `true`）。
    · **`QueryTabViewModel`**：排序态由 `int? _sortColumn` + `bool _sortDescending` 换成 `ResultSort? _sort` + `bool _serverSortApplied`；`SortCommand` 改 `ReactiveCommand.CreateFromTask<int>(SortByAsync)`（公开 `public async Task SortByAsync(int index)` 供测试直接 await），下推成功则重跑整条查询，否则退回 `ResultCellKinds.Sorted` 只排本页；`ExecuteAsync()` 回第 1 页并清排序；状态栏区分"整表排序"与"当前页 N 行排序"。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 370 / 成功 338 / 跳过 32 / 失败 0**；接入 MariaDB 10.5.29 + PostgreSQL 13.23 → **370 / 成功 345 / 跳过 25 / 失败 0**；再接入 SQL Server 2022 Developer → **370 / 成功 347 / 跳过 23 / 失败 0**。README 的徽章 / 用例数 / 已知限制 8 / 路线图已同步。
  - [x] **第五批改进（2026-10-15）✅**：补上 §3 自 2026-10-11 起记的缺口——**会话并发上限与生命周期**（`ISessionManager` 此前从未创建，会话语义由 `DbSession` + VM 各自承担）。全部带红测对照。逐条：
    · **`Core/Sessions/ISessionManager.cs` + `SessionManager.cs`**：`Active` 只读活动会话；`MaxOpenSessions`（默认由 GUI 侧传 1）；`OpenAsync(profile)` 在已达上限时**抛 `InvalidOperationException` 且不改状态**（把"连接泄漏"变成响亮失败，而不是静默多挂一条连接）；`CloseAsync(session)` 对不属于自己的会话返回 `false`（幂等、不误关他人）；`CloseAllAsync()` 幂等且**后开先关**；所有状态读写在同一把锁内，避免并发下的"上限被绕过"。
    · **`MainViewModel`**：删除自持的 `_session` 与手写 `new DbSession(...)`/`DisposeAsync()`，改为 `private DbSession? Session => _sessions.Active` 只读别名 + 公开 `Sessions` 属性（供 `App` 与测试使用）；`ConnectCoreAsync` 的策略是**先 `CloseAllAsync()` 再 `OpenAsync(profile)`**——上限为 1 时忘了关旧会话会立刻抛错暴露出来；`MainViewModel` 实现 `IAsyncDisposable`。
    · **`App` 退出路径**：`Desktop.ShutdownRequested` 里先 `e.Cancel = true`，`await viewModel.DisposeAsync()` 之后再 `desktop.Shutdown()`（第二次请求靠 `_shutdownRequested` 放行）——否则退出时 FreeSql 连接池不释放。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 380 / 成功 348 / 跳过 32 / 失败 0**。⚠️ **靶机通道本轮未跑成**（2026-10-15 靶宿主机 192.168.17.130 的 3306/5432/1433 协议层全部超时、SSH 亦握手失败，9 个方言集成测按设计 `Assert.Skip`），故本轮**没有**可报的靶机数字，宿主机恢复后必须补跑。README 的徽章 / 用例数 / 路线图已同步。
  - [x] **第六批改进（2026-10-15）✅**：把「密钥库不可用时只回退明文、不说原因」补成**可操作的失败原因**，并复核 libsecret 直连方案（结论：维持 D-Bus，见 §2.8）。全部带红测对照。逐条：
    · **回退原因分类（`Core/Secrets/SecretStoreFallback.cs`）**：`SecretStoreFallbackReason`（未知 / 无密钥库 / 平台不支持 / 服务不可达 / 拒绝访问 / 已锁定 / 缺授权 / 用户取消）+ `FromMacStatus(int)`（-128 / -25291 / -25293 / -25308 / -34018 映射，常量引用 `MacSecurityFramework.Status*` 而非魔法数字）+ `FromFailure(Exception)`（`PlatformNotSupportedException` → 平台不支持；类型名或消息含 `DBus` / `D-Bus` / `session bus` → 服务不可达；传入 `null` 抛 `ArgumentNullException`）+ `Describe(reason, platformDetail)`（翻成可操作中文；未知原因带 OSStatus 原文，不把诊断信息吞掉）。
    · **原因随保护器传递**：`ISecretProtector` 加默认接口成员 `string? FallbackReason => null;`（加密保护器天然为 null，无需逐个实现）；`PlainTextSecretProtector` 主构造函数收 `fallbackReason`，`SecretProtectors.CreateDefaultAsync` 探测失败时把平台细节拼成中文句子再回退。`MacKeychainKeyProvider` / `SecretServiceKeyProvider` 各自记录失败原因（`UnavailableReason`，另有 `UnavailableStatus` / `UnavailableDetail` 供组装文案）。
    · **UI 文案**：`MainViewModel.PlaintextFallbackWarning(protectorName, reason)` 有原因就用原因（可操作，如「请在"钥匙串访问"中删除 DdraigBench 条目后重试」），没有原因才回落原来的「未检测到可用的系统密钥库」。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 407 / 成功 375 / 跳过 32 / 失败 0**（380→407：`SecretStoreFallbackTests` 26 例 + `MainViewModelTests` 1 例）。⚠️ 当时靶机通道未跑成（宿主机 3306/5432/1433 协议层无应答）；**随后补跑**：接入 MySQL + PostgreSQL → **407 / 成功 382 / 跳过 25 / 失败 0**，SQL Server 靶机（1433）当日拒绝连接故其用例按设计跳过；**随后查明是该机 rootless podman 容器 `ddb-sqlserver` 处于 `Exited`，`podman start` 后约 30 秒 `Recovery is complete`，三靶机通道补跑为 407 / 成功 384 / 跳过 23 / 失败 0**（详见 §7 补跑条目）；**2026-10-16 该靶机的 SQL Server 已按用户要求改为微软原生 `mssql-server` 包**（systemd 常驻，非 podman；旧容器已停止保留作回滚），三靶机通道复跑仍为 **407 / 成功 384 / 跳过 23 / 失败 0**（见 §7）。README 的徽章 / 用例数 / 已知限制 / 路线图已同步。
  - [x] **第七批改进（2026-10-16）✅**：清零 README「已知限制」里的两项测试债——**需要 UI 线程的控件无法在测试里构造**、**线程归属类修复没有红测对照**。全部带红绿对照。逐条：
    · **headless UI 线程（`DdraigBench.Test/TestAppFixture.cs` + 新增 `TestUiThread.cs`）**：fixture 改为在**专用线程**（`IsBackground = true`、`Name = "ddb-headless-ui"`）上 `SetupWithoutStarting()`（于是 `Dispatcher.UIThread` 与 Compositor 都归这条线程），就绪经 `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)` 通知，随后 `Dispatcher.UIThread.MainLoop(_shutdown.Token)`；`Dispose()` 里 `_shutdown.Cancel()` + `_uiThread.Join(TimeSpan.FromSeconds(10))`。测试体一律经 `TestUiThread.Run/T.Run<T>/RunAsync` 编组到该线程。**根因**：旧 fixture 所在的那条线程**没有消息循环**，`InvokeAsync` 排进队列的任务永不执行——这才是 `HeadlessUnitTestSession`「挂死」的真因；而测试体跑在线程池上、Compositor 归 fixture 线程，`new Window()` 必然在 `Dispatcher.VerifyAccess` 抛异常。新增 `EditorTextTests`（5 例：`EditorText` 附加属性推入/回流、重复赋值不打环、经 `editor.Bind(EditorText.TextProperty, …)` 的真双向绑定、真实 `QueryTabView` 内 `TextEditor` 单例）与 `WindowTests`（2 例：`new Window{…}.Show()/Close()`、窗口内挂 `QueryTabView`）。
    · **线程归属观测缝（`Core/Sessions/DbSession.cs` + `ConnectionLease.cs` + 新增 `DdraigBench.Test/SessionThreadAffinityTests.cs`）**：`DbSession` 加 `internal Action<string>? WorkObserved`（仅测试赋值，生产恒 `null`，只多一次判空），`LeaseRawAsync` 报 `lease-acquire`、`ConnectionLease.Dispose/DisposeAsync` 报 `lease-return`。用例在**自建非池线程**（冒充 UI 线程，避免线程池复用 id 让断言不稳）上跑 `BeginTransactionAsync()` 与 `CommitAsync()`，断言取还租约**从未**发生在调用线程上——去掉 `QueryTabViewModel.BeginTransactionAsync`/`EndTransactionAsync` 里的 `Task.Run` 各**恰好 1 红**（分别报 `[("lease-acquire",16),("lease-return",6)]` 与 `[("lease-acquire",10),("lease-return",16)]`）。
    · **红绿**：把 `TestUiThread.Run` 改成直接执行 → **恰好 7 个新测全红**（`System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.`，栈 `Avalonia.Threading.Dispatcher.<VerifyAccess>…`）；`cp` 还原后逐文件 `diff -q` 一致（RESTORED-EXACT）。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 415 / 成功 383 / 跳过 32 / 失败 0**（407→415：`EditorTextTests` 5 + `WindowTests` 2 + `SessionThreadAffinityTests` 1）。⚠️ **靶机通道本轮未能复跑**（`192.168.17.130` `ping` 100% 丢包、SSH `Connection timed out during banner exchange`；3306/5432/1433 仅 TCP 可连、协议层无应答，方言集成测全按设计 `Assert.Skip`），得到的数字与离线一致、**不作为靶机验证**，宿主机恢复后须按同一批 415 个用例补跑。README 的徽章 / 用例数 / 已知限制（4、5 条）与 §7 已同步。
  - [x] **第八批改进（2026-10-16）✅**：清零 README 路线图最后一条「结果网格排序的剩余边界打磨」。四处行为变更 + 一处死代码清理，全部带红绿对照。逐条：
    · **① 用户自带的顶层 `LIMIT/OFFSET` 不再挡住服务端分页与跨页排序（包装方言）**：旧 `PagedSql.cs` 里 `!scan.StartsWithQuery || scan.HasTopLevelPaging || scan.MultipleStatements` 无条件拒绝改写；现 `HasTopLevelPaging` **只在 `dialect.RequiresTopLevelOrderByForPaging`（即 SQL Server）时**才拒绝。理由：包装方言把用户语句连同它自己的 `LIMIT` 关进派生表完全合法——`SELECT * FROM (SELECT id FROM t ORDER BY id LIMIT 10) AS "ddb_page" ORDER BY 1 DESC LIMIT 5 OFFSET 10`——于是"我先 `LIMIT` 十万行"不再等于"排序只对本页有效"。SQL Server 不能包派生表，仍 fail-closed（`HasTopLevelPaging || HasTsqlPagingConflict` → 整条 `null`）。
    · **② 列名唯一性改为大小写不敏感**：新增 `ResultSort.For(IReadOnlyList<string> columns, int index, bool descending)`（`Core/Querying/ResultSort.cs`），`ColumnNameIsUnique = columns.Count(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) == 1`；`QueryTabViewModel.SortByAsync` 改用它。理由：T-SQL 默认排序规则（及 MySQL 常见配置）下列名不区分大小写，`SELECT ID, id …` 用 `ORDER BY [id]` 会被引擎判 ambiguous——旧写法 `c == name`（Ordinal）把这种重名列**误判为唯一**，等于 fail-open 生成一条报错 SQL。
    · **③ 排序/翻页与编辑器文本绑定**：`QueryTabViewModel` 新增 `private string? _executedSql`（`RunAsync` 在取消检查之后记录**被执行的**那一段文本）与 `private bool EditorMatchesLastRun() => _executedSql is null || string.Equals(Sql, _executedSql, StringComparison.Ordinal)`。`NextPageAsync`/`PreviousPageAsync` 在 `HasNextPage`/`HasPreviousPage` 守卫之后、**改页码之前**核对文本，不一致就写 `StaleEditorStatus`（「编辑器内容已改动，未重跑查询：请先执行（F5）再翻页」）并 `return`（页码不动）；`SortByAsync` 的下推条件变为 `SortIsPushable && EditorMatchesLastRun()`。理由：`RunAsync` 用的是实时 `Sql`，用户改完语句直接翻页会**拿旧表头排序去跑一条新语句**、静默给出错误的行序，而 UI/文档都声称"翻页重跑同一条语句"。
    · **④ 未下推的原因写进状态栏**：新增 `Core/Querying/PagedSqlPlan.cs`——`public enum SortFallbackReason { None, NoServerPaging, StatementNotRewritable, SqlServerKeepsUserOrder, UnsortableColumnName }` + `public readonly record struct PagedSqlPlan(string? Sql, bool SortApplied, SortFallbackReason SortFallback)`；`PagedSql.Plan(sql, dialect, pageSize, offset, sort = null)` 返回它，`Build` 两个重载退化为薄包装（`Build(..., sort, out sortApplied)` 语义不变，集成测与既有测不受影响）。本地函数 `Refused(reason)` 在 `sort is null` 时刻意把原因压成 `None`（没请求排序时"为什么没排序"没有意义）。VM 侧 `SortFallbackText()` 按序给出：编辑器文本已改动 → 事务进行中 → 未连接 → 无方言 → 再按 `Plan(...).SortFallback` 映射（`NoServerPaging` = 该方言不支持服务端分页；`SqlServerKeepsUserOrder` = SQL Server 不改写语句里已有的 ORDER BY；`UnsortableColumnName` = 列名重复或为表达式，按名排序会报错；其余 = 该语句无法被改写（非查询 / 多语句 / 自带顶层分页）），文案统一为`（只在当前页 N 行内排序：<原因>）`（保留"当前页"字样，既有事务测不至于变红）。
    · **⑤ 死代码清理**：删除 `PagedSql.cs` 中全仓无引用的 `IsWordBoundary` 与 `MatchesKeyword`（扫描器早已改成内联 `IsWordPart` 判定），并把错位的 `ScanResult` 文档注释移回 record 上。
    · **红绿（四处，逐处单改单还原，`diff -q` 均 RESTORED-EXACT、`grep -c RED-CHECK` = 0）**：① 在 `PagedSql.cs` 的改写判定后面再加回 `|| scan.HasTopLevelPaging` → **恰好 9 红**（`PagedSqlTests.Statement_with_top_level_paging_is_still_wrapped_by_wrapper_dialects` ×3、`Wrapper_dialects_all_accept_a_user_supplied_limit`、`Sort_is_pushed_into_the_outer_wrapper_even_when_the_statement_has_its_own_limit`、`Escaped_quote_inside_literal_does_not_confuse_the_scanner`、`QueryTabViewModelTests.Cross_page_sort_applies_to_the_whole_user_supplied_limit`、`User_supplied_limit_with_a_sort_is_no_longer_a_fallback_case`、`User_supplied_limit_is_wrapped_so_server_paging_still_works`）；② `ResultSort.For` 的唯一性比较改回 `StringComparison.Ordinal` → **恰好 1 红**（`For_treats_column_names_as_case_insensitive_when_deciding_uniqueness`）；③ `EditorMatchesLastRun()` 改成恒 `true` → **恰好 3 红**（`Next_page_is_refused_when_the_editor_text_changed_after_the_last_run`、`Previous_page_is_refused_when_the_editor_text_changed_after_the_last_run`、`Header_sort_only_sorts_the_current_page_when_the_editor_text_changed`）；④ `PagedSql.Plan` 的 `Refused` 把原因恒定为 `None` → **恰好 2 红**（`Plan_reports_why_a_sort_could_not_be_pushed`、`For_treats_column_names_as_case_insensitive_when_deciding_uniqueness`）。新增/改写用例：`PagedSqlTests` 新增 `Sort_is_pushed_into_the_outer_wrapper_even_when_the_statement_has_its_own_limit`、`Plan_reports_why_a_sort_could_not_be_pushed`（None / `StatementNotRewritable` ×3 含 `SELECT TOP (5) …` / `SqlServerKeepsUserOrder` 并断言 `kept.Sql == "SELECT id FROM dbo.t ORDER BY id OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY"` / `UnsortableColumnName` ×2 / `pageSize 0` → `NoServerPaging` / `sort == null` → `None`）、`For_treats_column_names_as_case_insensitive_when_deciding_uniqueness`；`QueryTabViewModelTests` 删 `User_supplied_limit_is_left_untouched`，新增 `User_supplied_limit_is_wrapped_so_server_paging_still_works`、`Cross_page_sort_applies_to_the_whole_user_supplied_limit`、`Next_page_is_refused_when_the_editor_text_changed_after_the_last_run`、`Previous_page_is_refused_when_the_editor_text_changed_after_the_last_run`、`Header_sort_only_sorts_the_current_page_when_the_editor_text_changed`、`User_supplied_limit_with_a_sort_is_no_longer_a_fallback_case`，并把 `Column_header_sort_falls_back_to_the_current_page_when_it_cannot_be_pushed` 的"不可下推"样本从 `SELECT n FROM t LIMIT 3`（已可下推）换成 `EXPLAIN SELECT n FROM t`（扫描器首词只认 `SELECT`/`WITH`，故 `StatementNotRewritable` 且仍返回行），事务用例补断言「事务进行中不重跑查询」。
    · **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 425 / 成功 393 / 跳过 32 / 失败 0**（415→425：新增 10 例，改写 1 例）。⚠️ 靶机通道本轮未复跑（全部改动都在纯逻辑层与 VM 层，离线真 SQLite 已覆盖；`EXPLAIN` 与包装语句均由真 SQLite 执行），README 的用例数 / 已知限制 8 / 路线图与 §7 已同步。

- [x] **第九批改进（2026-10-16）✅**：双击命令桥接控件级测试 + 壳层死代码清理（**无 Core 改动、无靶机依赖**）
  - **做的两件事**：① `DdraigBench/Controls/ConnectionActivation.cs` 与 `DdraigBench/Controls/TreeItemActivation.cs` 这两个附加属性桥接此前**只有 XAML 接线、零测试**——桥接一旦断线（命令没接上 / 不冒泡 / 上爬找不到节点）用户双击**什么都不发生且没有任何报错**，属于最难靠阅读发现的壳层缺陷；② `DdraigBench/Converters/NavigatorWidthConverter.cs` 是全仓唯一的 orphan public 类型文件：`NavigatorWidthConverter` / `QueryEditorRowHeightConverter` / `QueryEditorSplitterHeightConverter` 三个类在 `.cs` 与 `.axaml` 里都零引用，功能早已被 `Controls/WorkbenchLayout.cs` 附加属性取代（`MainWindow.axaml` 的 `controls:WorkbenchLayout.NavigatorVisible`、`QueryTabView.axaml` 的 `controls:WorkbenchLayout.QueryEditorVisible`）。
  - **headless 合成双击的做法（新增 `DdraigBench.Test/ActivationBridgeTests.cs`，11 例）**：headless 平台没有真实指针，`DoubleTapped` 只能自己造参数——`new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true)` → `new PointerEventArgs(InputElement.DoubleTappedEvent, target, pointer, target, new Point(0,0), 0, new PointerPointProperties(), KeyModifiers.None)` → `new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs)`，然后 `control.RaiseEvent(...)`。**关键事实**：`Control.RaiseEvent` 会沿 `InteractiveParent`（= `VisualParent as Interactive`）建事件路由并上冒泡，**不必挂进 `Window` 也能冒泡**，故 `ListBox` 的用例可以完全裸构造；但 `TreeViewItem` 想被真正实现出来（视觉树里长出 `TextBlock`）**必须** `new Window { Content = tree }` + `Show()` + `Dispatcher.UIThread.RunJobs(null)`，裸构造的 `TreeView` 里只有一个空 `TreeViewItem`。（顺带记下查过的死路：`LayoutManager` 是 internal、`ExecuteLayoutPass` 调不到；`Control.InteractiveParent` 是 internal `virtual`。）
  - **11 例覆盖**：列表侧 `Double_click_on_a_selected_connection_runs_the_command`（参数为 `null`——桥接不带参，命令自己读 `SelectedSavedConnection`）、`Double_click_without_a_selection_does_nothing`、`Double_click_on_a_row_bubbles_up_to_the_list`、`Clearing_the_command_detaches_the_double_click_handler`、`Replacing_the_command_runs_only_the_new_one`；树侧 `Double_click_on_a_tree_node_runs_the_command_with_that_node`（`Assert.Same` 断言拿到的是那个节点 VM）、`Double_click_on_a_nested_element_walks_up_to_the_nearest_data_context`、`Double_click_outside_any_tree_node_does_nothing`、`Double_click_does_not_run_a_command_that_cannot_execute`、`Clearing_the_command_detaches_the_tree_handler`、`Replacing_the_tree_command_runs_only_the_new_one`。树用例的行模板刻意把最内层 `Border.DataContext` 置 `null`（真实模板里由祖先继承），以真正走到"沿 `Parent` 上爬"那条路径。
  - **红绿（五处，逐处单改单还原；备份 `/tmp/redcheck2/`，`diff -q` 均 RESTORED-EXACT、`grep -c RED-CHECK` = 0；均只跑 `--filter FullyQualifiedName~ActivationBridgeTests`）**：① 去掉 `ConnectionActivation.OnDoubleTapped` 的 `sender is ListBox { SelectedItem: not null } &&` → **恰好 1 红**（`Double_click_without_a_selection_does_nothing`）；② 给 `TreeItemActivation` 的上爬 `while` 加 `false &&` → **恰好 1 红**（`Double_click_on_a_nested_element_walks_up_to_the_nearest_data_context`）；③ 把 `RoutingStrategies.Bubble` 改成 `Direct` → **恰好 3 红**（两条树用例 + `Clearing_the_command_detaches_the_tree_handler`）；④ 去掉 `ConnectionActivation.Update` 里的 `RemoveHandler` → **恰好 1 红**（`Replacing_the_command_runs_only_the_new_one`）；⑤ 去掉 `TreeItemActivation.Update` 里的 `RemoveHandler` → **恰好 1 红**（`Replacing_the_tree_command_runs_only_the_new_one`）。**教训**：④ 第一次注入**没变红**——当时只有"清空命令"用例，而摘不摘旧处理器不影响"命令被清空后再点"这条路径；补了重绑定用例后才见红。**注入不变红时先怀疑测试没覆盖那条分支，而不是去强化断言**。
  - **死代码清理**：`rm DdraigBench/Converters/NavigatorWidthConverter.cs` + `rmdir DdraigBench/Converters`；`AGENTS.md` 架构图行改为 `│  Views/ViewModels/Controls                    │`、§3 目录树删掉 `Converters/` 那一行。壳工程 csproj 是 SDK 风格 glob，不列文件，无需改动。
  - **本轮实测**：`dotnet build DdraigBench.slnx -c Release` → 0 警告 0 错误；`dotnet test DdraigBench.slnx -c Release` → **总计 436 / 成功 404 / 跳过 32 / 失败 0**（425→436：新增 11 例）。⚠️ 靶机通道本轮未复跑（改动全在壳层控件与测试，Core 一行未动），README 的用例数 436 / 已知限制 4 / 路线图与 §7 已同步。
  - [x] **M5-3 元数据完整性 ✅（2026-10-12）**：已完成并有靶测：SQL Server 首批目录查询、MySQL/PG 触发器与例程定义、PG 物化视图定义、MySQL `SHOW CREATE TABLE` 原始定义、MySQL 计划事件（`DbObjectKind.Event`）与 MySQL 分区表子树。SQL Server 顶层有序简单查询采用直接 `OFFSET/FETCH` 的服务端分页已实现并靶测；复杂或有歧义的形状 fail-closed，普通查询仍受客户端读取上限保护。SQL Server 不持久化原始建表语句，表 DDL 继续由 `DdlGenerator` 重建，这不是可读取定义的实现缺口。完整 Release 门槛：离线 **324 / 成功 292 / 跳过 32 / 失败 0**；接入 MySQL + PostgreSQL **324 / 成功 297 / 跳过 27 / 失败 0**；再接入 SQL Server **324 / 成功 301 / 跳过 23 / 失败 0**。SQL Server 首批靶测历史门槛为 2026-10-11 **309 / 282 / 27 / 0**（当时尚无本轮测试）；本轮分页靶测已包含于 2026-10-12 SQL Server 目标通道。本机靶机凭据走 `/tmp/ddb/env.sh`（0600，勿 cat），用 `set -a && . /tmp/ddb/env.sh && set +a` 注入。以后新增目录查询或 T-SQL 分页实现同样必须先经对应靶机验证，不提交未经验证的方言代码。
M0（依赖整改）已完成：`FreeSql.All` 已替换为裸 `FreeSql`，GPL 风险源已移除。UI 依赖骨架亦已就位（FluentAvaloniaUI 3.1.0 主题 + FluentIcons.Avalonia.Fluent 2.1.343 + Avalonia.Controls.DataGrid 12.1.2，§2.6）。

M1 历史进度（2026-09-18）：**M1-1 地基** + **M1-2 Querying** + **M1-3 Connections** + **M1-4 Metadata/Sessions** + **M1-6 UI 闭环** + **M1-7 GPL 防回归（静态断言）** 完成——Core 工程 + Provider.Sqlite（主/测/Core）+ SQLite 冒烟测试 + MasterPool 取还语义实测（§4）+ `QueryResult`/`QueryRunner` + `ConnectionProfile`/`FreeSqlFactory` + `IMetadataExplorer`/`FreeSqlMetadataExplorer`（Table/View/Column）+ `DbSession`/`ConnectionLease` + `MainViewModel`/`ConnectionTreeNodeViewModel`/`QueryTabViewModel` + `ResultColumns`（DataGrid 动态列附加属性）+ `IFilePicker` + `GplRegressionTests`。当时测试 25 个全绿；当时剩余的手动截图验收已于 2026-10-11 通过（见 §8 M1），不再是待办。

M2-a 历史进度（2026-09-18）：当时接入 `FreeSql.Provider.MySqlConnector` + `FreeSql.Provider.PostgreSQL` 3.5.311 并通过 MariaDB 10.5 / PostgreSQL 13 靶机集成测试；当时遗留的 SQL Server 与连接对话框项已由下方 M2-b/M2-c 完成，当前状态以 §8 M2 检查项为准。

M2-b（2026-09-18）**完成**：`ConnectionParameters`/`ConnectionStringBuilder`（Core，按 Provider 生成连接串与建议名）+ `ConnectionDialogViewModel`（Provider 切换/校验/`BuildProfile`，纯逻辑）+ `ConnectionDialogView.axaml` + `IConnectionDialog`/`ConnectionDialog`（Avalonia `ShowDialog`，VM 不碰 Avalonia）落地；`MainViewModel` 的 SQLite 专用入口已换成对话框入口（`OpenConnectionCommand`），`MainWindow`/`App` 接线完成。测试 45 个全绿（无靶机时 2 个集成测试跳过）。

M2-b 历史收尾（2026-09-18）：当时连接对话框已加入 Provider 友好名（SQLite / MySQL / MariaDB / PostgreSQL）与测试连接按钮；当时 SQL Server 明确挂起、M2 曾暂时收口。该挂起状态已被 2026-10-11 的 M2-c 取代：SQL Server Provider 与 UI 已接入并完成 SQL Server 2022 靶机基础集成验证，当前剩余限制见 §8 M5-3。

M3 进度（2026-09-18 起，2026-10-05 收口）：
- **M3-1 取消执行 ✅**：`QueryTabViewModel` 接 `CancellationTokenSource`（`Cancel()`/`CanCancel`，含"执行刚开始时按下也生效"的 `_cancelRequested` 兜底），token 贯穿 `QueryRunner.ExecuteAsync` → ADO；执行中显示"取消"按钮（`MainWindow` 绑 `SelectedTab.CanCancel`）。
- **M3-2 事务模式 ✅**：`QueryTabViewModel` 持租约 + `DbTransaction`，`QueryRunner` 加事务通道（SQL 跑在事务连接上）；`BEGIN → INSERT → ROLLBACK` 丢弃、`COMMIT` 持久、标签处置自动回滚；接新连接时旧标签先处置（回滚 + 归还租约）。测试 60 个全绿（无靶机时 2 跳过；接靶机 60/60）。
- **M3-3 SQL 编辑器 ✅**：`Avalonia.AvaloniaEdit 12.0.0`（§2.7 实查通过）+ 显式引入 `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`；`QueryTabView` 的 `TextBox` 换成 `TextEditor`（行号 / 等宽 / 不换行）；因 `TextEditor.Text` 非 `AvaloniaProperty`，用附加属性 `Controls/EditorText.Text` 双向绑定到 VM 的 `Sql`。
- **M3-4 结果上限与虚拟化 ✅**：`QueryResult` 加 `Truncated`（取满 `maxRows` 后**多读一行**判定，区分"正好 N 行"与"被截断"）。**行虚拟化**由 DataGrid 自带（只渲染可见行），我们只确保不关闭它。
- **M3-5 真·服务端分页 ✅（2026-10-05，把 M3-4 遗留的 LIMIT/OFFSET 收口）**：`Core/Querying/PagedSql.cs` 纯逻辑——把查询包成 `SELECT * FROM (<原语句>) AS "ddb_page" LIMIT n+1 OFFSET m`（**必须包派生表**：用户 SQL 可能已带 LIMIT、以分号结尾或含锁提示，直接拼接会语法错）。判定"是否查询"只看首词 SELECT/WITH；"是否已分页"做**顶层**关键字扫描（跟踪括号深度 + 字符串/引用标识符状态，故子查询里的 LIMIT 与字面量 `'limit'` 都不误伤）。`QueryTabViewModel` 以 `PageSize`（默认 1000）取代原 `MaxRows`，配 `CurrentPage`/`HasNextPage`/`HasPreviousPage` + `NextPage`/`PreviousPage` 命令，**刻意多取一行（`LIMIT pageSize+1`）判定有无下一页再裁掉**——注意这一行必须由 SQL 的 LIMIT 产生，`QueryRunner` 的读取上限拿不到它（初版就踩了：把 `Truncated` 当 `HasNextPage` 用，结果恒为 false）。`ExecuteAsync` 回第 1 页、翻页走私有 `RunAsync`（否则翻页会被重置回第 1 页）。非查询语句（INSERT/UPDATE/DDL）原样执行，看 `影响 N 行`。`QueryTabView` 换成分页工具条（每页行数 + ◀/▶ + 第 N 页）。测试：`PagedSqlTests`（26 = 7 个 Fact + 19 组 InlineData，含 CTE/分号/子查询 LIMIT/字面量/转义引号/非法参数/方言引用）+ `QueryTabViewModelTests` 4 个真 SQLite 分页往返（切片/最后一页/重执行回第 1 页/用户自带 LIMIT 不叠加/非查询语句原样执行）。全量 180 个（168 通过 / 12 跳过 / 0 失败）。
- **前置遗留已全部解决（2026-10-16）**：headless 平台已接通（`Avalonia.Headless` + xUnit v3 `AssemblyFixture`），`ResultColumns` 动态列已纳入回归 ✅；需要 UI 线程的控件（如 `TextEditor`、`Window`）**现在也能测**（§7 的专用线程 + `TestUiThread` 编组），线程归属类修复（事务取租约/提交/回滚）也有红测对照（`DbSession.WorkObserved` + `SessionThreadAffinityTests`）。

M4 进度（2026-09-18，切片进行中）：
- **M4-1 ISqlDialect 落地 ✅**：`ISqlDialect`（`QuoteIdentifier` / `BuildPaging` / `FormatLiteral`）+ `SqlDialectBase` + SQLite/MySQL/PostgreSQL 三实现 + `SqlDialects` 工厂。分页统一 `LIMIT n OFFSET m`（三方言均支持）；字面量含字符串转义（`''`）、日期、布尔（`TRUE/FALSE`）、二进制（PG 用 `'\x..'::bytea`，SQLite/MySQL 用 `X'..'`）。测试 73 个全绿（无靶机时 2 跳过）。
- **M4-2 导出 CSV / INSERT ✅**：`Core/Exporting/ResultExporter.cs` 纯逻辑（`ToCsv` 含分隔符/引号/换行转义与 null 空字段；`ToInsert` 标识符引用与字面量全交给 `ISqlDialect`，支持 `batchSize` 合并多行、空列名补 `columnN`）。壳层：`IFilePicker` 加 `PickSaveFileAsync`（StorageProvider 实现）+ `IExportDialog`/`ExportDialog`（保存路径 + INSERT 目标表名）+ `ExportDialogViewModel`（纯逻辑）+ `ExportDialogView.axaml`；`QueryTabViewModel` 增 `ExportCsvCommand`/`ExportInsertCommand` 与 `CanExport`（有列有行才可导），CSV 落盘**带 UTF-8 BOM**（Excel 友好）、INSERT 脚本**不带 BOM**；工具栏加两个按钮。INSERT 前先校验方言（`SqlDialects.TryGet`）——不支持则直接报状态、不弹对话框。测试 88 个全绿（无靶机时 2 跳过）。
- **M4-3 DDL 预览 ✅**：Core `Metadata/DdlGenerator.cs`（吃 FreeSql `DbTableInfo` → `CREATE TABLE` + `CREATE [UNIQUE] INDEX`，含列注释/DEFAULT/复合主键 `PK_<表>`/外键；**单列自增主键内联**——顺序方言不同，故 `ISqlDialect` 加 `IdentityClause` + `BuildIdentityColumn`（SQLite `PRIMARY KEY AUTOINCREMENT` / MySQL `NOT NULL AUTO_INCREMENT PRIMARY KEY` / PG `GENERATED BY DEFAULT AS IDENTITY`）；`sqlite_` 内部自动索引跳过（不可显式创建）；视图只给提示+列结构）。`IMetadataExplorer.GetDdlAsync` 串起取元数据+选方言。壳层：`IDdlViewer`/`DdlViewer` + `DdlView.axaml`（只读 TextBox，Ctrl+A/C 可复制）；树节点带 `Database`，`MainViewModel.SelectedNode` + `PreviewDdlCommand`/`CanPreviewDdl`，工具栏加「预览 DDL」。测试 97 个全绿（无靶机时 2 跳过），含**真实 SQLite 往返**（生成 DDL → DROP → 重新执行成功）。已知缺口见 §6.2。
- **M4-4 连接凭据加密存储（Windows + Linux + macOS 全部完成）**：
  - **抽象**：`Core/Secrets/ISecretProtector`——`Name` / `IsEncrypted` / **`ProtectAsync` / `UnprotectAsync`**（`ValueTask<string>` + `CancellationToken`）。**接口为异步**，因 Linux 经 D-Bus 访问密钥库天然异步；DPAPI/明文实现用 `ValueTask.FromResult` 包装。
  - **公共基类**：`EnvelopeSecretProtector`（主密钥 + AES-256-GCM 的共用实现；子类只给 `Name` / `IsEncrypted` / 平台守卫），Linux 与 macOS 两个保护器各只剩 ~15 行。
  - **Windows**：`DpapiSecretProtector`（`System.Security.Cryptography.ProtectedData` 10.0.12，**CurrentUser 作用域** + 固定 entropy）。
  - **Linux/FreeBSD**：`SecretServiceKeyProvider`（主密钥存 freedesktop Secret Service，D-Bus **DH 会话**，属主属性 `application=DdraigBench, purpose=master-key`，内存缓存 + `SemaphoreSlim` 防并发重复创建）+ `SecretServiceSecretProtector`（`Name="freedesktop-secret-service"`）+ **`SecretKeyEnvelope`**（AES-256-GCM，格式 `[版本:1][nonce:12][标签:16][密文]` → Base64；`internal` + `InternalsVisibleTo("DdraigBench.Test")`）。
  - **macOS（2026-10-04 落地）**：`MacKeychainKeyProvider`（主密钥存 login keychain 的 `kSecClassGenericPassword` 条目，`service=DdraigBench / account=master-key / label="DdraigBench master key"`，`SemaphoreSlim` + 内存缓存 + `ResetMasterKeyAsync` 重置入口）+ `MacKeychainSecretProtector`（`Name="macos-keychain"`）+ `MacSecurityFramework`（`SecItem*` / CoreFoundation P/Invoke，键名常量走 `dlsym`）+ `KeychainUnavailableException`。**零新包**（仅 `<AllowUnsafeBlocks>`）。**并发首建走"读到 DuplicateItem 就重读那把"而非删再加**，杜绝两个进程各持一把不同密钥。
  - **回退**：`PlainTextSecretProtector`（`plain:` 前缀）；工厂 `SecretProtectors.CreateDefaultAsync`（Windows→DPAPI；macOS→探测钥匙串（禁 UI）可用则钥匙串；Linux/FreeBSD 探测密钥库可用→keyring；否则明文回退）+ `CreateByName`（读历史文件，含 `freedesktop-secret-service` / `macos-keychain`）。**macOS 正式使用实例允许弹授权框**（首次「Always Allow」后无感），探测实例一律 `suppressUi` 免得 CI 挂在看不见的授权框上。
  - **存储**：`Core/Connections/ConnectionStore.cs` camelCase JSON（`version`/`protector`/`connections`），**只加密连接串**、名称与方言明文便于列表展示，**先写 `.tmp` 再 `File.Move` 覆盖**（避免半截文件），文件缺失返回空清单。**明文文件升级到 DPAPI/keyring/钥匙串 后仍可读**（按文件里的 `protector` 字段取实现），未知 `protector` 值直接报明确错误。落盘 schema **不引入 keyId 指纹**（保持跨平台一致），"密钥已换/条目被删"靠 `SecretKeyEnvelope` 的认证失败 + 明确错误消息表达。
  - **测试**：macOS 本机全量 **151 个（139 通过 / 12 跳过 / 0 失败）**——M4-4 Core 侧新增 `MacKeychainProtectorTests`（6，假主密钥，跨平台可跑）与 `MacKeychainKeyProviderTests`（6，**真 login keychain 集成**：唯一 service 隔离 + 结束即删 + 全程 `suppressUi`，含 `Item_is_discoverable_by_the_security_cli` 用 Apple `security` CLI 交叉验证 service/account/label 真按系统常量落库）；`ConnectionStoreTests` 的两个信封测试已放宽到 macOS（本机 11 个全过、0 跳过）；UI 接线另加 `MainViewModelTests` 6 个（内存 store 替身）。Linux 侧历史测试见 `SecretKeyEnvelopeTests`（13）、`SecretServiceProtectorTests`（6）、`SecretServiceKeyProviderTests`（5，真 gnome-keyring）。
  - **UI 接线 ✅（2026-10-05）**：`Core/Connections/IConnectionStore` 抽象（`Protector` + `LoadAsync`/`SaveAsync`，`ConnectionStore` 实现它）；壳层 `IConnectionStoreFactory` + `SecretConnectionStoreFactory`（**整个密钥库探测放 `Task.Run`**——macOS 钥匙串调用是同步阻塞的原生调用，钥匙串被锁时会等授权，绝不能占 UI 线程）；`App.axaml.cs` 在 `window.Opened` 后 `await viewModel.InitializeAsync()`；`MainViewModel` 增 `SavedConnections`/`SelectedSavedConnection`/`OpenSavedConnectionCommand`/`ForgetSavedConnectionCommand`/`ProtectorName`/`SecretsEncrypted`/`SecurityWarning`/`HasSecurityWarning`，新建连接成功后按**名称**去重覆盖并落盘、打开已保存连接不重写文件；`MainWindow.axaml` 左栏加「已保存连接」`ListBox`（激活即打开）+ 保护方式标签，顶部明文回退告警条（**普通 `Border` 自绘，见 §2.6 的 InfoBar 更正**）。新增 VM 用例 6 个（载入/明文告警/存储故障不崩启动/新建落盘去重/打开与移除/未选择时提示）。**已补（2026-10-11，M8）**：`ForgetSavedConnectionCommand`（移除已保存连接）已在左栏「连接」标题栏接上 UI（未选中时禁用）；列表本身仍只绑 `OpenSavedConnectionCommand`（激活即打开）。**已补（2026-10-13，编辑已保存连接）**：新增 `EditSavedConnectionCommand` + `CanEditSavedConnection`，标题栏加 `Edit` 图标按钮（`Grid ColumnDefinitions="*,Auto,Auto,Auto"`）；`IConnectionDialog.ShowAsync(ConnectionProfile? existing)` 与 `ConnectionDialogViewModel(filePicker, tester, existing)` 进入编辑模式（`Title`/`ConfirmLabel`/`IsEditing` 随之为「编辑连接 / 保存 / true」），字段由 `Core/Connections/ConnectionStringParser.TryParse` 从连接串反解回填（借 MySqlConnector/Npgsql/SqlClient/SQLite 真实驱动解析，不手写 `;` 切分），解析失败只回填名称与方言并亮 `ParseWarning`，**绝不静默清空原连接串**；`EditSavedConnectionAsync` 就地替换 `SavedConnections` 中对应位置（不改顺序）、拒绝与另一条重名（否则按名去重的落盘会静默吞掉同名条目）、被编辑的恰是活动连接且连接串/方言有变时状态栏提示"当前会话仍用旧设置，重新打开后生效"（仅改名不算过期）。**已补（2026-10-13，启动自动重连）**：界面偏好存储由「只管主题」的 `IThemePreferenceStore`/`ThemePreferenceStore` 换成 `IUiSettingsStore`/`UiSettingsStore`（`ui-settings.json` 一份文件同时放 `theme` 与 `lastConnectionName`，**整份读写**——两条各自 RMW 会让"切主题"抹掉"上次连接名"，红测 `Theme_switch_keeps_the_auto_reconnect_record`），旧 PascalCase JSON 仍读得出来；`MainViewModel.InitializeAsync` 载入清单后调 `TryReconnectLastConnectionAsync()` 按名在当前清单里找，**只有连接成功才记名字**（失败只写状态栏、不弹对话框、不记名字），名字已不在清单里就安静跳过并清掉记录，清单整体读不出来（`_store is null`）时**保留记录**；编辑保存时若改了名，新名字会搬运过去。
- **踩坑（务必保留）**：① DotNet 的 **`Ace4896` 只暴露 `SecretService.SearchItemsAsync`（返回 `(unlocked, locked)` 元组）**，`Collection.SearchItemsAsync` 才返回 `Item[]`；② `Path.GetFileName` 在 Linux 上**不切 `\`**（既有 `SuggestName` 缺陷已修，改用手写分隔符归一化）；③ **`ConnectionStore` 曾误 chmod 配置文件的既有父目录**（`/tmp` 等）——改为**仅对本次新建目录**收紧权限（§2.8 规则 5）；④ **`CFDictionaryCreate` 的回调参数传 NULL 会导致字典不 retain 键值** → 释放 CFString 后 `SecItem*` 读野指针**永久卡死**（macOS 侧头号坑，见 §4）；⑤ `security add -U` **不能**用来改写我们创建的条目来模拟损坏（会换 ACL），造损坏条目用我们自己的 `SecItemUpdate`；⑥ **FluentAvaloniaUI 3.1.0 没有 `InfoBar`**（§2.6 已更正），告警条用 `Border` 自绘。

- **依赖审计（M0 起累积）**：见 §2；M4-4 新增 `Ace4896.DBus.Services.Secrets 1.6.0`（MIT）与其传递依赖 `Tmds.DBus.Protocol`（MIT），均**无原生库依赖**；macOS 侧**零新包**（只用系统自带 `Security.framework` / `CoreFoundation`）。

---

## 9. 常用命令（仓库根执行）

```bash
dotnet build DdraigBench.slnx               # 构建整个解决方案
dotnet test  --solution DdraigBench.slnx    # 全部测试（MTP 模式，见下）
dotnet run   --project DdraigBench          # 起应用
```

**测试运行器（实测 2026-09-18）**：`xunit.runner.visualstudio 4.0.0` → `xunit.v3` + `Microsoft.Testing.Platform`，故测试工程是 MTP 应用。.NET 10 SDK 起 `dotnet test <sln>`（VSTest 目标）被禁用；仓库根 `global.json` 已声明 MTP 运行器：

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

之后用 `dotnet test --solution DdraigBench.slnx`（或直接 `dotnet test`；MTP 模式**不再需要** `--` 与 `TestingPlatformDotnetTestSupport`）。传解决方案要加 `--solution`，传工程要加 `--project`。

**构建被 IDE 预览器锁住时（实测）**：IDE 的 Avalonia XAML 预览器进程（`Avalonia.Designer.HostApp`）会占用 `bin\Debug\net10.0\DdraigBench.dll`，导致复制失败（MSB3027）。临时验证可改用 `-c Release`（`bin\Release` 不被占用）：`dotnet build DdraigBench.slnx -c Release` + `dotnet test --solution DdraigBench.slnx -c Release`；要跑应用则先关闭预览面板。

---

## 10. 红线汇总

1. 禁止 `FreeSql.All` / `FreeSql.Provider.MySql` / `MySql.Data`（GPL 或冲突源）。
2. `Provider.MySql` 与 `Provider.MySqlConnector` 不得共存。
3. DdraigBench.Core 禁引 Avalonia/ReactiveUI；ViewModel 禁引 Avalonia。
4. 未经 §2 流程（许可证+兼容性核查）不得新增 PackageReference。
5. 任何交付必须附 build/test 真实输出。
6. `<FluentTheme/>` / `SimpleTheme` 不得与 `<FluentAvaloniaTheme/>` 并存（§2.6）；图标只用 FluentIcons 一家，禁止混用第二套图标字体。
