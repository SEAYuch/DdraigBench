// DdraigBench — 主密钥存放于 freedesktop Secret Service（gnome-keyring / KWallet）

using System.Security.Cryptography;
using DBus.Services.Secrets;

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 在系统密钥库的默认集合里保存/取回一把 32 字节主密钥（首次使用时随机生成）。
/// 该密钥供 <see cref="SecretKeyEnvelope"/> 做信封加密——连接串密文留在配置文件、密钥留在密钥库。
/// </summary>
/// <remarks>
/// 仅访问本机 D-Bus 会话总线上的 <c>org.freedesktop.secrets</c> 服务，不依赖任何原生库
/// （gnome-keyring / KWallet 均可作为后端）。无会话总线（服务器、容器、CI）时
/// <see cref="IsAvailableAsync"/> 返回 <c>false</c>，由调用方回退明文保护器。
/// </remarks>
public sealed class SecretServiceKeyProvider : IMasterKeyProvider
{
    /// <summary>密钥库中该条目的显示标签。</summary>
    internal const string ItemLabel = "DdraigBench master key";

    /// <summary>查找条目用的属性：<c>secret-tool lookup application DdraigBench purpose master-key</c>。</summary>
    internal const string AttributeApplication = "application";
    internal const string AttributeApplicationValue = "DdraigBench";
    internal const string AttributePurpose = "purpose";
    internal const string AttributePurposeValue = "master-key";

    private const string KeyContentType = "application/octet-stream";

    private readonly string _application;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SecretService? _service;
    private byte[]? _masterKey;

    /// <summary>
    /// 最近一次 <see cref="IsAvailableAsync"/> 判定"不可用"时的原因分类；可用时为 <c>null</c>。
    /// 供 <see cref="SecretProtectors.CreateDefaultAsync"/> 组装用户可读的回退说明。
    /// </summary>
    public SecretStoreFallbackReason? UnavailableReason { get; private set; }

    /// <summary>最近一次探测失败时的原始错误文本（无异常时为 <c>null</c>），仅用于"未能归类"时补充说明。</summary>
    public string? UnavailableDetail { get; private set; }

    /// <param name="application">
    /// 条目的 <c>application</c> 属性值，用于与其它应用或测试隔离；默认 <c>DdraigBench</c>。
    /// </param>
    public SecretServiceKeyProvider(string application = AttributeApplicationValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        _application = application;
    }

    public string Name => "freedesktop Secret Service";

    /// <summary>
    /// 探测本机 Secret Service 是否可用（能连上会话总线且存在默认集合）。
    /// 成功会缓存连接供后续取主密钥复用；任何失败都返回 <c>false</c>，交由调用方回退明文。
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_service is not null)
        {
            UnavailableReason = null;
            UnavailableDetail = null;
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_service is not null)
            {
                return true;
            }

            try
            {
                await EnsureServiceAsync().ConfigureAwait(false);
                UnavailableReason = null;
                UnavailableDetail = null;
                return true;
            }
            catch (Exception ex)
            {
                // 无会话总线 / 无 Secret Service / 无默认集合 / 密钥环被拒 —— 统一视为"不可用"。
                // 宽catch是有意的：这里任何失败都应安全回退到明文保护器，而不是让启动崩掉。
                // 但要把原因留下来：UI 得能区分"环境里压根没有密钥库服务"与"服务在、却被拒绝"。
                UnavailableReason = SecretStoreFallback.FromFailure(ex);
                UnavailableDetail = ex.Message;
                return false;
            }
        }
        finally
        {
            _gate.Release();
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

            var service = await EnsureServiceAsync().ConfigureAwait(false);
            var collection = await service.GetDefaultCollectionAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("Secret Service 默认集合不可用。");

            var matches = await collection.SearchItemsAsync(CreateLookupAttributes()).ConfigureAwait(false);
            var existing = matches.FirstOrDefault();

            byte[] key;
            if (existing is null)
            {
                key = RandomNumberGenerator.GetBytes(SecretKeyEnvelope.KeyLength);
                var item = await collection
                    .CreateItemAsync(ItemLabel, CreateLookupAttributes(), key, KeyContentType, replace: true)
                    .ConfigureAwait(false);
                if (item is null)
                {
                    throw new InvalidOperationException("密钥库拒绝创建 DdraigBench 主密钥条目（提示可能被取消或集合被锁定）。");
                }
            }
            else
            {
                key = await existing.GetSecretAsync().ConfigureAwait(false); // 条目若锁定会自动触发解锁
                if (!SecretKeyEnvelope.IsWellFormedKey(key))
                {
                    throw new CryptographicException(
                        $"密钥库中 DdraigBench 主密钥长 {key.Length} 字节，应为 {SecretKeyEnvelope.KeyLength} 字节（可能已损坏）。");
                }
            }

            _masterKey = key;
            return key;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, string> CreateLookupAttributes() => new()
    {
        { AttributeApplication, _application },
        { AttributePurpose, AttributePurposeValue },
    };

    /// <summary>
    /// 删除本提供器在密钥库里存放的全部主密钥条目（并清空内存缓存），供"重置凭据"与测试清理使用。
    /// 不可用时返回 0。
    /// </summary>
    public async Task<int> DeleteStoredKeysAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _masterKey = null;

            if (_service is null)
            {
                return 0;
            }

            var collection = await _service.GetDefaultCollectionAsync().ConfigureAwait(false);
            if (collection is null)
            {
                return 0;
            }

            var matches = await collection.SearchItemsAsync(CreateLookupAttributes()).ConfigureAwait(false);
            foreach (var item in matches)
            {
                await item.DeleteAsync().ConfigureAwait(false);
            }

            return matches.Length;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 建立到 Secret Service 的连接并确认默认集合存在；成功即缓存连接。
    /// 用 DH 会话（<see cref="EncryptionType.Dh"/>）而非明文传输密钥负载。
    /// </summary>
    /// <remarks>D-Bus 调用本身不支持取消令牌，故本方法不接受 <see cref="CancellationToken"/>。</remarks>
    private async Task<SecretService> EnsureServiceAsync()
    {
        if (_service is not null)
        {
            return _service;
        }

        var service = await SecretService.ConnectAsync(EncryptionType.Dh).ConfigureAwait(false);

        if (await service.GetDefaultCollectionAsync().ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException("Secret Service 没有默认集合（default collection）。");
        }

        return _service = service;
    }
}
