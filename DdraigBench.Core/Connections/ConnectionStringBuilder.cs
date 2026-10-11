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
            $"Data Source={Escape(p.FilePath)};Pooling=False",
        DataType.MySql =>
            $"Server={Escape(p.Host)};Port={p.Port ?? DefaultPort(DataType.MySql)};Database={Escape(p.Database)};" +
            $"Uid={Escape(p.User)};Pwd={Escape(p.Password)};Pooling=false",
        DataType.PostgreSQL =>
            $"Host={Escape(p.Host)};Port={p.Port ?? DefaultPort(DataType.PostgreSQL)};Database={Escape(p.Database)};" +
            $"Username={Escape(p.User)};Password={Escape(p.Password)};Pooling=false",
        _ => throw new NotSupportedException($"暂不支持的 Provider：{p.DataType}"),
    };

    /// <summary>
    /// 按 ADO.NET 连接串约定转义取值：含 <c>;</c>、引号或首尾空白时用单引号包裹，内部单引号翻倍。
    /// </summary>
    /// <remarks>
    /// 不转义会让取值里的 <c>;</c> 被解析成"下一个键值对"——口令含分号时连接串被静默改写
    /// （轻则连不上，重则把后半截当成别的关键字，被驱动拒绝或连到非预期目标）。
    /// 三个驱动（System.Data.SQLite / MySqlConnector / Npgsql）都遵循这套引号与翻倍转义约定。
    /// </remarks>
    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var needsQuotes = text.IndexOf(';') >= 0
            || text.IndexOf('\'') >= 0
            || text.IndexOf('"') >= 0
            || char.IsWhiteSpace(text[0])
            || char.IsWhiteSpace(text[^1]);

        return needsQuotes ? $"'{text.Replace("'", "''", StringComparison.Ordinal)}'" : text;
    }

    public static string SuggestName(ConnectionParameters p) => p.DataType switch
    {
        // 用户可能粘贴任一平台的路径（Windows 反斜杠 / Unix 斜杠），故两种分隔符都要切；
        // 仅用 Path.GetFileName 会因平台差异漏切另一种分隔符。
        DataType.Sqlite => FileNameOf(p.FilePath ?? string.Empty),
        _ => $"{p.User}@{p.Host}/{p.Database}",
    };

    private static string FileNameOf(string path) =>
        Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar));
}
