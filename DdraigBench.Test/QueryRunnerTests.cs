// DdraigBench — QueryRunner 单测（SQLite 临时文件）

using DdraigBench.Core.Querying;

namespace DdraigBench.Test;

public sealed class QueryRunnerTests
{
    [Fact]
    public async Task Select_returns_columns_and_rows()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("SELECT 1 AS n, 'x' AS s", ct: TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        Assert.Equal(new[] { "n", "s" }, result.Columns);
        var row = Assert.Single(result.Rows);
        Assert.Equal(1, Convert.ToInt32(row[0]));
        Assert.Equal("x", row[1]);
    }

    [Fact]
    public async Task Non_query_returns_affected_rows()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();
        fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("INSERT INTO t (id) VALUES (1)", ct: TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        Assert.Empty(result.Columns);
        Assert.Empty(result.Rows);
        Assert.Equal(1, result.AffectedRows);
    }

    [Fact]
    public async Task Null_column_maps_to_null()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("SELECT NULL AS v", ct: TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        var row = Assert.Single(result.Rows);
        Assert.Null(row[0]);
    }

    [Fact]
    public async Task Rows_are_capped_at_max_rows()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();
        fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        fsql.Ado.ExecuteNonQuery("INSERT INTO t (id) VALUES (1), (2), (3)");
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("SELECT id FROM t ORDER BY id", maxRows: 2, ct: TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public async Task Invalid_sql_is_reported_in_error_without_throwing()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("SELECT * FROM does_not_exist", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Rows);
    }
}
