// DdraigBench — MainViewModel / QueryTabViewModel 单测（假对话框 + 临时 SQLite）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

public sealed class MainViewModelTests
{
    private sealed class StubThemePreferenceStore(ThemeMode mode = ThemeMode.System) : IThemePreferenceStore
    {
        public ThemeMode Mode { get; private set; } = mode;

        public int SaveCount { get; private set; }

        public Exception? LoadFailure { get; init; }

        public Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default) =>
            LoadFailure is not null ? Task.FromException<ThemeMode>(LoadFailure) : Task.FromResult(Mode);

        public Task SaveAsync(ThemeMode mode, CancellationToken cancellationToken = default)
        {
            Mode = mode;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubConnectionDialog(ConnectionProfile? profile) : IConnectionDialog
    {
        public Task<ConnectionProfile?> ShowAsync() => Task.FromResult(profile);
    }

    private static ConnectionProfile SqliteProfile(TempSqliteDatabase db) =>
        new(Path.GetFileName(db.FilePath), DataType.Sqlite, db.ConnectionString);

    private static MainViewModel CreateViewModel(TempSqliteDatabase db, IConnectionStoreFactory? storeFactory = null) =>
        new(
            new StubConnectionDialog(SqliteProfile(db)),
            new FreeSqlFactory(),
            new FreeSqlMetadataExplorer(),
            new StubFilePicker(),
            new StubExportDialog(),
            new StubDdlViewer(),
            storeFactory ?? new StubConnectionStoreFactory(new StubConnectionStore()));

    private static MainViewModel CreateViewModelWithStore(ConnectionProfile? dialogProfile, IConnectionStoreFactory storeFactory) =>
        new(
            new StubConnectionDialog(dialogProfile),
            new FreeSqlFactory(),
            new FreeSqlMetadataExplorer(),
            new StubFilePicker(),
            new StubExportDialog(),
            new StubDdlViewer(),
            storeFactory);

    private static MainViewModel CreateViewModelWithExplorer(ConnectionProfile dialogProfile, IMetadataExplorer explorer) =>
        new(
            new StubConnectionDialog(dialogProfile),
            new FreeSqlFactory(),
            explorer,
            new StubFilePicker(),
            new StubExportDialog(),
            new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));

    [Fact]
    public async Task Open_populates_databases_and_opens_a_query_tab()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");
        var vm = CreateViewModel(db);

        await vm.OpenConnectionAsync();

