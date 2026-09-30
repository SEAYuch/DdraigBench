// DdraigBench — Windows DPAPI 口令保护（CurrentUser 作用域）

using System.Security.Cryptography;
using System.Text;

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 仅 Windows 可用；其他平台由 <see cref="SecretProtectors.CreateDefault"/> 选择各自实现。
/// 平台守卫直接写在方法体内（CA1416 只认同方法的 <c>OperatingSystem.IsWindows()</c> 流分析）。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DdraigBench.ConnectionStore.v1");

    public string Name => "windows-dpapi";

    public bool IsEncrypted => true;

    public string Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 仅在 Windows 可用");
        }

        var data = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI 仅在 Windows 可用");
        }

        var data = ProtectedData.Unprotect(
            Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }
}
