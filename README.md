# DdraigBench

> **Ddraig**（威尔士语 *y ddraig*，"红龙" Y Ddraig Goch → 亚瑟 → Avalon → **Avalonia**）+ **Bench**：一个基于 Avalonia + FreeSql 的跨平台数据库 GUI 客户端，对标 Navicat / DBeaver。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Tests: 244 passed](https://img.shields.io/badge/tests-244%20passed%20%2F%2027%20skipped-success.svg)]

---

## 现状

| 项 | 状态 |
|---|---|
| 构建 | `dotnet build DdraigBench.slnx -c Release` — **零警告零错误** |
| 测试 | **271 个用例**（244 通过 / 27 跳过 — 跳过项为无环境用例：macOS 钥匙串、freedesktop Secret Service、Unix 文件权限、MySQL/PG 靶机） |
| 已实现方言 | SQLite、MySQL / MariaDB、PostgreSQL |
| 里程碑 | M1 闭环 / M2 多方言 / M3 编辑器与会话 / M4 生产力功能 / M5-1 目录查询层 / M5-2 模式层 — **均已落地**（M5-3 与 SQL Server 待靶机） |

> 截图待补（开发环境无显示器，UI 验收为人工项，见「已知限制」）。

## 功能

**连接**
- 按 Provider 出参数表的连接对话框（SQLite 本地文件 / MySQL / MariaDB / PostgreSQL）
- 连接测试按钮（返回成功或可读的失败原因）
- 连接清单持久化：camelCase JSON，**连接串密码经系统密钥库加密**（Windows DPAPI / Linux+FreeBSD freedesktop Secret Service / macOS Keychain；无密钥库时回退明文并在顶部告警），名称与方言明文便于列表展示
- 清单 UI：左侧已保存连接列表（双击激活）+ 明文回退告警条；清单读取失败时**放弃本次会话写盘**，绝不覆盖既有文件

**对象浏览**
- 左侧树惰性加载：SQLite / MySQL「库 → 表 / 视图 / 触发器 / 函数 / 存储过程 → 列」，PostgreSQL 多一层「模式」（M5-2）
- 树筛选（只筛已加载节点，不清库）与展开失败收敛（失败写状态栏，不留"静默空树"）
- **DDL 预览**：**原始定义优先**（SQLite 读 `sqlite_master.sql`，保留 `AUTOINCREMENT` / 内联 `UNIQUE` / `CHECK` / `COLLATE`），取不到才按 `IDbFirst` 元数据重建 `CREATE TABLE` / `CREATE [UNIQUE] INDEX` / 外键 / 注释 / DEFAULT，可直接复制执行

**查询**
- SQL 编辑器（AvaloniaEdit）：行号、等宽、不换行
- 执行 / **取消**（长查询可中断）
- **事务模式**：显式提交 / 回滚，切换连接时自动回滚
- **真·服务端分页**（M3-5）：查询包成派生表 + `LIMIT/OFFSET`，多取一行判定"有无下一页"；工具条可配每页行数并翻页；结果网格行虚拟化
- 结果集单元格直读无类型行数组（不经绑定引擎）

**导出**
- **CSV**（落盘带 UTF-8 BOM，Excel 直开不乱码）
- **INSERT 脚本**（标识符引用与字面量按方言渲染，支持每语句多行）

**方言层**（`ISqlDialect`）
- 标识符引用、分页子句、字面量渲染、自增列定义——SQLite / MySQL / PostgreSQL 三实现

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
| FreeSql.Provider.Sqlite / MySqlConnector / PostgreSQL | 3.5.311 | MIT |
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
- **多方言集成** → 读环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG`；未设置或不可达即 `Assert.Skip`（**凭据不进仓库**）
- **密钥库集成** → 真访问本机 freedesktop Secret Service；用唯一 `application` 属性隔离并结束即删，**不污染真实凭据**；无会话总线即 `Assert.Skip`。信封加密本身用假主密钥纯单测，**CI 无密钥库也全绿**
- **ViewModel** → 纯逻辑测，不启 Avalonia
- **控件级** → `Avalonia.Headless` + xUnit v3 `AssemblyFixture`（覆盖结果网格动态列）

## 已知限制

诚实列出当前**未做 / 未实测**的部分：

1. **SQL Server 挂起**——无靶机，故不装 `FreeSql.Provider.SqlServer`、也不进连接对话框（避免未实测代码入库）。
2. **M5-3 元数据仍有缺口**——已落地：视图定义（SQLite / MySQL / PG 三方）、SQLite 的触发器定义、触发器 / 函数 / 存储过程**清单**、PG 模式层（M5-1 / M5-2）。仍缺：事件（`information_schema.EVENTS`）、分区（`information_schema.PARTITIONS`）、MySQL/PG 的触发器与例程**定义**、MySQL 表定义（`SHOW CREATE TABLE`）、PG 物化视图（`relkind='m'`）。这些几乎全要 MySQL/PG 靶机实测，按「未实测代码不入库」原则挂起。
3. **两个命令尚无 UI 入口**——`ForgetSavedConnectionCommand`（删除已保存连接）与 `SetThemeCommand`（跟随系统 / 浅色 / 深色）已实现且有单测覆盖，但 XAML 未绑定控件；主题目前只按启动时读到的偏好应用。
4. **需要 UI 线程的控件无法在测试里构造**——如 `TextEditor`；`HeadlessUnitTestSession` 在 MTP + fixture 组合下实测挂死。动态列等不触碰 Dispatcher 的控件已纳入 headless 回归。
5. **线程归属类修复无红测对照**——事务的取租约 / 提交 / 回滚移出 UI 线程（M4）：`DbSession` 是 `sealed` 且 `_sessionProvider` 无观测缝，由既有真 SQLite 事务往返测兜底；其余审计修复（启动崩溃、执行重入、并发连接、分页解析、清单版本闸、连接串转义、目录截断、取消契约、存储线程归属）均有"先见红再修"的回归测。
6. **UI 视觉验收为人工项**——开发环境无显示器，截图未入库；`App.axaml` 的全局 `TextBlock` 前景色样式是否会压掉强调按钮文字色亦未经截图核实。
7. `ReactiveUI.Avalonia.Autofac` 是未接线的死重（默认走构造函数注入），属待清理项。

## 路线图

- M5-3：事件与分区、MySQL/PG 触发器与例程定义、MySQL 表定义、PG 物化视图（**需先接上 MySQL/PG 靶机**）
- M5：SQL Server 支持（需靶机）
- 连接清单的增删改 UI（删除命令已就绪）+ 主题切换入口
- 跨平台密钥库打磨：libsecret 直连评估、钥匙串 ACL 异常的用户可读提示
- 结果网格排序与列类型感知（BLOB/日期显示）；`ISessionManager` 的并发上限与生命周期

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
