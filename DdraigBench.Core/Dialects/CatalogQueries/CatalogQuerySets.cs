// DdraigBench — 目录查询工厂（DataType → ICatalogQueries）

using FreeSql;

namespace DdraigBench.Core.Dialects.CatalogQueries;

/// <summary>按 <see cref="DataType"/> 取目录查询实现；与 <c>SqlDialects</c> 同构，便于调用方统一处理「未接入」。</summary>
public static class CatalogQuerySets
{
    public static ICatalogQueries Get(DataType dataType) => dataType switch
    {
        DataType.Sqlite => SqliteCatalogQueries.Instance,
        DataType.MySql => MySqlCatalogQueries.Instance,
        DataType.PostgreSQL => PostgreSqlCatalogQueries.Instance,
        DataType.SqlServer => SqlServerCatalogQueries.Instance,
        _ => throw new NotSupportedException($"尚未接入该方言的目录查询：{dataType}"),
    };

    public static bool TryGet(DataType dataType, out ICatalogQueries catalog)
    {
        try
        {
            catalog = Get(dataType);
            return true;
        }
        catch (NotSupportedException)
        {
            catalog = null!;
            return false;
        }
    }
}
