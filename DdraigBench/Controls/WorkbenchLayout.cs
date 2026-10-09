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
    }

    private sealed class QueryState
    {
        public GridLength EditorHeight { get; set; } = new(2, GridUnitType.Star);
        public GridLength SplitterHeight { get; set; } = new(5);
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
        }
        else
        {
            if (grid.ColumnDefinitions[0].Width.Value > 0)
            {
                state.Width = grid.ColumnDefinitions[0].Width;
            }

            grid.ColumnDefinitions[0].Width = new GridLength(0);
            grid.ColumnDefinitions[1].Width = new GridLength(0);
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
            grid.RowDefinitions[0].Height = state.EditorHeight;
            grid.RowDefinitions[1].Height = state.SplitterHeight;
        }
        else
        {
            if (grid.RowDefinitions[0].Height.Value > 0)
            {
                state.EditorHeight = grid.RowDefinitions[0].Height;
                state.SplitterHeight = grid.RowDefinitions[1].Height;
            }

            grid.RowDefinitions[0].Height = new GridLength(0);
            grid.RowDefinitions[1].Height = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
    }
}
