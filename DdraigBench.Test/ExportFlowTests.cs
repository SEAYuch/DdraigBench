// DdraigBench — 导出流程单测（对话框 VM + 标签页导出到真实文件）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Sessions;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ExportDialogViewModelTests
{
    [Fact]
    public void Csv_defaults_and_confirms_without_table_name()
    {
        var vm = new ExportDialogViewModel(ExportFormat.Csv, new StubFilePicker());

        Assert.False(vm.IsInsert);
        Assert.Equal("export.csv", vm.FilePath);
        Assert.True(vm.CanConfirm);

        ExportTarget? closed = null;
        vm.CloseRequested += target => closed = target;
        vm.Confirm();

        Assert.NotNull(closed);
        Assert.Equal(string.Empty, closed!.TableName);
    }

    [Fact]
    public void Insert_requires_table_name_and_trims_it()
    {
        var vm = new ExportDialogViewModel(ExportFormat.Insert, new StubFilePicker());

        Assert.True(vm.IsInsert);
        Assert.Equal("export.sql", vm.FilePath);

        vm.TableName = "   ";
        Assert.False(vm.CanConfirm);
        vm.Confirm();
        Assert.True(vm.HasError);

        ExportTarget? closed = null;
        vm.CloseRequested += target => closed = target;
        vm.TableName = "  t1  ";
        vm.Confirm();

        Assert.Equal("t1", closed!.TableName);
    }

    [Fact]
    public async Task Browse_takes_path_from_save_picker()
    {
        var picker = new StubFilePicker(savePath: @"C:\tmp\x.sql");
        var vm = new ExportDialogViewModel(ExportFormat.Insert, picker);

        await vm.BrowseAsync();

        Assert.Equal(@"C:\tmp\x.sql", vm.FilePath);
        Assert.Equal("export.sql", picker.LastSuggestedFileName);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = new ExportDialogViewModel(ExportFormat.Csv, new StubFilePicker());

        ExportTarget? closed = null;
        vm.CloseRequested += target => closed = target;
        vm.Cancel();

        Assert.Null(closed);
    }
}

public sealed class QueryTabExportTests
{
    [Fact]
    public async Task Export_csv_writes_utf8_bom_file()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var session = new DbSession(new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        await using var _ = session;

        var path = TempPath("csv");
        var dialog = new StubExportDialog(new ExportTarget(path, string.Empty));
        var tab = new QueryTabViewModel("q", () => session, new StubFilePicker(), dialog) { Sql = "SELECT 1 AS n" };
        await tab.ExecuteAsync();

        await tab.ExportCsvAsync();

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        Assert.Equal("n\r\n1\r\n", await File.ReadAllTextAsync(path));
        Assert.Equal(ExportFormat.Csv, dialog.LastFormat);
        Assert.Contains("已导出 CSV", tab.Status);
        File.Delete(path);
    }

    [Fact]
    public async Task Export_insert_uses_dialect_for_table_and_literals()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var session = new DbSession(new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        await using var _ = session;

        var path = TempPath("sql");
        var dialog = new StubExportDialog(new ExportTarget(path, "t1"));
        var tab = new QueryTabViewModel(
            "q",
            () => session,
            new StubFilePicker(),
            dialog) { Sql = "SELECT 1 AS n, 'a''b' AS s" };
        await tab.ExecuteAsync();

        await tab.ExportInsertAsync();

        Assert.Equal(
            "INSERT INTO \"t1\" (\"n\", \"s\") VALUES (1, 'a''b');\n",
            await File.ReadAllTextAsync(path));
        Assert.Equal(ExportFormat.Insert, dialog.LastFormat);
        Assert.Contains("已导出 INSERT", tab.Status);
        File.Delete(path);
    }

    [Fact]
    public async Task Export_without_results_reports_status_without_dialog()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var session = new DbSession(new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        await using var _ = session;

        var dialog = new StubExportDialog(new ExportTarget(TempPath("csv"), string.Empty));
        var tab = new QueryTabViewModel("q", () => session, new StubFilePicker(), dialog);

        Assert.False(tab.CanExport);
        await tab.ExportCsvAsync();

        Assert.Null(dialog.LastFormat);
        Assert.Contains("没有可导出的结果集", tab.Status);
    }

    [Fact]
    public async Task Export_cancelled_dialog_writes_nothing()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var session = new DbSession(new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString), factory);
        await using var _ = session;

        var path = TempPath("csv");
        var dialog = new StubExportDialog(null);
        var tab = new QueryTabViewModel("q", () => session, new StubFilePicker(), dialog) { Sql = "SELECT 1 AS n" };
        await tab.ExecuteAsync();

        await tab.ExportCsvAsync();

        Assert.False(File.Exists(path));
    }

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"ddraigbench_export_{Guid.NewGuid():N}.{extension}");
}
