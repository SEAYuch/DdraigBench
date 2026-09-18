// DdraigBench — 测试用临时 SQLite 库

using FreeSql;

namespace DdraigBench.Test;

public sealed class TempSqliteDatabase : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"ddraigbench-{Guid.NewGuid():N}.db");

    public string FilePath => _path;

    public string ConnectionString => $"Data Source={_path};Pooling=False";

    public IFreeSql CreateFsql() =>
        new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, ConnectionString)
            .Build();

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
    }
}
