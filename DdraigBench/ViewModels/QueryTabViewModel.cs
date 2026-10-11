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
    private int? _sortColumn;
    private bool _sortDescending;

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
        SortCommand = ReactiveCommand.Create<int>(SortBy);
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

    /// <summary>
    /// 按列排序当前页（参数 = 列序号）。只排手里已有的行——服务端分页下没有整表，
    /// 所以排序不跨页；再次点同一列切换升降序，换页/重新执行会清掉排序状态。
    /// </summary>
    public ICommand SortCommand { get; }

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
            this.RaisePropertyChanged(nameof(CanRun));
        }
    }

    public bool CanCancel => IsExecuting;

    /// <summary>
    /// 能否发起一次执行。<b>执行中必须为 false</b>：并行两次执行会让后者覆盖 <c>_cts</c>，
    /// 先结束者的 finally 会 dispose 掉正在被使用的那把 CTS（取消串台 / ObjectDisposedException）。
    /// </summary>
    public bool CanRun => IsQueryEditor && !IsExecuting;

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
        // 重入守卫（最后一道防线，UI 侧由 CanRun/IsEnabled 挡住）：执行中再进来会覆盖 _cts，
        // 两次执行的状态与取消互相串台；直接忽略重入请求，不改变任何状态。
        if (IsExecuting)
        {
            return;
        }

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

            ResetSort();
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
            // 租约获取 / 开连接 / 开事务在多数驱动里是同步实现（SQLite 尤甚），必须离开 UI 线程（AGENTS §5）；
            // 事务对象本身无线程亲和性，后续查询照旧在事务连接上跑。
            var (lease, transaction) = await Task.Run(async () =>
            {
                var acquired = await session.LeaseRawAsync().ConfigureAwait(false);
                try
                {
                    var connection = acquired.Connection;
                    if (connection.State != ConnectionState.Open)
                    {
                        await connection.OpenAsync().ConfigureAwait(false);
                    }

                    return (Lease: acquired, Transaction: await connection.BeginTransactionAsync().ConfigureAwait(false));
                }
                catch
                {
                    // 中途失败必须归还租约，否则连接池泄漏
                    acquired.Dispose();
                    throw;
                }
            });

            _lease = lease;
            _transaction = transaction;
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

        // 先摘字段再干活：提交/回滚期间不会再被当成"有事务"，避免二次提交；
        // 下面即便抛异常也不会留下已死的事务把后续执行钉在旧连接上。
        var transaction = _transaction;
        var lease = _lease;
        _transaction = null;
        _lease = null;

        try
        {
            // 提交/回滚/归还租约同为同步实现，须离开 UI 线程（AGENTS §5）。
            // Dispose 与租约归还在同一个 finally 里：Dispose 失败也必须还连接（原先会漏还）。
            await Task.Run(async () =>
            {
                try
                {
                    if (commit)
                    {
                        await transaction.CommitAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                    lease?.Dispose();
                }
            });

            Status = commit ? "事务已提交" : "事务已回滚";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            this.RaisePropertyChanged(nameof(HasTransaction));
        }
    }

    /// <summary>
    /// 排序当前页已读回的行：数字按数值比（"10" &gt; "9"）、日期按时间点比、其余按序数文本比
    /// （见 <see cref="ResultCellKinds"/>），空值一律排最后。首次点某列升序，再点同一列切换降序。
    /// </summary>
    private void SortBy(int index)
    {
        if (index < 0 || index >= Columns.Count || Rows.Count == 0)
        {
            return;
        }

        if (_sortColumn == index)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = index;
            _sortDescending = false;
        }

        Rows = ResultCellKinds.Sorted(Rows, index, _sortDescending);
        Status = $"按「{Columns[index]}」{(_sortDescending ? "降序" : "升序")}排序（当前页 {Rows.Count} 行）";
    }

    /// <summary>新结果到达时清掉排序状态——排序只对到达时的那一页有效，翻页后必须重来。</summary>
    private void ResetSort()
    {
        _sortColumn = null;
        _sortDescending = false;
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
