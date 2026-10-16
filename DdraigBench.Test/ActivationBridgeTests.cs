// DdraigBench — 双击命令桥接的控件级回归（需要 UI 线程，见 TestUiThread）
//
// 两个附加属性桥接（ConnectionActivation / TreeItemActivation）此前只有 XAML 接线、没有测试：
// 桥接一旦断线（命令没接上 / 不冒泡 / 上爬找不到节点），用户双击不发生任何事且**没有任何报错**。

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DdraigBench.Controls;
using DdraigBench.Core.Metadata;
using DdraigBench.ViewModels;

namespace DdraigBench.Test;

public sealed class ActivationBridgeTests
{
    // ------------------------------------------------------------------ 已保存连接（ListBox）

    [Fact]
    public void Double_click_on_a_selected_connection_runs_the_command()
    {
        var command = new RecordingCommand();

        TestUiThread.Run(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "local", "staging" }, SelectedIndex = 0 };
            ConnectionActivation.SetCommand(list, command);

            list.RaiseEvent(DoubleTapped(list));
        });

        Assert.Equal(1, command.CallCount);
        // 连接桥接不带参数：命令自己从 SelectedSavedConnection 取
        Assert.Null(command.Calls[0]);
    }

    [Fact]
    public void Double_click_without_a_selection_does_nothing()
    {
        var command = new RecordingCommand();

        TestUiThread.Run(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "local", "staging" } };
            ConnectionActivation.SetCommand(list, command);

            list.RaiseEvent(DoubleTapped(list));
        });

        Assert.Equal(0, command.CallCount);
    }

    [Fact]
    public void Double_click_on_a_row_bubbles_up_to_the_list()
    {
        var command = new RecordingCommand();
        var calls = TestUiThread.Run(() =>
        {
            var list = new ListBox
            {
                ItemsSource = new[] { "local" },
                ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock { Text = name }),
                // 真实交互里双击前单击已经选中该行；桥接只认「有选中项」
                SelectedIndex = 0,
            };
            ConnectionActivation.SetCommand(list, command);

            var window = new Window { Width = 300, Height = 200, Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var row = list.GetVisualDescendants().OfType<TextBlock>().First();
            row.RaiseEvent(DoubleTapped(row));

            window.Close();
            return command.CallCount;
        });

        // 附加属性是按「行上冒泡」设计的：点在行内的文本上也要能触发。
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Clearing_the_command_detaches_the_double_click_handler()
    {
        var command = new RecordingCommand();

        TestUiThread.Run(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "local" }, SelectedIndex = 0 };
            ConnectionActivation.SetCommand(list, command);
            list.RaiseEvent(DoubleTapped(list));

            ConnectionActivation.SetCommand(list, null);
            list.RaiseEvent(DoubleTapped(list));
        });

        // 换绑/解绑必须摘掉旧处理器，否则清空命令后双击仍然会调旧命令
        Assert.Equal(1, command.CallCount);
    }

    [Fact]
    public void Replacing_the_command_runs_only_the_new_one()
    {
        var first = new RecordingCommand();
        var second = new RecordingCommand();

        TestUiThread.Run(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "local" }, SelectedIndex = 0 };
            ConnectionActivation.SetCommand(list, first);
            ConnectionActivation.SetCommand(list, second);

            list.RaiseEvent(DoubleTapped(list));
        });

        // Update 先 RemoveHandler 再 AddHandler：不摘旧处理器会让两个命令各响一次
        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    // ------------------------------------------------------------------ 对象树（TreeView）

    [Fact]
    public void Double_click_on_a_tree_node_runs_the_command_with_that_node()
    {
        var command = new RecordingCommand();
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, command);

            var item = FirstTreeViewItem(tree);
            item.RaiseEvent(DoubleTapped(item));
        }));

        Assert.Equal(1, command.CallCount);
        Assert.Same(nodes[0], command.Calls[0]);
    }

    [Fact]
    public void Double_click_on_a_nested_element_walks_up_to_the_nearest_data_context()
    {
        var command = new RecordingCommand();
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, command);

            // 行模板里最内层的元素自己不带 DataContext（真实模板里由祖先继承），
            // 桥接必须沿 Parent 上爬到有 DataContext 的那层。
            var leaf = tree.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Null(leaf.DataContext);

            leaf.RaiseEvent(DoubleTapped(leaf));
        }));

        Assert.Equal(1, command.CallCount);
        Assert.Same(nodes[0], command.Calls[0]);
    }

    [Fact]
    public void Double_click_outside_any_tree_node_does_nothing()
    {
        var command = new RecordingCommand();
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, command);

            // 树本体没有 DataContext，且不在任何行内：不得拿 null 去 Execute
            tree.RaiseEvent(DoubleTapped(tree));
        }));

        Assert.Equal(0, command.CallCount);
    }

    [Fact]
    public void Double_click_does_not_run_a_command_that_cannot_execute()
    {
        var command = new RecordingCommand(canExecute: false);
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, command);

            var item = FirstTreeViewItem(tree);
            item.RaiseEvent(DoubleTapped(item));
        }));

        Assert.Equal(0, command.CallCount);
    }

    [Fact]
    public void Clearing_the_command_detaches_the_tree_handler()
    {
        var command = new RecordingCommand();
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, command);
            FirstTreeViewItem(tree).RaiseEvent(DoubleTapped(FirstTreeViewItem(tree)));

            TreeItemActivation.SetCommand(tree, null);
            FirstTreeViewItem(tree).RaiseEvent(DoubleTapped(FirstTreeViewItem(tree)));
        }));

        Assert.Equal(1, command.CallCount);
    }

    [Fact]
    public void Replacing_the_tree_command_runs_only_the_new_one()
    {
        var first = new RecordingCommand();
        var second = new RecordingCommand();
        var nodes = Nodes();

        TestUiThread.Run(() => WithRealizedTree(nodes, tree =>
        {
            TreeItemActivation.SetCommand(tree, first);
            TreeItemActivation.SetCommand(tree, second);

            FirstTreeViewItem(tree).RaiseEvent(DoubleTapped(FirstTreeViewItem(tree)));
        }));

        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    // ------------------------------------------------------------------ 辅助

    private static ObservableCollection<ConnectionTreeNodeViewModel> Nodes() =>
    [
        new ConnectionTreeNodeViewModel("orders", DbObjectKind.Table),
        new ConnectionTreeNodeViewModel("order_view", DbObjectKind.View),
    ];

    /// <summary>把树放进窗口、跑一次布局，TreeViewItem 才会被真正实现出来。</summary>
    private static void WithRealizedTree(
        ObservableCollection<ConnectionTreeNodeViewModel> nodes, Action<TreeView> body)
    {
        var tree = new TreeView
        {
            ItemsSource = nodes,
            ItemTemplate = new FuncTreeDataTemplate<ConnectionTreeNodeViewModel>(
                (node, _) => new Border
                {
                    DataContext = null,
                    Child = new TextBlock { Text = node.Name },
                },
                node => node.Children),
        };

        var window = new Window { Width = 400, Height = 300, Content = tree };
        window.Show();
        Dispatcher.UIThread.RunJobs(null);

        try
        {
            body(tree);
        }
        finally
        {
            window.Close();
        }
    }

    private static TreeViewItem FirstTreeViewItem(TreeView tree) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>().First();

    /// <summary>合成一次双击：headless 平台没有真实指针，<c>DoubleTapped</c> 要自己造参数。</summary>
    private static TappedEventArgs DoubleTapped(Control target)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var pointerArgs = new PointerEventArgs(
            InputElement.DoubleTappedEvent,
            target,
            pointer,
            target,
            new Point(0, 0),
            0,
            new PointerPointProperties(),
            KeyModifiers.None);

        return new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs);
    }

    private sealed class RecordingCommand(bool canExecute = true) : ICommand
    {
        public List<object?> Calls { get; } = [];

        public int CallCount => Calls.Count;

        public bool CanExecuteResult { get; set; } = canExecute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => CanExecuteResult;

        public void Execute(object? parameter) => Calls.Add(parameter);
    }
}
