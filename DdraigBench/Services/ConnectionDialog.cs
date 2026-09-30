// DdraigBench — 基于 Avalonia 窗口的连接对话框实现

using Avalonia.Controls;
using DdraigBench.Core.Connections;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Services;

public sealed class ConnectionDialog : IConnectionDialog
{
    private readonly Func<Window> _owner;
    private readonly IConnectionTester _connectionTester;

    public ConnectionDialog(Func<Window> owner, IConnectionTester connectionTester)
    {
        _owner = owner;
        _connectionTester = connectionTester;
    }

    public async Task<ConnectionProfile?> ShowAsync()
    {
        var owner = _owner();
        var viewModel = new ConnectionDialogViewModel(new StorageProviderFilePicker(owner), _connectionTester);
        var window = new ConnectionDialogView { DataContext = viewModel };

        viewModel.CloseRequested += result => window.Close(result);

        return await window.ShowDialog<ConnectionProfile?>(owner);
    }
}
