// DdraigBench — MainViewModel / QueryTabViewModel 单测（假文件选择器 + 临时 SQLite）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Services;
using DdraigBench.ViewModels;

namespace DdraigBench.Test;

public sealed class MainViewModelTests
{
    private sealed class StubFilePicker(string? path) : IFilePicker
    {
        public IReadOnlyList<string>? LastPatterns { get; private set; }

        public Task<string?> PickOpenFileAsync(
            string title, string fileTypeName, IReadOnlyList<string> patterns, CancellationToken ct = default)
        {
            LastPatterns = patterns;
            return Task.FromResult(path);
        }
    }

    private static MainViewModel CreateViewModel(TempSqliteDatabase db) =>
        new(new StubFilePicker(db.FilePath), new FreeSqlFactory(), new FreeSqlMetadataExplorer());

    [Fact]
    public async Task Open_populates_databases_and_opens_a_query_tab()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");
        var vm = CreateViewModel(db);

        await vm.OpenSqliteAsync();

        Assert.Contains("main", vm.Databases.Select(d => d.Name));
        Assert.Single(vm.Tabs);
        Assert.NotNull(vm.SelectedTab);
    }

    [Fact]
    public async Task Open_requests_db_sqlite_and_sqlite3_patterns()
    {
        using var db = new TempSqliteDatabase();
        var picker = new StubFilePicker(db.FilePath);
        var vm = new MainViewModel(picker, new FreeSqlFactory(), new FreeSqlMetadataExplorer());

        await vm.OpenSqliteAsync();

        Assert.NotNull(picker.LastPatterns);
        Assert.Contains("*.db", picker.LastPatterns);
        Assert.Contains("*.sqlite", picker.LastPatterns);
        Assert.Contains("*.sqlite3", picker.LastPatterns);
    }

    [Fact]
    public async Task Database_node_loads_tables_on_first_expand()
    {
        using var db = new TempSqliteDatabase();
        using var seed = db.CreateFsql();
        seed.Ado.ExecuteNonQuery("CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT)");
        var vm = CreateViewModel(db);
        await vm.OpenSqliteAsync();

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
        await vm.OpenSqliteAsync();

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
            new StubFilePicker(null), new FreeSqlFactory(), new FreeSqlMetadataExplorer());
        vm.NewQuery();

        await vm.SelectedTab!.ExecuteAsync();

        Assert.Contains("未连接", vm.SelectedTab.Status);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
