// DdraigBench — SessionManager：会话上限、活动会话与确定性处置

using DdraigBench.Core.Connections;

namespace DdraigBench.Core.Sessions;

/// <summary>
/// <see cref="ISessionManager"/> 的默认实现。所有状态操作都在同一把锁里做，保证并发调用
/// （多个 async 分支同时连/断）下"数量上限"与"活动会话"不会错乱。
/// </summary>
public sealed class SessionManager : ISessionManager
{
    private readonly FreeSqlFactory _factory;
    private readonly List<DbSession> _sessions = [];
    private readonly object _gate = new();
    private bool _disposed;

    /// <param name="factory">会话共用的 IFreeSql 工厂（每连接单例）。</param>
    /// <param name="maxOpenSessions">同时允许打开的会话数；GUI 单活动连接为 1。</param>
    public SessionManager(FreeSqlFactory factory, int maxOpenSessions = 1)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (maxOpenSessions < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOpenSessions), maxOpenSessions, "会话并发上限至少为 1。");
        }

        _factory = factory;
        MaxOpenSessions = maxOpenSessions;
    }

    public int MaxOpenSessions { get; }

    public DbSession? Active
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count == 0 ? null : _sessions[^1];
            }
        }
    }

    public int OpenCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    public IReadOnlyList<DbSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.ToArray();
            }
        }
    }

    public Task<DbSession> OpenAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.Count >= MaxOpenSessions)
            {
                // 上限不是摆设：真的开第二个就报错，而不是悄悄泄漏一个连接。
                throw new InvalidOperationException(
                    $"会话并发上限为 {MaxOpenSessions}，当前已打开 {_sessions.Count} 个；请先关闭不再使用的会话。");
            }

            var session = new DbSession(profile, _factory);
            _sessions.Add(session);
            return Task.FromResult(session);
        }
    }

    public Task<bool> CloseAsync(DbSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_gate)
        {
            if (!_sessions.Remove(session))
            {
                return Task.FromResult(false);
            }
        }

        // 处置放在锁外：DbSession.DisposeAsync 会连带释放 IFreeSql 的连接池，
        // 那是外部资源清理，不该占着管理器的锁。
        return DisposeAndReportAsync(session);
    }

    public Task CloseAllAsync()
    {
        DbSession[] closing;
        lock (_gate)
        {
            closing = _sessions.ToArray();
            _sessions.Clear();
        }

        return DisposeAllAsync(closing);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await CloseAllAsync();
    }

    private static async Task<bool> DisposeAndReportAsync(DbSession session)
    {
        await session.DisposeAsync();
        return true;
    }

    private static async Task DisposeAllAsync(IReadOnlyList<DbSession> sessions)
    {
        // 后开先关：让最后建起来的会话先释放，和"连接切换"的人类直觉一致。
        for (var i = sessions.Count - 1; i >= 0; i--)
        {
            await sessions[i].DisposeAsync();
        }
    }
}
