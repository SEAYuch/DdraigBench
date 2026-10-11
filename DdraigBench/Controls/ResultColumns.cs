// DdraigBench — DataGrid 动态列（附加属性，避免派生 DataGrid）

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DdraigBench.Core.Querying;

namespace DdraigBench.Controls;

public static class ResultColumns
{
    public static readonly AttachedProperty<IReadOnlyList<string>?> HeadersProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, IReadOnlyList<string>?>("Headers", typeof(ResultColumns));

    /// <summary>
    /// 当前页的行（与 <c>ItemsSource</c> 同源）。<b>只用于推断列类型</b>：<see cref="QueryResult"/> 没有类型元数据，
    /// 想知道一列是数字还是日期，只能看已读回的值（数字/日期右对齐就是靠它）。
    /// </summary>
    public static readonly AttachedProperty<IReadOnlyList<object?[]>?> CellsProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, IReadOnlyList<object?[]>?>("Cells", typeof(ResultColumns));

    /// <summary>表头点击时要执行的排序命令，参数是列序号。为 null 时表头就是普通文本（不假装能排序）。</summary>
    public static readonly AttachedProperty<ICommand?> SortProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, ICommand?>("Sort", typeof(ResultColumns));

    static ResultColumns()
    {
        HeadersProperty.Changed.AddClassHandler<DataGrid>((grid, _) => Rebuild(grid));
        CellsProperty.Changed.AddClassHandler<DataGrid>((grid, _) => Rebuild(grid));
        SortProperty.Changed.AddClassHandler<DataGrid>((grid, _) => Rebuild(grid));
    }

    public static IReadOnlyList<string>? GetHeaders(DataGrid grid) => grid.GetValue(HeadersProperty);

    public static void SetHeaders(DataGrid grid, IReadOnlyList<string>? value) => grid.SetValue(HeadersProperty, value);

    public static IReadOnlyList<object?[]>? GetCells(DataGrid grid) => grid.GetValue(CellsProperty);

    public static void SetCells(DataGrid grid, IReadOnlyList<object?[]>? value) => grid.SetValue(CellsProperty, value);

    public static ICommand? GetSort(DataGrid grid) => grid.GetValue(SortProperty);

    public static void SetSort(DataGrid grid, ICommand? value) => grid.SetValue(SortProperty, value);

    private static void Rebuild(DataGrid grid)
    {
        var headers = grid.GetValue(HeadersProperty);
        var cells = grid.GetValue(CellsProperty);
        var sort = grid.GetValue(SortProperty);

        grid.Columns.Clear();
        if (headers is null)
        {
            return;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var index = i;
            var kind = cells is null ? ResultCellKind.Text : ResultCellKinds.Infer(cells, index);
            var alignment = kind is ResultCellKind.Number or ResultCellKind.DateTime
                ? TextAlignment.Right
                : TextAlignment.Left;

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = BuildHeader(headers[i], index, sort),
                CellTemplate = new FuncDataTemplate<object?[]>(
                    (row, _) => new TextBlock
                    {
                        Text = Format(ResultCellKinds.Cell(row, index)),
                        TextAlignment = alignment,
                        // 撑满单元格再靠 TextAlignment 靠边，否则 TextBlock 会缩到文字宽度、对齐看不出效果
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0),
                    }),
            });
        }
    }

    /// <summary>
    /// 表头。给了排序命令就包一个透明按钮（表头能点），否则保持纯文本。
    /// </summary>
    private static object BuildHeader(string header, int index, ICommand? sort)
    {
        if (sort is null)
        {
            return header;
        }

        var button = new Button
        {
            Content = new TextBlock { Text = header, TextTrimming = TextTrimming.CharacterEllipsis },
            Command = sort,
            CommandParameter = index,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        ToolTip.SetTip(button, $"点击按「{header}」排序（只排当前页已读回的行）");
        return button;
    }

    /// <summary>
    /// 单元格显示文本。二进制列（BLOB → <c>byte[]</c>）渲染成十六进制，而不是 .NET 默认的 <c>System.Byte[]</c>
    /// （C7，2026-10-11）；超长内容只取前 <see cref="MaxHexBytes"/> 字节并标注总长，避免一行塞进几 MB。
    /// </summary>
    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        byte[] bytes => FormatBytes(bytes),
        _ => value.ToString() ?? string.Empty,
    };

    private const int MaxHexBytes = 32;

    private static string FormatBytes(byte[] bytes) => bytes.Length <= MaxHexBytes
        ? $"0x{Convert.ToHexString(bytes)}"
        : $"0x{Convert.ToHexString(bytes.AsSpan(0, MaxHexBytes))}…（共 {bytes.Length} 字节）";
}
