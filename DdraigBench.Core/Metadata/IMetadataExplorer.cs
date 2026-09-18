// DdraigBench — 左侧树的统一元数据 API

using DdraigBench.Core.Sessions;

namespace DdraigBench.Core.Metadata;

public interface IMetadataExplorer
{
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default);

    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default);

    Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default);
}
