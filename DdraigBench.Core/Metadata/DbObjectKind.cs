// DdraigBench — 元数据对象种类

namespace DdraigBench.Core.Metadata;

public enum DbObjectKind
{
    Database,
    Table,
    View,
    Column,
    Index,
    ForeignKey,
    StoredProcedure,
    Trigger,
    Function,
}
