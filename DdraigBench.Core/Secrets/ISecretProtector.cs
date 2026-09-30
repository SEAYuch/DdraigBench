// DdraigBench — 口令保护抽象（系统密钥库 / 回退实现）

namespace DdraigBench.Core.Secrets;

/// <summary>对称的"明文 ⇄ 密文"保护器；密文以 Base64 文本形式落盘。</summary>
public interface ISecretProtector
{
    /// <summary>写入配置文件、便于日后识别与迁移（如换密钥库时提示用户）。</summary>
    string Name { get; }

    /// <summary>是否真的加密（false = 明文回退，UI 须给出警告）。</summary>
    bool IsEncrypted { get; }

    string Protect(string plainText);

    string Unprotect(string protectedText);
}
