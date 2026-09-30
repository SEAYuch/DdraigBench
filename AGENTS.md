# AGENTS.md — DdraigBench

> 面向在此仓库工作的编码 agent / 协作者。定位：单一事实来源（SSOT）。
> 规则：架构冲突时以本文档为准；文档过时则先改文档再改代码。
> 依赖事实全部经 NuGet nuspec / GitHub API 实查（基线 2026-09-18），非记忆。

---

## 1. 产品定义

**DdraigBench** = Avalonia + FreeSql 的跨平台数据库 GUI 客户端（对标 Navicat / DBeaver）。
名字：威尔士语 *y ddraig*（红龙 Y Ddraig Goch → 亚瑟 → Avalon → Avalonia）+ Bench。双写 dd 是威尔士语定冠词后的软化变位，拼写必须保持 **Ddraig**，勿"纠正"为 Draig。

技术基线：net10.0 · Avalonia 12.1.2 · FluentAvaloniaUI 3.1.0（Fluent/WinUI 主题）· FluentIcons.Avalonia.Fluent 2.1.341 · ReactiveUI 24.2.0 · FreeSql 3.5.311 · xunit.v3 4.0.1。

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
| Avalonia / .Desktop / .Themes.Fluent / .Fonts.Inter | 12.1.2 | 框架四件套 | ✅ 统一 12.1.x |
| **FluentAvaloniaUI** | 3.1.0 | WinUI3 风格控件 + Fluent v2 主题（取代 `<FluentTheme/>`） | ✅ MIT，nuspec 钉 Avalonia ≥12.1.0；详见 §2.6 |
| **FluentIcons.Avalonia.Fluent** | 2.1.341 | Fluent UI System Icons 图标控件（配对 FluentAvalonia） | ✅ MIT，nuspec 钉 FluentAvaloniaUI ≥3.0.0；详见 §2.6 |
| Avalonia.Controls.DataGrid | 12.1.2 | 结果集网格（D1） | ✅ 显式引用，家族统一 12.1.x |
| **Avalonia.AvaloniaEdit** | 12.0.0 | SQL 编辑器（M3-3，官方 AvaloniaUI 移植） | ✅ MIT，nuspec 钉 Avalonia ≥12.0.0；详见 §2.7 |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 调试面板 | ✅ 已正确限定 Debug-only |
| **FreeSql** | 3.5.311 | ORM 核心 | ✅ **零传递依赖**（nuspec 实查为空），GPL 风险已随 FreeSql.All 移除而消除 |
| **FreeSql.Provider.Sqlite** | 3.5.311 | SQLite 方言 + 驱动（M1 黄金通道）→ 钉 System.Data.SQLite.Core 1.0.119（自带原生 e_sqlite3） | ✅ MIT；主工程 / Core / 测试工程三处均引用 |
| **FreeSql.Provider.MySqlConnector** | 3.5.311 | MySQL/MariaDB 方言 + 驱动（M2）→ 钉 MySqlConnector 2.5.0 | ✅ MIT；主/测/Core 三处均引用 |
| **FreeSql.Provider.PostgreSQL** | 3.5.311 | PostgreSQL 方言 + 驱动（M2）→ 钉 Npgsql.LegacyPostgis / Npgsql.NetTopologySuite 5.0.18 + Newtonsoft.Json 13.0.1 | ✅ MIT；主/测/Core 三处均引用 |
| ReactiveUI.Avalonia | 12.1.2 | MVVM 桥 → ReactiveUI 24.2.0 + Splat 21.0.0 | ✅ 保留 |
| ReactiveUI.Avalonia.Autofac | 12.1.2 | Autofac 容器桥 → Splat.Autofac | ⚠️ **未接线**：Program/App 无任何 Autofac 配置，纯死重 |

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
- **D3 主题与图标栈**：✅ **已解决**（2026-09-18）。取 FluentAvaloniaUI 3.1.0 + FluentIcons.Avalonia.Fluent 2.1.341，考查表、决策与备选见 §2.6。

### 2.5 Test 工程

