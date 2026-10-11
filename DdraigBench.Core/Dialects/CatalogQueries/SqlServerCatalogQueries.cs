// DdraigBench — SQL Server 目录查询能力

using DdraigBench.Core.Metadata;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>
/// SQL Server 的表/视图/列由 FreeSql IDbFirst 提供；定义对象目录暂未实现。
/// 显式提供空能力矩阵，确保连接树展开时不把「尚未实现目录 SQL」当成连接失败。
/// </summary>
public sealed class SqlServerCatalogQueries : ICatalogQueries
{
    public static readonly SqlServerCatalogQueries Instance = new();

    public bool Supports(DbObjectKind kind) => false;

    public string BuildObjectsQuery(string database, DbObjectKind kind) =>
        throw new NotSupportedException($"SQL Server 目录查询尚未实现：{kind}");

    public bool ConnectionIsBoundToSingleDatabase => true;

    public string BuildCurrentDatabaseQuery() => "SELECT DB_NAME() AS name";

    public bool SupportsViewDefinitions => false;

    public string BuildViewDefinitionQuery(string database, string view) =>
        throw new NotSupportedException("SQL Server 视图定义查询尚未实现");

    public string ComposeViewDdl(string database, string view, string definition) =>
        throw new NotSupportedException("SQL Server 视图定义查询尚未实现");

    public bool SupportsTableDefinitions => false;

    public string BuildTableDefinitionQuery(string database, string table) =>
        throw new NotSupportedException("SQL Server 表定义查询尚未实现");
}
