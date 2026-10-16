// DdraigBench — 服务端分页语句构造（把用户查询包成 LIMIT/OFFSET）

using DdraigBench.Core.Dialects;

namespace DdraigBench.Core.Querying;

/// <summary>
/// 用方言把用户语句包成"服务端分页"查询：<c>SELECT * FROM (&lt;原语句&gt;) AS 别名 LIMIT … OFFSET …</c>。
/// </summary>
/// <remarks>
/// 为什么必须包一层而不是直接给原语句加 <c>LIMIT</c>：用户 SQL 可能已经带 <c>LIMIT</c>、
/// 以分号结尾、或含尾随的锁提示子句，直接拼接会变成语法错误。包成派生表后，
/// 原语句的 <c>ORDER BY</c>/<c>WHERE</c> 都保持原语义，分页只作用于结果集。
/// 非查询语句（INSERT/UPDATE/DELETE/DDL…）没有"翻页"语义，返回 <c>null</c> 让调用方原样执行。
/// </remarks>
public static class PagedSql
{
    /// <summary>派生表别名（经方言引用标识符，避免与用户列名/关键字冲突）。</summary>
    public const string PageAlias = "ddb_page";

    /// <summary>
    /// 构造分页查询；<paramref name="sql"/> 不是查询语句、被方言拒绝改写、是多语句脚本、或参数非法时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 判定一律走 <see cref="Scan"/>（跳过字符串/引用标识符/注释）：注释里可能出现 <c>LIMIT</c> 字样，
    /// 行注释还会吃到行尾（M1）；顶层分号意味着多语句脚本，包成派生表必然是语法错误（M2）。
    /// SQL Server 走独立分支：只在顶层 ORDER BY 存在且无 TOP / OPTION / FOR 冲突子句时，
    /// 直接追加 OFFSET/FETCH；不包派生表，也不制造任意排序。
    /// </remarks>
    public static string? Build(string? sql, ISqlDialect dialect, int pageSize, long offset) =>
        Plan(sql, dialect, pageSize, offset).Sql;

    /// <summary>
    /// 构造分页查询，并把表头排序一并下推到服务端（跨页排序）。
    /// </summary>
    /// <param name="sort">
    /// 表头排序目标；为 <c>null</c> 时行为与不排序完全一致。
    /// </param>
    /// <param name="sortApplied">
    /// 排序是否真的写进了 SQL。为 <c>false</c> 时调用方必须退回"只排当前页已读回的行"，
    /// 否则用户会看到"点了排序但顺序没变"（SQL Server 已有顶层 ORDER BY、列名不可排序等情况）。
    /// </param>
    /// <remarks>
    /// 支持序号的方言（SQLite/MySQL/PG）把原语句包成派生表后在**外层**加 <c>ORDER BY 序号</c>：
    /// 排序作用于整条语句的结果集，再叠加 LIMIT/OFFSET，因此跨页有效。
    /// SQL Server 不能包派生表（内层裸 ORDER BY 非法），只在原语句**没有**顶层 ORDER BY 时
    /// 直接补一段 <c>ORDER BY 列名</c> + OFFSET/FETCH；已有 ORDER BY 时保持可分页但不排序（fail-closed）。
    /// </remarks>
    public static string? Build(
        string? sql,
        ISqlDialect dialect,
        int pageSize,
        long offset,
        ResultSort? sort,
        out bool sortApplied)
    {
        var plan = Plan(sql, dialect, pageSize, offset, sort);
        sortApplied = plan.SortApplied;
        return plan.Sql;
    }