**xunit.v3 4.0.1** / Test.Sdk 18.10.1 / coverlet 10.0.1 ✅（2026-09-18 由 `xunit 2.9.3` 升为 `xunit.v3 4.0.1`，与 Pathfinder1eHelper 对齐）。`xunit.v3` 要求测试工程 `<OutputType>Exe</OutputType>`，并 `NoWarn xUnit1051` 以保零告警。**已增补（M1-1）**：`FreeSql` + `FreeSql.Provider.Sqlite` 3.5.311，以及对 `DdraigBench.Core` 的 ProjectReference——集成测试用临时文件 SQLite（`Path.GetTempPath`），无需 docker。另增对 `DdraigBench` 壳工程的 ProjectReference，以便纯逻辑测 ViewModel（§7，不启 Avalonia）。

> ⚠️ 测试工程是 **MTP 应用**（`xunit.v3` + `Microsoft.Testing.Platform`）。.NET 10 下 `dotnet test <sln>` 的旧写法会报错，必须走 MTP 模式（§9 的 `global.json`）。**实测 MTP + xunit.v3 4.0.1 可正常发现并运行（25 绿）**；Pathfinder1eHelper 则走 VSTest 通道（其测试工程设 `IsTestingPlatformApplication=false`）——两种通道**互斥**，本项目选 MTP + `global.json`。

另增 **`Avalonia.Headless 12.1.2`**（仅测试工程；基础包、无 xunit 耦合）+ xUnit v3 `AssemblyFixture` 启动 headless 平台，用于控件级 UI 单测（§7 / `DdraigBench.Test/TestAppFixture.cs`）。

### 2.6 主题与图标栈（已定，2026-09-18 nuspec 实查）

考查对象为 NuGet 上的 Fluent 风格主题包与图标包：

| 包 | 版本 | 许可证 | 依赖的 Avalonia 线 | 判定 |
|---|---|---|---|---|
| Avalonia.Themes.Fluent | 12.1.2 | MIT | 内置（框架四件套） | 基线保留，但 `<FluentTheme/>` 元素已移除（见下） |
| **FluentAvaloniaUI**（amwx） | 3.1.0 | MIT | **≥12.1.0** ✅ | ✅ **采用**。WinUI3 控件：NavigationView / TabView / ContentDialog / InfoBar / SettingsExpander / TaskDialog |
| **FluentIcons.Avalonia.Fluent**（davidxuang） | 2.1.341 | MIT | 钉 FluentAvaloniaUI **≥3.0.0** ✅ | ✅ **采用**。微软 Fluent UI System Icons（Regular/Filled/Color），与主题同设计语言 |
| FluentIcons.Avalonia | 2.1.341 | MIT | ≥12.0.0 ✅ | 备选：仅用内置 FluentTheme 时选它（非 `.Fluent` 变体） |
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
6. 已知传递依赖 `Avalonia.Controls.ColorPicker 12.1.0`（FluentAvalonia 钉 ≥12.1.0）与主族 12.1.2 存在 patch 级差，semver 兼容，暂不处理。
7. 自定义控件若派生自主题化控件（`DataGrid` 等），**必须** `protected override Type StyleKeyOverride => typeof(基类);`——否则 `ControlTheme` 按派生类型查不到，模板缺失、整块不渲染且无报错（M1 实测）。能不改派生就优先用**附加属性**（M1 的结果网格即如此，见 `Controls/ResultColumns.cs`）。

### 2.7 SQL 编辑器包（M3 候选，2026-09-18 nuspec 实查）

| 包 | 版本 | 许可证 | 依赖的 Avalonia 线 | 判定 |
|---|---|---|---|---|
| **Avalonia.AvaloniaEdit**（AvaloniaUI 官方移植） | 12.0.0 | MIT | **≥12.0.0**（net10.0） | ✅ **采用**（M3-3 已落地，与 12.1.2 兼容），仅额外依赖 `Avalonia`；需显式引入其主题（§2.6） |
| AvaloniaEdit.TextMate | 12.0.0 | MIT | 钉 Avalonia.AvaloniaEdit 12.0.0 + Avalonia ≥12.0.0 | 备选：SQL 语法高亮（会再引 TextMateSharp 2.0.3 + TextMateSharp.Grammars 2.0.3），留待后续打磨 |

> §8 要求"引入前先验证与 Avalonia 12.1 兼容版本"，本节即该核查结果。

---

## 3. 工程结构

