// DdraigBench — 表头排序目标（跨页/服务端排序用）

namespace DdraigBench.Core.Querying;

/// <summary>
/// 一次表头点击的排序目标：列序号（0 基，供支持 <c>ORDER BY 序号</c> 的方言使用）、
/// 列名与方向。
/// </summary>
/// <remarks>
/// 为什么序号也要传进来：用户 SQL 可能出现重名列或无名表达式列，按名排序会歧义或直接报错；
/// SQLite/MySQL/PG 支持 <c>ORDER BY 1</c>（按输出列位置），SQL Server 不支持，只能回退列名。
/// <paramref name="ColumnNameIsUnique"/> 就是为这条回退路径准备的：列名重复时按名排序会被引擎
/// 判为 ambiguous，宁可不排序也不要把用户查询改成一条报错的语句。
/// </remarks>
public readonly record struct ResultSort(
    int ColumnIndex,
    string ColumnName,
    bool Descending,
    bool ColumnNameIsUnique = true);
