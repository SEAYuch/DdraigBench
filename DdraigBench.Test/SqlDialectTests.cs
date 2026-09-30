// DdraigBench — ISqlDialect 单测

using DdraigBench.Core.Dialects;
using FreeSql;

namespace DdraigBench.Test;

public sealed class SqlDialectTests
{
    [Theory]
    [InlineData(DataType.Sqlite, "\"t\"")]
    [InlineData(DataType.MySql, "`t`")]
    [InlineData(DataType.PostgreSQL, "\"t\"")]
    public void Quote_identifier_per_dialect(DataType dataType, string expected)
    {
        Assert.Equal(expected, SqlDialects.Get(dataType).QuoteIdentifier("t"));
    }

    [Fact]
    public void Quote_identifier_escapes_embedded_quote()
    {
        Assert.Equal("\"a\"\"b\"", SqlDialects.Get(DataType.Sqlite).QuoteIdentifier("a\"b"));
        Assert.Equal("`a``b`", SqlDialects.Get(DataType.MySql).QuoteIdentifier("a`b"));
        Assert.Equal("\"a\"\"b\"", SqlDialects.Get(DataType.PostgreSQL).QuoteIdentifier("a\"b"));
    }

    [Fact]
    public void Paging_uses_limit_offset()
    {
        foreach (var dataType in new[] { DataType.Sqlite, DataType.MySql, DataType.PostgreSQL })
        {
            Assert.Equal("LIMIT 50 OFFSET 100", SqlDialects.Get(dataType).BuildPaging(50, 100));
        }
    }

    [Fact]
    public void Literals_cover_null_string_number_bool_and_datetime()
    {
        var dialect = SqlDialects.Get(DataType.Sqlite);

        Assert.Equal("NULL", dialect.FormatLiteral(null));
        Assert.Equal("'it''s'", dialect.FormatLiteral("it's"));
        Assert.Equal("42", dialect.FormatLiteral(42));
        Assert.Equal("1.5", dialect.FormatLiteral(1.5));
        Assert.Equal("TRUE", dialect.FormatLiteral(true));
        Assert.Equal("FALSE", dialect.FormatLiteral(false));
        Assert.Equal("'2026-09-21 13:05:00.000'", dialect.FormatLiteral(new DateTime(2026, 9, 21, 13, 5, 0)));
    }

    [Fact]
    public void Binary_literal_differs_for_postgres()
    {
        var bytes = new byte[] { 0x68, 0x69 };

        Assert.Equal("X'6869'", SqlDialects.Get(DataType.Sqlite).FormatLiteral(bytes));
        Assert.Equal("X'6869'", SqlDialects.Get(DataType.MySql).FormatLiteral(bytes));
        Assert.Equal("'\\x6869'::bytea", SqlDialects.Get(DataType.PostgreSQL).FormatLiteral(bytes));
    }

    [Fact]
    public void Unknown_dialect_throws_and_tryget_returns_false()
    {
        Assert.Throws<NotSupportedException>(() => SqlDialects.Get(DataType.SqlServer));
        Assert.False(SqlDialects.TryGet(DataType.SqlServer, out _));
    }
}
