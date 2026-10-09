// DdraigBench — 主题偏好 JSON 存储

using System.Text.Json;

namespace DdraigBench.Services;

public sealed class ThemePreferenceStore : IThemePreferenceStore
{
    private readonly string _path;

    public ThemePreferenceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DdraigBench",
            "ui-settings.json");
    }

    public async Task<ThemeMode> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return ThemeMode.System;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<UiSettings>(stream, cancellationToken: cancellationToken);
            return Enum.TryParse<ThemeMode>(settings?.Theme, ignoreCase: true, out var mode)
                ? mode
                : ThemeMode.System;
        }
        catch (JsonException)
        {
            return ThemeMode.System;
        }
    }

    public async Task SaveAsync(ThemeMode mode, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, new UiSettings(mode.ToString()), cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed record UiSettings(string Theme);
}
