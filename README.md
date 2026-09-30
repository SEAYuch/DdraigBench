# DdraigBench

> **Ddraig**（威尔士语 *y ddraig*，"红龙" Y Ddraig Goch → 亚瑟 → Avalon → **Avalonia**）+ **Bench**：一个基于 Avalonia + FreeSql 的跨平台数据库 GUI 客户端，对标 Navicat / DBeaver。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Tests: 103 passed](https://img.shields.io/badge/tests-103%20passed%20%2F%202%20skipped-success.svg)]

---

## 现状

| 项 | 状态 |
|---|---|
| 构建 | `dotnet build DdraigBench.slnx -c Release` — **零警告零错误** |
| 测试 | **103 个用例**（101 通过 / 2 个靶机用例无环境变量时跳过） |
| 已实现方言 | SQLite、MySQL / MariaDB、PostgreSQL |
| 里程碑 | M1 闭环 / M2 多方言 / M3 编辑器与会话 / M4 生产力功能 — **均已落地** |

> 截图待补（开发环境无显示器，UI 验收为人工项，见「已知限制」）。

## 功能

**连接**
- 按 Provider 出参数表的连接对话框（SQLite 本地文件 / MySQL / MariaDB / PostgreSQL）
- 连接测试按钮（返回成功或可读的失败原因）
- 连接清单持久化：camelCase JSON，**连接串经系统密钥库加密**（Windows DPAPI），名称与方言明文便于列表展示

**对象浏览**
- 左侧树惰性加载：数据库 → 表 / 视图 → 列
- **DDL 预览**：从 `IDbFirst` 元数据渲染 `CREATE TABLE` / `CREATE [UNIQUE] INDEX` / 外键 / 注释 / DEFAULT，可直接复制执行

**查询**
- SQL 编辑器（AvaloniaEdit）：行号、等宽、不换行
- 执行 / **取消**（长查询可中断）
- **事务模式**：显式提交 / 回滚，切换连接时自动回滚
- 可配行数上限，取满即提示"已截断"；结果网格按行虚拟化
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
| Avalonia（框架四件套 / DataGrid / Headless） | 12.1.2 | MIT |
| Avalonia.AvaloniaEdit（SQL 编辑器） | 12.0.0 | MIT |
| FluentAvaloniaUI（WinUI3 风格控件 + 主题） | 3.1.0 | MIT |
| FluentIcons.Avalonia.Fluent（图标，与主题配对） | 2.1.341 | MIT |
| ReactiveUI.Avalonia | 12.1.2 | MIT |
| FreeSql（方言引擎 + 元数据引擎） | 3.5.311 | MIT |
| FreeSql.Provider.Sqlite / MySqlConnector / PostgreSQL | 3.5.311 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.12 | MIT |
| xunit.v3 / Test.Sdk / coverlet | 4.0.1 / 18.10.1 / 10.0.1 | Apache-2.0 / MIT |

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
dotnet test  --solution DdraigBench.slnx          # 全部测试（MTP 模式）
dotnet run   --project DdraigBench                # 启动应用
```

几个容易踩的点：

- **`.NET 10` 下不要写 `dotnet test DdraigBench.slnx`**（VSTest 目标已禁用）：传解决方案要加 `--solution`，传工程加 `--project`。
- **IDE 锁文件**：Avalonia XAML 预览器会占用 `bin\Debug\net10.0\DdraigBench.dll`，导致复制失败（MSB3027）。验证时改用 `-c Release`，或先关闭预览面板。

## 测试

- **Core 逻辑** → xunit 单测；数据库依赖一律用**临时 SQLite 文件**（不 mock `IFreeSql`，走真连接）
- **GPL 防回归** → 静态断言 + 程序集加载探测（`MySqlConnector` 必须可加载，`MySql.Data` / `FreeSql.Provider.MySql` 必须加载失败）
- **多方言集成** → 读环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG`；未设置或不可达即 `Assert.Skip`（**凭据不进仓库**）
- **ViewModel** → 纯逻辑测，不启 Avalonia
- **控件级** → `Avalonia.Headless` + xUnit v3 `AssemblyFixture`（覆盖结果网格动态列）

## 已知限制

诚实列出当前**未做 / 未实测**的部分：

1. **SQL Server 挂起**——无靶机，故不装 `FreeSql.Provider.SqlServer`、也不进连接对话框（避免未实测代码入库）。
2. **非 Windows 平台的密钥库未实现**——Linux Secret Service(libsecret) / macOS Keychain 尚未接入；当前非 Windows 走明文回退（`ISecretProtector.IsEncrypted == false`，UI 须告警）。
3. **需要 UI 线程的控件无法在测试里构造**——如 `TextEditor`；`HeadlessUnitTestSession` 在 MTP + fixture 组合下实测挂死。动态列等不触碰 Dispatcher 的控件已纳入 headless 回归。
4. **元数据缺口**（`IDbFirst` 不提供，需按方言查 catalog）：**视图定义、触发器、函数、事件、分区**；SQLite 的 `AUTOINCREMENT` 也无法从声明类型识别（故 DDL 预览会退化成 `INTEGER PRIMARY KEY`）。
5. **真·服务端分页未接线**——`ISqlDialect.BuildPaging` 已就绪，但尚未把用户 SQL 包进分页查询（当前是客户端行数上限 + 网格虚拟化）。
6. **UI 视觉验收为人工项**——开发环境无显示器，截图未入库。
7. `ReactiveUI.Avalonia.Autofac` 是未接线的死重（默认走构造函数注入），属待清理项。

## 路线图

- M5：SQL Server（需靶机）、连接清单的 UI 管理（增删改/多连接）
- 元数据补齐：视图定义 / 触发器 / 函数（`Metadata/CatalogQueries/`，素材抄 FreeSql 对应 Provider 的 MIT 源码）
- 跨平台密钥库：libsecret / Keychain
- 结果网格排序与列类型感知；`ISessionManager` 的并发上限与生命周期

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
