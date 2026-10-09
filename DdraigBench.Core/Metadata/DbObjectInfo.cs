// DdraigBench — 元数据对象描述

namespace DdraigBench.Core.Metadata;

/// <param name="Name">对象名。</param>
/// <param name="Schema">所属模式（SQLite 为 null）。</param>
/// <param name="Comment">注释（方言不提供时为 null）。</param>
/// <param name="Definition">
/// 完整可执行定义（M5-1 追加）。表 / 视图 / 触发器在方言能给出原始语句时填入
/// （SQLite 的 sqlite_master.sql 一律给得出）；拿不到定义的对象此处为 null，
/// 调用方应回落到按元数据重建的 <c>DdlGenerator</c>。
/// </param>
public sealed record DbObjectInfo(
    string Name,
    string? Schema = null,
    string? Comment = null,
    string? Definition = null);
