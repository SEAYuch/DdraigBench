// DdraigBench — 连接参数（按 Provider 结构化的输入）

using FreeSql;

namespace DdraigBench.Core.Connections;

public sealed record ConnectionParameters(
    DataType DataType,
    string? FilePath = null,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? User = null,
    string? Password = null);
