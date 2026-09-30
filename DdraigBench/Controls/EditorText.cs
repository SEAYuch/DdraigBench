// DdraigBench — TextEditor 文本双向同步的附加属性（TextEditor.Text 非 AvaloniaProperty，不能直接绑定）

using System.Runtime.CompilerServices;
using Avalonia;
using AvaloniaEdit;

namespace DdraigBench.Controls;

public static class EditorText
{
    private static readonly ConditionalWeakTable<TextEditor, object> Tracked = new();

    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, string?>("Text", typeof(EditorText));

    static EditorText()
    {
        TextProperty.Changed.AddClassHandler<TextEditor>(OnTextChanged);
    }

    public static string? GetText(TextEditor editor) => editor.GetValue(TextProperty);

    public static void SetText(TextEditor editor, string? value) => editor.SetValue(TextProperty, value);

    private static void OnTextChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs change)
    {
        // 首次见到该编辑器时挂一次反向同步（用户输入 → 附加属性 → 绑定回 ViewModel）
        if (!Tracked.TryGetValue(editor, out _))
        {
            Tracked.Add(editor, new object());
            editor.TextChanged += (_, _) => SetText(editor, editor.Text);
        }

        var value = change.GetNewValue<string?>();
        if (!string.Equals(editor.Text, value, StringComparison.Ordinal))
        {
            editor.Text = value;
        }
    }
}
