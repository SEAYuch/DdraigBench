// DdraigBench — 连接串 → ConnectionParameters（编辑已保存连接时回填对话框）

using FreeSql;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System.Data.SQLite;

namespace DdraigBench.Core.Connections;

/// <summary>
/// <see cref="ConnectionStringBuilder.Build"/> 的逆运算：把落盘的连接串拆回结构化参数，
/// 供「编辑已保存连接」把已有连接回填进对话框。
/// </summary>
/// <remarks>
/// 刻意借三个真实驱动（<see cref="MySqlConnectionStringBuilder"/> 等）解析，而不是自己切分
/// <c>;</c>——落盘连接串里可能含被引号包裹的分号、转义的单引号，手写切分会把它们拆错
/// （与 <see cref="ConnectionStringBuilder"/> 的转义规则必须严格互逆）。解析失败一律返回
/// <c>false</c>，调用方据此只回填名称与方言，不让界面崩掉。
/// </remarks>
public static class ConnectionStringParser
{
    public static bool TryParse(
        DataType dataType, string? connectionString, out ConnectionParameters parameters)
    {
        parameters = new ConnectionParameters(dataType);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            switch (dataType)
            {
                case DataType.Sqlite:
                {
                    var parsed = new SQLiteConnectionStringBuilder(connectionString);
                    parameters = new ConnectionParameters(DataType.Sqlite, FilePath: parsed.DataSource);
                    return true;
                }

                case DataType.MySql:
                {
                    var parsed = new MySqlConnectionStringBuilder(connectionString);
                    parameters = new ConnectionParameters(
                        DataType.MySql,
                        Host: parsed.Server,
                        // 驱动在缺省时把 Port 填成 3306；与 Build 的"沉默即默认端口"对齐，收敛回 null。
                        Port: parsed.Port == ConnectionStringBuilder.DefaultPort(DataType.MySql)
                            ? null
                            : NormalizePort((int)parsed.Port),
                        Database: parsed.Database,
                        User: parsed.UserID,
                        Password: parsed.Password);
                    return true;
                }

                case DataType.PostgreSQL:
                {
                    var parsed = new NpgsqlConnectionStringBuilder(connectionString);
                    // 驱动会把未显式给出的 Port 填成 5432；与 Build 的"沉默即默认端口"对齐，
                    // 这里照样把默认值收敛成 null，避免回填出一个用户从未选择过的显式端口。
                    parameters = new ConnectionParameters(
                        DataType.PostgreSQL,
                        Host: parsed.Host,
                        Port: parsed.Port == ConnectionStringBuilder.DefaultPort(DataType.PostgreSQL)
                            ? null
                            : NormalizePort(parsed.Port),
                        Database: parsed.Database,
                        User: parsed.Username,
                        Password: parsed.Password);
                    return true;
                }

                case DataType.SqlServer:
                {
                    var parsed = new SqlConnectionStringBuilder(connectionString);
                    var (host, port) = SplitServer(parsed.DataSource);
                    parameters = new ConnectionParameters(
                        DataType.SqlServer,
                        Host: host,
                        Port: port == ConnectionStringBuilder.DefaultPort(DataType.SqlServer) ? null : port,
                        Database: parsed.InitialCatalog,
                        User: parsed.UserID,
                        Password: parsed.Password,
                        TrustServerCertificate: parsed.TrustServerCertificate);
                    return true;
                }

                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException
            or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static int? NormalizePort(int port) => port <= 0 ? null : port;

    /// <summary>
    /// SQL Server 的 <c>DataSource</c> 把主机与端口写在一起（<c>host,1433</c>），托管实例名则形如
    /// <c>host\SQLEXPRESS</c>。只在"最后一段是最多 5 位的十进制数"时才当端口，避免把实例名拆坏。
    /// </summary>
    internal static (string Host, int? Port) SplitServer(string? dataSource)
    {
        var text = (dataSource ?? string.Empty).Trim();
        var comma = text.LastIndexOf(',');
        if (comma > 0)
        {
            var tail = text[(comma + 1)..].Trim();
            if (tail.Length <= 5 && int.TryParse(tail, out var port) && port is > 0 and <= 65535)
            {
                return (text[..comma].Trim(), port);
            }

            // 逗号后不是端口（含 "h," 这种空尾巴）：逗号只是分隔符噪声，主机名要把它去掉，
            // 否则回填出的主机就是 "h,"，再连接必然解析失败。
            if (tail.Length == 0)
            {
                return (text[..comma].Trim(), null);
            }
        }

        return (text, null);
    }
}
