// DdraigBench — IMetadataExplorer 的 FreeSql(IDbFirst) 实现

using DdraigBench.Core.Dialects;
using DdraigBench.Core.Sessions;
using FreeSql.DatabaseModel;

namespace DdraigBench.Core.Metadata;

public sealed class FreeSqlMetadataExplorer : IMetadataExplorer
{
    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            return session.Fsql.DbFirst.GetDatabases();
        }, ct);

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DbObjectInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            var wanted = kind switch
            {
                DbObjectKind.Table => DbTableType.TABLE,
                DbObjectKind.View => DbTableType.VIEW,
                _ => throw new NotSupportedException(
                    $"元数据种类 {kind} 尚未实现（IDbFirst 缺口，见 AGENTS.md §6.2）"),
            };

            return session.Fsql.DbFirst.GetTablesByDatabase(database)
                .Where(t => t.Type == wanted)
                .Select(t => new DbObjectInfo(
                    t.Name,
                    string.IsNullOrEmpty(t.Schema) ? null : t.Schema,
                    t.Comment))
                .ToList();
        }, ct);

    public Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DbColumnInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            var match = session.Fsql.DbFirst
                .GetTablesByDatabase(database)
                .FirstOrDefault(t => t.Name == table);

            if (match is null)
            {
                return Array.Empty<DbColumnInfo>();
            }

            return match.Columns
                .Select(c => new DbColumnInfo(c.Name, c.DbTypeText, c.IsPrimary, c.IsNullable, c.Comment))
                .ToList();
        }, ct);

    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.Run<string?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!SqlDialects.TryGet(session.Profile.DataType, out var dialect))
            {
                throw new NotSupportedException($"该方言暂不支持 DDL 预览：{session.Profile.DataType}");
            }

            var match = session.Fsql.DbFirst
                .GetTablesByDatabase(database)
                .FirstOrDefault(t => t.Name == table);

            return match is null ? null : new DdlGenerator(dialect).CreateTable(match);
        }, ct);
}
