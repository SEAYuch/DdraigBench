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

        // 连续分号（空语句）同样只当结尾符号处理
        Assert.Equal("SELECT * FROM (SELECT id FROM t) AS \"ddb_page\" LIMIT 10 OFFSET 0",
            PagedSql.Build("SELECT id FROM t;;", SqliteDialect.Instance, 10, 0));
    }

    [Fact]
    public void Semicolon_inside_a_literal_is_not_a_statement_end()
    {
        var sql = PagedSql.Build("SELECT ';' AS s FROM t", SqliteDialect.Instance, 10, 0);

        Assert.Equal("SELECT * FROM (SELECT ';' AS s FROM t) AS \"ddb_page\" LIMIT 10 OFFSET 0", sql);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1)")]
    [InlineData("SELECT 1; -- 后面还有一句\nSELECT 2")]
    public void Multi_statement_scripts_are_left_alone(string sql)
    {
        // M2：多语句脚本包进派生表必然是语法错误（`near ";": syntax error`），原样执行
        Assert.Null(PagedSql.Build(sql, SqliteDialect.Instance, 100, 0));
    }

    [Fact]
    public void Trailing_line_comment_does_not_swallow_the_closing_parenthesis()
    {
        // M1：`-- …` 吃到行尾，注释后面必须补换行，否则右括号会被吞掉
        var sql = PagedSql.Build("SELECT id FROM t -- 只看这一页", SqliteDialect.Instance, 10, 0);

        Assert.Equal(
            "SELECT * FROM (SELECT id FROM t -- 只看这一页\n) AS \"ddb_page\" LIMIT 10 OFFSET 0",
            sql);
    }

    [Fact]
    public void Leading_comment_does_not_hide_the_query()
    {
        var sql = PagedSql.Build("-- 本月订单\nSELECT id FROM t", SqliteDialect.Instance, 10, 0);

        Assert.NotNull(sql);
        Assert.StartsWith("SELECT * FROM (-- 本月订单\nSELECT id FROM t)", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT id FROM t -- LIMIT 3")]
    [InlineData("SELECT id FROM t /* LIMIT 3 */")]
    public void Keywords_inside_comments_are_not_mistaken_for_paging(string sql)
    {
        // 注释里的 LIMIT 不算"已分页"：否则会被判定为"用户自带分页"而整块关掉服务端分页
        var paged = PagedSql.Build(sql, SqliteDialect.Instance, 10, 0);

        Assert.NotNull(paged);
        Assert.EndsWith("LIMIT 10 OFFSET 0", paged, StringComparison.Ordinal);
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
    public void SqlServer_paging_appends_offset_fetch_only_to_top_level_ordered_queries()
    {
        var sql = PagedSql.Build("SELECT id FROM dbo.t ORDER BY id", SqlServerDialect.Instance, 25, 50);

        Assert.Equal("SELECT id FROM dbo.t ORDER BY id OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY", sql);
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t", SqlServerDialect.Instance, 25, 50));
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t ORDER BY id LIMIT 10", SqlServerDialect.Instance, 25, 50));
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t ORDER BY id; SELECT 2", SqlServerDialect.Instance, 25, 50));
    }

    [Fact]
    public void SqlServer_order_by_in_literals_nested_queries_and_bracket_identifiers_does_not_enable_paging()
    {
        Assert.Null(PagedSql.Build("SELECT '[ORDER BY]' AS note FROM dbo.t", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT * FROM (SELECT id FROM dbo.t ORDER BY id) q", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT [ORDER BY] FROM dbo.t", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT [a]]ORDER BY]] FROM dbo.t", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT TOP (10) id FROM dbo.t ORDER BY id", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t ORDER BY id OPTION (RECOMPILE)", SqlServerDialect.Instance, 10, 0));
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t ORDER BY id FOR XML PATH('x')", SqlServerDialect.Instance, 10, 0));
    }

    [Fact]
    public void Throws_when_dialect_is_missing()
    {
        Assert.Throws<ArgumentNullException>(() => PagedSql.Build("SELECT 1", null!, 10, 0));
    }
}