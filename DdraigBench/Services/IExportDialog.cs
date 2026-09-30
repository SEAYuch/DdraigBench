// DdraigBench — 导出对话框（保存路径 + INSERT 目标表名，注入 ViewModel）

namespace DdraigBench.Services;

public enum ExportFormat
{
    Csv,
    Insert,
}

public sealed record ExportTarget(string FilePath, string TableName);

public interface IExportDialog
{
    Task<ExportTarget?> ShowAsync(ExportFormat format, string? suggestedFileName = null);
}
