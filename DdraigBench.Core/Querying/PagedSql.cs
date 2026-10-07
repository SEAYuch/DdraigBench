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
    /// 构造分页查询；<paramref name="sql"/> 不是查询语句、已自带顶层分页、或参数非法时返回 <c>null</c>。
    /// </summary>
    public static string? Build(string? sql, ISqlDialect dialect, int pageSize, long offset)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        if (pageSize <= 0 || offset < 0)
        {
            return null;
        }

        var statement = Normalize(sql);
        if (statement.Length == 0 || !IsQuery(statement) || HasTopLevelPaging(statement))
        {
            return null;
        }

        return $"SELECT * FROM ({statement}) AS {dialect.QuoteIdentifier(PageAlias)} {dialect.BuildPaging(pageSize, offset)}";
    }

    /// <summary>去首尾空白与结尾分号（多语句脚本里常见的"SELECT …;"）。</summary>
    private static string Normalize(string? sql)
    {
        var text = (sql ?? string.Empty).Trim();
        while (text.EndsWith(';'))
        {
            text = text[..^1].TrimEnd();
        }

        return text;
    }

    private static bool IsQuery(string statement) =>
        statement.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        || statement.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断语句**顶层**（括号深度 0、字符串/引用之外）是否已出现 <c>LIMIT</c>/<c>OFFSET</c>。
    /// 只做顶层判断是为了不误伤子查询里的 LIMIT，也不被字面量里的 "limit" 字样带偏。
    /// </summary>
    private static bool HasTopLevelPaging(string statement)
    {
        var depth = 0;
        var inString = false;
        var inQuotedIdentifier = false;

        for (var i = 0; i < statement.Length; i++)
        {
            var c = statement[i];

            if (inString)
            {
                if (c == '\'')
                {
                    // '' 是转义的单引号，跳过两次
                    if (i + 1 < statement.Length && statement[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        inString = false;
                    }
                }

                continue;
            }

            if (inQuotedIdentifier)
            {
                if (c == '"')
                {
                    inQuotedIdentifier = false;
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                    inString = true;
                    continue;
                case '"':
                case '`':
                    inQuotedIdentifier = true;
                    continue;
                case '(':
                    depth++;
                    continue;
                case ')':
                    depth--;
                    continue;
            }

            if (depth != 0 || !IsWordBoundary(statement, i))
            {
                continue;
            }

            if (MatchesKeyword(statement, i, "LIMIT") || MatchesKeyword(statement, i, "OFFSET"))
            {
                return true;
            }
        }

        return false;
    }

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