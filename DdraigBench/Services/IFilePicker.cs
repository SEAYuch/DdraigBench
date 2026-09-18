// DdraigBench — 文件选择服务（View 层能力，注入 ViewModel）

namespace DdraigBench.Services;

public interface IFilePicker
{
    Task<string?> PickOpenFileAsync(
        string title,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        CancellationToken ct = default);
}
