// DdraigBench — PostgreSQL 目录查询（pg_catalog）

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// PostgreSQL 的 IDbFirst 缺口补齐，全部走 <c>pg_catalog</c>：
/// 触发器 <c>pg_trigger</c>、例程 <c>pg_proc</c>、视图定义 <c>pg_get_viewdef()</c>。
/// </summary>
/// <remarks>
/// 已知版本要求：<c>pg_proc.prokind</c> 是 PostgreSQL 11 才引入的（10 及更早用 <c>proisagg</c>/<c>proiswindow</c> 组合），
/// 故本实现要求 PG ≥ 11；低版本上例程清单会直接报错而不是静默给错结果。
/// </remarks>
public sealed class PostgreSqlCatalogQueries : ICatalogQueries
{
    public static readonly PostgreSqlCatalogQueries Instance = new();

    private static PostgreSqlDialect Dialect => PostgreSqlDialect.Instance;

    /// <summary>PostgreSQL 有独立的模式层；这是 <c>GetDatabasesAsync()</c> 返回库名、而对象查询按模式过滤所必需的中间层。</summary>
    public bool Supports(DbObjectKind kind) =>
        kind is DbObjectKind.Schema or DbObjectKind.Trigger or DbObjectKind.Function or DbObjectKind.StoredProcedure;

    /// <summary>
    /// 一个 PG 连接只能看到所连库内的模式（跨库要换连接或 dblink/FDW），
    /// 故库清单必须收窄成「当前库」——否则树会列出打不开的库（M5-2 修的既有缺陷）。
    /// </summary>
    public bool ConnectionIsBoundToSingleDatabase => true;

    public string BuildCurrentDatabaseQuery() => "SELECT current_database() AS name";

    public string BuildObjectsQuery(string database, DbObjectKind kind)
    {
        var schema = Dialect.FormatLiteral(database);

        return kind switch
        {
            // 模式清单：与 database 参数无关（pg_namespace 本就只含当前库的模式），故忽略它
            DbObjectKind.Schema =>
                "SELECT n.nspname AS name FROM pg_namespace n "
                + "WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast') "
                + @"AND n.nspname NOT LIKE 'pg\_temp%' AND n.nspname NOT LIKE 'pg\_toast_temp%' "
                + "ORDER BY n.nspname",
            // 触发器：tgisinternal 为真是约束/外键的实现细节，不该出现在用户视图里
            DbObjectKind.Trigger =>
                "SELECT t.tgname AS name FROM pg_trigger t "
                + "INNER JOIN pg_class c ON c.oid = t.tgrelid "
                + "INNER JOIN pg_namespace n ON n.oid = c.relnamespace "
                + $"WHERE NOT t.tgisinternal AND n.nspname = {schema} ORDER BY t.tgname",
            // prokind：'f' = 普通函数，'p' = 存储过程（PG ≥ 11；见类注释）。系统自带的 pg_* 前置过滤掉，
            // 否则用户库里会混进大量 pg_catalog 的函数（当 nspname 指向 pg_catalog 时）。
            DbObjectKind.Function => RoutineQuery(schema, 'f'),
            DbObjectKind.StoredProcedure => RoutineQuery(schema, 'p'),
            _ => throw new NotSupportedException(UnsupportedMessage(kind)),
        };
    }

    private static string RoutineQuery(string schema, char prokind) =>
        "SELECT p.proname AS name FROM pg_proc p "
        + "INNER JOIN pg_namespace n ON n.oid = p.pronamespace "
        + $"WHERE n.nspname = {schema} AND p.proname NOT LIKE 'pg\\_%' AND p.prokind = '{prokind}' "
        + "ORDER BY p.proname";

    public bool SupportsViewDefinitions => true;

    // relkind = 'v'：普通视图（物化视图是 'm'，其定义要改用 pg_get_viewdef 同样可读，此处先只收普通视图）
    public string BuildViewDefinitionQuery(string database, string view) =>
        "SELECT pg_get_viewdef(c.oid, true) AS definition FROM pg_class c "
        + "INNER JOIN pg_namespace n ON n.oid = c.relnamespace "
        + $"WHERE c.relkind = 'v' AND n.nspname = {Dialect.FormatLiteral(database)} "
        + $"AND c.relname = {Dialect.FormatLiteral(view)}";

    /// <summary><c>pg_get_viewdef()</c> 只返回 SELECT 主体（且可能带尾分号），在此补 <c>CREATE OR REPLACE VIEW</c> 前缀。</summary>
    public string ComposeViewDdl(string database, string view, string definition) =>
        $"CREATE OR REPLACE VIEW {QualifiedName(database, view)} AS\n{definition.Trim().TrimEnd(';')};";

    /// <summary>PostgreSQL 无「原始建表语句」可查（系统表里只存解析后的结构），不走本契约。</summary>
    public bool SupportsTableDefinitions => false;

    public string BuildTableDefinitionQuery(string database, string table) =>
        throw new NotSupportedException("PostgreSQL 不保存原始建表语句，表 DDL 由元数据重建（DdlGenerator）");

    private static string QualifiedName(string database, string name) =>
        $"{Dialect.QuoteIdentifier(database)}.{Dialect.QuoteIdentifier(name)}";

    private static string UnsupportedMessage(DbObjectKind kind) =>
        $"PostgreSQL 的目录查询不支持 {kind}（见 AGENTS.md §6.2）";
}
