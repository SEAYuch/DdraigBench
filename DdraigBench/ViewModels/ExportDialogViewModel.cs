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
        _filePath = DefaultPath(format == ExportFormat.Csv ? "export.csv" : "export.sql");
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

    /// <summary>
    /// 默认导出路径：文档目录下的文件名。
    /// 不能用裸文件名——相对路径会被解析到进程工作目录（快捷方式启动时可能是 System32 之类不可写的位置），
    /// 用户看到"导出失败"却不知道文件本该落在哪里（C6）。取不到文档目录就依次回落用户目录、当前目录。
    /// </summary>
    public static string DefaultPath(string fileName)
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return string.IsNullOrWhiteSpace(directory) ? fileName : Path.Combine(directory, fileName);
    }

    public async Task BrowseAsync()
    {
        // 选择器只接受文件名（StorageProviderFilePicker 把它塞进 SuggestedFileName），
        // 传整条路径会连目录一起显示在文件名框里。
        var path = await _filePicker.PickSaveFileAsync(
            Title,
            IsInsert ? "SQL 脚本" : "CSV 文件",
            IsInsert ? new[] { "*.sql" } : new[] { "*.csv" },
            Path.GetFileName(FilePath));

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
