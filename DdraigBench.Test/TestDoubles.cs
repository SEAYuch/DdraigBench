// DdraigBench — 测试替身（文件选择 / 导出对话框）

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
