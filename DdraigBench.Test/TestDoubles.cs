// DdraigBench — 测试替身（文件选择 / 导出对话框）

using System.Security.Cryptography;
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
