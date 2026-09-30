// DdraigBench — 口令保护器工厂（按平台选实现；文件里记录名称以便迁移）

namespace DdraigBench.Core.Secrets;

public static class SecretProtectors
{
    public const string ServiceName = "DdraigBench";

    /// <summary>当前平台可用的最佳实现；无系统密钥库时回退明文（<see cref="ISecretProtector.IsEncrypted"/> 为 false）。</summary>
    public static ISecretProtector CreateDefault() =>
        OperatingSystem.IsWindows() ? new DpapiSecretProtector() : CreateByName("plaintext");

    /// <summary>按配置文件里记录的名称取实现，用于读取历史文件（如明文 → DPAPI 升级）。</summary>
    public static ISecretProtector CreateByName(string name) => name switch
    {
        "windows-dpapi" => new DpapiSecretProtector(),
        "plaintext" => new PlainTextSecretProtector(),
        _ => throw new NotSupportedException($"未知的口令保护方式：{name}"),
    };
}
