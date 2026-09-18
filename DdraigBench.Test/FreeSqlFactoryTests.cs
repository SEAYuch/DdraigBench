// DdraigBench — FreeSqlFactory 单测

using DdraigBench.Core.Connections;
using DdraigBench.Core.Querying;
using FreeSql;

namespace DdraigBench.Test;

public sealed class FreeSqlFactoryTests
{
    [Fact]
    public void GetOrCreate_reuses_instance_for_equal_profile()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var first = new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString);
        var second = new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString);

        Assert.Same(factory.GetOrCreate(first), factory.GetOrCreate(second));
    }

    [Fact]
    public void GetOrCreate_creates_distinct_instances_for_distinct_profiles()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();

        var first = factory.GetOrCreate(new ConnectionProfile("a", DataType.Sqlite, db.ConnectionString));
        var second = factory.GetOrCreate(new ConnectionProfile("b", DataType.Sqlite, db.ConnectionString));

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Created_instance_executes_select_1()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var fsql = factory.GetOrCreate(new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString));
        var runner = new QueryRunner(fsql);

        var result = await runner.ExecuteAsync("SELECT 1 AS n", ct: TestContext.Current.CancellationToken);

        Assert.Null(result.Error);
        Assert.Equal(1, Convert.ToInt32(Assert.Single(result.Rows)[0]));
    }

    [Fact]
    public void Remove_drops_cached_instance()
    {
        using var db = new TempSqliteDatabase();
        using var factory = new FreeSqlFactory();
        var profile = new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString);

        var first = factory.GetOrCreate(profile);
        factory.Remove(profile);

        Assert.NotSame(first, factory.GetOrCreate(profile));
    }
}
