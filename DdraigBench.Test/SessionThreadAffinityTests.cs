// DdraigBench — 会话工作的线程归属回归（AGENTS §5：DB 调用不得跑在 UI 线程）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Sessions;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

/// <summary>
/// 已知限制 5 的回归：线程归属类修复此前没有红测对照——`DbSession` 是 sealed、
/// `LeaseRawAsync` 不可覆写，测试没法代理这条调用，只能靠「真 SQLite 事务往返成功」间接兜底。
/// 现在走 `DbSession.WorkObserved` 这条实例级观测缝，直接断言租约往来落在哪条线程上。
/// </summary>
public sealed class SessionThreadAffinityTests
{
    [Fact]
    public async Task Transaction_lease_traffic_never_runs_on_the_calling_thread()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        await using var session = new DbSession(
            new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);

        var observed = new List<(string Operation, int ThreadId)>();
        session.WorkObserved = operation => observed.Add((operation, Environment.CurrentManagedThreadId));

        var tab = new QueryTabViewModel("q", () => session, new StubFilePicker(), new StubExportDialog());

        var callerThreadId = 0;
        Exception? failure = null;

        // 自建线程冒充 UI 线程：它不属于线程池，故 Task.Run 里的租约往来必然落在别的线程上（确定性）
        var caller = new Thread(() =>
        {
            try
            {
                callerThreadId = Environment.CurrentManagedThreadId;
                tab.BeginTransactionAsync().GetAwaiter().GetResult();
                tab.CommitAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        caller.Start();
        Assert.True(caller.Join(TimeSpan.FromSeconds(30)), "事务往返未在 30 秒内完成");

        Assert.Null(failure);
        Assert.NotEqual(0, callerThreadId);

        // 取租约（BeginTransactionAsync）与归还租约（CommitAsync 的提交/回滚块）都真的发生过
        Assert.Contains(observed, o => o.Operation == "lease-acquire");
        Assert.Contains(observed, o => o.Operation == "lease-return");

        // 且没有任何一次落在调用线程上——去掉 QueryTabViewModel 里的 Task.Run 包裹即红
        Assert.DoesNotContain(observed, o => o.ThreadId == callerThreadId);
        await Task.CompletedTask;
    }
}
