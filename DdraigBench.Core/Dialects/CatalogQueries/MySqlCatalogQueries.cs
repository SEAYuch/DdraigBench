// DdraigBench — MySQL / MariaDB 目录查询（information_schema）

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// MySQL / MariaDB 的 IDbFirst 缺口补齐，全部走 <c>information_schema</c>
/// （触发器 <c>TRIGGERS</c>、例程 <c>ROUTINES</c>、视图 <c>VIEWS</c>）。
/// 这些视图对「本库范围内」的对象可见，故一律按 <c>*_SCHEMA = &lt;database&gt;</c> 收窄，
/// 不会把同实例其他库的对象混进来。
/// </summary>
public sealed class MySqlCatalogQueries : ICatalogQueries
{
    public static readonly MySqlCatalogQueries Instance = new();

    private static MySqlDialect Dialect => MySqlDialect.Instance;

    /// <summary>MySQL 的库等价于模式，没有独立的模式层。</summary>
    public bool Supports(DbObjectKind kind) =>
        kind is DbObjectKind.Trigger or DbObjectKind.Function or DbObjectKind.StoredProcedure;

    /// <summary>MySQL 一个连接可用 <c>USE</c> 切库，且 <c>information_schema</c> 本就跨库可见，故不是"钉死一个库"。</summary>
    public bool ConnectionIsBoundToSingleDatabase => false;

    public string BuildCurrentDatabaseQuery() =>
        throw new NotSupportedException("MySQL 的库清单由 IDbFirst.GetDatabases() 提供（information_schema.schemata）");

    public string BuildObjectsQuery(string database, DbObjectKind kind)
    {
        var schema = Dialect.FormatLiteral(database);

        return kind switch
        {
            DbObjectKind.Trigger =>
                "SELECT TRIGGER_NAME AS name FROM information_schema.TRIGGERS "
                + $"WHERE TRIGGER_SCHEMA = {schema} ORDER BY TRIGGER_NAME",
            DbObjectKind.Function =>
                "SELECT ROUTINE_NAME AS name FROM information_schema.ROUTINES "
                + $"WHERE ROUTINE_SCHEMA = {schema} AND ROUTINE_TYPE = 'FUNCTION' ORDER BY ROUTINE_NAME",
            DbObjectKind.StoredProcedure =>
                "SELECT ROUTINE_NAME AS name FROM information_schema.ROUTINES "
                + $"WHERE ROUTINE_SCHEMA = {schema} AND ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_NAME",
            _ => throw new NotSupportedException(UnsupportedMessage(kind)),
        };
    }

    public bool SupportsViewDefinitions => true;

    public string BuildViewDefinitionQuery(string database, string view) =>
        "SELECT VIEW_DEFINITION AS definition FROM information_schema.VIEWS "
        + $"WHERE TABLE_SCHEMA = {Dialect.FormatLiteral(database)} "
        + $"AND TABLE_NAME = {Dialect.FormatLiteral(view)}";

    /// <summary>
    /// <c>information_schema.VIEWS.VIEW_DEFINITION</c> 只给 SELECT 主体（不含 <c>CREATE VIEW</c> 前缀），
    /// 故在此补前缀与限定名（不用 <c>SHOW CREATE VIEW</c>：它要求 SHOW VIEW 权限，且返回两列、
    /// 列名不固定，不适合本契约的单列约定）。
    /// </summary>
    public string ComposeViewDdl(string database, string view, string definition) =>
        $"CREATE VIEW {QualifiedName(database, view)} AS\n{definition.Trim().TrimEnd(';')};";

    /// <summary>MySQL 的表定义只能通过 <c>SHOW CREATE TABLE</c> 取得（多列、列名不固定），不走本契约。</summary>
    public bool SupportsTableDefinitions => false;

    public string BuildTableDefinitionQuery(string database, string table) =>
        throw new NotSupportedException("MySQL 的表定义需走 SHOW CREATE TABLE，不在目录查询契约内");

    private static string QualifiedName(string database, string name) =>
        $"{Dialect.QuoteIdentifier(database)}.{Dialect.QuoteIdentifier(name)}";

    private static string UnsupportedMessage(DbObjectKind kind) =>
        $"MySQL 的目录查询不支持 {kind}（见 AGENTS.md §6.2）";
}
