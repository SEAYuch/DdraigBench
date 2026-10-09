// DdraigBench — IMetadataExplorer 实现：IDbFirst（表/视图/列）+ 目录查询（触发器/例程/定义）

using DdraigBench.Core.Dialects;
using DdraigBench.Core.Dialects.CatalogQueries;
using DdraigBench.Core.Sessions;
using FreeSql.DatabaseModel;

namespace DdraigBench.Core.Metadata;

/// <summary>
/// 元数据两条通道：
/// <list type="number">
///   <item>IDbFirst：表 / 视图 / 列 / 主键 / 索引 / 外键——白嫖 FreeSql 的 22 方言（AGENTS.md §6.1）。</item>
///   <item>目录查询（<c>Dialects/CatalogQueries</c>）：IDbFirst 覆盖不到的触发器 / 函数 / 存储过程，
///     以及视图与表的「原始定义」——比按元数据重建保真（AGENTS.md §6.2）。</item>
/// </list>
/// </summary>
public sealed class FreeSqlMetadataExplorer : IMetadataExplorer
{
    /// <summary>
    /// 可浏览的库清单。**PostgreSQL 例外**：一个连接只能看到所连库内的对象（跨库要换连接或 dblink/FDW），
    /// 而 FreeSql 的 <c>GetDatabases()</c> 返回的是 <c>pg_database</c> 全量库名——列进树就是"看得见打不开"。
    /// 故 PG 收窄为当前库；库内再靠 <see cref="DbObjectKind.Schema"/> 这一层展开（M5-2）。
    /// </summary>
    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog)
            || !catalog.ConnectionIsBoundToSingleDatabase)
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                return session.Fsql.DbFirst.GetDatabases();
            }, ct);
        }

        return CurrentDatabaseOnlyAsync(session, catalog, ct);
    }

    private static async Task<IReadOnlyList<string>> CurrentDatabaseOnlyAsync(
        DbSession session, ICatalogQueries catalog, CancellationToken ct)
    {
        var name = await CatalogQueryExecutor.ReadScalarAsync(session, catalog.BuildCurrentDatabaseQuery(), ct);
        return string.IsNullOrWhiteSpace(name) ? Array.Empty<string>() : [name];
    }

    public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default) =>
        kind switch
        {
            DbObjectKind.Table => TablesAndViewsAsync(session, database, DbTableType.TABLE, ct),
            DbObjectKind.View => TablesAndViewsAsync(session, database, DbTableType.VIEW, ct),
            // Schema = 库内的模式层（PG 有；MySQL 的库即模式、SQLite 无模式 → 空集）
            DbObjectKind.Schema or DbObjectKind.Trigger or DbObjectKind.Function or DbObjectKind.StoredProcedure
                => CatalogObjectsAsync(session, database, kind, ct),
            _ => throw new NotSupportedException(
                $"{kind} 不是「容器级对象集合」：列请走 GetColumnsAsync，索引/外键挂在表级元数据（DbTableInfo.IndexesDict / ForeignsDict）上"),
        };

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

    /// <summary>
    /// DDL 预览。优先级：原始定义 &gt; 按元数据重建。
    /// 原始定义的保真度明显更高——SQLite 的 <c>AUTOINCREMENT</c>、内联 <c>UNIQUE</c>、
    /// <c>CHECK</c>、<c>COLLATE</c> 都只存在于 <c>sqlite_master.sql</c> 里（AGENTS.md §6.2 缺口的①②③由此一并闭合）。
    /// </summary>
    public Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default) =>
        Task.Run(() => GetDdlCoreAsync(session, database, table, ct), ct);

    private static async Task<string?> GetDdlCoreAsync(
        DbSession session, string database, string name, CancellationToken ct)
    {
        if (!SqlDialects.TryGet(session.Profile.DataType, out var dialect))
        {
            throw new NotSupportedException($"该方言暂不支持 DDL 预览：{session.Profile.DataType}");
        }

        var match = await Task.Run(
            () => session.Fsql.DbFirst.GetTablesByDatabase(database).FirstOrDefault(t => t.Name == name), ct);

        if (match is null)
        {
            // 表/视图里没有，再按触发器 / 函数 / 存储过程找（IDbFirst 完全看不到它们）
            return await TryReadRoutineDdlAsync(session, database, name, ct);
        }

        if (match.Type == DbTableType.VIEW)
        {
            return await TryReadViewDdlAsync(session, database, name, ct)
                ?? new DdlGenerator(dialect).CreateTable(match);
        }

        if (CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog) && catalog.SupportsTableDefinitions)
        {
            var raw = await CatalogQueryExecutor.ReadScalarAsync(
                session, catalog.BuildTableDefinitionQuery(database, name), ct);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                return Terminate(raw);
            }
        }

        return new DdlGenerator(dialect).CreateTable(match);
    }

    private static async Task<string?> TryReadViewDdlAsync(
        DbSession session, string database, string view, CancellationToken ct)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog) || !catalog.SupportsViewDefinitions)
        {
            return null;
        }

        var body = await CatalogQueryExecutor.ReadScalarAsync(
            session, catalog.BuildViewDefinitionQuery(database, view), ct);

        return string.IsNullOrWhiteSpace(body)
            ? null
            : Terminate(catalog.ComposeViewDdl(database, view, body));
    }

    private static async Task<string?> TryReadRoutineDdlAsync(
        DbSession session, string database, string name, CancellationToken ct)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog))
        {
            return null;
        }

        foreach (var kind in new[] { DbObjectKind.Trigger, DbObjectKind.Function, DbObjectKind.StoredProcedure })
        {
            if (!catalog.Supports(kind))
            {
                continue;
            }

            var objects = await CatalogQueryExecutor.ReadObjectsAsync(
                session, catalog.BuildObjectsQuery(database, kind), ct);

            if (objects.FirstOrDefault(o => o.Name == name)?.Definition is { Length: > 0 } definition)
            {
                return Terminate(definition);
            }
        }

        return null;
    }

    private static Task<IReadOnlyList<DbObjectInfo>> TablesAndViewsAsync(
        DbSession session, string database, DbTableType wanted, CancellationToken ct) =>
        Task.Run<IReadOnlyList<DbObjectInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            return session.Fsql.DbFirst.GetTablesByDatabase(database)
                .Where(t => t.Type == wanted)
                .Select(t => new DbObjectInfo(
                    t.Name,
                    string.IsNullOrEmpty(t.Schema) ? null : t.Schema,
                    t.Comment))
                .ToList();
        }, ct);

    private static async Task<IReadOnlyList<DbObjectInfo>> CatalogObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog))
        {
            throw new NotSupportedException($"尚未接入该方言的目录查询：{session.Profile.DataType}");
        }

        // 方言没有这类对象（例如 SQLite 的存储过程）：空集而非报错，树照常展开
        if (!catalog.Supports(kind))
        {
            return Array.Empty<DbObjectInfo>();
        }

        return await CatalogQueryExecutor.ReadObjectsAsync(
            session, catalog.BuildObjectsQuery(database, kind), ct);
    }

    /// <summary>原始语句多半不带结尾分号（SQLite 就不带），补齐以便直接复制执行。</summary>
    private static string Terminate(string sql)
    {
        var trimmed = sql.TrimEnd();
        return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
    }
}