```
DdraigBench.slnx
global.json                     # test.runner = Microsoft.Testing.Platform（见 §9）
├── DdraigBench/                  # Avalonia 壳（唯一引用 Avalonia 的工程）
│   ├── Program.cs / App.axaml
│   ├── Assets/
│   ├── Services/                # IFilePicker（开+存）+ StorageProviderFilePicker + IConnectionDialog + IExportDialog + IDdlViewer（M1-6 / M2-b / M4-2 / M4-3）
│   ├── Controls/                # 附加属性：ResultColumns（DataGrid 动态列）+ EditorText（TextEditor 文本同步）
│   ├── Views/                   # axaml + code-behind（仅 InitializeComponent）
│   │   ├── MainWindow.axaml     # 布局：左连接树 | 中 Tab 区 | 底状态面板（M1-6）
│   │   ├── QueryTabView.axaml   # AvaloniaEdit SQL 编辑器 + 结果 DataGrid（M1-6 / M3-3）
│   │   └── ConnectionDialogView.axaml   # 按 Provider 出参数表（M2-b）
│   │   ├── ExportDialogView.axaml      # 导出对话框（M4-2）
│   │   └── DdlView.axaml               # DDL 预览窗口（M4-3）
│   └── ViewModels/              # 全部 ReactiveObject，构造注入 Core 接口
│       ├── MainViewModel        # 树 + Tab 集合 + 全局命令 + DDL 预览（M1-6 / M4-2 / M4-3）
│       ├── ConnectionTreeNodeViewModel  # 左树节点模型（惰性加载子节点，带 Database，M1-6 / M4-3）
│       ├── QueryTabViewModel    # 语句执行、取消、事务、结果集/状态、导出（M1-6 / M3 / M4-2）
│       ├── ConnectionDialogViewModel    # Provider 参数表 + 校验 + BuildProfile（M2-b）
│       ├── ExportDialogViewModel        # 导出保存路径 + INSERT 目标表名（M4-2）
│       └── DdlViewModel                 # DDL 只读展示（M4-3）
├── DdraigBench.Core/            # ★ 新建类库：零 UI 依赖，可完整单测
│   ├── Connections/
│   │   ├── ConnectionProfile.cs       # 连接定义（Name / DataType / ConnectionString，M1-3 已落地）
│   │   ├── ConnectionParameters.cs / ConnectionStringBuilder.cs   # 按 Provider 出参数 → 连接串（M2-b）
│   │   ├── ConnectionStore.cs         # 连接清单持久化：JSON + 口令保护（连接串加密，名称/方言明文）（M4-4）
│   │   ├── FreeSqlFactory.cs          # ConnectionProfile → IFreeSql（每连接单例，M1-3 已落地）
│   │   └── IConnectionTester.cs / FreeSqlConnectionTester.cs      # 连接测试（M2-b）
│   ├── Secrets/
│   │   ├── ISecretProtector.cs        # Protect/Unprotect 抽象（含 IsEncrypted 供 UI 告警）（M4-4）
│   │   ├── DpapiSecretProtector.cs    # Windows DPAPI（CurrentUser 作用域）（M4-4）
│   │   ├── PlainTextSecretProtector.cs # 回退明文（前缀 plain:，供读旧文件/无密钥库平台）
│   │   └── SecretProtectors.cs        # 按平台选实现 / 按文件记录名取历史实现
│   ├── Sessions/
│   │   ├── DbSession.cs              # 一个打开的连接标签 = 一个会话（M1-4 已落地）
│   │   ├── ConnectionLease.cs        # MasterPool 取还句柄（M1-4 已落地）
│   │   └── ISessionManager.cs        # 生命周期、并发上限、关闭清理 → M3
│   ├── Metadata/
│   │   ├── IMetadataExplorer.cs      # 树的统一元数据 API（签名见 §4，M1-4 已落地）
│   │   ├── DbObjectKind.cs / DbObjectInfo.cs / DbColumnInfo.cs   # 元数据模型（M1-4）
│   │   ├── FreeSqlMetadataExplorer.cs# 包装 IDbFirst（M1-4：Table/View/Column；M4-3：GetDdlAsync）
│   │   ├── DdlGenerator.cs       # DbTableInfo → CREATE TABLE/INDEX（M4-3，纯逻辑）
│   │   └── CatalogQueries/           # 触发器/函数/分区/视图定义等 IDbFirst 缺口的补齐 SQL → 后续
│   ├── Querying/
│   │   ├── QueryResult.cs            # Columns + Rows(object[][]) + Affected + 时长 + Truncated
│   │   ├── QueryRunner.cs            # MasterPool 原始连接执行 + 流式分批读取（见 §4 实测）
│   │   └── Exporting/ResultExporter.cs  # CSV / INSERT 脚本渲染（M4-2，纯逻辑）
│   └── Dialects/
│       ├── ISqlDialect.cs / SqlDialectBase.cs   # 方言钩子：标识符引用 / 分页 / 字面量渲染（M4-1）
│       ├── SqliteDialect.cs / MySqlDialect.cs / PostgreSqlDialect.cs
│       └── SqlDialects.cs            # DataType → ISqlDialect 工厂
└── DdraigBench.Test/            # Core 的全部单测/集成测（SQLite 黄金通道）
```

