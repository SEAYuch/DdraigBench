// DdraigBench — ConnectionParameters → 各方言连接串

using FreeSql;

namespace DdraigBench.Core.Connections;

public static class ConnectionStringBuilder
{
    public static int DefaultPort(DataType dataType) => dataType switch
    {
        DataType.MySql => 3306,
        DataType.PostgreSQL => 5432,
        _ => 0,
    };

    public static string Build(ConnectionParameters p) => p.DataType switch
    {
        DataType.Sqlite =>
            $"Data Source={p.FilePath};Pooling=False",
        DataType.MySql =>
            $"Server={p.Host};Port={p.Port ?? DefaultPort(DataType.MySql)};Database={p.Database};" +
            $"Uid={p.User};Pwd={p.Password};Pooling=false",
        DataType.PostgreSQL =>
            $"Host={p.Host};Port={p.Port ?? DefaultPort(DataType.PostgreSQL)};Database={p.Database};" +
            $"Username={p.User};Password={p.Password};Pooling=false",
        _ => throw new NotSupportedException($"暂不支持的 Provider：{p.DataType}"),
    };

    public static string SuggestName(ConnectionParameters p) => p.DataType switch
    {
        DataType.Sqlite => Path.GetFileName(p.FilePath ?? string.Empty),
        _ => $"{p.User}@{p.Host}/{p.Database}",
    };
}
