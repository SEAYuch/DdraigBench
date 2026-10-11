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
    public async Task Kind_outside_the_database_level_throws()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        // 列不属于「库级对象集合」：签名的 database 参数表达不了它，必须明确报错而非返回空
        await Assert.ThrowsAsync<NotSupportedException>(
            () => explorer.GetObjectsAsync(session, "main", DbObjectKind.Column, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Triggers_come_from_the_catalog_with_their_original_definition()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        session.Fsql.Ado.ExecuteNonQuery(
            """
            CREATE TRIGGER trg_audit AFTER INSERT ON t1
            BEGIN
                UPDATE t1 SET name = name;
            END;
            """);
        var explorer = new FreeSqlMetadataExplorer();

        var triggers = await explorer.GetObjectsAsync(
            session, "main", DbObjectKind.Trigger, TestContext.Current.CancellationToken);

        var trigger = Assert.Single(triggers);
        Assert.Equal("trg_audit", trigger.Name);
        Assert.Contains("CREATE TRIGGER trg_audit", trigger.Definition);
    }

    [Fact]
    public async Task Ddl_of_a_trigger_is_resolved_by_name()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        session.Fsql.Ado.ExecuteNonQuery(
            "CREATE TRIGGER trg_audit AFTER INSERT ON t1 BEGIN UPDATE t1 SET name = name; END");
        var explorer = new FreeSqlMetadataExplorer();

        var ddl = await explorer.GetDdlAsync(session, "main", "trg_audit", TestContext.Current.CancellationToken);

        Assert.NotNull(ddl);
        Assert.Contains("CREATE TRIGGER trg_audit", ddl);
        Assert.EndsWith(";", ddl);
    }

    [Fact]
    public async Task View_ddl_is_the_real_definition_not_a_placeholder()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        var ddl = await explorer.GetDdlAsync(session, "main", "v1", TestContext.Current.CancellationToken);

        Assert.NotNull(ddl);
        Assert.Contains("CREATE VIEW v1 AS SELECT id FROM t1", ddl);
        // §6.2 缺口①：以前只能给提示与列结构
        Assert.DoesNotContain("IDbFirst 不返回视图定义", ddl);
    }

    [Fact]
    public async Task Table_ddl_prefers_the_original_script_so_autoincrement_and_unique_survive()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        session.Fsql.Ado.ExecuteNonQuery(
            "CREATE TABLE gapless (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT NOT NULL UNIQUE)");
        var explorer = new FreeSqlMetadataExplorer();

        var ddl = await explorer.GetDdlAsync(session, "main", "gapless", TestContext.Current.CancellationToken);

        Assert.NotNull(ddl);
        // §6.2 缺口②③：AUTOINCREMENT 与内联 UNIQUE 都只存在于 sqlite_master.sql，按元数据重建必丢
        Assert.Contains("AUTOINCREMENT", ddl);
        Assert.Contains("UNIQUE", ddl);
        Assert.EndsWith(";", ddl);

        // 原样回放的脚本必须能再次执行
        session.Fsql.Ado.ExecuteNonQuery("DROP TABLE gapless");
        session.Fsql.Ado.ExecuteNonQuery(ddl);
    }

    [Fact]
    public async Task Kinds_the_dialect_lacks_return_empty_instead_of_throwing()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        // SQLite 没有存储过程与 SQL 层可枚举的函数：空集，树照常展开
        Assert.Empty(await explorer.GetObjectsAsync(
            session, "main", DbObjectKind.StoredProcedure, TestContext.Current.CancellationToken));
        Assert.Empty(await explorer.GetObjectsAsync(
            session, "main", DbObjectKind.Function, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sqlite_has_no_schema_level_so_the_tree_stays_two_levels()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        // 空集而非报错：VM 据此判断"不加模式层"（M5-2）
        Assert.Empty(await explorer.GetObjectsAsync(
            session, "main", DbObjectKind.Schema, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ddl_of_an_unknown_object_is_null()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);
        var explorer = new FreeSqlMetadataExplorer();

        Assert.Null(await explorer.GetDdlAsync(session, "main", "nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Catalog_list_truncation_is_reported_instead_of_silently_cut()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = CreateSession(db, factory);

        // 造出比 MaxRows 多一行的清单：截断必须抛错，而不是把半截清单当成"库里就这些对象"
        var sql =
            $"SELECT 'trigger_' || value AS name FROM (WITH RECURSIVE c(value) AS "
            + $"(SELECT 1 UNION ALL SELECT value + 1 FROM c WHERE value < {CatalogQueryExecutor.MaxRows + 1}) "
            + "SELECT value FROM c)";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CatalogQueryExecutor.ReadObjectsAsync(session, sql, TestContext.Current.CancellationToken));

        Assert.Contains($"超过 {CatalogQueryExecutor.MaxRows} 上限", error.Message, StringComparison.Ordinal);
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