**新建 Core 的 csproj 要点**：`net10.0` + `Nullable enable` + `ImplicitUsings enable` + 仅引用 `FreeSql` 与所选 Provider；**禁止引用任何 Avalonia.* / ReactiveUI.* / Splat**。

> M1-1（2026-09-18）已落地：`DdraigBench.Core` 建成（仅 `FreeSql` + `FreeSql.Provider.Sqlite`）并加入 `DdraigBench.slnx`，主/测工程均已 ProjectReference 之。Core 内部各类文件随 M1-2..M1-5 逐个补齐。

---

## 4. 关键服务契约

```csharp
// 元数据：左侧树的唯一数据源。任何树节点懒加载都走它。
public interface IMetadataExplorer
{
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession s, CancellationToken ct = default);
    // M1 已实现 Table/View；Index/ForeignKey/StoredProcedure/Trigger/Function 待补齐（§6.2）
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(DbSession s, string database,
        DbObjectKind kind, CancellationToken ct = default);
    // M1-4 追加：列不属于数据库级签名，单列一法（对应 §6.3 的"表→列"级）
    Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(DbSession s, string database, string table,
        CancellationToken ct = default);
    // M4-3 追加：元数据 → DDL 预览；对象不存在返回 null。视图/触发器/函数定义 IDbFirst 不返回
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
- **M1 实测结论（2026-09-18，SQLite）**：`IDbFirst.GetDatabases()` 返回 `[main]`；`GetTablesByDatabase("main")` 同时含表与视图，`DbTableInfo.Type` 为 `FreeSql.DatabaseModel.DbTableType.TABLE/VIEW`，`Schema` 为空串。故 SQLite 的"库→表→列"可直接映射、无需特判；`GetObjectsAsync` 目前仅实现 Table/View，其余 kind 抛 `NotSupportedException`。单测见 `DdraigBench.Test/MetadataExplorerTests.cs`。

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

§4 的服务契约经 Splat 或 Autofac 注入 ViewModel 构造函数（见 §2.4 D2）。

---

## 6. 元数据层策略

1. **能用 IDbFirst 就不手写**：`GetDatabasesAsync` → `IDbFirst.GetDatabases()`；表/列/主键/索引/外键/视图/存储过程 → `IDbFirst.GetTablesByDatabase()` 的 `DbTableInfo`（含 `IndexesDict` / `ForeignsDict`）。这是白嫖 22 方言的最大头。
2. **IDbFirst 缺口**（实测确认无 API）：**触发器、函数、事件、分区**。补齐方式：`Dialects/CatalogQueries/` 每库一组 SQL，素材**直接抄 FreeSql 对应 Provider 的 MIT 源码**（仓库 dotnetcore/FreeSql，保留版权声明）。
   - ⚠️ **M4-3 实测补充缺口**：
     ① **视图定义**拿不到（`GetDdlAsync` 对 `DbTableType.VIEW` 只输出提示 + 列结构）；
     ② **SQLite 的 `AUTOINCREMENT` 识别不出**——`INTEGER PRIMARY KEY AUTOINCREMENT` 的 `AUTOINCREMENT` 不在声明类型里，FreeSql 不填 `IsIdentity`，只存在于 `sqlite_master.sql`；
     ③ 唯一约束（`UniquesDict`）当前不单独输出（其唯一索引一般已含于 `IndexesDict`，`IsUnique=true`）。
     以上都归入 `CatalogQueries/` 的后续工作（`SELECT sql FROM sqlite_master` / `pg_get_viewdef` / `SHOW CREATE VIEW`）。
3. 树节点惰性加载：展开才查子级；库→模式→表→列 四级各自 `DbObjectKind`。

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
| 多方言集成 | 靶机连接串走环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG`；未设置或 `Ado.ExecuteConnectTest()` 不可达即 `Assert.Skip`（无靶机时全绿）。**凭据不进仓库**（`MultiDialectIntegrationTests.cs`） |
| ViewModels | 纯逻辑测（不启 Avalonia）；测试工程 ProjectReference 壳工程；交互留到 Avalonia.Headless（M3 再引入） |
| 交付门槛 | `dotnet build DdraigBench.slnx` 零警告零错误 + `dotnet test` 全绿，输出贴进交付说明 |

