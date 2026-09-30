// DdraigBench — 导出对话框 VM（纯逻辑，不碰 Avalonia）

using System.Windows.Input;
using DdraigBench.Services;
using ReactiveUI;

namespace DdraigBench.ViewModels;

public sealed class ExportDialogViewModel : ViewModelBase
{
    private readonly IFilePicker _filePicker;
    private string _filePath;
    private string _tableName;
    private string? _error;

    public ExportDialogViewModel(ExportFormat format, IFilePicker filePicker)
    {
        Format = format;
        _filePicker = filePicker;
        _filePath = format == ExportFormat.Csv ? "export.csv" : "export.sql";
        _tableName = "exported_table";

        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public event Action<ExportTarget?>? CloseRequested;

    public ExportFormat Format { get; }

    public bool IsInsert => Format == ExportFormat.Insert;

    public string Title => IsInsert ? "导出 INSERT 脚本" : "导出 CSV";

    public ICommand BrowseCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public string FilePath
    {
        get => _filePath;
        set
        {
            this.RaiseAndSetIfChanged(ref _filePath, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public string TableName
    {
        get => _tableName;
        set
        {
            this.RaiseAndSetIfChanged(ref _tableName, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
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

    public bool HasError => Error is not null;

    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(FilePath) && (!IsInsert || !string.IsNullOrWhiteSpace(TableName));

    public async Task BrowseAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            Title,
            IsInsert ? "SQL 脚本" : "CSV 文件",
            IsInsert ? new[] { "*.sql" } : new[] { "*.csv" },
            FilePath);

        if (!string.IsNullOrEmpty(path))
        {
            FilePath = path;
        }
    }

    public void Confirm()
    {
        if (!CanConfirm)
        {
            Error = IsInsert ? "请填写保存路径与目标表名" : "请填写保存路径";
            return;
        }

        CloseRequested?.Invoke(new ExportTarget(FilePath.Trim(), IsInsert ? TableName.Trim() : string.Empty));
    }

    public void Cancel() => CloseRequested?.Invoke(null);
}
