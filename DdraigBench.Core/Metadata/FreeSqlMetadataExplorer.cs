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
///   <item>目录查询（<c>Dialects/CatalogQueries</c>）：IDbFirst 覆盖不到的模式 / 触发器 / 函数 / 存储过程，
///     以及视图与表的「原始定义」——比按元数据重建保真（AGENTS.md §6.2）。</item>
/// </list>
/// </summary>
public sealed class FreeSqlMetadataExplorer : IMetadataExplorer
{
    /// <summary>
    /// 可浏览的库清单。PostgreSQL 一个连接只能访问所连库，FreeSql 却返回全量 <c>pg_database</c>，
    /// 所以收窄为当前库（M5-2）。SQL Server 的 <c>IDbFirst.GetDatabases()</c> 实测为空，也改用当前库查询；
    /// 两者再通过 <see cref="DbObjectKind.Schema"/> 展开库内模式。
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
            // Schema = 库内的模式层（PG / SQL Server 有）；Event 是 MySQL/MariaDB 的库级对象。
            // 两者都由 Supports(kind) 控制，方言不支持时返回空集。
            DbObjectKind.Schema or DbObjectKind.Trigger or DbObjectKind.Function
                or DbObjectKind.StoredProcedure or DbObjectKind.Event
                => CatalogObjectsAsync(session, database, kind, ct),
            DbObjectKind.Partition => throw new NotSupportedException(
                "Partition 是表级子结构，请走 GetPartitionsAsync，而不是容器级 GetObjectsAsync"),
            _ => throw new NotSupportedException(
                $"{kind} 不是「容器级对象集合」：列请走 GetColumnsAsync，索引/外键挂在表级元数据（DbTableInfo.IndexesDict / ForeignsDict）上"),
        };

    public async Task<IReadOnlyList<DbPartitionInfo>> GetPartitionsAsync(
        DbSession session, string container, string table, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog) || !catalog.SupportsTablePartitions)
        {
            return Array.Empty<DbPartitionInfo>();
        }

        var sql = catalog.BuildTablePartitionsQuery(container, table);
        return await CatalogQueryExecutor.ReadPartitionsAsync(session, sql, ct);
    }

    public async Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string container, string table, CancellationToken ct = default)
    {
        var database = await GetIdbFirstDatabaseAsync(session, container, ct);
        return await Task.Run<IReadOnlyList<DbColumnInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            var match = session.Fsql.DbFirst
                .GetTablesByDatabase(database)
                .FirstOrDefault(t => t.Name == table && MatchesSchemaContainer(session, t.Schema, container));

            if (match is null)
            {
                return Array.Empty<DbColumnInfo>();
            }

            return match.Columns
                .Select(c => new DbColumnInfo(c.Name, c.DbTypeText, c.IsPrimary, c.IsNullable, c.Comment))
                .ToList();
        }, ct);
    }

    /// <summary>
    /// DDL 预览。优先级：目录里的原始定义 &gt; 按元数据重建。
    /// SQLite 的 <c>sqlite_master.sql</c> 保留 <c>AUTOINCREMENT</c> / 内联约束；SQL Server 的视图与 DML 触发器、函数、存储过程读取原始模块定义。
    /// SQL Server 表仍由 <see cref="DdlGenerator"/> 重建；缺少原始定义的视图回落到结构提示。
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

        var idbFirstDatabase = await GetIdbFirstDatabaseAsync(session, database, ct);
        var match = await Task.Run(
            () => session.Fsql.DbFirst.GetTablesByDatabase(idbFirstDatabase)
                .FirstOrDefault(t => t.Name == name && MatchesSchemaContainer(session, t.Schema, database)), ct);

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
            var raw = await CatalogQueryExecutor.ReadDefinitionAsync(
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

        foreach (var kind in new[]
                 {
                     DbObjectKind.Trigger, DbObjectKind.Function, DbObjectKind.StoredProcedure, DbObjectKind.Event,
                 })
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

    private static async Task<IReadOnlyList<DbObjectInfo>> TablesAndViewsAsync(
        DbSession session, string container, DbTableType wanted, CancellationToken ct)
    {
        var database = await GetIdbFirstDatabaseAsync(session, container, ct);
        return await Task.Run<IReadOnlyList<DbObjectInfo>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            return session.Fsql.DbFirst.GetTablesByDatabase(database)
                .Where(t => t.Type == wanted && MatchesSchemaContainer(session, t.Schema, container))
                .Select(t => new DbObjectInfo(
                    t.Name,
                    string.IsNullOrEmpty(t.Schema) ? null : t.Schema,
                    t.Comment))
                .ToList();
        }, ct);
    }

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

    /// <summary>
    /// 把树交给我们的「容器」换成 <c>IDbFirst</c> 认的库名。
    /// <para>
    /// PostgreSQL 与 SQL Server 的 <c>GetTablesByDatabase</c> 期望的是**库名**，但 PG 下树传进来的是模式名
    /// （SQL Server 下模式另成一层，对象容器同样是模式名）。PG 实测（PG 13，2026-10-12）：传
    /// <c>"public"</c> 返回 0 行，传当前库名才拿到所连库的对象（<c>DbTableInfo.Schema</c> 是真实模式名）
    /// ——传模式名时会去找同名数据库，找不到就静默空集，于是 PG 的表/视图之前根本列不出来。
    /// 换成当前库名后必须由 <see cref="MatchesSchemaContainer"/> 按模式收敛。
    /// </para>
    /// </summary>
    private static async Task<string> GetIdbFirstDatabaseAsync(DbSession session, string container, CancellationToken ct)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog)
            || !catalog.ConnectionIsBoundToSingleDatabase)
        {
            return container;
        }

        var current = await CatalogQueryExecutor.ReadScalarAsync(
            session, catalog.BuildCurrentDatabaseQuery(), ct);
        return string.IsNullOrWhiteSpace(current) ? container : current;
    }

    /// <summary>
    /// 是否需要自行按 <c>DbTableInfo.Schema</c> 收敛到调用方要的模式。
    /// MySQL 的 <c>GetTablesByDatabase(schema)</c> 已按库收窄（库即模式）；SQLite 的 <c>Schema</c> 是空串、
    /// 容器是附加库名——这两者都不能再过滤。PG / SQL Server 换成了库名查询，必须自己过滤。
    /// </summary>
    private static bool MatchesSchemaContainer(DbSession session, string? schema, string container)
    {
        if (!CatalogQuerySets.TryGet(session.Profile.DataType, out var catalog)
            || !catalog.ConnectionIsBoundToSingleDatabase)
        {
            return true;
        }

        return string.Equals(schema, container, StringComparison.Ordinal);
    }

    /// <summary>原始语句多半不带结尾分号（SQLite 就不带），补齐以便直接复制执行。</summary>
    private static string Terminate(string sql)
    {
        var trimmed = sql.TrimEnd();
        return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
    }
}
