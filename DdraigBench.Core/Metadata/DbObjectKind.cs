// DdraigBench — 元数据对象种类

namespace DdraigBench.Core.Metadata;

public enum DbObjectKind
{
    Database,

    /// <summary>
    /// 模式（M5-2 追加）。只有 PostgreSQL 这类「库内含模式」的方言才有这一层：
    /// MySQL 的库等价于模式、SQLite 无模式，两者在 <c>GetObjectsAsync</c> 里返回空集，树也就不加这一层。
    /// </summary>
    Schema,

    Table,
    View,
    Column,
    Index,
    ForeignKey,
    StoredProcedure,
    Trigger,
    Function,
}
