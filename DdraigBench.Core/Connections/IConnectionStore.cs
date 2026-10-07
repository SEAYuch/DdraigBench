// DdraigBench — 连接清单存储抽象（壳层注入，便于 ViewModel 单测）

using DdraigBench.Core.Secrets;

namespace DdraigBench.Core.Connections;

/// <summary>
/// 连接清单的读写契约。实现是 <see cref="ConnectionStore"/>（JSON 文件 + 密钥库加密连接串）；
/// 壳层与 ViewModel 只依赖本接口，便于用内存实现做纯逻辑单测。
/// </summary>
public interface IConnectionStore
{
    /// <summary>实际使用的口令保护方式（UI 据 <see cref="ISecretProtector.IsEncrypted"/> 决定是否告警）。</summary>
    ISecretProtector Protector { get; }

    /// <summary>读取全部已保存连接；文件不存在时返回空清单。</summary>
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default);

    /// <summary>整份覆盖保存。</summary>
    Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default);
}