// DdraigBench — SessionManager 单测（会话并发上限与生命周期）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Sessions;
using FreeSql;

namespace DdraigBench.Test;

public sealed class SessionManagerTests
{
    private static ConnectionProfile Profile(TempSqliteDatabase db, string name) =>
        new(name, DataType.Sqlite, db.ConnectionString);

    [Fact]
    public async Task Sessions_are_capped_and_the_overflow_throws_instead_of_leaking()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var manager = new SessionManager(factory, maxOpenSessions: 2);
        var first = Profile(db, "第一个");
        var second = Profile(db, "第二个");
        var third = Profile(db, "第三个");

        await manager.OpenAsync(first);
        var active = await manager.OpenAsync(second);

        Assert.Equal(2, manager.OpenCount);
        Assert.Same(active, manager.Active);

        // 上限不是摆设：再开就是响亮失败，而不是悄悄多挂一个连接。
        var overflow = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.OpenAsync(third));
        Assert.Contains("会话并发上限", overflow.Message);
        Assert.Equal(2, manager.OpenCount);
        Assert.Same(active, manager.Active);
    }

    [Fact]
    public async Task Closing_a_session_evicts_its_fsql_from_the_factory()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var manager = new SessionManager(factory);
        var profile = Profile(db, "db");

        var session = await manager.OpenAsync(profile);
        var fsql = session.Fsql;

        Assert.True(await manager.CloseAsync(session));

        // 会话处置必须真的走到 FreeSqlFactory.Remove（= 释放连接池），
        // 所以同一 profile 再取会是一个新实例。
        Assert.NotSame(fsql, factory.GetOrCreate(profile));
        Assert.Equal(0, manager.OpenCount);
        Assert.Null(manager.Active);
    }

    [Fact]
    public async Task Closing_a_session_the_manager_never_opened_reports_false()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var manager = new SessionManager(factory);
        var foreign = new DbSession(Profile(db, "野生会话"), factory);

        Assert.False(await manager.CloseAsync(foreign));
        Assert.Equal(0, manager.OpenCount);

        await foreign.DisposeAsync();
    }

    [Fact]
    public async Task CloseAll_disposes_every_session_and_is_idempotent()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var manager = new SessionManager(factory, maxOpenSessions: 2);
        var first = Profile(db, "第一个");
        var second = Profile(db, "第二个");

        var firstSession = await manager.OpenAsync(first);
        var secondSession = await manager.OpenAsync(second);
        var firstFsql = firstSession.Fsql;
        var secondFsql = secondSession.Fsql;

        await manager.CloseAllAsync();
        await manager.CloseAllAsync(); // 幂等：再关一次不能炸，也不能重复处置

        Assert.Equal(0, manager.OpenCount);
        Assert.Null(manager.Active);
        Assert.NotSame(firstFsql, factory.GetOrCreate(first));
        Assert.NotSame(secondFsql, factory.GetOrCreate(second));
    }

    [Fact]
    public async Task Sessions_snapshot_follows_open_order()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var manager = new SessionManager(factory, maxOpenSessions: 2);

        var first = await manager.OpenAsync(Profile(db, "第一个"));
        var second = await manager.OpenAsync(Profile(db, "第二个"));

        Assert.Equal([first, second], manager.Sessions);
        Assert.Same(second, manager.Active);
    }

    [Fact]
    public async Task DisposeAsync_closes_sessions_and_refuses_further_opens()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var manager = new SessionManager(factory);
        await manager.OpenAsync(Profile(db, "db"));

        await manager.DisposeAsync();
        await manager.DisposeAsync(); // 幂等

        Assert.Equal(0, manager.OpenCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.OpenAsync(Profile(db, "db")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Cap_below_one_is_rejected(int maxOpenSessions)
    {
        using var factory = new FreeSqlFactory();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionManager(factory, maxOpenSessions));
    }
}
