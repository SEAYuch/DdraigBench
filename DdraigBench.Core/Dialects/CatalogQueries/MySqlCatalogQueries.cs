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
        kind is DbObjectKind.Trigger or DbObjectKind.Function or DbObjectKind.StoredProcedure
            or DbObjectKind.Event;

    /// <summary>MySQL 一个连接可用 <c>USE</c> 切库，且 <c>information_schema</c> 本就跨库可见，故不是"钉死一个库"。</summary>
    public bool ConnectionIsBoundToSingleDatabase => false;

    public string BuildCurrentDatabaseQuery() =>
        throw new NotSupportedException("MySQL 的库清单由 IDbFirst.GetDatabases() 提供（information_schema.schemata）");

    /// <summary>
    /// 清单同时带回 <c>definition</c> 列（原始定义），与 SQLite / SQL Server 的目录查询保持同一形状，
    /// 于是 DDL 预览不必再为「按名取定义」开第二条通道。
    /// <para>
    /// <c>information_schema.ROUTINES.ROUTINE_DEFINITION</c> 只给**函数体**（<c>BEGIN … END</c> 或单条语句），
    /// 不含 <c>CREATE</c> 前缀、参数表与 <c>RETURNS</c>，故这里在本库内用 <c>CONCAT</c> 拼装完整可执行语句：
    /// 参数表取自 <c>information_schema.PARAMETERS</c>（<c>ORDINAL_POSITION &gt; 0</c> 才是形参，
    /// 0 号是函数返回值行），返回类型取自 <c>DTD_IDENTIFIER</c>。同理触发器用
    /// <c>TRIGGERS.ACTION_*</c> 组前缀 + <c>ACTION_STATEMENT</c> 当体。
    /// </para>
    /// <para>
    /// <c>ROUTINE_DEFINITION</c> 在无权限时为 NULL → <c>CASE</c> 把整条定义置 NULL，
    /// 于是调用方（<c>TryReadRoutineDdlAsync</c>）拿不到定义并安静回落，而不是抛错。
    /// </para>
    /// </summary>
    public string BuildObjectsQuery(string database, DbObjectKind kind)
    {
        var schema = Dialect.FormatLiteral(database);

        return kind switch
        {
            DbObjectKind.Trigger =>
                "SELECT TRIGGER_NAME AS name, CONCAT("
                + "'CREATE TRIGGER ', " + QualifiedLiteral("TRIGGER_SCHEMA") + ", '.', " + QualifiedLiteral("TRIGGER_NAME") + ", ' ', "
                + "ACTION_TIMING, ' ', EVENT_MANIPULATION, ' ON ', "
                + QualifiedLiteral("EVENT_OBJECT_SCHEMA") + ", '.', " + QualifiedLiteral("EVENT_OBJECT_TABLE") + ", ' ', "
                + "'FOR EACH ROW ', ACTION_STATEMENT) AS definition "
                + "FROM information_schema.TRIGGERS "
                + $"WHERE TRIGGER_SCHEMA = {schema} ORDER BY TRIGGER_NAME",
            DbObjectKind.Function =>
                RoutineQuery(schema, "FUNCTION", includeReturns: true),
            DbObjectKind.StoredProcedure =>
                RoutineQuery(schema, "PROCEDURE", includeReturns: false),
            DbObjectKind.Event =>
                EventQuery(schema),
            _ => throw new NotSupportedException(UnsupportedMessage(kind)),
        };
    }

    /// <summary>
    /// 例程清单 + 拼装定义。<c>r</c> 是外层 <c>ROUTINES</c> 别名，子查询按它关联 <c>PARAMETERS</c>。
    /// </summary>
    private static string RoutineQuery(string schema, string routineType, bool includeReturns) =>
        "SELECT r.ROUTINE_NAME AS name, CASE WHEN r.ROUTINE_DEFINITION IS NULL THEN NULL ELSE CONCAT("
        + "'CREATE " + routineType + " ', " + QualifiedLiteral("r.ROUTINE_SCHEMA") + ", '.', " + QualifiedLiteral("r.ROUTINE_NAME") + ", '(', "
        + "COALESCE((SELECT GROUP_CONCAT("
        + ParameterExpression(includeMode: routineType == "PROCEDURE")
        + " ORDER BY p.ORDINAL_POSITION SEPARATOR ', ') "
        + "FROM information_schema.PARAMETERS p "
        + "WHERE p.SPECIFIC_SCHEMA = r.ROUTINE_SCHEMA AND p.SPECIFIC_NAME = r.SPECIFIC_NAME "
        + "AND p.ORDINAL_POSITION > 0), ''), ')', "
        + (includeReturns ? "COALESCE(CONCAT(' RETURNS ', r.DTD_IDENTIFIER), ''), " : string.Empty)
        + "CASE WHEN r.IS_DETERMINISTIC = 'YES' THEN ' DETERMINISTIC' ELSE '' END, "
        + "' ', r.ROUTINE_DEFINITION) END AS definition "
        + "FROM information_schema.ROUTINES r "
        + $"WHERE r.ROUTINE_SCHEMA = {schema} AND r.ROUTINE_TYPE = '{routineType}' ORDER BY r.ROUTINE_NAME";

    // 过程参数带 IN/OUT/INOUT 模式，函数参数不带（PARAMETER_MODE 对函数为 NULL）
    private static string ParameterExpression(bool includeMode) =>
        includeMode
            ? "CONCAT(COALESCE(p.PARAMETER_MODE, ''), ' ', COALESCE(p.PARAMETER_NAME, ''), ' ', p.DTD_IDENTIFIER)"
            : "CONCAT(COALESCE(p.PARAMETER_NAME, ''), ' ', p.DTD_IDENTIFIER)";

    /// <summary>
    /// 计划事件清单 + 拼装定义（M5-3）。<c>information_schema.EVENTS</c> 只给 <c>EVENT_DEFINITION</c>
    /// 这一段 <c>DO</c> 后的体，时间表/完成策略/启停状态都在别的列里，故必须逐列拼回完整
    /// <c>CREATE EVENT</c>——少任何一段语义都会变（丢 <c>ON COMPLETION</c> 会让一次性事件执行后被自动删除，
    /// 丢 <c>DISABLE</c> 会把人工停用的事件直接跑起来）。
    /// <para>
    /// 刻意**不写 <c>DEFINER</c>**：靶机实测带与不带都能执行，但不带更稳——带 <c>DEFINER=`someone`@`host`</c>
    /// 时若目标服务器上没有该账号，整条语句直接失败（且非 SUPER 用户本就无权指定他人为 definer）。
    /// 不带则 definer 取执行者，语义上是"以谁的身份重建"而非"原本是谁的"。
    /// </para>
    /// <para>
    /// 一次性事件的时刻取 <c>EXECUTE_AT</c>、重复事件取 <c>EVERY INTERVAL_VALUE INTERVAL_FIELD</c> 加
    /// 可选的 <c>STARTS</c>/<c>ENDS</c>（三者都用 <c>QUOTE()</c> 渲染成字面量，NULL 时才不出现，故先判空）。
    /// <c>STATUS</c> 的 <c>SLAVESIDE_DISABLED</c> 要还原成 <c>DISABLE ON SLAVE</c> 而不是普通 <c>DISABLE</c>。
    /// </para>
    /// </summary>
    private static string EventQuery(string schema) =>
        "SELECT e.EVENT_NAME AS name, CONCAT("
        + "'CREATE EVENT ', " + QualifiedLiteral("e.EVENT_SCHEMA") + ", '.', " + QualifiedLiteral("e.EVENT_NAME") + ", "
        + "' ON SCHEDULE ', "
        + "CASE WHEN e.EVENT_TYPE = 'ONE TIME' "
        + "THEN CONCAT('AT ', QUOTE(e.EXECUTE_AT)) "
        + "ELSE CONCAT('EVERY ', e.INTERVAL_VALUE, ' ', e.INTERVAL_FIELD, "
        + "CASE WHEN e.STARTS IS NULL THEN '' ELSE CONCAT(' STARTS ', QUOTE(e.STARTS)) END, "
        + "CASE WHEN e.ENDS IS NULL THEN '' ELSE CONCAT(' ENDS ', QUOTE(e.ENDS)) END) END, "
        + "' ON COMPLETION ', "
        + "CASE WHEN e.ON_COMPLETION = 'NOT PRESERVE' THEN 'NOT PRESERVE' ELSE 'PRESERVE' END, "
        + "CASE e.STATUS "
        + "WHEN 'SLAVESIDE_DISABLED' THEN ' DISABLE ON SLAVE' "
        + "WHEN 'DISABLED' THEN ' DISABLE' "
        + "ELSE ' ENABLE' END, "
        + "CASE WHEN COALESCE(e.EVENT_COMMENT, '') = '' THEN '' ELSE CONCAT(' COMMENT ', QUOTE(e.EVENT_COMMENT)) END, "
        + "' DO ', e.EVENT_DEFINITION) AS definition "
        + "FROM information_schema.EVENTS e "
        + $"WHERE e.EVENT_SCHEMA = {schema} ORDER BY e.EVENT_NAME";

    /// <summary>把标识符列渲染成 <c>'`值`'</c> 字面量（内部反引号翻倍），供 CONCAT 拼进 DDL。</summary>
    private static string QualifiedLiteral(string column) =>
        $"CONCAT('`', REPLACE({column}, '`', '``'), '`')";

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

    /// <summary>
    /// 表定义走 <c>SHOW CREATE TABLE</c>——MySQL 没有其它能拿到原始建表语句的通道
    /// （<c>information_schema</c> 只有列/索引的拆解元数据，重建必丢 <c>AUTO_INCREMENT</c> 起始值、
    /// 字符集/排序规则、<c>ON UPDATE</c>、生成列表达式等）。
    /// <para>
    /// 返回两列（<c>Table</c> / <c>Create Table</c>），列名随对象类型变，故执行器按列名挑
    /// （见 <c>CatalogQueryExecutor.ReadDefinitionAsync</c>）——本契约的「单列单行」约定为此放宽。
    /// 标识符一律经 <see cref="MySqlDialect.QuoteIdentifier"/> 引用（反引号翻倍），不用字面量拼接。
    /// </para>
    /// </summary>
    public bool SupportsTableDefinitions => true;

    public string BuildTableDefinitionQuery(string database, string table) =>
        $"SHOW CREATE TABLE {QualifiedName(database, table)}";

    private static string QualifiedName(string database, string name) =>
        $"{Dialect.QuoteIdentifier(database)}.{Dialect.QuoteIdentifier(name)}";

    private static string UnsupportedMessage(DbObjectKind kind) =>
        $"MySQL 的目录查询不支持 {kind}（见 AGENTS.md §6.2）";
}
