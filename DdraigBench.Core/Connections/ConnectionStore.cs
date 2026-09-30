// DdraigBench — 连接配置持久化（JSON 文件 + 系统密钥库保护连接串）

using System.Text.Json;
using System.Text.Json.Serialization;
using DdraigBench.Core.Secrets;
using FreeSql;

namespace DdraigBench.Core.Connections;

/// <summary>
/// 连接清单的落盘/读取。连接串（含口令）经 <see cref="ISecretProtector"/> 加密，名称与方言保持明文以便列表展示。
/// </summary>
public sealed class ConnectionStore(string filePath, ISecretProtector protector)
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

        var effective = string.Equals(file.Protector, Protector.Name, StringComparison.Ordinal)
            ? Protector
            : SecretProtectors.CreateByName(file.Protector);

        var profiles = new List<ConnectionProfile>(file.Connections.Count);
        foreach (var stored in file.Connections)
        {
            profiles.Add(new ConnectionProfile(
                stored.Name,
                stored.DataType,
                effective.Unprotect(stored.ProtectedConnectionString)));
        }

        return profiles;
    }

    public async Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var file = new StoreFile
        {
            Version = CurrentVersion,
            Protector = Protector.Name,
            Connections = profiles
                .Select(p => new StoredConnection
                {
                    Name = p.Name,
                    DataType = p.DataType,
                    ProtectedConnectionString = Protector.Protect(p.ConnectionString),
                })
                .ToList(),
        };

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 先写临时文件再替换，避免中途崩溃留下半截配置
        var temporary = FilePath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, file, JsonOptions, ct);
        }

        File.Move(temporary, FilePath, overwrite: true);
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
