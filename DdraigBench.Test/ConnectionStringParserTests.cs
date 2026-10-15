// DdraigBench — ConnectionStringParser 单测（编辑已保存连接的回填通道）

using DdraigBench.Core.Connections;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ConnectionStringParserTests
{
    [Fact]
    public void Sqlite_round_trips_data_source()
    {
        var parameters = new ConnectionParameters(DataType.Sqlite, FilePath: @"C:\db\a;b.sqlite3");
        var connectionString = ConnectionStringBuilder.Build(parameters);

        Assert.True(ConnectionStringParser.TryParse(DataType.Sqlite, connectionString, out var parsed));
        Assert.Equal(DataType.Sqlite, parsed.DataType);
        Assert.Equal(@"C:\db\a;b.sqlite3", parsed.FilePath);
    }

    [Fact]
    public void MySql_round_trips_host_port_database_user_password()
    {
        var parameters = new ConnectionParameters(
            DataType.MySql, Host: "h", Port: 13306, Database: "d", User: "u", Password: "p;w'd");
        var connectionString = ConnectionStringBuilder.Build(parameters);

        Assert.True(ConnectionStringParser.TryParse(DataType.MySql, connectionString, out var parsed));
        Assert.Equal("h", parsed.Host);
        Assert.Equal(13306, parsed.Port);
        Assert.Equal("d", parsed.Database);
        Assert.Equal("u", parsed.User);
        Assert.Equal("p;w'd", parsed.Password);
    }

    [Fact]
    public void MySql_default_port_is_normalized_to_null()
    {
        // Build 在 Port=null 时写默认端口；解析回来必须回到 null，
        // 否则"回填→确认"会把一个用户从未选择的显式端口写进连接串。
        var built = ConnectionStringBuilder.Build(new ConnectionParameters(
            DataType.MySql, Host: "h", Database: "d", User: "u", Password: "w"));

        Assert.True(ConnectionStringParser.TryParse(DataType.MySql, built, out var parsed));
        Assert.Null(parsed.Port);
        Assert.Equal(built, ConnectionStringBuilder.Build(parsed));
    }

    [Fact]
    public void PostgreSql_round_trips_and_normalizes_the_default_port()
    {
        var explicitPort = new ConnectionParameters(
            DataType.PostgreSQL, Host: "h", Port: 5433, Database: "d", User: "u", Password: "w");
        Assert.True(ConnectionStringParser.TryParse(
            DataType.PostgreSQL, ConnectionStringBuilder.Build(explicitPort), out var parsed));
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("u", parsed.User);

        var defaultPort = new ConnectionParameters(
            DataType.PostgreSQL, Host: "h", Database: "d", User: "u", Password: "w");
        var connectionString = ConnectionStringBuilder.Build(defaultPort);
        Assert.True(ConnectionStringParser.TryParse(DataType.PostgreSQL, connectionString, out var defaults));
        Assert.Null(defaults.Port);
        Assert.Equal(connectionString, ConnectionStringBuilder.Build(defaults));
    }

    [Fact]
    public void SqlServer_round_trips_and_normalizes_the_default_port()
    {
        var explicitPort = new ConnectionParameters(
            DataType.SqlServer, Host: "db.example", Port: 14330, Database: "app",
            User: "sa", Password: "p;w", TrustServerCertificate: true);

        Assert.True(ConnectionStringParser.TryParse(
            DataType.SqlServer, ConnectionStringBuilder.Build(explicitPort), out var parsed));
        Assert.Equal("db.example", parsed.Host);
        Assert.Equal(14330, parsed.Port);
        Assert.Equal("app", parsed.Database);
        Assert.Equal("sa", parsed.User);
        Assert.Equal("p;w", parsed.Password);
        Assert.True(parsed.TrustServerCertificate);
        Assert.Equal(ConnectionStringBuilder.Build(explicitPort), ConnectionStringBuilder.Build(parsed));

        var defaultPort = new ConnectionParameters(
            DataType.SqlServer, Host: "db.example", Database: "app", User: "sa", Password: "w");
        var connectionString = ConnectionStringBuilder.Build(defaultPort);
        Assert.True(ConnectionStringParser.TryParse(DataType.SqlServer, connectionString, out var defaults));
        Assert.Null(defaults.Port);
        Assert.False(defaults.TrustServerCertificate);
        Assert.Equal(connectionString, ConnectionStringBuilder.Build(defaults));
    }

    [Fact]
    public void SqlServer_keeps_a_named_instance_in_the_host()
    {
        var connectionString =
            @"Server=db.example\SQLEXPRESS;Database=app;User Id=sa;Password=w;Encrypt=True;" +
            "TrustServerCertificate=False;Pooling=False";

        Assert.True(ConnectionStringParser.TryParse(DataType.SqlServer, connectionString, out var parsed));
        Assert.Equal(@"db.example\SQLEXPRESS", parsed.Host);
        Assert.Null(parsed.Port);
    }

    [Fact]
    public void Empty_or_unparsable_input_reports_failure()
    {
        Assert.False(ConnectionStringParser.TryParse(DataType.MySql, null, out _));
        Assert.False(ConnectionStringParser.TryParse(DataType.MySql, "   ", out _));
        Assert.False(ConnectionStringParser.TryParse(DataType.MySql, "这不是连接串", out _));
        Assert.False(ConnectionStringParser.TryParse(DataType.SqlServer, "这不是连接串", out _));
    }

    [Fact]
    public void SplitServer_reads_the_port_only_when_the_tail_is_numeric()
    {
        Assert.Equal(("h", (int?)1433), ConnectionStringParser.SplitServer("h,1433"));
        Assert.Equal(("h", (int?)null), ConnectionStringParser.SplitServer("h"));
        Assert.Equal((@"h\SQLEXPRESS", (int?)null), ConnectionStringParser.SplitServer(@"h\SQLEXPRESS"));
        Assert.Equal(("h", (int?)null), ConnectionStringParser.SplitServer("h,"));
        Assert.Equal(("h,notaport", (int?)null), ConnectionStringParser.SplitServer("h,notaport"));
    }
}
