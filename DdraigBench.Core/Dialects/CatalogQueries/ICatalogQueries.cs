// DdraigBench — IDbFirst 缺口的目录查询契约（触发器 / 函数 / 存储过程 / 视图与表定义）

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// FreeSql 的 <c>IDbFirst</c> 只覆盖表 / 视图 / 列 / 主键 / 索引 / 外键，触发器、函数、存储过程、
/// 以及视图的原始定义都取不到（见 AGENTS.md §6.2）。本契约按方言给出补齐用的目录 SQL。
/// </summary>
/// <remarks>
/// 实现只负责「造 SQL」，不执行——执行与结果映射交给 <c>CatalogQueryExecutor</c>。
/// 这样即便在没有靶机的环境里，SQL 形状、能力矩阵与引用/转义也都可做纯逻辑单测。
/// <para>
/// 结果集的列名契约（映射层按名取列，缺列即视为 <c>null</c>，故各方言只 SELECT 自己给得出的列）：
/// <list type="bullet">
///   <item>对象清单：<c>name</c>（必填）；<c>schema</c>、<c>comment</c>、<c>definition</c>（可选，definition 必须是完整可执行语句）。</item>
///   <item>定义查询：首行即目标对象的定义，**完整可执行语句**。执行器按列名取 DDL
///   （见 <c>CatalogQueryExecutor.ReadDefinitionAsync</c>）：优先取名为 <c>definition</c> 的列，
///   否则取唯一一个以 <c>Create </c> 开头的列。之所以不是「单列单行」，是因为 MySQL 的
///   <c>SHOW CREATE TABLE</c> 无法用 <c>SELECT</c> 包起来、且列名随对象类型变
///   （表 = <c>Create Table</c>、视图 = <c>Create View</c>），只能连名字列一起返回。</item>
/// </list>
/// </para>
/// <para>
/// <c>database</c> 参数的语义与 <c>IDbFirst.GetTablesByDatabase</c> 保持一致，即「对象所在的容器」：
/// SQLite = 附加库名（<c>main</c> / <c>temp</c>）、MySQL = schema（等价于库）、
/// PostgreSQL = schema（FreeSql 的 PG 实现按 <c>pg_namespace.nspname</c> 过滤，尽管它的
/// <c>GetDatabases()</c> 返回 <c>pg_database.datname</c>——见 AGENTS.md §4）；
/// SQL Server = 当前数据库名作为库清单项，模式层与库名分开查询。
/// </para>
/// </remarks>
public interface ICatalogQueries
{
    /// <summary>该方言能否列出这种库级对象。false 表示「这类对象在该库里不存在」，调用方应视为空集而非报错。</summary>
    bool Supports(DbObjectKind kind);

    /// <summary>对象清单 SQL；<paramref name="kind"/> 不受支持时抛 <see cref="NotSupportedException"/>。</summary>
    string BuildObjectsQuery(string database, DbObjectKind kind);

    /// <summary>
    /// 连接是否被钉死在一个库上（PostgreSQL / SQL Server：连接的对象目录限于所连库，
    /// 跨库需新连接）。为真时 <c>GetDatabasesAsync</c> 只应返回当前库，
    /// 否则会把"看得见却打不开"的库列进树里（M5-2 修的就是这个）。
    /// </summary>
    bool ConnectionIsBoundToSingleDatabase { get; }

    /// <summary>取当前库名的 SQL（单列单行）；仅当 <see cref="ConnectionIsBoundToSingleDatabase"/> 为真时需要。</summary>
    string BuildCurrentDatabaseQuery();

    /// <summary>能否取到视图的原始定义。</summary>
    bool SupportsViewDefinitions { get; }

    /// <summary>视图定义 SQL（单列单行，值为「定义体」，是否含 CREATE 前缀由方言决定，见 <see cref="ComposeViewDdl"/>）。</summary>
    string BuildViewDefinitionQuery(string database, string view);

    /// <summary>
    /// 把定义体拼成可直接执行的 <c>CREATE VIEW</c> 语句。
    /// SQLite 的 <c>sqlite_master.sql</c> 与 SQL Server 的 <c>sys.sql_modules.definition</c> 本身即完整语句，实现应原样返回；
    /// MySQL / PostgreSQL 只给得出 SELECT 主体，需在此补上 CREATE 前缀与限定名。
    /// </summary>
    string ComposeViewDdl(string database, string view, string definition);

    /// <summary>能否取到表的原始建表语句（保真度高于按元数据重建：AUTOINCREMENT / UNIQUE / CHECK / COLLATE 都不丢）。</summary>
    bool SupportsTableDefinitions { get; }

    /// <summary>表定义 SQL（单列单行，值为完整建表语句）。</summary>
    string BuildTableDefinitionQuery(string database, string table);
}
