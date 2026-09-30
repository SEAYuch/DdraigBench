// DdraigBench — 多方言集成测试（靶机连接串走环境变量；未设置或不可达则跳过）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Querying;
using DdraigBench.Core.Sessions;
using FreeSql;

namespace DdraigBench.Test;

public sealed class MultiDialectIntegrationTests
{
    private const string MySqlEnvVar = "DDRAIGBENCH_MYSQL";
    private const string PostgreSqlEnvVar = "DDRAIGBENCH_PG";

    [Fact]
    public Task MySql_runs_select_and_reads_databases() =>
        RunAsync(MySqlEnvVar, DataType.MySql, "MySQL/MariaDB");

    [Fact]
    public Task PostgreSql_runs_select_and_reads_databases() =>
        RunAsync(PostgreSqlEnvVar, DataType.PostgreSQL, "PostgreSQL");

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

        var databases = await new FreeSqlMetadataExplorer().GetDatabasesAsync(session);
        Assert.NotEmpty(databases);
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
