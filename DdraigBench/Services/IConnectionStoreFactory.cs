// DdraigBench — 连接清单存储工厂接口（供壳层注入）

using DdraigBench.Core.Connections;

namespace DdraigBench.Services;

/// <summary>
/// 创建 <see cref="IConnectionStore"/> 的工厂。之所以不是直接注入 store：口令保护方式要
/// <b>异步探测</b>系统密钥库（Windows DPAPI / macOS 钥匙串 / freedesktop Secret Service），
/// 且探测期间不得占用 UI 线程（§5）。故由实现把探测放到后台线程，壳层只管 await。
/// </summary>
public interface IConnectionStoreFactory
{
    Task<IConnectionStore> CreateAsync(CancellationToken ct = default);
}