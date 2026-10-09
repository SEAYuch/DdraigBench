// DdraigBench — 已保存连接双击命令桥接

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace DdraigBench.Controls;

public static class ConnectionActivation
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ICommand?>("Command", typeof(ConnectionActivation));

    static ConnectionActivation()
    {
        CommandProperty.Changed.AddClassHandler<ListBox>((list, _) => Update(list));
    }

    public static void SetCommand(AvaloniaObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(AvaloniaObject element) => element.GetValue(CommandProperty);

    private static void Update(ListBox list)
    {
        list.RemoveHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        if (GetCommand(list) is not null)
        {
            list.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped);
        }
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox list || GetCommand(list) is not { } command)
        {
            return;
        }

        if (sender is ListBox { SelectedItem: not null } && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
