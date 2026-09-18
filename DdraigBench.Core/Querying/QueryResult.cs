// DdraigBench — 查询结果模型（无类型）

namespace DdraigBench.Core.Querying;

public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<object?[]> Rows,
    long AffectedRows,
    TimeSpan Elapsed,
    string? Error);
