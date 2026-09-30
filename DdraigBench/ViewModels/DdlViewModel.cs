// DdraigBench — DDL 预览 VM（只读展示，无业务逻辑）

using System.Windows.Input;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class DdlViewModel : ViewModelBase
{
    public DdlViewModel(string title, string ddl)
    {
        Title = title;
        Ddl = ddl;
        CloseCommand = ReactiveCommand.Create(Close);
    }

    public event Action? CloseRequested;

    public string Title { get; }

    public string Ddl { get; }

    public ICommand CloseCommand { get; }

    public void Close() => CloseRequested?.Invoke();
}
