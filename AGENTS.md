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
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 调试面板 | ✅ 已正确限定 Debug-only |
| **FreeSql** | 3.5.311 | ORM 核心 | ✅ **零传递依赖**（nuspec 实查为空），GPL 风险已随 FreeSql.All 移除而消除 |
| **FreeSql.Provider.Sqlite** | 3.5.311 | SQLite 方言 + 驱动（M1 黄金通道）→ 钉 System.Data.SQLite.Core 1.0.119（自带原生 e_sqlite3） | ✅ MIT；主工程 / Core / 测试工程三处均引用 |
| ReactiveUI.Avalonia | 12.1.2 | MVVM 桥 → ReactiveUI 24.2.0 + Splat 21.0.0 | ✅ 保留 |
| ReactiveUI.Avalonia.Autofac | 12.1.2 | Autofac 容器桥 → Splat.Autofac | ⚠️ **未接线**：Program/App 无任何 Autofac 配置，纯死重 |

### 2.2 Provider 引入策略（M1 已装 Sqlite，其余按 milestone 逐个加）

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

### 2.3 Provider → 驱动钉版表（选型前先看）

Provider 的驱动版本被 FreeSql 钉死，上游不动则无法升级：

| Provider | 钉住的驱动 | 版本 | 备注 |
|---|---|---|---|
| Sqlite | System.Data.SQLite.Core | 1.0.119 | 自带原生 e_sqlite3，**M1 首选** |
| MySqlConnector | MySqlConnector | 2.5.0 | MySQL 标准通道（MIT） |
| PostgreSQL | Npgsql + Newtonsoft.Json | 5.0.18 / 13.0.1 | 成熟 LTS 线 |
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

1. `<Application.Styles>` 里只有 `<sty:FluentAvaloniaTheme/>`，**禁止再放 `<FluentTheme/>` / `SimpleTheme` / 其他主题**（官方文档：并存会产生画刷冲突与视觉劣化）。**DataGrid 也不要额外 `StyleInclude`**——FluentAvaloniaTheme 已自带完整 DataGrid 主题（FluentAvalonia.dll 内含 `DataGridStyles`/`DataGridCellStyles`/`DataGridRowStyles`/`DataGridColumnHeaderStyles` 等）；再叠加 stock `Avalonia.Controls.DataGrid/Themes/Fluent.xaml` 会覆盖它、并在缺 `FluentTheme` 资源键时把网格渲染搞空（M1 实测）。
2. 图标全工程只用 FluentIcons 一家，禁止混用第二套图标字体（笔画/圆角不一致 = 视感崩坏）。
3. 图标包与主题包必须配对：上 FluentAvalonia 就用 `.Fluent` 变体（它钉 FluentAvaloniaUI ≥3.0.0）；只用内置 FluentTheme 才用裸 `FluentIcons.Avalonia`。
4. `Avalonia.Themes.Fluent` 包保留在框架四件套（FluentAvaloniaTheme 未声明依赖它，属可精简项，留待后续评估，勿擅动）。
5. 主题/图标只允许出现在 `DdraigBench` 壳工程（View 层），Core 保持零 UI 依赖（§3）。
6. 已知传递依赖 `Avalonia.Controls.ColorPicker 12.1.0`（FluentAvalonia 钉 ≥12.1.0）与主族 12.1.2 存在 patch 级差，semver 兼容，暂不处理。
7. 自定义控件若派生自主题化控件（`DataGrid` 等），**必须** `protected override Type StyleKeyOverride => typeof(基类);`——否则 `ControlTheme` 按派生类型查不到，模板缺失、整块不渲染且无报错（M1 实测）。能不改派生就优先用**附加属性**（M1 的结果网格即如此，见 `Controls/ResultColumns.cs`）。

---

## 3. 工程结构

