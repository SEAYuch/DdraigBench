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
    private decimal? _pageSize = QueryRunner.DefaultMaxRows;
    private int _currentPage = 1;
    private bool _hasNextPage;
    private string _sql = "SELECT 1";
    private IReadOnlyList<string> _columns = Array.Empty<string>();
    private IReadOnlyList<object?[]> _rows = Array.Empty<object?[]>();
    private string _status = "就绪";
    private bool _isExecuting;
    private readonly string? _browserObjectKey;
    private readonly string? _browserObjectName;

    public QueryTabViewModel(
        string header,
        Func<DbSession?> sessionProvider,
        IFilePicker filePicker,
        IExportDialog exportDialog,
        string? browserObjectKey = null,
        string? browserObjectName = null)
    {
        Header = header;
        _sessionProvider = sessionProvider;
        _filePicker = filePicker;
        _exportDialog = exportDialog;
        _browserObjectKey = browserObjectKey;
        _browserObjectName = browserObjectName;
        ExecuteCommand = ReactiveCommand.CreateFromTask(ExecuteAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        NextPageCommand = ReactiveCommand.CreateFromTask(NextPageAsync);
        PreviousPageCommand = ReactiveCommand.CreateFromTask(PreviousPageAsync);
        CommitCommand = ReactiveCommand.CreateFromTask(CommitAsync);
        RollbackCommand = ReactiveCommand.CreateFromTask(RollbackAsync);
        ExportCsvCommand = ReactiveCommand.CreateFromTask(ExportCsvAsync);
        ExportInsertCommand = ReactiveCommand.CreateFromTask(ExportInsertAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(ExecuteAsync);
    }

    public string Header { get; }

    public bool IsDataBrowser => _browserObjectKey is not null;

    public bool IsQueryEditor => !IsDataBrowser;

    public string? BrowserObjectKey => _browserObjectKey;

    public string? BrowserObjectName => _browserObjectName;

    public ICommand ExecuteCommand { get; }

    public ICommand CancelCommand { get; }

public ICommand NextPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand RefreshCommand { get; }

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

    /// <summary>每页行数（服务端分页的 <c>LIMIT</c>；未分页时也是读取上限，默认 1000）。</summary>
    public decimal? PageSize
    {
        get => _pageSize;
        set => this.RaiseAndSetIfChanged(ref _pageSize, value);
    }

    /// <summary>当前页码（从 1 开始）。翻页时会重新执行同一条语句。</summary>
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentPage, value);
            this.RaisePropertyChanged(nameof(HasPreviousPage));
        }
    }

    /// <summary>上次执行时是否还有下一页（多读一行探测得出）。</summary>
    public bool HasNextPage
    {
        get => _hasNextPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasNextPage, value);
            this.RaisePropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public bool CanGoToNextPage => HasNextPage && !IsExecuting;

    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>请求取消当前执行；在“执行刚开始”时按下也能生效。</summary>
    public void Cancel()
    {
        if (!IsExecuting)
        {
            return;
        }

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
        // 手动按「执行」回到第一页；翻页命令走 RunAsync（不重置页码）。
        CurrentPage = 1;
        await RunAsync();
    }

    public async Task NextPageAsync()
    {
        if (!HasNextPage)
        {
            return;
        }

        CurrentPage++;
        await RunAsync();
    }

    public async Task PreviousPageAsync()
    {
        if (!HasPreviousPage)
        {
            return;
        }

        CurrentPage--;
        await RunAsync();
    }

    private async Task RunAsync()
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
            var transaction = _transaction;
            var pageSize = PageSize is > 0 ? (int)PageSize.Value : QueryRunner.DefaultMaxRows;

            // 服务端分页：查询语句包LIMIT/OFFSET；非查询语句原样执行。
            // 刻意多取一行（pageSize + 1）用来判断"还有下一页"，随后把那行裁掉——
            // 这一行必须由 SQL 的 LIMIT 产生，读取上限拿不到它。
            var paged = SqlDialects.TryGet(session.Profile.DataType, out var dialect)
                ? PagedSql.Build(Sql, dialect, pageSize + 1, (long)(CurrentPage - 1) * pageSize)
                : null;
            var statement = paged ?? Sql;
            var readCap = paged is null ? pageSize : pageSize + 1;

            var result = await Task.Run(
                () => runner.ExecuteAsync(statement, readCap, token, transaction), token);

            if (token.IsCancellationRequested)
            {
                Status = "已取消";
                return;
            }

            var visible = result;
            if (paged is not null)
            {
                // 多读的那一行落在结果里：超过 pageSize 就说明还有下一页，裁掉它。
                HasNextPage = result.Rows.Count > pageSize;
                if (HasNextPage)
                {
                    visible = result with { Rows = result.Rows.Take(pageSize).ToList() };
                }
            }
            else
            {
                HasNextPage = false;
            }

            Columns = visible.Columns;
            Rows = visible.Rows;
            Status = BuildStatus(visible, paged is not null);
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
            this.RaisePropertyChanged(nameof(CanGoToNextPage));
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

    private string BuildStatus(QueryResult result, bool paged)
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

        if (paged)
        {
            parts.Add($"第 {CurrentPage} 页（服务端分页，每页 {result.Rows.Count} 行）");
            parts.Add(HasNextPage ? "有下一页 →" : "已是最后一页");
        }
        else if (result.Truncated)
        {
            parts.Add("已截断，可提高每页行数");
        }

        if (result.AffectedRows > 0)
        {
            parts.Add($"影响 {result.AffectedRows} 行");
        }

        return string.Join(" · ", parts);
    }
}
