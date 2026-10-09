// DdraigBench — 主题偏好存储契约

namespace DdraigBench.Services;

public interface IThemePreferenceStore
{
    Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ThemeMode mode, CancellationToken cancellationToken = default);
}
