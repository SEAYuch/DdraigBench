// DdraigBench — 会话生命周期与并发上限

using DdraigBench.Core.Connections;

namespace DdraigBench.Core.Sessions;

/// <summary>
/// 会话生命周期的唯一入口：打开 / 关闭 / 全部处置，并按上限约束「同时打开的会话数」。
/// <para>
/// 早期规划里就有这个名字，但一直没落地，会话语义散在 GUI 层：换连接时 `_session` 全靠手写
/// dispose，一旦哪条分支忘了关就变成连接泄漏（而且没人拦得住"同时开着两个会话"）。
/// 现在把上限与处置收到 Core，让约束可被单测，而不是靠人记。
/// </para>
/// </summary>
public interface ISessionManager : IAsyncDisposable
{
    /// <summary>当前活动会话（最近打开的那个）；没有打开任何会话时为 <c>null</c>。</summary>
    DbSession? Active { get; }

    /// <summary>同时允许打开的会话数上限。GUI 是单活动连接，默认 1。</summary>
    int MaxOpenSessions { get; }

    /// <summary>当前打开的会话数。</summary>
    int OpenCount { get; }

    /// <summary>当前打开的会话快照（顺序为打开顺序）。</summary>
    IReadOnlyList<DbSession> Sessions { get; }

    /// <summary>
    /// 打开新会话并让它成为活动会话。达到 <see cref="MaxOpenSessions"/> 时抛
    /// <see cref="InvalidOperationException"/>——宁可响亮失败，也不静默多开一个连接。
    /// </summary>
    Task<DbSession> OpenAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>关闭并处置指定会话（非活动会话也可以）；会话不属于本管理器时返回 <c>false</c>。</summary>
    Task<bool> CloseAsync(DbSession session);

    /// <summary>关闭并处置全部会话（幂等）。应用退出时调用。</summary>
    Task CloseAllAsync();
}
