// DdraigBench — 生产用连接清单存储工厂（探测系统密钥库 + 落盘到用户配置目录）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Secrets;

namespace DdraigBench.Services;

/// <summary>
/// 生产实现：按平台挑最佳口令保护方式（<see cref="SecretProtectors.CreateDefaultAsync"/>），
/// 落盘到 <see cref="ConnectionStore.DefaultFilePath"/>。
/// </summary>
/// <remarks>
/// 整个创建过程（含密钥库探测）都在线程池上跑：macOS 的钥匙串调用是同步阻塞的原生调用，
/// 钥匙串被锁时可能等待授权（§2.8 规则 6），绝不能占着 UI 线程。
/// </remarks>
public sealed class SecretConnectionStoreFactory : IConnectionStoreFactory
{
    private readonly string? _filePath;

    public SecretConnectionStoreFactory(string? filePath = null)
    {
        _filePath = filePath;
    }

    public async Task<IConnectionStore> CreateAsync(CancellationToken ct = default)
    {
        var protector = await Task
            .Run(() => SecretProtectors.CreateDefaultAsync(ct), ct)
            .ConfigureAwait(false);

        return new ConnectionStore(_filePath ?? ConnectionStore.DefaultFilePath, protector);
    }
}