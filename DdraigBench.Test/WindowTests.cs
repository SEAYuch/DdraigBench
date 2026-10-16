// DdraigBench — Window / TopLevel 的 headless 回归（需要 UI 线程，见 TestUiThread）

using Avalonia.Controls;
using Avalonia.LogicalTree;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Test;

public sealed class WindowTests
{
    [Fact]
    public void Window_can_be_constructed_shown_and_closed()
    {
        // 修复前：new Window() 在 PresentationSource → Compositor.RegisterForSerialization 处
        // 抛 InvalidOperationException（Composition 归 assembly fixture 的线程），
        // 于是"控件级回归一律只 new 控件、不建 TopLevel"。
        var (shown, hasContent) = TestUiThread.Run(() =>
        {
            var window = new Window
            {
                Title = "ddb-headless",
                Width = 400,
                Height = 300,
                Content = new TextBlock { Text = "hello" },
            };

            window.Show();
            var shown = window.IsVisible;
            var content = window.Content is TextBlock;

            window.Close();

            return (shown, content);
        });

        Assert.True(shown);
        Assert.True(hasContent);
    }

    [Fact]
    public void Query_tab_view_renders_inside_a_window()
    {
        var (isVisible, editorCount) = TestUiThread.Run(() =>
        {
            var vm = new QueryTabViewModel("查询", () => null, new StubFilePicker(), new StubExportDialog());
            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = new QueryTabView { DataContext = vm },
            };

            window.Show();

            var view = Assert.IsType<QueryTabView>(window.Content);
            var editors = view.GetLogicalDescendants().OfType<AvaloniaEdit.TextEditor>().Count();

            var visible = window.IsVisible;
            window.Close();

            return (visible, editors);
        });

        Assert.True(isVisible);
        Assert.Equal(1, editorCount);
    }
}
