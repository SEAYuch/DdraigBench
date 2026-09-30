// DdraigBench — 方言工厂

using FreeSql;

namespace DdraigBench.Core.Dialects;

public static class SqlDialects
{
    public static ISqlDialect Get(DataType dataType) => dataType switch
    {
        DataType.Sqlite => SqliteDialect.Instance,
        DataType.MySql => MySqlDialect.Instance,
        DataType.PostgreSQL => PostgreSqlDialect.Instance,
        _ => throw new NotSupportedException($"暂无该方言的实现：{dataType}"),
    };

    public static bool TryGet(DataType dataType, out ISqlDialect dialect)
    {
        try
        {
            dialect = Get(dataType);
            return true;
        }
        catch (NotSupportedException)
        {
            dialect = null!;
            return false;
        }
    }
}
