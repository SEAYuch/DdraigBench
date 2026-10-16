// DdraigBench — 连接租借句柄（MasterPool 取还语义）

using System.Data.Common;
using FreeSql.Internal.ObjectPool;

namespace DdraigBench.Core.Sessions;

public sealed class ConnectionLease : IDisposable, IAsyncDisposable
{
    private readonly Object<DbConnection> _leased;
    private readonly Action<string>? _observed;

    internal ConnectionLease(Object<DbConnection> leased, Action<string>? observed = null)
    {
        _leased = leased;
        _observed = observed;
    }

    public DbConnection Connection => _leased.Value;

    public void Dispose()
    {
        _observed?.Invoke("lease-return");
        _leased.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _observed?.Invoke("lease-return");
        _leased.Dispose();
        return ValueTask.CompletedTask;
    }
}
