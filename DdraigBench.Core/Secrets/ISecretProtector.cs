// DdraigBench — 口令保护抽象（系统密钥库 / 回退实现）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 对称的"明文 ⇄ 密文"保护器；密文以文本形式落盘（Base64 等）。
/// 因 Linux freedesktop Secret Service 经 D-Bus 访问天然异步，接口统一为异步；
/// 同步实现（DPAPI / 明文）用 <see cref="ValueTask.FromResult{TResult}"/> 包装。
/// </summary>
public interface ISecretProtector
{
    /// <summary>写入配置文件、便于日后识别与迁移（如换密钥库时提示用户）。</summary>
    string Name { get; }

    /// <summary>是否真的加密（false = 明文回退，UI 须给出警告）。</summary>
    bool IsEncrypted { get; }

    ValueTask<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default);

    ValueTask<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default);
}
