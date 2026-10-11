// DdraigBench — 目录查询执行器（SQL → 元数据模型，按列名映射）

using System.Globalization;
using DdraigBench.Core.Querying;
using DdraigBench.Core.Sessions;

namespace DdraigBench.Core.Metadata;

/// <summary>
/// 执行 <c>Dialects/CatalogQueries</c> 造出来的目录 SQL 并把结果映射成元数据模型。
/// 复用 <see cref="QueryRunner"/>（MasterPool 原始连接 + 流式读取），即 §4「执行 SQL 的唯一通道」。
/// </summary>
internal static class CatalogQueryExecutor
{
    /// <summary>目录对象清单的上限。真实库里触发器/例程极少超过此数；超出即报错，不静默截断。</summary>
    internal const int MaxRows = 5000;

    internal static async Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(
        DbSession session, string sql, CancellationToken ct)
    {
        var result = await RunAsync(session, sql, ct);

        var nameIndex = IndexOf(result.Columns, "name");
        if (nameIndex < 0)
        {
            throw new InvalidOperationException($"目录查询未返回 name 列：{sql}");
        }

        var commentIndex = IndexOf(result.Columns, "comment");
        var definitionIndex = IndexOf(result.Columns, "definition");

        var objects = new List<DbObjectInfo>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            var name = AsString(row[nameIndex]);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            objects.Add(new DbObjectInfo(
                name,
                Schema: null,
                Comment: commentIndex >= 0 ? AsString(row[commentIndex]) : null,
                Definition: definitionIndex >= 0 ? AsString(row[definitionIndex]) : null));
        }

        return objects;
    }

    /// <summary>取单值（单列单行的第一格）；无行或值为空即返回 <c>null</c>。</summary>
    internal static async Task<string?> ReadScalarAsync(DbSession session, string sql, CancellationToken ct)
    {
        var result = await RunAsync(session, sql, ct);

        if (result.Rows.Count == 0 || result.Rows[0].Length == 0)
        {
            return null;
        }

        return AsString(result.Rows[0][0]);
    }

    private static async Task<QueryResult> RunAsync(DbSession session, string sql, CancellationToken ct)
    {
        var result = await new QueryRunner(session.Fsql).ExecuteAsync(sql, MaxRows, ct);

        // QueryRunner 把 SQL 异常收敛进 Error；元数据场景必须暴露出来，否则树会「静默空」
        if (result.Error is not null)
        {
            throw new InvalidOperationException($"目录查询失败：{result.Error}（SQL：{sql}）");
        }

        // 截断必须报出来：清单少一截而用户以为"库里就这些对象"，是最难查的一类错觉。
        if (result.Truncated)
        {
            throw new InvalidOperationException(
                $"目录对象数超过 {MaxRows} 上限，清单可能不完整（SQL：{sql}）");
        }

        return result;
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? AsString(object? value) =>
        value is null or DBNull
            ? null
            : Convert.ToString(value, CultureInfo.InvariantCulture);
}
