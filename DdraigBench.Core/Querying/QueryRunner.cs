// DdraigBench — SQL 执行器（MasterPool 原始连接 + 流式分批读取）

using System.Data;
using System.Diagnostics;
using FreeSql;

namespace DdraigBench.Core.Querying;

public sealed class QueryRunner
{
    public const int DefaultMaxRows = 1000;

    private readonly IFreeSql _fsql;

    public QueryRunner(IFreeSql fsql) => _fsql = fsql;

    public async Task<QueryResult> ExecuteAsync(
        string sql,
        int maxRows = DefaultMaxRows,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var columns = new List<string>();
        var rows = new List<object?[]>();
        long affected = 0;
        string? error = null;

        var pool = _fsql.Ado.MasterPool;
        if (pool is null)
        {
            return new QueryResult(columns, rows, 0, TimeSpan.Zero, "MasterPool 不可用：连接未配置");
        }

        try
        {
            using var leased = await pool.GetAsync(ct);
            var connection = leased.Value;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(ct);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            while (rows.Count < maxRows && await reader.ReadAsync(ct))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }

            if (columns.Count == 0)
            {
                affected = Math.Max(reader.RecordsAffected, 0);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        stopwatch.Stop();
        return new QueryResult(columns, rows, affected, stopwatch.Elapsed, error);
    }
}
