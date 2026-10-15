// DdraigBench — 测试替身（文件选择 / 导出对话框）

using System.Security.Cryptography;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Core.Secrets;
using DdraigBench.Core.Sessions;
using DdraigBench.Services;

namespace DdraigBench.Test;

internal sealed class StubFilePicker(string? openPath = null, string? savePath = null) : IFilePicker
{
    public string? OpenPath { get; set; } = openPath;

    public string? SavePath { get; set; } = savePath;

    public IReadOnlyList<string>? LastOpenPatterns { get; private set; }

    public string? LastSuggestedFileName { get; private set; }

    public Task<string?> PickOpenFileAsync(
        string title, string fileTypeName, IReadOnlyList<string> patterns, CancellationToken ct = default)
    {
        LastOpenPatterns = patterns;
        return Task.FromResult(OpenPath);
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string fileTypeName,
        IReadOnlyList<string> patterns,
        string? suggestedFileName = null,
        CancellationToken ct = default)
    {
        LastSuggestedFileName = suggestedFileName;
        return Task.FromResult(SavePath);
    }
}

internal sealed class StubDdlViewer : IDdlViewer{
    public string? LastTitle { get; private set; }

    public string? LastDdl { get; private set; }

    public int ShowCount { get; private set; }

    public Task ShowAsync(string title, string ddl)
    {
        LastTitle = title;
        LastDdl = ddl;
        ShowCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 可编排的元数据替身：直接把「库 → 模式 → 对象」的形状喂给 VM，
/// 这样没有 PostgreSQL 靶机也能把树的层级逻辑测透（M5-2）。
/// </summary>
internal sealed class ScriptedMetadataExplorer : IMetadataExplorer
{
    public IReadOnlyList<string> Databases { get; init; } = ["app"];

    public IReadOnlyList<string> Schemas { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tables { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Views { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>分区键为「container.table」，例如 <c>public.orders</c>。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DbPartitionInfo>> Partitions { get; init; } =
        new Dictionary<string, IReadOnlyList<DbPartitionInfo>>();

    public List<(string Container, string Name)> DdlRequests { get; } = [];

    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default) =>
        Task.FromResult(Databases);

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default)
    {
        IReadOnlyList<string> names = kind switch
        {
            DbObjectKind.Schema => Schemas,
            DbObjectKind.Table => Tables.TryGetValue(database, out var tables) ? tables : [],
            DbObjectKind.View => Views.TryGetValue(database, out var views) ? views : [],
            _ => [],
        };

        return Task.FromResult<IReadOnlyList<DbObjectInfo>>(
            names.Select(name => new DbObjectInfo(name)).ToList());
    }

    public Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbColumnInfo>>([]);

    public Task<IReadOnlyList<DbPartitionInfo>> GetPartitionsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult(Partitions.TryGetValue($"{database}.{table}", out var partitions)
            ? partitions
            : (IReadOnlyList<DbPartitionInfo>)[]);

    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default)
    {
        DdlRequests.Add((database, table));
        return Task.FromResult<string?>($"-- ddl {database}.{table}");
    }
}

/// <summary>
/// 前 N 次对象查询失败、之后交给可编排替身的元数据探查器：
/// 验证「展开失败后节点仍可重试，且失败前塞进去的半个子树会被清掉」（M9，2026-10-11）。
/// </summary>
internal sealed class FlakyMetadataExplorer : IMetadataExplorer
{
    private readonly ScriptedMetadataExplorer _inner;
    private readonly DbObjectKind? _failOnKind;
    private int _failuresLeft;

    public FlakyMetadataExplorer(
        int failures = 1,
        DbObjectKind? failOnKind = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? tables = null)
    {
        _failuresLeft = failures;
        _failOnKind = failOnKind;
        _inner = new ScriptedMetadataExplorer
        {
            Databases = ["main"],
            Tables = tables ?? new Dictionary<string, IReadOnlyList<string>> { ["main"] = ["t1"] },
        };
    }

    public int ObjectQueryCount { get; private set; }

    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default) =>
        _inner.GetDatabasesAsync(session, ct);

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default)
    {
        ObjectQueryCount++;
        if (_failuresLeft > 0 && (_failOnKind is null || _failOnKind == kind))
        {
            _failuresLeft--;
            return Task.FromException<IReadOnlyList<DbObjectInfo>>(new InvalidOperationException("元数据暂时不可用"));
        }

        return _inner.GetObjectsAsync(session, database, kind, ct);
    }

    public Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        _inner.GetColumnsAsync(session, database, table, ct);

    public Task<IReadOnlyList<DbPartitionInfo>> GetPartitionsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        _inner.GetPartitionsAsync(session, database, table, ct);

    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        _inner.GetDdlAsync(session, database, table, ct);
}

/// <summary>元数据探查器替身：对象查询一律抛异常，用于验证「展开失败只写状态、不崩树」。</summary>
internal sealed class ThrowingMetadataExplorer : IMetadataExplorer
{
    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(["main"]);

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default) =>
        Task.FromException<IReadOnlyList<DbObjectInfo>>(new InvalidOperationException("元数据不可用"));

    public Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbColumnInfo>>([]);

    public Task<IReadOnlyList<DbPartitionInfo>> GetPartitionsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbPartitionInfo>>([]);

    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