> ✅ **Headless UI 测试已部分接通（2026-09-18 实测）**：改用 **`Avalonia.Headless 12.1.2`（基础包，无 xunit 耦合）+ xUnit v3 `AssemblyFixture`**（`DdraigBench.Test/TestAppFixture.cs`：`AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting()`）。
> - **能用**：不触碰 Dispatcher 的控件与附加属性——`ResultColumns` 动态列已纳入回归（`HeadlessUiTests`，覆盖 M1 那个"整块不渲染"的坑）。
> - ⚠️ **不能用**：`Avalonia.Headless.XUnit 12.1.2` 钉 `xunit.v3.extensibility.core 3.2.2`，与本工程直接引用的 `xunit.v3 4.0.1` 不兼容（discovery 阶段即 `MissingMethodException: TestIntrospectionHelper.GetTestCaseDetails`）；而 `HeadlessUnitTestSession.StartNew(...).Dispatch(...)` 在本 MTP+fixture 组合下**实测两次挂死**（须限时杀掉）。故**构造 `TextEditor` 这类需要 UI 线程 Dispatcher 的控件仍无法在测试里做**（线程池线程构造会 `VerifyAccess` 抛 `InvalidOperationException`）——`EditorText` 的 UI 回归待后续（或改走 VSTest 通道时再试）。

---

## 8. Milestones 与验收

- [ ] **M1 黄金闭环（SQLite）**：Provider.Sqlite 进主工程+测试工程 → Core 工程（Connections/Metadata/Querying）→ MainWindow 连 SQLite 文件 → 左树展开 → `SELECT 1` 出网格。验收：xunit 全绿 + GPL 防回归测试进仓库 + 手动截图。
- [ ] **M2 多方言**：MySqlConnector / PostgreSQL **已接入并实测通过**（SqlServer 待靶机挂起）；连接对话框按 Provider 出参数表 ✅；GPL 防回归已升级为真测试 ✅（§7）。（"MasterPool 冒烟测试回写 §4" 已在 M1-1 完成。）
- [ ] **M3 编辑器与会话**：SQL 编辑器（候选 AvaloniaEdit，**引入前先验证与 Avalonia 12.1 兼容版本**）；事务模式（LeaseRawAsync）；取消执行；结果网格分页（DataGrid 分页或虚拟化）。
- [ ] **M4 生产力功能**：连接凭据加密存储、导出 CSV/INSERT、DDL 预览、ISqlDialect 落地。

M0（依赖整改）已完成：`FreeSql.All` 已替换为裸 `FreeSql`，GPL 风险源已移除。UI 依赖骨架亦已就位（FluentAvaloniaUI 3.1.0 主题 + FluentIcons.Avalonia.Fluent 2.1.341 + Avalonia.Controls.DataGrid 12.1.2，§2.6）。

M1 进度：**M1-1 地基** + **M1-2 Querying** + **M1-3 Connections** + **M1-4 Metadata/Sessions** + **M1-6 UI 闭环** + **M1-7 GPL 防回归（静态断言）** 完成（2026-09-18）——Core 工程 + Provider.Sqlite（主/测/Core）+ SQLite 冒烟测试 + MasterPool 取还语义实测（§4）+ `QueryResult`/`QueryRunner` + `ConnectionProfile`/`FreeSqlFactory` + `IMetadataExplorer`/`FreeSqlMetadataExplorer`（Table/View/Column）+ `DbSession`/`ConnectionLease` + `MainViewModel`/`ConnectionTreeNodeViewModel`/`QueryTabViewModel` + `ResultColumns`（DataGrid 动态列附加属性）+ `IFilePicker` + `GplRegressionTests`。测试 25 个全绿（xunit.v3 4.0.1，含 VM 纯逻辑测）。M1 仅剩 **手动截图验收**（本环境无显示器，需人工 `dotnet run -c Release --project DdraigBench` 确认：打开 SQLite → 左树展开 → `SELECT 1` 出网格；用 Release 可绕开 IDE 预览器对 `bin\Debug` 的文件锁，见 §9）。

