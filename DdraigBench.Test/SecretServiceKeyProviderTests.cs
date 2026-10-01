// DdraigBench — Secret Service 主密钥提供器集成测试（真密钥库；无会话总线/服务则跳过）

using DdraigBench.Core.Secrets;

namespace DdraigBench.Test;

/// <summary>
/// 真机集成测试：访问本机会话总线上的 <c>org.freedesktop.secrets</c>。
/// 用唯一的 <c>application</c> 属性与真实条目隔离，测试结束删除自己写入的条目
/// （不污染用户 gnome-keyring/KWallet 中的真实凭据）。
/// </summary>
public sealed class SecretServiceKeyProviderTests
{
    private static bool OnSupportedPlatform => OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD();

    /// <summary>为用户真实凭据隔离而生成一个只属于本次测试的属性作用域。</summary>
    private static string NewTestApplication() => $"DdraigBench.Test.{Guid.NewGuid():N}";

    private static async Task<SecretServiceKeyProvider?> TryCreateAvailableProviderAsync(string application)
    {
        var provider = new SecretServiceKeyProvider(application);
        return await provider.IsAvailableAsync() ? provider : null;
    }

    [Fact]
    public async Task Availability_probe_never_throws()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("freedesktop Secret Service 仅 Linux/FreeBSD 可用");
            return;
        }

        // 无论有无密钥库都必须安全返回布尔值（供工厂回退判定），不得抛异常。
        var available = await new SecretServiceKeyProvider().IsAvailableAsync();

        Assert.True(available || !available);
    }

    [Fact]
    public async Task Creates_then_reuses_a_32_byte_master_key()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("freedesktop Secret Service 仅 Linux/FreeBSD 可用");
            return;
        }

        var application = NewTestApplication();
        if (await TryCreateAvailableProviderAsync(application) is not { } provider)
        {
            Assert.Skip("本机无可用 Secret Service（会话总线/默认集合缺失），跳过");
            return;
        }

        try
        {
            var first = await provider.GetOrCreateMasterKeyAsync();
            Assert.Equal(SecretKeyEnvelope.KeyLength, first.Length);

            // 用全新实例再取一次：应命中密钥库里的同一把密钥（而非重新生成）。
            var reloaded = await new SecretServiceKeyProvider(application).GetOrCreateMasterKeyAsync();
            Assert.Equal(first, reloaded);
        }
        finally
        {
            Assert.Equal(1, await provider.DeleteStoredKeysAsync());
        }
    }

    [Fact]
    public async Task Delete_removes_stored_key_and_clears_cache()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("freedesktop Secret Service 仅 Linux/FreeBSD 可用");
            return;
        }

        var application = NewTestApplication();
        if (await TryCreateAvailableProviderAsync(application) is not { } provider)
        {
            Assert.Skip("本机无可用 Secret Service（会话总线/默认集合缺失），跳过");
            return;
        }

        try
        {
            var original = await provider.GetOrCreateMasterKeyAsync();

            Assert.Equal(1, await provider.DeleteStoredKeysAsync());

            // 删除后再取会生成一把新的（与前一把不同）。
            var regenerated = await provider.GetOrCreateMasterKeyAsync();
            Assert.Equal(SecretKeyEnvelope.KeyLength, regenerated.Length);
            Assert.NotEqual(original, regenerated);
        }
        finally
        {
            await provider.DeleteStoredKeysAsync();
        }
    }

    [Fact]
    public async Task Secret_protector_round_trips_through_real_keyring()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("freedesktop Secret Service 仅 Linux/FreeBSD 可用");
            return;
        }

        var application = NewTestApplication();
        if (await TryCreateAvailableProviderAsync(application) is not { } provider)
        {
            Assert.Skip("本机无可用 Secret Service（会话总线/默认集合缺失），跳过");
            return;
        }

        try
        {
            var protector = new SecretServiceSecretProtector(provider);
            const string plain = "Server=127.0.0.1;Port=5432;Password=pw-from-keyring;Database=app";

            var cipher = await protector.ProtectAsync(plain);

            Assert.DoesNotContain("pw-from-keyring", cipher);
            Assert.Equal(plain, await protector.UnprotectAsync(cipher));
        }
        finally
        {
            await provider.DeleteStoredKeysAsync();
        }
    }

    [Fact]
    public async Task Default_factory_falls_back_or_upgrades_on_this_machine()
    {
        // 工厂行为：Windows → DPAPI；能连密钥库 → Secret Service；否则 → 明文回退。
        var protector = await SecretProtectors.CreateDefaultAsync();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiSecretProtector>(protector);
            Assert.True(protector.IsEncrypted);
        }
        else if (OnSupportedPlatform)
        {
            Assert.True(
                protector is SecretServiceSecretProtector or PlainTextSecretProtector,
                $"非预期类型：{protector.GetType().Name}");
            Assert.Equal(protector is SecretServiceSecretProtector, protector.IsEncrypted);
        }
    }
}
