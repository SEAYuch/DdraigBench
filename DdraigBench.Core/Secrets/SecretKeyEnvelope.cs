// DdraigBench — 对称信封加密（AES-256-GCM）

using System.Security.Cryptography;
using System.Text;

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 用一把 32 字节主密钥对连接串做 AES-256-GCM 认证加密。密文自描述：
/// <c>[版本:1][nonce:12][标签:16][密文:n]</c>，整体 Base64 后落盘。
/// 主密钥本身存于系统密钥库（见 <see cref="IMasterKeyProvider"/>），故配置文件里不含密钥本体。
/// </summary>
/// <remarks>
/// 之所以不把连接串逐条存进密钥库：① 避免条目泛滥与重命名/删除后的孤儿条目；
/// ② 文件自描述、可整份备份；③ 加密逻辑纯函数化，可用假主密钥在无密钥库环境完整单测。
/// </remarks>
internal static class SecretKeyEnvelope
{
    private const byte Version = 1;
    private const int KeySize = 32;   // AES-256
    private const int NonceSize = 12; // GCM 推荐 96-bit nonce
    private const int TagSize = 16;   // 128-bit 认证标签

    /// <summary>主密钥长度（字节）；<see cref="IMasterKeyProvider"/> 必须返回此长度的密钥。</summary>
    public static int KeyLength => KeySize;

    public static bool IsWellFormedKey(byte[]? key) => key is { Length: KeySize };

    public static string Encrypt(byte[] key, string plainText)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plainText);
        if (!IsWellFormedKey(key))
        {
            throw new ArgumentException($"主密钥必须为 {KeySize} 字节，实际 {key.Length} 字节", nameof(key));
        }

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plainBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[1 + NonceSize + TagSize + cipher.Length];
        payload[0] = Version;
        nonce.CopyTo(payload, 1);
        tag.CopyTo(payload, 1 + NonceSize);
        cipher.CopyTo(payload, 1 + NonceSize + TagSize);

        return Convert.ToBase64String(payload);
    }

    public static string Decrypt(byte[] key, string protectedText)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(protectedText);
        if (!IsWellFormedKey(key))
        {
            throw new ArgumentException($"主密钥必须为 {KeySize} 字节，实际 {key.Length} 字节", nameof(key));
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("密文不是合法的 Base64，可能已损坏。", ex);
        }

        if (payload.Length < 1 + NonceSize + TagSize)
        {
            throw new CryptographicException("密文长度不足，无法解析信封头部。");
        }

        if (payload[0] != Version)
        {
            throw new CryptographicException($"不支持的信封版本：{payload[0]}（本程序支持版本 {Version}）。");
        }

        var nonce = payload.AsSpan(1, NonceSize);
        var tag = payload.AsSpan(1 + NonceSize, TagSize);
        var cipher = payload.AsSpan(1 + NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        // AesGcm 在认证失败时抛 AuthenticationTagMismatchException（CryptographicException 子类）。
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
