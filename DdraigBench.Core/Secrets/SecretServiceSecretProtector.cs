// DdraigBench — Linux/Unix 口令保护器（freedesktop Secret Service + AES-GCM 信封加密）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 非 Windows 平台的口令保护器：主密钥存于系统密钥库（<see cref="IMasterKeyProvider"/>），
/// 连接串用 <see cref="SecretKeyEnvelope"/>（AES-256-GCM）加密后落盘。
/// </summary>
/// <remarks>
/// 与 <see cref="DpapiSecretProtector"/> 对位：构造不触密钥库，首次加解密才访问并缓存主密钥。
/// 仅 Linux/FreeBSD 可用；其它平台由 <see cref="SecretProtectors.CreateDefaultAsync"/> 选择各自实现。
/// </remarks>
public sealed class SecretServiceSecretProtector(IMasterKeyProvider keyProvider) : ISecretProtector
{
    /// <summary>写入配置文件的保护器名，用于读回历史文件时挑实现。</summary>
    public const string ProtectorName = "freedesktop-secret-service";

    private readonly IMasterKeyProvider _keyProvider = keyProvider
        ?? throw new ArgumentNullException(nameof(keyProvider));

    public string Name => ProtectorName;

    public bool IsEncrypted => true;

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

    private static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            throw new PlatformNotSupportedException(
                "freedesktop Secret Service 仅在 Linux/FreeBSD 上可用（Windows 用 DPAPI，见 DpapiSecretProtector）。");
        }
    }
}
