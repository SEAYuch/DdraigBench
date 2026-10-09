// DdraigBench — SQLite 目录查询（sqlite_master）

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// SQLite 的 IDbFirst 缺口补齐。SQLite 把一切对象都登记在 <c>sqlite_master</c>，
/// 且 <c>sql</c> 列保存的是**原始建表/建视图/建触发器语句**——因此本方言的「定义」类查询
/// 天然给出完整可执行语句（含 <c>AUTOINCREMENT</c>、<c>UNIQUE</c>、<c>CHECK</c>、<c>COLLATE</c>），
/// 比按元数据重建的 DDL 保真得多。
/// </summary>
public sealed class SqliteCatalogQueries : ICatalogQueries
{
    public static readonly SqliteCatalogQueries Instance = new();

    private static SqliteDialect Dialect => SqliteDialect.Instance;

    /// <summary>SQLite 无存储过程与（SQL 层可枚举的）函数——内建函数是 C 层注册的，不在 sqlite_master 里。也无模式层。</summary>
    public bool Supports(DbObjectKind kind) => kind == DbObjectKind.Trigger;

    public string BuildObjectsQuery(string database, DbObjectKind kind) => kind switch
    {
        DbObjectKind.Trigger =>
            $"SELECT name AS name, sql AS definition FROM {Master(database)} WHERE type = 'trigger' ORDER BY name",
        _ => throw new NotSupportedException(UnsupportedMessage(kind)),
    };

    /// <summary>SQLite 一个连接可 <c>ATTACH</c> 多个库，且 <c>IDbFirst.GetDatabases()</c> 已给出这些附加库。</summary>
    public bool ConnectionIsBoundToSingleDatabase => false;

    public string BuildCurrentDatabaseQuery() =>
        throw new NotSupportedException("SQLite 的库清单由 IDbFirst.GetDatabases() 提供（main + 各附加库）");

    public bool SupportsViewDefinitions => true;

    public string BuildViewDefinitionQuery(string database, string view) =>
        $"SELECT sql AS definition FROM {Master(database)} "
        + $"WHERE type = 'view' AND name = {Dialect.FormatLiteral(view)}";

    /// <summary>sqlite_master.sql 已是完整语句（例如 <c>CREATE VIEW v1 AS SELECT ...</c>），原样返回。</summary>
    public string ComposeViewDdl(string database, string view, string definition) => definition;

    public bool SupportsTableDefinitions => true;

    public string BuildTableDefinitionQuery(string database, string table) =>
        $"SELECT sql AS definition FROM {Master(database)} "
        + $"WHERE type = 'table' AND name = {Dialect.FormatLiteral(table)}";

    /// <summary>限定到某个附加库：<c>"main".sqlite_master</c>。</summary>
    private static string Master(string database) =>
        $"{Dialect.QuoteIdentifier(database)}.sqlite_master";

    private static string UnsupportedMessage(DbObjectKind kind) =>
        $"SQLite 的目录查询不支持 {kind}（见 AGENTS.md §6.2）";
}
