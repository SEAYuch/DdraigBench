// DdraigBench — 基于 Avalonia 窗口的导出对话框实现

using Avalonia.Controls;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Services;

public sealed class ExportDialog : IExportDialog
{
    private readonly Func<Window> _owner;
    private readonly IFilePicker _filePicker;

    public ExportDialog(Func<Window> owner, IFilePicker filePicker)
    {
        _owner = owner;
        _filePicker = filePicker;
    }

    public async Task<ExportTarget?> ShowAsync(ExportFormat format, string? suggestedFileName = null)
    {
        var owner = _owner();
        var viewModel = new ExportDialogViewModel(format, _filePicker);
        if (!string.IsNullOrWhiteSpace(suggestedFileName))
        {
            viewModel.FilePath = suggestedFileName;
        }

        var window = new ExportDialogView { DataContext = viewModel };
        viewModel.CloseRequested += result => window.Close(result);

        return await window.ShowDialog<ExportTarget?>(owner);
    }
}
