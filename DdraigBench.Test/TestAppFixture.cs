// DdraigBench — headless 测试宿主（xUnit v3 assembly fixture：执行期初始化，不在发现期）

using Avalonia;
using Avalonia.Headless;

[assembly: Xunit.AssemblyFixture(typeof(DdraigBench.Test.TestAppFixture))]

namespace DdraigBench.Test;

/// <summary>
/// 一次性启动 headless Avalonia（供 UI 相关单测构造真实控件）。
/// 用 <see cref="Xunit.AssemblyFixtureAttribute"/> 而非 <c>[ModuleInitializer]</c>：
/// 后者在发现期做重初始化会挂死 MTP discovery（Pathfinder1eHelper 同款做法）。
/// </summary>
public sealed class TestAppFixture
{
    public TestAppFixture()
    {
        AppBuilder.Configure<global::DdraigBench.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }
}
