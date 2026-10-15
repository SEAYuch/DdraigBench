// DdraigBench — 密钥库回退说明单测（原因分类 + 用户可读文案；纯逻辑，无需任何密钥库即可跑）

using DdraigBench.Core.Secrets;

namespace DdraigBench.Test;

public sealed class SecretStoreFallbackTests
{
    [Theory]
    [InlineData(-128, SecretStoreFallbackReason.UserDeclined)]              // errSecUserCanceled
    [InlineData(-25291, SecretStoreFallbackReason.KeyStoreMissing)]         // errSecNotAvailable
    [InlineData(-25293, SecretStoreFallbackReason.AccessDenied)]            // errSecAuthFailed
    [InlineData(-25308, SecretStoreFallbackReason.Locked)]                  // errSecInteractionNotAllowed
    [InlineData(-34018, SecretStoreFallbackReason.MissingEntitlement)]      // errSecMissingEntitlement
    [InlineData(-25300, SecretStoreFallbackReason.Unknown)]                 // errSecItemNotFound：探测里算"可用"
    [InlineData(0, SecretStoreFallbackReason.Unknown)]                      // errSecSuccess：不是失败
    [InlineData(-99999, SecretStoreFallbackReason.Unknown)]
    public void Mac_status_codes_map_to_their_reason(int status, SecretStoreFallbackReason expected) =>
        Assert.Equal(expected, SecretStoreFallback.FromMacStatus(status));

    [Fact]
    public void Every_reason_has_a_non_empty_description()
    {
        foreach (var reason in Enum.GetValues<SecretStoreFallbackReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SecretStoreFallback.Describe(reason)), $"{reason} 的文案为空");
        }
    }

    [Theory]
    [InlineData(SecretStoreFallbackReason.AccessDenied, "钥匙串访问")]
    [InlineData(SecretStoreFallbackReason.Locked, "解锁")]
    [InlineData(SecretStoreFallbackReason.MissingEntitlement, "签名")]
    [InlineData(SecretStoreFallbackReason.ServiceUnreachable, "gnome-keyring")]
    [InlineData(SecretStoreFallbackReason.UserDeclined, "允许")]
    public void Actionable_reasons_tell_the_user_what_to_do(SecretStoreFallbackReason reason, string action) =>
        Assert.Contains(action, SecretStoreFallback.Describe(reason));

    [Fact]
    public void Unknown_reason_falls_back_to_the_platform_detail()
    {
        var text = SecretStoreFallback.Describe(SecretStoreFallbackReason.Unknown, "钥匙串授权校验失败（OSStatus -25293）");

        Assert.Contains("OSStatus -25293", text);
    }

    [Fact]
    public void Unknown_reason_without_a_detail_still_reads_as_a_sentence() =>
        Assert.Equal("系统密钥库不可用。", SecretStoreFallback.Describe(SecretStoreFallbackReason.Unknown, "  "));

    [Fact]
    public void Known_reason_does_not_leak_the_raw_status_instead_of_the_advice()
    {
        // 已知原因自带的"下一步动作"比状态码有用；把 detail 混进去只会让用户看到一串数字。
        var text = SecretStoreFallback.Describe(SecretStoreFallbackReason.AccessDenied, "OSStatus -25293");

        Assert.DoesNotContain("-25293", text);
    }

    [Fact]
    public void Platform_guard_failures_are_classified_as_unsupported_platform() =>
        Assert.Equal(
            SecretStoreFallbackReason.PlatformUnsupported,
            SecretStoreFallback.FromFailure(new PlatformNotSupportedException("仅 macOS")));

    [Theory]
    [InlineData("Unable to connect to the session bus")]
    [InlineData("D-Bus 会话总线不可达")]
    public void Bus_failures_are_classified_as_service_unreachable(string message) =>
        Assert.Equal(
            SecretStoreFallbackReason.ServiceUnreachable,
            SecretStoreFallback.FromFailure(new InvalidOperationException(message)));

    [Fact]
    public void A_dbus_typed_exception_is_recognised_by_its_type_name() =>
        Assert.Equal(
            SecretStoreFallbackReason.ServiceUnreachable,
            SecretStoreFallback.FromFailure(new FakeDBusException("no bus")));

    [Fact]
    public void Unrecognised_failures_stay_unknown_so_the_detail_is_shown() =>
        Assert.Equal(SecretStoreFallbackReason.Unknown, SecretStoreFallback.FromFailure(new IOException("磁盘满了")));

    [Fact]
    public void Failure_classification_rejects_a_null_exception() =>
        Assert.Throws<ArgumentNullException>(() => SecretStoreFallback.FromFailure(null!));

    [Fact]
    public void Plaintext_protector_carries_the_reason_while_encrypted_ones_do_not()
    {
        Assert.Equal("系统没有可用的密钥库", new PlainTextSecretProtector("系统没有可用的密钥库").FallbackReason);
        Assert.Null(new PlainTextSecretProtector().FallbackReason);

        // 按名取历史实现时没有探测上下文：不该凭空编一个原因出来。
        Assert.Null(SecretProtectors.CreateByName("plaintext").FallbackReason);
        Assert.False(SecretProtectors.CreateByName("plaintext").IsEncrypted);
    }

    [Fact]
    public async Task Mac_provider_reports_unsupported_platform_off_macos()
    {
        if (OperatingSystem.IsMacOS())
        {
            Assert.Skip("macOS 上走真实钥匙串路径（另有集成测覆盖）");
        }

        var provider = new MacKeychainKeyProvider(suppressUi: true);

        Assert.False(await provider.IsAvailableAsync());
        Assert.Equal(SecretStoreFallbackReason.PlatformUnsupported, provider.UnavailableReason);
        Assert.Null(provider.UnavailableStatus);
    }

    [Fact]
    public async Task Windows_default_protector_is_dpapi_and_needs_no_fallback_reason()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("仅在 Windows 上断言 DPAPI 默认通道");
        }

        var protector = await SecretProtectors.CreateDefaultAsync();

        Assert.True(protector.IsEncrypted);
        Assert.Equal("windows-dpapi", protector.Name);
        Assert.Null(protector.FallbackReason);
    }

    /// <summary>类型名里带 DBus 的替身异常：验证归类靠类型名而非硬编类型。</summary>
    private sealed class FakeDBusException(string message) : Exception(message);
}
