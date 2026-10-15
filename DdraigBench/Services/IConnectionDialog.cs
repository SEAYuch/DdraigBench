// DdraigBench — 连接对话框（View 层能力，注入 ViewModel）

using DdraigBench.Core.Connections;

namespace DdraigBench.Services;

public interface IConnectionDialog
{
    /// <summary>
    /// 打开连接对话框。<paramref name="existing"/> 非空时进入「编辑已保存连接」模式（回填字段、沿用原名）。
    /// </summary>
    Task<ConnectionProfile?> ShowAsync(ConnectionProfile? existing = null);
}
