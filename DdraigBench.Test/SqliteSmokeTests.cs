// DdraigBench — M1 SQLite 黄金通道冒烟测试

using FreeSql;

namespace DdraigBench.Test;

public sealed class SqliteSmokeTests
{
    [Fact]
    public void Sqlite_select_1_executes_against_temp_file_database()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();

        var scalar = fsql.Ado.ExecuteScalar("SELECT 1");

        Assert.Equal(1, Convert.ToInt32(scalar));
    }

    [Fact]
    public void MasterPool_get_wraps_connection_and_dispose_returns_it_to_pool()
    {
        using var db = new TempSqliteDatabase();
        using var fsql = db.CreateFsql();

        var pool = fsql.Ado.MasterPool;
        Assert.NotNull(pool);

        var first = pool.Get();
        var connection = first.Value;
        Assert.NotNull(connection);
        Assert.Contains("SQLite", connection.GetType().FullName!, StringComparison.OrdinalIgnoreCase);
        Assert.Same(pool, first.Pool);

        first.Dispose();

        var second = pool.Get();
        Assert.Same(connection, second.Value);
        second.Dispose();
    }
}
