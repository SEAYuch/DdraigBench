// DdraigBench — CatalogQueries 单测（纯逻辑：能力矩阵 + SQL 形状 + 引用与转义）

using DdraigBench.Core.Dialects.CatalogQueries;
using DdraigBench.Core.Metadata;
using FreeSql;

namespace DdraigBench.Test;

/// <summary>
/// 目录查询层「只造 SQL 不执行」，因此这些断言在无任何靶机的环境下也全部为真：
/// 它守的是方言路由、能力矩阵、限定名与转义——执行路径由 SQLite 集成测与靶机集成测覆盖。
/// </summary>
public sealed class CatalogQueryTests
{
    [Theory]
    [InlineData(DataType.Sqlite)]
    [InlineData(DataType.MySql)]
    [InlineData(DataType.PostgreSQL)]
    public void Installed_dialects_have_catalog_queries(DataType dataType)
    {
        Assert.True(CatalogQuerySets.TryGet(dataType, out var catalog));
        Assert.Same(CatalogQuerySets.Get(dataType), catalog);
    }

    [Fact]
    public void Unknown_dialect_reports_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => CatalogQuerySets.Get(DataType.SqlServer));
        Assert.False(CatalogQuerySets.TryGet(DataType.SqlServer, out _));
    }

    [Theory]
    [InlineData(DataType.Sqlite, DbObjectKind.Trigger, true)]
    [InlineData(DataType.Sqlite, DbObjectKind.Function, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.StoredProcedure, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.Schema, false)]
    [InlineData(DataType.MySql, DbObjectKind.Trigger, true)]
    [InlineData(DataType.MySql, DbObjectKind.Function, true)]
    [InlineData(DataType.MySql, DbObjectKind.StoredProcedure, true)]
    [InlineData(DataType.MySql, DbObjectKind.Schema, false)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Trigger, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Function, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.StoredProcedure, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Schema, true)]
    [InlineData(DataType.Sqlite, DbObjectKind.Table, false)]
    [InlineData(DataType.MySql, DbObjectKind.View, false)]
    public void Capability_matrix_matches_the_dialect(DataType dataType, DbObjectKind kind, bool expected) =>
        Assert.Equal(expected, CatalogQuerySets.Get(dataType).Supports(kind));

    [Fact]
    public void Only_postgres_has_a_schema_level()
    {
        // 模式层的存在性决定树的形状（M5-2）：PG 得到「库→模式→对象」，另两方保持「库→对象」
        Assert.True(PostgreSqlCatalogQueries.Instance.Supports(DbObjectKind.Schema));
        Assert.False(SqliteCatalogQueries.Instance.Supports(DbObjectKind.Schema));
        Assert.False(MySqlCatalogQueries.Instance.Supports(DbObjectKind.Schema));
    }

    [Fact]
    public void PostgreSql_schema_query_hides_system_namespaces()
    {
        var sql = PostgreSqlCatalogQueries.Instance.BuildObjectsQuery(string.Empty, DbObjectKind.Schema);

        Assert.Contains("pg_namespace", sql);
        Assert.Contains("NOT IN ('pg_catalog', 'information_schema', 'pg_toast')", sql);
        Assert.Contains(@"NOT LIKE 'pg\_temp%'", sql);
        Assert.Contains(@"NOT LIKE 'pg\_toast_temp%'", sql);
        Assert.Contains("ORDER BY n.nspname", sql);

        // 模式清单与 database 参数无关（pg_namespace 本就只含当前库的模式）
        Assert.Equal(sql, PostgreSqlCatalogQueries.Instance.BuildObjectsQuery("whatever", DbObjectKind.Schema));
    }

    [Fact]
    public void Only_postgres_is_bound_to_a_single_database()
    {
        // PG 一个连接只能浏览所连库：库清单必须收窄，否则树里会出现"看得见打不开"的库
        Assert.True(PostgreSqlCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);
        Assert.False(SqliteCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);
        Assert.False(MySqlCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);

        Assert.Equal("SELECT current_database() AS name", PostgreSqlCatalogQueries.Instance.BuildCurrentDatabaseQuery());

        // 另两方的库清单由 IDbFirst 提供，不该被调用
        Assert.Throws<NotSupportedException>(() => SqliteCatalogQueries.Instance.BuildCurrentDatabaseQuery());
        Assert.Throws<NotSupportedException>(() => MySqlCatalogQueries.Instance.BuildCurrentDatabaseQuery());
    }

    [Theory]
    [InlineData(DataType.Sqlite)]
    [InlineData(DataType.MySql)]
    [InlineData(DataType.PostgreSQL)]
    public void Table_is_not_a_catalog_object_listing(DataType dataType) =>
        // 表/视图由 IDbFirst 提供；目录查询只补 IDbFirst 看不到的对象
        Assert.Throws<NotSupportedException>(() =>
            CatalogQuerySets.Get(dataType).BuildObjectsQuery("db", DbObjectKind.Table));

    [Fact]
    public void Sqlite_reads_objects_from_the_attached_database()
    {
        var sql = SqliteCatalogQueries.Instance.BuildObjectsQuery("main", DbObjectKind.Trigger);

        Assert.Contains("\"main\".sqlite_master", sql);
        Assert.Contains("type = 'trigger'", sql);
        // 触发器定义在 sqlite_master.sql 里，一次查询即可同时拿到名字与完整语句
        Assert.Contains("sql AS definition", sql);
    }

    [Fact]
    public void Sqlite_returns_full_statements_so_composing_is_identity()
    {
        const string ddl = "CREATE VIEW v1 AS SELECT id FROM t1";

        Assert.Equal(ddl, SqliteCatalogQueries.Instance.ComposeViewDdl("main", "v1", ddl));
    }

    [Fact]
    public void Sqlite_escapes_names_in_definition_lookups()
    {
        var view = SqliteCatalogQueries.Instance.BuildViewDefinitionQuery("main", "o'brien");
        Assert.Contains("name = 'o''brien'", view);

        var table = SqliteCatalogQueries.Instance.BuildTableDefinitionQuery("main", "o'brien");
        Assert.Contains("type = 'table'", table);
        Assert.Contains("name = 'o''brien'", table);
    }

    [Fact]
    public void MySql_objects_come_from_information_schema_scoped_to_the_schema()
    {
        var catalog = MySqlCatalogQueries.Instance;

        var triggers = catalog.BuildObjectsQuery("shop", DbObjectKind.Trigger);
        Assert.Contains("information_schema.TRIGGERS", triggers);
        Assert.Contains("TRIGGER_SCHEMA = 'shop'", triggers);

        var functions = catalog.BuildObjectsQuery("shop", DbObjectKind.Function);
        Assert.Contains("information_schema.ROUTINES", functions);
        Assert.Contains("ROUTINE_SCHEMA = 'shop'", functions);
        Assert.Contains("ROUTINE_TYPE = 'FUNCTION'", functions);

        var procedures = catalog.BuildObjectsQuery("shop", DbObjectKind.StoredProcedure);
        Assert.Contains("ROUTINE_TYPE = 'PROCEDURE'", procedures);
    }

    [Fact]
    public void MySql_view_definition_is_wrapped_into_a_creatable_statement()
    {
        var sql = MySqlCatalogQueries.Instance.BuildViewDefinitionQuery("shop", "v1");

        Assert.Contains("information_schema.VIEWS", sql);
        Assert.Contains("TABLE_SCHEMA = 'shop'", sql);
        Assert.Contains("TABLE_NAME = 'v1'", sql);

        // information_schema 只给 SELECT 主体，需补前缀与限定名；已有的分号不能翻倍
        Assert.Equal(
            "CREATE VIEW `shop`.`v1` AS\nselect 1;",
            MySqlCatalogQueries.Instance.ComposeViewDdl("shop", "v1", " select 1; "));
    }

    [Fact]
    public void MySql_escapes_backslashes_when_inlining_schema_names()
    {
        // 默认 sql_mode 下反斜杠是转义符：'a\b' 必须写成 'a\\b'
        var sql = MySqlCatalogQueries.Instance.BuildObjectsQuery(@"a\b", DbObjectKind.Trigger);

        Assert.Contains(@"TRIGGER_SCHEMA = 'a\\b'", sql);
    }

    [Fact]
    public void PostgreSql_objects_come_from_pg_catalog_scoped_to_the_schema()
    {
        var catalog = PostgreSqlCatalogQueries.Instance;

        var triggers = catalog.BuildObjectsQuery("public", DbObjectKind.Trigger);
        Assert.Contains("pg_trigger", triggers);
        Assert.Contains("NOT t.tgisinternal", triggers);
        Assert.Contains("n.nspname = 'public'", triggers);

        var functions = catalog.BuildObjectsQuery("public", DbObjectKind.Function);
        Assert.Contains("pg_proc", functions);
        Assert.Contains("prokind = 'f'", functions);
        Assert.Contains(@"NOT LIKE 'pg\_%'", functions);

        var procedures = catalog.BuildObjectsQuery("public", DbObjectKind.StoredProcedure);
        Assert.Contains("prokind = 'p'", procedures);
    }

    [Fact]
    public void PostgreSql_view_definition_uses_pg_get_viewdef()
    {
        var sql = PostgreSqlCatalogQueries.Instance.BuildViewDefinitionQuery("public", "v1");

        Assert.Contains("pg_get_viewdef(c.oid, true)", sql);
        Assert.Contains("c.relkind = 'v'", sql);
        Assert.Contains("n.nspname = 'public'", sql);
        Assert.Contains("c.relname = 'v1'", sql);

        // pg_get_viewdef 返回的体可能带尾分号与空白，拼装时要规整掉
        Assert.Equal(
            "CREATE OR REPLACE VIEW \"public\".\"v1\" AS\nSELECT 1;",
            PostgreSqlCatalogQueries.Instance.ComposeViewDdl("public", "v1", " SELECT 1;\n"));
    }

    [Fact]
    public void Only_sqlite_can_return_raw_table_definitions()
    {
        Assert.True(SqliteCatalogQueries.Instance.SupportsTableDefinitions);
        Assert.Contains("type = 'table'", SqliteCatalogQueries.Instance.BuildTableDefinitionQuery("main", "t1"));

        // MySQL 只能走 SHOW CREATE TABLE（多列、列名不固定），PG 系统表里只存解析后的结构
        Assert.False(MySqlCatalogQueries.Instance.SupportsTableDefinitions);
        Assert.False(PostgreSqlCatalogQueries.Instance.SupportsTableDefinitions);
        Assert.Throws<NotSupportedException>(
            () => MySqlCatalogQueries.Instance.BuildTableDefinitionQuery("shop", "t1"));
        Assert.Throws<NotSupportedException>(
            () => PostgreSqlCatalogQueries.Instance.BuildTableDefinitionQuery("public", "t1"));
    }
}
