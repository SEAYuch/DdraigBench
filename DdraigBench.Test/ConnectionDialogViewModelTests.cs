// DdraigBench — ConnectionDialogViewModel 单测（纯逻辑，不启 Avalonia）

using DdraigBench.Core.Connections;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ConnectionDialogViewModelTests
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

        public Task<string?> PickSaveFileAsync(
            string title,
            string fileTypeName,
            IReadOnlyList<string> patterns,
            string? suggestedFileName = null,
            CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class StubTester(string? failure) : IConnectionTester
    {
        public ConnectionProfile? LastProfile { get; private set; }

        public Task<string?> TestAsync(ConnectionProfile profile, CancellationToken ct = default)
        {
            LastProfile = profile;
            return Task.FromResult(failure);
        }
    }

    private static ConnectionDialogViewModel Create(
        IFilePicker? picker = null, IConnectionTester? tester = null) =>
        new(picker ?? new StubFilePicker(null), tester ?? new StubTester(null));

    [Fact]
    public void Defaults_to_sqlite_file_mode()
    {
        var vm = Create();

        Assert.Equal(DataType.Sqlite, vm.Provider);
        Assert.True(vm.IsFileBased);
        Assert.False(vm.IsServerBased);
        Assert.Equal("localhost", vm.Host);
    }

    [Fact]
    public void Provider_options_expose_friendly_display_names()
    {
        var vm = Create();

        Assert.Equal(
            new[] { "SQLite（本地文件）", "MySQL / MariaDB", "PostgreSQL" },
            vm.ProviderOptions.Select(o => o.DisplayName));
        Assert.Equal("SQLite（本地文件）", vm.SelectedProvider.DisplayName);
    }

    [Fact]
    public void Switching_provider_resets_port_to_that_dialect_default()
    {
        var vm = Create();

        vm.Provider = DataType.PostgreSQL;
        Assert.False(vm.IsFileBased);
        Assert.Equal("5432", vm.Port);

        vm.Provider = DataType.MySql;
        Assert.Equal("3306", vm.Port);

        vm.Provider = DataType.Sqlite;
        Assert.Equal(string.Empty, vm.Port);
    }

    [Fact]
    public void Sqlite_requires_file_path()
    {
        var vm = Create();

        Assert.Null(vm.BuildProfile());
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Sqlite_builds_profile_from_file_path()
    {
        var vm = Create();
        vm.Name = "demo";
        vm.FilePath = @"C:\db\demo.db";

        var profile = vm.BuildProfile();

        Assert.NotNull(profile);
        Assert.Equal("demo", profile!.Name);
        Assert.Equal(DataType.Sqlite, profile.DataType);
        Assert.Equal(@"Data Source=C:\db\demo.db;Pooling=False", profile.ConnectionString);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Browse_sets_file_path_and_requests_db_patterns()
    {
        var picker = new StubFilePicker(@"C:\db\demo.sqlite3");
        var vm = Create(picker);

        await vm.BrowseAsync();

        Assert.Equal(@"C:\db\demo.sqlite3", vm.FilePath);
        Assert.NotNull(picker.LastPatterns);
        Assert.Contains("*.db", picker.LastPatterns);
        Assert.Contains("*.sqlite3", picker.LastPatterns);
    }

    [Fact]
    public void Server_provider_validates_required_fields_then_builds_profile()
    {
        var vm = Create();
        vm.Provider = DataType.MySql;

        // Host 默认 localhost，故首个缺失项是数据库名
        Assert.Null(vm.BuildProfile());
        Assert.Contains("数据库名", vm.Error!);

        vm.Database = "d";
        Assert.Null(vm.BuildProfile());
        Assert.Contains("用户名", vm.Error!);

        vm.User = "u";
        var profile = vm.BuildProfile();

        Assert.NotNull(profile);
        Assert.Null(vm.Error);
        Assert.Equal("u@localhost/d", profile!.Name);
        Assert.Equal(DataType.MySql, profile.DataType);
        Assert.Contains("Port=3306", profile.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_port_is_rejected()
    {
        var vm = Create();
        vm.Provider = DataType.PostgreSQL;
        vm.Host = "h";
        vm.Database = "d";
        vm.User = "u";
        vm.Port = "abc";

        Assert.Null(vm.BuildProfile());
        Assert.Contains("端口", vm.Error!);
    }

    [Fact]
    public async Task Test_reports_success_when_tester_returns_null()
    {
        var tester = new StubTester(null);
        var vm = Create(tester: tester);
        vm.FilePath = @"C:\db\demo.db";

        await vm.TestAsync();

        Assert.True(vm.TestSucceeded);
        Assert.False(vm.HasTestFailure);
        Assert.NotNull(tester.LastProfile);
    }

    [Fact]
    public async Task Test_reports_failure_message()
    {
        var vm = Create(tester: new StubTester("拒绝连接"));
        vm.FilePath = @"C:\db\demo.db";

        await vm.TestAsync();

        Assert.False(vm.TestSucceeded);
        Assert.True(vm.HasTestFailure);
        Assert.Equal("拒绝连接", vm.TestFailure);
    }

    [Fact]
    public async Task Test_with_invalid_input_sets_error_and_skips_tester()
    {
        var tester = new StubTester(null);
        var vm = Create(tester: tester);

        await vm.TestAsync();

        Assert.NotNull(vm.Error);
        Assert.Null(tester.LastProfile);
        Assert.False(vm.TestSucceeded);
    }

    [Fact]
    public async Task Switching_provider_clears_previous_test_result()
    {
        var vm = Create(tester: new StubTester("x"));
        vm.FilePath = @"C:\db\demo.db";
        await vm.TestAsync();
        Assert.True(vm.HasTestFailure);

        vm.Provider = DataType.PostgreSQL;

        Assert.False(vm.HasTestFailure);
        Assert.False(vm.TestSucceeded);
    }

    [Fact]
    public void Confirm_raises_close_requested_with_profile()
    {
        var vm = Create();
        vm.FilePath = @"C:\db\demo.db";
        ConnectionProfile? captured = null;
        vm.CloseRequested += profile => captured = profile;

        vm.ConfirmCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal(DataType.Sqlite, captured!.DataType);
    }

    [Fact]
    public void Cancel_raises_close_requested_with_null()
    {
        var vm = Create();
        var raised = false;
        ConnectionProfile? captured = new ConnectionProfile("x", DataType.Sqlite, "y");
        vm.CloseRequested += profile => { raised = true; captured = profile; };

        vm.CancelCommand.Execute(null);

        Assert.True(raised);
        Assert.Null(captured);
    }
}
