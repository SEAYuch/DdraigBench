# DdraigBench

> **Ddraig**（威尔士语 *y ddraig*，"红龙" Y Ddraig Goch → 亚瑟 → Avalon → **Avalonia**）+ **Bench**：一个基于 Avalonia + FreeSql 的跨平台数据库 GUI 客户端，对标 Navicat / DBeaver。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Tests: 415 total](https://img.shields.io/badge/tests-415%20total-success.svg)]

---

## 现状

| 项 | 状态 |
|---|---|
| 构建 | `dotnet build DdraigBench.slnx -c Release` — **零警告零错误** |
| 测试 | **415 个用例**（离线通道 383 通过 / 32 跳过。跳过项全部是靶机与平台相关用例——未设环境变量的方言集成测、平台密钥库与 Unix 权限类用例。**靶机补跑（2026-10-15）**：接入 `DDRAIGBENCH_MYSQL`+`DDRAIGBENCH_PG`（MariaDB 10.5.29 / PostgreSQL 13.23）为 **382 通过 / 25 跳过 / 0 失败**；SQL Server 靶机（192.168.17.130:1433）当日拒绝连接，设了 `DDRAIGBENCH_SQLSERVER` 后 SQL Server 用例按设计连接超时并 `Assert.Skip`（整轮 382 通过 / 25 跳过 / 0 失败、无挂起）；**随后查明是该机上 rootless podman 容器 `ddb-sqlserver` 处于 `Exited`，`podman start` 后约 30 秒恢复，接入三个靶机变量得 384 通过 / 23 跳过 / 0 失败**（9 个方言集成测全部真跑）；**2026-10-16** 又按用户要求把该靶机的 SQL Server 从 rootless podman 容器换成微软原生 `mssql-server` 包（`mssql-server.service` 为 `enabled` + `active`、1433 监听，SA 口令沿用不变，故无需改环境变量），三靶机通道复跑同为 **384 通过 / 23 跳过 / 0 失败**（安装与下载链路的坑详见 AGENTS.md §7）。上一轮（380 用例）离线为 348 通过 / 32 跳过，更早的 370 用例一轮接 MariaDB + PostgreSQL 为 345 通过 / 25 跳过，三靶机 347 通过 / 23 跳过）。**2026-10-16（测试债清零）**：新增 8 个用例（`EditorText` 5 + `Window` 2 + `SessionThreadAffinity` 1）→ 离线 **415 / 383 / 32 / 0**；本轮靶机通道未能复跑（`192.168.17.130` 无 ping 应答、SSH banner 交换超时，3306/5432/1433 虽被 TCP 应答但协议层无响应），待宿主机恢复后按同一批用例补跑 |
| 已实现方言 | SQLite、MySQL / MariaDB、PostgreSQL、SQL Server |
| 里程碑 | M1 闭环 / M2 多方言 / M3 编辑器与会话 / M4 生产力功能 / M5-1 目录查询层 / M5-2 模式层 / **M5-3 元数据完整性 — 已落地并通过靶机验证** |

> 工作台人工验收已通过；截图未入库（见 AGENTS.md §8 的 M1 验收记录）。

## 功能

**连接**
- 按 Provider 出参数表的连接对话框（SQLite 本地文件 / MySQL / MariaDB / PostgreSQL / SQL Server）；SQL Server 默认启用加密并验证证书，自签证书仅可显式勾选信任选项
- 连接测试按钮（返回成功或可读的失败原因）
- 连接清单持久化：camelCase JSON，**连接串密码经系统密钥库加密**（Windows DPAPI / Linux+FreeBSD freedesktop Secret Service / macOS Keychain；无密钥库时回退明文并在顶部告警），名称与方言明文便于列表展示
- **回退明文告警会写明失败原因（2026-10-15）**：探测不到密钥库时不再只写"未检测到可用的系统密钥库"，而是给出可操作原因——如「请在"钥匙串访问"中删除 DdraigBench 条目后重试」「请解锁钥匙串后重试」「请改用签名构建」「请换到桌面会话」（`SecretStoreFallback` + `ISecretProtector.FallbackReason`，见 AGENTS.md §2.8 规则 4）
- 清单 UI：左侧已保存连接列表（双击激活）+ 标题栏「编辑」「移除」「新建」（未选中时编辑/移除禁用）+ 明文回退告警条；清单读取失败时**放弃本次会话写盘**，绝不覆盖既有文件

**对象浏览**
- 左侧树惰性加载：SQLite / MySQL「库 → 表 / 视图 / 触发器 / 函数 / 存储过程 / 事件 → 列」；PostgreSQL / SQL Server「当前库 → 模式 → 对象组 → 对象 → 列」（SQL Server M5-3 目录与模式层已靶机验证）
- 树筛选（只筛已加载节点，不清库）与展开失败收敛（失败写状态栏、清掉失败前塞进去的半个子树，并可右键「重试加载」或折叠后重新展开，不留"静默空树"）
- **DDL 预览**：**原始定义优先**（SQLite 读 `sqlite_master.sql`，保留 `AUTOINCREMENT` / 内联 `UNIQUE` / `CHECK` / `COLLATE`；MySQL 表走 `SHOW CREATE TABLE`，保住 `ENGINE=` / 排序规则 / 表注释；SQL Server 从 `sys.sql_modules` 取视图 / DML 触发器 / 函数 / 过程定义；PG 视图与物化视图分别拼 `CREATE [OR REPLACE] VIEW` / `CREATE MATERIALIZED VIEW`——均已在真靶机实测），取不到才按 `IDbFirst` 元数据重建 `CREATE TABLE` / `CREATE [UNIQUE] INDEX` / 外键 / 注释 / DEFAULT，可直接复制执行

**查询**
- SQL 编辑器（AvaloniaEdit）：行号、等宽、不换行
- 执行 / **取消**（长查询可中断）
- **事务模式**：显式提交 / 回滚，切换连接时自动回滚
- **真·服务端分页**（M3-5）：SQLite / MySQL / PostgreSQL 查询包成派生表 + `LIMIT/OFFSET`，多取一行判定"有无下一页"；工具条可配每页行数并翻页；结果网格行虚拟化。SQL Server 仅对顶层带 `ORDER BY` 的简单 SELECT 直加 `OFFSET/FETCH` 服务端分页（不包派生表）；无序、多语句、已有分页及含 `TOP`/`OPTION`/`FOR` 冲突语句 fail-closed，仍走客户端读取上限
- 结果集单元格直读无类型行数组（不经绑定引擎）；**列类型感知**：按已读回的值推断列类型，数字与日期右对齐、文本左对齐；二进制列（BLOB）显示为十六进制前缀，超长只截前 32 字节并标注总长，不再显示 `System.Byte[]`
- **表头点击排序（跨页）**：按列类型比较（数字按数值、"10" > "9"；日期按时间点；空值一律排最后；同分保持原顺序），再点同一列切换升降序。能下推就**下推到服务端**——排序作用在整表上，翻页仍是全局有序；下推不成立（语句自带顶层 `LIMIT`、SQL Server 已有顶层 `ORDER BY` 或列名重复/是表达式、事务中）时退回"只排当前页已读回的行"并在状态栏写明
- **导出默认路径**：默认落在「文档」目录下的 `export.csv` / `export.sql`（绝对路径），不再相对进程工作目录（快捷方式启动时那是 `System32` 之类不可写的位置）

**导出**
- **CSV**（落盘带 UTF-8 BOM，Excel 直开不乱码）
- **INSERT 脚本**（标识符引用与字面量按方言渲染，支持每语句多行）

**方言层**（`ISqlDialect`）
- 标识符引用、分页子句、字面量渲染、自增列定义——SQLite / MySQL / PostgreSQL / SQL Server；SQL Server 由 `PagedSql` 对带顶层 `ORDER BY` 的简单查询直加 `OFFSET/FETCH`；不使用派生表分页

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
- **多方言集成** → 读环境变量 `DDRAIGBENCH_MYSQL` / `DDRAIGBENCH_PG` / `DDRAIGBENCH_SQLSERVER`；未设置或不可达即 `Assert.Skip`（**凭据不进仓库**）。SQL Server 基础测试与 M5-3 集成测（schema、表/视图/函数/过程/DML trigger 清单与定义），以及 MySQL/PG 的触发器与例程定义、PG 物化视图、MySQL 原始表定义（`SHOW CREATE TABLE`）和 MySQL 计划事件（建→列→取定义→重执行→清理全链路）和 MySQL 分区表树（建 RANGE 表→展开→核对分区名/方法/表达式/边界→清理）均已在 MariaDB 10.5.29 / PostgreSQL 13.23 / SQL Server 2022 Developer 靶机实测通过
- **密钥库集成** → 真访问本机 freedesktop Secret Service；用唯一 `application` 属性隔离并结束即删，**不污染真实凭据**；无会话总线即 `Assert.Skip`。信封加密本身用假主密钥纯单测，**CI 无密钥库也全绿**
- **ViewModel** → 纯逻辑测，不启 Avalonia
- **控件级** → `Avalonia.Headless` + xUnit v3 `AssemblyFixture`：fixture 在**专用线程**上 `SetupWithoutStarting()` 并运行该线程的 `Dispatcher.UIThread.MainLoop`，测试体经 `TestUiThread.Run/RunAsync` 编组到同一条 UI 线程，因此**可以**构造 `TextEditor`、真实 `Window` 与 `QueryTabView`（覆盖结果网格动态列、工作台折叠还原、强调色对比度、编辑器文本双向绑定与整窗挂载）

## 已知限制

诚实列出当前**未做 / 未实测**的部分：

1. **SQL Server 原始表 DDL 的产品边界**——Provider、连接对话框、连接测试、查询、当前库、schema 层和基础表/视图元数据已接入，连接/表 DDL 重建与 M5-3 的 schema、DML trigger、函数/过程目录及定义、视图原始定义均已在 SQL Server 2022 Developer（AlmaLinux 9.8 上 Podman）靶机实测。SQL Server catalog 不保存原始 `CREATE TABLE` 文本，表 DDL 只能由 FreeSql 元数据重建。SQL Server 服务端分页仅在顶层 `ORDER BY` 且无冲突语法时直加 `OFFSET/FETCH`，已靶机验证；`PagedSql` 不使用派生表包装，其他形状 fail-closed 并继续受客户端读取上限保护。
2. **M5-3 已完成；剩余为产品边界**——已落地并验证：视图定义（SQLite / MySQL / PG / SQL Server）、SQLite 触发器定义、schema 与触发器/函数/存储过程/事件清单，以及 **MySQL / PG 的触发器与例程定义**（2026-10-12 在 MariaDB 10.5.29 / PostgreSQL 13.23 靶机跑通建→列→取定义→清理全链路）、PG 物化视图定义（`relkind='m'` 拼 `CREATE MATERIALIZED VIEW`）、MySQL 原始表定义（`SHOW CREATE TABLE`）和 SQL Server schema / DML trigger / 函数 / 过程清单与定义及原始视图定义。仍无可读取的 SQL Server 原始 `CREATE TABLE` 文本（SQL Server 不持久化该语句，这是产品限制而非可补的目录查询缺口）；表 DDL 由 `DdlGenerator` 按元数据重建。SQL Server 的直接 `OFFSET/FETCH` 分页已在 SQL Server 2022 Developer 靶机验证，仅支持顶层有序简单查询，复杂/不安全形状 fail-closed。MySQL 计划事件已新增 `DbObjectKind.Event`，在 MariaDB 10.5.29 靶机完成周期/一次性事件的建→列→DDL 预览→定义重执行→清理验证；MySQL 分区现作为表节点下的惰性子树，通过 `information_schema.PARTITIONS` 读取（分区名、方法、表达式与边界）；该信息只在展开表节点时读取，普通/非分区表返回空集。MariaDB 10.5.29 靶测确认 `SHOW CREATE TABLE` 保留 `PARTITION BY`，独立子树亦通过分区表建→展开→断言→清理验证。按「未实测代码不入库」原则，后续方言查询需对应靶机验证。
3. **连接清单 UI 已支持新建 / 打开 / 编辑 / 删除**——2026-10-13：标题栏新增「编辑」按钮（绑 `EditSavedConnectionCommand`，未选中时禁用），对话框进入编辑模式（标题「编辑连接」、确认按钮「保存」）并回填连接串拆出的字段；保存时**就地替换**原条目、不改动列表顺序，重名会被拒绝（否则按名去重会静默吞掉同名条目），被编辑的正是活动连接时状态栏会提示"当前会话仍用旧设置，重新打开后生效"。连接串 → 字段的逆解析在 `Core/Connections/ConnectionStringParser.cs`，借三个真实驱动解析（不手写 `;` 切分），解析失败只回填名称与方言并给提示，绝不静默清空原连接串。2026-10-11 的移除入口（C4/`ForgetSavedConnectionCommand`）与 `SetThemeCommand` 死命令清理保持不变。**启动时自动重连上次连接已落地（2026-10-13）**：只有**连接成功**后才把连接名记进 `ui-settings.json`（连不上的连接不该成为下次启动的目标，否则服务器维护时每次启动都白等一次超时），下次启动按名在清单里找并连接，找不到就安静跳过并清掉失效记录；清单整体读不出来时**保留**该记录（读不出可能只是临时被占用，清掉等于把下次自启动的机会也删了）。移除连接时同步清记录，改名时把记录搬到新名字（名字是唯一身份）。偏好文件因此从"只存主题"扩为 `IUiSettingsStore`（主题 + 上次连接名同住一份文件、整份读写——拆成两个各写一半的存储会让切主题顺手抹掉上次连接名，有红测守着），旧版 PascalCase 的 `{"Theme":"Dark"}` 仍读得出来。
4. **需要 UI 线程的控件现已可在测试里构造（2026-10-16 解决）**——`TestAppFixture` 改在专用线程（`ddb-headless-ui`）上 `SetupWithoutStarting()` 再跑 `Dispatcher.UIThread.MainLoop`，`TestUiThread` 把测试体编组回这条线程，所以 `TextEditor`、`QueryTabView` 与 `Window` 都能真构造：`EditorTextTests`（5 例，含附加属性双向绑定与真实视图单例断言）+ `WindowTests`（2 例，含 `Show()`/`Close()` 与整窗挂载）。**此前根因**：那条初始化 UI 平台的线程没有消息循环，`InvokeAsync` 没人执行，于是 `HeadlessUnitTestSession` 在 MTP + fixture 组合下挂死、`new Window()` 抛 `InvalidOperationException`（`Dispatcher.VerifyAccess` ← `Compositor.RegisterForSerialization` ← `PresentationSource..ctor`）。红绿对照：把 `TestUiThread.Run` 改成直接调用（去掉编组）→ 恰好 7 个新测全红（`The calling thread cannot access this object because a different thread owns it.`）。
5. **线程归属类修复现在都有红测对照（2026-10-16 补齐最后一条）**——事务的取租约 / 提交 / 回滚移出 UI 线程（M4）：`DbSession` 是 `sealed`、`LeaseRawAsync` 不可覆写，此前只能由既有真 SQLite 事务往返测兜底。现加**实例级**观测缝 `DbSession.WorkObserved`（`internal`，生产路径恒 `null`，仅多一次 null 判断），`SessionThreadAffinityTests` 在自建非池线程上跑 `BeginTransactionAsync` + `CommitAsync`，断言 `lease-acquire` / `lease-return` 都没落在调用线程；去掉 `QueryTabViewModel` 两处 `Task.Run` 包裹会各自变红（±1 红，实测）。其余修复（启动崩溃、执行重入、并发连接、分页解析、清单版本闸、连接串转义、目录截断、取消契约、存储线程归属、展开失败重试、折叠展开还原、BLOB 显示、导出默认路径、列类型对齐与排序）同样有"先见红再修"的回归测。
6. **UI 视觉验收为人工项**——开发环境无显示器，截图未入库。强调按钮文字色被压掉的问题**已修复（2026-10-11）**：`App.axaml` 原先有一条无条件设 `Foreground` 的隐式 `Style Selector="TextBlock"`，把强调按钮内文字从 `Button` 继承的 `White` 压成正文色 `#FF1B2430`（蓝底 `#FF155A9C`，对比度约 2.2:1，实测）。现改为只在 `Style Selector="Window"` 上设一次前景色，按钮自身的 `Foreground` 在继承链上更近而胜出；回归测 `ThemeContrastTests` 用「样式表 + 继承」两路断言守住，并带 WCAG 4.5:1 对比度门槛。
7. `ReactiveUI.Avalonia.Autofac` 死重**已清理（2026-10-11，C5）**：从未接线的容器桥包已从 `DdraigBench.csproj` 删除，DI 走 Splat 默认定位器（按 AGENTS.md §2.4 D2 取 b）。
8. **结果网格排序的边界**——表头点击会尽量把 `ORDER BY` 下推到服务端（排序在 `LIMIT/OFFSET` 之前生效，因此跨页有序）；但用户 SQL 自带顶层 `LIMIT`、是多语句脚本或非查询语句时 `PagedSql` 拒绝改写，退回"只排当前页"；SQL Server 另有两条限制：已有顶层 `ORDER BY` 时不改写用户语义（保分页、不排序），列名重复或形如 `COUNT(*)` 的表达式列无法按名排序（宁可不排序也不给出会报错的 SQL）。这些情况状态栏都会注明"当前页 N 行"。

## 路线图

- M5-3：SQL Server 目录对象/定义查询**已靶机验证通过**；MySQL/PG 触发器与例程定义、PG 物化视图定义、MySQL 原始表定义（`SHOW CREATE TABLE`）、计划事件与表节点下的分区子树**均已靶机验证通过**（2026-10-12）。SQL Server 仅对带顶层 `ORDER BY` 的简单 SELECT 尝试直接 `OFFSET/FETCH`，无序/多语句/已有分页/TOP/OPTION/FOR 查询保留客户端读取上限；该 T-SQL 分支已于 2026-10-12 经 SQL Server 2022 Developer 靶机验证（有序首末页均取到预期记录，无序与危险 SQL 形状 fail-closed）。SQL Server 不保存原始 `CREATE TABLE` 文本，这不是可读取的原始定义通道；表 DDL 继续按元数据重建。
- 启动时自动重连上次连接——**已完成（2026-10-13）**，见「已知限制」3。
- 跨平台密钥库打磨（libsecret 直连评估 + 钥匙串失败原因可读提示）——**已完成（2026-10-15）**：libsecret 直连复核结论为**维持 D-Bus**（`Ace4896.DBus.Services.Secrets`；libsecret 本体 LGPL-2.1-or-later、需 4 个原生 `.so`、异步要 GLib 主循环，且没有任何我们缺的能力——collection/别名/`SearchItems`/`Lock`/`Unlock`/DH 会话/Prompt 都是 Secret Service D-Bus 规范自身能力；详见 AGENTS.md §2.8）；密钥库回退原因经 `SecretStoreFallback` 翻成可操作中文，顶部告警不再只写"未检测到可用的系统密钥库"
- 结果网格排序的**剩余边界打磨**（跨页/服务端排序已完成 2026-10-13，见「已知限制」8：用户 SQL 自带顶层 `LIMIT`、SQL Server 已有 `ORDER BY` 或表达式列名时仍退回当前页排序）
- 会话并发上限与生命周期——**已完成（2026-10-15）**：`Core/Sessions/ISessionManager.cs` + `SessionManager.cs` 落地（默认上限 `MaxOpenSessions = 1`，超限开新会话抛 `InvalidOperationException` 而不是静默多挂连接），`MainViewModel` 不再自行 `new DbSession`/`DisposeAsync`，`App` 在 `ShutdownRequested` 里先 `e.Cancel = true`、`await viewModel.DisposeAsync()` 之后才 `desktop.Shutdown()`（否则退出时连接池不释放）。详见 AGENTS.md §4「会话生命周期与并发上限」

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
