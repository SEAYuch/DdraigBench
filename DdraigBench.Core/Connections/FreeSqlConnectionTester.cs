// DdraigBench — IConnectionTester 的 FreeSql 实现（临时 IFreeSql，不污染工厂缓存）

using FreeSql;

namespace DdraigBench.Core.Connections;

public sealed class FreeSqlConnectionTester : IConnectionTester
{
    public Task<string?> TestAsync(ConnectionProfile profile, CancellationToken ct = default) =>
        Task.Run<string?>(() =>
        {
            try
            {
                using var fsql = new FreeSqlBuilder()
                    .UseConnectionString(profile.DataType, profile.ConnectionString)
                    .UseAutoSyncStructure(false)
                    .Build();

                return fsql.Ado.ExecuteConnectTest() ? null : "连接测试未通过（ExecuteConnectTest 返回 false）";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }, ct);
}
