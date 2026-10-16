// DdraigBench — headless 测试宿主（xUnit v3 assembly fixture：执行期初始化，不在发现期）

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

[assembly: Xunit.AssemblyFixture(typeof(DdraigBench.Test.TestAppFixture))]

namespace DdraigBench.Test;

/// <summary>
/// 一次性启动 headless Avalonia，并**把 UI 线程显式交给我们自己**。
///
/// 平台在专用线程上初始化（<see cref="Dispatcher.UIThread"/> 因此归那条线程，Compositor 亦然），
/// 随后就在该线程上跑消息循环；测试侧统一用 <see cref="TestUiThread"/> 编组过去。
///
/// 为什么必须这样：先前平台是在 assembly fixture 的线程上初始化的，而 xunit 的测试体跑在线程池线程上，
/// 于是 `TextEditor` 构造时 `VerifyAccess` 抛 `InvalidOperationException`，
/// `new Window()` / `TopLevel` 也在 `Compositor.RegisterForSerialization` 处抛同一异常
/// ——「需要 UI 线程的控件测不了」这笔测试债的根因正是"UI 线程没人跑循环"。
///
/// 用 <see cref="Xunit.AssemblyFixtureAttribute"/> 而非 <c>[ModuleInitializer]</c>：
/// 后者在发现期做重初始化会挂死 MTP discovery（Pathfinder1eHelper 同款做法）。
/// </summary>
public sealed class TestAppFixture : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _uiThread;

    public TestAppFixture()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<global::DdraigBench.App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }

            ready.SetResult();

            // 消息循环：TestUiThread 投过来的委托在这里被执行
            Dispatcher.UIThread.MainLoop(_shutdown.Token);
        })
        {
            IsBackground = true,
            Name = "ddb-headless-ui",
        };

        _uiThread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _uiThread.Join(TimeSpan.FromSeconds(10));
        _shutdown.Dispose();
    }
}
