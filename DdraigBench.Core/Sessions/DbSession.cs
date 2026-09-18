// DdraigBench — 一个打开的连接标签 = 一个会话

using DdraigBench.Core.Connections;
using FreeSql;

namespace DdraigBench.Core.Sessions;

public sealed class DbSession : IAsyncDisposable
{
    private readonly FreeSqlFactory _factory;

    public DbSession(ConnectionProfile profile, FreeSqlFactory factory)
    {
        Profile = profile;
        _factory = factory;
        Fsql = factory.GetOrCreate(profile);
    }

    public ConnectionProfile Profile { get; }

    public IFreeSql Fsql { get; }

    public async ValueTask<ConnectionLease> LeaseRawAsync(CancellationToken ct = default)
    {
        var pool = Fsql.Ado.MasterPool
            ?? throw new InvalidOperationException("MasterPool 不可用：连接未配置");
        return new ConnectionLease(await pool.GetAsync(ct));
    }

    public ValueTask DisposeAsync()
    {
        _factory.Remove(Profile);
        return ValueTask.CompletedTask;
    }
}
