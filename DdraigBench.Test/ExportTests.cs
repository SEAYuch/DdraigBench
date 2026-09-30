// DdraigBench — 导出（CSV / INSERT）单测

using DdraigBench.Core.Dialects;
using DdraigBench.Core.Exporting;
using DdraigBench.Core.Querying;

namespace DdraigBench.Test;

public sealed class ExportTests
{
    private static QueryResult MakeResult(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows) =>
        new(columns, rows, 0, TimeSpan.Zero, null);

    [Fact]
    public void Csv_writes_header_and_rows()
    {
        var result = MakeResult(
            new[] { "id", "name" },
            new object?[][]
            {
                new object?[] { 1, "a" },
                new object?[] { 2, "b" },
            });

        Assert.Equal("id,name\r\n1,a\r\n2,b\r\n", ResultExporter.ToCsv(result));
    }

    [Fact]
    public void Csv_quotes_delimiter_quote_newline_and_null_is_empty()
    {
        var result = MakeResult(
            new[] { "a;b", "q" },
            new object?[][]
            {
                new object?[] { "x;y", "he said \"hi\"" },
                new object?[] { "l1\nl2", null },
            });

        Assert.Equal(
            "\"a;b\";q\r\n\"x;y\";\"he said \"\"hi\"\"\"\r\n\"l1\nl2\";\r\n",
            ResultExporter.ToCsv(result, delimiter: ";"));
    }

    [Fact]
    public void Csv_formats_cell_types()
    {
        var result = MakeResult(
            new[] { "b", "d", "bin", "n" },
            new object?[][]
            {
                new object?[]
                {
                    true,
                    new DateTime(2026, 9, 21, 13, 5, 0),
                    new byte[] { 0x68, 0x69 },
                    1.5,
                },
            });

        Assert.Equal("b,d,bin,n\r\ntrue,2026-09-21 13:05:00.000,aGk=,1.5\r\n", ResultExporter.ToCsv(result));
    }

    [Fact]
    public void Csv_honors_custom_delimiter()
    {
        var result = MakeResult(new[] { "a" }, new object?[][]
        {
            new object?[] { "x;y" },
        });

        Assert.Equal("a\n\"x;y\"\n", ResultExporter.ToCsv(result, delimiter: ";", lineEnding: "\n"));
    }

    [Fact]
    public void Insert_uses_dialect_quoting_and_literals()
    {
        var result = MakeResult(
            new[] { "id", "name" },
            new object?[][]
            {
                new object?[] { 1, "it's" },
                new object?[] { 2, null },
            });

        Assert.Equal(
            "INSERT INTO \"t\" (\"id\", \"name\") VALUES (1, 'it''s');\nINSERT INTO \"t\" (\"id\", \"name\") VALUES (2, NULL);\n",
            ResultExporter.ToInsert(result, "t", SqliteDialect.Instance));

        Assert.Equal(
            "INSERT INTO `t` (`id`, `name`) VALUES (1, 'it''s');\nINSERT INTO `t` (`id`, `name`) VALUES (2, NULL);\n",
            ResultExporter.ToInsert(result, "t", MySqlDialect.Instance));
    }

    [Fact]
    public void Insert_batches_rows_per_statement()
    {
        var result = MakeResult(
            new[] { "id" },
            new object?[][]
            {
                new object?[] { 1 },
                new object?[] { 2 },
                new object?[] { 3 },
            });

        Assert.Equal(
            "INSERT INTO \"t\" (\"id\") VALUES (1), (2);\nINSERT INTO \"t\" (\"id\") VALUES (3);\n",
            ResultExporter.ToInsert(result, "t", SqliteDialect.Instance, batchSize: 2));
    }

    [Fact]
    public void Insert_returns_empty_for_empty_result_and_placeholder_columns()
    {
        Assert.Equal(string.Empty, ResultExporter.ToInsert(MakeResult(Array.Empty<string>(), Array.Empty<object?[]>()), "t", SqliteDialect.Instance));

        var noRows = MakeResult(new[] { "a" }, Array.Empty<object?[]>());
        Assert.Equal(string.Empty, ResultExporter.ToInsert(noRows, "t", SqliteDialect.Instance));

        var blankColumn = MakeResult(new[] { " " }, new object?[][]
        {
            new object?[] { 1 },
        });
        Assert.Equal("INSERT INTO \"t\" (\"column1\") VALUES (1);\n", ResultExporter.ToInsert(blankColumn, "t", SqliteDialect.Instance));
    }
}
