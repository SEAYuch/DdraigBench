// DdraigBench — 视觉冒烟：真实窗口的像素级回归 + 截图入库
//
// 为什么存在：README「已知限制」第 6 条长期写着"UI 视觉验收为人工项——开发环境无显示器，截图未入库"。
// 这里把它变成自动化项：headless + Skia 能**真的拿到像素**（`UseHeadlessDrawing = false` +
// `.UseSkia()`，见 TestAppFixture），于是「渲染出来是什么颜色」「强调按钮文字还看不看得见」
// 「浅/深主题是不是两套配色」都能断言，截图也随测试重新生成并入库。

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench.Test;

/// <summary>
/// 真窗口渲染回归。全部集中在**一个**测试类里：它们共用 headless UI 线程与
/// <see cref="Application.RequestedThemeVariant"/>，跨类并行会互相换主题。
/// </summary>
[Collection(HeadlessUiCollection.Name)]
public sealed class VisualSmokeTests
{
    private const int DesignedWidth = 1180;
    private const int DesignedHeight = 760;

    [Fact]
    public void Workbench_renders_a_real_frame_at_its_designed_size() => TestUiThread.Run(() =>
    {
        var frame = Render(ThemeVariant.Light);

        Assert.Equal(DesignedWidth, frame.Width);
        Assert.Equal(DesignedHeight, frame.Height);

        // "渲染成功"的最低证据：整幅不是单一颜色。空窗口只会剩背景色，
        // 真实工作台有工具栏、边框、文字抗锯齿，实测浅色 800+ 种。
        Assert.True(frame.DistinctColors > 100,
            $"疑似没渲染出内容：整幅只有 {frame.DistinctColors} 种颜色");
    });

