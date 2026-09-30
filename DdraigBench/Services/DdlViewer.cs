// DdraigBench — DDL 预览窗口（只读展示）

using Avalonia.Controls;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Services;

public interface IDdlViewer
{
    Task ShowAsync(string title, string ddl);
}

public sealed class DdlViewer : IDdlViewer
{
    private readonly Func<Window> _owner;

    public DdlViewer(Func<Window> owner) => _owner = owner;

    public async Task ShowAsync(string title, string ddl)
    {
        var owner = _owner();
        var viewModel = new DdlViewModel(title, ddl);
        var window = new DdlView { DataContext = viewModel };

        viewModel.CloseRequested += () => window.Close();
        await window.ShowDialog(owner);
    }
}
