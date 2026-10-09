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

    [Fact]
    public void Navigator_collapse_and_restore_preserves_splitter_width()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,5,*"),
        };

        WorkbenchLayout.SetNavigatorVisible(grid, false);

        Assert.Equal(0, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(0, grid.ColumnDefinitions[1].Width.Value);

        WorkbenchLayout.SetNavigatorVisible(grid, true);

        Assert.Equal(320, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(5, grid.ColumnDefinitions[1].Width.Value);
    }

    [Fact]
    public void Query_editor_collapse_and_restore_keeps_adjustable_split()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,5,2*"),
        };

        WorkbenchLayout.SetQueryEditorVisible(grid, false);

        Assert.Equal(0, grid.RowDefinitions[0].Height.Value);
        Assert.Equal(0, grid.RowDefinitions[1].Height.Value);

        WorkbenchLayout.SetQueryEditorVisible(grid, true);

        Assert.Equal(3, grid.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Star, grid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(5, grid.RowDefinitions[1].Height.Value);
    }
}