    [Fact]
    public void Workbench_uses_the_declared_theme_surfaces() => TestUiThread.Run(() =>
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var frame = Render(variant);

            // 工具栏面板（顶栏 Border，Background=WorkbenchPanelBrush）。
            // y=3 落在 Border 的 padding 带里（内容 StackPanel 有 Margin 8,6 与 34 高按钮），
            // 不会被某个命令按钮自己的浅灰底盖住。
            AssertSurface(frame, variant, "WorkbenchPanelBrush", 600, 3);

            // 工作区之外的 DockPanel 底色（Background=WorkbenchBackgroundBrush）。
            // x=278 落在导航器 Border（…281 含描边）与工作区 Border（282 起）之间那几像素的
            // 缝隙里——实测两套主题下都恰好是背景色，往右一点就被工作区面板的白色盖住。
            AssertSurface(frame, variant, "WorkbenchBackgroundBrush", 278, 400);
        }
    });

    /// <summary>
    /// 两套主题必须真的给出两套配色。
    /// <para>
    /// **采样点要覆盖不只一块表面**：只读 (2,2) 落在顶部工具栏（<c>WorkbenchPanelBrush</c>），
    /// 把 <c>WorkbenchBackgroundBrush</c> 的深色值改成浅色值也照样绿——那不是"两套配色"，
    /// 是量错了地方（2026-10-16 实测踩到）。这里同时量工具栏与工作区外的 DockPanel 底色。
    /// </para>
    /// </summary>
    [Fact]
    public void Light_and_dark_are_two_different_palettes() => TestUiThread.Run(() =>
    {
        var light = Render(ThemeVariant.Light);
        var dark = Render(ThemeVariant.Dark);

        // (2,2)：顶部工具栏面板；(278,400)：工作区之外的 DockPanel 底色。
        foreach (var (x, y) in new[] { (2, 2), (278, 400) })
        {
            Assert.NotEqual(light.ColorAt(x, y), dark.ColorAt(x, y));

            // 深色底应明显暗于浅色底——否则"切主题"可能只是换了个名字
            Assert.True(Luminance(dark.ColorAt(x, y)) < Luminance(light.ColorAt(x, y)) - 0.5,
                $"深色主题在 ({x},{y}) 没变暗：light={light.ColorAt(x, y)} dark={dark.ColorAt(x, y)}");
        }
    });

    /// <summary>
    /// A2 的**端到端**版本：`App.axaml` 曾有一条无条件设前景色的隐式 `TextBlock` 样式，
    /// 把强调按钮里的白字压成正文深色（浅色主题深蓝底深灰字 ≈2.5:1、深色主题浅青底浅灰字 ≈1.6:1）。
    /// `ThemeContrastTests` 从资源与继承链两路守着，这里再从**真实像素**上守一次。
    /// <para>
    /// **必须量按钮内容 TextBlock 自己的矩形，不能量整个按钮**：按钮里还有一个白色 Play 图标，
    /// 它在缺陷下依然是白的——按"整个按钮里对比度最大的颜色"取会把图标当成文字而稳稳漏过
    /// （2026-10-16 实测：注入缺陷后整按钮口径仍报 6.28:1，而文字实际只有 2.49:1）。
    /// </para>
    /// </summary>
    [Fact]
    public void Accent_button_text_stays_readable_in_the_real_render() => TestUiThread.Run(() =>
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            using var context = new Workbench();
            context.Apply(variant);

            var accent = context.Window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("accent"));
            var label = accent.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()
                        ?? throw new InvalidOperationException("强调按钮里找不到内容 TextBlock");
            var origin = label.TranslatePoint(default, context.Window)
                         ?? throw new InvalidOperationException("强调按钮的文字不在可视树上");

            var frame = context.CaptureStable();
            var rect = new PixelRect(
                (int)origin.X,
                (int)origin.Y,
                (int)Math.Ceiling(label.Bounds.Width),
                (int)Math.Ceiling(label.Bounds.Height));

            var (fill, text) = frame.FillAndInk(rect);
            var ratio = Contrast(fill, text);

            Assert.True(ratio >= 4.5,
                $"{variant} 强调按钮文字对比度只有 {ratio:F2}:1（底色 {fill}、文字 {text}）" +
                "——A2 的缺陷形态是隐式 TextBlock 样式压掉了按钮前景色");
        }
    });

    /// <summary>
    /// 截图入库：README 的「UI 视觉验收为人工项」至此结束。每次跑测试都会重新生成
    /// <c>docs/screenshots/</c> 下的两张整窗截图，人可以直接打开看，也可与上一版对比。
    /// </summary>
    [Fact]
    public void Workbench_screenshots_are_regenerated_for_review() => TestUiThread.Run(() =>
    {
        var directory = Path.Combine(RepoRoot(), "docs", "screenshots");
        Directory.CreateDirectory(directory);

        foreach (var (name, variant) in new[]
                 {
                     ("workbench-light.png", ThemeVariant.Light),
                     ("workbench-dark.png", ThemeVariant.Dark),
                 })
        {
            var path = Path.Combine(directory, name);
            RenderTo(variant, path);

            Assert.True(new FileInfo(path).Length > 5_000,
                $"{name} 太小，可能渲染成了空窗口");
        }
    });

    // ---- 渲染脚手架 -------------------------------------------------------

    private sealed class Workbench : IDisposable
    {
        public Workbench()
        {
            ViewModel = new MainViewModel(
                new NullConnectionDialog(),
                new FreeSqlFactory(),
                new FreeSqlMetadataExplorer(),
                new StubFilePicker(),
                new StubExportDialog(),
                new StubDdlViewer(),
                new StubConnectionStoreFactory(new StubConnectionStore()));

            Window = new MainWindow { DataContext = ViewModel };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public MainWindow Window { get; }

        private MainViewModel ViewModel { get; }

        public void Apply(ThemeVariant variant)
        {
            Application.Current!.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// 连拍到相邻两帧完全一致为止。headless + Skia 的首帧**不保证**已经画全
        /// （实测偶见状态栏还是未初始化内存的颜色），所以不能"点一下就拿"。
        /// </summary>
        public Frame CaptureStable()
        {
            Frame? previous = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);

                var bitmap = Window.CaptureRenderedFrame()
                             ?? throw new InvalidOperationException("headless 渲染没有产出帧");
                using (bitmap)
                {
                    var frame = Frame.From(bitmap);
                    if (previous is not null && previous.SamePixels(frame))
                    {
                        return frame;
                    }

                    previous = frame;
                }
            }

            return previous ?? throw new InvalidOperationException("headless 渲染没有产出帧");
        }

        public void Dispose() => Window.Close();
    }

    private static Frame Render(ThemeVariant variant)
    {
        using var context = new Workbench();
        context.Apply(variant);
        return context.CaptureStable();
    }

    private static void RenderTo(ThemeVariant variant, string path)
    {
        using var context = new Workbench();
        context.Apply(variant);
        context.CaptureStable();

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        using var bitmap = context.Window.CaptureRenderedFrame()
                           ?? throw new InvalidOperationException("headless 渲染没有产出帧");
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private static void AssertSurface(Frame frame, ThemeVariant variant, string resourceKey, int x, int y)
    {
        Assert.True(Application.Current!.TryFindResource(resourceKey, variant, out var value),
            $"主题 {variant} 里找不到资源 {resourceKey}");
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;

        Assert.True(expected == frame.ColorAt(x, y),
            $"({x},{y}) 在 {variant} 下是 {frame.ColorAt(x, y)}，期望 {resourceKey} = {expected}");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DdraigBench.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（DdraigBench.slnx）");
    }

    // ---- 像素工具 ---------------------------------------------------------

    /// <summary>一帧的 BGRA 像素（headless 默认 <c>Bgra8888</c>）。</summary>
    private sealed record Frame(int Width, int Height, int Stride, byte[] Bgra)
    {
        public static Frame From(Bitmap bitmap)
        {
            using var copy = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96));
            using var buffer = copy.Lock();
            bitmap.CopyPixels(buffer);

            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            return new Frame(buffer.Size.Width, buffer.Size.Height, buffer.RowBytes, bytes);
        }

        public int DistinctColors
        {
            get
            {
                var seen = new HashSet<uint>();
                for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    seen.Add(BitConverter.ToUInt32(Bgra, y * Stride + x * 4));
                }

                return seen.Count;
            }
        }

        public Color ColorAt(int x, int y)
        {
            var offset = y * Stride + x * 4;
            return Color.FromRgb(Bgra[offset + 2], Bgra[offset + 1], Bgra[offset]);
        }

        public bool SamePixels(Frame other) => Bgra.AsSpan().SequenceEqual(other.Bgra);

        /// <summary>
        /// 取矩形内的**底色**（出现最多的颜色）与**文字墨色**。
        ///
        /// 墨色取"与底色亮度差最大"的常见颜色，而不是"最亮"的：浅色主题下强调按钮是
        /// 深蓝底白字（文字更亮），深色主题下是浅青底黑字（文字更暗）——只认"最亮"会在
        /// 后一种情形里把底色自己当成文字（对比度恒为 1:1，实测踩到）。
        /// 要求出现 ≥3 次，免得单个抗锯齿噪点被当成字。
        /// </summary>
        public (Color Fill, Color Ink) FillAndInk(PixelRect rect)
        {
            var tally = new Dictionary<uint, int>();
            for (var y = rect.Y + 2; y < rect.Bottom - 2; y++)
            for (var x = rect.X + 2; x < rect.Right - 2; x++)
            {
                var key = BitConverter.ToUInt32(Bgra, y * Stride + x * 4);
                tally[key] = tally.GetValueOrDefault(key) + 1;
            }

            Assert.NotEmpty(tally);

            var fillKey = tally.MaxBy(pair => pair.Value).Key;
            var fill = FromKey(fillKey);
            var fillLuminance = Luminance(fill);

            var ink = tally.Where(pair => pair.Key != fillKey && pair.Value >= 3)
                .Select(pair => FromKey(pair.Key))
                .OrderByDescending(color => Math.Abs(Luminance(color) - fillLuminance))
                .First();

            return (fill, ink);
        }

        private static Color FromKey(uint value) =>
            Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>WCAG 2.x 相对亮度对比度（与 <c>ThemeContrastTests</c> 同一套公式）。</summary>
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

    private sealed class NullConnectionDialog : IConnectionDialog
    {
        public Task<ConnectionProfile?> ShowAsync(ConnectionProfile? existing = null) =>
            Task.FromResult<ConnectionProfile?>(null);
    }
}
