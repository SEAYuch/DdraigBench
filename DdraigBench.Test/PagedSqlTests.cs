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
    public void Statement_with_top_level_paging_is_still_wrapped_by_wrapper_dialects(string sql)
    {
        // 包装方言把用户自己的 LIMIT/OFFSET 关进派生表，外层照样能叠加我们的 LIMIT/OFFSET：
        // 于是 "SELECT … LIMIT 100000" 这类查询也能服务端分页 + 跨页排序（2026-10-16 起）。
        Assert.Equal(
            $"SELECT * FROM ({sql}) AS \"ddb_page\" LIMIT 100 OFFSET 100",
            PagedSql.Build(sql, SqliteDialect.Instance, 100, 100));
    }

    [Fact]
    public void Wrapper_dialects_all_accept_a_user_supplied_limit()
    {
        Assert.EndsWith(
            "LIMIT 100 OFFSET 100",
            PagedSql.Build("SELECT id FROM t LIMIT 10", MySqlDialect.Instance, 100, 100),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "LIMIT 100 OFFSET 100",
            PagedSql.Build("SELECT id FROM t LIMIT 10", PostgreSqlDialect.Instance, 100, 100),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_still_refuses_paging_when_the_statement_pages_by_itself()
    {
        // T-SQL 不能包派生表：用户自带 OFFSET/FETCH 时无处安放我们的后缀 → 整条 fail-closed。
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t LIMIT 10", SqlServerDialect.Instance, 25, 0));
        Assert.Null(PagedSql.Build(
            "SELECT id FROM dbo.t ORDER BY id OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY",
            SqlServerDialect.Instance,
            25,
            0));
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

        // 字面量里的 limit 不是分页，后面那个真 LIMIT 3 才是：字面量必须原样留在派生表里。
        Assert.Equal(
            "SELECT * FROM (SELECT 'it''s a limit' FROM t LIMIT 3) AS \"ddb_page\" LIMIT 10 OFFSET 0",
            PagedSql.Build("SELECT 'it''s a limit' FROM t LIMIT 3", SqliteDialect.Instance, 10, 0));
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

    [Fact]
    public void Sort_is_pushed_into_the_outer_wrapper_for_dialects_that_accept_ordinals()
    {
        var sort = new ResultSort(ColumnIndex: 1, ColumnName: "amount", Descending: true);
        var sql = PagedSql.Build("SELECT name, amount FROM t", SqliteDialect.Instance, 10, 20, sort, out var applied);

        // 序号而非列名：重名列/表达式列按名排序会歧义
        Assert.True(applied);
        Assert.Equal(
            "SELECT * FROM (SELECT name, amount FROM t) AS \"ddb_page\" ORDER BY 2 DESC LIMIT 10 OFFSET 20",
            sql);
    }

    [Fact]
    public void Sort_inside_a_wrapper_keeps_user_order_by_untouched()
    {
        var sort = new ResultSort(0, "id", Descending: false);
        var sql = PagedSql.Build(
            "SELECT id FROM t ORDER BY id DESC", SqliteDialect.Instance, 5, 0, sort, out var applied);

        // 用户的 ORDER BY 留在子查询里（原样），我们的排序只加在外层
        Assert.True(applied);
        Assert.Equal(
            "SELECT * FROM (SELECT id FROM t ORDER BY id DESC) AS \"ddb_page\" ORDER BY 1 ASC LIMIT 5 OFFSET 0",
            sql);
    }

    [Fact]
    public void No_sort_means_no_order_by_clause_and_sort_applied_is_false()
    {
        var sql = PagedSql.Build("SELECT id FROM t", SqliteDialect.Instance, 10, 0, null, out var applied);

        Assert.False(applied);
        Assert.Equal("SELECT * FROM (SELECT id FROM t) AS \"ddb_page\" LIMIT 10 OFFSET 0", sql);
    }

    [Fact]
    public void SqlServer_gets_an_injected_order_by_only_when_it_has_none_already()
    {
        var sort = new ResultSort(0, "id", Descending: false);

        // 没有顶层 ORDER BY：可以现造一段，OFFSET/FETCH 才合法
        var injected = PagedSql.Build("SELECT id FROM dbo.t", SqlServerDialect.Instance, 25, 50, sort, out var applied1);
        Assert.True(applied1);
        Assert.Equal("SELECT id FROM dbo.t ORDER BY [id] ASC OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY", injected);

        // 已有顶层 ORDER BY：用户语义优先，分页照做但**不**声称排序生效
        var existing = PagedSql.Build(
            "SELECT id FROM dbo.t ORDER BY id", SqlServerDialect.Instance, 25, 50, sort, out var applied2);
        Assert.False(applied2);
        Assert.Equal("SELECT id FROM dbo.t ORDER BY id OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY", existing);
    }

    [Theory]
    [InlineData("COUNT(*)")]
    [InlineData("a + b")]
    [InlineData("1")]
    [InlineData("")]
    public void SqlServer_refuses_to_order_by_something_that_is_not_a_plain_column(string columnName)
    {
        var sort = new ResultSort(0, columnName, Descending: false);

        // 加方括号会把 COUNT(*) 变成未知标识符——宁可不分页，也不给出会报错的 SQL
        Assert.Null(PagedSql.Build("SELECT id FROM dbo.t", SqlServerDialect.Instance, 25, 0, sort, out var applied));
        Assert.False(applied);
    }

    [Fact]
    public void SqlServer_refuses_a_duplicate_column_name()
    {
        var sort = new ResultSort(0, "id", Descending: false, ColumnNameIsUnique: false);

        Assert.Null(PagedSql.Build("SELECT id, id FROM dbo.t", SqlServerDialect.Instance, 25, 0, sort, out var applied));
        Assert.False(applied);
    }

    [Fact]
    public void Sort_does_not_resurrect_paging_for_statements_that_cannot_be_rewritten()
    {
        var sort = new ResultSort(0, "id", Descending: false);

        // 多语句脚本 / 非查询语句：排序下推也救不回来
        Assert.Null(PagedSql.Build("SELECT 1; SELECT 2", SqliteDialect.Instance, 10, 0, sort, out var a));
        Assert.Null(PagedSql.Build("INSERT INTO t (id) VALUES (1)", SqliteDialect.Instance, 10, 0, sort, out var b));
        Assert.False(a || b);
    }

    [Fact]
    public void Sort_is_pushed_into_the_outer_wrapper_even_when_the_statement_has_its_own_limit()
    {
        // 用户自带 LIMIT 不再挡下推：排序加在外层，作用范围是"用户 LIMIT 之后的那批行"。
        var sort = new ResultSort(0, "id", Descending: true);
        var sql = PagedSql.Build("SELECT id FROM t ORDER BY id LIMIT 10", SqliteDialect.Instance, 5, 10, sort, out var applied);

        Assert.True(applied);
        Assert.Equal(
            "SELECT * FROM (SELECT id FROM t ORDER BY id LIMIT 10) AS \"ddb_page\" ORDER BY 1 DESC LIMIT 5 OFFSET 10",
            sql);
    }

    [Fact]
    public void Plan_reports_why_a_sort_could_not_be_pushed()
    {
        var sort = new ResultSort(0, "id", Descending: false);

        // 已下推：原因恒为 None
        var pushed = PagedSql.Plan("SELECT id FROM t", SqliteDialect.Instance, 10, 0, sort);
        Assert.True(pushed.SortApplied);
        Assert.Equal(SortFallbackReason.None, pushed.SortFallback);

        // 多语句 / 非查询 / T-SQL 自带分页或冲突子句：语句不可改写
        Assert.Equal(
            SortFallbackReason.StatementNotRewritable,
            PagedSql.Plan("SELECT 1; SELECT 2", SqliteDialect.Instance, 10, 0, sort).SortFallback);
        Assert.Equal(
            SortFallbackReason.StatementNotRewritable,
            PagedSql.Plan("INSERT INTO t (id) VALUES (1)", SqliteDialect.Instance, 10, 0, sort).SortFallback);
        Assert.Equal(
            SortFallbackReason.StatementNotRewritable,
            PagedSql.Plan("SELECT TOP (5) id FROM dbo.t", SqlServerDialect.Instance, 10, 0, sort).SortFallback);

        // SQL Server 已有顶层 ORDER BY：分页照做，但排序不生效
        var kept = PagedSql.Plan("SELECT id FROM dbo.t ORDER BY id", SqlServerDialect.Instance, 10, 0, sort);
        Assert.Equal(SortFallbackReason.SqlServerKeepsUserOrder, kept.SortFallback);
        Assert.False(kept.SortApplied);
        Assert.Equal("SELECT id FROM dbo.t ORDER BY id OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY", kept.Sql);

        // 列名重复 / 是表达式：按名排序会报错
        Assert.Equal(
            SortFallbackReason.UnsortableColumnName,
            PagedSql.Plan(
                "SELECT id, id FROM dbo.t",
                SqlServerDialect.Instance,
                10,
                0,
                new ResultSort(0, "id", Descending: false, ColumnNameIsUnique: false)).SortFallback);
        Assert.Equal(
            SortFallbackReason.UnsortableColumnName,
            PagedSql.Plan(
                "SELECT COUNT(*) FROM dbo.t",
                SqlServerDialect.Instance,
                10,
                0,
                new ResultSort(0, "COUNT(*)", Descending: false)).SortFallback);

        // 分页参数非法：没有服务端分页可言
        Assert.Equal(
            SortFallbackReason.NoServerPaging,
            PagedSql.Plan("SELECT id FROM t", SqliteDialect.Instance, 0, 0, sort).SortFallback);

        // 没请求排序时原因无意义，恒为 None（即便语句根本不可分页）
        Assert.Equal(
            SortFallbackReason.None,
            PagedSql.Plan("SELECT 1; SELECT 2", SqliteDialect.Instance, 10, 0).SortFallback);
    }

    [Fact]
    public void For_treats_column_names_as_case_insensitive_when_deciding_uniqueness()
    {
        var columns = new[] { "ID", "id", "name" };

        // T-SQL 默认排序规则下列名不区分大小写：ID 与 id 并存时 ORDER BY [id] 会被判 ambiguous。
        Assert.False(ResultSort.For(columns, 0, descending: false).ColumnNameIsUnique);
        Assert.False(ResultSort.For(columns, 1, descending: false).ColumnNameIsUnique);
        Assert.True(ResultSort.For(columns, 2, descending: false).ColumnNameIsUnique);
        Assert.True(ResultSort.For(columns, 2, descending: true).Descending);

        // 一路走到 SQL：大小写不同的重名列在 SQL Server 上必须拒绝按名排序，而不是给出 ambiguous 的语句。
        var duplicated = ResultSort.For(new[] { "ID", "id" }, 0, descending: false);
        Assert.Equal(
            SortFallbackReason.UnsortableColumnName,
            PagedSql.Plan("SELECT ID, id FROM dbo.t", SqlServerDialect.Instance, 10, 0, duplicated).SortFallback);
    }

    [Fact]
    public void Invalid_page_parameters_still_disable_paging_when_a_sort_is_supplied()
    {
        var sort = new ResultSort(0, "id", Descending: false);

        Assert.Null(PagedSql.Build("SELECT 1", SqliteDialect.Instance, 0, 0, sort, out var applied));
        Assert.False(applied);
        Assert.Throws<ArgumentNullException>(() => PagedSql.Build("SELECT 1", null!, 10, 0, sort, out _));
    }
}