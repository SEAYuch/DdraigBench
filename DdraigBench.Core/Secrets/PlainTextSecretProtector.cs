// DdraigBench — 明文回退保护器（系统密钥库不可用时；IsEncrypted=false 供 UI 告警）

namespace DdraigBench.Core.Secrets;

public sealed class PlainTextSecretProtector : ISecretProtector
{
    private const string Prefix = "plain:";

    public string Name => "plaintext";

    public bool IsEncrypted => false;

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
