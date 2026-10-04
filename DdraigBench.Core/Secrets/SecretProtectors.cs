// DdraigBench — 口令保护器工厂（按平台选实现；文件里记录名称以便迁移）

namespace DdraigBench.Core.Secrets;

public static class SecretProtectors
{
    public const string ServiceName = "DdraigBench";

    /// <summary>
    /// 当前平台可用的最佳实现：Windows → DPAPI；macOS → 可用则钥匙串；Linux/FreeBSD → 可用则 Secret Service；
    /// 否则回退明文（<see cref="ISecretProtector.IsEncrypted"/> 为 false，UI 须告警）。
    /// </summary>
    /// <remarks>
    /// 会探测密钥库可用性，故为异步。只应在启动/接线时调用一次，把结果注入
    /// <c>ConnectionStore</c>，不要在每次读写连接清单时重复探测。
    /// </remarks>
    public static async Task<ISecretProtector> CreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretProtector();
        }

        if (OperatingSystem.IsMacOS())
        {
            // 探测用的实例一律禁 UI：无人值守环境（CI / 启动早期）不该把线程挂在看不见的授权框上，
            // 拿不到就按"无密钥库"回退明文（§2.8 规则 4），不得让启动崩掉。
            var probe = new MacKeychainKeyProvider(suppressUi: true);
            return await probe.IsAvailableAsync(cancellationToken).ConfigureAwait(false)
                // 正式使用的实例允许弹框：首次授权后（尤其发布签名后）就完全无感。
                ? new MacKeychainSecretProtector(new MacKeychainKeyProvider())
                : new PlainTextSecretProtector();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            var provider = new SecretServiceKeyProvider();
            if (await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                return new SecretServiceSecretProtector(provider);
            }
        }

        return new PlainTextSecretProtector();
    }

    /// <summary>按配置文件里记录的名称取实现，用于读取历史文件（如明文 → DPAPI → 钥匙串/keyring 迁移）。</summary>
    public static ISecretProtector CreateByName(string name) => name switch
    {
        "windows-dpapi" => new DpapiSecretProtector(),
        MacKeychainSecretProtector.ProtectorName => new MacKeychainSecretProtector(new MacKeychainKeyProvider()),
        SecretServiceSecretProtector.ProtectorName => new SecretServiceSecretProtector(new SecretServiceKeyProvider()),
        "plaintext" => new PlainTextSecretProtector(),
        _ => throw new NotSupportedException($"未知的口令保护方式：{name}"),
    };
}
