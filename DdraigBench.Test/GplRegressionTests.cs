// DdraigBench — GPL 防回归（静态断言 + 驱动集真测试）

using System.Reflection;
using FreeSql;

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

    [Fact]
    public void MySql_provider_uses_MySqlConnector_driver()
    {
        // DataType.MySql 应只由 MySqlConnector 提供驱动（Provider.MySql / MySql.Data 是 GPL-2.0，禁用）
        Assert.True(CanLoadAssembly("MySqlConnector"), "应存在 MySqlConnector 驱动程序集");
        Assert.False(CanLoadAssembly("MySql.Data"), "不得出现 MySql.Data（GPL-2.0）");
        Assert.False(CanLoadAssembly("FreeSql.Provider.MySql"), "不得出现 FreeSql.Provider.MySql");
    }

    [Fact]
    public void PostgreSql_provider_uses_Npgsql_driver()
    {
        Assert.True(CanLoadAssembly("Npgsql"), "应存在 Npgsql 驱动程序集");
        Assert.True(CanLoadAssembly("FreeSql.Provider.PostgreSQL"), "应存在 PostgreSQL Provider 程序集");
    }

    [Fact]
    public void MySql_and_Sqlite_data_types_are_registered()
    {
        // 两个方言都能建出 IFreeSql（不连接），证明 Provider 已随程序集注册
        using var mysql = new FreeSqlBuilder()
            .UseConnectionString(DataType.MySql, "Server=127.0.0.1;Port=1;Database=none;Uid=none;Pwd=none")
            .Build();
        using var postgres = new FreeSqlBuilder()
            .UseConnectionString(
                DataType.PostgreSQL, "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")
            .Build();

        Assert.Equal(DataType.MySql, mysql.Ado.DataType);
        Assert.Equal(DataType.PostgreSQL, postgres.Ado.DataType);
    }

    private static bool CanLoadAssembly(string simpleName)
    {
        try
        {
            Assembly.Load(simpleName);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
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
