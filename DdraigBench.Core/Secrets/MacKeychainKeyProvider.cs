// DdraigBench — 主密钥存放于 macOS 钥匙串（Security.framework 通用密码条目）

using System.Security.Cryptography;

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 在 macOS 钥匙串里保存/取回一把 32 字节主密钥（首次使用时随机生成）。该密钥供
/// <see cref="SecretKeyEnvelope"/> 做信封加密——连接串密文留在配置文件、密钥留在钥匙串。
/// </summary>
/// <remarks>
/// 与 <see cref="SecretServiceKeyProvider"/> 同构（同为 freedesktop 那条路的 macOS 兄弟）：
/// 内存缓存 + <see cref="SemaphoreSlim"/> 防并发重复创建，条目标识沿用同一套
/// <c>service=DdraigBench / account=master-key / label="DdraigBench master key"</c>。
/// 任何"钥匙串不可用/未获授权"的状态码都抛 <see cref="KeychainUnavailableException"/>，
/// 由 <see cref="SecretProtectors.CreateDefaultAsync"/> 探测阶段回退明文保护器（见 §2.8 规则 4）。
/// </remarks>
public sealed class MacKeychainKeyProvider : IMasterKeyProvider
{
    /// <summary>钥匙串条目的显示标签。</summary>
    internal const string ItemLabel = "DdraigBench master key";

    /// <summary>钥匙串条目的 service 属性（对应 freedesktop 侧的 <c>application</c>）。</summary>
    internal const string DefaultService = "DdraigBench";

    /// <summary>钥匙串条目的 account 属性（对应 freedesktop 侧的 <c>purpose=master-key</c>）。</summary>
    internal const string DefaultAccount = "master-key";

    private readonly string _service;
    private readonly string _account;
    private readonly bool _suppressUi;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _masterKey;

    /// <param name="service">条目 service 属性；默认 <c>DdraigBench</c>，测试可传入唯一值以隔离。</param>
    /// <param name="account">条目 account 属性；默认 <c>master-key</c>。</param>
    /// <param name="suppressUi">
    /// 禁止钥匙串弹出授权界面（等价 <c>kSecUseAuthenticationUIFail</c>）。用于自动化测试与 CI：
    /// 未授权时立刻得到 <c>errSecInteractionNotAllowed</c>，而不是把调用线程挂在看不见的授权框上。
    /// </param>
    public MacKeychainKeyProvider(
        string? service = null,
        string? account = null,
        bool suppressUi = false)
    {
        _service = string.IsNullOrWhiteSpace(service) ? DefaultService : service;
        _account = string.IsNullOrWhiteSpace(account) ? DefaultAccount : account;
        _suppressUi = suppressUi;
    }

    public string Name => "macOS Keychain";

    /// <summary>
    /// 最近一次 <see cref="IsAvailableAsync"/> 判定"不可用"时的原因分类；
    /// 可用时为 <c>null</c>。供 <see cref="SecretProtectors.CreateDefaultAsync"/> 组装用户可读的回退说明。
    /// </summary>
    public SecretStoreFallbackReason? UnavailableReason { get; private set; }

    /// <summary>最近一次探测失败时的原始 <c>OSStatus</c>（无状态码失败时为 <c>null</c>）。</summary>
    public int? UnavailableStatus { get; private set; }

    /// <summary>
    /// 探测本机钥匙串是否可用（能读到条目、或确认条目不存在但钥匙串可写）。
    /// 任何失败都返回 <c>false</c>，交由调用方回退明文——绝不抛异常。
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        UnavailableReason = null;
        UnavailableStatus = null;

        if (!OperatingSystem.IsMacOS())
        {
            UnavailableReason = SecretStoreFallbackReason.PlatformUnsupported;
            return Task.FromResult(false);
        }

