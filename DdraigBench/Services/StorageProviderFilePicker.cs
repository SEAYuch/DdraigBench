// DdraigBench — 基于 Avalonia StorageProvider 的文件选择实现

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DdraigBench.Services;

public sealed class StorageProviderFilePicker : IFilePicker
{
    private readonly TopLevel _topLevel;

    public StorageProviderFilePicker(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<string?> PickOpenFileAsync(
        string title,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        CancellationToken ct = default)
    {
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(fileTypeName) { Patterns = patterns.ToArray() },
                FilePickerFileTypes.All,
            },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        string? suggestedFileName = null,
        CancellationToken ct = default)
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(fileTypeName) { Patterns = patterns.ToArray() },
            },
        });

        return file?.TryGetLocalPath();
    }
}
