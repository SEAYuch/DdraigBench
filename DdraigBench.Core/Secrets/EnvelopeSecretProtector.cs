// DdraigBench — 信封加密保护器基类（主密钥在系统密钥库，连接串密文在配置文件）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// "一把主密钥 + <see cref="SecretKeyEnvelope"/>（AES-256-GCM）"这一族保护器的共同实现：
/// 主密钥由 <see cref="IMasterKeyProvider"/> 落在系统密钥库，连接串密文留在配置文件。
/// 子类只需给出标识名与平台守卫（Windows 用 DPAPI，无需本基类；Linux/macOS 走本基类）。
/// </summary>
/// <remarks>
/// 为什么不逐条把连接串存进密钥库：① 避免条目泛滥与重命名/删除后的孤儿条目；
/// ② 配置文件自描述、可整份备份；③ 加密逻辑纯函数化，可用假主密钥在无密钥库环境完整单测。
/// </remarks>
public abstract class EnvelopeSecretProtector(IMasterKeyProvider keyProvider) : ISecretProtector
{
    private readonly IMasterKeyProvider _keyProvider = keyProvider
        ?? throw new ArgumentNullException(nameof(keyProvider));

    public abstract string Name { get; }

    public abstract bool IsEncrypted { get; }

    public async ValueTask<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        EnsureSupportedPlatform();

        var key = await _keyProvider.GetOrCreateMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        return SecretKeyEnvelope.Encrypt(key, plainText);
    }

    public async ValueTask<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        EnsureSupportedPlatform();

        var key = await _keyProvider.GetOrCreateMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        return SecretKeyEnvelope.Decrypt(key, protectedText);
    }

    /// <summary>平台守卫：不在目标平台上抛 <see cref="PlatformNotSupportedException"/>。</summary>
    protected abstract void EnsureSupportedPlatform();
}