// DdraigBench — 界面偏好存储契约（ui-settings.json：主题 + 上次连接）

namespace DdraigBench.Services;

/// <summary>
/// 界面偏好的读写。<b>刻意做成「整体读写」而不是按字段读写</b>：
/// 主题与「上次连接的连接名」同住一个 JSON 文件，若拆成两个只写自己那半边的接口，
/// 后写的那次会把另一半抹掉（例如切主题会清掉上次连接名）。
/// </summary>
public interface IUiSettingsStore
{
    /// <summary>读偏好。文件不存在 / 损坏 / 无权限时返回默认值，绝不抛（启动路径靠它兜底）。</summary>
    Task<UiSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>覆盖写入整份偏好。</summary>
    Task SaveAsync(UiSettings settings, CancellationToken cancellationToken = default);
}