        Assert.Contains("main", vm.Databases.Select(d => d.Name));
        Assert.Single(vm.Tabs);
        Assert.NotNull(vm.SelectedTab);
    }

    [Fact]
    public async Task Cancelled_dialog_changes_nothing()
    {
        var vm = new MainViewModel(
            new StubConnectionDialog(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));

        await vm.OpenConnectionAsync();

        Assert.Empty(vm.Databases);
        Assert.Empty(vm.Tabs);
    }

    [Fact]
    public async Task Failed_connect_does_not_leave_a_dead_session()
    {
        using var db = new TempSqliteDatabase();
        var vm = CreateViewModelWithExplorer(
            SqliteProfile(db),
            new GatedMetadataExplorer { DatabasesFailure = new InvalidOperationException("库清单不可用") });

        await vm.InitializeAsync();
        await vm.OpenConnectionAsync();

        // 失败后必须回到"未连接"：否则 IsConnected / 连接名会谎报已连接，
        // 而后续每个查询都报错（且那个会话再没人处置）。
        Assert.False(vm.IsConnected);
        Assert.Equal("未连接", vm.ActiveConnectionName);
        Assert.Equal("库清单不可用", vm.Status);
        Assert.Empty(vm.Databases);
    }

    [Fact]
    public async Task Concurrent_connect_is_rejected_instead_of_stacking()
    {
        using var db = new TempSqliteDatabase();
        var explorer = new GatedMetadataExplorer();
        var vm = CreateViewModelWithExplorer(SqliteProfile(db), explorer);
        await vm.InitializeAsync();

        var first = vm.OpenConnectionAsync();

        // 第一次已卡在库清单查询上：此时再连必须被守卫挡下，而不是叠加执行。
        Assert.Equal(1, explorer.DatabaseQueryCount);
        var second = vm.OpenConnectionAsync();
        Assert.Contains("正在连接", vm.Status);

        explorer.ReleaseDatabases();
        await Task.WhenAll(first, second);

        Assert.Equal(1, explorer.DatabaseQueryCount);
        Assert.True(vm.IsConnected);
        Assert.Contains("main", vm.Databases.Select(d => d.Name));
    }

    [Fact]
    public async Task Failed_load_disables_persistence_instead_of_overwriting_the_file()
    {
        using var db = new TempSqliteDatabase();
        var store = new StubConnectionStore { LoadFailure = new IOException("读取失败") };
        var vm = CreateViewModelWithStore(SqliteProfile(db), new StubConnectionStoreFactory(store));

        await vm.InitializeAsync();

        Assert.Equal(0, store.SaveCount);
        Assert.Equal("读取失败", vm.Status);
        Assert.Contains("本次会话不会持久化连接", vm.SecurityWarning);

        // 读取失败后仍能新建连接（只落在内存里），但绝不能再把空/残缺清单写回文件
        await vm.OpenConnectionAsync();

        Assert.Single(vm.SavedConnections);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Database_node_loads_tables_on_first_expand()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");
        var vm = CreateViewModel(db);
        await vm.OpenConnectionAsync();

        var database = vm.Databases.Single(d => d.Name == "main");
        Assert.Empty(database.Children);

        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Count > 0);

        var tableGroup = Assert.Single(database.Children);
        Assert.Equal("表", tableGroup.Name);
        Assert.Contains(tableGroup.Children, c => c.Name == "t1");
    }

    [Fact]
    public async Task Query_tab_executes_select_into_columns_and_rows()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");
        var vm = CreateViewModel(db);
        await vm.OpenConnectionAsync();

        var tab = vm.SelectedTab!;
        tab.Sql = "SELECT 1 AS n";
        await tab.ExecuteAsync();

        Assert.Equal(new[] { "n" }, tab.Columns);
        Assert.Single(tab.Rows);
        Assert.Contains("1 行", tab.Status);
    }

    [Fact]
    public async Task Query_tab_reports_when_not_connected()
    {
        var vm = new MainViewModel(
            new StubConnectionDialog(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        vm.NewQuery();

        await vm.SelectedTab!.ExecuteAsync();

        Assert.Contains("未连接", vm.SelectedTab.Status);
    }

    [Fact]
    public async Task Preview_ddl_renders_script_for_selected_table()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");

        var viewer = new StubDdlViewer();
        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), viewer,
            new StubConnectionStoreFactory(new StubConnectionStore()));

        await vm.OpenConnectionAsync();

        var databaseNode = vm.Databases.Single();
        Assert.False(vm.CanPreviewDdl);

        databaseNode.IsExpanded = true;
        await WaitUntilAsync(() => databaseNode.Children.Count > 0);

        vm.SelectedNode = databaseNode.Children.Single().Children.Single();
        Assert.True(vm.CanPreviewDdl);

        await vm.PreviewDdlAsync();

        Assert.Equal(1, viewer.ShowCount);
        // M5-1 起 SQLite 的表 DDL 走 sqlite_master.sql 原文（保真优先，见 AGENTS.md §6.2），
        // 因此这里断言的是原始脚本而非 DdlGenerator 的加引号重建版本（后者由 DdlGeneratorTests 覆盖）。
        Assert.Contains("CREATE TABLE t1", viewer.LastDdl);
        Assert.Contains("name TEXT", viewer.LastDdl);
        Assert.EndsWith(";", viewer.LastDdl);
        Assert.Contains("DDL 预览", vm.Status);
    }

    [Fact]
    public async Task Preview_ddl_without_selection_reports_hint()
    {
        var vm = new MainViewModel(
            new StubConnectionDialog(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        vm.NewQuery();

        await vm.PreviewDdlAsync();

        Assert.Contains("请先连接", vm.Status);
    }

    [Fact]
    public async Task Initialize_loads_saved_connections_and_reports_protector()
    {
        var saved = new ConnectionProfile("生产 MySQL", DataType.MySql, "Server=db;Password=pw");
        var store = new StubConnectionStore([saved]);
        var vm = CreateViewModelWithStore(null, new StubConnectionStoreFactory(store));

        await vm.InitializeAsync();

        Assert.Equal(saved, Assert.Single(vm.SavedConnections));
        Assert.True(vm.SecretsEncrypted);
        Assert.Equal("stub-encrypted", vm.ProtectorName);
        Assert.False(vm.HasSecurityWarning);
        Assert.Contains("1 个已保存连接", vm.Status);
    }

    [Fact]
    public async Task Plaintext_fallback_raises_a_visible_warning()
    {
        var store = new StubConnectionStore(isEncrypted: false);
        var vm = CreateViewModelWithStore(null, new StubConnectionStoreFactory(store));

        await vm.InitializeAsync();

        Assert.False(vm.SecretsEncrypted);
        Assert.True(vm.HasSecurityWarning);
        Assert.Contains("明文", vm.SecurityWarning);
        Assert.Contains("plaintext", vm.SecurityWarning);
    }

    [Fact]
    public async Task Store_failure_does_not_break_startup()
    {
        var store = new StubConnectionStore { CreateFailure = new IOException("配置文件不可读") };
        var vm = CreateViewModelWithStore(null, new StubConnectionStoreFactory(store));

        await vm.InitializeAsync();

        Assert.Empty(vm.SavedConnections);
        Assert.True(vm.HasSecurityWarning);
        Assert.Contains("不会持久化", vm.SecurityWarning);

        // 存储不可用时新建连接仍要能用（只是不落盘）。
        using var db = new TempSqliteDatabase();
        var withDb = CreateViewModel(db, new StubConnectionStoreFactory(store));
        await withDb.OpenConnectionAsync();

        Assert.Contains("main", withDb.Databases.Select(d => d.Name));
    }

    [Fact]
    public async Task Store_probe_and_load_do_not_run_on_the_calling_thread()
    {
        using var db = new TempSqliteDatabase();
        var factory = new ThreadRecordingConnectionStoreFactory();
        var vm = CreateViewModel(db, factory);

        var callerThreadId = 0;
        var createThreadId = 0;
        var loadThreadId = 0;
        var saveThreadId = 0;
        Exception? failure = null;

        // 自建线程冒充 UI 线程：它不属于线程池，故 Task.Run 里的存储探测必然落在别的线程上（确定性）
        var caller = new Thread(() =>
        {
            try
            {
                callerThreadId = Environment.CurrentManagedThreadId;
                vm.InitializeAsync().GetAwaiter().GetResult();
                createThreadId = factory.CreateThreadId;
                loadThreadId = factory.LoadThreadId;

                vm.OpenConnectionAsync().GetAwaiter().GetResult();
                saveThreadId = factory.SaveThreadId;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        caller.Start();
        Assert.True(caller.Join(TimeSpan.FromSeconds(30)), "InitializeAsync 未在 30 秒内完成");

        Assert.Null(failure);
        Assert.NotEqual(0, callerThreadId);
        Assert.NotEqual(0, createThreadId);
        Assert.NotEqual(0, loadThreadId);
        Assert.NotEqual(0, saveThreadId);
        Assert.NotEqual(callerThreadId, createThreadId);
        Assert.NotEqual(callerThreadId, loadThreadId);
        Assert.NotEqual(callerThreadId, saveThreadId);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task New_connection_is_persisted_and_deduped_by_name()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY)");

        var store = new StubConnectionStore();
        var vm = CreateViewModel(db, new StubConnectionStoreFactory(store));
        await vm.InitializeAsync();

        await vm.OpenConnectionAsync();

        Assert.Single(vm.SavedConnections);
        Assert.Equal(1, store.SaveCount);

        // 同名再连一次应覆盖而不是追加。
        await vm.OpenConnectionAsync();
        Assert.Single(vm.SavedConnections);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task Saved_connection_opens_and_can_be_forgotten()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY)");
        var profile = SqliteProfile(db);

        var store = new StubConnectionStore([profile]);
        var vm = CreateViewModelWithStore(null, new StubConnectionStoreFactory(store));
        await vm.InitializeAsync();

        Assert.False(vm.CanOpenSavedConnection);
        vm.SelectedSavedConnection = profile;
        Assert.True(vm.CanOpenSavedConnection);
        Assert.True(vm.CanForgetSavedConnection);

        await vm.OpenSavedConnectionAsync();
        Assert.Contains("main", vm.Databases.Select(d => d.Name));
        Assert.Equal(0, store.SaveCount); // 打开已保存连接不重写文件

        await vm.ForgetSavedConnectionAsync();
        Assert.Empty(vm.SavedConnections);
        Assert.Null(vm.SelectedSavedConnection);
        Assert.Empty(store.Saved);
        Assert.Equal(1, store.SaveCount);
        Assert.Contains("已移除连接", vm.Status);
    }

    [Fact]
    public async Task Saved_connection_commands_report_when_nothing_selected()
    {
        var vm = CreateViewModelWithStore(null, new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.InitializeAsync();

        await vm.OpenSavedConnectionAsync();
        Assert.Contains("请先", vm.Status);

        await vm.ForgetSavedConnectionAsync();
        Assert.Contains("请先", vm.Status);
    }

    [Fact]
    public async Task Database_node_lists_triggers_and_their_ddl_can_be_previewed()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery(
            """
            CREATE TABLE t1 (id INTEGER PRIMARY KEY);
            CREATE TRIGGER trg_t1 AFTER INSERT ON t1 BEGIN UPDATE t1 SET id = id; END;
            """);

        var viewer = new StubDdlViewer();
        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), viewer,
            new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.OpenConnectionAsync();

        var database = vm.Databases.Single();
        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Any(c => c.Name == "触发器"));

        // §6.2 缺口：IDbFirst 完全看不到触发器，M5-1 起由目录查询补齐
        var trigger = database.Children.Single(c => c.Name == "触发器").Children.Single(c => c.Name == "trg_t1");
        Assert.Equal(DbObjectKind.Trigger, trigger.Kind);

        vm.SelectedNode = trigger;
        Assert.True(vm.CanPreviewDdl);

        await vm.PreviewDdlAsync();

        Assert.Equal(1, viewer.ShowCount);
        Assert.Contains("CREATE TRIGGER trg_t1", viewer.LastDdl);
    }

    [Fact]
    public async Task Expand_failure_is_reported_instead_of_crashing_the_tree()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY)");

        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), new ThrowingMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.OpenConnectionAsync();

        vm.Databases.Single().IsExpanded = true;

        // 展开是 fire-and-forget，异常必须收敛到状态栏（否则成为未观察的任务异常）
        await WaitUntilAsync(() => vm.Status.Contains("失败"));
        Assert.Contains("展开 main 失败", vm.Status);
    }

    [Fact]
    public async Task Failed_expansion_is_retried_by_expanding_the_node_again()
    {
        using var db = new TempSqliteDatabase();
        var explorer = new FlakyMetadataExplorer(failures: 1);

        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), explorer,
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.OpenConnectionAsync();

        var database = vm.Databases.Single();
        database.IsExpanded = true;
        await WaitUntilAsync(() => database.LoadFailed);

        Assert.True(database.LoadFailed);
        Assert.Empty(database.Children);

        // M9：失败不能被记成"已加载且为空"。旧实现把 _loaded 留在 true，
        // 第二次展开会被直接跳过，用户再也看不到对象、也没有任何重试入口。
        database.IsExpanded = false;
        database.IsExpanded = true;

        await WaitUntilAsync(() => database.Children.Count > 0);

        Assert.Contains(database.Children, c => c.Name == "表");
        Assert.False(database.LoadFailed);
    }

    [Fact]
    public async Task Failed_expansion_drops_the_half_built_subtree_and_retries_from_the_menu()
    {
        using var db = new TempSqliteDatabase();

        // 表查得出来、触发器查询炸掉：失败那一刻「表」组已经在节点上了
        var explorer = new FlakyMetadataExplorer(failures: 1, failOnKind: DbObjectKind.Trigger);

        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), explorer,
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.OpenConnectionAsync();

        var database = vm.Databases.Single();
        database.IsExpanded = true;
        await WaitUntilAsync(() => database.LoadFailed);

        // 半个子树必须清掉，否则重试会在同一节点上再叠一组「表」
        Assert.Empty(database.Children);

        // 树右键「重试加载」（MainWindow.axaml 的 ObjectContextMenu → RetryLoadCommand）
        await vm.RetryLoadAsync(database);

        Assert.False(database.LoadFailed);
        Assert.Equal(["表"], database.Children.Select(c => c.Name));
        Assert.Contains("已重新加载 main", vm.Status);
    }

    [Fact]
    public async Task Schema_level_is_inserted_when_the_dialect_reports_schemas()
    {
        using var db = new TempSqliteDatabase();
        var explorer = new ScriptedMetadataExplorer
        {
            Databases = ["app"],
            Schemas = ["public", "sales"],
            Tables = new Dictionary<string, IReadOnlyList<string>>
            {
                ["public"] = ["t1"],
                ["sales"] = ["t2"],
            },
        };

        var viewer = new StubDdlViewer();
        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), explorer,
            new StubFilePicker(), new StubExportDialog(), viewer,
            new StubConnectionStoreFactory(new StubConnectionStore()));

        // 无模式层的方言（SQLite）不该出现模式节点；此处靠替身模拟 PostgreSQL 的形状
        await vm.OpenConnectionAsync();
        var database = vm.Databases.Single();

        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Count > 0);

        Assert.Equal(
            new[] { "public", "sales" },
            database.Children.Select(c => c.Name));
        Assert.All(database.Children, c => Assert.Equal(DbObjectKind.Schema, c.Kind));

        var publicSchema = database.Children.Single(c => c.Name == "public");
        publicSchema.IsExpanded = true;
        await WaitUntilAsync(() => publicSchema.Children.Count > 0);

        var tableGroup = publicSchema.Children.Single();
        Assert.Equal("表", tableGroup.Name);
        var table = tableGroup.Children.Single();
        Assert.Equal("t1", table.Name);
        Assert.Equal(DbObjectKind.Table, table.Kind);
        // 关键：对象的容器是**模式**而不是库——传错会让 DDL 预览取不到对象
        Assert.Equal("public", table.Container);

        vm.SelectedNode = table;
        Assert.True(vm.CanPreviewDdl);

        await vm.PreviewDdlAsync();

        Assert.Equal([("public", "t1")], explorer.DdlRequests);
        Assert.Equal("-- ddl public.t1", viewer.LastDdl);
    }

    [Fact]
    public async Task Database_node_without_schemas_groups_objects_by_kind()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY)");
        var explorer = new ScriptedMetadataExplorer
        {
            Databases = ["main"],
            Schemas = [],
            Tables = new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["t1"] },
        };

        var vm = new MainViewModel(
            new StubConnectionDialog(SqliteProfile(db)), new FreeSqlFactory(), explorer,
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()));
        await vm.OpenConnectionAsync();

        var database = vm.Databases.Single();
        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Count > 0);

        // 无模式 → 类型分组挂在库节点下，真实对象仍以库名为容器
        var tableGroup = database.Children.Single();
        Assert.Equal("表", tableGroup.Name);
        var table = tableGroup.Children.Single();
        Assert.Equal("t1", table.Name);
        Assert.Equal("main", table.Container);
    }

    [Fact]
    public async Task Tree_filter_keeps_matching_ancestors_without_loading_unexpanded_databases()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE customer_orders (id INTEGER PRIMARY KEY)");
        var vm = CreateViewModel(db);
        await vm.OpenConnectionAsync();

        var database = Assert.Single(vm.Databases);
        vm.TreeFilter = "main";
        Assert.Single(vm.VisibleDatabases);
        Assert.Empty(database.Children); // filter must not trigger lazy loading

        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Count > 0);
        vm.TreeFilter = "customer";
        var tableGroup = Assert.Single(database.Children);
        Assert.Single(tableGroup.VisibleChildren);
        Assert.Equal("customer_orders", tableGroup.VisibleChildren.Single().Name);

        vm.TreeFilter = "missing";
        Assert.Empty(vm.VisibleDatabases);
        Assert.Single(database.Children); // loaded nodes remain loaded while filtered out
    }

    [Fact]
    public async Task Opening_table_creates_read_only_paged_browser_and_reuses_it()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE \"odd.table\" (id INTEGER PRIMARY KEY, label TEXT)");
        seed.Ado.ExecuteNonQuery("INSERT INTO \"odd.table\" VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        var vm = CreateViewModel(db);
        await vm.OpenConnectionAsync();

        var database = Assert.Single(vm.Databases);
        database.IsExpanded = true;
        await WaitUntilAsync(() => database.Children.Count > 0);
        var table = database.Children.Single().Children.Single();

        await vm.OpenDataAsync(table);

        var tab = vm.SelectedTab!;
        Assert.True(tab.IsDataBrowser);
        Assert.Contains("\"main\".\"odd.table\"", tab.Sql);
        Assert.Equal(3, tab.Rows.Count);
        Assert.Equal(new[] { "id", "label" }, tab.Columns);
        Assert.False(tab.HasTransaction);

        tab.PageSize = 1;
        await tab.ExecuteAsync();
        await tab.NextPageAsync();
        Assert.Equal(2, tab.CurrentPage);

        await vm.OpenDataAsync(table);
        Assert.Same(tab, vm.SelectedTab);
        Assert.Equal(2, vm.Tabs.Count); // the connection creates an initial query tab
        Assert.Equal(2, tab.CurrentPage);

        await vm.RefreshSelectedDataAsync();
        Assert.Equal(1, tab.CurrentPage);
        Assert.Single(tab.Rows);
    }

    [Theory]
    [InlineData(DataType.Sqlite, "main", "odd.table", "SELECT * FROM \"main\".\"odd.table\"")]
    [InlineData(DataType.MySql, "sales`west", "order.items", "SELECT * FROM `sales``west`.`order.items`")]
    [InlineData(DataType.PostgreSQL, "sales\"west", "order.items", "SELECT * FROM \"sales\"\"west\".\"order.items\"")]
    public void Data_select_quotes_each_qualified_identifier_component(DataType provider, string container, string name, string expected)
    {
        Assert.Equal(expected, MainViewModel.BuildDataSelect(provider, container, name));
    }

    [Fact]
    public async Task Theme_preference_loads_and_selection_is_persisted_and_applied()
    {
        var themeStore = new StubThemePreferenceStore(ThemeMode.Dark);
        var applied = new List<ThemeMode>();
        var vm = new MainViewModel(
            new StubConnectionDialog(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()), themeStore,
            mode =>
            {
                applied.Add(mode);
                return Task.CompletedTask;
            });

        await vm.InitializeAsync();

        Assert.Equal("深色", vm.SelectedThemeName);
        Assert.Equal([ThemeMode.Dark], applied);

        await vm.SetThemeAsync(ThemeMode.Light);

        Assert.Equal("浅色", vm.SelectedThemeName);
        Assert.Equal(ThemeMode.Light, themeStore.Mode);
        Assert.Equal(1, themeStore.SaveCount);
        Assert.Equal([ThemeMode.Dark, ThemeMode.Light], applied);
    }

    [Fact]
    public async Task Theme_load_failure_does_not_break_startup()
    {
        // 回归：读取界面偏好失败绝不能穿透 InitializeAsync ——
        // 壳层是在 window.Opened 的 async void 处理器里调它的（App.axaml.cs），
        // 一旦外抛就是 UI 线程未处理异常、应用启动即崩。
        var themeStore = new StubThemePreferenceStore { LoadFailure = new IOException("偏好文件被占用") };
        var vm = new MainViewModel(
            new StubConnectionDialog(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer(),
            new StubFilePicker(), new StubExportDialog(), new StubDdlViewer(),
            new StubConnectionStoreFactory(new StubConnectionStore()), themeStore);

        await vm.InitializeAsync();

        Assert.Equal(ThemeMode.System, vm.SelectedThemeMode);
        Assert.Contains("界面偏好", vm.Status);
        Assert.Equal(ThemeMode.System, themeStore.Mode);
    }

    [Fact]
    public async Task Theme_preference_store_defaults_to_system_and_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ddb-theme-{Guid.NewGuid():N}", "ui-settings.json");
        var store = new ThemePreferenceStore(path);

        Assert.Equal(ThemeMode.System, await store.LoadAsync());
        await store.SaveAsync(ThemeMode.Dark);
        Assert.Equal(ThemeMode.Dark, await store.LoadAsync());

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public async Task Theme_preference_store_falls_back_when_the_file_cannot_be_read()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ddb-theme-{Guid.NewGuid():N}", "ui-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"Theme":"Dark"}""");

        // 回归：文件被独占占用（IOException）时必须回退"跟随系统"，
        // 不能让异常逃到 App.axaml / MainViewModel 的启动路径。
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(ThemeMode.System, await new ThemePreferenceStore(path).LoadAsync());
        }

        Assert.Equal(ThemeMode.Dark, await new ThemePreferenceStore(path).LoadAsync());

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
