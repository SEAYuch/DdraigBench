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
    bool ColumnNameIsUnique = true)
{
    /// <summary>
    /// 按结果列清单判定"这一列的名字是否唯一"，再构造排序目标。
    /// </summary>
    /// <remarks>
    /// 唯一性判定必须**忽略大小写**：T-SQL 默认排序规则（以及 MySQL 的常见配置）下列名不区分大小写，
    /// <c>SELECT id, ID</c> 这类结果集用 <c>ORDER BY [id]</c> 会被引擎判 ambiguous。
    /// 早前用 <c>c == name</c>（区分大小写）会把这种列误判成唯一，从而生成一条报错的 SQL。
    /// </remarks>
    public static ResultSort For(IReadOnlyList<string> columns, int index, bool descending)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var name = columns[index];
        var unique = columns.Count(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) == 1;
        return new ResultSort(index, name, descending, unique);
    }
}
