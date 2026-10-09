// DdraigBench — 树项目双击命令桥接

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DdraigBench.Controls;

public static class TreeItemActivation
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<TreeView, ICommand?>("Command", typeof(TreeItemActivation));

    static TreeItemActivation()
    {
        CommandProperty.Changed.AddClassHandler<TreeView>((tree, _) => Update(tree));
    }

    public static void SetCommand(AvaloniaObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(AvaloniaObject element) => element.GetValue(CommandProperty);

    private static void Update(TreeView tree)
    {
        tree.RemoveHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        if (GetCommand(tree) is not null)
        {
            tree.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        }
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TreeView tree || GetCommand(tree) is not { } command)
        {
            return;
        }

        var source = e.Source as Control;
        var item = source;
        while (item is not null && item.DataContext is null)
        {
            item = item.Parent as Control;
        }

        if (item?.DataContext is { } node && command.CanExecute(node))
        {
            command.Execute(node);
        }
    }
}
