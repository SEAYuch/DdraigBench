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
│  Views/ViewModels/Converters                  │
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
| ReactiveUI.Avalonia | 12.1.6 | MVVM 桥 → ReactiveUI 24.2.0 + Splat 21.0.0 | ✅ 保留 |
| ReactiveUI.Avalonia.Autofac | 12.1.6 | Autofac 容器桥 → Splat.Autofac | ⚠️ **未接线**：Program/App 无任何 Autofac 配置，纯死重 |

### 2.2 Provider 引入策略（M1 装 Sqlite；M2 装 MySqlConnector + PostgreSQL；SqlServer 待靶机）

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

**SqlServer 明确挂起**（2026-09-18）：当前无 SQL Server 靶机 → 暂不装 `FreeSql.Provider.SqlServer`、也不放进连接对话框，避免未实测的方言代码入库；等有靶机再按上表加入（§8）。

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
- **D2 DI 容器**：a) 接线 Autofac；b) 删除 ReactiveUI.Avalonia.Autofac，用 Splat 默认定位器 `Locator.CurrentMutable.RegisterConstant(...)`。**默认取 b**；服务多于 ~20 个再迁 a。
- **D3 主题与图标栈**：✅ **已解决**（2026-09-18）。取 FluentAvaloniaUI 3.1.0 + FluentIcons.Avalonia.Fluent 2.1.343，考查表、决策与备选见 §2.6。

### 2.5 Test 工程

**xunit.v3 4.0.1** / Test.Sdk 18.10.1 / coverlet.collector 10.1.0 ✅（2026-09-18 由 `xunit 2.9.3` 升为 `xunit.v3 4.0.1`，与 Pathfinder1eHelper 对齐）。`xunit.v3` 要求测试工程 `<OutputType>Exe</OutputType>`，并 `NoWarn xUnit1051` 以保零告警。**已增补（M1-1）**：`FreeSql` + `FreeSql.Provider.Sqlite` 3.5.311，以及对 `DdraigBench.Core` 的 ProjectReference——集成测试用临时文件 SQLite（`Path.GetTempPath`），无需 docker。另增对 `DdraigBench` 壳工程的 ProjectReference，以便纯逻辑测 ViewModel（§7，不启 Avalonia）。

> ⚠️ 测试工程是 **MTP 应用**（`xunit.v3` + `Microsoft.Testing.Platform`）。.NET 10 下 `dotnet test <sln>` 的旧写法会报错，必须走 MTP 模式（§9 的 `global.json`）。**实测 MTP + xunit.v3 4.0.1 可正常发现并运行（25 绿）**；Pathfinder1eHelper 则走 VSTest 通道（其测试工程设 `IsTestingPlatformApplication=false`）——两种通道**互斥**，本项目选 MTP + `global.json`。

另增 **`Avalonia.Headless 12.1.3`**（仅测试工程；基础包、无 xunit 耦合）+ xUnit v3 `AssemblyFixture` 启动 headless 平台，用于控件级 UI 单测（§7 / `DdraigBench.Test/TestAppFixture.cs`）。

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
| libsecret（`secret-tool` / C 库 P/Invoke） | — | LGPL-2.1（库本身） | **同步**；需 `libsecret-1.so.0`，GError/GLib 互操作复杂 | ❌ 不采用（引入 GPL/LGPL 家族库风险 + 原生依赖 + 互操作脆弱） |
| GnomeStack.Os.Secrets / OutWit.* / SyntaxCircus.* | 0.1.x / 1.0.0 | 多未标注或非 MIT | 同样包 libsecret 或自建 | ❌ 不采用（许可证不明确、维护者小众、功能过剩） |

考查过的 macOS Keychain 访问方式（NuGet `q=keychain+macos` 共 19 个命中，**无 Microsoft/dotnet 官方包**）：

| 方案 | 版本 | 许可证 | 判定 |
|---|---|---|---|
| **自研 `Security.framework` P/Invoke**（`SecItemCopyMatching/Add/Update/Delete` + CF 字典） | — | 系统自带框架 | ✅ **采用**。零新依赖、API 面 4 个函数、键名常量由 `dlsym` 取自框架（不硬编码字面量） |
| Latchkey | 0.1.0 | MIT（有 net10.0 资产） | ❌ 传递依赖 `CliWrap` 3.10.2、0.1.0 单维护者、1271 下载 |
| GnomeStack.Os.Secrets / GnomeStack.Secrets.OperatingSystem | 0.1.3 / 0.1.6 | 未标注 | ❌ 小维护者、且走 libsecret |
| ktsu.CredentialCache / OutWit.Shared.Secrets.Provider.MacOS / SyntaxCircus.Credentials / Keytar.Core | 1.3.x / 1.0.0 / 0.1.1 / 1.0.0 | 未标注 | ❌ 同一"小维护者 + 包 libsecret"模式 |
| `security` CLI 子进程 | — | — | ❌ 每次加解密起进程、`-A` 放开 ACL、二进制 key 过命令行 |

**决策与硬性规则**：

