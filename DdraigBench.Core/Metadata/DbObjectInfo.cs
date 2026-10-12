// DdraigBench — 元数据对象描述

namespace DdraigBench.Core.Metadata;

/// <param name="Name">对象名。</param>
/// <param name="Schema">所属模式（SQLite 为 null）。</param>
/// <param name="Comment">注释（方言不提供时为 null）。</param>
/// <param name="Definition">
/// 完整可执行定义（M5-1 追加）。视图、触发器、函数、存储过程仅在方言支持且当前用户有权限读取时填入
/// （SQLite 从 <c>sqlite_master.sql</c> 读取，SQL Server 从系统目录模块定义读取）；不可见或不支持的定义为 null。
/// 表定义仅部分方言提供，其他情况由调用方回落到按元数据重建的 <c>DdlGenerator</c>。
/// </param>
public sealed record DbObjectInfo(
    string Name,
    string? Schema = null,
    string? Comment = null,
    string? Definition = null);
