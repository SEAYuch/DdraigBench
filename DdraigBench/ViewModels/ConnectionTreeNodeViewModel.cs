// DdraigBench — 左树节点模型（惰性加载子节点）

using System.Collections.ObjectModel;
using DdraigBench.Core.Metadata;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class ConnectionTreeNodeViewModel : ViewModelBase
{
    private readonly Func<ConnectionTreeNodeViewModel, Task>? _loadChildren;
    private bool _loaded;
    private bool _isExpanded;
    private bool _isSelected;

    public ConnectionTreeNodeViewModel(
        string name,
        DbObjectKind kind,
        Func<ConnectionTreeNodeViewModel, Task>? loadChildren = null,
        string? database = null)
    {
        Name = name;
        Kind = kind;
        Database = database;
        _loadChildren = loadChildren;
    }

    public string Name { get; }

    public DbObjectKind Kind { get; }

    /// <summary>所属数据库（DDL 预览需要库名 + 表名两级）。</summary>
    public string? Database { get; }

    public ObservableCollection<ConnectionTreeNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            var wasExpanded = _isExpanded;
            this.RaiseAndSetIfChanged(ref _isExpanded, value);

            if (!wasExpanded && value && !_loaded)
            {
                _loaded = true;
                if (_loadChildren is not null)
                {
                    _ = _loadChildren(this);
                }
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
