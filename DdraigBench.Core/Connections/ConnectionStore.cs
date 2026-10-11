// DdraigBench — 连接配置持久化（JSON 文件 + 系统密钥库保护连接串）

using System.Text.Json;
using System.Text.Json.Serialization;
using DdraigBench.Core.Secrets;
using FreeSql;

namespace DdraigBench.Core.Connections;

/// <summary>
/// 连接清单的落盘/读取。连接串（含口令）经 <see cref="ISecretProtector"/> 加密，名称与方言保持明文以便列表展示。
/// </summary>
public sealed class ConnectionStore(string filePath, ISecretProtector protector) : IConnectionStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = filePath;

    public ISecretProtector Protector { get; } = protector;

    /// <summary>跨平台默认路径：%APPDATA% / ~/.config / ~/Library/Application Support 下的 DdraigBench/connections.json。</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        SecretProtectors.ServiceName,
        "connections.json");

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        await using var stream = File.OpenRead(FilePath);
        var file = await JsonSerializer.DeserializeAsync<StoreFile>(stream, JsonOptions, ct);

        if (file is null)
        {
            return Array.Empty<ConnectionProfile>();
        }

        // 版本比本程序新：宁可报错也不要按旧结构解读后覆盖——未知格式一律当"读不出来"处理，
        // 由调用方（MainViewModel）收敛成告警并停用持久化（见 H3/C1 的同类处理）。
        if (file.Version > CurrentVersion)
        {
            throw new NotSupportedException(
                $"连接清单版本 {file.Version} 高于本程序支持的版本 {CurrentVersion}，请升级 DdraigBench 后再试（{FilePath}）");
        }

        var effective = string.Equals(file.Protector, Protector.Name, StringComparison.Ordinal)
            ? Protector
            : SecretProtectors.CreateByName(file.Protector);

        // JSON 里写成 "connections": null 时，反序列化同样会把集合置空 → 不判空即 NRE
        var stored = file.Connections ?? [];

        var profiles = new List<ConnectionProfile>(stored.Count);
        foreach (var connection in stored)
        {
            profiles.Add(new ConnectionProfile(
                connection.Name,
                connection.DataType,
                await effective.UnprotectAsync(connection.ProtectedConnectionString, ct)));
        }

        return profiles;
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var stored = new List<StoredConnection>(profiles.Count);
        foreach (var profile in profiles)
        {
            stored.Add(new StoredConnection
            {
                Name = profile.Name,
                DataType = profile.DataType,
                ProtectedConnectionString = await Protector.ProtectAsync(profile.ConnectionString, ct),
            });
        }

        var file = new StoreFile
        {
            Version = CurrentVersion,
            Protector = Protector.Name,
            Connections = stored,
        };

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            // 只对"本次新建"的目录收紧权限：配置路径可能落在 /tmp、~ 等既有目录上，
            // 擅自 chmod 会破坏系统/家目录语义（非属主时还会直接 EPERM 失败）。
            var createdDirectory = !Directory.Exists(directory);
            Directory.CreateDirectory(directory);
            if (createdDirectory)
            {
                HardenUnixDirectory(directory);
            }
        }

        // 先写临时文件再替换，避免中途崩溃留下半截配置
        var temporary = FilePath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, file, JsonOptions, ct);
        }

        HardenUnixFile(temporary);
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>
    /// 非 Windows 平台把配置目录收紧到 0700：即便保护器是明文回退，也至少不给同机其他用户可读。
    /// 系统不支持 Unix 文件模式时静默跳过（如 Windows / 某些容器文件系统）。
    /// </summary>
    private static void HardenUnixDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            // 权限收紧失败不应阻断连接清单落盘（如文件系统不支持 Unix 权限位）。
        }
    }

    /// <summary>非 Windows 平台把配置文件收紧到 0600（仅属主可读写）。</summary>
    private static void HardenUnixFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class StoreFile
    {
        public int Version { get; set; } = CurrentVersion;

        public string Protector { get; set; } = string.Empty;

        public List<StoredConnection> Connections { get; set; } = new();
    }

    private sealed class StoredConnection
    {
        public string Name { get; set; } = string.Empty;

        public DataType DataType { get; set; }

        public string ProtectedConnectionString { get; set; } = string.Empty;
    }
}
