// DdraigBench — GPL 防回归（静态断言：仓库不引入 GPL/冲突源）

namespace DdraigBench.Test;

public sealed class GplRegressionTests
{
    private static readonly string[] ForbiddenPackageIds =
    {
        "FreeSql.All",
        "FreeSql.Provider.MySql",
        "MySql.Data",
    };

    [Fact]
    public void No_forbidden_package_references_in_any_csproj()
    {
        var csprojs = Directory
            .GetFiles(FindRepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .ToList();

        Assert.NotEmpty(csprojs);

        foreach (var file in csprojs)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in ForbiddenPackageIds)
            {
                Assert.DoesNotContain(
                    $"Include=\"{forbidden}\"", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void No_forbidden_assemblies_in_test_output()
    {
        foreach (var forbidden in new[] { "MySql.Data.dll", "FreeSql.Provider.MySql.dll" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, forbidden);
            Assert.False(File.Exists(path), $"{forbidden} 出现在测试输出目录：{path}");
        }
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DdraigBench.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
