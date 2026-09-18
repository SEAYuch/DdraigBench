// DdraigBench — ConnectionProfile → IFreeSql 工厂（每连接单例）

using System.Collections.Concurrent;
using FreeSql;

namespace DdraigBench.Core.Connections;

public sealed class FreeSqlFactory : IDisposable
{
    private readonly ConcurrentDictionary<ConnectionProfile, IFreeSql> _instances = new();
    private bool _disposed;

    public IFreeSql GetOrCreate(ConnectionProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _instances.GetOrAdd(profile, Create);
    }

    public void Remove(ConnectionProfile profile)
    {
        if (_instances.TryRemove(profile, out var fsql))
        {
            fsql.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var fsql in _instances.Values)
        {
            fsql.Dispose();
        }
        _instances.Clear();
    }

    private static IFreeSql Create(ConnectionProfile profile) =>
        new FreeSqlBuilder()
            .UseConnectionString(profile.DataType, profile.ConnectionString)
            .UseAutoSyncStructure(false)
            .Build();
}
