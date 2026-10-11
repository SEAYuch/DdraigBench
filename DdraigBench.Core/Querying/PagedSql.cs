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
    /// 构造分页查询；<paramref name="sql"/> 不是查询语句、已自带顶层分页、是多语句脚本、或参数非法时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 判定一律走 <see cref="Scan"/>（跳过字符串/引用标识符/注释）：注释里可能出现 <c>LIMIT</c> 字样，
    /// 行注释还会吃到行尾（M1）；顶层分号意味着多语句脚本，包成派生表必然是语法错误（M2）。
    /// </remarks>
    public static string? Build(string? sql, ISqlDialect dialect, int pageSize, long offset)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        if (pageSize <= 0 || offset < 0)
        {
            return null;
        }

        var statement = (sql ?? string.Empty).Trim();
        if (statement.Length == 0)
        {
            return null;
        }

        var scan = Scan(statement);
        if (!scan.StartsWithQuery || scan.HasTopLevelPaging || scan.MultipleStatements)
        {
            return null;
        }

        // 结尾的顶层分号（`SELECT …;`）必须去掉：分号留在派生表里是语法错误
        var body = scan.LastTopLevelSemicolon >= 0
            ? statement[..scan.LastTopLevelSemicolon].TrimEnd()
            : statement;

        while (body.EndsWith(';'))
        {
            body = body[..^1].TrimEnd();
        }

        // 语句以行注释结尾时要补一个换行：`-- …` 会吃到行尾，否则会把我们补的右括号一起吞掉
        var terminator = scan.EndsInLineComment ? "\n" : string.Empty;

        return $"SELECT * FROM ({body}{terminator}) AS {dialect.QuoteIdentifier(PageAlias)} {dialect.BuildPaging(pageSize, offset)}";
    }

    /// <summary>
    /// 单遍扫描语句：跳过字符串/引用标识符/注释，得出首词是否为查询、顶层是否已有分页、
    /// 是否为多语句脚本、最后一个顶层分号的位置，以及语句是否以行注释结尾。
    /// </summary>
    /// <remarks>
    /// 未处理 MySQL 的 <c>#</c> 注释：SQLite/PG 里 <c>#</c> 是普通字符（PG 还当运算符），
    /// 按注释处理反而更危险；代价只是 MySQL 用 <c>#</c> 写注释时可能少翻一页。
    /// </remarks>
    private static ScanResult Scan(string statement)
    {
        var depth = 0;
        var index = 0;
        var firstWordRead = false;
        var startsWithQuery = false;
        var hasTopLevelPaging = false;
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

            if (c is '\'' or '"' or '`')
            {
                firstWordRead = true;
                index = SkipQuoted(statement, index);
                NoteToken();
                continue;
            }

            if (!firstWordRead)
            {
                firstWordRead = true;
                startsWithQuery = MatchesKeyword(statement, index, "SELECT")
                    || MatchesKeyword(statement, index, "WITH");
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == ';' && depth == 0)
            {
                lastSemicolon = index;
                index++;
                continue;
            }
            else if (depth == 0
                && IsWordBoundary(statement, index)
                && (MatchesKeyword(statement, index, "LIMIT") || MatchesKeyword(statement, index, "OFFSET")))
            {
                hasTopLevelPaging = true;
            }

            index++;
            NoteToken();
        }

        return new ScanResult(startsWithQuery, hasTopLevelPaging, multipleStatements, lastSemicolon, endsInLineComment);
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
        bool MultipleStatements,
        int LastTopLevelSemicolon,
        bool EndsInLineComment);

    private static bool IsWordBoundary(string statement, int index)
    {
        var before = index > 0 ? statement[index - 1] : ' ';
        return !char.IsLetterOrDigit(before) && before != '_';
    }

    private static bool MatchesKeyword(string statement, int index, string keyword)
    {
        if (index + keyword.Length > statement.Length
            || !statement.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var after = index + keyword.Length < statement.Length ? statement[index + keyword.Length] : ' ';
        return !char.IsLetterOrDigit(after) && after != '_';
    }
}