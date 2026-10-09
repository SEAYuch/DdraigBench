// DdraigBench — 左树节点模型（惰性加载子节点）

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DdraigBench.Core.Metadata;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class ConnectionTreeNodeViewModel : ViewModelBase
{
    private readonly Func<ConnectionTreeNodeViewModel, Task>? _loadChildren;
    private bool _loaded;
    private bool _isExpanded;
    private bool _isSelected;
    private string _filter = string.Empty;

    public ConnectionTreeNodeViewModel(
        string name,
        DbObjectKind? kind,
        Func<ConnectionTreeNodeViewModel, Task>? loadChildren = null,
        string? container = null,
        string? groupLabel = null)
    {
        Name = name;
        Kind = kind;
        Container = container;
        GroupLabel = groupLabel;
        _loadChildren = loadChildren;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public string Name { get; }

    public DbObjectKind? Kind { get; }

    public string? GroupLabel { get; }

    public bool IsGroup => GroupLabel is not null;

    public string KindLabel => GroupLabel ?? (Kind switch
    {
        DbObjectKind.Database => "DB",
        DbObjectKind.Schema => "SC",
        DbObjectKind.Table => "T",
        DbObjectKind.View => "V",
        DbObjectKind.Trigger => "TR",
        DbObjectKind.StoredProcedure => "SP",
        DbObjectKind.Function => "FN",
        DbObjectKind.Column => "C",
        _ => string.Empty,
    });

    public bool CanOpenData => Kind is DbObjectKind.Table or DbObjectKind.View;

    public bool CanPreviewDdl => Kind is DbObjectKind.Table or DbObjectKind.View or DbObjectKind.Trigger
        or DbObjectKind.StoredProcedure or DbObjectKind.Function;

    public ObservableCollection<ConnectionTreeNodeViewModel> VisibleChildren { get; } = new();

    public bool IsVisible { get; private set; } = true;

    public bool HasVisibleChildren => VisibleChildren.Count > 0 || (!_loaded && _loadChildren is not null);

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

    public void ApplyFilter(string filter)
    {
        _filter = filter;
        foreach (var child in Children)
        {
            child.ApplyFilter(filter);
        }

        RefreshVisibility();
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var child in Children)
        {
            child.PropertyChanged -= OnChildPropertyChanged;
            child.PropertyChanged += OnChildPropertyChanged;
            child.ApplyFilter(_filter);
        }

        RefreshVisibility();
        this.RaisePropertyChanged(nameof(HasVisibleChildren));
    }

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VisibleChildren) or nameof(IsVisible))
        {
            RefreshVisibility();
        }
    }

    private void RefreshVisibility()
    {
        VisibleChildren.Clear();
        foreach (var child in Children)
        {
            if (string.IsNullOrWhiteSpace(_filter)
                || child.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || child.VisibleChildren.Count > 0)
            {
                VisibleChildren.Add(child);
            }
        }

        IsVisible = string.IsNullOrWhiteSpace(_filter)
            || Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || VisibleChildren.Count > 0;
        this.RaisePropertyChanged(nameof(IsVisible));
        this.RaisePropertyChanged(nameof(VisibleChildren));
        this.RaisePropertyChanged(nameof(HasVisibleChildren));
    }
}
