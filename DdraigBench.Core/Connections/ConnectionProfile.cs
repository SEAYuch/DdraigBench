// DdraigBench — 连接定义

using FreeSql;

namespace DdraigBench.Core.Connections;

public sealed record ConnectionProfile(
    string Name,
    DataType DataType,
    string ConnectionString);
