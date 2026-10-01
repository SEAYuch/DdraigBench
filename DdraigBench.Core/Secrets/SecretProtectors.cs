// DdraigBench — 口令保护器工厂（按平台选实现；文件里记录名称以便迁移）

namespace DdraigBench.Core.Secrets;

public static class SecretProtectors
{
    public const string ServiceName = "DdraigBench";

    /// <summary>
    /// 当前平台可用的最佳实现：Windows → DPAPI；Linux/FreeBSD → 可用则 Secret Service；
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

    /// <summary>按配置文件里记录的名称取实现，用于读取历史文件（如明文 → DPAPI → keyring 迁移）。</summary>
    public static ISecretProtector CreateByName(string name) => name switch
    {
        "windows-dpapi" => new DpapiSecretProtector(),
        SecretServiceSecretProtector.ProtectorName => new SecretServiceSecretProtector(new SecretServiceKeyProvider()),
        "plaintext" => new PlainTextSecretProtector(),
        _ => throw new NotSupportedException($"未知的口令保护方式：{name}"),
    };
}
