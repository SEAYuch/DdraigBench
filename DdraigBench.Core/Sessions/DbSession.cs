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

    /// <summary>
    /// 线程归属观测缝（AGENTS §5：DB 调用不得跑在 UI 线程）。
    /// 仅测试赋值，用来断言「取租约 / 归还租约」实际跑在哪条线程上——
    /// <see cref="DbSession"/> 是 sealed、<see cref="LeaseRawAsync"/> 无法被子类覆写，
    /// 没有这条缝就只能靠「事务往返成功」间接兜底（README 已知限制 5）。
    /// 挂在实例上（非静态），测试之间互不干扰；生产路径恒为 null，只多一次 null 判断。
    /// </summary>
    internal Action<string>? WorkObserved { get; set; }

    public async ValueTask<ConnectionLease> LeaseRawAsync(CancellationToken ct = default)
    {
        WorkObserved?.Invoke("lease-acquire");

        var pool = Fsql.Ado.MasterPool
            ?? throw new InvalidOperationException("MasterPool 不可用：连接未配置");
        return new ConnectionLease(await pool.GetAsync(ct), WorkObserved);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Remove(Profile);
        return ValueTask.CompletedTask;
    }
}
