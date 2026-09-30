// DdraigBench — ConnectionDialogViewModel（按 Provider 出参数表 + 连接测试，纯逻辑可单测）

using System.Windows.Input;
using DdraigBench.Core.Connections;
using DdraigBench.Services;
using FreeSql;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed record ProviderOption(DataType DataType, string DisplayName);

public sealed class ConnectionDialogViewModel : ViewModelBase
{
    private readonly IFilePicker _filePicker;
    private readonly IConnectionTester _connectionTester;
    private ProviderOption _selectedProvider;
    private string _name = string.Empty;
    private string _filePath = string.Empty;
    private string _host = "localhost";
    private string _port = string.Empty;
    private string _database = string.Empty;
    private string _user = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _isTesting;
    private bool _testSucceeded;
    private string? _testFailure;

    public ConnectionDialogViewModel(IFilePicker filePicker, IConnectionTester connectionTester)
    {
        _filePicker = filePicker;
        _connectionTester = connectionTester;
        _selectedProvider = ProviderOptions[0];

        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(null));
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);
        TestCommand = ReactiveCommand.CreateFromTask(TestAsync);
    }

    public event Action<ConnectionProfile?>? CloseRequested;

    public IReadOnlyList<ProviderOption> ProviderOptions { get; } = new[]
    {
        new ProviderOption(DataType.Sqlite, "SQLite（本地文件）"),
        new ProviderOption(DataType.MySql, "MySQL / MariaDB"),
        new ProviderOption(DataType.PostgreSQL, "PostgreSQL"),
    };

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand BrowseCommand { get; }

    public ICommand TestCommand { get; }

    public ProviderOption SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (ReferenceEquals(_selectedProvider, value) || value is null)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedProvider, value);
            this.RaisePropertyChanged(nameof(Provider));
            this.RaisePropertyChanged(nameof(IsFileBased));
            this.RaisePropertyChanged(nameof(IsServerBased));
            ResetTestResult();
            OnProviderChanged();
        }
    }

    public DataType Provider
    {
        get => SelectedProvider.DataType;
        set => SelectedProvider = ProviderOptions.First(o => o.DataType == value);
    }

    public bool IsFileBased => Provider == DataType.Sqlite;

    public bool IsServerBased => !IsFileBased;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public string Host
    {
        get => _host;
        set => this.RaiseAndSetIfChanged(ref _host, value);
    }

    public string Port
    {
        get => _port;
        set => this.RaiseAndSetIfChanged(ref _port, value);
    }

    public string Database
    {
        get => _database;
        set => this.RaiseAndSetIfChanged(ref _database, value);
    }

    public string User
    {
        get => _user;
        set => this.RaiseAndSetIfChanged(ref _user, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            this.RaiseAndSetIfChanged(ref _error, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsTesting
    {
        get => _isTesting;
        private set => this.RaiseAndSetIfChanged(ref _isTesting, value);
    }

    public bool TestSucceeded
    {
        get => _testSucceeded;
        private set => this.RaiseAndSetIfChanged(ref _testSucceeded, value);
    }

    public string? TestFailure
    {
        get => _testFailure;
        private set
        {
            this.RaiseAndSetIfChanged(ref _testFailure, value);
            this.RaisePropertyChanged(nameof(HasTestFailure));
        }
    }

    public bool HasTestFailure => !string.IsNullOrEmpty(TestFailure);

    public async Task BrowseAsync()
    {
        var path = await _filePicker.PickOpenFileAsync(
            "打开 SQLite 数据库", "SQLite 数据库", new[] { "*.db", "*.sqlite", "*.sqlite3" });

        if (path is not null)
        {
            FilePath = path;
        }
    }

    public async Task TestAsync()
    {
        if (IsTesting)
        {
            return;
        }

        var profile = BuildProfile();
        if (profile is null)
        {
            return;
        }

        ResetTestResult();
        IsTesting = true;
        try
        {
            var failure = await _connectionTester.TestAsync(profile);
            TestSucceeded = failure is null;
            TestFailure = failure;
        }
        finally
        {
            IsTesting = false;
        }
    }

    public ConnectionProfile? BuildProfile()
    {
        Error = null;

        if (Provider == DataType.Sqlite)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return Fail("请选择 SQLite 数据库文件");
            }

            var file = new ConnectionParameters(DataType.Sqlite, FilePath: FilePath.Trim());
            return ToProfile(file, DataType.Sqlite);
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            return Fail("请填写主机");
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            return Fail("请填写数据库名");
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            return Fail("请填写用户名");
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(Port))
        {
            if (!int.TryParse(Port, out var parsed) || parsed is <= 0 or > 65535)
            {
                return Fail("端口必须是 1..65535 的整数");
            }

            port = parsed;
        }

        var server = new ConnectionParameters(
            Provider,
            Host: Host.Trim(),
            Port: port,
            Database: Database.Trim(),
            User: User.Trim(),
            Password: Password);

        return ToProfile(server, Provider);
    }

    private void Confirm()
    {
        var profile = BuildProfile();
        if (profile is not null)
        {
            CloseRequested?.Invoke(profile);
        }
    }

    private ConnectionProfile ToProfile(ConnectionParameters parameters, DataType dataType)
    {
        var name = string.IsNullOrWhiteSpace(Name)
            ? ConnectionStringBuilder.SuggestName(parameters)
            : Name.Trim();

        return new ConnectionProfile(name, dataType, ConnectionStringBuilder.Build(parameters));
    }

    private ConnectionProfile? Fail(string message)
    {
        Error = message;
        return null;
    }

    private void ResetTestResult()
    {
        TestSucceeded = false;
        TestFailure = null;
    }

    private void OnProviderChanged()
    {
        // 切换 Provider 时端口回到该方言默认值（切换即重填，避免沿用上一个方言的端口）
        Port = Provider == DataType.Sqlite
            ? string.Empty
            : ConnectionStringBuilder.DefaultPort(Provider).ToString();
    }
}
