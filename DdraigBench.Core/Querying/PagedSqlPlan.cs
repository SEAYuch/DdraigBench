// DdraigBench — 服务端分页计划（分页 SQL + 排序是否真的下推 + 没下推的原因）

namespace DdraigBench.Core.Querying;

/// <summary>
/// 表头排序没能下推到服务端的原因。UI 用它把状态栏文案从"该语句无法跨页排序"升级成可操作的具体原因。
/// </summary>
public enum SortFallbackReason
{
    /// <summary>排序已下推（或本次调用根本没要求排序）。</summary>
    None,

    /// <summary>方言不支持服务端分页（或分页参数非法），排序只能在当前页已读回的行里做。</summary>
    NoServerPaging,

    /// <summary>语句不可改写：非查询、空语句、多语句脚本，或 SQL Server 无法追加 OFFSET/FETCH 的形状。</summary>
    StatementNotRewritable,

    /// <summary>SQL Server 语句里已有顶层 ORDER BY——改写会改变用户语义，故只分页不排序。</summary>
    SqlServerKeepsUserOrder,

    /// <summary>该列不能按名排序：列名重复（大小写不敏感）或不是简单标识符（如 <c>COUNT(*)</c>）。</summary>
    UnsortableColumnName,
}

/// <summary>
/// 一次服务端分页的完整计划。
/// </summary>
/// <param name="Sql">改写后的分页语句；为 <c>null</c> 表示这条语句不分页（调用方原样执行）。</param>
/// <param name="SortApplied">排序是否真的写进了 <paramref name="Sql"/>。</param>
/// <param name="SortFallback">
/// <paramref name="SortApplied"/> 为 <c>false</c> 且调用方请求了排序时的原因；
/// 没请求排序（<c>sort</c> 传 <c>null</c>）时恒为 <see cref="SortFallbackReason.None"/>。
/// </param>
public readonly record struct PagedSqlPlan(
    string? Sql,
    bool SortApplied,
    SortFallbackReason SortFallback);