1. **只有 `Ace4896.DBus.Services.Secrets` 可进 Core**（Linux 路径）；macOS 只允许自研 P/Invoke，**不得引第三方钥匙库包**。若日后要换实现，先更新本节并跑通 §7 的密钥库集成测试。
2. **信封加密**（`SecretKeyEnvelope`）：密钥库只存**一把 32 字节主密钥**，连接串用 **AES-256-GCM** 加密后留在 JSON 文件——避免每连接一个密钥库条目（重命名/删除会留孤儿），且文件自描述、易备份。Linux 与 macOS 的保护器共用抽象基类 `EnvelopeSecretProtector`（子���只给标识名 + 平台守卫）。
3. 主密钥由 `SecretServiceKeyProvider` 以 D-Bus **DH 会话**（`EncryptionType.Dh`）存取，绝不走明文会话。
4. **无密钥库即回退明文**（`PlainTextSecretProtector`，`IsEncrypted=false`）——服务器/容器/CI 无会话总线时不得崩，交 UI 告警。macOS 侧的"不可用"判定见 §4 的 OSStatus 分类；探测用 `suppressUi: true` 实例，**正式使用才允许弹授权框**。
5. **不得擅自 chmod 既有目录**：`ConnectionStore` 只在"本次新建目录"时收紧到 0700（配置路径可能落在 `/tmp`、`~` 上，见 §4 实测）。
6. **macOS 附加规则**：
   - 用**传统文件钥匙串**（login keychain）。**不设** `kSecAttrAccessible`（SDK 头 `SecItem.h:89` 明确：macOS 上仅当配 `kSecAttrSynchronizable` 或 `kSecUseDataProtectionKeychain` 才生效）；**不用**数据保护钥匙串（未签名构建会 `errSecMissingEntitlement` = -34018）。
   - 条目标识沿用同一套：`service=DdraigBench`（= `SecretProtectors.ServiceName`）、`account=master-key`、`label="DdraigBench master key"`（与 freedesktop 的 `application=DdraigBench purpose=master-key` 对位），便于用户理解与手工清理。
   - 保护器名 `"macos-keychain"`（写入 `connections.json` 的 `protector` 字段，`SecretProtectors.CreateByName` 认它）。
   - `Core.csproj` 需 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`：.NET 的 `[LibraryImport]` 源生成器要求（SYSLIB1062）。业务代码本身**不使用指针语法**（钉数组走 `GCHandle`），该开关只服务于 P/Invoke 桩。
   - **授权/解锁类失败一律不静默重生成主密钥**：条目长度异常要抛 `CryptographicException`（否则旧连接串密文永久报废），授权类状态码抛 `KeychainUnavailableException`（供未来 UI 接线精确捕获后回退明文 + 告警）。

---

## 3. 工程结构

```
DdraigBench.slnx
global.json                     # test.runner = Microsoft.Testing.Platform（见 §9）
├── DdraigBench/                  # Avalonia 壳（唯一引用 Avalonia 的工程）
│   ├── Program.cs / App.axaml
│   ├── Assets/
│   ├── Services/                # IFilePicker（开+存）+ StorageProviderFilePicker + IConnectionDialog + IExportDialog + IDdlViewer（M1-6 / M2-b / M4-2 / M4-3）+ IConnectionStoreFactory/SecretConnectionStoreFactory（M4-4 接线）
│   ├── Controls/                # 附加属性：ResultColumns（DataGrid 动态列）+ EditorText（TextEditor 文本同步）
│   ├── Views/                   # axaml + code-behind（仅 InitializeComponent）
│   │   ├── MainWindow.axaml     # 布局：顶部明文告警条 + 工具栏 | 左「已保存连接」+ 连接树 | 中 Tab 区 | 底状态面板（M1-6 / M4-4 接线）
│   │   ├── QueryTabView.axaml   # AvaloniaEdit SQL 编辑器 + 结果 DataGrid（M1-6 / M3-3）
│   │   └── ConnectionDialogView.axaml   # 按 Provider 出参数表（M2-b）
│   │   ├── ExportDialogView.axaml      # 导出对话框（M4-2）
│   │   └── DdlView.axaml               # DDL 预览窗口（M4-3）
│   └── ViewModels/              # 全部 ReactiveObject，构造注入 Core 接口
│       ├── MainViewModel        # 树 + Tab 集合 + 全局命令 + DDL 预览 + 已保存连接清单/明文告警（M1-6 / M4-2 / M4-3 / M4-4 / M5-2 模式层接线）
│       ├── ConnectionTreeNodeViewModel  # 左树节点模型（惰性加载子节点；Container = 库名或 PG 模式名，M1-6 / M4-3 / M5-2 改名）
│       ├── QueryTabViewModel    # 语句执行、取消、事务、结果集/状态、导出（M1-6 / M3 / M4-2）
│       ├── ConnectionDialogViewModel    # Provider 参数表 + 校验 + BuildProfile（M2-b）
│       ├── ExportDialogViewModel        # 导出保存路径 + INSERT 目标表名（M4-2）
│       └── DdlViewModel                 # DDL 只读展示（M4-3）
├── DdraigBench.Core/            # ★ 新建类库：零 UI 依赖，可完整单测
│   ├── Connections/
│   │   ├── ConnectionProfile.cs       # 连接定义（Name / DataType / ConnectionString，M1-3 已落地）
│   │   ├── ConnectionParameters.cs / ConnectionStringBuilder.cs   # 按 Provider 出参数 → 连接串（M2-b）
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
│   │   └── SecretProtectors.cs        # CreateDefaultAsync 按平台探测 / CreateByName 取历史实现
│   ├── Sessions/
│   │   ├── DbSession.cs              # 一个打开的连接标签 = 一个会话（M1-4 已落地）
│   │   ├── ConnectionLease.cs        # MasterPool 取还句柄（M1-4 已落地）
│   │   └── ISessionManager.cs        # 生命周期、并发上限、关闭清理 → M3
│   ├── Metadata/
│   │   ├── IMetadataExplorer.cs      # 树的统一元数据 API（签名见 §4，M1-4 已落地）
│   │   ├── DbObjectKind.cs / DbObjectInfo.cs / DbColumnInfo.cs   # 元数据模型（M1-4；DbObjectInfo.Definition 为 M5-1、DbObjectKind.Schema 为 M5-2 追加）
│   │   ├── FreeSqlMetadataExplorer.cs# 两条通道：IDbFirst（表/视图/列，M1-4）+ 目录查询（触发器/例程/原始定义，M5-1）
│   │   ├── CatalogQueryExecutor.cs   # 目录 SQL 执行 + 按列名映射（internal，M5-1）
│   │   └── DdlGenerator.cs       # DbTableInfo → CREATE TABLE/INDEX（M4-3，纯逻辑；作原始定义取不到时的回落）
│   ├── Querying/
│   │   ├── QueryResult.cs            # Columns + Rows(object[][]) + Affected + 时长 + Truncated
│   │   ├── QueryRunner.cs            # MasterPool 原始连接执行 + 流式分批读取（见 §4 实测）
│   │   ├── PagedSql.cs               # 服务端分页：包派生表 + 方言 LIMIT/OFFSET（M3-5，纯逻辑）
│   │   └── Exporting/ResultExporter.cs  # CSV / INSERT 脚本渲染（M4-2，纯逻辑）
│   └── Dialects/
│       ├── ISqlDialect.cs / SqlDialectBase.cs   # 方言钩子：标识符引用 / 分页 / 字面量渲染（M4-1）
│       ├── SqliteDialect.cs / MySqlDialect.cs / PostgreSqlDialect.cs
│       ├── SqlDialects.cs            # DataType → ISqlDialect 工厂
│       └── CatalogQueries/           # ★ IDbFirst 缺口的补齐 SQL（M5-1）：每库一组，只造 SQL 不执行
│           ├── ICatalogQueries.cs    # 能力矩阵 + 对象清单/视图定义/表定义 SQL（列名契约见类注释）
│           ├── SqliteCatalogQueries.cs / MySqlCatalogQueries.cs / PostgreSqlCatalogQueries.cs
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
    // 可浏览的「库」。SQLite/MySQL 走 IDbFirst.GetDatabases()；PostgreSQL 收窄为当前库
    //   （一个连接只能看到所连库，见 §4 实测 / M5-2）。
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession s, CancellationToken ct = default);
    // Table/View ← IDbFirst；Schema/Trigger/StoredProcedure/Function ← 目录查询（M5-1 / M5-2）：
    //   方言没有这类对象时返回「空集」而非报错（如 SQLite 的 Schema/存储过程）；
    //   不是"容器级对象集合"的 kind（Column/Index/ForeignKey/Database）抛 NotSupportedException。
    //   ⚠️ database 参数实为「容器」：库级对象传库名，PG 的模式级对象传模式名。
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(DbSession s, string database,
        DbObjectKind kind, CancellationToken ct = default);
    // M1-4 追加：列不属于数据库级签名，单列一法（对应 §6.3 的"表→列"级）
    Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(DbSession s, string database, string table,
        CancellationToken ct = default);
    // M4-3 追加、M5-1 改优先级：DDL 预览。**原始定义 > 按元数据重建**——
    //   表：方言能给出原始建表语句就用它（当前仅 SQLite），否则回落 DdlGenerator；
    //   视图：目录查询取真实定义（SQLite/MySQL/PG 三方均可），取不到才回落占位；
    //   触发器/函数/存储过程：按名在目录里找，拿得到 definition 就给，否则 null。对象不存在返回 null。
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
- **M3-5 服务端分页实测（2026-10-05）**：判定"有没有下一页"的**多读那一行必须由 SQL 的 `LIMIT pageSize+1` 产生**，不能指望 `QueryRunner` 的读取上限——它的 `Truncated` 语义是"取满 `maxRows` 之外还有一行"，把 `maxRows=pageSize+1` 时恒为 false（初版踩坑，`HasNextPage` 一直 false、翻页点击无效）。另：`ExecuteAsync()` 必须**回到第 1 页**，而翻页要另走私有 `RunAsync()`，否则翻页命令里的 `CurrentPage++` 立刻被重置。分页 SQL 必须包派生表（`SELECT * FROM (…) AS "ddb_page" LIMIT …`），"是否已分页"只扫**顶层**关键字（括号深度 + 字符串/引用标识符状态机），避免误伤子查询 LIMIT 与字面量 `'limit'`。
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
  - **FreeSql 的 PostgreSQL 实现自相矛盾**（从 `FreeSql.Provider.PostgreSQL.dll` 反查 SQL 字面量确认，非猜测）：
    `GetDatabases()` = `select datname from pg_database where datname not in ('template1','template0')`（**库名**），
    而 `GetTablesByDatabase(x)` 却按 `pg_namespace.nspname = x`（**模式名**）过滤。
    故 PG 下把 `GetDatabasesAsync()` 的首项回喂给 `GetTablesByDatabase()` **通常一个表都取不到**（除非恰好存在与库同名的模式）——既有缺陷，M2 集成测只断言了"能返回非空库名"，没覆盖这一层。
    目录查询因此统一按「**schema 语义**」实现（与 `GetTablesByDatabase` 对齐），靶机测试用 `SELECT current_schema()` 取容器名。
    > M5-2 已修：PG 的库清单收窄为当前库 + 补上模式层，见下。
  - MySQL 用 `information_schema.TRIGGERS / ROUTINES / VIEWS`，PG 用 `pg_trigger / pg_proc / pg_get_viewdef`。PG 的 `pg_proc.prokind`（`'f'`=函数 / `'p'`=存储过程）**要求 PG ≥ 11**（10 及更早是 `proisagg`/`proiswindow`）。
  - **SQLite 的 `sqlite_master.sql` 是原始语句**（建表/视图/触发器都在内），故 SQLite 的定义类缺口一次读原文全解决：`AUTOINCREMENT`、内联 `UNIQUE`、`CHECK`、`COLLATE` 都保留（§6.2 的①②③）。**保真度高于按元数据重建**，所以 `GetDdlAsync` 改成"原始定义优先"。副作用：SQLite 的表 DDL 预览不再输出 `DdlGenerator` 的加引号重建版（那反而是有损的）。原始语句通常**不带结尾分号**，需补上才能直接复制执行。
  - `sqlite_master` 可加附加库限定：`"main".sqlite_master` 合法（本层所有 SQLite 查询都这样写，`main` 来自 `QuoteIdentifier`）。
  - **MySQL 的字面量必须转义反斜杠**：默认 `sql_mode` 下 `\` 是转义符（`NO_BACKSLASH_ESCAPES` 未开）。原先 `SqlDialectBase.Quote` 只翻倍单引号，含 `\` 的数据（Windows 路径、JSON 里的 `\n`）会在导出 INSERT 与库名过滤里被**静默改写**——M5-1 修（`MySqlDialect` 覆写 `Quote`），并加了回归测。假设：`NO_BACKSLASH_ESCAPES` 未开启。
  - `QueryRunner` 把 SQL 异常收敛进 `QueryResult.Error`（不抛）。**元数据场景必须把它重新抛出去**（`CatalogQueryExecutor` 负责），否则树会"静默空"、用户看不出是权限不足还是真没对象。
  - 树节点展开是 **fire-and-forget**（`ConnectionTreeNodeViewModel` 里 `_ = _loadChildren(this)`），异常会变成未观察的任务异常被吞。故 `MainViewModel` 的各 loader 都就地 try/catch 收敛成状态栏文案；回归测 `Expand_failure_is_reported_instead_of_crashing_the_tree`。
- **M5-2 模式层实测结论（2026-10-08）**：
  - **「库」这一层在不同方言的含义差异极大**，必须分开处理，否则树形就是错的：
    SQLite 一个连接可 `ATTACH` 多个库（`IDbFirst.GetDatabases()` 给 main + 附加库）；MySQL 的库 == 模式且**可跨库**（`information_schema` 与 `GetTablesByDatabase(table_schema=…)` 都不受"当前库"限制，`USE` 可切换）；**PG 一个连接只能看到所连库内**的对象（跨库要换连接或 dblink/FDW）。
    故 `ICatalogQueries.ConnectionIsBoundToSingleDatabase` 只对 PG 为真，`GetDatabasesAsync` 相应收窄为 `SELECT current_database()`——避免把"看得见打不开"的库列进树。
  - 模式层作为**普通对象种类**接入（`DbObjectKind.Schema`），复用 M5-1 的目录查询通道（`Supports`/`BuildObjectsQuery`），因此**不需要给 `IMetadataExplorer` 加新方法**。SQLite/MySQL 返回空集，树据此判断"不加这一层"。
  - PG 的模式清单要滤掉系统命名空间：`pg_catalog` / `information_schema` / `pg_toast`，以及 `pg_temp%` / `pg_toast_temp%`（临时对象的模式，形如 `pg_temp_3`）。
  - `pg_namespace` 本就只含当前库的模式，**所以模式清单 SQL 与 `database` 参数无关**（实现里刻意忽略它，并有测守着这一点）。
  - 树节点的容器字段由 `Database` **改名 `Container`**：PG 下对象的容器是**模式名**而非库名，沿用旧名会把 DDL 预览的入参传错。改名后 VM 侧只有 `CanPreviewDdl` / `PreviewDdlAsync` / 三个 loader 用到，XAML 未绑定它。
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
- `TextEditor.Text`（AvaloniaEdit）**不是 `AvaloniaProperty`**，不能直接绑定（README 也只演示字面量）→ 用附加属性 `Controls/EditorText.Text` 双向同步（M3-3）。
- 左树展开是 **fire-and-forget**（`ConnectionTreeNodeViewModel` 里 `_ = _loadChildren(this)`）→ loader 内部**必须** try/catch 并把失败写进 `Status`（M5-1），否则元数据查询失败会变成未观察的任务异常被静默吞掉，用户只看到"树是空的"。

§4 的服务契约经 Splat 或 Autofac 注入 ViewModel 构造函数（见 §2.4 D2）。

---

## 6. 元数据层策略

1. **能用 IDbFirst 就不手写**：`GetDatabasesAsync` → `IDbFirst.GetDatabases()`；表/列/主键/索引/外键/视图/存储过程 → `IDbFirst.GetTablesByDatabase()` 的 `DbTableInfo`（含 `IndexesDict` / `ForeignsDict`）。这是白嫖 22 方言的最大头。
2. **IDbFirst 缺口 → `Dialects/CatalogQueries/`**（M5-1 落地；早前 §3 曾把该目录写为 `Metadata/CatalogQueries/`，**以 §6.2 为准，实际在 `Dialects/` 下**——它绑方言、用 `ISqlDialect` 引用，放方言侧才对）。
   - 设计：**只造 SQL 不执行**（`CatalogQueryExecutor` 执行 + 按列名映射）。好处是无靶机也能对 SQL 形状/能力矩阵/转义做纯逻辑单测。
   - `database` 参数语义 = **schema 语义**（与 `FreeSql.DbFirst.GetTablesByDatabase` 对齐）：SQLite = 附加库名、MySQL = schema、PG = schema（PG 的 `GetDatabases()` 返回库名，两者不同源，M5-2 起由「库清单收窄 + 模式层」处理，见 §4）。
3. **缺口状态（2026-10-08）**：
   - ✅ ① **视图定义**：SQLite 读 `sqlite_master.sql` 原文；MySQL 用 `information_schema.VIEWS.VIEW_DEFINITION` + `ComposeViewDdl` 补前缀；PG 用 `pg_get_viewdef()` + 补前缀。
   - ✅ ② **SQLite `AUTOINCREMENT`**：改走"原始定义优先"，不再依赖 `IsIdentity`（`INTEGER PRIMARY KEY`（无 AUTOINCREMENT）**不得**输出 `AUTOINCREMENT`——那会改变 rowid 复用语义）。
   - ✅ ③ **唯一约束**：内联 `UNIQUE` / `CHECK` / `COLLATE` 随原始语句一并保真（按元数据重建必丢，因为 SQLite 把它们落在 `sqlite_autoindex_*` 里而 `DdlGenerator` 要跳过内部索引）。
   - ✅ **触发器 / 函数 / 存储过程清单**：三方方言均可列；SQLite 的触发器还能直接给出完整定义。
   - ✅ **模式层（M5-2）**：`DbObjectKind.Schema` + PG 的 `pg_namespace` 清单；树变成「库 → 模式 → 对象」，SQLite/MySQL 保持「库 → 对象」。PG 的库清单同时收窄为当前库。
   - ⏳ **仍缺**（后续）：事件（MySQL `information_schema.EVENTS`）与分区（`information_schema.PARTITIONS`）；MySQL/PG 的**触发器与例程定义**（需 `SHOW CREATE TRIGGER` / `pg_get_functiondef`，或按 `information_schema.TRIGGERS`/`PARAMETERS` 逐列拼装）；MySQL 的表定义（只能走 `SHOW CREATE TABLE`，多列且列名不固定，未纳入契约）；PG 物化视图（`relkind='m'`）。
4. 树节点惰性加载：展开才查子级。**实际层级**：SQLite/MySQL = 「库 → 表/视图/触发器/存储过程/函数 → 列」，PostgreSQL = 「库 → 模式 → 表/视图/触发器/存储过程/函数 → 列」。库节点下**平铺**（不做"表/视图"分组文件夹），这是既有取舍，改动前先想清楚。§6.3 原本写的"库→模式→表→列四级"是目标形态，M5-2 起 PG 已达成。

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
| GPL 防回归 | 静态断言（任何 `*.csproj` 不含 `FreeSql.All` / `FreeSql.Provider.MySql` / `MySql.Data`；输出目录无对应 dll）+ **驱动集真测试**：`Assembly.Load("MySqlConnector")` 成功、`MySql.Data` / `FreeSql.Provider.MySql` 失败，MySQL/PG 均可建出 `IFreeSql` 且 `Ado.DataType` 正确（`GplRegressionTests.cs`） |
| 多方言集成 | 靶机连接串走环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG`；未设置或 `Ado.ExecuteConnectTest()` 不可达即 `Assert.Skip`（无靶机时全绿）。**凭据不进仓库**（`MultiDialectIntegrationTests.cs`）。M5-1 起另跑一组**目录查询靶机测**：清单 SQL 必须跑得通（允许空集、不允许报错），并建一个探测视图验证视图定义、结束即删；容器名由 `SELECT DATABASE()` / `SELECT current_schema()` 现取（不喂 `GetDatabasesAsync()` 的首项，原因见 §4 的 PG 不一致） |
| 目录查询（纯逻辑） | `CatalogQueryTests`：方言路由、能力矩阵（含 `Schema`）、SQL 形状（目标系统表/收窄条件）、限定名与**转义**（SQLite `''`、MySQL `\\`+`` `` ``、PG 双引号）、单库绑定标志——**不依赖任何数据库即可全绿** |
| 密钥库集成 | **Linux**：真访问本机 freedesktop Secret Service（`SecretServiceKeyProviderTests.cs`），**用唯一 `application` 属性隔离并结束即删**，不污染用户真实凭据；无会话总线/服务即 `Assert.Skip`。**macOS**：真访问 login keychain（`MacKeychainKeyProviderTests.cs`），唯一 `service` 隔离 + `suppressUi`（禁授权框）+ 结束即删；非 macOS `Assert.Skip`。信封加密本身一律用**假主密钥**（`FakeMasterKeyProvider`）纯单测，CI 无密钥库也全绿 |
| ViewModels | 纯逻辑测（不启 Avalonia）；测试工程 ProjectReference 壳工程；交互留到 Avalonia.Headless（M3 再引入）。**替身分层**：`StubXxx`（对话框/选择器）与 `ScriptedMetadataExplorer`（可编排「库→模式→对象」形状，让树的多层逻辑无需靶机即可测透，M5-2） |
| 交付门槛 | `dotnet build DdraigBench.slnx` 零警告零错误 + `dotnet test` 全绿，输出贴进交付说明 |

