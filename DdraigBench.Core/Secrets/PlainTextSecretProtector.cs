// DdraigBench — 明文回退保护器（系统密钥库不可用时；IsEncrypted=false 供 UI 告警）

namespace DdraigBench.Core.Secrets;

public sealed class PlainTextSecretProtector : ISecretProtector
{
    private const string Prefix = "plain:";

    public string Name => "plaintext";

    public bool IsEncrypted => false;

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        return Prefix + plainText;
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        return protectedText.StartsWith(Prefix, StringComparison.Ordinal)
            ? protectedText[Prefix.Length..]
            : protectedText;
    }
}
