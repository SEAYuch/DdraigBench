// DdraigBench — 信封加密单测（AES-256-GCM，纯逻辑、无密钥库依赖）

using System.Security.Cryptography;
using System.Text;
using DdraigBench.Core.Secrets;

namespace DdraigBench.Test;

public sealed class SecretKeyEnvelopeTests
{
    private static readonly byte[] Key = Enumerable.Range(0, SecretKeyEnvelope.KeyLength).Select(i => (byte)i).ToArray();

    [Fact]
    public void Round_trips_connection_string()
    {
        const string plain = "Server=db;Port=3306;Password=s3cret-pw;Database=app";

        var cipher = SecretKeyEnvelope.Encrypt(Key, plain);

        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, SecretKeyEnvelope.Decrypt(Key, cipher));
    }

    [Theory]
    [InlineData("")]
    [InlineData("中文口令 with émojis 🐉 and \n newlines")]
    [InlineData("Data Source=/tmp/x.db")]
    public void Round_trips_edge_cases(string plain)
    {
        var cipher = SecretKeyEnvelope.Encrypt(Key, plain);
        Assert.Equal(plain, SecretKeyEnvelope.Decrypt(Key, cipher));
    }

    [Fact]
    public void Ciphertext_is_not_the_plaintext_in_base64()
    {
        const string plain = "Password=s3cret-pw";

        var cipher = SecretKeyEnvelope.Encrypt(Key, plain);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cipher));

        Assert.DoesNotContain("s3cret-pw", decoded);
    }

    [Fact]
    public void Same_plaintext_produces_different_ciphertext()
    {
        // 随机 nonce：相同明文两次加密结果必须不同（否则泄露"两条连接串相同"）
        Assert.NotEqual(SecretKeyEnvelope.Encrypt(Key, "x"), SecretKeyEnvelope.Encrypt(Key, "x"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var payload = Convert.FromBase64String(SecretKeyEnvelope.Encrypt(Key, "Server=db;Password=pw"));
        payload[^1] ^= 0xFF; // 翻转密文最后一字节

        Assert.ThrowsAny<CryptographicException>(() =>
            SecretKeyEnvelope.Decrypt(Key, Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Tampered_tag_is_rejected()
    {
        var payload = Convert.FromBase64String(SecretKeyEnvelope.Encrypt(Key, "Server=db;Password=pw"));
        payload[1 + 12] ^= 0x01; // 翻转认证标签首字节

        Assert.ThrowsAny<CryptographicException>(() =>
            SecretKeyEnvelope.Decrypt(Key, Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Wrong_key_is_rejected()
    {
        var cipher = SecretKeyEnvelope.Encrypt(Key, "Server=db;Password=pw");
        var other = new byte[SecretKeyEnvelope.KeyLength];
        other[0] = 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => SecretKeyEnvelope.Decrypt(other, cipher));
    }

    [Fact]
    public void Wrong_key_length_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SecretKeyEnvelope.Encrypt(new byte[16], "x"));
        Assert.Throws<ArgumentException>(() => SecretKeyEnvelope.Decrypt(new byte[16], "AAAA"));
    }

    [Fact]
    public void Invalid_base64_is_rejected()
    {
        Assert.Throws<CryptographicException>(() => SecretKeyEnvelope.Decrypt(Key, "not base64!!!"));
    }

    [Fact]
    public void Unsupported_version_is_reported()
    {
        var payload = Convert.FromBase64String(SecretKeyEnvelope.Encrypt(Key, "x"));
        payload[0] = 99;

        var error = Assert.Throws<CryptographicException>(() =>
            SecretKeyEnvelope.Decrypt(Key, Convert.ToBase64String(payload)));
        Assert.Contains("99", error.Message);
    }

    [Fact]
    public void Truncated_payload_is_rejected()
    {
        Assert.Throws<CryptographicException>(() => SecretKeyEnvelope.Decrypt(Key, Convert.ToBase64String([1, 2, 3])));
    }
}