> ✅ **Headless UI 测试已部分接通（2026-09-18 实测）**：改用 **`Avalonia.Headless 12.1.3`（基础包，无 xunit 耦合）+ xUnit v3 `AssemblyFixture`**（`DdraigBench.Test/TestAppFixture.cs`：`AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting()`）。
> - **能用**：不触碰 Dispatcher 的控件与附加属性——`ResultColumns` 动态列已纳入回归（`HeadlessUiTests`，覆盖 M1 那个"整块不渲染"的坑）。
> - ⚠️ **不能用**：`Avalonia.Headless.XUnit 12.1.2` 钉 `xunit.v3.extensibility.core 3.2.2`，与本工程直接引用的 `xunit.v3 4.0.1` 不兼容（discovery 阶段即 `MissingMethodException: TestIntrospectionHelper.GetTestCaseDetails`）；而 `HeadlessUnitTestSession.StartNew(...).Dispatch(...)` 在本 MTP+fixture 组合下**实测两次挂死**（须限时杀掉）。故**构造 `TextEditor` 这类需要 UI 线程 Dispatcher 的控件仍无法在测试里做**（线程池线程构造会 `VerifyAccess` 抛 `InvalidOperationException`）——`EditorText` 的 UI 回归待后续（或改走 VSTest 通道时再试）。

