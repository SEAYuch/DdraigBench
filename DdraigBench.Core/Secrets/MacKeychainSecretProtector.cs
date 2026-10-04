// DdraigBench — macOS 口令保护器（钥匙串主密钥 + AES-GCM 信封加密）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// macOS 的信封保护器：主密钥存于系统钥匙串（<see cref="MacKeychainKeyProvider"/>），
/// 连接串用 <see cref="SecretKeyEnvelope"/>（AES-256-GCM）加密后落盘。
/// </summary>
/// <remarks>
/// 与 <see cref="SecretServiceSecretProtector"/> 对位：同样只在首次加解密时访问密钥库并缓存主密钥。
/// 仅有 macOS 可用；其它平台由 <see cref="SecretProtectors.CreateDefaultAsync"/> 选择各自实现。
/// </remarks>
public sealed class MacKeychainSecretProtector : EnvelopeSecretProtector
{
    /// <summary>写入配置文件的保护器名，用于读回历史文件时挑实现。</summary>
    public const string ProtectorName = "macos-keychain";

    public MacKeychainSecretProtector(IMasterKeyProvider keyProvider)
        : base(keyProvider)
    {
    }

    public override string Name => ProtectorName;

    public override bool IsEncrypted => true;

    protected override void EnsureSupportedPlatform()
    {
        MacSecurityFramework.EnsureSupportedPlatform();
    }
}