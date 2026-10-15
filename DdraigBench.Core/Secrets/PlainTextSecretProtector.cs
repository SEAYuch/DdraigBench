// DdraigBench — 明文回退保护器（系统密钥库不可用时；IsEncrypted=false 供 UI 告警）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 无系统密钥库时的回退：连接串直接以明文（<c>plain:</c> 前缀）落盘。
/// </summary>
/// <remarks>
/// 带上 <paramref name="fallbackReason"/> 才能让 UI 说清"为什么回退了、用户能做什么"
/// （由 <see cref="SecretStoreFallback.Describe"/> 生成）；从既有文件按名取实现
/// （<see cref="SecretProtectors.CreateByName"/>）时没有探测上下文，故为 <c>null</c>。
/// </remarks>
public sealed class PlainTextSecretProtector(string? fallbackReason = null) : ISecretProtector
{
    private const string Prefix = "plain:";

    public string Name => "plaintext";

    public bool IsEncrypted => false;

    public string? FallbackReason { get; } = fallbackReason;

    public ValueTask<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return ValueTask.FromResult(Prefix + plainText);
    }

    public ValueTask<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        return ValueTask.FromResult(protectedText.StartsWith(Prefix, StringComparison.Ordinal)
            ? protectedText[Prefix.Length..]
            : protectedText);
    }
}
