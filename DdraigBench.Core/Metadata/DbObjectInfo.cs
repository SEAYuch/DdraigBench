// DdraigBench — 元数据对象描述

namespace DdraigBench.Core.Metadata;

public sealed record DbObjectInfo(
    string Name,
    string? Schema = null,
    string? Comment = null);
