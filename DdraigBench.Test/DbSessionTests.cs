// DdraigBench — DbSession / ConnectionLease 单测

using System.Data.Common;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Sessions;
using FreeSql;

namespace DdraigBench.Test;

public sealed class DbSessionTests
{
    [Fact]
    public async Task Lease_returns_connection_and_recycles_it_after_dispose()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = new DbSession(
            new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);

        DbConnection first;
        await using (var lease = await session.LeaseRawAsync(TestContext.Current.CancellationToken))
        {
            first = lease.Connection;
            Assert.Contains("SQLite", first.GetType().FullName!, StringComparison.OrdinalIgnoreCase);
        }

        await using var second = await session.LeaseRawAsync(TestContext.Current.CancellationToken);
        Assert.Same(first, second.Connection);
    }

    [Fact]
    public async Task Dispose_evicts_fsql_from_factory()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString);
        var session = new DbSession(profile, factory);
        var fsql = session.Fsql;

        await session.DisposeAsync();

        Assert.NotSame(fsql, factory.GetOrCreate(profile));
    }
}