---

## 8. Milestones 与验收

- [ ] **M1 黄金闭环（SQLite）**：Provider.Sqlite 进主工程+测试工程 → Core 工程（Connections/Metadata/Querying）→ MainWindow 连 SQLite 文件 → 左树展开 → `SELECT 1` 出网格。验收：xunit 全绿 + GPL 防回归测试进仓库 + 手动截图。
- [ ] **M2 多方言**：MySqlConnector / PostgreSQL **已接入并实测通过**（SqlServer 待靶机挂起）；连接对话框按 Provider 出参数表 ✅；GPL 防回归已升级为真测试 ✅（§7）。（"MasterPool 冒烟测试回写 §4" 已在 M1-1 完成。）
- [x] **M3 编辑器与会话**：SQL 编辑器（AvaloniaEdit 12.0.0，§2.7 实查通过）；事务模式（LeaseRawAsync）；取消执行；结果网格**服务端分页 + 行虚拟化**（M3-1..M3-4 均已完成，2026-10-05）。
- [x] **M4 生产力功能**：连接凭据加密存储（三平台 + UI 接线）、导出 CSV/INSERT、DDL 预览、ISqlDialect 落地。**已全部完成**（2026-10-05，含 M4-4 的壳工程接线）。
- [ ] **M5 元数据完整性**（2026-10-08 起）：把 IDbFirst 的缺口补齐到"用户可见、可测"。
  - [x] **M5-1 目录查询层 ✅（2026-10-08）**：`Dialects/CatalogQueries/`（`ICatalogQueries` + SQLite/MySQL/PG 三实现 + `CatalogQuerySets` 工厂，只造 SQL）+ `Metadata/CatalogQueryExecutor`（执行 + 按列名映射）+ `DbObjectInfo.Definition`；`FreeSqlMetadataExplorer` 改为"IDbFirst + 目录查询"双通道，`GetObjectsAsync` 支持 Trigger/StoredProcedure/Function（方言没有即空集，不报错），`GetDdlAsync` 改为**原始定义优先**（表：SQLite 保留 AUTOINCREMENT/UNIQUE/CHECK/COLLATE；视图：三方真实定义；触发器/例程：按名取定义）。壳层：库节点平铺触发器/例程、`CanPreviewDdl` 放开这三类、展开失败收敛成状态栏（fire-and-forget 异常不再被吞）。顺带修 **MySQL 字面量反斜杠未转义**（导出 INSERT 会静默改写含 `\` 的数据）。测试 **218 个（204 通过 / 14 跳过 / 0 失败）**，构建 0 警告 0 错误。
  - [x] **M5-2 模式层与 PG 库清单 ✅（2026-10-08）**：`DbObjectKind.Schema` + `ICatalogQueries` 增 `ConnectionIsBoundToSingleDatabase` / `BuildCurrentDatabaseQuery`；`FreeSqlMetadataExplorer.GetDatabasesAsync` 对 PG 收窄为 `SELECT current_database()`（原先会把「看得见打不开」的库列进树）、`GetObjectsAsync` 支持 `Schema`（SQLite/MySQL 空集）；PG 模式清单滤掉 `pg_catalog`/`information_schema`/`pg_toast`/`pg_temp%`。壳层树变为「库 → 模式 → 对象」（有模式才加层，无模式保持 M5-1 原样），`ConnectionTreeNodeViewModel.Database` **改名 `Container`**（PG 下对象容器是模式名）。**修的既有缺陷**：PG 下库名被当作模式名查表，树通常展不开。测试 **227 个（213 通过 / 14 跳过 / 0 失败）**，构建 0 警告 0 错误。
  - [ ] **M5-3（待做）**：事件与分区、MySQL/PG 的触发器与例程定义、MySQL 表定义（`SHOW CREATE TABLE`）、PG 物化视图。**注意**：这些几乎全是"无靶机就无法实测"的方言代码——按 §8 的既定原则（SqlServer 挂起）应先把靶机（`DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG`）接上再做，别堆未验证的 SQL。
M0（依赖整改）已完成：`FreeSql.All` 已替换为裸 `FreeSql`，GPL 风险源已移除。UI 依赖骨架亦已就位（FluentAvaloniaUI 3.1.0 主题 + FluentIcons.Avalonia.Fluent 2.1.343 + Avalonia.Controls.DataGrid 12.1.2，§2.6）。

M1 进度：**M1-1 地基** + **M1-2 Querying** + **M1-3 Connections** + **M1-4 Metadata/Sessions** + **M1-6 UI 闭环** + **M1-7 GPL 防回归（静态断言）** 完成（2026-09-18）——Core 工程 + Provider.Sqlite（主/测/Core）+ SQLite 冒烟测试 + MasterPool 取还语义实测（§4）+ `QueryResult`/`QueryRunner` + `ConnectionProfile`/`FreeSqlFactory` + `IMetadataExplorer`/`FreeSqlMetadataExplorer`（Table/View/Column）+ `DbSession`/`ConnectionLease` + `MainViewModel`/`ConnectionTreeNodeViewModel`/`QueryTabViewModel` + `ResultColumns`（DataGrid 动态列附加属性）+ `IFilePicker` + `GplRegressionTests`。测试 25 个全绿（xunit.v3 4.0.1，含 VM 纯逻辑测）。M1 仅剩 **手动截图验收**（本环境无显示器，需人工 `dotnet run -c Release --project DdraigBench` 确认：打开 SQLite → 左树展开 → `SELECT 1` 出网格；用 Release 可绕开 IDE 预览器对 `bin\Debug` 的文件锁，见 §9）。

M2 进度：**M2-a 完成**（2026-09-18）——`FreeSql.Provider.MySqlConnector` + `FreeSql.Provider.PostgreSQL` 3.5.311 进 Core/主/测（§2.1）；GPL 防回归升级为**真测试**（程序集加载探测，§7）；多方言集成测试接通本地 AlmaLinux 靶机（MariaDB 10.5 / PostgreSQL 13），`SELECT 1` + `GetDatabasesAsync` 实跑通过。测试 30 个全绿（无靶机时 2 个集成测试跳过）。**M2 剩余**：SqlServer 待靶机；连接对话框可再打磨（Provider 友好名、连接测试按钮）。

M2-b（2026-09-18）**完成**：`ConnectionParameters`/`ConnectionStringBuilder`（Core，按 Provider 生成连接串与建议名）+ `ConnectionDialogViewModel`（Provider 切换/校验/`BuildProfile`，纯逻辑）+ `ConnectionDialogView.axaml` + `IConnectionDialog`/`ConnectionDialog`（Avalonia `ShowDialog`，VM 不碰 Avalonia）落地；`MainViewModel` 的 SQLite 专用入口已换成对话框入口（`OpenConnectionCommand`），`MainWindow`/`App` 接线完成。测试 45 个全绿（无靶机时 2 个集成测试跳过）。

M2 收尾（2026-09-18）：连接对话框加 **Provider 友好名**（`ProviderOption.DisplayName`：SQLite（本地文件）/ MySQL / MariaDB / PostgreSQL）与 **测试连接** 按钮（`IConnectionTester` + `FreeSqlConnectionTester`，返回 `null` 即成功、失败给出原因；VM 侧 `TestSucceeded`/`TestFailure`）。SqlServer **明确挂起**（无靶机 → 不装包、不入对话框，见 §2.2）。测试 52 个全绿（无靶机时 2 跳过；接靶机时 52/52）。**M2 至此收口**，仅余 SqlServer 一项待靶机。

M3 进度（2026-09-18 起，2026-10-05 收口）：
- **M3-1 取消执行 ✅**：`QueryTabViewModel` 接 `CancellationTokenSource`（`Cancel()`/`CanCancel`，含"执行刚开始时按下也生效"的 `_cancelRequested` 兜底），token 贯穿 `QueryRunner.ExecuteAsync` → ADO；执行中显示"取消"按钮（`MainWindow` 绑 `SelectedTab.CanCancel`）。
- **M3-2 事务模式 ✅**：`QueryTabViewModel` 持租约 + `DbTransaction`，`QueryRunner` 加事务通道（SQL 跑在事务连接上）；`BEGIN → INSERT → ROLLBACK` 丢弃、`COMMIT` 持久、标签处置自动回滚；接新连接时旧标签先处置（回滚 + 归还租约）。测试 60 个全绿（无靶机时 2 跳过；接靶机 60/60）。
- **M3-3 SQL 编辑器 ✅**：`Avalonia.AvaloniaEdit 12.0.0`（§2.7 实查通过）+ 显式引入 `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`；`QueryTabView` 的 `TextBox` 换成 `TextEditor`（行号 / 等宽 / 不换行）；因 `TextEditor.Text` 非 `AvaloniaProperty`，用附加属性 `Controls/EditorText.Text` 双向绑定到 VM 的 `Sql`。
- **M3-4 结果上限与虚拟化 ✅**：`QueryResult` 加 `Truncated`（取满 `maxRows` 后**多读一行**判定，区分"正好 N 行"与"被截断"）。**行虚拟化**由 DataGrid 自带（只渲染可见行），我们只确保不关闭它。
- **M3-5 真·服务端分页 ✅（2026-10-05，把 M3-4 遗留的 LIMIT/OFFSET 收口）**：`Core/Querying/PagedSql.cs` 纯逻辑——把查询包成 `SELECT * FROM (<原语句>) AS "ddb_page" LIMIT n+1 OFFSET m`（**必须包派生表**：用户 SQL 可能已带 LIMIT、以分号结尾或含锁提示，直接拼接会语法错）。判定"是否查询"只看首词 SELECT/WITH；"是否已分页"做**顶层**关键字扫描（跟踪括号深度 + 字符串/引用标识符状态，故子查询里的 LIMIT 与字面量 `'limit'` 都不误伤）。`QueryTabViewModel` 以 `PageSize`（默认 1000）取代原 `MaxRows`，配 `CurrentPage`/`HasNextPage`/`HasPreviousPage` + `NextPage`/`PreviousPage` 命令，**刻意多取一行（`LIMIT pageSize+1`）判定有无下一页再裁掉**——注意这一行必须由 SQL 的 LIMIT 产生，`QueryRunner` 的读取上限拿不到它（初版就踩了：把 `Truncated` 当 `HasNextPage` 用，结果恒为 false）。`ExecuteAsync` 回第 1 页、翻页走私有 `RunAsync`（否则翻页会被重置回第 1 页）。非查询语句（INSERT/UPDATE/DDL）原样执行，看 `影响 N 行`。`QueryTabView` 换成分页工具条（每页行数 + ◀/▶ + 第 N 页）。测试：`PagedSqlTests`（26 = 7 个 Fact + 19 组 InlineData，含 CTE/分号/子查询 LIMIT/字面量/转义引号/非法参数/方言引用）+ `QueryTabViewModelTests` 4 个真 SQLite 分页往返（切片/最后一页/重执行回第 1 页/用户自带 LIMIT 不叠加/非查询语句原样执行）。全量 180 个（168 通过 / 12 跳过 / 0 失败）。
- **前置遗留已部分解决**：headless 平台已接通（`Avalonia.Headless` + xUnit v3 `AssemblyFixture`），`ResultColumns` 动态列已纳入回归 ✅；受 `xunit.v3` 版本与 `HeadlessUnitTestSession` 挂死限制，需 UI 线程的控件（如 `TextEditor`）仍无法测（§7）。

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
  - **UI 接线 ✅（2026-10-05）**：`Core/Connections/IConnectionStore` 抽象（`Protector` + `LoadAsync`/`SaveAsync`，`ConnectionStore` 实现它）；壳层 `IConnectionStoreFactory` + `SecretConnectionStoreFactory`（**整个密钥库探测放 `Task.Run`**——macOS 钥匙串调用是同步阻塞的原生调用，钥匙串被锁时会等授权，绝不能占 UI 线程）；`App.axaml.cs` 在 `window.Opened` 后 `await viewModel.InitializeAsync()`；`MainViewModel` 增 `SavedConnections`/`SelectedSavedConnection`/`OpenSavedConnectionCommand`/`ForgetSavedConnectionCommand`/`ProtectorName`/`SecretsEncrypted`/`SecurityWarning`/`HasSecurityWarning`，新建连接成功后按**名称**去重覆盖并落盘、打开已保存连接不重写文件；`MainWindow.axaml` 左栏加「已保存连接」ComboBox + 打开/移除 + 保护方式标签，顶部明文回退告警条（**普通 `Border` 自绘，见 §2.6 的 InfoBar 更正**）。新增 VM 用例 6 个（载入/明文告警/存储故障不崩启动/新建落盘去重/打开与移除/未选择时提示）。**已知边界**：编辑已保存连接仍走「新建连接」对话框（同名即覆盖）；尚未做启动时自动重连上次连接。
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
