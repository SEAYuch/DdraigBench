// DdraigBench — 服务端分页语句构造单测（纯逻辑，不连数据库）

using DdraigBench.Core.Dialects;
using DdraigBench.Core.Querying;

namespace DdraigBench.Test;

public sealed class PagedSqlTests
{
    [Fact]
    public void Wraps_select_with_limit_offset()
    {
        var sql = PagedSql.Build("SELECT id FROM t ORDER BY id", SqliteDialect.Instance, 50, 100);

        Assert.Equal(
            "SELECT * FROM (SELECT id FROM t ORDER BY id) AS \"ddb_page\" LIMIT 50 OFFSET 100",
            sql);
    }

    [Fact]
    public void Wraps_common_table_expressions()
    {
        var sql = PagedSql.Build("WITH x AS (SELECT 1 AS n) SELECT * FROM x", PostgreSqlDialect.Instance, 10, 0);

        Assert.NotNull(sql);
        Assert.Contains("(WITH x AS (SELECT 1 AS n) SELECT * FROM x)", sql, StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 10 OFFSET 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_dialect_identifier_quoting()
    {
        var sql = PagedSql.Build("SELECT 1", PostgreSqlDialect.Instance, 5, 5);

        Assert.Contains("AS \"ddb_page\"", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INSERT INTO t (id) VALUES (1)")]
    [InlineData("UPDATE t SET id = 2")]
    [InlineData("DELETE FROM t")]
    [InlineData("CREATE TABLE t (id INTEGER)")]
    [InlineData("DROP TABLE t")]
    [InlineData("PRAGMA table_info(t)")]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData(null)]
    public void Non_query_statements_are_left_alone(string? sql)
    {
        Assert.Null(PagedSql.Build(sql, SqliteDialect.Instance, 100, 0));
    }

    [Fact]
    public void Tolerates_trailing_semicolon_and_whitespace()
    {
        var sql = PagedSql.Build("  SELECT id FROM t;  ", SqliteDialect.Instance, 10, 0);

        Assert.Equal("SELECT * FROM (SELECT id FROM t) AS \"ddb_page\" LIMIT 10 OFFSET 0", sql);
    }

    [Theory]
    [InlineData("SELECT id FROM t LIMIT 10")]
    [InlineData("SELECT id FROM t limit 10 offset 5")]
    [InlineData("SELECT id FROM t ORDER BY id LIMIT 10, 20")]
    public void Statement_with_top_level_paging_is_not_rewrapped(string sql)
    {
        Assert.Null(PagedSql.Build(sql, SqliteDialect.Instance, 100, 100));
    }

    [Fact]
    public void Paging_inside_subquery_does_not_disable_our_paging()
    {
        var sql = PagedSql.Build(
            "SELECT * FROM (SELECT id FROM t LIMIT 5) AS inner_q ORDER BY id",
            SqliteDialect.Instance,
            10,
            20);

        Assert.NotNull(sql);
        Assert.EndsWith("LIMIT 10 OFFSET 20", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT 'limit' AS note FROM t")]
    [InlineData("SELECT \"limit\" FROM t")]
    [InlineData("SELECT unlimited_col FROM t")]
    [InlineData("SELECT offset_col FROM t")]
    public void Keywords_inside_literals_or_identifiers_are_ignored(string sql)
    {
        Assert.NotNull(PagedSql.Build(sql, SqliteDialect.Instance, 10, 0));
    }

    [Fact]
    public void Escaped_quote_inside_literal_does_not_confuse_the_scanner()
    {
        // 'it''s a limit' —— 转义单引号后的 limit 不应被当成顶层分页
        Assert.NotNull(PagedSql.Build("SELECT 'it''s a limit' AS s FROM t", SqliteDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT 'it''s a limit' FROM t LIMIT 3", SqliteDialect.Instance, 10, 0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    public void Invalid_page_parameters_disable_paging(int pageSize, long offset)
    {
        Assert.Null(PagedSql.Build("SELECT 1", SqliteDialect.Instance, pageSize, offset));
    }

    [Fact]
    public void Throws_when_dialect_is_missing()
    {
        Assert.Throws<ArgumentNullException>(() => PagedSql.Build("SELECT 1", null!, 10, 0));
    }
}