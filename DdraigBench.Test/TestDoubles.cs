// DdraigBench — 测试替身（文件选择 / 导出对话框）

using System.Security.Cryptography;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Secrets;
using DdraigBench.Services;

namespace DdraigBench.Test;

internal sealed class StubFilePicker(string? openPath = null, string? savePath = null) : IFilePicker
{
    public string? OpenPath { get; set; } = openPath;

    public string? SavePath { get; set; } = savePath;

    public IReadOnlyList<string>? LastOpenPatterns { get; private set; }

    public string? LastSuggestedFileName { get; private set; }

    public Task<string?> PickOpenFileAsync(
        string title, string fileTypeName, IReadOnlyList<string> patterns, CancellationToken ct = default)
    {
        LastOpenPatterns = patterns;
        return Task.FromResult(OpenPath);
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        string? suggestedFileName = null,
        CancellationToken ct = default)
    {
        LastSuggestedFileName = suggestedFileName;
        return Task.FromResult(SavePath);
    }
}

internal sealed class StubDdlViewer : IDdlViewer
{
    public string? LastTitle { get; private set; }

    public string? LastDdl { get; private set; }

    public int ShowCount { get; private set; }

    public Task ShowAsync(string title, string ddl)
    {
        LastTitle = title;
        LastDdl = ddl;
        ShowCount++;
        return Task.CompletedTask;
    }
}

internal sealed class StubExportDialog(ExportTarget? target = null) : IExportDialog
{
    public ExportTarget? Target { get; set; } = target;

    public ExportFormat? LastFormat { get; private set; }

    public Task<ExportTarget?> ShowAsync(ExportFormat format, string? suggestedFileName = null)
    {
        LastFormat = format;
        return Task.FromResult(Target);
    }
}

/// <summary>
/// 假的主密钥提供器：让信封加密逻辑可在无密钥库环境（CI / Windows）完整单测。
/// 传入固定 <paramref name="key"/> 可模拟"换了密钥"或"密钥损坏"。
/// </summary>
internal sealed class FakeMasterKeyProvider(byte[]? key = null) : IMasterKeyProvider
{
    public byte[] Key { get; } = key ?? RandomNumberGenerator.GetBytes(SecretKeyEnvelope.KeyLength);

    public int CallCount { get; private set; }

    public string Name => "fake-keyring";

    public ValueTask<byte[]> GetOrCreateMasterKeyAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return ValueTask.FromResult(Key);
    }
}

/// <summary>壳层测试用的口令保护器替身：只暴露名字与是否加密，不做任何真实加解密。</summary>
internal sealed class FakeSecretProtector(string name, bool isEncrypted) : ISecretProtector
{
    public string Name { get; } = name;

    public bool IsEncrypted { get; } = isEncrypted;

    public ValueTask<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(plainText);

    public ValueTask<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(protectedText);
}

/// <summary>内存版连接清单存储：让 MainViewModel 的持久化逻辑无需真实文件与密钥库即可测。</summary>
internal sealed class StubConnectionStore(
    IReadOnlyList<ConnectionProfile>? initial = null,
    bool isEncrypted = true) : IConnectionStore
{
    public ISecretProtector Protector { get; } =
        new FakeSecretProtector(isEncrypted ? "stub-encrypted" : "plaintext", isEncrypted);

    public List<ConnectionProfile> Saved { get; set; } = [.. initial ?? []];

    public int LoadCount { get; private set; }

    public int SaveCount { get; private set; }

    public Exception? LoadFailure { get; init; }

    public Exception? CreateFailure { get; init; }

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default)
    {
        LoadCount++;
        return LoadFailure is not null
            ? Task.FromException<IReadOnlyList<ConnectionProfile>>(LoadFailure)
            : Task.FromResult<IReadOnlyList<ConnectionProfile>>(Saved.ToList());
    }

    public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default)
    {
        SaveCount++;
        Saved = [.. profiles];
        return Task.CompletedTask;
    }
}

/// <summary>可编排失败的 store 工厂（测"启动时读清单失败也不崩"）。</summary>
internal sealed class StubConnectionStoreFactory(StubConnectionStore? store = null) : IConnectionStoreFactory
{
    public StubConnectionStore? Store { get; } = store;

    public Task<IConnectionStore> CreateAsync(CancellationToken ct = default) =>
        Store?.CreateFailure is { } failure
            ? Task.FromException<IConnectionStore>(failure)
            : Task.FromResult<IConnectionStore>(Store!);
}
