// DdraigBench — TextEditor 与 EditorText 附加属性的 UI 回归（需要 UI 线程，见 TestUiThread）

using Avalonia.Data;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using DdraigBench.Controls;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Test;

public sealed class EditorTextTests
{
    [Fact]
    public void Attached_text_is_pushed_into_the_editor()
    {
        // 修复前：在线程池线程上 new TextEditor() 直接 VerifyAccess 抛 InvalidOperationException
        var text = TestUiThread.Run(() =>
        {
            var editor = new TextEditor();

            EditorText.SetText(editor, "select 1");

            return editor.Text;
        });

        Assert.Equal("select 1", text);
    }

    [Fact]
    public void Editor_edits_flow_back_into_the_attached_property()
    {
        var text = TestUiThread.Run(() =>
        {
            var editor = new TextEditor();
            EditorText.SetText(editor, "select 1");

            // 模拟用户敲键盘：TextEditor 自己改 Text
            editor.Text = "select 2";

            return EditorText.GetText(editor);
        });

        Assert.Equal("select 2", text);
    }

    [Fact]
    public void Repeated_assignment_does_not_loop_through_text_changed()
    {
        var text = TestUiThread.Run(() =>
        {
            var editor = new TextEditor();
            EditorText.SetText(editor, "a");
            EditorText.SetText(editor, "a");
            EditorText.SetText(editor, "b");

            return editor.Text;
        });

        Assert.Equal("b", text);
    }

    [Fact]
    public void Two_way_binding_keeps_the_view_model_and_the_editor_in_sync()
    {
        // QueryTabView.axaml 的真实形状：controls:EditorText.Text="{Binding Sql, Mode=TwoWay}"
        var vm = new QueryTabViewModel("查询", () => null, new StubFilePicker(), new StubExportDialog())
        {
            Sql = "select 1",
        };

        var (loaded, pushed, typed) = TestUiThread.Run(() =>
        {
            var editor = new TextEditor();
            editor.Bind(
                EditorText.TextProperty,
                new Binding(nameof(QueryTabViewModel.Sql)) { Source = vm, Mode = BindingMode.TwoWay });

            var afterBind = editor.Text;

            vm.Sql = "select 2";
            var afterViewModelEdit = editor.Text;

            editor.Text = "select 3";

            return (afterBind, afterViewModelEdit, vm.Sql);
        });

        Assert.Equal("select 1", loaded);
        Assert.Equal("select 2", pushed);
        Assert.Equal("select 3", typed);
    }

    [Fact]
    public void Query_tab_view_loads_its_editor_and_binds_it_to_the_view_model()
    {
        var (fromViewModel, fromEditor, typedBack) = TestUiThread.Run(() =>
        {
            var vm = new QueryTabViewModel("查询", () => null, new StubFilePicker(), new StubExportDialog())
            {
                Sql = "select 42",
            };

            var initial = vm.Sql;

            var view = new QueryTabView { DataContext = vm };
            var editor = Assert.Single(view.GetLogicalDescendants().OfType<TextEditor>());

            var bound = editor.Text;

            editor.Text = "select 43";

            return (initial, bound, vm.Sql);
        });

        Assert.Equal("select 42", fromViewModel);
        Assert.Equal("select 42", fromEditor);
        // XAML 里的 Mode=TwoWay 必须真的生效：编辑器里的输入要回到 ViewModel.Sql
        Assert.Equal("select 43", typedBack);
    }
}
