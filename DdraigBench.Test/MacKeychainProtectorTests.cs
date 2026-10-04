// DdraigBench — macOS 钥匙串保护器单测（用假主密钥提供器，无钥匙串依赖）

using System.Security.Cryptography;
using DdraigBench.Core.Secrets;

namespace DdraigBench.Test;

public sealed class MacKeychainProtectorTests
{
    private static bool OnSupportedPlatform => OperatingSystem.IsMacOS();

    [Fact]
    public void Advertises_encryption_and_stable_name()
    {
        var protector = new MacKeychainSecretProtector(new FakeMasterKeyProvider());

        Assert.Equal("macos-keychain", protector.Name);
        Assert.Equal(MacKeychainSecretProtector.ProtectorName, protector.Name);
        Assert.True(protector.IsEncrypted);
        Assert.Equal("macos-keychain", SecretProtectors.CreateByName(protector.Name).Name);
    }

    [Fact]
    public async Task Round_trips_connection_string()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var protector = new MacKeychainSecretProtector(new FakeMasterKeyProvider());
        const string plain = "Server=db;Port=3306;Password=s3cret-pw;Database=app";

        var cipher = await protector.ProtectAsync(plain);

        Assert.NotEqual(plain, cipher);
        Assert.DoesNotContain("s3cret-pw", cipher);
        Assert.Equal(plain, await protector.UnprotectAsync(cipher));
    }

    [Fact]
    public async Task Same_plaintext_produces_different_ciphertext()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var protector = new MacKeychainSecretProtector(new FakeMasterKeyProvider());

        Assert.NotEqual(await protector.ProtectAsync("x"), await protector.ProtectAsync("x"));
    }

    [Fact]
    public async Task Unprotect_with_different_key_fails()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var cipher = await new MacKeychainSecretProtector(new FakeMasterKeyProvider()).ProtectAsync("Server=db;Password=pw");
        var other = new MacKeychainSecretProtector(new FakeMasterKeyProvider());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => other.UnprotectAsync(cipher).AsTask());
    }

    [Fact]
    public async Task Reuses_provider_key_across_calls()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var provider = new FakeMasterKeyProvider();
        var protector = new MacKeychainSecretProtector(provider);

        var first = await protector.ProtectAsync("a");
        var second = await protector.ProtectAsync("b");

        Assert.Equal("a", await protector.UnprotectAsync(first));
        Assert.Equal("b", await protector.UnprotectAsync(second));
        Assert.Equal(4, provider.CallCount); // 2 次加密 + 2 次解密，共用同一把假密钥
    }

    [Fact]
    public async Task Throws_on_unsupported_platform()
    {
        if (OnSupportedPlatform)
        {
            Assert.Skip("本断言仅在非 macOS 平台有意义");
            return;
        }

        var protector = new MacKeychainSecretProtector(new FakeMasterKeyProvider());

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => protector.ProtectAsync("x").AsTask());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => protector.UnprotectAsync("x").AsTask());
    }

    [Fact]
    public async Task Unsupported_platform_probe_reports_unavailable_instead_of_throwing()
    {
        if (OnSupportedPlatform)
        {
            Assert.Skip("本断言仅在非 macOS 平台有意义");
            return;
        }

        Assert.False(await new MacKeychainKeyProvider().IsAvailableAsync());
        Assert.False(await new MacKeychainKeyProvider().ResetMasterKeyAsync());
    }
}