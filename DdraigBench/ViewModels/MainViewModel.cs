// DdraigBench — 主窗口 VM（连接树 + 查询标签 + 全局命令）

using System.Collections.ObjectModel;
using System.Windows.Input;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Sessions;
using DdraigBench.Services;
using FreeSql;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly string[] SqlitePatterns = { "*.db", "*.sqlite", "*.sqlite3" };

    private readonly IFilePicker _filePicker;
    private readonly FreeSqlFactory _factory;
    private readonly IMetadataExplorer _metadata;
    private DbSession? _session;
    private QueryTabViewModel? _selectedTab;
    private string _status = "就绪：请打开一个 SQLite 数据库";

    public MainViewModel(IFilePicker filePicker, FreeSqlFactory factory, IMetadataExplorer metadata)
    {
        _filePicker = filePicker;
        _factory = factory;
        _metadata = metadata;

        OpenSqliteCommand = ReactiveCommand.CreateFromTask(OpenSqliteAsync);
        NewQueryCommand = ReactiveCommand.Create(NewQuery);
    }

    public ObservableCollection<ConnectionTreeNodeViewModel> Databases { get; } = new();

    public ObservableCollection<QueryTabViewModel> Tabs { get; } = new();

    public ICommand OpenSqliteCommand { get; }

    public ICommand NewQueryCommand { get; }

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

    public async Task OpenSqliteAsync()
    {
        var path = await _filePicker.PickOpenFileAsync("打开 SQLite 数据库", "SQLite 数据库", SqlitePatterns);
        if (path is null)
        {
            return;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        var profile = new ConnectionProfile(
            Path.GetFileName(path), DataType.Sqlite, $"Data Source={path};Pooling=False");
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
            NewQuery();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    public void NewQuery()
    {
        var tab = new QueryTabViewModel($"查询 {Tabs.Count + 1}", () => _session);
        Tabs.Add(tab);
        SelectedTab = tab;
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
        });

    private ConnectionTreeNodeViewModel CreateTableNode(string database, string table) =>
        new(table, DbObjectKind.Table, async node =>
        {
            foreach (var column in await _metadata.GetColumnsAsync(_session!, database, table))
            {
                node.Children.Add(new ConnectionTreeNodeViewModel(
                    $"{column.Name} : {column.DataType}", DbObjectKind.Column));
            }
        });
}
