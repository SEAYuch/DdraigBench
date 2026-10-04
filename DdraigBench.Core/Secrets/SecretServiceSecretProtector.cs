// DdraigBench — Linux/FreeBSD 口令保护器（freedesktop Secret Service + AES-GCM 信封加密）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 非 Windows 平台的信封保护器实现：主密钥存于 freedesktop Secret Service，
/// 连接串用 <see cref="SecretKeyEnvelope"/>（AES-256-GCM）加密后落盘。
/// </summary>
/// <remarks>
/// 构造不触密钥库，首次加解密才访问并缓存主密钥（缓存由 <see cref="IMasterKeyProvider"/> 负责）。
/// 仅 Linux/FreeBSD 可用；其它平台由 <see cref="SecretProtectors.CreateDefaultAsync"/> 选择各自实现。
/// </remarks>
public sealed class SecretServiceSecretProtector : EnvelopeSecretProtector
{
    /// <summary>写入配置文件的保护器名，用于读回历史文件时挑实现。</summary>
    public const string ProtectorName = "freedesktop-secret-service";

    public SecretServiceSecretProtector(IMasterKeyProvider keyProvider)
        : base(keyProvider)
    {
    }

    public override string Name => ProtectorName;

    public override bool IsEncrypted => true;

    protected override void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            throw new PlatformNotSupportedException(
                "freedesktop Secret Service 仅在 Linux/FreeBSD 上可用（Windows 用 DPAPI，macOS 用钥匙串，见 MacKeychainSecretProtector）。");
        }
    }
}