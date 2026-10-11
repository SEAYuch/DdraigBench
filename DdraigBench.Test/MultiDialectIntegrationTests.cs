// DdraigBench — 多方言集成测试（靶机连接串走环境变量；未设置或不可达则跳过）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Dialects;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Querying;
using DdraigBench.Core.Sessions;
using FreeSql;

namespace DdraigBench.Test;

public sealed class MultiDialectIntegrationTests
{
    private const string MySqlEnvVar = "DDRAIGBENCH_MYSQL";
    private const string PostgreSqlEnvVar = "DDRAIGBENCH_PG";
    private const string SqlServerEnvVar = "DDRAIGBENCH_SQLSERVER";

    [Fact]
    public Task MySql_runs_select_and_reads_databases() =>
        RunAsync(MySqlEnvVar, DataType.MySql, "MySQL/MariaDB");

    [Fact]
    public Task PostgreSql_runs_select_and_reads_databases() =>
        RunAsync(PostgreSqlEnvVar, DataType.PostgreSQL, "PostgreSQL");

    [Fact]
    public Task SqlServer_runs_select_and_reads_databases() =>
        RunAsync(SqlServerEnvVar, DataType.SqlServer, "SQL Server");

    [Fact]
    public Task MySql_catalog_queries_run_against_the_target() =>
        RunCatalogAsync(MySqlEnvVar, DataType.MySql, "MySQL/MariaDB", "SELECT DATABASE()");

    [Fact]
    public Task PostgreSql_catalog_queries_run_against_the_target() =>
        RunCatalogAsync(PostgreSqlEnvVar, DataType.PostgreSQL, "PostgreSQL", "SELECT current_schema()");

    /// <summary>
    /// 目录查询（触发器 / 例程 / 视图定义）的靶机验证。允许空集，但绝不允许报错——
    /// 这守的是 SQL 语法与 information_schema / pg_catalog 的可见性。
    /// </summary>
    /// <param name="schemaQuery">
    /// 取「当前容器名」的 SQL。刻意不用 <c>GetDatabasesAsync()</c> 的首项：
    /// PG 的 FreeSql 实现把 <c>GetTablesByDatabase</c> 按 <c>pg_namespace.nspname</c> 过滤，
    /// 而 <c>GetDatabases()</c> 返回的是 <c>pg_database.datname</c>，两者不同源（AGENTS.md §4 已记录）。
    /// </param>
    private static async Task RunCatalogAsync(
        string envVar, DataType dataType, string label, string schemaQuery)
    {
        var connectionString = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"{envVar} 未设置，跳过 {label} 目录查询集成测试");
            return;
        }

        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile(label, dataType, connectionString);
        await using var session = new DbSession(profile, factory);

        if (!IsReachable(session))
        {
            Assert.Skip($"{envVar} 指向的 {label} 不可达，跳过");
            return;
        }

        var schema = Convert.ToString(session.Fsql.Ado.ExecuteScalar(schemaQuery));
        Assert.False(string.IsNullOrWhiteSpace(schema), $"{schemaQuery} 未返回容器名");

        var explorer = new FreeSqlMetadataExplorer();
        var ct = TestContext.Current.CancellationToken;

        // M5-2：库清单。PG 必须收窄为当前库（一个连接只能浏览所连库），否则树里会出现"看得见打不开"的库
        var databases = await explorer.GetDatabasesAsync(session, ct);
        Assert.NotEmpty(databases);

        if (dataType == DataType.PostgreSQL)
        {
            var currentDatabase = Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT current_database()"));
            Assert.Equal(new[] { currentDatabase! }, databases);
        }

        // M5-2：模式层。PG 有且当前模式必在其中；MySQL 的库即模式，必须为空集（树不加这一层）
        var schemas = await explorer.GetObjectsAsync(session, schema!, DbObjectKind.Schema, ct);

        if (dataType == DataType.PostgreSQL)
        {
            Assert.Contains(schemas, s => s.Name == schema);
        }
        else
        {
            Assert.Empty(schemas);
        }

        // 目录 SQL 必须在真实服务器上跑得通（语法 + 权限）
        foreach (var kind in new[] { DbObjectKind.Trigger, DbObjectKind.StoredProcedure, DbObjectKind.Function })
        {
            await explorer.GetObjectsAsync(session, schema!, kind, ct);
        }

        // 视图定义：建一个探测视图取真实定义，结束即删
        session.Fsql.Ado.ExecuteNonQuery("CREATE OR REPLACE VIEW ddb_probe_view AS SELECT 1 AS n");
        try
        {
            var ddl = await explorer.GetDdlAsync(session, schema!, "ddb_probe_view", ct);

            Assert.NotNull(ddl);
            Assert.Contains("VIEW", ddl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery("DROP VIEW IF EXISTS ddb_probe_view");
        }
    }

    private static async Task RunAsync(string envVar, DataType dataType, string label)
    {
        var connectionString = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"{envVar} 未设置，跳过 {label} 靶机集成测试");
            return;
        }

        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile(label, dataType, connectionString);
        await using var session = new DbSession(profile, factory);

        if (!IsReachable(session))
        {
            Assert.Skip($"{envVar} 指向的 {label} 不可达，跳过");
            return;
        }

        var result = await new QueryRunner(session.Fsql).ExecuteAsync("SELECT 1 AS n");

        Assert.Null(result.Error);
        Assert.Equal(1, Convert.ToInt32(Assert.Single(result.Rows)[0]));

        var explorer = new FreeSqlMetadataExplorer();
        var databases = await explorer.GetDatabasesAsync(session);
        Assert.NotEmpty(databases);

        if (dataType == DataType.SqlServer)
        {
            var database = Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT DB_NAME()"));
            Assert.Contains(database!, databases);

            session.Fsql.Ado.ExecuteNonQuery(
                "IF OBJECT_ID(N'dbo.ddb_probe_table', N'U') IS NOT NULL DROP TABLE dbo.ddb_probe_table; " +
                "CREATE TABLE dbo.ddb_probe_table (id int NOT NULL PRIMARY KEY, name nvarchar(40) NULL)");
            try
            {
                var orderedSql = "SELECT id, name FROM dbo.ddb_probe_table ORDER BY id";
                Assert.Null(PagedSql.Build(orderedSql, SqlDialects.Get(dataType), 2, 0));
                var orderedResult = await new QueryRunner(session.Fsql).ExecuteAsync(orderedSql, maxRows: 10);
                Assert.Null(orderedResult.Error); // 原始 ORDER BY 直通执行，SQL Server 不使用派生表自动分页
                Assert.Equal("name", orderedResult.Columns[1]);

                var ddl = await explorer.GetDdlAsync(
                    session, database!, "ddb_probe_table", TestContext.Current.CancellationToken);
                Assert.Contains("CREATE TABLE", ddl, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                session.Fsql.Ado.ExecuteNonQuery("DROP TABLE dbo.ddb_probe_table");
            }
        }
    }

    private static bool IsReachable(DbSession session)
    {
        try
        {
            return session.Fsql.Ado.ExecuteConnectTest();
        }
        catch
        {
            return false;
        }
    }
}
