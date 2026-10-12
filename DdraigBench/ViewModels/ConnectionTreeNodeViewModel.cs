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
    private bool _loadFailed;
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
        DbObjectKind.Event => "EV",
        DbObjectKind.Partition => "P",
        DbObjectKind.Column => "C",
        _ => string.Empty,
    });

    public bool CanOpenData => Kind is DbObjectKind.Table or DbObjectKind.View;

    public bool CanPreviewDdl => Kind is DbObjectKind.Table or DbObjectKind.View or DbObjectKind.Trigger
        or DbObjectKind.StoredProcedure or DbObjectKind.Function or DbObjectKind.Event;

    public ObservableCollection<ConnectionTreeNodeViewModel> VisibleChildren { get; } = new();

    public bool IsVisible { get; private set; } = true;

    public bool HasVisibleChildren => VisibleChildren.Count > 0 || (!_loaded && _loadChildren is not null);

    /// <summary>
    /// 上一次子节点加载是否失败（M9，2026-10-11）。失败时树右键菜单给出「重试加载」入口；
    /// 没有它的话节点会被记成"已加载且为空"，用户既看不到对象也无法重试。
    /// </summary>
    public bool LoadFailed
    {
        get => _loadFailed;
        private set => this.RaiseAndSetIfChanged(ref _loadFailed, value);
    }

    /// <summary>
    /// 所属**容器**：SQLite/MySQL 的库级对象传库名；PostgreSQL/SQL Server 的模式级对象传模式名。
    /// 之所以不叫 Database，是因为 PG / SQL Server 下对象查询的入参是模式，两者混用会把 DDL 预览的容器名传错。
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
                LoadFailed = false;
                if (_loadChildren is not null)
                {
                    _ = _loadChildren(this);
                }
            }
        }
    }

    /// <summary>
    /// 子节点加载失败时由 loader 的 catch 调用（M9）：撤销「已加载」标记并丢掉失败前已塞进去的半个子树，
    /// 这样再次展开（或点「重试加载」）会真的重跑 loader，而不是把失败当成"加载完成、结果为空"。
    /// </summary>
    public void ReportLoadFailure()
    {
        _loaded = false;
        LoadFailed = true;
        Children.Clear();
        this.RaisePropertyChanged(nameof(HasVisibleChildren));
    }

    /// <summary>强制重新加载子节点（M9）：供树右键「重试加载」调用；loader 内部自己收敛异常。</summary>
    public Task ReloadAsync()
    {
        if (_loadChildren is null)
        {
            return Task.CompletedTask;
        }

        _loaded = true;
        LoadFailed = false;
        Children.Clear();
        return _loadChildren(this);
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
