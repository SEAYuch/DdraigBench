// DdraigBench — 连接存储单测（JSON + DPAPI；真落盘，文件内容断言）

using DdraigBench.Core.Connections;
using DdraigBench.Core.Secrets;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ConnectionStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(), $"ddraigbench_conns_{Guid.NewGuid():N}.json");

    private readonly List<ConnectionProfile> _profiles =
    [
        new("本地 SQLite", DataType.Sqlite, @"Data Source=/tmp/x.db"),
        new("生产 MySQL", DataType.MySql, "Server=db;Password=s3cret-pw;Database=app"),
    ];

    public void Dispose()
    {
        foreach (var path in new[] { _filePath, _filePath + ".tmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Missing_file_loads_empty()
    {
        var store = new ConnectionStore(_filePath, new PlainTextSecretProtector());

        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Round_trip_with_plaintext_protector()
    {
        var protector = new PlainTextSecretProtector();
        var store = new ConnectionStore(_filePath, protector);

        await store.SaveAsync(_profiles);
        var loaded = await store.LoadAsync();

        Assert.Equal(_profiles, loaded);
        Assert.False(protector.IsEncrypted);

        var json = await File.ReadAllTextAsync(_filePath);
        Assert.Contains("plain:Server=db;Password=s3cret-pw", json);
        Assert.Contains("\"Sqlite\"", json);
        Assert.Contains("\"protector\": \"plaintext\"", json.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Round_trip_with_dpapi_hides_password_in_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiSecretProtector();
        var store = new ConnectionStore(_filePath, protector);

        await store.SaveAsync(_profiles);
        var loaded = await store.LoadAsync();

        Assert.Equal(_profiles, loaded);
        Assert.True(protector.IsEncrypted);

        var json = await File.ReadAllTextAsync(_filePath);
        Assert.DoesNotContain("s3cret-pw", json);
        Assert.DoesNotContain("Server=db", json);
        Assert.Contains("windows-dpapi", json);
    }

    [Fact]
    public async Task Plaintext_file_still_loads_after_upgrading_protector()
    {
        await new ConnectionStore(_filePath, new PlainTextSecretProtector()).SaveAsync(_profiles);

        var upgraded = new ConnectionStore(_filePath, new DpapiSecretProtector());
        var loaded = await upgraded.LoadAsync();

        Assert.Equal(_profiles, loaded);
    }

    [Fact]
    public async Task Round_trip_with_keyring_envelope_hides_password_in_file()
    {
        if (EnvelopeProtectorOnThisPlatform() is not { } protector)
        {
            Assert.Skip("信封保护器仅 Linux/FreeBSD/macOS 可用");
            return;
        }

        var store = new ConnectionStore(_filePath, protector);

        await store.SaveAsync(_profiles);
        var loaded = await store.LoadAsync();

        Assert.Equal(_profiles, loaded);
        Assert.True(protector.IsEncrypted);

        var json = await File.ReadAllTextAsync(_filePath);
        Assert.DoesNotContain("s3cret-pw", json);
        Assert.DoesNotContain("Server=db", json);
        Assert.Contains(protector.Name, json);
        Assert.Contains("\"Sqlite\"", json); // 名称与方言仍明文，便于列表展示
    }

    [Fact]
    public async Task Plaintext_file_still_loads_after_upgrading_to_keyring_envelope()
    {
        if (EnvelopeProtectorOnThisPlatform() is not { } protector)
        {
            Assert.Skip("信封保护器仅 Linux/FreeBSD/macOS 可用");
            return;
        }

        await new ConnectionStore(_filePath, new PlainTextSecretProtector()).SaveAsync(_profiles);

        // 换一把"新主密钥"的保护器：明文文件仍要读得回来（升级不丢连接）。
        var upgraded = new ConnectionStore(_filePath, protector);
        var loaded = await upgraded.LoadAsync();

        Assert.Equal(_profiles, loaded);

        // 再存一次即完成迁移：文件里只剩密文。
        await upgraded.SaveAsync(loaded);
        var json = await File.ReadAllTextAsync(_filePath);
        Assert.DoesNotContain("s3cret-pw", json);
        Assert.Contains(protector.Name, json);
    }

    /// <summary>本平台的信封保护器（假主密钥，无需真实密钥库）；Windows/其它平台返回 null。</summary>
    private static ISecretProtector? EnvelopeProtectorOnThisPlatform()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            return new SecretServiceSecretProtector(new FakeMasterKeyProvider());
        }

        return OperatingSystem.IsMacOS()
            ? new MacKeychainSecretProtector(new FakeMasterKeyProvider())
            : null;
    }

    [Fact]
    public async Task Saved_file_is_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new ConnectionStore(_filePath, new PlainTextSecretProtector());
        await store.SaveAsync(_profiles);

        var mode = File.GetUnixFileMode(_filePath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public async Task Existing_directory_permissions_are_left_untouched()
    {
        // 回归：配置路径可能落在 /tmp 等既有目录上，绝不能擅自 chmod（曾误改，非属主时 EPERM 崩溃）。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"ddraigbench_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var expected = File.GetUnixFileMode(directory);

        try
        {
            var filePath = Path.Combine(directory, "connections.json");
            await new ConnectionStore(filePath, new PlainTextSecretProtector()).SaveAsync(_profiles);

            Assert.Equal(expected, File.GetUnixFileMode(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_directory_is_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"ddraigbench_{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "connections.json");
        try
        {
            await new ConnectionStore(filePath, new PlainTextSecretProtector()).SaveAsync(_profiles);

            var mode = File.GetUnixFileMode(directory);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                mode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Unknown_protector_name_is_reported()
    {
        await File.WriteAllTextAsync(_filePath, """
            { "version": 1, "protector": "some-future-vault", "connections": [] }
            """);

        var store = new ConnectionStore(_filePath, new PlainTextSecretProtector());

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => store.LoadAsync());
        Assert.Contains("some-future-vault", error.Message);
    }

    [Fact]
    public void Default_file_path_is_under_app_data()
    {
        Assert.EndsWith(Path.Combine("DdraigBench", "connections.json"), ConnectionStore.DefaultFilePath);
    }
}
