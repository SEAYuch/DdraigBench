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
            // 触发器：tgisinternal 为真是约束/外键的实现细节，不该出现在用户视图里。
            // pg_get_triggerdef() 直接给完整 CREATE TRIGGER（含时机、事件、表名与体），无需拼装。
            DbObjectKind.Trigger =>
                "SELECT t.tgname AS name, pg_get_triggerdef(t.oid, true) AS definition FROM pg_trigger t "
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

    /// <summary>
    /// 例程清单 + 原始定义。
    /// <para>
    /// <c>pg_get_functiondef()</c> 对**聚合函数**（<c>CREATE AGGREGATE</c>、<c>pg_aggregate.aggfnoid</c> 非空）
    /// 与 <c>LANGUAGE internal</c>/<c>c</c> 的函数会**直接报错**（实测 PG 13：<c>"agg1" 是一个聚合函数</c>），
    /// 一条坏行足以让整个清单查询失败、树上就看不到任何例程。故用 <c>CASE</c> 把这类行折成 NULL 定义：
    /// 定义取不到只是这一项没有 DDL 预览，清单本身照常列出。
    /// </para>
    /// </summary>
    private static string RoutineQuery(string schema, char prokind) =>
        "SELECT p.proname AS name, "
        + "CASE WHEN l.lanname IN ('sql', 'plpgsql') AND a.aggfnoid IS NULL "
        + "THEN pg_get_functiondef(p.oid) END AS definition "
        + "FROM pg_proc p "
        + "INNER JOIN pg_namespace n ON n.oid = p.pronamespace "
        + "INNER JOIN pg_language l ON l.oid = p.prolang "
        + "LEFT JOIN pg_aggregate a ON a.aggfnoid = p.oid "
        + $"WHERE n.nspname = {schema} AND p.proname NOT LIKE 'pg\\_%' AND p.prokind = '{prokind}' "
        + "ORDER BY p.proname";

    public bool SupportsViewDefinitions => true;

    /// <summary>
    /// 普通视图（<c>relkind='v'</c>）与**物化视图**（<c>relkind='m'</c>）共用一条查询：
    /// 两者的定义体都从 <c>pg_get_viewdef()</c> 取，但前缀不同，故直接在 SQL 里按 <c>relkind</c> 拼成完整语句
    /// （<c>ComposeViewDdl</c> 因此退化为原样返回，与 SQLite / SQL Server 的"查询即完整语句"一致）。
    /// <para>
    /// 物化视图若被误拼成 <c>CREATE OR REPLACE VIEW</c>，用户拿这份 DDL 去执行会得到一个**普通视图**——
    /// 语义被静默改变（不再物化、不再有 <c>REFRESH MATERIALIZED VIEW</c>），故必须按 relkind 分开。
    /// </para>
    /// </summary>
    public string BuildViewDefinitionQuery(string database, string view) =>
        "SELECT CASE c.relkind WHEN 'm' THEN 'CREATE MATERIALIZED VIEW ' ELSE 'CREATE OR REPLACE VIEW ' END "
        + "|| quote_ident(n.nspname) || '.' || quote_ident(c.relname) || ' AS ' || pg_get_viewdef(c.oid, true) AS definition "
        + "FROM pg_class c "
        + "INNER JOIN pg_namespace n ON n.oid = c.relnamespace "
        + $"WHERE c.relkind IN ('v', 'm') AND n.nspname = {Dialect.FormatLiteral(database)} "
        + $"AND c.relname = {Dialect.FormatLiteral(view)}";

    /// <summary>查询已按 <c>relkind</c> 拼好 <c>CREATE [MATERIALIZED] VIEW</c> 前缀，此处只需去掉尾部分号。</summary>
    public string ComposeViewDdl(string database, string view, string definition) =>
        definition.Trim().TrimEnd(';');

    /// <summary>PostgreSQL 无「原始建表语句」可查（系统表里只存解析后的结构），不走本契约。</summary>
    public bool SupportsTableDefinitions => false;

    public string BuildTableDefinitionQuery(string database, string table) =>
        throw new NotSupportedException("PostgreSQL 不保存原始建表语句，表 DDL 由元数据重建（DdlGenerator）");

    private static string QualifiedName(string database, string name) =>
        $"{Dialect.QuoteIdentifier(database)}.{Dialect.QuoteIdentifier(name)}";

    private static string UnsupportedMessage(DbObjectKind kind) =>
        $"PostgreSQL 的目录查询不支持 {kind}（见 AGENTS.md §6.2）";
}
