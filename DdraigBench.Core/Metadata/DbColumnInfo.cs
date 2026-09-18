// DdraigBench — 列元数据描述

namespace DdraigBench.Core.Metadata;

public sealed record DbColumnInfo(
    string Name,
    string DataType,
    bool IsPrimary,
    bool IsNullable,
    string? Comment = null);
