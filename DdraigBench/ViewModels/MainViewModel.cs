// DdraigBench — 主窗口 VM（连接树 + 查询标签 + 全局命令）

using System.Collections.ObjectModel;
using System.Windows.Input;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Sessions;
using DdraigBench.Services;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IConnectionDialog _connectionDialog;
    private readonly FreeSqlFactory _factory;
    private readonly IMetadataExplorer _metadata;
    private readonly IFilePicker _filePicker;
    private readonly IExportDialog _exportDialog;
    private readonly IDdlViewer _ddlViewer;
    private DbSession? _session;
    private QueryTabViewModel? _selectedTab;
    private ConnectionTreeNodeViewModel? _selectedNode;
    private string _status = "就绪：请新建一个连接";

    public MainViewModel(
        IConnectionDialog connectionDialog,
        FreeSqlFactory factory,
        IMetadataExplorer metadata,
        IFilePicker filePicker,
        IExportDialog exportDialog,
        IDdlViewer ddlViewer)
    {
        _connectionDialog = connectionDialog;
        _factory = factory;
        _metadata = metadata;
        _filePicker = filePicker;
        _exportDialog = exportDialog;
        _ddlViewer = ddlViewer;

        OpenConnectionCommand = ReactiveCommand.CreateFromTask(OpenConnectionAsync);
        NewQueryCommand = ReactiveCommand.Create(NewQuery);
        PreviewDdlCommand = ReactiveCommand.CreateFromTask(PreviewDdlAsync);
    }

    public ObservableCollection<ConnectionTreeNodeViewModel> Databases { get; } = new();

    public ObservableCollection<QueryTabViewModel> Tabs { get; } = new();

    public ICommand OpenConnectionCommand { get; }

    public ICommand NewQueryCommand { get; }

    public ICommand PreviewDdlCommand { get; }

    /// <summary>左树选中节点（DDL 预览的来源）。</summary>
    public ConnectionTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNode, value);
            this.RaisePropertyChanged(nameof(CanPreviewDdl));
        }
    }

    public bool CanPreviewDdl =>
        _session is not null
        && _selectedNode is { Database: not null, Kind: DbObjectKind.Table or DbObjectKind.View };

    public QueryTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public async Task OpenConnectionAsync()
    {
        var profile = await _connectionDialog.ShowAsync();
        if (profile is null)
        {
            return;
        }

        // 换连接前先收掉旧标签（其未提交事务会被回滚、租约归还）
        foreach (var tab in Tabs)
        {
            await tab.DisposeAsync();
        }

        Tabs.Clear();
        SelectedTab = null;

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = new DbSession(profile, _factory);

        Databases.Clear();
        try
        {
            var databases = await _metadata.GetDatabasesAsync(_session);
            foreach (var database in databases)
            {
                Databases.Add(CreateDatabaseNode(database));
            }

            Status = $"已连接 {profile.Name}：{databases.Count} 个数据库";
            this.RaisePropertyChanged(nameof(CanPreviewDdl));
            NewQuery();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    public void NewQuery()
    {
        var tab = new QueryTabViewModel($"查询 {Tabs.Count + 1}", () => _session, _filePicker, _exportDialog);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    public async Task PreviewDdlAsync()
    {
        if (_session is null || _selectedNode is not { Database: not null } node)
        {
            Status = "请先连接并选中左侧的表或视图";
            return;
        }

        try
        {
            var ddl = await _metadata.GetDdlAsync(_session, node.Database!, node.Name);
            if (ddl is null)
            {
                Status = $"未找到对象：{node.Database}.{node.Name}";
                return;
            }

            Status = $"DDL 预览：{node.Database}.{node.Name}";
            await _ddlViewer.ShowAsync($"{node.Name} — DDL", ddl);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private ConnectionTreeNodeViewModel CreateDatabaseNode(string database) =>
        new(database, DbObjectKind.Database, async node =>
        {
            foreach (var table in await _metadata.GetObjectsAsync(_session!, database, DbObjectKind.Table))
            {
                node.Children.Add(CreateTableNode(database, table.Name));
            }

            foreach (var view in await _metadata.GetObjectsAsync(_session!, database, DbObjectKind.View))
            {
                node.Children.Add(CreateTableNode(database, view.Name));
            }
        }, database);

    private ConnectionTreeNodeViewModel CreateTableNode(string database, string table) =>
        new(table, DbObjectKind.Table, async node =>
        {
            foreach (var column in await _metadata.GetColumnsAsync(_session!, database, table))
            {
                node.Children.Add(new ConnectionTreeNodeViewModel(
                    $"{column.Name} : {column.DataType}", DbObjectKind.Column));
            }
        }, database);
}
