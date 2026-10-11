// DdraigBench — headless UI 单测（动态列附加属性）

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    public void Blob_cells_render_as_hex_instead_of_the_type_name()
    {
        var grid = new DataGrid();

        ResultColumns.SetHeaders(grid, new[] { "payload" });

        var column = Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
        var cell = Assert.IsType<TextBlock>(
            column.CellTemplate!.Build(new object?[] { new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } }));

        // C7：byte[] 曾经显示成 "System.Byte[]"
        Assert.Equal("0xDEADBEEF", cell.Text);
    }

    [Fact]
    public void Blob_formatting_truncates_long_values_and_leaves_other_types_alone()
    {
        Assert.Equal(string.Empty, ResultColumns.Format(null));
        Assert.Equal("123", ResultColumns.Format(123));
        Assert.Equal("0x00", ResultColumns.Format(new byte[] { 0x00 }));

        var truncated = ResultColumns.Format(new byte[40]);

        Assert.Contains("共 40 字节", truncated);
        Assert.StartsWith("0x00000000", truncated);
    }

    [Fact]
    public void Query_editor_collapse_and_restore_keeps_the_results_row_after_a_splitter_drag()
    {
        // 拖动上下分割条后三行都是绝对像素（GridSplitter 会把参与的两行都写成 Pixel）
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("421,5,334"),
        };

        WorkbenchLayout.SetQueryEditorVisible(grid, false);

        Assert.Equal(0, grid.RowDefinitions[0].Height.Value);
        Assert.Equal(0, grid.RowDefinitions[1].Height.Value);
        Assert.Equal(1, grid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, grid.RowDefinitions[2].Height.GridUnitType);

        WorkbenchLayout.SetQueryEditorVisible(grid, true);

        // A3：只恢复前两行会让结果区留在 1*，折叠再展开后比例漂移
        Assert.Equal(421, grid.RowDefinitions[0].Height.Value);
        Assert.Equal(5, grid.RowDefinitions[1].Height.Value);
        Assert.Equal(334, grid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, grid.RowDefinitions[2].Height.GridUnitType);
    }

    [Fact]
    public void Navigator_collapse_and_restore_keeps_the_workspace_column_after_a_splitter_drag()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,5,845"),
        };

        WorkbenchLayout.SetNavigatorVisible(grid, false);

        Assert.Equal(0, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(0, grid.ColumnDefinitions[1].Width.Value);
        // 收起导航器后工作区必须吃掉整块宽度，否则右侧留一条空白
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[2].Width.GridUnitType);

        WorkbenchLayout.SetNavigatorVisible(grid, true);

        Assert.Equal(320, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(5, grid.ColumnDefinitions[1].Width.Value);
        Assert.Equal(845, grid.ColumnDefinitions[2].Width.Value);
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

    [Fact]
    public void Numeric_and_date_columns_are_right_aligned_while_text_stays_left()
    {
        var grid = new DataGrid();
        var row = new object?[] { 42, "text", new DateTime(2026, 10, 11, 8, 30, 0) };

        ResultColumns.SetHeaders(grid, new[] { "n", "s", "d" });
        ResultColumns.SetCells(grid, new[] { row });

        var number = Cell(grid, 0, row);
        var text = Cell(grid, 1, row);
        var date = Cell(grid, 2, row);

        // 列类型只能按已读回的值推断（QueryResult 没有类型元数据）：数字/日期右对齐，文本左对齐
        Assert.Equal(TextAlignment.Right, number.TextAlignment);
        Assert.Equal(TextAlignment.Left, text.TextAlignment);
        Assert.Equal(TextAlignment.Right, date.TextAlignment);
        // 必须以"撑满单元格 + 文本靠边"实现，否则 TextBlock 缩到文字宽度、对齐看不出效果
        Assert.Equal(HorizontalAlignment.Stretch, number.HorizontalAlignment);
    }

    [Fact]
    public void Column_headers_become_sort_buttons_when_a_sort_command_is_supplied()
    {
        var command = new RecordingCommand();
        var grid = new DataGrid();

        ResultColumns.SetHeaders(grid, new[] { "n", "s" });
        ResultColumns.SetSort(grid, command);

        var header = Assert.IsType<Button>(grid.Columns[0].Header);
        Assert.Equal(0, header.CommandParameter);

        header.Command!.Execute(header.CommandParameter);

        Assert.Equal(0, command.LastParameter);
    }

    [Fact]
    public void Column_headers_stay_plain_text_without_a_sort_command()
    {
        var grid = new DataGrid();

        ResultColumns.SetHeaders(grid, new[] { "n" });

        Assert.Equal("n", grid.Columns[0].Header);
    }

    private static TextBlock Cell(DataGrid grid, int index, object?[] row) =>
        Assert.IsType<TextBlock>(
            Assert.IsType<DataGridTemplateColumn>(grid.Columns[index]).CellTemplate!.Build(row));

    private sealed class RecordingCommand : System.Windows.Input.ICommand
    {
        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => LastParameter = parameter;
    }
}