M2 进度：**M2-a 完成**（2026-09-18）——`FreeSql.Provider.MySqlConnector` + `FreeSql.Provider.PostgreSQL` 3.5.311 进 Core/主/测（§2.1）；GPL 防回归升级为**真测试**（程序集加载探测，§7）；多方言集成测试接通本地 AlmaLinux 靶机（MariaDB 10.5 / PostgreSQL 13），`SELECT 1` + `GetDatabasesAsync` 实跑通过。测试 30 个全绿（无靶机时 2 个集成测试跳过）。**M2 剩余**：SqlServer 待靶机；连接对话框可再打磨（Provider 友好名、连接测试按钮）。

M2-b（2026-09-18）**完成**：`ConnectionParameters`/`ConnectionStringBuilder`（Core，按 Provider 生成连接串与建议名）+ `ConnectionDialogViewModel`（Provider 切换/校验/`BuildProfile`，纯逻辑）+ `ConnectionDialogView.axaml` + `IConnectionDialog`/`ConnectionDialog`（Avalonia `ShowDialog`，VM 不碰 Avalonia）落地；`MainViewModel` 的 SQLite 专用入口已换成对话框入口（`OpenConnectionCommand`），`MainWindow`/`App` 接线完成。测试 45 个全绿（无靶机时 2 个集成测试跳过）。

M2 收尾（2026-09-18）：连接对话框加 **Provider 友好名**（`ProviderOption.DisplayName`：SQLite（本地文件）/ MySQL / MariaDB / PostgreSQL）与 **测试连接** 按钮（`IConnectionTester` + `FreeSqlConnectionTester`，返回 `null` 即成功、失败给出原因；VM 侧 `TestSucceeded`/`TestFailure`）。SqlServer **明确挂起**（无靶机 → 不装包、不入对话框，见 §2.2）。测试 52 个全绿（无靶机时 2 跳过；接靶机时 52/52）。**M2 至此收口**，仅余 SqlServer 一项待靶机。

M3 进度（2026-09-18，切片进行中）：
- **M3-1 取消执行 ✅**：`QueryTabViewModel` 接 `CancellationTokenSource`（`Cancel()`/`CanCancel`，含"执行刚开始时按下也生效"的 `_cancelRequested` 兜底），token 贯穿 `QueryRunner.ExecuteAsync` → ADO；执行中显示"取消"按钮（`MainWindow` 绑 `SelectedTab.CanCancel`）。
- **M3-2 事务模式 ✅**：`QueryTabViewModel` 持租约 + `DbTransaction`，`QueryRunner` 加事务通道（SQL 跑在事务连接上）；`BEGIN → INSERT → ROLLBACK` 丢弃、`COMMIT` 持久、标签处置自动回滚；接新连接时旧标签先处置（回滚 + 归还租约）。测试 60 个全绿（无靶机时 2 跳过；接靶机 60/60）。
- **M3-3 SQL 编辑器 ✅**：`Avalonia.AvaloniaEdit 12.0.0`（§2.7 实查通过）+ 显式引入 `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`；`QueryTabView` 的 `TextBox` 换成 `TextEditor`（行号 / 等宽 / 不换行）；因 `TextEditor.Text` 非 `AvaloniaProperty`，用附加属性 `Controls/EditorText.Text` 双向绑定到 VM 的 `Sql`。
- **M3-4 结果上限与虚拟化 ✅**：`QueryResult` 加 `Truncated`（取满 `maxRows` 后**多读一行**判定，区分"正好 N 行"与"被截断"）；`QueryTabViewModel.MaxRows`（`decimal?`，配 `NumericUpDown`，默认 `QueryRunner.DefaultMaxRows`）；状态栏在截断时提示"已截断，可提高行数上限"。**行虚拟化**由 DataGrid 自带（只渲染可见行），我们只确保不关闭它。真·服务端分页（LIMIT/OFFSET）仍留 M4 的 `ISqlDialect`。
- **前置遗留已部分解决**：headless 平台已接通（`Avalonia.Headless` + xUnit v3 `AssemblyFixture`），`ResultColumns` 动态列已纳入回归 ✅；受 `xunit.v3` 版本与 `HeadlessUnitTestSession` 挂死限制，需 UI 线程的控件（如 `TextEditor`）仍无法测（§7）。

