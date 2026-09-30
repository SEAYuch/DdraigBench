// DdraigBench — FreeSqlConnectionTester 单测

using DdraigBench.Core.Connections;
using FreeSql;

namespace DdraigBench.Test;

public sealed class ConnectionTesterTests
{
    [Fact]
    public async Task Sqlite_temp_file_reports_success()
    {
        using var db = new TempSqliteDatabase();
        var profile = new ConnectionProfile("db", DataType.Sqlite, db.ConnectionString);

        var failure = await new FreeSqlConnectionTester().TestAsync(profile);

        Assert.Null(failure);
    }

    [Fact]
    public async Task Unreachable_server_reports_failure_message()
    {
        var profile = new ConnectionProfile(
            "bad",
            DataType.MySql,
            "Server=127.0.0.1;Port=1;Database=none;Uid=none;Pwd=none;Pooling=false");

        var failure = await new FreeSqlConnectionTester().TestAsync(profile);

        Assert.NotNull(failure);
    }
}
