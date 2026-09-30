// DdraigBench — 结果导出（CSV / INSERT 脚本）

using System.Globalization;
using System.Text;
using DdraigBench.Core.Dialects;
using DdraigBench.Core.Querying;

namespace DdraigBench.Core.Exporting;

/// <summary>
/// 把结果集渲染成 CSV 或 INSERT 脚本。纯逻辑、可单测；文件写入由调用方负责。
/// </summary>
public static class ResultExporter
{
    /// <summary>CSV：Excel 友好（含 BOM 由写入方决定），日期保留毫秒，二进制走 Base64。</summary>
    public static string ToCsv(QueryResult result, string delimiter = ",", string lineEnding = "\r\n")
    {
        var sb = new StringBuilder();

        if (result.Columns.Count > 0)
        {
            for (var i = 0; i < result.Columns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(delimiter);
                }

                sb.Append(EscapeField(FormatCell(result.Columns[i]), delimiter));
            }

            sb.Append(lineEnding);
        }

        foreach (var row in result.Rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(delimiter);
                }

                sb.Append(EscapeField(FormatCell(row[i]), delimiter));
            }

            sb.Append(lineEnding);
        }

        return sb.ToString();
    }

    /// <summary>INSERT 脚本：标识符引用与字面量渲染全部交给方言。<paramref name="batchSize"/> 为每条语句的行数。</summary>
    public static string ToInsert(QueryResult result, string tableName, ISqlDialect dialect, int batchSize = 1)
    {
        if (result.Columns.Count == 0 || result.Rows.Count == 0)
        {
            return string.Empty;
        }

        var step = Math.Max(1, batchSize);
        var target = dialect.QuoteIdentifier(tableName);
        var columnList = new List<string>(result.Columns.Count);
        for (var i = 0; i < result.Columns.Count; i++)
        {
            columnList.Add(dialect.QuoteIdentifier(ColumnNameOr(result.Columns[i], i)));
        }

        var columns = string.Join(", ", columnList);
        var sb = new StringBuilder();

        for (var start = 0; start < result.Rows.Count; start += step)
        {
            var take = Math.Min(step, result.Rows.Count - start);
            var tuples = new List<string>(take);

            for (var r = start; r < start + take; r++)
            {
                var row = result.Rows[r];
                var values = new List<string>(row.Length);
                for (var c = 0; c < row.Length; c++)
                {
                    values.Add(dialect.FormatLiteral(row[c]));
                }

                tuples.Add($"({string.Join(", ", values)})");
            }

            sb.Append("INSERT INTO ").Append(target)
                .Append(" (").Append(columns).Append(") VALUES ")
                .Append(string.Join(", ", tuples))
                .Append(';').Append('\n');
        }

        return sb.ToString();
    }

    private static string ColumnNameOr(string name, int index) =>
        string.IsNullOrWhiteSpace(name) ? $"column{index + 1}" : name;

    private static string FormatCell(object? value) => value switch
    {
        null or DBNull => string.Empty,
        bool b => b ? "true" : "false",
        byte[] bytes => Convert.ToBase64String(bytes),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
        string s => s,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string EscapeField(string text, string delimiter)
    {
        var needsQuote = text.Length != text.Trim().Length
            || text.Contains(delimiter, StringComparison.Ordinal)
            || text.Contains('"')
            || text.Contains('\r')
            || text.Contains('\n');

        return needsQuote ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
