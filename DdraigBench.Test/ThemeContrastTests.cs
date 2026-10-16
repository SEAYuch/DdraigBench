// DdraigBench — 强调按钮文字色回归（A2：应用级隐式 TextBlock 前景样式）

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DdraigBench.Test;

/// <summary>
/// A2 回归：应用级隐式样式若直接落在 <see cref="TextBlock"/> 上（<c>Style Selector="TextBlock"</c> 设 <c>Foreground</c>），
/// 会盖掉强调按钮自身的前景色，浅色主题下变成「蓝底深灰字」（#1B2430 on #155A9C，对比度约 2.2:1，2026-10-11 实测）。
/// 正文前景色必须设在 <c>Window</c> 上随继承链下发——按钮的 <c>Foreground</c> 在链上更近，自然胜出。
/// <para>
/// 这里断言样式表与继承机制本身：<c>MainWindow</c> 级的实例化验证在 headless 下做不了——
/// <c>new Window()</c> 会碰 Composition，其 dispatcher 归属 assembly fixture 线程，测试线程构造即
/// <c>InvalidOperationException</c>（即 AGENTS.md §7 记录的线程归属限制）。
/// </para>
/// </summary>
[Collection(HeadlessUiCollection.Name)]
public sealed class ThemeContrastTests
{
    [Fact]
    public void Application_styles_do_not_paint_TextBlock_foreground_directly() => TestUiThread.Run(() =>
    {
        var offenders = Application.Current!.Styles.OfType<Style>()
            .Where(style => style.Selector?.ToString() == "TextBlock")
            .Where(style => style.Setters.OfType<Setter>()
                .Any(setter => setter.Property == TextBlock.ForegroundProperty))
            .ToList();

        Assert.Empty(offenders);
    });

    [Fact]
    public void Window_style_carries_the_workbench_text_foreground() => TestUiThread.Run(() =>
    {
        var windowStyle = Assert.Single(
            Application.Current!.Styles.OfType<Style>(),
            style => style.Selector?.ToString() == "Window");

        Assert.Contains(windowStyle.Setters.OfType<Setter>(),
            setter => setter.Property == Window.ForegroundProperty);
    });

    /// <summary>
    /// 上一条修复成立的前提：按钮的 Foreground 能沿逻辑树继承到其内容里的 TextBlock。
    /// 该继承链一旦被上游改动打断，Window 上的正文色就传不到正文、强调按钮也会重新露出正文色，
    /// 而上面两条样式表断言仍是绿的——所以这条要单独守着（不需要 TopLevel，避免 Composition）。
    /// </summary>
    [Fact]
    public void Button_foreground_is_inherited_by_its_content_text() => TestUiThread.Run(() =>
    {
        var label = new TextBlock { Text = "运行" };
        var button = new Button { Foreground = Brushes.White, Content = label };
        Assert.Same(button, label.Parent);

        Assert.Equal(ColorOf(button.Foreground), ColorOf(label.Foreground));
    });

    [Fact]
    public void Accent_text_on_the_accent_surface_meets_wcag() => TestUiThread.Run(() =>
    {
        var surface = ColorOfBrush("WorkbenchAccentBrush");
        var accentText = ColorOfBrush("WorkbenchAccentTextBrush");
        var bodyText = ColorOfBrush("WorkbenchTextBrush");

        Assert.True(Contrast(accentText, surface) >= 4.5,
            $"强调按钮文字对比度不足：{accentText} on {surface}；" +
            $"正文色压上去只有 {Contrast(bodyText, surface):F2}:1（A2 的缺陷形态）");
    });

    private static Color ColorOfBrush(string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, ThemeVariant.Light, out var value),
            $"浅色主题里找不到资源 {key}");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color ColorOf(IBrush? brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    /// <summary>WCAG 2.x 相对亮度对比度。</summary>
    private static double Contrast(Color first, Color second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second));
        if (lighter < darker)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
