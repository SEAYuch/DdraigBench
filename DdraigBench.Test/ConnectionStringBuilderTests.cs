// DdraigBench — ConnectionStringBuilder 单测

using DdraigBench.Core.Connections;
using FreeSql;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System.Data.SQLite;

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

    [Theory]
    [InlineData("p;w")]
    [InlineData("p'w")]
    [InlineData("\"quoted\"")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("p;w'd\"x")]
    [InlineData("")]
    public void MySql_password_special_characters_survive_a_real_driver_parse(string password)
    {
        var parameters = new ConnectionParameters(
            DataType.MySql, Host: "h", Database: "d", User: "u", Password: password);

        var parsed = new MySqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal(password, parsed.Password);
        Assert.Equal("d", parsed.Database);
        Assert.Equal("u", parsed.UserID);
    }

    [Theory]
    [InlineData("p;w")]
    [InlineData("p'w")]
    [InlineData("p;w'd\"x")]
    public void PostgreSql_password_special_characters_survive_a_real_driver_parse(string password)
    {
        var parameters = new ConnectionParameters(
            DataType.PostgreSQL, Host: "h", Database: "d", User: "u", Password: password);

        var parsed = new NpgsqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal(password, parsed.Password);
        Assert.Equal("d", parsed.Database);
        Assert.Equal("u", parsed.Username);
    }

    [Fact]
    public void Sqlite_path_with_semicolon_is_quoted()
    {
        var parameters = new ConnectionParameters(DataType.Sqlite, FilePath: @"C:\db\a;b.sqlite3");

        var parsed = new SQLiteConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal(@"C:\db\a;b.sqlite3", parsed.DataSource);
    }

    [Fact]
    public void Semicolon_in_a_value_cannot_smuggle_extra_keywords()
    {
        // M6：不转义时 `Database=d;Uid=evil` 会把用户名改成 evil（后半截被当成新键值对）
        var parameters = new ConnectionParameters(
            DataType.MySql, Host: "h", Database: "d;Uid=evil", User: "u", Password: "w");

        var parsed = new MySqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal("d;Uid=evil", parsed.Database);
        Assert.Equal("u", parsed.UserID);
    }

    [Fact]
    public void Server_provider_suggests_user_at_host_database()
    {
        var parameters = new ConnectionParameters(DataType.MySql, Host: "h", Database: "d", User: "u");

        Assert.Equal("u@h/d", ConnectionStringBuilder.SuggestName(parameters));
    }

    [Fact]
    public void SqlServer_uses_default_port_and_secure_tls_defaults()
    {
        var parameters = new ConnectionParameters(
            DataType.SqlServer, Host: "db.example", Database: "app", User: "sa", Password: "secret");

        var parsed = new SqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal("db.example,1433", parsed.DataSource);
        Assert.Equal("app", parsed.InitialCatalog);
        Assert.Equal("sa", parsed.UserID);
        Assert.Equal("secret", parsed.Password);
        Assert.True(parsed.Encrypt);
        Assert.False(parsed.TrustServerCertificate);
        Assert.False(parsed.Pooling);
        Assert.Equal("sa@db.example:1433/app", ConnectionStringBuilder.SuggestName(parameters));
    }

    [Fact]
    public void SqlServer_explicit_port_and_trust_server_certificate_are_applied()
    {
        var parameters = new ConnectionParameters(
            DataType.SqlServer, Host: "db.example", Port: 14330, Database: "app",
            User: "sa", Password: "secret", TrustServerCertificate: true);

        var parsed = new SqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal("db.example,14330", parsed.DataSource);
        Assert.True(parsed.TrustServerCertificate);
    }

    [Theory]
    [InlineData("p;w")]
    [InlineData("p'w\"x")]
    [InlineData(" leading ")]
    public void SqlServer_password_special_characters_survive_a_real_driver_parse(string password)
    {
        var parameters = new ConnectionParameters(
            DataType.SqlServer, Host: "db.example", Database: "app", User: "sa", Password: password);

        var parsed = new SqlConnectionStringBuilder(ConnectionStringBuilder.Build(parameters));

        Assert.Equal(password, parsed.Password);
        Assert.Equal("app", parsed.InitialCatalog);
        Assert.Equal("sa", parsed.UserID);
    }
}
