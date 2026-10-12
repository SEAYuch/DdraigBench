# DdraigBench

> **Ddraig**（威尔士语 *y ddraig*，"红龙" Y Ddraig Goch → 亚瑟 → Avalon → **Avalonia**）+ **Bench**：一个基于 Avalonia + FreeSql 的跨平台数据库 GUI 客户端，对标 Navicat / DBeaver。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Tests: 320 total](https://img.shields.io/badge/tests-320%20total-success.svg)]

---

## 现状

| 项 | 状态 |
|---|---|
| 构建 | `dotnet build DdraigBench.slnx -c Release` — **零警告零错误** |
| 测试 | **320 个用例**（离线通道 288 通过 / 32 跳过；接入 MySQL/PG 靶机后 295 通过 / 25 跳过 — 跳过项为无环境用例：SQL Server 靶机、macOS 钥匙串、freedesktop Secret Service、Unix 文件权限） |
| 已实现方言 | SQLite、MySQL / MariaDB、PostgreSQL、SQL Server |
| 里程碑 | M1 闭环 / M2 多方言 / M3 编辑器与会话 / M4 生产力功能 / M5-1 目录查询层 / M5-2 模式层 — **均已落地**（M5-3 元数据缺口仍在） |

> 工作台人工验收已通过；截图未入库（见 AGENTS.md §8 的 M1 验收记录）。

## 功能

**连接**
- 按 Provider 出参数表的连接对话框（SQLite 本地文件 / MySQL / MariaDB / PostgreSQL / SQL Server）；SQL Server 默认启用加密并验证证书，自签证书仅可显式勾选信任选项
- 连接测试按钮（返回成功或可读的失败原因）
- 连接清单持久化：camelCase JSON，**连接串密码经系统密钥库加密**（Windows DPAPI / Linux+FreeBSD freedesktop Secret Service / macOS Keychain；无密钥库时回退明文并在顶部告警），名称与方言明文便于列表展示
- 清单 UI：左侧已保存连接列表（双击激活）+ 标题栏「移除」（未选中时禁用）+ 明文回退告警条；清单读取失败时**放弃本次会话写盘**，绝不覆盖既有文件

**对象浏览**
- 左侧树惰性加载：SQLite / MySQL「库 → 表 / 视图 / 触发器 / 函数 / 存储过程 / 事件 → 列」；PostgreSQL / SQL Server「当前库 → 模式 → 对象组 → 对象 → 列」（SQL Server M5-3 目录与模式层已靶机验证）
- 树筛选（只筛已加载节点，不清库）与展开失败收敛（失败写状态栏、清掉失败前塞进去的半个子树，并可右键「重试加载」或折叠后重新展开，不留"静默空树"）
- **DDL 预览**：**原始定义优先**（SQLite 读 `sqlite_master.sql`，保留 `AUTOINCREMENT` / 内联 `UNIQUE` / `CHECK` / `COLLATE`；MySQL 表走 `SHOW CREATE TABLE`，保住 `ENGINE=` / 排序规则 / 表注释；SQL Server 从 `sys.sql_modules` 取视图 / DML 触发器 / 函数 / 过程定义；PG 视图与物化视图分别拼 `CREATE [OR REPLACE] VIEW` / `CREATE MATERIALIZED VIEW`——均已在真靶机实测），取不到才按 `IDbFirst` 元数据重建 `CREATE TABLE` / `CREATE [UNIQUE] INDEX` / 外键 / 注释 / DEFAULT，可直接复制执行

**查询**
- SQL 编辑器（AvaloniaEdit）：行号、等宽、不换行
- 执行 / **取消**（长查询可中断）
- **事务模式**：显式提交 / 回滚，切换连接时自动回滚
- **真·服务端分页**（M3-5）：SQLite / MySQL / PostgreSQL 查询包成派生表 + `LIMIT/OFFSET`，多取一行判定"有无下一页"；工具条可配每页行数并翻页；结果网格行虚拟化。SQL Server 当前禁用自动服务端分页（派生表内裸 `ORDER BY` 会报错/改变语义），查询仍可执行并受客户端读取上限保护
- 结果集单元格直读无类型行数组（不经绑定引擎）；**列类型感知**：按已读回的值推断列类型，数字与日期右对齐、文本左对齐；二进制列（BLOB）显示为十六进制前缀，超长只截前 32 字节并标注总长，不再显示 `System.Byte[]`
- **表头点击排序**：按列类型比较（数字按数值、"10" > "9"；日期按时间点；空值一律排最后；同分保持原顺序），再点同一列切换升降序，状态栏显示当前排序
- **导出默认路径**：默认落在「文档」目录下的 `export.csv` / `export.sql`（绝对路径），不再相对进程工作目录（快捷方式启动时那是 `System32` 之类不可写的位置）

**导出**
- **CSV**（落盘带 UTF-8 BOM，Excel 直开不乱码）
- **INSERT 脚本**（标识符引用与字面量按方言渲染，支持每语句多行）

**方言层**（`ISqlDialect`）
- 标识符引用、分页子句、字面量渲染、自增列定义——SQLite / MySQL / PostgreSQL / SQL Server；SQL Server 目前禁用 `PagedSql` 自动派生表分页

## 技术栈

| 组件 | 版本 | 许可证 |
|---|---|---|
| .NET | 10.0 | MIT |
| Avalonia（框架四件套 / Headless） | 12.1.3 | MIT |
| Avalonia.Controls.DataGrid（结果网格） | 12.1.2 | MIT |
| Avalonia.AvaloniaEdit（SQL 编辑器） | 12.0.0 | MIT |
| FluentAvaloniaUI（WinUI3 风格控件 + 主题） | 3.1.0 | MIT |
| FluentIcons.Avalonia.Fluent（图标，与主题配对） | 2.1.343 | MIT |
| ReactiveUI.Avalonia | 12.1.6 | MIT |
| FreeSql（方言引擎 + 元数据引擎） | 3.5.311 | MIT |
| FreeSql.Provider.Sqlite / MySqlConnector / PostgreSQL / SqlServer | 3.5.311 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.12 | MIT |
| Ace4896.DBus.Services.Secrets（freedesktop Secret Service 托管 D-Bus 绑定） | 1.6.0 | MIT |
| Tmds.DBus.Protocol（上者的传递依赖，无原生库） | 0.9x | MIT |
| xunit.v3 / Test.Sdk / coverlet | 4.0.1 / 18.10.1 / 10.1.0 | Apache-2.0 / MIT |

> 依赖许可证均经 NuGet nuspec 实查。**红线：任何位置不得引入 GPL 依赖**（尤其是 `FreeSql.All` / `FreeSql.Provider.MySql` / `MySql.Data`）——仓库内有防回归测试守着这条线。

## 架构

FreeSql 只当**方言引擎 + 元数据引擎**，不当 ORM 用；GUI 语义（会话、树、网格、编辑器）由本仓库自建。

```
┌──────────────────────────────────────────────┐
│ DdraigBench (Avalonia 壳)                     │
│  Views / ViewModels / Controls                │
└──────────────┬───────────────────────────────┘
               │ 仅依赖接口（构造函数注入）
┌──────────────▼───────────────────────────────┐
│ DdraigBench.Core (无 Avalonia 依赖, 可完整单测) │
│  Connections / Secrets / Sessions / Metadata  │
│  Querying / Exporting / Dialects              │
└──────────────┬───────────────────────────────┘
               │ IFreeSql (DbFirst + Ado)
        FreeSql.Provider.*  →  底层 ADO.NET 驱动
```

分层红线：`DdraigBench.Core` 禁引 Avalonia / ReactiveUI；ViewModel 禁引 Avalonia（文件选择、对话框等能力以接口注入，保持可单测）。完整契约见 **[AGENTS.md](AGENTS.md)**（单一事实来源，含实测结论与踩坑记录）。

## 快速开始

**前置**：.NET 10 SDK。仓库根 `global.json` 已声明测试运行器为 `Microsoft.Testing.Platform`（MTP）。

```bash
dotnet build DdraigBench.slnx                     # 构建
dotnet test  DdraigBench.slnx                     # 全部测试（MTP 模式，实测可用）
dotnet run   --project DdraigBench                # 启动应用
```

几个容易踩的点：

- **`.NET 10` + MTP 下测试的传参**：本仓库实测 `dotnet test DdraigBench.slnx` 与 `dotnet test --solution DdraigBench.slnx` 均可跑（输出 MTP 的"测试运行摘要"）；指名单个工程时用 `--project`。
- **IDE 锁文件**：Avalonia XAML 预览器会占用 `bin\Debug\net10.0\DdraigBench.dll`，导致复制失败（MSB3027）。验证时改用 `-c Release`，或先关闭预览面板。

## 测试

- **Core 逻辑** → xunit 单测；数据库依赖一律用**临时 SQLite 文件**（不 mock `IFreeSql`，走真连接）
- **GPL 防回归** → 静态断言 + 程序集加载探测（`MySqlConnector` 必须可加载，`MySql.Data` / `FreeSql.Provider.MySql` 必须加载失败）
- **多方言集成** → 读环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG` / `DDRAIGBENCH_SQLSERVER`；未设置或不可达即 `Assert.Skip`（**凭据不进仓库**）。SQL Server 基础测试与 M5-3 集成测（schema、表/视图/函数/过程/DML trigger 清单与定义），以及 MySQL/PG 的触发器与例程定义、PG 物化视图、MySQL 原始表定义（`SHOW CREATE TABLE`）和 MySQL 计划事件（建→列→取定义→重执行→清理全链路）均已在 MariaDB 10.5.29 / PostgreSQL 13.23 / SQL Server 2022 Developer 靶机实测通过
- **密钥库集成** → 真访问本机 freedesktop Secret Service；用唯一 `application` 属性隔离并结束即删，**不污染真实凭据**；无会话总线即 `Assert.Skip`。信封加密本身用假主密钥纯单测，**CI 无密钥库也全绿**
- **ViewModel** → 纯逻辑测，不启 Avalonia
- **控件级** → `Avalonia.Headless` + xUnit v3 `AssemblyFixture`（覆盖结果网格动态列、工作台折叠还原、强调色对比度；只 new 控件，不构造 `Window`——headless 下 Composition 归 fixture 线程，测试线程建 `TopLevel` 会抛 `InvalidOperationException`）

## 已知限制

诚实列出当前**未做 / 未实测**的部分：

1. **SQL Server 元数据仍不完整**——Provider、连接对话框、连接测试、查询、当前库、schema 层和基础表/视图元数据已接入，连接/表 DDL 重建与 M5-3 的 schema、DML trigger、函数/过程目录及定义、视图原始定义均已在 SQL Server 2022 Developer（AlmaLinux 9.8 上 Podman）靶机实测。SQL Server catalog 不保存原始 `CREATE TABLE` 文本，表 DDL 只能由 FreeSql 元数据重建。自动服务端分页有意关闭（`PagedSql` 的派生表包装不符合 T-SQL 对子查询 `ORDER BY` 的要求，且移除排序会改变语义），查询仍可执行并受客户端读取上限保护。
2. **M5-3 元数据仍有缺口**——已落地并验证：视图定义（SQLite / MySQL / PG / SQL Server）、SQLite 触发器定义、schema 与触发器/函数/存储过程清单，以及 **MySQL / PG 的触发器与例程定义**（2026-10-12 在 MariaDB 10.5.29 / PostgreSQL 13.23 靶机跑通建→列→取定义→清理全链路）、PG 物化视图定义（`relkind='m'` 拼 `CREATE MATERIALIZED VIEW`）、MySQL 原始表定义（`SHOW CREATE TABLE`）和 SQL Server schema / DML trigger / 函数 / 过程清单与定义及原始视图定义。仍缺：MySQL 分区的独立结构化树展示（分区 DDL 已随 `SHOW CREATE TABLE` 保真）和 SQL Server 安全服务端分页。SQL Server 不保存原始 `CREATE TABLE` 文本，表 DDL 由 `DdlGenerator` 按元数据重建。MySQL 计划事件已新增 `DbObjectKind.Event`，在 MariaDB 10.5.29 靶机完成周期/一次性事件的建→列→DDL 预览→定义重执行→清理验证；`SHOW CREATE TABLE` 原文也已在靶机确认保留 `PARTITION BY` 子句。按「未实测代码不入库」原则，后续方言查询需对应靶机验证。
3. **连接清单 UI 只能新建 / 打开 / 删除**——2026-10-11 起：左栏「连接」标题栏新增移除按钮（绑 `ForgetSavedConnectionCommand`，未选中时禁用），删除已有入口；与主题下拉框重复的死命令 `SetThemeCommand` 已删除（C4）。仍欠**编辑**已保存连接（改名 / 改连接串 / 改口令）。
4. **需要 UI 线程的控件无法在测试里构造**——如 `TextEditor`；`HeadlessUnitTestSession` 在 MTP + fixture 组合下实测挂死，`new Window()` 也会抛 `InvalidOperationException`（Composition 归 fixture 线程）。动态列等不触碰 Dispatcher 的控件已纳入 headless 回归。
5. **线程归属类修复无红测对照**——事务的取租约 / 提交 / 回滚移出 UI 线程（M4）：`DbSession` 是 `sealed` 且 `_sessionProvider` 无观测缝，由既有真 SQLite 事务往返测兜底；其余修复（启动崩溃、执行重入、并发连接、分页解析、清单版本闸、连接串转义、目录截断、取消契约、存储线程归属、展开失败重试、折叠展开还原、BLOB 显示、导出默认路径、列类型对齐与排序）均有"先见红再修"的回归测。
6. **UI 视觉验收为人工项**——开发环境无显示器，截图未入库。强调按钮文字色被压掉的问题**已修复（2026-10-11）**：`App.axaml` 原先有一条无条件设 `Foreground` 的隐式 `Style Selector="TextBlock"`，把强调按钮内文字从 `Button` 继承的 `White` 压成正文色 `#FF1B2430`（蓝底 `#FF155A9C`，对比度约 2.2:1，实测）。现改为只在 `Style Selector="Window"` 上设一次前景色，按钮自身的 `Foreground` 在继承链上更近而胜出；回归测 `ThemeContrastTests` 用「样式表 + 继承」两路断言守住，并带 WCAG 4.5:1 对比度门槛。
7. `ReactiveUI.Avalonia.Autofac` 死重**已清理（2026-10-11，C5）**：从未接线的容器桥包已从 `DdraigBench.csproj` 删除，DI 走 Splat 默认定位器（按 AGENTS.md §2.4 D2 取 b）。
8. **结果网格排序只排当前页**——服务端分页下手里只有当前页读回的行（没有整表），所以表头点击是"客户端排当前页"、**不跨页**；跨页排序要把 `ORDER BY` 下推到服务端，列在路线图。

## 路线图

- M5-3：SQL Server 目录对象/定义查询**已靶机验证通过**；MySQL/PG 触发器与例程定义、PG 物化视图定义、MySQL 原始表定义（`SHOW CREATE TABLE`）与 MySQL 计划事件**均已靶机验证通过**（2026-10-12）；剩余 MySQL 分区的独立树展示与 SQL Server 安全服务端分页。SQL Server 不保存原始 `CREATE TABLE` 文本，这不是可读取的原始定义通道；表 DDL 继续按元数据重建。
- 连接清单的**编辑**（重命名 / 改连接串 / 改口令；新建 / 打开 / 删除已就绪）
- 跨平台密钥库打磨：libsecret 直连评估、钥匙串 ACL 异常的用户可读提示
- 结果网格的**跨页 / 服务端排序**（表头点击排序当前页、数字与日期右对齐已就绪，见「已知限制」8）
- 会话并发上限与生命周期（`ISessionManager` 从未创建，见 AGENTS.md §3 的更正）

## 贡献

请先读 **[AGENTS.md](AGENTS.md)**，它是本仓库的单一事实来源。几条硬规则：

1. **验证优先**：任何交付都要贴出 `dotnet build` 与 `dotnet test` 的真实输出，不接受口头保证。
2. **小步提交**：每个子任务独立可编译、可测试，禁止"半成品大爆炸"式提交。
3. **不引新包先过审核**：确认许可证与兼容性，并回写 AGENTS.md 的依赖表。
4. **文件头注释**：每个新增 `.cs` 文件首行写 `// DdraigBench — <模块名>`。
5. **文档先改后改代码**：架构冲突以 AGENTS.md 为准。

## 许可证

[MIT](LICENSE) — 与全部依赖的许可证兼容，允许商用与闭源分发。

依赖均为 MIT / Apache-2.0（经 NuGet nuspec 实查）。本项目**不使用任何 GPL 依赖**。
