// DdraigBench — QueryTabViewModel 单测（执行/取消/事务，纯逻辑）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Sessions;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

public sealed class QueryTabViewModelTests
{
    private static (DbSession Session, QueryTabViewModel Tab) Create(TempSqliteDatabase db, FreeSqlFactory factory)
    {
        var session = new DbSession(
            new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        return (session, new QueryTabViewModel("q", () => session, new StubFilePicker(), new StubExportDialog()));
    }

    [Fact]
    public async Task Execute_select_fills_rows_and_status()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Sql = "SELECT 1 AS n";
        await tab.ExecuteAsync();

        Assert.Equal(new[] { "n" }, tab.Columns);
        Assert.Single(tab.Rows);
        Assert.False(tab.IsExecuting);
        Assert.False(tab.CanCancel);
    }

    [Fact]
    public async Task Cancel_requested_before_execute_reports_cancelled()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Cancel();
        await tab.ExecuteAsync();

        Assert.Equal("已取消", tab.Status);
        Assert.Empty(tab.Rows);
        Assert.False(tab.IsExecuting);
    }

    [Fact]
    public async Task Cancel_is_safe_when_idle_and_updates_CanCancel()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Cancel();

        Assert.False(tab.CanCancel);
        Assert.Equal("就绪", tab.Status);
    }

    [Fact]
    public async Task Execute_without_session_reports_not_connected()
    {
        var tab = new QueryTabViewModel("q", () => null, new StubFilePicker(), new StubExportDialog());

        await tab.ExecuteAsync();

        Assert.Contains("未连接", tab.Status);
        Assert.False(tab.IsExecuting);
    }

    [Fact]
    public async Task Without_toggle_no_transaction_is_opened()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Sql = "SELECT 1";
        await tab.ExecuteAsync();

        Assert.False(tab.HasTransaction);
    }

    [Fact]
    public async Task Transaction_rollback_discards_changes()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.UseTransaction = true;
        tab.Sql = "CREATE TABLE t (x INTEGER)";
        await tab.ExecuteAsync();
        tab.Sql = "INSERT INTO t VALUES (1)";
        await tab.ExecuteAsync();

        Assert.True(tab.HasTransaction);

        await tab.RollbackAsync();

        Assert.False(tab.HasTransaction);

        tab.UseTransaction = false;
        tab.Sql = "SELECT x FROM t";
        await tab.ExecuteAsync();

        Assert.Contains("no such table", tab.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transaction_commit_persists_changes()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.UseTransaction = true;
        tab.Sql = "CREATE TABLE t (x INTEGER)";
        await tab.ExecuteAsync();
        tab.Sql = "INSERT INTO t VALUES (7)";
        await tab.ExecuteAsync();

        await tab.CommitAsync();
        Assert.False(tab.HasTransaction);

        tab.UseTransaction = false;
        tab.Sql = "SELECT x FROM t";
        await tab.ExecuteAsync();

        var row = Assert.Single(tab.Rows);
        Assert.Equal(7, Convert.ToInt32(row[0]));
    }

    [Fact]
    public async Task Dispose_rolls_back_open_transaction()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.UseTransaction = true;
        tab.Sql = "CREATE TABLE t (x INTEGER)";
        await tab.ExecuteAsync();
        tab.Sql = "INSERT INTO t VALUES (1)";
        await tab.ExecuteAsync();

        await tab.DisposeAsync();
        Assert.False(tab.HasTransaction);

        var verify = new QueryTabViewModel("v", () => session, new StubFilePicker(), new StubExportDialog()) { Sql = "SELECT x FROM t" };
        await verify.ExecuteAsync();

        Assert.Contains("no such table", verify.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaxRows_caps_result_and_status_mentions_truncation()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (id) VALUES (1), (2), (3)");

        tab.MaxRows = 2;
        tab.Sql = "SELECT id FROM t ORDER BY id";
        await tab.ExecuteAsync();

        Assert.Equal(2, tab.Rows.Count);
        Assert.Contains("已截断", tab.Status);
    }
}
