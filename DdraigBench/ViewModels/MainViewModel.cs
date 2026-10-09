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
    private readonly IConnectionStoreFactory _storeFactory;
    private IConnectionStore? _store;
    private DbSession? _session;
    private QueryTabViewModel? _selectedTab;
    private ConnectionTreeNodeViewModel? _selectedNode;
    private ConnectionProfile? _selectedSavedConnection;
    private string _status = "就绪：请新建一个连接";
    private string _protectorName = "未初始化";
    private bool _secretsEncrypted;
    private string? _securityWarning;

    public MainViewModel(
        IConnectionDialog connectionDialog,
        FreeSqlFactory factory,
        IMetadataExplorer metadata,
        IFilePicker filePicker,
        IExportDialog exportDialog,
        IDdlViewer ddlViewer,
        IConnectionStoreFactory storeFactory)
    {
        _connectionDialog = connectionDialog;
        _factory = factory;
        _metadata = metadata;
        _filePicker = filePicker;
        _exportDialog = exportDialog;
        _ddlViewer = ddlViewer;
        _storeFactory = storeFactory;

        OpenConnectionCommand = ReactiveCommand.CreateFromTask(OpenConnectionAsync);
        OpenSavedConnectionCommand = ReactiveCommand.CreateFromTask(OpenSavedConnectionAsync);
        ForgetSavedConnectionCommand = ReactiveCommand.CreateFromTask(ForgetSavedConnectionAsync);
        NewQueryCommand = ReactiveCommand.Create(NewQuery);
        PreviewDdlCommand = ReactiveCommand.CreateFromTask(PreviewDdlAsync);
    }

    public ObservableCollection<ConnectionTreeNodeViewModel> Databases { get; } = new();

    public ObservableCollection<QueryTabViewModel> Tabs { get; } = new();

    /// <summary>已保存的连接清单（来自 <see cref="ConnectionStore"/>，按名称去重覆盖）。</summary>
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = new();

    public ICommand OpenConnectionCommand { get; }

    public ICommand OpenSavedConnectionCommand { get; }

    public ICommand ForgetSavedConnectionCommand { get; }

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

    public ConnectionProfile? SelectedSavedConnection
    {
        get => _selectedSavedConnection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSavedConnection, value);
            this.RaisePropertyChanged(nameof(CanOpenSavedConnection));
            this.RaisePropertyChanged(nameof(CanForgetSavedConnection));
        }
    }

    /// <summary>当前连接串的保护方式名（启动后由 <see cref="InitializeAsync"/> 填）。</summary>
    public string ProtectorName
    {
        get => _protectorName;
        private set => this.RaiseAndSetIfChanged(ref _protectorName, value);
    }

    /// <summary>连接串是否真被加密（false = 明文回退，UI 必须告警）。</summary>
    public bool SecretsEncrypted
    {
        get => _secretsEncrypted;
        private set => this.RaiseAndSetIfChanged(ref _secretsEncrypted, value);
    }

    /// <summary>安全告警文案（明文回退 / 清单读取失败时非空，界面顶栏显示）。</summary>
    public string? SecurityWarning
    {
        get => _securityWarning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _securityWarning, value);
            this.RaisePropertyChanged(nameof(HasSecurityWarning));
        }
    }

    public bool HasSecurityWarning => SecurityWarning is not null;

    public bool CanOpenSavedConnection => SelectedSavedConnection is not null;

    public bool CanForgetSavedConnection => SelectedSavedConnection is not null;

    public bool CanPreviewDdl =>
        _session is not null
        && _selectedNode is
        {
            Container: not null,
            Kind: DbObjectKind.Table
                or DbObjectKind.View
                or DbObjectKind.Trigger
                or DbObjectKind.StoredProcedure
                or DbObjectKind.Function,
        };

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

    /// <summary>
    /// 启动初始化：挑口令保护方式、读入已保存连接、给出明文回退告警。
    /// 由壳层在窗口打开后调用（密钥库探测在后台线程，见 <see cref="SecretConnectionStoreFactory"/>）；
    /// 任何失败都只体现为告警与状态栏，绝不让应用起不来。
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _store = await _storeFactory.CreateAsync();
            ProtectorName = _store.Protector.Name;
            SecretsEncrypted = _store.Protector.IsEncrypted;

            SavedConnections.Clear();
            foreach (var profile in await _store.LoadAsync())
            {
                Upsert(profile);
            }

            SecurityWarning = SecretsEncrypted
                ? null
                : $"连接口令将以明文保存（保护方式：{ProtectorName}）——未检测到可用的系统密钥库。";

            Status = SavedConnections.Count > 0
                ? $"已载入 {SavedConnections.Count} 个已保存连接"
                : "就绪：请新建一个连接";
        }
        catch (Exception ex)
        {
            SecurityWarning = $"读取已保存连接失败：{ex.Message}（本次会话不会持久化连接）";
            Status = ex.Message;
        }
    }

    /// <summary>新建连接：走对话框，成功后写入已保存清单。</summary>
    public async Task OpenConnectionAsync()
    {
        var profile = await _connectionDialog.ShowAsync();
        if (profile is null)
        {
            return;
        }

        if (!await ConnectAsync(profile))
        {
            return;
        }

        Upsert(profile);
        await PersistAsync();
    }

    /// <summary>打开列表中选中的已保存连接（不重复写盘）。</summary>
    public async Task OpenSavedConnectionAsync()
    {
        if (SelectedSavedConnection is not { } profile)
        {
            Status = "请先在列表中选择一个已保存的连接";
            return;
        }

        await ConnectAsync(profile);
    }

    /// <summary>从列表与配置文件中移除选中的连接。</summary>
    public async Task ForgetSavedConnectionAsync()
    {
        if (SelectedSavedConnection is not { } profile)
        {
            Status = "请先在列表中选择一个已保存的连接";
            return;
        }

        SavedConnections.Remove(profile);
        SelectedSavedConnection = null;
        await PersistAsync();
        Status = $"已移除连接：{profile.Name}";
    }

    /// <summary>真正建立会话并铺开左树；失败只写状态栏并返回 false。</summary>
    private async Task<bool> ConnectAsync(ConnectionProfile profile)
    {
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
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            return false;
        }
    }

    /// <summary>按名称覆盖或追加（连接名是用户可见的身份）。</summary>
    private void Upsert(ConnectionProfile profile)
    {
        var existing = SavedConnections.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is null)
        {
            SavedConnections.Add(profile);
        }
        else
        {
            SavedConnections[SavedConnections.IndexOf(existing)] = profile;
        }
    }

    private async Task PersistAsync()
    {
        if (_store is null)
        {
            Status = "连接清单不可用，本次改动不会保存";
            return;
        }

        try
        {
            await _store.SaveAsync(SavedConnections.ToList());
        }
        catch (Exception ex)
        {
            Status = $"保存连接清单失败：{ex.Message}";
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
        if (_session is null || _selectedNode is not { Container: not null } node)
        {
            Status = "请先连接并选中左侧的表或视图";
            return;
        }

        try
        {
            var ddl = await _metadata.GetDdlAsync(_session, node.Container!, node.Name);
            if (ddl is null)
            {
                Status = $"未找到对象：{node.Container}.{node.Name}";
                return;
            }

            Status = $"DDL 预览：{node.Container}.{node.Name}";
            await _ddlViewer.ShowAsync($"{node.Name} — DDL", ddl);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>
    /// 展开一个库节点：先探模式层，有模式就先铺一层模式节点，没有就直接铺对象。
    /// SQLite（无模式）与 MySQL（库即模式）落进"直接铺对象"的分支，树形与 M5-1 完全一致；
    /// PostgreSQL 则得到「库 → 模式 → 对象」。
    /// </summary>
    private ConnectionTreeNodeViewModel CreateDatabaseNode(string database) =>
        new(database, DbObjectKind.Database, async node =>
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                var schemas = await _metadata.GetObjectsAsync(_session, database, DbObjectKind.Schema);

                if (schemas.Count > 0)
                {
                    foreach (var schema in schemas)
                    {
                        node.Children.Add(CreateSchemaNode(schema.Name));
                    }

                    return;
                }

                await LoadObjectsAsync(node, database);
            }
            catch (Exception ex)
            {
                // 展开是 fire-and-forget（见 ConnectionTreeNodeViewModel），异常必须就地收敛成状态，
                // 否则会成为未观察的任务异常、静默吞掉。
                Status = $"展开 {database} 失败：{ex.Message}";
            }
        }, database);

    /// <summary>模式节点（PostgreSQL）：其子对象与 DDL 预览都以模式名为容器。</summary>
    private ConnectionTreeNodeViewModel CreateSchemaNode(string schema) =>
        new(schema, DbObjectKind.Schema, async node =>
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                await LoadObjectsAsync(node, schema);
            }
            catch (Exception ex)
            {
                Status = $"展开模式 {schema} 失败：{ex.Message}";
            }
        }, schema);

    /// <summary>
    /// 铺开一个容器（库或模式）下的对象：表 / 视图走 IDbFirst，触发器 / 存储过程 / 函数走目录查询补齐
    /// （M5-1，AGENTS.md §6.2）。方言没有这类对象时返回空集，不会报错。
    /// </summary>
    private async Task LoadObjectsAsync(ConnectionTreeNodeViewModel node, string container)
    {
        foreach (var table in await _metadata.GetObjectsAsync(_session!, container, DbObjectKind.Table))
        {
            node.Children.Add(CreateObjectNode(container, table.Name, DbObjectKind.Table));
        }

        foreach (var view in await _metadata.GetObjectsAsync(_session!, container, DbObjectKind.View))
        {
            node.Children.Add(CreateObjectNode(container, view.Name, DbObjectKind.View));
        }

        foreach (var kind in new[]
                 {
                     DbObjectKind.Trigger, DbObjectKind.StoredProcedure, DbObjectKind.Function,
                 })
        {
            foreach (var item in await _metadata.GetObjectsAsync(_session!, container, kind))
            {
                node.Children.Add(new ConnectionTreeNodeViewModel(item.Name, kind, container: container));
            }
        }
    }

    private ConnectionTreeNodeViewModel CreateObjectNode(string container, string name, DbObjectKind kind) =>
        new(name, kind, async node =>
        {
            if (_session is null)
            {
                return;
            }

            try
            {
                foreach (var column in await _metadata.GetColumnsAsync(_session, container, name))
                {
                    node.Children.Add(new ConnectionTreeNodeViewModel(
                        $"{column.Name} : {column.DataType}", DbObjectKind.Column));
                }
            }
            catch (Exception ex)
            {
                Status = $"读取 {name} 的列失败：{ex.Message}";
            }
        }, container);
}