        try
        {
            MacSecurityFramework.EnsureSupportedPlatform();

            // 探测一律禁 UI：仓库/CI 里没有钥匙串窗口可点。
            var status = MacSecurityFramework.TryCopyData(_service, _account, suppressUi: true, out _);
            if (status is MacSecurityFramework.StatusSuccess or MacSecurityFramework.StatusItemNotFound)
            {
                return Task.FromResult(true);
            }

            // 记下失败原因（ACL 拦下 / 已锁定 / 缺授权组 / 钥匙串本身不可用），
            // 让 UI 能给出"删条目重来 / 解锁 / 换签名构建"这类可操作提示。
            UnavailableReason = SecretStoreFallback.FromMacStatus(status);
            UnavailableStatus = status;
            return Task.FromResult(false);
        }
        catch (PlatformNotSupportedException)
        {
            UnavailableReason = SecretStoreFallbackReason.PlatformUnsupported;
            return Task.FromResult(false);
        }
        catch (KeychainUnavailableException ex)
        {
            UnavailableReason = SecretStoreFallback.FromMacStatus(ex.Status);
            UnavailableStatus = ex.Status;
            return Task.FromResult(false);
        }
        catch (Exception)
        {
            // 宽 catch 是有意的：探测只用于"能否加密"的判定，任何失败都应安全回退明文，而不是让启动崩掉。
            UnavailableReason = SecretStoreFallbackReason.Unknown;
            return Task.FromResult(false);
        }
    }

    public async ValueTask<byte[]> GetOrCreateMasterKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_masterKey is not null)
        {
            return _masterKey;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_masterKey is not null)
            {
                return _masterKey;
            }

            MacSecurityFramework.EnsureSupportedPlatform();

            // 最多两轮：第一轮撞上"并发已建"时，第二轮去读别人建好的那把（避免两个进程各持一把不同密钥）。
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var read = MacSecurityFramework.TryCopyData(_service, _account, _suppressUi, out var stored);
                if (read == MacSecurityFramework.StatusSuccess)
                {
                    _masterKey = EnsureWellFormedKey(stored);
                    return _masterKey;
                }

                if (read != MacSecurityFramework.StatusItemNotFound)
                {
                    throw Failure(read, "读取");
                }

                var candidate = RandomNumberGenerator.GetBytes(SecretKeyEnvelope.KeyLength);
                var added = MacSecurityFramework.TryAddData(_service, _account, ItemLabel, candidate);
                if (added == MacSecurityFramework.StatusSuccess)
                {
                    _masterKey = candidate;
                    return _masterKey;
                }

                if (added != MacSecurityFramework.StatusDuplicateItem)
                {
                    throw Failure(added, "写入");
                }
            }

            throw new CryptographicException("钥匙串中 DdraigBench 主密钥被并发占用：两次尝试都遇到已存在条目。");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 删除本提供器在钥匙串中的主密钥条目（并清空内存缓存），供"重置凭据"与测试清理使用。
    /// 条目本来就不存在时返回 <c>false</c>；非 macOS 返回 <c>false</c>。
    /// </summary>
    public async Task<bool> ResetMasterKeyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _masterKey = null;

            if (!OperatingSystem.IsMacOS())
            {
                return false;
            }

            var status = MacSecurityFramework.TryDelete(_service, _account, _suppressUi);
            if (status == MacSecurityFramework.StatusSuccess)
            {
                return true;
            }

            if (MacSecurityFramework.IsAuthorizationFailure(status))
            {
                return false;
            }

            throw Failure(status, "删除");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] EnsureWellFormedKey(byte[]? key)
    {
        if (!SecretKeyEnvelope.IsWellFormedKey(key))
        {
            var length = key?.Length ?? 0;
            throw new CryptographicException(
                $"钥匙串中 DdraigBench 主密钥长 {length} 字节，应为 {SecretKeyEnvelope.KeyLength} 字节" +
                "（可能已被改写或损坏；可重置主密钥后重新录入连接口令）。");
        }

        return key!;
    }

    private static Exception Failure(int status, string operation)
    {
        var description = MacSecurityFramework.DescribeStatus(status);
        return MacSecurityFramework.IsAuthorizationFailure(status)
            ? new KeychainUnavailableException($"macOS 钥匙串{operation}主密钥失败：{description}。", status)
            : new InvalidOperationException($"macOS 钥匙串{operation}主密钥失败：{description}（OSStatus {status}）。");
    }
}