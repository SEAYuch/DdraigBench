// DdraigBench — 查询标签页（SQL 执行、取消、事务、结果集、状态）

using System.Data;
using System.Data.Common;
using System.Text;
using System.Windows.Input;
using DdraigBench.Core.Dialects;
using DdraigBench.Core.Exporting;
using DdraigBench.Core.Querying;
using DdraigBench.Core.Sessions;
using DdraigBench.Services;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class QueryTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Func<DbSession?> _sessionProvider;
    private readonly IFilePicker _filePicker;
    private readonly IExportDialog _exportDialog;
    private CancellationTokenSource? _cts;
    private volatile bool _cancelRequested;
    private ConnectionLease? _lease;
    private DbTransaction? _transaction;
    private bool _useTransaction;
    private decimal? _maxRows = QueryRunner.DefaultMaxRows;
    private string _sql = "SELECT 1";
    private IReadOnlyList<string> _columns = Array.Empty<string>();
    private IReadOnlyList<object?[]> _rows = Array.Empty<object?[]>();
    private string _status = "就绪";
    private bool _isExecuting;

    public QueryTabViewModel(
        string header,
        Func<DbSession?> sessionProvider,
        IFilePicker filePicker,
        IExportDialog exportDialog)
    {
        Header = header;
        _sessionProvider = sessionProvider;
        _filePicker = filePicker;
        _exportDialog = exportDialog;
        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        CommitCommand = ReactiveCommand.CreateFromTask(CommitAsync);
        RollbackCommand = ReactiveCommand.CreateFromTask(RollbackAsync);
        ExportCsvCommand = ReactiveCommand.CreateFromTask(ExportCsvAsync);
        ExportInsertCommand = ReactiveCommand.CreateFromTask(ExportInsertAsync);
    }

    public string Header { get; }

    public ICommand ExecuteCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand CommitCommand { get; }

    public ICommand RollbackCommand { get; }

    public ICommand ExportCsvCommand { get; }

    public ICommand ExportInsertCommand { get; }

    public string Sql
    {
        get => _sql;
        set => this.RaiseAndSetIfChanged(ref _sql, value);
    }

    public IReadOnlyList<string> Columns
    {
        get => _columns;
        private set
        {
            this.RaiseAndSetIfChanged(ref _columns, value);
            this.RaisePropertyChanged(nameof(CanExport));
        }
    }

    public IReadOnlyList<object?[]> Rows
    {
        get => _rows;
        private set
        {
            this.RaiseAndSetIfChanged(ref _rows, value);
            this.RaisePropertyChanged(nameof(CanExport));
        }
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isExecuting, value);
            this.RaisePropertyChanged(nameof(CanCancel));
        }
    }

    public bool CanCancel => IsExecuting;

    /// <summary>有列有行才可导出（M4-2）。</summary>
    public bool CanExport => Columns.Count > 0 && Rows.Count > 0;

    /// <summary>勾选后，执行语句前自动开启事务（后续需显式提交/回滚）。</summary>
    public bool UseTransaction
    {
        get => _useTransaction;
        set => this.RaiseAndSetIfChanged(ref _useTransaction, value);
    }

    public bool HasTransaction => _transaction is not null;

    /// <summary>结果行数上限（取满即截断；网格本身按行虚拟化，只渲染可见行）。</summary>
    public decimal? MaxRows
    {
        get => _maxRows;
        set => this.RaiseAndSetIfChanged(ref _maxRows, value);
    }

    /// <summary>请求取消当前执行；在“执行刚开始”时按下也能生效。</summary>
    public void Cancel()
    {
        _cancelRequested = true;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task ExecuteAsync()
    {
        var session = _sessionProvider();
        if (session is null)
        {
            Status = "未连接：请先打开数据库";
            return;
        }

        if (UseTransaction && _transaction is null)
        {
            await BeginTransactionAsync();
            if (_transaction is null)
            {
                return;
            }
        }

        IsExecuting = true;
        _cts = new CancellationTokenSource();
        if (_cancelRequested)
        {
            _cts.Cancel();
        }

        var token = _cts.Token;

        try
        {
            var runner = new QueryRunner(session.Fsql);
            var sql = Sql;
            var transaction = _transaction;
            var maxRows = MaxRows is > 0 ? (int)MaxRows.Value : QueryRunner.DefaultMaxRows;
            var result = await Task.Run(
                () => runner.ExecuteAsync(sql, maxRows, token, transaction), token);

            if (token.IsCancellationRequested)
            {
                Status = "已取消";
                return;
            }

            Columns = result.Columns;
            Rows = result.Rows;
            Status = BuildStatus(result);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _cancelRequested = false;
            _cts.Dispose();
            _cts = null;
            IsExecuting = false;
        }
    }

    public async Task BeginTransactionAsync()
    {
        if (_transaction is not null)
        {
            return;
        }

        var session = _sessionProvider();
        if (session is null)
        {
            Status = "未连接：请先打开数据库";
            return;
        }

        try
        {
            _lease = await session.LeaseRawAsync();
            var connection = _lease.Connection;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            _transaction = await connection.BeginTransactionAsync();
            this.RaisePropertyChanged(nameof(HasTransaction));
            Status = "事务已开始";
        }
        catch (Exception ex)
        {
            _lease?.Dispose();
            _lease = null;
            _transaction = null;
            Status = ex.Message;
        }
    }

    public Task CommitAsync() => EndTransactionAsync(commit: true);

    public Task RollbackAsync() => EndTransactionAsync(commit: false);

    public Task ExportCsvAsync() => ExportAsync(ExportFormat.Csv);

    public Task ExportInsertAsync() => ExportAsync(ExportFormat.Insert);

    private async Task ExportAsync(ExportFormat format)
    {
        if (!CanExport)
        {
            Status = "没有可导出的结果集";
            return;
        }

        var session = _sessionProvider();
        if (session is null)
        {
            Status = "未连接：请先打开数据库";
            return;
        }

        ISqlDialect? dialect = null;
        if (format == ExportFormat.Insert && !SqlDialects.TryGet(session.Profile.DataType, out dialect))
        {
            Status = "该方言暂不支持导出 INSERT";
            return;
        }

        var target = await _exportDialog.ShowAsync(format);
        if (target is null)
        {
            return;
        }

        try
        {
            var result = new QueryResult(Columns, Rows, 0, TimeSpan.Zero, null);
            if (format == ExportFormat.Csv)
            {
                // Excel 友好：UTF-8 带 BOM
                var csv = ResultExporter.ToCsv(result);
                await File.WriteAllTextAsync(target.FilePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Status = $"已导出 CSV：{target.FilePath}（{Rows.Count} 行）";
            }
            else
            {
                var script = ResultExporter.ToInsert(result, target.TableName, dialect!);
                await File.WriteAllTextAsync(target.FilePath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Status = $"已导出 INSERT 脚本：{target.FilePath}（{Rows.Count} 行）";
            }
        }
        catch (Exception ex)
        {
            Status = $"导出失败：{ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await EndTransactionAsync(commit: false);

    private async Task EndTransactionAsync(bool commit)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            if (commit)
            {
                await _transaction.CommitAsync();
                Status = "事务已提交";
            }
            else
            {
                await _transaction.RollbackAsync();
                Status = "事务已回滚";
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            _lease?.Dispose();
            _lease = null;
            this.RaisePropertyChanged(nameof(HasTransaction));
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

        if (result.Truncated)
        {
            parts.Add("已截断，可提高行数上限");
        }

        if (result.AffectedRows > 0)
        {
            parts.Add($"影响 {result.AffectedRows} 行");
        }

        return string.Join(" · ", parts);
    }
}
