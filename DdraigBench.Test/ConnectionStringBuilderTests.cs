// DdraigBench — ConnectionStringBuilder 单测

using DdraigBench.Core.Connections;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ConnectionStringBuilderTests
{
    [Fact]
    public void Sqlite_uses_data_source_and_suggests_file_name()
    {
        var parameters = new ConnectionParameters(DataType.Sqlite, FilePath: @"C:\db\demo.sqlite3");

        Assert.Equal(
            @"Data Source=C:\db\demo.sqlite3;Pooling=False", ConnectionStringBuilder.Build(parameters));
        Assert.Equal("demo.sqlite3", ConnectionStringBuilder.SuggestName(parameters));
    }

    [Fact]
    public void MySql_defaults_port_3306()
    {
        var parameters = new ConnectionParameters(
            DataType.MySql, Host: "h", Database: "d", User: "u", Password: "w");

        Assert.Equal(
            "Server=h;Port=3306;Database=d;Uid=u;Pwd=w;Pooling=false",
            ConnectionStringBuilder.Build(parameters));
    }

    [Fact]
    public void PostgreSql_defaults_port_5432_and_uses_Username_Password_keys()
    {
        var parameters = new ConnectionParameters(
            DataType.PostgreSQL, Host: "h", Database: "d", User: "u", Password: "w");

        Assert.Equal(
            "Host=h;Port=5432;Database=d;Username=u;Password=w;Pooling=false",
            ConnectionStringBuilder.Build(parameters));
    }

    [Fact]
    public void Explicit_port_wins_over_default()
    {
        var parameters = new ConnectionParameters(
            DataType.MySql, Host: "h", Port: 13306, Database: "d", User: "u", Password: "w");

        Assert.Contains("Port=13306", ConnectionStringBuilder.Build(parameters), StringComparison.Ordinal);
    }

    [Fact]
    public void Server_provider_suggests_user_at_host_database()
    {
        var parameters = new ConnectionParameters(DataType.MySql, Host: "h", Database: "d", User: "u");

        Assert.Equal("u@h/d", ConnectionStringBuilder.SuggestName(parameters));
    }

    [Fact]
    public void Unsupported_provider_throws()
    {
        var parameters = new ConnectionParameters(DataType.SqlServer, Host: "h");

        Assert.Throws<NotSupportedException>(() => ConnectionStringBuilder.Build(parameters));
    }
}