/// <summary>
/// 库清单可挂起 / 可失败的元数据替身：验证「并发连接被挡下」与「连接失败不留死会话」两条壳层路径。
/// </summary>
internal sealed class GatedMetadataExplorer : IMetadataExplorer
{
    private readonly TaskCompletionSource _databases = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DatabaseQueryCount { get; private set; }

    public Exception? DatabasesFailure { get; init; }

    public void ReleaseDatabases() => _databases.TrySetResult();

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default)
    {
        DatabaseQueryCount++;
        if (DatabasesFailure is not null)
        {
            throw DatabasesFailure;
        }

        await _databases.Task.ConfigureAwait(false);
        return ["main"];
    }

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbObjectInfo>>([]);

    public Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbColumnInfo>>([]);

    public Task<IReadOnlyList<DbPartitionInfo>> GetPartitionsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DbPartitionInfo>>([]);

    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}

internal sealed class StubExportDialog(ExportTarget? target = null) : IExportDialog
{
    public ExportTarget? Target { get; set; } = target;

    public ExportFormat? LastFormat { get; private set; }

    public Task<ExportTarget?> ShowAsync(ExportFormat format, string? suggestedFileName = null)
    {
        LastFormat = format;
        return Task.FromResult(Target);
    }
}

/// <summary>
/// 假的主密钥提供器：让信封加密逻辑可在无密钥库环境（CI / Windows）完整单测。
/// 传入固定 <paramref name="key"/> 可模拟"换了密钥"或"密钥损坏"。
/// </summary>
internal sealed class FakeMasterKeyProvider(byte[]? key = null) : IMasterKeyProvider
{
    public byte[] Key { get; } = key ?? RandomNumberGenerator.GetBytes(SecretKeyEnvelope.KeyLength);

    public int CallCount { get; private set; }

    public string Name => "fake-keyring";

    public ValueTask<byte[]> GetOrCreateMasterKeyAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return ValueTask.FromResult(Key);
    }
}

/// <summary>壳层测试用的口令保护器替身：只暴露名字、是否加密与回退原因，不做任何真实加解密。</summary>
internal sealed class FakeSecretProtector(string name, bool isEncrypted, string? fallbackReason = null) : ISecretProtector
{
    public string Name { get; } = name;

    public bool IsEncrypted { get; } = isEncrypted;

    public string? FallbackReason { get; } = fallbackReason;

    public ValueTask<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(plainText);

    public ValueTask<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(protectedText);
}

/// <summary>内存版连接清单存储：让 MainViewModel 的持久化逻辑无需真实文件与密钥库即可测。</summary>
internal sealed class StubConnectionStore(
    IReadOnlyList<ConnectionProfile>? initial = null,
    bool isEncrypted = true,
    string? fallbackReason = null) : IConnectionStore
{
    public ISecretProtector Protector { get; } =
        new FakeSecretProtector(isEncrypted ? "stub-encrypted" : "plaintext", isEncrypted, fallbackReason);

    public List<ConnectionProfile> Saved { get; set; } = [.. initial ?? []];

    public int LoadCount { get; private set; }

    public int SaveCount { get; private set; }

    public Exception? LoadFailure { get; init; }

    public Exception? CreateFailure { get; init; }

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default)
    {
        LoadCount++;
        return LoadFailure is not null
            ? Task.FromException<IReadOnlyList<ConnectionProfile>>(LoadFailure)
            : Task.FromResult<IReadOnlyList<ConnectionProfile>>(Saved.ToList());
    }

    public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default)
    {
        SaveCount++;
        Saved = [.. profiles];
        return Task.CompletedTask;
    }
}

/// <summary>可编排失败的 store 工厂（测"启动时读清单失败也不崩"）。</summary>
internal sealed class StubConnectionStoreFactory(StubConnectionStore? store = null) : IConnectionStoreFactory
{
    public StubConnectionStore? Store { get; } = store;

    public Task<IConnectionStore> CreateAsync(CancellationToken ct = default) =>
        Store?.CreateFailure is { } failure
            ? Task.FromException<IConnectionStore>(failure)
            : Task.FromResult<IConnectionStore>(Store!);
}

/// <summary>
/// 记录 <see cref="CreateAsync"/> / <c>LoadAsync</c> / <c>SaveAsync</c> 各落在哪个托管线程：
/// 壳层必须把密钥库探测与清单读写放到后台线程（AGENTS §5），否则 macOS 钥匙串的同步 P/Invoke 会卡 UI。
/// </summary>
internal sealed class ThreadRecordingConnectionStoreFactory : IConnectionStoreFactory
{
    public int CreateThreadId { get; private set; }

    public int LoadThreadId { get; private set; }

    public int SaveThreadId { get; private set; }

    public Task<IConnectionStore> CreateAsync(CancellationToken ct = default)
    {
        CreateThreadId = Environment.CurrentManagedThreadId;
        return Task.FromResult<IConnectionStore>(new RecordingStore(this));
    }

    private sealed class RecordingStore(ThreadRecordingConnectionStoreFactory owner) : IConnectionStore
    {
        public ISecretProtector Protector { get; } = new FakeSecretProtector("stub-encrypted", isEncrypted: true);

        public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken ct = default)
        {
            owner.LoadThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>([]);
        }

        public Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken ct = default)
        {
            owner.SaveThreadId = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        }
    }
}
