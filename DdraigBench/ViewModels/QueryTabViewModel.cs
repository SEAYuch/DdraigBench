// DdraigBench — 查询标签页（SQL 执行、结果集、状态）

using System.Windows.Input;
using DdraigBench.Core.Querying;
using DdraigBench.Core.Sessions;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class QueryTabViewModel : ViewModelBase
{
    private readonly Func<DbSession?> _sessionProvider;
    private string _sql = "SELECT 1";
    private IReadOnlyList<string> _columns = Array.Empty<string>();
    private IReadOnlyList<object?[]> _rows = Array.Empty<object?[]>();
    private string _status = "就绪";
    private bool _isExecuting;

    public QueryTabViewModel(string header, Func<DbSession?> sessionProvider)
    {
        Header = header;
        _sessionProvider = sessionProvider;
        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync);
    }

    public string Header { get; }

    public string Sql
    {
        get => _sql;
        set => this.RaiseAndSetIfChanged(ref _sql, value);
    }

    public IReadOnlyList<string> Columns
    {
        get => _columns;
        private set => this.RaiseAndSetIfChanged(ref _columns, value);
    }

    public IReadOnlyList<object?[]> Rows
    {
        get => _rows;
        private set => this.RaiseAndSetIfChanged(ref _rows, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set => this.RaiseAndSetIfChanged(ref _isExecuting, value);
    }

    public ICommand ExecuteCommand { get; }

    public async Task ExecuteAsync()
    {
        var session = _sessionProvider();
        if (session is null)
        {
            Status = "未连接：请先打开数据库";
            return;
        }

        IsExecuting = true;
        try
        {
            var runner = new QueryRunner(session.Fsql);
            var sql = Sql;
            var result = await Task.Run(() => runner.ExecuteAsync(sql));
            Columns = result.Columns;
            Rows = result.Rows;
            Status = BuildStatus(result);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private static string BuildStatus(QueryResult result)
    {
        if (result.Error is not null)
        {
            return result.Error;
        }

        var parts = new List<string>
        {
            $"{result.Rows.Count} 行",
            $"{result.Elapsed.TotalMilliseconds:F0} ms",
        };

        if (result.AffectedRows > 0)
        {
            parts.Add($"影响 {result.AffectedRows} 行");
        }

        return string.Join(" · ", parts);
    }
}
