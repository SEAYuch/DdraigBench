// DdraigBench — 把测试代码编组到 headless UI 线程上执行

using Avalonia.Threading;

namespace DdraigBench.Test;

/// <summary>
/// headless UI 线程（<see cref="TestAppFixture"/> 启动的那条）的访问入口。
///
/// 需要 Dispatcher 的控件——`TextEditor`（AvaloniaEdit）与 `Window` / `TopLevel`（Composition）——
/// 必须在它上面构造与操作；直接在线程池线程上 new 会 `VerifyAccess` 抛
/// <c>InvalidOperationException</c>。所有编组都走这里，测试体本身仍跑在 xunit 的线程上。
/// </summary>
public static class TestUiThread
{
    public static Dispatcher Dispatcher => Dispatcher.UIThread;

    public static bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <summary>同步编组（已在 UI 线程时直接内联执行，不会自锁）。</summary>
    public static void Run(Action action) => Dispatcher.UIThread.Invoke(action);

    /// <summary>同步编组并取回返回值。</summary>
    public static T Run<T>(Func<T> func) => Dispatcher.UIThread.Invoke(func);

    /// <summary>异步编组。</summary>
    public static Task RunAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    /// <summary>异步编组并取回返回值。</summary>
    public static Task<T> RunAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
}