    /// <summary>
    /// 构造完整的服务端分页计划：分页语句 + 排序是否真的下推 + 未下推的原因
    /// （见 <see cref="PagedSqlPlan"/>）。
    /// </summary>
    /// <remarks>
    /// 与两个 <c>Build</c> 重载的唯一区别是**多说一句为什么**：UI 之前只能拿到
    /// <c>sortApplied == false</c>，于是所有回退都显示同一句"该语句无法跨页排序"，
    /// 用户分不清是事务、多语句、还是 SQL Server 不肯改写 ORDER BY。
    /// </remarks>
    public static PagedSqlPlan Plan(
        string? sql,
        ISqlDialect dialect,
        int pageSize,
        long offset,
        ResultSort? sort = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        // 没请求排序时"原因"没有意义，恒为 None；请求了才需要解释为什么没下推。
        PagedSqlPlan Refused(SortFallbackReason reason) =>
            new(null, false, sort is null ? SortFallbackReason.None : reason);

        if (pageSize <= 0 || offset < 0 || !dialect.SupportsServerPaging)
        {
            return Refused(SortFallbackReason.NoServerPaging);
        }

        var statement = (sql ?? string.Empty).Trim();
        if (statement.Length == 0)
        {
            return Refused(SortFallbackReason.StatementNotRewritable);
        }

        // 需要顶层 ORDER BY 的方言（SQL Server）不能包派生表：既加不了自己的 OFFSET/FETCH，
        // 也改不动用户已有的 TOP / OPTION / FOR / 分页子句。
        var requiresTopLevelOrderBy = dialect.RequiresTopLevelOrderByForPaging;
        var scan = Scan(statement, requiresTopLevelOrderBy);
        if (!scan.StartsWithQuery || scan.MultipleStatements)
        {
            return Refused(SortFallbackReason.StatementNotRewritable);
        }

        string? injectedOrderBy;
        if (requiresTopLevelOrderBy)
        {
            // 用户自带顶层分页（OFFSET/FETCH）或 TOP/OPTION/FOR：后缀无处安放，整条放弃分页。
            if (scan.HasTopLevelPaging || scan.HasTsqlPagingConflict)
            {
                return Refused(SortFallbackReason.StatementNotRewritable);
            }

            if (scan.HasTopLevelOrderBy)
            {
                // 改写用户已有的 ORDER BY 会改变语义：分页照做，但**不**声称排序生效。
                return Finish(
                    statement, scan, dialect, injectedOrderBy: null, pageSize, offset,
                    SortFallbackReason.SqlServerKeepsUserOrder);
            }

            injectedOrderBy = sort is ResultSort tsqlSort ? BuildOrderBy(dialect, tsqlSort) : null;
            if (injectedOrderBy is null)
            {
                // 没有 ORDER BY 又造不出一段 → OFFSET/FETCH 不合法，整条放弃分页。
                return Refused(SortFallbackReason.UnsortableColumnName);
            }
        }
        else
        {
            // 包装方言把用户语句（连同它自己的 LIMIT/OFFSET）关进派生表：外层再加排序与分页，
            // 于是"用户写了 LIMIT 100000"的查询同样能跨页排序（派生表里的 LIMIT 是合法的）。
            injectedOrderBy = sort is ResultSort wrapperSort ? BuildOrderBy(dialect, wrapperSort) : null;
            if (sort is not null && injectedOrderBy is null)
            {
                return Finish(
                    statement, scan, dialect, injectedOrderBy: null, pageSize, offset,
                    SortFallbackReason.UnsortableColumnName);
            }
        }

        return Finish(statement, scan, dialect, injectedOrderBy, pageSize, offset, SortFallbackReason.None);
    }

