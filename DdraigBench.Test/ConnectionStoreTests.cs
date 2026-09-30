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