```
DdraigBench.slnx
global.json                     # test.runner = Microsoft.Testing.Platform（见 §9）
├── DdraigBench/                  # Avalonia 壳（唯一引用 Avalonia 的工程）
│   ├── Program.cs / App.axaml
│   ├── Assets/
│   ├── Services/                # IFilePicker + StorageProviderFilePicker（M1-6）
│   ├── Controls/                # ResultColumns：DataGrid 动态列的附加属性（普通 DataGrid，不派生，M1-6）
│   ├── Views/                   # axaml + code-behind（仅 InitializeComponent）
│   │   ├── MainWindow.axaml     # 布局：左连接树 | 中 Tab 区 | 底状态面板（M1-6）
│   │   ├── QueryTabView.axaml   # SQL 编辑器 + 结果 DataGrid（M1-6）
│   │   └── ConnectionDialogView.axaml   # → 后续
│   └── ViewModels/              # 全部 ReactiveObject，构造注入 Core 接口
│       ├── MainViewModel        # 树 + Tab 集合 + 全局命令（M1-6）
│       ├── ConnectionTreeNodeViewModel  # 左树节点模型（惰性加载子节点，M1-6）
│       └── QueryTabViewModel    # 语句执行、结果集、耗时/行数状态（M1-6）
├── DdraigBench.Core/            # ★ 新建类库：零 UI 依赖，可完整单测
│   ├── Connections/
│   │   ├── ConnectionProfile.cs       # 连接定义（Name / DataType / ConnectionString，M1-3 已落地）
│   │   ├── ConnectionStore.cs         # 保存/读取连接（JSON + DPAPI/系统钥匙串）→ M4
│   │   └── FreeSqlFactory.cs          # ConnectionProfile → IFreeSql（每连接单例，M1-3 已落地）
│   ├── Sessions/
│   │   ├── DbSession.cs              # 一个打开的连接标签 = 一个会话（M1-4 已落地）
│   │   ├── ConnectionLease.cs        # MasterPool 取还句柄（M1-4 已落地）
│   │   └── ISessionManager.cs        # 生命周期、并发上限、关闭清理 → M3
│   ├── Metadata/
│   │   ├── IMetadataExplorer.cs      # 树的统一元数据 API（签名见 §4，M1-4 已落地）
│   │   ├── DbObjectKind.cs / DbObjectInfo.cs / DbColumnInfo.cs   # 元数据模型（M1-4）
│   │   ├── FreeSqlMetadataExplorer.cs# 包装 IDbFirst（M1-4：Table/View/Column）
│   │   └── CatalogQueries/           # 触发器/函数/分区等 IDbFirst 缺口的补齐 SQL → 后续
│   ├── Querying/
│   │   ├── QueryResult.cs            # Columns + Rows(object[][]) + Affected + 时长
│   │   └── QueryRunner.cs            # MasterPool 原始连接执行 + 流式分批读取（见 §4 实测）
│   └── Dialects/
│       └── ISqlDialect.cs            # 标识符引用/分页/类型映射钩子
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
}

// 执行：SQL 编辑器 → 网格的唯一通道。永远返回无类型结果。
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    long AffectedRows,
    TimeSpan Elapsed,
    string? Error);

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
- **M1 实测结论（2026-09-18，SQLite）**：`IDbFirst.GetDatabases()` 返回 `[main]`；`GetTablesByDatabase("main")` 同时含表与视图，`DbTableInfo.Type` 为 `FreeSql.DatabaseModel.DbTableType.TABLE/VIEW`，`Schema` 为空串。故 SQLite 的"库→表→列"可直接映射、无需特判；`GetObjectsAsync` 目前仅实现 Table/View，其余 kind 抛 `NotSupportedException`。单测见 `DdraigBench.Test/MetadataExplorerTests.cs`。

---

## 5. MVVM 与线程规则（ReactiveUI）

- `ViewModelBase : ReactiveObject`（已有）。绑定属性用 `this.RaiseAndSetIfChanged`；集合用 `ObservableCollection`。
- **一切 DB 调用不得跑在 UI 线程**：`Observable.FromAsync(...).SubscribeOn(RxApp.TaskpoolScheduler).ObserveOn(RxApp.MainThreadScheduler)` 或 `await Task.Run` + `Dispatcher.UIThread.Post`。
- ViewModels **禁止** `using Avalonia.*`（允许 ReactiveUI）。引用控件/颜色的 ViewModel 视为 bug。
- View code-behind 只留 `InitializeComponent`；逻辑进 ViewModel 或 Converter。
- 命令用 `ReactiveCommand.CreateFromTask`，长任务必须接 `CancellationToken`（窗口关闭即取消）。
- **ReactiveUI 24 已不依赖 `System.Reactive`**：命令用 `ReactiveCommand.Create*` 实现、以 `ICommand` 暴露属性（避免 `Unit` 来源问题），测试直接 await VM 的公开 async 方法。
- 交互能力（文件选择等）以接口注入 VM（如 `IFilePicker`/`StorageProviderFilePicker`），保持 VM 零 Avalonia 依赖且可单测。
- 结果网格用**普通 `DataGrid` + 附加属性** `ResultColumns.Headers`（`Controls/ResultColumns.cs`）按列头动态生成列；单元格用 `DataGridTemplateColumn` + `FuncDataTemplate<object?[]>` 从行数组直取（**不经绑定引擎**）。**不派生 `DataGrid`**。
- （教训）**派生自 Avalonia 主题化控件的自定义控件必须 override `StyleKeyOverride`**，否则 `ControlTheme` 按派生类型查不到 → 模板缺失 → **整块不渲染且无报错**。M1 首选改用**附加属性**正是为了避开这类派生陷阱。

§4 的服务契约经 Splat 或 Autofac 注入 ViewModel 构造函数（见 §2.4 D2）。

---

## 6. 元数据层策略

1. **能用 IDbFirst 就不手写**：`GetDatabasesAsync` → `IDbFirst.GetDatabases()`；表/列/主键/索引/外键/视图/存储过程 → `IDbFirst.GetTablesByDatabase()` 的 `DbTableInfo`（含 `IndexesDict` / `ForeignsDict`）。这是白嫖 22 方言的最大头。
2. **IDbFirst 缺口**（实测确认无 API）：**触发器、函数、事件、分区**。补齐方式：`Dialects/CatalogQueries/` 每库一组 SQL，素材**直接抄 FreeSql 对应 Provider 的 MIT 源码**（仓库 dotnetcore/FreeSql，保留版权声明）。
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
| GPL 防回归 | M1 用静态断言：全部 `*.csproj` 不含 `FreeSql.All` / `FreeSql.Provider.MySql` / `MySql.Data`，且输出目录无 `MySql.Data.dll`、`FreeSql.Provider.MySql.dll`（`DdraigBench.Test/GplRegressionTests.cs`）。M2 装 `Provider.MySqlConnector` 后升级为真测试：构建 `DataType.MySql` 的 IFreeSql（指向不存在主机），断言底层驱动集为 `MySqlConnector`、无 `MySql.Data` |
| ViewModels | 纯逻辑测（不启 Avalonia）；测试工程 ProjectReference 壳工程；交互留到 Avalonia.Headless（M3 再引入） |
| 交付门槛 | `dotnet build DdraigBench.slnx` 零警告零错误 + `dotnet test` 全绿，输出贴进交付说明 |

> ⚠️ **Headless UI 测试暂不可用（2026-09-18 实测）**：`Avalonia.Headless.XUnit 12.1.2` 钉 `xunit.v3.extensibility.core 3.2.2`，与本工程解析到的 `xunit.v3 4.0.1`（由 `xunit.runner.visualstudio 4.0.0` 传递）不兼容——discovery 阶段即 `MissingMethodException: TestIntrospectionHelper.GetTestCaseDetails`。M3 引入 headless 前必须先对齐 xunit.v3 版本（或改用 `Avalonia.Headless` 自建会话），届时把 `ResultDataGrid` 动态列纳入 headless 回归。

---

## 8. Milestones 与验收

- [ ] **M1 黄金闭环（SQLite）**：Provider.Sqlite 进主工程+测试工程 → Core 工程（Connections/Metadata/Querying）→ MainWindow 连 SQLite 文件 → 左树展开 → `SELECT 1` 出网格。验收：xunit 全绿 + GPL 防回归测试进仓库 + 手动截图。
- [ ] **M2 多方言**：MySqlConnector / PostgreSQL / SqlServer 接入 FreeSqlFactory；连接对话框按 Provider 出参数表；MasterPool 冒烟测试回写 §4。
- [ ] **M3 编辑器与会话**：SQL 编辑器（候选 AvaloniaEdit，**引入前先验证与 Avalonia 12.1 兼容版本**）；事务模式（LeaseRawAsync）；取消执行；结果网格分页（DataGrid 分页或虚拟化）。
- [ ] **M4 生产力功能**：连接凭据加密存储、导出 CSV/INSERT、DDL 预览、ISqlDialect 落地。

M0（依赖整改）已完成：`FreeSql.All` 已替换为裸 `FreeSql`，GPL 风险源已移除。UI 依赖骨架亦已就位（FluentAvaloniaUI 3.1.0 主题 + FluentIcons.Avalonia.Fluent 2.1.341 + Avalonia.Controls.DataGrid 12.1.2，§2.6）。

M1 进度：**M1-1 地基** + **M1-2 Querying** + **M1-3 Connections** + **M1-4 Metadata/Sessions** + **M1-6 UI 闭环** + **M1-7 GPL 防回归（静态断言）** 完成（2026-09-18）——Core 工程 + Provider.Sqlite（主/测/Core）+ SQLite 冒烟测试 + MasterPool 取还语义实测（§4）+ `QueryResult`/`QueryRunner` + `ConnectionProfile`/`FreeSqlFactory` + `IMetadataExplorer`/`FreeSqlMetadataExplorer`（Table/View/Column）+ `DbSession`/`ConnectionLease` + `MainViewModel`/`ConnectionTreeNodeViewModel`/`QueryTabViewModel` + `ResultDataGrid`（动态列）+ `IFilePicker` + `GplRegressionTests`。测试 24 个全绿（含 VM 纯逻辑测）。M1 仅剩 **手动截图验收**（本环境无显示器，需人工 `dotnet run --project DdraigBench` 确认：打开 SQLite → 左树展开 → `SELECT 1` 出网格）。

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
