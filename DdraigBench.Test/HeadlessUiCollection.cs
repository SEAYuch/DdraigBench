// DdraigBench — 把所有需要 headless UI 线程的测试收进同一个 xUnit 集合
//
// 为什么必须串行：整个测试程序集共用**一条** UI 线程（`TestAppFixture` 的 `ddb-headless-ui`）
// 和一个 `Application` 实例。不同测试类默认属于不同 collection，会并行跑：
//   · `VisualSmokeTests` 会改全局 `Application.RequestedThemeVariant` 并驱动渲染计时器；
//   · `ActivationBridgeTests` / `WindowTests` 会建自己的 `Window` 与 `TreeViewItem`；
// 两者交错时（尤其开了 Skia 真渲染后）会互相踩——实测整包并行时
// `CaptureRenderedFrame()` 返回 null、`TreeViewItem` 抛
// 「The calling thread cannot access this object because a different thread owns it.」，
// 而单独跑这些类全绿。同集合的测试**永不并行**，正是这里要的语义。
//
// 只影响这个集合内部：其它 collection（Core / 集成测）仍照常并行。

namespace DdraigBench.Test;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessUiCollection
{
    public const string Name = "headless-ui";
}
