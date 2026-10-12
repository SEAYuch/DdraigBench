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
    [InlineData(DataType.SqlServer)]
    public void Installed_dialects_have_catalog_queries(DataType dataType)
    {
        Assert.True(CatalogQuerySets.TryGet(dataType, out var catalog));
        Assert.Same(CatalogQuerySets.Get(dataType), catalog);
    }

    [Fact]
    public void SqlServer_catalog_supports_schema_objects_and_raw_view_definitions()
    {
        var catalog = CatalogQuerySets.Get(DataType.SqlServer);

        Assert.True(catalog.ConnectionIsBoundToSingleDatabase);
        Assert.Equal("SELECT DB_NAME() AS name", catalog.BuildCurrentDatabaseQuery());
        Assert.True(catalog.Supports(DbObjectKind.Schema));
        Assert.True(catalog.Supports(DbObjectKind.Trigger));
        Assert.True(catalog.Supports(DbObjectKind.Function));
        Assert.True(catalog.Supports(DbObjectKind.StoredProcedure));
        Assert.True(catalog.SupportsViewDefinitions);
        Assert.False(catalog.SupportsTableDefinitions);

        var schemas = catalog.BuildObjectsQuery("master", DbObjectKind.Schema);
        Assert.Contains("sys.schemas", schemas);
        Assert.Contains("DB_NAME() = N'master'", schemas);
        Assert.Contains("N'sys'", schemas);

        var triggers = catalog.BuildObjectsQuery("dbo", DbObjectKind.Trigger);
        Assert.Contains("sys.triggers", triggers);
        Assert.Contains("parent_class = 1", triggers);
        Assert.Contains("OBJECT_DEFINITION(tr.object_id) AS definition", triggers);
        Assert.Contains("s.name AS [schema]", triggers);
        Assert.Contains("s.name = N'dbo'", triggers);

        var functions = catalog.BuildObjectsQuery("app", DbObjectKind.Function);
        Assert.Contains("o.type IN ('FN', 'IF', 'TF', 'FS', 'FT', 'AF')", functions);
        Assert.Contains("sys.sql_modules", functions);
        Assert.Contains("s.name AS [schema]", functions);
        Assert.Contains("s.name = N'app'", functions);

        var procedures = catalog.BuildObjectsQuery("app", DbObjectKind.StoredProcedure);
        Assert.Contains("o.type IN ('P', 'PC')", procedures);
        Assert.Contains("s.name AS [schema]", procedures);
        Assert.Contains("s.name = N'app'", procedures);

        var view = catalog.BuildViewDefinitionQuery("dbo", "v1");
        Assert.Contains("sys.views", view);
        Assert.Contains("sys.sql_modules", view);
        Assert.Contains("s.name = N'dbo'", view);
        Assert.Contains("v.name = N'v1'", view);
        Assert.Equal("CREATE VIEW [dbo].[v1] AS SELECT 1", catalog.ComposeViewDdl("dbo", "v1", " CREATE VIEW [dbo].[v1] AS SELECT 1 "));

        var escaped = catalog.BuildObjectsQuery("d'bo", DbObjectKind.Trigger);
        Assert.Contains("s.name = N'd''bo'", escaped);
    }

    [Theory]
    [InlineData(DataType.Sqlite, DbObjectKind.Trigger, true)]
    [InlineData(DataType.Sqlite, DbObjectKind.Function, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.StoredProcedure, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.Schema, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.Event, false)]
    [InlineData(DataType.MySql, DbObjectKind.Trigger, true)]
    [InlineData(DataType.MySql, DbObjectKind.Function, true)]
    [InlineData(DataType.MySql, DbObjectKind.StoredProcedure, true)]
    [InlineData(DataType.MySql, DbObjectKind.Schema, false)]
    [InlineData(DataType.MySql, DbObjectKind.Event, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Trigger, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Function, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.StoredProcedure, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Schema, true)]
    [InlineData(DataType.PostgreSQL, DbObjectKind.Event, false)]
    [InlineData(DataType.SqlServer, DbObjectKind.Trigger, true)]
    [InlineData(DataType.SqlServer, DbObjectKind.Function, true)]
    [InlineData(DataType.SqlServer, DbObjectKind.StoredProcedure, true)]
    [InlineData(DataType.SqlServer, DbObjectKind.Schema, true)]
    [InlineData(DataType.SqlServer, DbObjectKind.Event, false)]
    [InlineData(DataType.Sqlite, DbObjectKind.Table, false)]
    [InlineData(DataType.MySql, DbObjectKind.View, false)]
    public void Capability_matrix_matches_the_dialect(DataType dataType, DbObjectKind kind, bool expected) =>
        Assert.Equal(expected, CatalogQuerySets.Get(dataType).Supports(kind));

    [Fact]
    public void Postgres_and_sqlserver_have_a_schema_level()
    {
        // 模式层的存在性决定树的形状：PG / SQL Server 为「库→模式→对象」，SQLite/MySQL 不加这一层
        Assert.True(PostgreSqlCatalogQueries.Instance.Supports(DbObjectKind.Schema));
        Assert.True(SqlServerCatalogQueries.Instance.Supports(DbObjectKind.Schema));
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
    public void Postgres_and_sqlserver_are_bound_to_the_current_database()
    {
        // PG 一个连接只能浏览所连库：库清单必须收窄，否则树里会出现"看得见打不开"的库
        Assert.True(PostgreSqlCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);
        Assert.False(SqliteCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);
        Assert.False(MySqlCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);
        Assert.True(SqlServerCatalogQueries.Instance.ConnectionIsBoundToSingleDatabase);

        Assert.Equal("SELECT current_database() AS name", PostgreSqlCatalogQueries.Instance.BuildCurrentDatabaseQuery());
        Assert.Equal("SELECT DB_NAME() AS name", SqlServerCatalogQueries.Instance.BuildCurrentDatabaseQuery());

        // SQLite / MySQL 的库清单由 IDbFirst 提供，不应调用当前库查询
        Assert.Throws<NotSupportedException>(() => SqliteCatalogQueries.Instance.BuildCurrentDatabaseQuery());
        Assert.Throws<NotSupportedException>(() => MySqlCatalogQueries.Instance.BuildCurrentDatabaseQuery());
    }

    [Theory]
    [InlineData(DataType.Sqlite)]
    [InlineData(DataType.MySql)]
    [InlineData(DataType.PostgreSQL)]
    [InlineData(DataType.SqlServer)]
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

    /// <summary>
    /// M5-3：MySQL 的清单必须自带 definition（原始定义）。information_schema 只给函数体，
    /// 故 SQL 里要能看到 <c>CONCAT</c> 拼装：参数取自 PARAMETERS、函数另有 RETURNS。
    /// </summary>
    [Fact]
    public void MySql_object_listings_carry_original_definitions()
    {
        var catalog = MySqlCatalogQueries.Instance;

        var triggers = catalog.BuildObjectsQuery("shop", DbObjectKind.Trigger);
        Assert.Contains("AS definition", triggers);
        Assert.Contains("ACTION_STATEMENT", triggers);
        Assert.Contains("ACTION_TIMING", triggers);
        Assert.Contains("EVENT_MANIPULATION", triggers);
        Assert.Contains("EVENT_OBJECT_TABLE", triggers);

        var functions = catalog.BuildObjectsQuery("shop", DbObjectKind.Function);
        Assert.Contains("ROUTINE_DEFINITION", functions);
        Assert.Contains("information_schema.PARAMETERS", functions);
        Assert.Contains("GROUP_CONCAT", functions);
        Assert.Contains("DTD_IDENTIFIER", functions);
        Assert.Contains("RETURNS", functions);
        // 权限不足时 ROUTINE_DEFINITION 为 NULL：整条定义必须是 NULL 而不是半截语句
        Assert.Contains("CASE WHEN r.ROUTINE_DEFINITION IS NULL THEN NULL", functions);

        // 过程带参数模式（IN/OUT），函数不带
        var procedures = catalog.BuildObjectsQuery("shop", DbObjectKind.StoredProcedure);
        Assert.Contains("PARAMETER_MODE", procedures);
        Assert.DoesNotContain("' RETURNS '", procedures);
    }

    [Fact]
    public void PostgreSql_listings_carry_original_definitions()
    {
        var catalog = PostgreSqlCatalogQueries.Instance;

        var triggers = catalog.BuildObjectsQuery("public", DbObjectKind.Trigger);
        Assert.Contains("pg_get_triggerdef(t.oid, true) AS definition", triggers);

        var functions = catalog.BuildObjectsQuery("public", DbObjectKind.Function);
        Assert.Contains("pg_get_functiondef(p.oid)", functions);
        // 聚合函数 / internal 函数会让 pg_get_functiondef 报错，必须折成 NULL 而不是让整条清单失败
        Assert.Contains("LEFT JOIN pg_aggregate a ON a.aggfnoid = p.oid", functions);
        Assert.Contains("CASE WHEN l.lanname IN ('sql', 'plpgsql') AND a.aggfnoid IS NULL", functions);
        Assert.Contains("INNER JOIN pg_language l ON l.oid = p.prolang", functions);

        var procedures = catalog.BuildObjectsQuery("public", DbObjectKind.StoredProcedure);
        Assert.Contains("prokind = 'p'", procedures);
        Assert.Contains("pg_get_functiondef(p.oid)", procedures);
    }

    [Fact]
    public void MySql_events_are_rebuilt_into_creatable_statements()
    {
        var sql = MySqlCatalogQueries.Instance.BuildObjectsQuery("shop", DbObjectKind.Event);

        Assert.Contains("information_schema.EVENTS", sql);
        Assert.Contains("AS definition", sql);
        Assert.Contains("' ON SCHEDULE '", sql);
        Assert.Contains("'AT ', QUOTE(e.EXECUTE_AT)", sql);
        Assert.Contains("'EVERY ', e.INTERVAL_VALUE", sql);
        Assert.Contains("e.INTERVAL_FIELD", sql);
        Assert.Contains("e.STARTS IS NULL", sql);
        Assert.Contains("e.ENDS IS NULL", sql);
        Assert.Contains("' ON COMPLETION '", sql);
        Assert.Contains("e.ON_COMPLETION = 'NOT PRESERVE'", sql);
        Assert.Contains("e.STATUS", sql);
        // 三种启停状态要分别还原，丢掉 SLAVESIDE_DISABLED 会把从库上的停用事件当普通停用
        Assert.Contains("WHEN 'SLAVESIDE_DISABLED' THEN ' DISABLE ON SLAVE'", sql);
        Assert.Contains("WHEN 'DISABLED' THEN ' DISABLE'", sql);
        Assert.Contains("ELSE ' ENABLE'", sql);
        Assert.Contains("' DO ', e.EVENT_DEFINITION", sql);
        // FAIL_ON_SLAVE 之外的语义都靠这些片段保住
        Assert.Contains("EVENT_SCHEMA = 'shop'", sql);
        // 刻意不写 DEFINER：目标服务器上没有该账号时整条语句会失败
        Assert.DoesNotContain("DEFINER", sql);
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
        // 普通视图与物化视图都要收：只写 relkind = 'v' 会让物化视图的 DDL 预览直接落空（M5-3 修）
        Assert.Contains("c.relkind IN ('v', 'm')", sql);
        Assert.Contains("n.nspname = 'public'", sql);
        Assert.Contains("c.relname = 'v1'", sql);

        // 前缀必须按 relkind 分开拼：物化视图拼成 CREATE OR REPLACE VIEW 会静默改变语义
        Assert.Contains("WHEN 'm' THEN 'CREATE MATERIALIZED VIEW '", sql);
        Assert.Contains("ELSE 'CREATE OR REPLACE VIEW '", sql);
        Assert.Contains("quote_ident(n.nspname)", sql);
        Assert.Contains("quote_ident(c.relname)", sql);

        // 查询已含完整前缀，拼装层只规整尾分号
        Assert.Equal(
            "CREATE OR REPLACE VIEW \"public\".\"v1\" AS SELECT 1",
            PostgreSqlCatalogQueries.Instance.ComposeViewDdl("public", "v1", " CREATE OR REPLACE VIEW \"public\".\"v1\" AS SELECT 1;\n"));
        Assert.Equal(
            "CREATE MATERIALIZED VIEW \"public\".\"mv1\" AS SELECT 1",
            PostgreSqlCatalogQueries.Instance.ComposeViewDdl("public", "mv1", " CREATE MATERIALIZED VIEW \"public\".\"mv1\" AS SELECT 1;\n"));
    }

    [Fact]
    public void Raw_table_definitions_come_from_sqlite_and_mysql()
    {
        Assert.True(SqliteCatalogQueries.Instance.SupportsTableDefinitions);
        Assert.Contains("type = 'table'", SqliteCatalogQueries.Instance.BuildTableDefinitionQuery("main", "t1"));

        // MySQL 只能走 SHOW CREATE TABLE（两列、列名随对象类型变），故执行器按列名挑定义列
        Assert.True(MySqlCatalogQueries.Instance.SupportsTableDefinitions);
        var mysql = MySqlCatalogQueries.Instance.BuildTableDefinitionQuery("shop", "o'brien");
        // 反引号内单引号是普通字符，不需翻倍；引号内绝不能再出现裸字面量
        Assert.Equal("SHOW CREATE TABLE `shop`.`o'brien`", mysql);
        Assert.DoesNotContain("'shop'", mysql);

        // 反引号要按 MySQL 规则翻倍，否则带反引号的对象名会把语句拆坏
        Assert.Equal(
            "SHOW CREATE TABLE `shop`.`a``b`",
            MySqlCatalogQueries.Instance.BuildTableDefinitionQuery("shop", "a`b"));

        // PG 系统表里只存解析后的结构，没有原始建表语句
        Assert.False(PostgreSqlCatalogQueries.Instance.SupportsTableDefinitions);
        Assert.Throws<NotSupportedException>(
            () => PostgreSqlCatalogQueries.Instance.BuildTableDefinitionQuery("public", "t1"));
    }
}
