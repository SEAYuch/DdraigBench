// DdraigBench — 连接租借句柄（MasterPool 取还语义）

using System.Data.Common;
using FreeSql.Internal.ObjectPool;

namespace DdraigBench.Core.Sessions;

public sealed class ConnectionLease : IDisposable, IAsyncDisposable
{
    private readonly Object<DbConnection> _leased;

    internal ConnectionLease(Object<DbConnection> leased) => _leased = leased;

    public DbConnection Connection => _leased.Value;

    public void Dispose() => _leased.Dispose();

    public ValueTask DisposeAsync()
    {
        _leased.Dispose();
        return ValueTask.CompletedTask;
    }
}
