// DdraigBench — 连接对话框（View 层能力，注入 ViewModel）

using DdraigBench.Core.Connections;

namespace DdraigBench.Services;

public interface IConnectionDialog
{
    Task<ConnectionProfile?> ShowAsync();
}
