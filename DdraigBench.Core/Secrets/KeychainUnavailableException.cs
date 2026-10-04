// DdraigBench — 密钥库不可用异常（统一"回退明文 + UI 告警"的判定依据）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 系统密钥库当前不可用：钥匙串被锁定、用户拒绝授权、不允许交互、无钥匙串等。
/// 未来壳工程接线时可捕获本异常，改为回退 <see cref="PlainTextSecretProtector"/> 并提示用户，
/// 而不是让启动崩掉。
/// </summary>
public sealed class KeychainUnavailableException : Exception
{
    public KeychainUnavailableException(string message, int status = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>Security.framework 的 <c>OSStatus</c>；0 表示与 <c>OSStatus</c> 无关（如框架符号解析失败）。</summary>
    public int Status { get; }
}