    /// <summary>收尾：去分号、补行注释换行、按方言拼分页/排序后缀。</summary>
    private static PagedSqlPlan Finish(
        string statement,
        ScanResult scan,
        ISqlDialect dialect,
        string? injectedOrderBy,
        int pageSize,
        long offset,
        SortFallbackReason fallback)
    {
        // 结尾的顶层分号必须先去掉；保留它会让派生表或 OFFSET/FETCH 后缀变成语法错误。
        var body = scan.LastTopLevelSemicolon >= 0
            ? statement[..scan.LastTopLevelSemicolon].TrimEnd()
            : statement;

        while (body.EndsWith(';'))
        {
            body = body[..^1].TrimEnd();
        }

        // 行尾 -- 注释会吃掉后缀，所以一定补换行。
        var terminator = scan.EndsInLineComment ? "\n" : string.Empty;
        var paging = dialect.BuildPaging(pageSize, offset);
        var sortApplied = injectedOrderBy is not null;

        // SQL Server 要求 OFFSET/FETCH 直接跟在带 ORDER BY 的查询后；不能套派生表，
        // 因为 T-SQL 禁止派生表中的裸 ORDER BY。requires-order 方言因此走直加后缀分支。
        if (dialect.RequiresTopLevelOrderByForPaging)
        {
            var order = injectedOrderBy is null ? string.Empty : $"{injectedOrderBy} ";
            return new PagedSqlPlan($"{body}{terminator} {order}{paging}", sortApplied, fallback);
        }

        var outerOrder = injectedOrderBy is null ? string.Empty : $" {injectedOrderBy}";
        return new PagedSqlPlan(
            $"SELECT * FROM ({body}{terminator}) AS {dialect.QuoteIdentifier(PageAlias)}{outerOrder} {paging}",
            sortApplied,
            fallback);
    }

    /// <summary>
    /// 排序子句。支持序号的方言用 <c>ORDER BY 序号</c>（对重名列、表达式列都无歧义）；
    /// 其余方言只能按列名，列名不唯一或不像一个标识符时返回 <c>null</c>（调用方退回客户端排序）。
    /// </summary>
    private static string? BuildOrderBy(ISqlDialect dialect, ResultSort sort)
    {
        if (sort.ColumnIndex < 0)
        {
            return null;
        }

        var direction = sort.Descending ? "DESC" : "ASC";

        if (dialect.SupportsOrdinalOrderBy)
        {
            return $"ORDER BY {sort.ColumnIndex + 1} {direction}";
        }

        // T-SQL 只认列名：重名列会让引擎报 ambiguous，表达式列（如 COUNT(*)）加方括号后直接语法错误。
        return sort.ColumnNameIsUnique && IsSimpleColumnName(sort.ColumnName)
            ? $"ORDER BY {dialect.QuoteIdentifier(sort.ColumnName)} {direction}"
            : null;
    }

