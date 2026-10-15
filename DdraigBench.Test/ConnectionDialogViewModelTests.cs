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
            new[] { "SQLite（本地文件）", "MySQL / MariaDB", "PostgreSQL", "SQL Server" },
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

        vm.Provider = DataType.SqlServer;
        Assert.Equal("1433", vm.Port);
        Assert.True(vm.IsSqlServer);
        Assert.False(vm.TrustServerCertificate);

        vm.Provider = DataType.Sqlite;
        Assert.Equal(string.Empty, vm.Port);
        Assert.False(vm.IsSqlServer);
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
    public void SqlServer_builds_profile_and_only_trusts_certificate_when_opted_in()
    {
        var vm = Create();
        vm.Provider = DataType.SqlServer;
        vm.Host = "sql.example";
        vm.Database = "app";
        vm.User = "sa";

        var secureProfile = vm.BuildProfile();

        Assert.NotNull(secureProfile);
        Assert.Contains("Server=sql.example,1433", secureProfile!.ConnectionString, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate=False", secureProfile.ConnectionString, StringComparison.Ordinal);

        vm.TrustServerCertificate = true;
        var testProfile = vm.BuildProfile();

        Assert.NotNull(testProfile);
        Assert.Contains("TrustServerCertificate=True", testProfile!.ConnectionString, StringComparison.Ordinal);
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

    [Fact]
    public void New_dialog_is_not_in_editing_mode()
    {
        var vm = Create();

        Assert.False(vm.IsEditing);
        Assert.Equal("新建连接", vm.Title);
        Assert.Equal("连接", vm.ConfirmLabel);
    }

    private static ConnectionDialogViewModel CreateEditing(
        ConnectionProfile existing, IConnectionTester? tester = null) =>
        new(new StubFilePicker(null), tester ?? new StubTester(null), existing);

    [Fact]
    public void Editing_a_sqlite_profile_round_trips_every_field()
    {
        var original = new ConnectionDialogViewModel(new StubFilePicker(null), new StubTester(null));
        original.Name = "本地库";
        original.FilePath = @"C:\db\demo.sqlite3";
        var profile = original.BuildProfile()!;

        var vm = CreateEditing(profile);

        Assert.True(vm.IsEditing);
        Assert.Equal("编辑连接", vm.Title);
        Assert.Equal("保存", vm.ConfirmLabel);
        Assert.Equal(DataType.Sqlite, vm.Provider);
        Assert.True(vm.IsFileBased);
        Assert.Equal("本地库", vm.Name);
        Assert.Equal(@"C:\db\demo.sqlite3", vm.FilePath);
        Assert.Equal(string.Empty, vm.Port);
        Assert.False(vm.HasParseWarning);

        // 回填后再确认必须得到同一个连接串（否则"编辑"会静默改写用户的连接）
        Assert.Equal(profile.ConnectionString, vm.BuildProfile()!.ConnectionString);
    }

    [Fact]
    public void Editing_a_sqlserver_profile_keeps_port_and_certificate_choice()
    {
        var original = new ConnectionDialogViewModel(new StubFilePicker(null), new StubTester(null));
        original.Name = "生产库";
        original.Provider = DataType.SqlServer;
        original.Host = "sql.example";
        original.Port = "14330";
        original.Database = "app";
        original.User = "sa";
        original.Password = "p;w'd";
        original.TrustServerCertificate = true;
        var profile = original.BuildProfile()!;

        var vm = CreateEditing(profile);

        Assert.True(vm.IsEditing);
        Assert.Equal(DataType.SqlServer, vm.Provider);
        Assert.Equal("sql.example", vm.Host);
        Assert.Equal("14330", vm.Port);
        Assert.Equal("app", vm.Database);
        Assert.Equal("sa", vm.User);
        Assert.Equal("p;w'd", vm.Password); // 含分号与引号的口令必须原样回来
        Assert.True(vm.TrustServerCertificate);

        Assert.Equal(profile.ConnectionString, vm.BuildProfile()!.ConnectionString);
    }

    [Fact]
    public void Editing_a_mysql_profile_keeps_a_non_default_port()
    {
        var original = new ConnectionDialogViewModel(new StubFilePicker(null), new StubTester(null));
        original.Name = "mysql";
        original.Provider = DataType.MySql;
        original.Host = "h";
        original.Port = "13306";
        original.Database = "d";
        original.User = "u";
        var profile = original.BuildProfile()!;

        var vm = CreateEditing(profile);

        Assert.Equal("13306", vm.Port);
        Assert.Equal(profile.ConnectionString, vm.BuildProfile()!.ConnectionString);

        // 默认端口也要回填成默认值（不是空串），端口框里看到的应为 3306
        var defaults = CreateEditing(
            new ConnectionProfile("默认", DataType.MySql, "Server=h;Port=3306;Database=d;Uid=u;Pwd=w;Pooling=false"));
        Assert.Equal("3306", defaults.Port);
        Assert.Equal("h", defaults.Host);
    }

    [Fact]
    public void Editing_a_postgresql_profile_round_trips()
    {
        var original = new ConnectionDialogViewModel(new StubFilePicker(null), new StubTester(null));
        original.Name = "pg";
        original.Provider = DataType.PostgreSQL;
        original.Host = "pg.example";
        original.Port = "5433";
        original.Database = "d";
        original.User = "u";
        original.Password = "p'w";
        var profile = original.BuildProfile()!;

        var vm = CreateEditing(profile);

        Assert.Equal(DataType.PostgreSQL, vm.Provider);
        Assert.Equal("pg.example", vm.Host);
        Assert.Equal("5433", vm.Port);
        Assert.Equal("p'w", vm.Password);
        Assert.Equal(profile.ConnectionString, vm.BuildProfile()!.ConnectionString);
    }

    [Fact]
    public void Unparsable_connection_string_keeps_name_and_warns_without_blanking()
    {
        var existing = new ConnectionProfile("坏条目", DataType.MySql, "这不是连接串");

        var vm = CreateEditing(existing);

        Assert.True(vm.IsEditing);
        Assert.Equal("坏条目", vm.Name);
        Assert.Equal(DataType.MySql, vm.Provider);
        Assert.True(vm.HasParseWarning);
        Assert.Contains("无法解析", vm.ParseWarning);

        // 解析失败时绝不能"确认即抹掉原连接串"：必须先把字段补全才能再次提交
        Assert.Null(vm.BuildProfile());
    }

    [Fact]
    public void Editing_mode_reports_the_same_provider_flags_as_manual_switching()
    {
        var vm = CreateEditing(new ConnectionProfile(
            "pg", DataType.PostgreSQL, "Host=h;Port=5432;Database=d;Username=u;Password=w;Pooling=false"));

        Assert.False(vm.IsFileBased);
        Assert.True(vm.IsServerBased);
        Assert.False(vm.IsSqlServer);

        var sql = CreateEditing(new ConnectionProfile(
            "ss", DataType.SqlServer,
            "Server=h,1433;Database=d;User Id=u;Password=w;Encrypt=True;TrustServerCertificate=False;Pooling=False"));

        Assert.True(sql.IsServerBased);
        Assert.True(sql.IsSqlServer);
        Assert.False(sql.TrustServerCertificate);
    }
}
