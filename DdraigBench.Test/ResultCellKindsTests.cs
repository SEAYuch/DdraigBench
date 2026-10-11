// DdraigBench — 结果集列类型推断与排序比较单测（纯逻辑，无数据库、无 UI）

using DdraigBench.Core.Querying;

namespace DdraigBench.Test;

public sealed class ResultCellKindsTests
{
    [Fact]
    public void Numeric_columns_are_inferred_as_numbers_and_sorted_numerically()
    {
        var rows = Rows(9, 10, 2);

        Assert.Equal(ResultCellKind.Number, ResultCellKinds.Infer(rows, 0));

        var sorted = ResultCellKinds.Sorted(rows, 0, descending: false);

        // 按文本比会得到 10, 2, 9 —— 数字列必须按数值比
        Assert.Equal(new object?[] { 2, 9, 10 }, sorted.Select(Value).ToArray());

        var descending = ResultCellKinds.Sorted(rows, 0, descending: true);

        Assert.Equal(new object?[] { 10, 9, 2 }, descending.Select(Value).ToArray());
    }

    [Fact]
    public void Nulls_stay_last_in_both_directions()
    {
        var rows = Rows(1, null, 3);

        // 把 null 当最小值再整体取反，会让降序时空值冲到最前面
        Assert.Equal(new object?[] { 1, 3, null }, ResultCellKinds.Sorted(rows, 0, false).Select(Value).ToArray());
        Assert.Equal(new object?[] { 3, 1, null }, ResultCellKinds.Sorted(rows, 0, true).Select(Value).ToArray());
    }

    [Fact]
    public void Dates_compare_by_instant_and_booleans_compare_false_first()
    {
        var later = new DateTime(2026, 10, 11, 8, 0, 0);
        var earlier = new DateTime(2026, 10, 10, 23, 59, 59);

        Assert.Equal(ResultCellKind.DateTime, ResultCellKinds.Infer(Rows(later, earlier), 0));
        Assert.Equal(
            new object?[] { earlier, later },
            ResultCellKinds.Sorted(Rows(later, earlier), 0, false).Select(Value).ToArray());

        Assert.Equal(ResultCellKind.Boolean, ResultCellKinds.Infer(Rows(true, false), 0));
        Assert.Equal(
            new object?[] { false, true },
            ResultCellKinds.Sorted(Rows(true, false), 0, false).Select(Value).ToArray());
    }

    [Fact]
    public void Binary_columns_compare_byte_by_byte_then_by_length()
    {
        Assert.Equal(ResultCellKind.Binary, ResultCellKinds.Infer(Rows(new byte[] { 2 }, new byte[] { 1 }), 0));

        var rows = Rows(new byte[] { 1, 0 }, new byte[] { 1 }, new byte[] { 2 });

        var sorted = ResultCellKinds.Sorted(rows, 0, false);

        // {1} < {1,0}（前缀相同，短的在前）< {2}
        Assert.Equal(
            new[] { "1", "1,0", "2" },
            sorted.Select(row => string.Join(',', (byte[])row[0]!)).ToArray());
    }

    [Fact]
    public void Mixed_or_all_null_columns_fall_back_to_text()
    {
        Assert.Equal(ResultCellKind.Text, ResultCellKinds.Infer(Rows(1, "x"), 0));
        Assert.Equal(ResultCellKind.Text, ResultCellKinds.Infer(Rows(null, null), 0));
        Assert.Equal(ResultCellKind.Text, ResultCellKinds.Infer(Array.Empty<object?[]>(), 0));
        Assert.Equal(ResultCellKind.Number, ResultCellKinds.Infer(Rows(1, null, 2), 0));
    }

    [Fact]
    public void Sorting_is_stable_for_equal_values()
    {
        object?[][] rows = [["a", 1], ["b", 1], ["c", 0]];

        var sorted = ResultCellKinds.Sorted(rows, 1, false);

        // 同分的 a/b 必须保持原有先后，否则每次点同一列都可能打乱
        Assert.Equal(new object?[] { "c", "a", "b" }, sorted.Select(row => row[0]).ToArray());
    }

    [Fact]
    public void Cell_reads_short_or_missing_rows_as_null()
    {
        Assert.Equal(1, ResultCellKinds.Cell(new object?[] { 1 }, 0));
        Assert.Null(ResultCellKinds.Cell(new object?[] { 1 }, 5));
        Assert.Null(ResultCellKinds.Cell(null, 0));
    }

    private static object?[][] Rows(params object?[] values) =>
        values.Select(value => new object?[] { value }).ToArray();

    private static object? Value(object?[] row) => row[0];
}
