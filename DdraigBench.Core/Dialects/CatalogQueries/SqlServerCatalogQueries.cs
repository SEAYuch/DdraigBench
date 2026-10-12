// DdraigBench — SQL Server 目录查询能力

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// SQL Server 的用户模式、DML 触发器、函数、存储过程与视图定义查询。
/// 表 / 视图 / 列结构仍由 FreeSql IDbFirst 提供；表 DDL 由 DdlGenerator 重建。
/// </summary>
public sealed class SqlServerCatalogQueries : ICatalogQueries
{
    public static readonly SqlServerCatalogQueries Instance = new();

    private static SqlServerDialect Dialect => SqlServerDialect.Instance;

    public bool Supports(DbObjectKind kind) =>
        kind is DbObjectKind.Schema or DbObjectKind.Trigger or DbObjectKind.Function or DbObjectKind.StoredProcedure;

    public string BuildObjectsQuery(string database, DbObjectKind kind)
    {
        return kind switch
        {
            // 一个连接只浏览当前数据库；从数据库根节点传入当前库名，列出用户模式。
            DbObjectKind.Schema =>
                "SELECT s.name AS name FROM sys.schemas AS s "
                + $"WHERE DB_NAME() = {UnicodeLiteral(database)} AND s.name NOT IN (N'sys', N'INFORMATION_SCHEMA') "
                + "ORDER BY s.name",
            // DML 触发器属于表/视图所在的模式；数据库级 DDL 触发器不属于模式树。
            DbObjectKind.Trigger =>
                "SELECT tr.name AS name, s.name AS [schema], OBJECT_DEFINITION(tr.object_id) AS definition "
                + "FROM sys.triggers AS tr "
                + "INNER JOIN sys.objects AS parent ON parent.object_id = tr.parent_id "
                + "INNER JOIN sys.schemas AS s ON s.schema_id = parent.schema_id "
                + $"WHERE tr.parent_class = 1 AND tr.is_ms_shipped = 0 AND s.name = {UnicodeLiteral(database)} "
                + "ORDER BY tr.name",
            DbObjectKind.Function =>
                "SELECT o.name AS name, s.name AS [schema], sm.definition AS definition "
                + "FROM sys.objects AS o "
                + "INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id "
                + "LEFT JOIN sys.sql_modules AS sm ON sm.object_id = o.object_id "
                + $"WHERE o.type IN ('FN', 'IF', 'TF', 'FS', 'FT', 'AF') AND o.is_ms_shipped = 0 AND s.name = {UnicodeLiteral(database)} "
                + "ORDER BY o.name",
            DbObjectKind.StoredProcedure =>
                "SELECT o.name AS name, s.name AS [schema], sm.definition AS definition "
                + "FROM sys.objects AS o "
                + "INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id "
                + "LEFT JOIN sys.sql_modules AS sm ON sm.object_id = o.object_id "
                + $"WHERE o.type IN ('P', 'PC') AND o.is_ms_shipped = 0 AND s.name = {UnicodeLiteral(database)} "
                + "ORDER BY o.name",
            _ => throw new NotSupportedException($"SQL Server 目录查询不支持 {kind}"),
        };
    }

    public bool ConnectionIsBoundToSingleDatabase => true;

    public string BuildCurrentDatabaseQuery() => "SELECT DB_NAME() AS name";

    public bool SupportsViewDefinitions => true;

    public string BuildViewDefinitionQuery(string database, string view) =>
        "SELECT sm.definition AS definition FROM sys.views AS v "
        + "INNER JOIN sys.schemas AS s ON s.schema_id = v.schema_id "
        + "LEFT JOIN sys.sql_modules AS sm ON sm.object_id = v.object_id "
        + $"WHERE s.name = {UnicodeLiteral(database)} AND v.name = {UnicodeLiteral(view)}";

    /// <summary><c>sys.sql_modules.definition</c> 已经是完整 T-SQL CREATE VIEW 语句。</summary>
    public string ComposeViewDdl(string database, string view, string definition) => definition.Trim();

    public bool SupportsTableDefinitions => false;

    public string BuildTableDefinitionQuery(string database, string table) =>
        throw new NotSupportedException("SQL Server 表定义由 FreeSql 元数据重建（DdlGenerator）");

    private static string UnicodeLiteral(string value) => "N" + Dialect.FormatLiteral(value);
}
