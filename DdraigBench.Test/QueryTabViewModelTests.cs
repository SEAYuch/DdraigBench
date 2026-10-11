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
    public async Task Cancel_while_idle_is_inert()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Cancel();
        await tab.ExecuteAsync();

        Assert.Equal(new[] { "1" }, tab.Columns);
        Assert.Single(tab.Rows);
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
    public void CanRun_is_false_for_a_data_browser_tab()
    {
        var browser = new QueryTabViewModel(
            "data", () => null, new StubFilePicker(), new StubExportDialog(),
            browserObjectKey: "main.t", browserObjectName: "t");
        var query = new QueryTabViewModel("q", () => null, new StubFilePicker(), new StubExportDialog());

        Assert.False(browser.CanRun);
        Assert.True(query.CanRun);
    }

    [Fact]
    public async Task Second_execute_while_running_is_ignored()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        tab.Sql = "SELECT length(hex(zeroblob(10000000))) AS n";

        // 第一条语句刻意慢（几十毫秒）：留出稳定的"执行中"窗口，让第二次调用必落在窗口内。
        var first = tab.ExecuteAsync();

        // 立刻发起第二次执行：必须被重入守卫挡下。若挡不住，第二次会覆盖 _cts
        // （先结束者的 finally 会 dispose 掉第一次正在用的 CTS，甚至 NRE），
        // 并用第二条语句的结果覆盖网格。
        tab.Sql = "SELECT 1 AS marker UNION ALL SELECT 2";
        var second = tab.ExecuteAsync();

        Assert.True(tab.IsExecuting);
        Assert.False(tab.CanRun);
        Assert.True(tab.CanCancel);

        await Task.WhenAll(first, second);

        Assert.False(tab.IsExecuting);
        Assert.True(tab.CanRun);
        Assert.Equal(new[] { "n" }, tab.Columns);
        Assert.Single(tab.Rows);
        Assert.Contains("1 行", tab.Status);
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
    public async Task PageSize_slices_result_server_side_and_reports_last_page()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (id) VALUES (1), (2), (3), (4), (5)");

        tab.PageSize = 2;
        tab.Sql = "SELECT id FROM t ORDER BY id";
        await tab.ExecuteAsync();

        // 第 1 页：2 行 + 探测到还有下一页
        Assert.Equal(1L, tab.Rows[0][0]);
        Assert.Equal(2, tab.Rows.Count);
        Assert.Equal(1, tab.CurrentPage);
        Assert.True(tab.HasNextPage);
        Assert.True(tab.CanGoToNextPage);
        Assert.False(tab.HasPreviousPage);
        Assert.Contains("有下一页", tab.Status);
        Assert.DoesNotContain("已截断", tab.Status);

        await tab.NextPageAsync();

        Assert.Equal(2, tab.CurrentPage);
        Assert.Equal(3L, tab.Rows[0][0]);
        Assert.True(tab.HasNextPage);

        await tab.NextPageAsync();

        // 第 3 页：最后 1 行，无下一页
        Assert.Equal(3, tab.CurrentPage);
        Assert.Single(tab.Rows);
        Assert.False(tab.HasNextPage);
        Assert.False(tab.CanGoToNextPage);
        Assert.True(tab.HasPreviousPage);
        Assert.Contains("已是最后一页", tab.Status);

        await tab.PreviousPageAsync();
        Assert.Equal(2, tab.CurrentPage);
        Assert.Equal(2, tab.Rows.Count);
    }

    [Fact]
    public async Task Executing_again_resets_to_first_page()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (id) VALUES (1), (2), (3)");

        tab.PageSize = 1;
        tab.Sql = "SELECT id FROM t ORDER BY id";
        await tab.ExecuteAsync();
        await tab.NextPageAsync();
        Assert.Equal(2, tab.CurrentPage);

        await tab.ExecuteAsync();

        Assert.Equal(1, tab.CurrentPage);
        Assert.Equal(1L, tab.Rows[0][0]);
    }

    [Fact]
    public async Task User_supplied_limit_is_left_untouched()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (id) VALUES (1), (2), (3)");

        tab.PageSize = 100;
        tab.Sql = "SELECT id FROM t ORDER BY id LIMIT 2";
        await tab.ExecuteAsync();

        // 用户自己限了 2 行：不再叠加我们的分页，也没有"已截断"（结果本身就是 2 行）。
        Assert.Equal(2, tab.Rows.Count);
        Assert.False(tab.HasNextPage);
        Assert.DoesNotContain("第 ", tab.Status);
        Assert.DoesNotContain("已截断", tab.Status);
    }

    [Fact]
    public async Task Non_query_statement_is_executed_as_is()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (id INTEGER)");

        tab.Sql = "INSERT INTO t (id) VALUES (7), (8)";
        await tab.ExecuteAsync();

        Assert.Empty(tab.Rows);
        Assert.Contains("影响 2 行", tab.Status);
        Assert.DoesNotContain("第 ", tab.Status);
    }

    [Fact]
    public async Task Sort_command_orders_the_current_page_and_toggles_direction()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (n INTEGER, label TEXT)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (n, label) VALUES (9, 'nine'), (10, 'ten'), (2, 'two')");

        tab.Sql = "SELECT n, label FROM t";
        await tab.ExecuteAsync();

        Assert.Equal(3, tab.Rows.Count);

        tab.SortCommand.Execute(0);

        // 数值比较：2 < 9 < 10（按文本比会得到 10, 2, 9）
        Assert.Equal(new[] { 2L, 9L, 10L }, tab.Rows.Select(row => Convert.ToInt64(row[0])).ToArray());
        Assert.Contains("升序", tab.Status);

        tab.SortCommand.Execute(0);

        Assert.Equal(new[] { 10L, 9L, 2L }, tab.Rows.Select(row => Convert.ToInt64(row[0])).ToArray());
        Assert.Contains("降序", tab.Status);

        // 越界列号不改变结果（列号来自表头按钮，做个兜底）
        tab.SortCommand.Execute(9);

        Assert.Equal(new[] { 10L, 9L, 2L }, tab.Rows.Select(row => Convert.ToInt64(row[0])).ToArray());
    }

    [Fact]
    public async Task Sorting_is_cleared_when_a_new_result_arrives()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var (session, tab) = Create(db, factory);
        await using var _ = session;

        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t (n INTEGER)");
        session.Fsql.Ado.ExecuteNonQuery("INSERT INTO t (n) VALUES (9), (10), (2)");

        tab.Sql = "SELECT n FROM t";
        await tab.ExecuteAsync();
        tab.SortCommand.Execute(0);
        Assert.Equal(new[] { 2L, 9L, 10L }, tab.Rows.Select(row => Convert.ToInt64(row[0])).ToArray());

        await tab.ExecuteAsync();

        // 排序只对到达时的那一页有效：重新执行后回到服务端顺序，状态栏也不再声称在排序
        Assert.Equal(new[] { 9L, 10L, 2L }, tab.Rows.Select(row => Convert.ToInt64(row[0])).ToArray());
        Assert.DoesNotContain("排序", tab.Status);
    }
}
