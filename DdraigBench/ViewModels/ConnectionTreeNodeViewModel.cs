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
        string? container = null)
    {
        Name = name;
        Kind = kind;
        Container = container;
        _loadChildren = loadChildren;
    }

    public string Name { get; }

    public DbObjectKind Kind { get; }

    /// <summary>
    /// 所属**容器**：库级对象是库名，PostgreSQL 的模式级对象是模式名（M5-2）。
    /// 之所以不叫 Database，是因为 PG 下对象查询的入参是模式，两者混用会把 DDL 预览的库名传错。
    /// </summary>
    public string? Container { get; }

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
