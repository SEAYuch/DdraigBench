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

    [Fact]
    public Task SqlServer_catalog_queries_and_definitions_run_against_the_target() =>
        RunSqlServerCatalogAsync();

    /// <summary>
    /// 目录查询（触发器 / 例程 / 视图定义）的靶机验证。允许空集，但绝不允许报错——
    /// 这守的是 SQL 语法与 information_schema / pg_catalog 的可见性。
    /// </summary>
    /// <param name="schemaQuery">
    /// 取「当前容器名」的 SQL。刻意不用 <c>GetDatabasesAsync()</c> 的首项：
    /// PG 的 <c>GetTablesByDatabase</c> 实测要的是**库名**（FreeSql 只在客户端拿参数去匹配
    /// <c>nspname || '.' || relname</c> 前缀），而 <c>GetDatabases()</c> 返回 <c>pg_database.datname</c>，
    /// 传模式名会静默返回空集（AGENTS.md §4 已记录）。
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

        // M5-3 回归：表的清单与列必须按「容器（PG = 模式名）」查得到。
        // 这条守的是 FreeSql PG 的 GetTablesByDatabase 期望库名而非模式名那个坑：传模式名会静默返回空集，
        // 于是 PG 的树里一张表都列不出来（2026-10-12 用真 PG 靶机暴露，修在 GetIdbFirstDatabaseAsync）。
        session.Fsql.Ado.ExecuteNonQuery("CREATE TABLE ddb_probe_table (id INT PRIMARY KEY, note VARCHAR(32))");
        try
        {
            var tables = await explorer.GetObjectsAsync(session, schema!, DbObjectKind.Table, ct);
            Assert.Contains(tables, t => t.Name == "ddb_probe_table");

            var columns = await explorer.GetColumnsAsync(session, schema!, "ddb_probe_table", ct);
            Assert.Contains(columns, c => c.Name == "id");

            // M5-3：MySQL 表定义走 SHOW CREATE TABLE（原始语句）。指纹取表选项：按元数据重建的
            // DdlGenerator 根本不输出 ENGINE=/COLLATE=，故这几个断言只有拿到原始语句才可能通过。
            if (dataType == DataType.MySql)
            {
                session.Fsql.Ado.ExecuteNonQuery(
                    "ALTER TABLE ddb_probe_table COLLATE = utf8mb4_unicode_ci COMMENT = 'ddb probe comment'");
            }

            var tableDdl = await explorer.GetDdlAsync(session, schema!, "ddb_probe_table", ct);
            Assert.NotNull(tableDdl);
            Assert.Contains("CREATE", tableDdl, StringComparison.OrdinalIgnoreCase);

            if (dataType == DataType.MySql)
            {
                Assert.Contains("ENGINE=", tableDdl);
                Assert.Contains("utf8mb4_unicode_ci", tableDdl);
                Assert.Contains("ddb probe comment", tableDdl);
                // SHOW CREATE TABLE 的第二列是定义、第一列是名字，按列名挑时绝不能把名字当 DDL 返回
                Assert.Contains("ddb_probe_table", tableDdl);
            }
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery("DROP TABLE IF EXISTS ddb_probe_table");
        }

        // M5-3：MySQL 分区是表的子结构，不单独伪装成库级 DbObjectKind；靶机验证表节点的分区子查询。
        if (dataType == DataType.MySql)
        {
            session.Fsql.Ado.ExecuteNonQuery(
                "CREATE TABLE ddb_probe_partitioned (id INT NOT NULL, note VARCHAR(32)) "
                + "ENGINE=InnoDB PARTITION BY RANGE (id) "
                + "(PARTITION p0 VALUES LESS THAN (10), PARTITION pmax VALUES LESS THAN MAXVALUE)");
            try
            {
                var partitions = await explorer.GetPartitionsAsync(session, schema!, "ddb_probe_partitioned", ct);
                Assert.Equal(new[] { "p0", "pmax" }, partitions.Select(p => p.Name));
                Assert.All(partitions, p => Assert.Equal("RANGE", p.Method));
                // information_schema 保留了原表达式的反引号引用，定义预览需按服务器返回值原样保真
                Assert.All(partitions, p => Assert.Equal("`id`", p.Expression));
                Assert.Equal(new[] { "10", "MAXVALUE" }, partitions.Select(p => p.Description));
            }
            finally
            {
                session.Fsql.Ado.ExecuteNonQuery("DROP TABLE IF EXISTS ddb_probe_partitioned");
            }
        }

        // 视图定义：建一个探测视图取真实定义，结束即删
        session.Fsql.Ado.ExecuteNonQuery("CREATE OR REPLACE VIEW ddb_probe_view AS SELECT 1 AS n");
        try
        {
            var ddl = await explorer.GetDdlAsync(session, schema!, "ddb_probe_view", ct);

            Assert.NotNull(ddl);
            Assert.Contains("VIEW", ddl, StringComparison.OrdinalIgnoreCase);
            // 普通视图不得被误判成物化视图（两者共用一条定义查询，靠 relkind 分流）
            Assert.DoesNotContain("MATERIALIZED", ddl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery("DROP VIEW IF EXISTS ddb_probe_view");
        }

        // M5-3：PG 物化视图定义。IDbFirst 把 relkind='m' 也当成 VIEW 列进树，
        // 故 DDL 预览必须给出 CREATE MATERIALIZED VIEW——拼成 CREATE VIEW 会静默丢掉物化语义。
        if (dataType == DataType.PostgreSQL)
        {
            session.Fsql.Ado.ExecuteNonQuery("CREATE MATERIALIZED VIEW ddb_probe_matview AS SELECT 1 AS n");
            try
            {
                var ddl = await explorer.GetDdlAsync(session, schema!, "ddb_probe_matview", ct);

                Assert.NotNull(ddl);
                Assert.Contains("CREATE MATERIALIZED VIEW", ddl, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                session.Fsql.Ado.ExecuteNonQuery("DROP MATERIALIZED VIEW IF EXISTS ddb_probe_matview");
            }
        }
    }

    /// <summary>
    /// M5-3 剩余项：MySQL / PostgreSQL 的**触发器与例程定义**。
    /// 清单 SQL 必须带回可执行的 <c>definition</c>，且定义要与刚建的对象对得上（不是空串、不是别的对象）。
    /// 结束即删，随机后缀避免与他人/历史运行互相污染。
    /// </summary>
    [Fact]
    public Task MySql_routine_and_trigger_definitions_come_back_from_the_catalog() =>
        RunRoutineDefinitionAsync(MySqlEnvVar, DataType.MySql, "MySQL/MariaDB");

    [Fact]
    public Task PostgreSql_routine_and_trigger_definitions_come_back_from_the_catalog() =>
        RunRoutineDefinitionAsync(PostgreSqlEnvVar, DataType.PostgreSQL, "PostgreSQL");

    /// <summary>
    /// M5-3：MySQL/MariaDB 的**计划事件**清单与定义。事件只有 MySQL 家族有，其余方言必须是空集（不加这一组）。
    /// </summary>
    [Fact]
    public Task MySql_events_come_back_from_the_catalog() =>
        RunMySqlEventAsync();

    private static async Task RunRoutineDefinitionAsync(string envVar, DataType dataType, string label)
    {
        var connectionString = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"{envVar} 未设置，跳过 {label} 例程/触发器定义集成测试");
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

        var dialect = SqlDialects.Get(dataType);
        var explorer = new FreeSqlMetadataExplorer();
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 容器：MySQL 用当前库（库即模式），PG 用当前模式
        var container = dataType == DataType.MySql
            ? Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT DATABASE()"))
            : Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT current_schema()"));

        Assert.False(string.IsNullOrWhiteSpace(container), "未取到容器名");

        var table = dialect.QuoteIdentifier("ddb_m53_" + suffix);
        var function = dialect.QuoteIdentifier("fn_" + suffix);
        var procedure = dialect.QuoteIdentifier("pr_" + suffix);
        var trigger = dialect.QuoteIdentifier("tg_" + suffix);

        if (dataType == DataType.MySql)
        {
            session.Fsql.Ado.ExecuteNonQuery($"CREATE TABLE {table} (id INT PRIMARY KEY, v INT)");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE FUNCTION {function}(x INT) RETURNS INT DETERMINISTIC RETURN x * 2");
            // 过程体是单条语句，免得依赖客户端 delimiter
            session.Fsql.Ado.ExecuteNonQuery($"CREATE PROCEDURE {procedure}(IN x INT) SELECT x");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE TRIGGER {trigger} BEFORE INSERT ON {table} FOR EACH ROW SET NEW.v = COALESCE(NEW.v, 0) + 1");
        }
        else
        {
            session.Fsql.Ado.ExecuteNonQuery($"CREATE TABLE {table} (id INT PRIMARY KEY, v INT)");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE FUNCTION {function}(x INT) RETURNS INT LANGUAGE sql AS $$ SELECT x * 2 $$");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE PROCEDURE {procedure}(x INT) LANGUAGE sql AS $$ SELECT x $$");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE FUNCTION fn_body_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW.v := 1; RETURN NEW; END $$");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE TRIGGER {trigger} BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION fn_body_{suffix}()");
        }

        try
        {
            foreach (var (kind, name) in new[]
            {
                (DbObjectKind.Trigger, trigger),
                (DbObjectKind.Function, function),
                (DbObjectKind.StoredProcedure, procedure),
            })
            {
                var objects = await explorer.GetObjectsAsync(session, container!, kind, ct);
                var item = Assert.Single(objects, o => o.Name == name.Trim('`', '"', '[', ']'));

                Assert.False(
                    string.IsNullOrWhiteSpace(item.Definition),
                    $"{label} 的 {kind} {item.Name} 应带回原始定义");

                // DDL 预览必须走出同一份定义（清单自带 definition，无需第二条通道）
                var ddl = await explorer.GetDdlAsync(session, container!, item.Name, ct);
                Assert.NotNull(ddl);
                Assert.Contains("CREATE", ddl, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery($"DROP TRIGGER IF EXISTS {trigger}"
                + (dataType == DataType.PostgreSQL ? $" ON {table}" : string.Empty));
            session.Fsql.Ado.ExecuteNonQuery($"DROP PROCEDURE IF EXISTS {procedure}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP FUNCTION IF EXISTS {function}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS {table}");
            if (dataType == DataType.PostgreSQL)
            {
                session.Fsql.Ado.ExecuteNonQuery($"DROP FUNCTION IF EXISTS fn_body_{suffix}()");
            }
        }
    }

    /// <summary>
    /// M5-3：MySQL/MariaDB 计划事件。事件的 <c>EVENT_DEFINITION</c> 只有 <c>DO</c> 之后的体，
    /// 时间表 / 完成策略 / 启停状态都在别的列里 —— 故清单 SQL 必须逐列拼回完整 <c>CREATE EVENT</c>。
    /// 漏拼任何一段都会静默改语义（丢 <c>ON COMPLETION NOT PRESERVE</c> 让一次性事件执行后不再删除；
    /// 丢 <c>DISABLE</c> 会把人工停用的事件当启用还原）。这里取回定义后按「再执行一遍」验证，
    /// 因为只有真的能跑通才说明拼装正确；<c>EVENT_SCHEDULER=OFF</c> 时事件不会被调度执行，安全。
    /// </summary>
    private static async Task RunMySqlEventAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(MySqlEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"{MySqlEnvVar} 未设置，跳过 MySQL 事件集成测试");
            return;
        }

        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile("MySQL/MariaDB", DataType.MySql, connectionString);
        await using var session = new DbSession(profile, factory);

        if (!IsReachable(session))
        {
            Assert.Skip($"{MySqlEnvVar} 指向的 MySQL/MariaDB 不可达，跳过");
            return;
        }

        var dialect = SqlDialects.Get(DataType.MySql);
        var explorer = new FreeSqlMetadataExplorer();
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var container = Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT DATABASE()"));
        Assert.False(string.IsNullOrWhiteSpace(container), "未取到当前库名");

        var log = dialect.QuoteIdentifier("ddb_evt_log_" + suffix);
        var recurring = dialect.QuoteIdentifier("ddb_evt_r_" + suffix);
        var once = dialect.QuoteIdentifier("ddb_evt_o_" + suffix);

        try
        {
            session.Fsql.Ado.ExecuteNonQuery($"CREATE TABLE {log} (note VARCHAR(64))");
            // 两种事件都建 DISABLE：事件调度器即使开着也不会真的插入，避免污染靶机数据
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE EVENT {recurring} ON SCHEDULE EVERY 1 DAY STARTS CURRENT_TIMESTAMP + INTERVAL 1 DAY "
                + "ON COMPLETION PRESERVE DISABLE COMMENT 'ddb probe recurring' "
                + $"DO INSERT INTO {log} (note) VALUES ('recurring')");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE EVENT {once} ON SCHEDULE AT CURRENT_TIMESTAMP + INTERVAL 1 YEAR "
                + "ON COMPLETION NOT PRESERVE DISABLE "
                + $"DO INSERT INTO {log} (note) VALUES ('once')");

            var objects = await explorer.GetObjectsAsync(session, container!, DbObjectKind.Event, ct);
            var schemaLiteral = MySqlDialect.Instance.FormatLiteral(container!);

            foreach (var (quoted, name, completion, schedule) in new[]
                     {
                         (recurring, recurring.Trim('`', '"', '[', ']'), "PRESERVE", "EVERY 1 DAY"),
                         (once, once.Trim('`', '"', '[', ']'), "NOT PRESERVE", "AT "),
                     })
            {
                var item = Assert.Single(objects, o => o.Name == name);

                Assert.False(
                    string.IsNullOrWhiteSpace(item.Definition),
                    $"MySQL 的事件 {name} 应带回原始定义");

                var definition = item.Definition!;
                Assert.Contains("CREATE EVENT", definition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ON SCHEDULE", definition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(schedule, definition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("DISABLE", definition, StringComparison.OrdinalIgnoreCase);

                // DDL 预览走同一条通道，不应是空/占位
                var ddl = await explorer.GetDdlAsync(session, container!, name, ct);
                Assert.NotNull(ddl);
                Assert.Contains("CREATE EVENT", ddl, StringComparison.OrdinalIgnoreCase);

                // 决定性验证：把重建出来的语句真的执行一遍，必须能建回同名事件
                session.Fsql.Ado.ExecuteNonQuery($"DROP EVENT IF EXISTS {quoted}");
                session.Fsql.Ado.ExecuteNonQuery(definition);

                var restored = Convert.ToString(session.Fsql.Ado.ExecuteScalar(
                    $"SELECT CONCAT(STATUS, '|', ON_COMPLETION) FROM information_schema.EVENTS "
                    + $"WHERE EVENT_SCHEMA = {schemaLiteral} AND EVENT_NAME = {MySqlDialect.Instance.FormatLiteral(name)}"));
                Assert.Equal($"DISABLED|{completion}", restored);
            }
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery($"DROP EVENT IF EXISTS {recurring}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP EVENT IF EXISTS {once}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS {log}");
        }
    }

    private static async Task RunSqlServerCatalogAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip($"{SqlServerEnvVar} 未设置，跳过 SQL Server 目录查询集成测试");
            return;
        }

        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile("SQL Server", DataType.SqlServer, connectionString);
        await using var session = new DbSession(profile, factory);
        if (!IsReachable(session))
        {
            Assert.Skip($"{SqlServerEnvVar} 指向的 SQL Server 不可达，跳过");
            return;
        }

        var currentDatabase = Convert.ToString(session.Fsql.Ado.ExecuteScalar("SELECT DB_NAME()"));
        Assert.False(string.IsNullOrWhiteSpace(currentDatabase));

        var explorer = new FreeSqlMetadataExplorer();
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var schemaName = "ddb_m53_" + suffix;
        var viewName = "view_" + suffix;
        var functionName = "function_" + suffix;
        var procedureName = "procedure_" + suffix;
        var triggerName = "trigger_" + suffix;
        var tableName = "table_" + suffix;
        var dialect = SqlDialects.Get(DataType.SqlServer);
        var qSchema = dialect.QuoteIdentifier(schemaName);
        var qView = dialect.QuoteIdentifier(viewName);
        var qFunction = dialect.QuoteIdentifier(functionName);
        var qProcedure = dialect.QuoteIdentifier(procedureName);
        var qTrigger = dialect.QuoteIdentifier(triggerName);
        var qTable = dialect.QuoteIdentifier(tableName);

        session.Fsql.Ado.ExecuteNonQuery($"CREATE SCHEMA {qSchema}");
        try
        {
            var schemas = await explorer.GetObjectsAsync(session, currentDatabase!, DbObjectKind.Schema, ct);
            Assert.Contains(schemas, item => item.Name == schemaName);

            session.Fsql.Ado.ExecuteNonQuery($"CREATE TABLE {qSchema}.{qTable} (id int NOT NULL PRIMARY KEY, value int NOT NULL)");
            session.Fsql.Ado.ExecuteNonQuery($"CREATE VIEW {qSchema}.{qView} AS SELECT id, value FROM {qSchema}.{qTable}");
            session.Fsql.Ado.ExecuteNonQuery($"CREATE FUNCTION {qSchema}.{qFunction}() RETURNS int AS BEGIN RETURN 1 END");
            session.Fsql.Ado.ExecuteNonQuery($"CREATE PROCEDURE {qSchema}.{qProcedure} AS SELECT 1 AS n");
            session.Fsql.Ado.ExecuteNonQuery(
                $"CREATE TRIGGER {qSchema}.{qTrigger} ON {qSchema}.{qTable} AFTER INSERT AS BEGIN SET NOCOUNT ON; END");

            var tables = await explorer.GetObjectsAsync(session, schemaName, DbObjectKind.Table, ct);
            Assert.Contains(tables, item => item.Name == tableName && item.Schema == schemaName);
            var columns = await explorer.GetColumnsAsync(session, schemaName, tableName, ct);
            Assert.Equal(new[] { "id", "value" }, columns.Select(column => column.Name));
            var tableDdl = await explorer.GetDdlAsync(session, schemaName, tableName, ct);
            Assert.Contains("CREATE TABLE", tableDdl, StringComparison.OrdinalIgnoreCase);

            foreach (var kind in new[] { DbObjectKind.View, DbObjectKind.Function, DbObjectKind.StoredProcedure, DbObjectKind.Trigger })
            {
                var objects = await explorer.GetObjectsAsync(session, schemaName, kind, ct);
                var targetName = kind switch
                {
                    DbObjectKind.View => viewName,
                    DbObjectKind.Function => functionName,
                    DbObjectKind.StoredProcedure => procedureName,
                    DbObjectKind.Trigger => triggerName,
                    _ => string.Empty,
                };
                var catalogItem = Assert.Single(objects, item => item.Name == targetName);
                Assert.Equal(schemaName, catalogItem.Schema);
                if (kind != DbObjectKind.View)
                {
                    Assert.Contains("CREATE", catalogItem.Definition, StringComparison.OrdinalIgnoreCase);
                }

                var definition = await explorer.GetDdlAsync(session, schemaName, targetName, ct);
                Assert.Contains(targetName, definition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("CREATE", definition, StringComparison.OrdinalIgnoreCase);
            }

            var viewDdl = await explorer.GetDdlAsync(session, schemaName, viewName, ct);
            Assert.Contains($"CREATE VIEW [{schemaName}].[{viewName}]", viewDdl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            session.Fsql.Ado.ExecuteNonQuery($"DROP VIEW IF EXISTS {qSchema}.{qView}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP TRIGGER IF EXISTS {qSchema}.{qTrigger}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP PROCEDURE IF EXISTS {qSchema}.{qProcedure}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP FUNCTION IF EXISTS {qSchema}.{qFunction}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP TABLE IF EXISTS {qSchema}.{qTable}");
            session.Fsql.Ado.ExecuteNonQuery($"DROP SCHEMA IF EXISTS {qSchema}");
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
                session.Fsql.Ado.ExecuteNonQuery("INSERT INTO dbo.ddb_probe_table (id, name) VALUES (1, N'a'), (2, N'b'), (3, N'c')");
                var dialect = SqlDialects.Get(dataType);
                Assert.Null(PagedSql.Build("SELECT id, name FROM dbo.ddb_probe_table", dialect, 2, 0));

                var orderedSql = "SELECT id, name FROM dbo.ddb_probe_table ORDER BY id";
                var pagedSql = PagedSql.Build(orderedSql, dialect, 2, 0);
                Assert.Equal(
                    "SELECT id, name FROM dbo.ddb_probe_table ORDER BY id OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY",
                    pagedSql);
                var orderedResult = await new QueryRunner(session.Fsql).ExecuteAsync(pagedSql!, maxRows: 10);
                Assert.Null(orderedResult.Error);
                Assert.Equal("name", orderedResult.Columns[1]);
                Assert.Equal(new[] { 1, 2 }, orderedResult.Rows.Select(row => Convert.ToInt32(row[0])));

                var secondPageSql = PagedSql.Build(orderedSql, SqlDialects.Get(dataType), 2, 2);
                Assert.NotNull(secondPageSql);
                var secondPage = await new QueryRunner(session.Fsql).ExecuteAsync(secondPageSql!, maxRows: 10);
                Assert.Null(secondPage.Error);
                Assert.Equal(new[] { 3 }, secondPage.Rows.Select(row => Convert.ToInt32(row[0])));

                // 表头排序下推：没有顶层 ORDER BY 时替用户造一段，再叠加 OFFSET/FETCH（真跑验证 T-SQL 合法）
                var sort = new ResultSort(0, "id", Descending: true, ColumnNameIsUnique: true);
                var sortedSql = PagedSql.Build(
                    "SELECT id, name FROM dbo.ddb_probe_table", dialect, 2, 0, sort, out var sortApplied);
                Assert.True(sortApplied);
                Assert.Equal(
                    "SELECT id, name FROM dbo.ddb_probe_table ORDER BY [id] DESC OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY",
                    sortedSql);
                var sortedResult = await new QueryRunner(session.Fsql).ExecuteAsync(sortedSql!, maxRows: 10);
                Assert.Null(sortedResult.Error);
                Assert.Equal(new[] { 3, 2 }, sortedResult.Rows.Select(row => Convert.ToInt32(row[0])));

                // 已有顶层 ORDER BY：不动用户语义，且如实回报"排序没下推"
                var keptSql = PagedSql.Build(orderedSql, dialect, 2, 0, sort, out var existingApplied);
                Assert.NotNull(keptSql);
                Assert.False(existingApplied);

                // SQL Server 加了模式层后对象容器是模式名（AGENTS.md §4），探测表建在 dbo 下
                var ddl = await explorer.GetDdlAsync(
                    session, "dbo", "ddb_probe_table", TestContext.Current.CancellationToken);
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
