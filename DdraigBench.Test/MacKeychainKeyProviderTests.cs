// DdraigBench — macOS Keychain 主密钥提供器集成测试（真钥匙串；非 macOS 跳过）

using System.Diagnostics;
using System.Security.Cryptography;
using DdraigBench.Core.Secrets;

namespace DdraigBench.Test;

/// <summary>
/// 真机集成测试：访问本机登录钥匙串（login keychain）的通用密码条目。
/// 每次测试用唯一的 <c>service</c> 属性与真实条目隔离，结束时删除自己写入的条目
/// （不污染用户钥匙串里的真实凭据），并全程 <c>suppressUi</c> 禁止弹授权框——CI/无人值守环境里
/// 授权框是不可见的挂死。
/// </summary>
public sealed class MacKeychainKeyProviderTests
{
    private static bool OnSupportedPlatform => OperatingSystem.IsMacOS();

    private static readonly TimeSpan CliTimeout = TimeSpan.FromSeconds(15);

    /// <summary>为用户真实凭据隔离而生成一个只属于本次测试的条目 scope。</summary>
    private static string NewTestService() => $"DdraigBench.Test.{Guid.NewGuid():N}";

    private static MacKeychainKeyProvider NewProvider(string service) => new(service, suppressUi: true);

    [Fact]
    public async Task Availability_probe_never_throws()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        // 无论钥匙串是否可用都必须安全返回布尔值（供工厂回退判定），不得抛异常。
        var available = await NewProvider(NewTestService()).IsAvailableAsync();

        Assert.True(available || !available);
    }

    [Fact]
    public async Task Creates_then_reuses_a_32_byte_master_key()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var service = NewTestService();
        var provider = NewProvider(service);

        try
        {
            var first = await provider.GetOrCreateMasterKeyAsync();
            Assert.Equal(SecretKeyEnvelope.KeyLength, first.Length);

            // 用全新实例再取一次：应命中钥匙串里的同一把密钥（而非重新生成）。
            var reloaded = await NewProvider(service).GetOrCreateMasterKeyAsync();
            Assert.Equal(first, reloaded);

            // 同实例的内存缓存也必须生效（不再访问钥匙串）。
            Assert.Equal(first, await provider.GetOrCreateMasterKeyAsync());
        }
        finally
        {
            await provider.ResetMasterKeyAsync();
        }
    }

    [Fact]
    public async Task Reset_removes_item_and_clears_cache()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var service = NewTestService();
        var provider = NewProvider(service);

        try
        {
            var original = await provider.GetOrCreateMasterKeyAsync();

            Assert.True(await provider.ResetMasterKeyAsync());

            // 删除后再取会生成一把新的（与前一把不同）。
            var regenerated = await provider.GetOrCreateMasterKeyAsync();
            Assert.Equal(SecretKeyEnvelope.KeyLength, regenerated.Length);
            Assert.NotEqual(original, regenerated);
        }
        finally
        {
            await provider.ResetMasterKeyAsync();
        }
    }

    [Fact]
    public async Task Secret_protector_round_trips_through_real_keychain()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var service = NewTestService();
        var provider = NewProvider(service);

        try
        {
            var protector = new MacKeychainSecretProtector(provider);
            const string plain = "Server=127.0.0.1;Port=5432;Password=pw-from-keychain;Database=app";

            var cipher = await protector.ProtectAsync(plain);

            Assert.DoesNotContain("pw-from-keychain", cipher);

            // 全新实例（内存缓存为空）也要能解开：密钥确实落在钥匙串而非只活在内存里。
            var reloaded = await new MacKeychainSecretProtector(NewProvider(service)).UnprotectAsync(cipher);
            Assert.Equal(plain, reloaded);
        }
        finally
        {
            await provider.ResetMasterKeyAsync();
        }
    }

    [Fact]
    public async Task Corrupted_item_is_reported_instead_of_silently_regenerated()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var service = NewTestService();
        var provider = NewProvider(service);

        try
        {
            await provider.GetOrCreateMasterKeyAsync();

            // 模拟"条目被改写/损坏"：用 SecItemUpdate 把 32 字节主密钥覆盖成 4 字节短数据。
            var update = MacSecurityFramework.TryUpdateData(
                service,
                MacKeychainKeyProvider.DefaultAccount,
                [1, 2, 3, 4],
                suppressUi: true);
            Assert.Equal(MacSecurityFramework.StatusSuccess, update);

            // 必须明确报错，绝不静默重生成——否则旧连接串的密文会永久解不开。
            await Assert.ThrowsAsync<CryptographicException>(
                async () => await NewProvider(service).GetOrCreateMasterKeyAsync());
        }
        finally
        {
            await provider.ResetMasterKeyAsync();
        }
    }

    /// <summary>
    /// 交叉验证：条目真的以系统认可的服务名/账号落在 genp 类里（用户能在「钥匙串访问」中搜索到并删除）。
    /// 用 Apple 自己的 <c>security</c> 工具查询——它只认框架常量，与我们的实现互为独立参照。
    /// </summary>
    [Fact]
    public async Task Item_is_discoverable_by_the_security_cli()
    {
        if (!OnSupportedPlatform)
        {
            Assert.Skip("macOS Keychain 仅在 macOS 可用");
            return;
        }

        var service = NewTestService();
        var provider = NewProvider(service);

        try
        {
            await provider.GetOrCreateMasterKeyAsync();

            var found = TryRunSecurity(
                "find-generic-password",
                "-s",
                service,
                "-a",
                MacKeychainKeyProvider.DefaultAccount);
            if (found is null)
            {
                Assert.Skip("本机无 security 工具（钥匙串访问器），跳过交叉验证");
                return;
            }

            Assert.Equal(0, found.Value.ExitCode);
            Assert.Contains(service, found.Value.Output, StringComparison.Ordinal);
            Assert.Contains(MacKeychainKeyProvider.DefaultAccount, found.Value.Output, StringComparison.Ordinal);
            Assert.Contains(MacKeychainKeyProvider.ItemLabel, found.Value.Output, StringComparison.Ordinal);
        }
        finally
        {
            await provider.ResetMasterKeyAsync();
        }
    }

    /// <summary>
    /// 启动 <c>security</c> 子进程并限时回收（不让不可见的授权框把测试挂死）。
    /// 工具不存在或超时返回 null。
    /// </summary>
    private static (int ExitCode, string Output)? TryRunSecurity(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            if (!process.WaitForExit((int)CliTimeout.TotalMilliseconds))
            {
                process.Kill();
                return null;
            }

            // 输出量极小（几条属性），此时读取不会因管道回压而死锁。
            return (process.ExitCode, process.StandardOutput.ReadToEnd());
        }
    }
}