// DdraigBench — 界面偏好 JSON 存储（ui-settings.json）

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DdraigBench.Services;

/// <summary>
/// <c>LocalApplicationData/DdraigBench/ui-settings.json</c> 的读写。
/// 主题与「上次连接」共用一份文件（见 <see cref="IUiSettingsStore"/> 的说明）；
/// 写入走「临时文件 + 原子替换」，避免写一半被强杀留下半个 JSON。
/// </summary>
public sealed class UiSettingsStore : IUiSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 旧的 ui-settings.json 由 ThemePreferenceStore 写成 PascalCase（{"Theme":"Dark"}），
        // 必须继续读得出来：用户不该因为一次升级就丢掉主题偏好。
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public UiSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DdraigBench",
            "ui-settings.json");
    }

    public async Task<UiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new UiSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<UiSettings>(
                stream, SerializerOptions, cancellationToken);
            return settings is null
                ? new UiSettings()
                : settings with
                {
                    // 枚举走字符串转换器，但历史/手写文件里可能有非法值（如 "Blue"）；
                    // 反序列化会把不认识的名字抛成 JsonException（被下面捕获），
                    // 认出的名字则原样返回——此处只兜 null 名字，避免把空串当成可重连目标。
                    Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.System,
                    LastConnectionName = string.IsNullOrWhiteSpace(settings.LastConnectionName)
                        ? null
                        : settings.LastConnectionName,
                };
        }
        catch (JsonException)
        {
            return new UiSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 偏好文件读不到（占用/权限/路径失效）不该阻断启动：退回默认偏好。
            return new UiSettings();
        }
    }

    public async Task SaveAsync(UiSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }
}
