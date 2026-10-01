// DdraigBench — 主密钥提供器抽象（密钥库存放信封加密用的对称密钥）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 提供一把用于 <see cref="SecretKeyEnvelope"/> 的 32 字节对称主密钥：不存在则生成并持久化到系统密钥库，
/// 已存在则取回。密钥库实现各自决定"存哪里"（Linux 为 freedesktop Secret Service）。
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>用于诊断/显示的实现名（如「freedesktop Secret Service」）。</summary>
    string Name { get; }

    /// <summary>
    /// 取回主密钥；密钥库中没有时生成一把并写入。返回值长度必为 <see cref="SecretKeyEnvelope.KeyLength"/>。
    /// 实现应缓存主密钥，避免每次加解密都访问密钥库。
    /// </summary>
    ValueTask<byte[]> GetOrCreateMasterKeyAsync(CancellationToken cancellationToken = default);
}
