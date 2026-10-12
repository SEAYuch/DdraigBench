// DdraigBench — 主窗口 VM（连接树 + 查询标签 + 全局命令）

using System.Collections.ObjectModel;
using System.Windows.Input;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Dialects;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Sessions;
using ReactiveUI;
using System.Runtime.CompilerServices;
using DdraigBench.Services;

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
    private readonly IThemePreferenceStore? _themePreferenceStore;
    private readonly Func<ThemeMode, Task>? _applyTheme;
    private IConnectionStore? _store;
    private DbSession? _session;
    private bool _isConnecting;
    private QueryTabViewModel? _selectedTab;
    private ConnectionTreeNodeViewModel? _selectedNode;
    private ConnectionProfile? _selectedSavedConnection;
    private string _status = "就绪：请新建一个连接";
    private string _protectorName = "未初始化";
    private bool _secretsEncrypted;
    private string? _securityWarning;
    private string _treeFilter = string.Empty;
    private bool _isNavigatorVisible = true;
    private int _sessionIdentity;
    private ThemeMode _selectedThemeMode = ThemeMode.System;

    public MainViewModel(
        IConnectionDialog connectionDialog,
        FreeSqlFactory factory,
        IMetadataExplorer metadata,
        IFilePicker filePicker,
        IExportDialog exportDialog,
        IDdlViewer ddlViewer,
        IConnectionStoreFactory storeFactory,
        IThemePreferenceStore? themePreferenceStore = null,
        Func<ThemeMode, Task>? applyTheme = null)
    {
        _connectionDialog = connectionDialog;
        _factory = factory;
        _metadata = metadata;
        _filePicker = filePicker;
        _exportDialog = exportDialog;
        _ddlViewer = ddlViewer;
        _storeFactory = storeFactory;
        _themePreferenceStore = themePreferenceStore;
        _applyTheme = applyTheme;

        OpenConnectionCommand = ReactiveCommand.CreateFromTask(OpenConnectionAsync);
        OpenSavedConnectionCommand = ReactiveCommand.CreateFromTask(OpenSavedConnectionAsync);
        ForgetSavedConnectionCommand = ReactiveCommand.CreateFromTask(ForgetSavedConnectionAsync);
        NewQueryCommand = ReactiveCommand.Create(NewQuery);
        ExecuteSelectedQueryCommand = ReactiveCommand.CreateFromTask(ExecuteSelectedQueryAsync);
        PreviewDdlCommand = ReactiveCommand.CreateFromTask<ConnectionTreeNodeViewModel>(PreviewDdlAsync);
        PreviewSelectedDdlCommand = ReactiveCommand.CreateFromTask(() => PreviewDdlAsync(SelectedNode));
        OpenDataCommand = ReactiveCommand.CreateFromTask<ConnectionTreeNodeViewModel>(OpenDataAsync);
        OpenSelectedDataCommand = ReactiveCommand.CreateFromTask(() => OpenDataAsync(SelectedNode));
        RetryLoadCommand = ReactiveCommand.CreateFromTask<ConnectionTreeNodeViewModel>(RetryLoadAsync);
        RefreshSelectedDataCommand = ReactiveCommand.CreateFromTask(RefreshSelectedDataAsync);
        ToggleNavigatorCommand = ReactiveCommand.Create(() => IsNavigatorVisible = !IsNavigatorVisible);
        Databases.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (ConnectionTreeNodeViewModel oldNode in e.OldItems)
                {
                    oldNode.PropertyChanged -= OnDatabaseNodeChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (ConnectionTreeNodeViewModel newNode in e.NewItems)
                {
                    newNode.PropertyChanged += OnDatabaseNodeChanged;
                }
            }

            RefreshVisibleDatabases();
        };
    }

    public ObservableCollection<ConnectionTreeNodeViewModel> Databases { get; } = new();

    public ObservableCollection<ConnectionTreeNodeViewModel> VisibleDatabases { get; } = new();

    public ObservableCollection<QueryTabViewModel> Tabs { get; } = new();

    /// <summary>已保存的连接清单（来自 <see cref="ConnectionStore"/>，按名称去重覆盖）。</summary>
    public ObservableCollection<ConnectionProfile> SavedConnections { get; } = new();

    public ICommand OpenConnectionCommand { get; }

    public ICommand OpenSavedConnectionCommand { get; }

    public ICommand ForgetSavedConnectionCommand { get; }

    public ICommand NewQueryCommand { get; }

    public ICommand ExecuteSelectedQueryCommand { get; }

    public ICommand PreviewDdlCommand { get; }

    public ICommand PreviewSelectedDdlCommand { get; }

    public ICommand OpenDataCommand { get; }

    public ICommand OpenSelectedDataCommand { get; }

    /// <summary>树节点展开失败后的「重试加载」（M9，2026-10-11）。</summary>
    public ICommand RetryLoadCommand { get; }

    public ICommand RefreshSelectedDataCommand { get; }

    public ICommand ToggleNavigatorCommand { get; }

    public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>();

    public IReadOnlyList<string> ThemeOptions { get; } = ["跟随系统", "浅色", "深色"];

    public string SelectedThemeName
    {
        get => SelectedThemeMode switch
        {
            ThemeMode.Light => "浅色",
            ThemeMode.Dark => "深色",
            _ => "跟随系统",
        };
        set
        {
            var mode = value switch
            {
                "浅色" => ThemeMode.Light,
                "深色" => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
            _ = SetThemeAsync(mode);
        }
    }

    public ThemeMode SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (_selectedThemeMode == value)
            {
                return;
            }

            _selectedThemeMode = value;
            this.RaisePropertyChanged();
            _ = SetThemeAsync(value);
        }
    }

    public string ActiveConnectionName => _session?.Profile.Name ?? "未连接";

    public bool IsConnected => _session is not null;

    public bool IsNavigatorVisible
    {
        get => _isNavigatorVisible;
        set => this.RaiseAndSetIfChanged(ref _isNavigatorVisible, value);
    }

    public string TreeFilter
    {
        get => _treeFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _treeFilter, value);
            foreach (var database in Databases)
            {
                database.ApplyFilter(value);
            }

            RefreshVisibleDatabases();
        }
    }

    /// <summary>左树选中节点（DDL 预览的来源）。</summary>
    public ConnectionTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNode, value);
            this.RaisePropertyChanged(nameof(CanPreviewDdl));
            this.RaisePropertyChanged(nameof(CanOpenSelectedData));
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
                or DbObjectKind.Function
                or DbObjectKind.Event,
        };

    public bool CanOpenSelectedData => _session is not null && _selectedNode?.CanOpenData == true;

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
            // 密钥库探测与连接串解密都可能**同步阻塞**（macOS 的 SecItem* 是同步 P/Invoke，
            // 钥匙串被锁时还会等授权框）→ 一律离开 UI 线程（AGENTS §5）。
            var (store, loaded) = await Task.Run(async () =>
            {
                var created = await _storeFactory.CreateAsync();
                return (Created: created, Profiles: await created.LoadAsync());
            });

            _store = store;
            ProtectorName = store.Protector.Name;
            SecretsEncrypted = store.Protector.IsEncrypted;

            SavedConnections.Clear();
            foreach (var profile in loaded)
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
            // 读取失败即放弃本次会话的持久化能力：内存里的清单是空的/残缺的，
            // 若继续持有 store，后续「新建/移除连接」的 PersistAsync 会用它覆盖
            // connections.json，把读不出来的条目（含其密文口令）永久抹掉。
            // 置 null 后 PersistAsync 走「连接清单不可用，本次改动不会保存」分支，
            // 与下面的告警文案一致。
            _store = null;

            SecurityWarning = $"读取已保存连接失败：{ex.Message}（本次会话不会持久化连接）";
            Status = ex.Message;
        }

        // 放在最后：偏好读取失败要在状态栏里盖过上面那句"就绪/已载入 N 个连接"（告警优先）。
        await LoadThemePreferenceAsync();
    }

    /// <summary>
    /// 读界面偏好并应用。<b>任何失败都不得外抛</b>：调用方是壳层 <c>window.Opened</c> 的
    /// async void 处理器（DdraigBench/App.axaml.cs），异常会变成 UI 线程未处理异常、启动即崩；
    /// 故此处兜底成"跟随系统 + 状态栏"（读不到偏好文件时 ThemePreferenceStore 自己已回退）。
    /// </summary>
    private async Task LoadThemePreferenceAsync()
    {
        if (_themePreferenceStore is null)
        {
            return;
        }

        try
        {
            _selectedThemeMode = await _themePreferenceStore.LoadAsync();
            this.RaisePropertyChanged(nameof(SelectedThemeMode));
            this.RaisePropertyChanged(nameof(SelectedThemeName));
            if (_applyTheme is not null)
            {
                await _applyTheme(SelectedThemeMode);
            }
        }
        catch (Exception ex)
        {
            _selectedThemeMode = ThemeMode.System;
            this.RaisePropertyChanged(nameof(SelectedThemeMode));
            this.RaisePropertyChanged(nameof(SelectedThemeName));
            Status = $"读取界面偏好失败，已回退为跟随系统：{ex.Message}";
        }
    }

    public async Task SetThemeAsync(ThemeMode mode)
    {
        _selectedThemeMode = mode;
        this.RaisePropertyChanged(nameof(SelectedThemeMode));
        this.RaisePropertyChanged(nameof(SelectedThemeName));
        try
        {
            if (_applyTheme is not null)
            {
                await _applyTheme(mode);
            }

            if (_themePreferenceStore is not null)
            {
                await _themePreferenceStore.SaveAsync(mode);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"主题已切换，但无法保存偏好：{ex.Message}";
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

    /// <summary>
    /// 建立连接（并发守卫）。连点两个连接 / 双击两下会把两次连接叠加执行：旧标签被清两次、
    /// 旧会话被重复处置，先建好的会话还会被后一次的 <c>_session</c> 覆盖而泄漏。
    /// </summary>
    private async Task<bool> ConnectAsync(ConnectionProfile profile)
    {
        if (_isConnecting)
        {
            Status = "正在连接，请稍候…";
            return false;
        }

        _isConnecting = true;
        try
        {
            return await ConnectCoreAsync(profile);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    /// <summary>真正建立会话并铺开左树；失败只写状态栏并返回 false。</summary>
    private async Task<bool> ConnectCoreAsync(ConnectionProfile profile)
    {
        // 换连接前先收掉旧标签（其未提交事务会被回滚、租约归还）
        foreach (var tab in Tabs)
        {
            await tab.DisposeAsync();
        }

        Tabs.Clear();
        SelectedTab = null;
        SelectedNode = null;

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = new DbSession(profile, _factory);
        _sessionIdentity = RuntimeHelpers.GetHashCode(_session);

        Databases.Clear();
        try
        {
            var databases = await _metadata.GetDatabasesAsync(_session);
            foreach (var database in databases)
            {
                var node = CreateDatabaseNode(database, _session);
                node.ApplyFilter(TreeFilter);
                Databases.Add(node);
            }

            Status = $"已连接 {profile.Name}：{databases.Count} 个数据库";
            this.RaisePropertyChanged(nameof(ActiveConnectionName));
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(CanPreviewDdl));
            NewQuery();
            return true;
        }
        catch (Exception ex)
        {
            // 失败不留死会话：_session 非 null 会让 IsConnected / 连接名显示成"已连接"，
            // 而后续每个查询都报错，且这个会话再没人处置。收拾干净并通知 UI。
            await _session.DisposeAsync();
            _session = null;
            _sessionIdentity = 0;

            Status = ex.Message;
            this.RaisePropertyChanged(nameof(ActiveConnectionName));
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(CanPreviewDdl));
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

    private void RefreshVisibleDatabases()
    {
        VisibleDatabases.Clear();
        foreach (var database in Databases)
        {
            if (database.IsVisible)
            {
                VisibleDatabases.Add(database);
            }
        }
    }

    private void OnDatabaseNodeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionTreeNodeViewModel.IsVisible) or nameof(ConnectionTreeNodeViewModel.VisibleChildren))
        {
            RefreshVisibleDatabases();
        }
    }

    private async Task PersistAsync()
    {
        var store = _store;
        if (store is null)
        {
            Status = "连接清单不可用，本次改动不会保存";
            return;
        }

        // 快照必须在 UI 线程取（SavedConnections 是绑定集合），加密与落盘则离开 UI 线程：
        // 密钥库写入同样可能是同步 P/Invoke / 弹授权框（AGENTS §5）。
        var snapshot = SavedConnections.ToList();

        try
        {
            await Task.Run(() => store.SaveAsync(snapshot));
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

    public async Task OpenDataAsync(ConnectionTreeNodeViewModel? node)
    {
        if (_session is null || node is not { Container: not null, Kind: DbObjectKind.Table or DbObjectKind.View })
        {
            Status = "请选择一个表或视图以浏览数据";
            return;
        }

        var key = string.Join('\u001f', _sessionIdentity, _session.Profile.DataType,
            node.Container, node.Kind, node.Name);
        var existing = Tabs.FirstOrDefault(tab => tab.IsDataBrowser && tab.BrowserObjectKey == key);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        if (!SqlDialects.TryGet(_session.Profile.DataType, out var dialect))
        {
            Status = $"该方言暂不支持数据浏览：{_session.Profile.DataType}";
            return;
        }

        // PostgreSQL and SQL Server are already connected to the selected database; their tree container is the schema.
        var sql = BuildDataSelect(_session.Profile.DataType, node.Container, node.Name);
        if (node.Kind == DbObjectKind.Table)
        {
            var primaryKeyColumns = (await _metadata.GetColumnsAsync(_session, node.Container, node.Name))
                .Where(column => column.IsPrimary)
                .Select(column => dialect.QuoteIdentifier(column.Name))
                .ToArray();
            if (primaryKeyColumns.Length > 0)
            {
                sql += $" ORDER BY {string.Join(", ", primaryKeyColumns)}";
            }
        }
        var tab = new QueryTabViewModel($"{node.Name}（数据）", () => _session, _filePicker, _exportDialog, key, node.Name)
        {
            Sql = sql,
        };
        Tabs.Add(tab);
        SelectedTab = tab;
        await tab.ExecuteAsync();
    }

    public static string BuildDataSelect(FreeSql.DataType dataType, string container, string objectName)
    {
        var dialect = SqlDialects.Get(dataType);
        return $"SELECT * FROM {dialect.QuoteIdentifier(container)}.{dialect.QuoteIdentifier(objectName)}";
    }

    public async Task RefreshSelectedDataAsync()
    {
        if (SelectedTab is not { IsDataBrowser: true } tab)
        {
            return;
        }

        await tab.ExecuteAsync();
    }

    public async Task ExecuteSelectedQueryAsync()
    {
        if (SelectedTab is { IsQueryEditor: true } tab)
        {
            await tab.ExecuteAsync();
        }
    }

    public Task PreviewDdlAsync() => PreviewDdlAsync(SelectedNode);

    /// <summary>
    /// 重新加载一个树节点的子项（M9，2026-10-11）：展开是 fire-and-forget，失败后节点会被记成"已加载且为空"，
    /// 用户既看不到对象也没有入口。这里由树右键「重试加载」调用，失败信息照旧收敛进状态栏。
    /// </summary>
    public async Task RetryLoadAsync(ConnectionTreeNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        Status = $"正在重新加载 {node.Name}…";
        await node.ReloadAsync();
        if (!node.LoadFailed)
        {
            Status = $"已重新加载 {node.Name}";
        }
    }

    public async Task PreviewDdlAsync(ConnectionTreeNodeViewModel? selectedNode)
    {
        if (_session is null || selectedNode is not { Container: not null, Kind: not null } node
            || node.Kind is not (DbObjectKind.Table or DbObjectKind.View or DbObjectKind.Trigger
                or DbObjectKind.StoredProcedure or DbObjectKind.Function or DbObjectKind.Event))
        {
            Status = "请先连接并选中左侧的表、视图、触发器、例程或事件";
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
    /// PostgreSQL 与 SQL Server 则得到「库 → 模式 → 对象」。
    /// </summary>
    private ConnectionTreeNodeViewModel CreateDatabaseNode(string database, DbSession session) =>
        new(database, DbObjectKind.Database, async node =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            try
            {
                var schemas = await _metadata.GetObjectsAsync(session, database, DbObjectKind.Schema);

                if (schemas.Count > 0)
                {
                    foreach (var schema in schemas)
                    {
                        node.Children.Add(CreateSchemaNode(schema.Name, session));
                    }

                    return;
                }

                await LoadObjectsAsync(node, database, session);
            }
            catch (Exception ex)
            {
                // 展开是 fire-and-forget（见 ConnectionTreeNodeViewModel），异常必须就地收敛成状态，
                // 否则会成为未观察的任务异常、静默吞掉。
                Status = $"展开 {database} 失败：{ex.Message}";
                node.ReportLoadFailure();
            }
        }, database);

    /// <summary>模式节点（PostgreSQL / SQL Server）：其子对象与 DDL 预览都以模式名为容器。</summary>
    private ConnectionTreeNodeViewModel CreateSchemaNode(string schema, DbSession session) =>
        new(schema, DbObjectKind.Schema, async node =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            try
            {
                await LoadObjectsAsync(node, schema, session);
            }
            catch (Exception ex)
            {
                Status = $"展开模式 {schema} 失败：{ex.Message}";
                node.ReportLoadFailure();
            }
        }, schema);

    /// <summary>
    /// 铺开一个容器（库或模式）下的对象：表 / 视图走 IDbFirst，触发器 / 存储过程 / 函数走目录查询补齐
    /// （M5-1；SQL Server M5-3（待靶机验证），AGENTS.md §6.2）。方言没有这类对象时返回空集，不会报错。
    /// </summary>
    private async Task LoadObjectsAsync(ConnectionTreeNodeViewModel node, string container, DbSession session)
    {
        foreach (var (kind, label) in new[]
                 {
                     (DbObjectKind.Table, "表"),
                     (DbObjectKind.View, "视图"),
                     (DbObjectKind.Trigger, "触发器"),
                     (DbObjectKind.StoredProcedure, "存储过程"),
                     (DbObjectKind.Function, "函数"),
                     (DbObjectKind.Event, "事件"),
                 })
        {
            var objects = await _metadata.GetObjectsAsync(session, container, kind);
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            if (objects.Count == 0)
            {
                continue;
            }

            var group = new ConnectionTreeNodeViewModel(label, null, container: container, groupLabel: label);
            foreach (var item in objects)
            {
                var child = kind is DbObjectKind.Table or DbObjectKind.View
                    ? CreateObjectNode(container, item.Name, kind, session)
                    : new ConnectionTreeNodeViewModel(item.Name, kind, container: container);
                group.Children.Add(child);
            }

            node.Children.Add(group);
        }
    }

    private ConnectionTreeNodeViewModel CreateObjectNode(string container, string name, DbObjectKind kind, DbSession session) =>
        new(name, kind, async node =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            try
            {
                foreach (var column in await _metadata.GetColumnsAsync(session, container, name))
                {
                    if (!ReferenceEquals(_session, session))
                    {
                        return;
                    }

                    node.Children.Add(new ConnectionTreeNodeViewModel(
                        $"{column.Name} : {column.DataType}", DbObjectKind.Column));
                }

                if (kind == DbObjectKind.Table)
                {
                    foreach (var partition in await _metadata.GetPartitionsAsync(session, container, name))
                    {
                        if (!ReferenceEquals(_session, session))
                        {
                            return;
                        }

                        var description = string.IsNullOrWhiteSpace(partition.Description)
                            ? string.Empty
                            : $" < {partition.Description}";
                        node.Children.Add(new ConnectionTreeNodeViewModel(
                            $"{partition.Name} ({partition.Method}: {partition.Expression}{description})",
                            DbObjectKind.Partition));
                    }
                }
            }
            catch (Exception ex)
            {
                Status = $"读取 {name} 的列/分区失败：{ex.Message}";
                node.ReportLoadFailure();
            }
        }, container);
}
