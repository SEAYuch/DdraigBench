// DdraigBench — 工作台可折叠分区布局

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace DdraigBench.Controls;

public static class WorkbenchLayout
{
    private sealed class NavigatorState
    {
        public GridLength Width { get; set; } = new(270);

        // 分割条拖动后两侧列都是绝对像素（Avalonia GridSplitter 会把参与的两行/两列都写成 Pixel），
        // 折叠时若不把工作区列也记下来，展开就只能恢复导航器一侧，工作区宽度漂移（A3，2026-10-11）。
        public GridLength WorkspaceWidth { get; set; } = new(1, GridUnitType.Star);
    }

    private sealed class QueryState
    {
        public GridLength EditorHeight { get; set; } = new(2, GridUnitType.Star);
        public GridLength SplitterHeight { get; set; } = new(5);
        public GridLength ResultsHeight { get; set; } = new(1, GridUnitType.Star);
    }

    private static readonly ConditionalWeakTable<Grid, NavigatorState> Navigators = new();
    private static readonly ConditionalWeakTable<Grid, QueryState> Queries = new();

    public static readonly AttachedProperty<bool> NavigatorVisibleProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("NavigatorVisible", typeof(WorkbenchLayout), true);

    public static readonly AttachedProperty<bool> QueryEditorVisibleProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("QueryEditorVisible", typeof(WorkbenchLayout), true);

    static WorkbenchLayout()
    {
        NavigatorVisibleProperty.Changed.AddClassHandler<Grid>(OnNavigatorVisibilityChanged);
        QueryEditorVisibleProperty.Changed.AddClassHandler<Grid>(OnQueryEditorVisibilityChanged);
        NavigatorVisibleProperty.Changed.AddClassHandler<Grid>(EnsureLoadedInitialization);
        QueryEditorVisibleProperty.Changed.AddClassHandler<Grid>(EnsureLoadedInitialization);
    }

    public static bool GetNavigatorVisible(Grid grid) => grid.GetValue(NavigatorVisibleProperty);

    public static void SetNavigatorVisible(Grid grid, bool value) => grid.SetValue(NavigatorVisibleProperty, value);

    public static bool GetQueryEditorVisible(Grid grid) => grid.GetValue(QueryEditorVisibleProperty);

    public static void SetQueryEditorVisible(Grid grid, bool value) => grid.SetValue(QueryEditorVisibleProperty, value);

    private static void EnsureLoadedInitialization(Grid grid, AvaloniaPropertyChangedEventArgs _)
    {
        grid.Loaded -= OnGridLoaded;
        grid.Loaded += OnGridLoaded;
    }

    private static void OnGridLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        if (sender is not Grid grid)
        {
            return;
        }

        grid.Loaded -= OnGridLoaded;
        if (grid.ColumnDefinitions.Count >= 2)
        {
            ApplyNavigatorVisibility(grid, GetNavigatorVisible(grid));
        }

        if (grid.RowDefinitions.Count >= 3)
        {
            ApplyQueryEditorVisibility(grid, GetQueryEditorVisible(grid));
        }
    }

    private static void OnNavigatorVisibilityChanged(Grid grid, AvaloniaPropertyChangedEventArgs change) =>
        ApplyNavigatorVisibility(grid, change.GetNewValue<bool>());

    private static void ApplyNavigatorVisibility(Grid grid, bool isVisible)
    {
        if (grid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var state = Navigators.GetOrCreateValue(grid);
        if (isVisible)
        {
            grid.ColumnDefinitions[0].Width = state.Width;
            grid.ColumnDefinitions[1].Width = new GridLength(5);
            // 三列一起还原：拖动过分割条时工作区列也是绝对像素，不还原就会把宽度漂移留在界面上（A3）。
            if (grid.ColumnDefinitions.Count >= 3)
            {
                grid.ColumnDefinitions[2].Width = state.WorkspaceWidth;
            }
        }
        else
        {
            if (grid.ColumnDefinitions[0].Width.Value > 0)
            {
                state.Width = grid.ColumnDefinitions[0].Width;
                if (grid.ColumnDefinitions.Count >= 3)
                {
                    state.WorkspaceWidth = grid.ColumnDefinitions[2].Width;
                }
            }

            grid.ColumnDefinitions[0].Width = new GridLength(0);
            grid.ColumnDefinitions[1].Width = new GridLength(0);
            if (grid.ColumnDefinitions.Count >= 3)
            {
                // 收起导航器后工作区必须吃掉整块宽度，否则右侧留一条空白。
                grid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }

    private static void OnQueryEditorVisibilityChanged(Grid grid, AvaloniaPropertyChangedEventArgs change) =>
        ApplyQueryEditorVisibility(grid, change.GetNewValue<bool>());

    private static void ApplyQueryEditorVisibility(Grid grid, bool isVisible)
    {
        if (grid.RowDefinitions.Count < 3)
        {
            return;
        }

        var state = Queries.GetOrCreateValue(grid);
        if (isVisible)
        {
            // 三行一起还原（含结果区）：拖动过上下分割条时结果区也是绝对像素，
            // 只还原编辑区会让「编辑器:结果」比例在折叠再展开后漂移（A3，2026-10-11）。
            grid.RowDefinitions[0].Height = state.EditorHeight;
            grid.RowDefinitions[1].Height = state.SplitterHeight;
            grid.RowDefinitions[2].Height = state.ResultsHeight;
        }
        else
        {
            if (grid.RowDefinitions[0].Height.Value > 0)
            {
                state.EditorHeight = grid.RowDefinitions[0].Height;
                state.SplitterHeight = grid.RowDefinitions[1].Height;
                state.ResultsHeight = grid.RowDefinitions[2].Height;
            }

            grid.RowDefinitions[0].Height = new GridLength(0);
            grid.RowDefinitions[1].Height = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
    }
}
