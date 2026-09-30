// DdraigBench — headless UI 单测（动态列附加属性）

using Avalonia.Controls;
using DdraigBench.Controls;

namespace DdraigBench.Test;

public sealed class HeadlessUiTests
{
    [Fact]
    public void Headers_build_columns_and_cells_read_untyped_rows()
    {
        var grid = new DataGrid();

        ResultColumns.SetHeaders(grid, new[] { "n", "s" });

        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("n", grid.Columns[0].Header);

        var column = Assert.IsType<DataGridTemplateColumn>(grid.Columns[1]);
        var cell = Assert.IsType<TextBlock>(column.CellTemplate!.Build(new object?[] { 1, "x" }));

        Assert.Equal("x", cell.Text);
    }

    [Fact]
    public void Headers_null_clears_columns()
    {
        var grid = new DataGrid();
        ResultColumns.SetHeaders(grid, new[] { "n" });

        ResultColumns.SetHeaders(grid, null);

        Assert.Empty(grid.Columns);
    }
}
