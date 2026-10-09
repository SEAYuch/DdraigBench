// DdraigBench — MainViewModel / QueryTabViewModel 单测（假对话框 + 临时 SQLite）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

public sealed class MainViewModelTests
{
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

        Assert.Contains(database.Children, c => c.Name == "t1");
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

        vm.SelectedNode = databaseNode.Children.Single();
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
        await WaitUntilAsync(() => database.Children.Any(c => c.Name == "trg_t1"));

        // §6.2 缺口：IDbFirst 完全看不到触发器，M5-1 起由目录查询补齐
        var trigger = database.Children.Single(c => c.Name == "trg_t1");
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

        var table = publicSchema.Children.Single();
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
    public async Task Database_node_without_schemas_stays_flat()
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

        // 无模式 → 对象直接挂在库节点下（与 M5-1 的树形一致），且容器是库名
        var table = database.Children.Single();
        Assert.Equal("t1", table.Name);
        Assert.Equal("main", table.Container);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
