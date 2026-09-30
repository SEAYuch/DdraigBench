// DdraigBench — 连接测试（供对话框调用；返回 null 表示成功）

namespace DdraigBench.Core.Connections;

public interface IConnectionTester
{
    Task<string?> TestAsync(ConnectionProfile profile, CancellationToken ct = default);
}
