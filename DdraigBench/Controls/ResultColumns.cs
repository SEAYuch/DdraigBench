// DdraigBench — DataGrid 动态列（附加属性，避免派生 DataGrid）

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace DdraigBench.Controls;

public static class ResultColumns
{
    public static readonly AttachedProperty<IReadOnlyList<string>?> HeadersProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, IReadOnlyList<string>?>("Headers", typeof(ResultColumns));

    static ResultColumns()
    {
        HeadersProperty.Changed.AddClassHandler<DataGrid>((grid, change) =>
            Rebuild(grid, change.GetNewValue<IReadOnlyList<string>?>()));
    }

    public static IReadOnlyList<string>? GetHeaders(DataGrid grid) => grid.GetValue(HeadersProperty);

    public static void SetHeaders(DataGrid grid, IReadOnlyList<string>? value) => grid.SetValue(HeadersProperty, value);

    private static void Rebuild(DataGrid grid, IReadOnlyList<string>? headers)
    {
        grid.Columns.Clear();
        if (headers is null)
        {
            return;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var index = i;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = headers[i],
                CellTemplate = new FuncDataTemplate<object?[]>(
                    (row, _) => new TextBlock
                    {
                        Text = row is null || index >= row.Length
                            ? string.Empty
                            : row[index]?.ToString() ?? string.Empty,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0),
                    }),
            });
        }
    }
}