M4 进度（2026-09-18，切片进行中）：
- **M4-1 ISqlDialect 落地 ✅**：`ISqlDialect`（`QuoteIdentifier` / `BuildPaging` / `FormatLiteral`）+ `SqlDialectBase` + SQLite/MySQL/PostgreSQL 三实现 + `SqlDialects` 工厂。分页统一 `LIMIT n OFFSET m`（三方言均支持）；字面量含字符串转义（`''`）、日期、布尔（`TRUE/FALSE`）、二进制（PG 用 `'\x..'::bytea`，SQLite/MySQL 用 `X'..'`）。测试 73 个全绿（无靶机时 2 跳过）。
- **M4-2 导出 CSV / INSERT ✅**：`Core/Exporting/ResultExporter.cs` 纯逻辑（`ToCsv` 含分隔符/引号/换行转义与 null 空字段；`ToInsert` 标识符引用与字面量全交给 `ISqlDialect`，支持 `batchSize` 合并多行、空列名补 `columnN`）。壳层：`IFilePicker` 加 `PickSaveFileAsync`（StorageProvider 实现）+ `IExportDialog`/`ExportDialog`（保存路径 + INSERT 目标表名）+ `ExportDialogViewModel`（纯逻辑）+ `ExportDialogView.axaml`；`QueryTabViewModel` 增 `ExportCsvCommand`/`ExportInsertCommand` 与 `CanExport`（有列有行才可导），CSV 落盘**带 UTF-8 BOM**（Excel 友好）、INSERT 脚本**不带 BOM**；工具栏加两个按钮。INSERT 前先校验方言（`SqlDialects.TryGet`）——不支持则直接报状态、不弹对话框。测试 88 个全绿（无靶机时 2 跳过）。
- **M4-3 DDL 预览 ✅**：Core `Metadata/DdlGenerator.cs`（吃 FreeSql `DbTableInfo` → `CREATE TABLE` + `CREATE [UNIQUE] INDEX`，含列注释/DEFAULT/复合主键 `PK_<表>`/外键；**单列自增主键内联**——顺序方言不同，故 `ISqlDialect` 加 `IdentityClause` + `BuildIdentityColumn`（SQLite `PRIMARY KEY AUTOINCREMENT` / MySQL `NOT NULL AUTO_INCREMENT PRIMARY KEY` / PG `GENERATED BY DEFAULT AS IDENTITY`）；`sqlite_` 内部自动索引跳过（不可显式创建）；视图只给提示+列结构）。`IMetadataExplorer.GetDdlAsync` 串起取元数据+选方言。壳层：`IDdlViewer`/`DdlViewer` + `DdlView.axaml`（只读 TextBox，Ctrl+A/C 可复制）；树节点带 `Database`，`MainViewModel.SelectedNode` + `PreviewDdlCommand`/`CanPreviewDdl`，工具栏加「预览 DDL」。测试 97 个全绿（无靶机时 2 跳过），含**真实 SQLite 往返**（生成 DDL → DROP → 重新执行成功）。已知缺口见 §6.2。
- **M4-4 连接凭据加密存储（部分完成）**：`Core/Secrets/ISecretProtector`（`Name`/`IsEncrypted`/`Protect`/`Unprotect`）+ `DpapiSecretProtector`（`System.Security.Cryptography.ProtectedData` 10.0.12，**CurrentUser 作用域** + 固定 entropy）+ `PlainTextSecretProtector`（`plain:` 前缀，供无密钥库平台与读旧文件）+ `SecretProtectors` 工厂（`CreateDefault` / `CreateByName`）；`Core/Connections/ConnectionStore.cs`：camelCase JSON（`version`/`protector`/`connections`），**只加密连接串**、名称与方言明文便于列表展示，**先写 `.tmp` 再 `File.Move` 覆盖**（避免半截文件），文件缺失返回空清单。**明文文件在升级到 DPAPI 后仍可读**（按文件里的 `protector` 字段取实现），未知 `protector` 值直接报明确错误。测试 103 个全绿（无靶机时 2 跳过），含**真落盘断言：DPAPI 落盘后文件里搜不到 `s3cret-pw` 与 `Server=db`**。**未完成：Linux Secret Service(libsecret) / macOS Keychain 两个实现（本开发机为 Windows，无法实测）**，当前非 Windows 平台走明文回退（`IsEncrypted=false`，UI 须告警）。

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