    /// <summary>
    /// 列名是否"看起来就是一个标识符"：只允许字母/数字/下划线/空格，且不以数字开头。
    /// 目的是挡住 <c>COUNT(*)</c>、<c>a + b</c> 这类被当成列名的表达式文本——加引号后会变成未知标识符。
    /// </summary>
    private static bool IsSimpleColumnName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var first = true;
        foreach (var c in name)
        {
            if (first)
            {
                first = false;
                if (!char.IsLetter(c) && c != '_')
                {
                    return false;
                }

                continue;
            }

            if (!char.IsLetterOrDigit(c) && c is not '_' and not ' ')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 单遍扫描语句：跳过字符串/引用标识符/注释，得出首词是否为查询、顶层分页与 ORDER BY、
    /// 是否含 T-SQL 冲突子句、是否为多语句脚本、最后一个顶层分号的位置，以及行注释收尾状态。
    /// </summary>
    /// <remarks>
    /// 未处理 MySQL 的 <c>#</c> 注释：SQLite/PG 里 <c>#</c> 是普通字符（PG 还当运算符），
    /// 按注释处理反而更危险；代价只是 MySQL 用 <c>#</c> 写注释时可能少翻一页。
    /// </remarks>
    private static ScanResult Scan(string statement, bool treatBracketIdentifiers)
    {
        var depth = 0;
        var index = 0;
        var firstWordRead = false;
        var startsWithQuery = false;
        var hasTopLevelPaging = false;
        var hasTopLevelOrderBy = false;
        var hasTsqlPagingConflict = false;
        var previousTopLevelWord = string.Empty;
        var lastSemicolon = -1;
        var multipleStatements = false;
        var endsInLineComment = false;

        void NoteToken()
        {
            // 顶层分号之后又出现实义字符 → 多语句脚本
            if (lastSemicolon >= 0)
            {
                multipleStatements = true;
            }
        }

        while (index < statement.Length)
        {
            var c = statement[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c == '-' && index + 1 < statement.Length && statement[index + 1] == '-')
            {
                var end = statement.IndexOf('\n', index);
                if (end < 0)
                {
                    endsInLineComment = true;
                    break;
                }

                index = end + 1;
                continue;
            }

            if (c == '/' && index + 1 < statement.Length && statement[index + 1] == '*')
            {
                var end = statement.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? statement.Length : end + 2;
                continue;
            }

            if (c is '\'' or '"' or '`' || (treatBracketIdentifiers && c == '['))
            {
                firstWordRead = true;
                previousTopLevelWord = string.Empty;
                index = c == '[' ? SkipBracketIdentifier(statement, index) : SkipQuoted(statement, index);
                NoteToken();
                continue;
            }

            if (IsWordStart(c))
            {
                var start = index++;
                while (index < statement.Length && IsWordPart(statement[index]))
                {
                    index++;
                }

                var word = statement[start..index];
                if (!firstWordRead)
                {
                    firstWordRead = true;
                    startsWithQuery = word.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("WITH", StringComparison.OrdinalIgnoreCase);
                }

                if (depth == 0)
                {
                    if (treatBracketIdentifiers
                        && (word.Equals("TOP", StringComparison.OrdinalIgnoreCase)
                            || word.Equals("FOR", StringComparison.OrdinalIgnoreCase)
                            || word.Equals("OPTION", StringComparison.OrdinalIgnoreCase)))
                    {
                        hasTsqlPagingConflict = true;
                    }

                    if (word.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
                        || word.Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTopLevelPaging = true;
                    }

                    if (word.Equals("BY", StringComparison.OrdinalIgnoreCase)
                        && previousTopLevelWord.Equals("ORDER", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTopLevelOrderBy = true;
                    }

                    previousTopLevelWord = word;
                }
                else
                {
                    previousTopLevelWord = string.Empty;
                }

                NoteToken();
                continue;
            }

            if (c == '(')
            {
                depth++;
                previousTopLevelWord = string.Empty;
            }
            else if (c == ')')
            {
                depth--;
                previousTopLevelWord = string.Empty;
            }
            else if (c == ';' && depth == 0)
            {
                lastSemicolon = index;
                previousTopLevelWord = string.Empty;
                index++;
                continue;
            }
            else if (!char.IsWhiteSpace(c))
            {
                previousTopLevelWord = string.Empty;
            }

            index++;
            NoteToken();
        }

        return new ScanResult(
            startsWithQuery, hasTopLevelPaging, hasTopLevelOrderBy, hasTsqlPagingConflict,
            multipleStatements, lastSemicolon, endsInLineComment);
    }

    /// <summary>跳过一个字符串/引用标识符（<c>''</c>、<c>""</c>、<c>``</c> 是转义写法），返回其后的下标。</summary>
    private static int SkipQuoted(string statement, int index)
    {
        var quote = statement[index];
        index++;

        while (index < statement.Length)
        {
            if (statement[index] != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < statement.Length && statement[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return statement.Length;
    }

    /// <summary>
    /// 扫描结果。<paramref name="LastTopLevelSemicolon"/> 为 <c>-1</c> 表示没有顶层分号；
    /// <paramref name="EndsInLineComment"/> 为真表示语句以 <c>-- …</c> 行注释收尾。
    /// </summary>
    private readonly record struct ScanResult(
        bool StartsWithQuery,
        bool HasTopLevelPaging,
        bool HasTopLevelOrderBy,
        bool HasTsqlPagingConflict,
        bool MultipleStatements,
        int LastTopLevelSemicolon,
        bool EndsInLineComment);

    private static int SkipBracketIdentifier(string statement, int index)
    {
        index++; // '['
        while (index < statement.Length)
        {
            if (statement[index] != ']')
            {
                index++;
                continue;
            }

            if (index + 1 < statement.Length && statement[index + 1] == ']')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return statement.Length;
    }

    private static bool IsWordStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsWordPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$';
}