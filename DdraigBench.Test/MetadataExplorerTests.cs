// DdraigBench — FreeSqlMetadataExplorer 单测（SQLite）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Sessions;
using FreeSql;

namespace DdraigBench.Test;

public sealed class MetadataExplorerTests
{
    [Fact]
    public async Task GetDatabases_returns_main()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        var databases = await explorer.GetDatabasesAsync(session, TestContext.Current.CancellationToken);

        Assert.Contains("main", databases);
    }

    [Fact]
    public async Task GetObjects_separates_tables_and_views()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        var tables = await explorer.GetObjectsAsync(session, "main", DbObjectKind.Table, TestContext.Current.CancellationToken);
        var views = await explorer.GetObjectsAsync(session, "main", DbObjectKind.View, TestContext.Current.CancellationToken);

        Assert.Contains(tables, t => t.Name == "t1");
        Assert.DoesNotContain(tables, t => t.Name == "v1");
        Assert.Contains(views, v => v.Name == "v1");
    }

    [Fact]
    public async Task GetColumns_returns_columns_with_types()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        var columns = await explorer.GetColumnsAsync(session, "main", "t1", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "id", "name" }, columns.Select(c => c.Name));
        var id = columns.Single(c => c.Name == "id");
        Assert.True(id.IsPrimary);
        Assert.False(id.IsNullable);
        Assert.Equal("INTEGER", id.DataType);
    }

    [Fact]
    public async Task Unsupported_kind_throws()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => explorer.GetObjectsAsync(session, "main", DbObjectKind.Trigger, TestContext.Current.CancellationToken));
    }

    private static DbSession CreateSession(TempSqliteDatabase db, FreeSqlFactory factory)
    {
        var session = new DbSession(
            new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        session.Fsql.Ado.ExecuteNonQuery("CREATE VIEW v1 AS SELECT id FROM t1");
        return session;
    }
}
