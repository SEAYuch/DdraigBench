// DdraigBench — 表分区元数据描述

namespace DdraigBench.Core.Metadata;

/// <summary>表级分区子结构（不是库级 DbObjectKind 对象集合）。</summary>
public sealed record DbPartitionInfo(
    string Name,
    string Method,
    string Expression,
    string? Description);
