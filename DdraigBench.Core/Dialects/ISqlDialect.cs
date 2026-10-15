// DdraigBench — 方言钩子（标识符引用 / 分页 / 字面量渲染）

namespace DdraigBench.Core.Dialects;

public interface ISqlDialect
{
    /// <summary>引用标识符（表名 / 列名）。</summary>
    string QuoteIdentifier(string name);

    /// <summary>该方言当前是否允许自动服务端分页（可能还受 SQL 形状条件约束）。</summary>
    bool SupportsServerPaging { get; }

    /// <summary>该方言的 OFFSET/FETCH 语法是否要求原查询有顶层 ORDER BY。</summary>
    bool RequiresTopLevelOrderByForPaging { get; }

    /// <summary>
    /// 是否支持 <c>ORDER BY 序号</c>（按输出列位置排序）。SQLite/MySQL/PG 支持；SQL Server 不支持，
    /// 只能按列名排序（见 <see cref="Querying.ResultSort"/>）。
    /// </summary>
    bool SupportsOrdinalOrderBy { get; }

    /// <summary>生成分页子句。</summary>
    string BuildPaging(int limit, long offset);

    /// <summary>把 CLR 值渲染成 SQL 字面量（导出 INSERT 用）。</summary>
    string FormatLiteral(object? value);

    /// <summary>自增列的子句（不含标识符与类型）。</summary>
    string IdentityClause { get; }

    /// <summary>内联单列自增主键的整段定义；子句顺序各方言不同（SQLite 的 AUTOINCREMENT 必须跟在 PRIMARY KEY 之后，MySQL 相反）。</summary>
    string BuildIdentityColumn(string quotedName, string type, bool nullable);
}
