// DdraigBench — 元数据对象种类

namespace DdraigBench.Core.Metadata;

public enum DbObjectKind
{
    Database,

    /// <summary>
    /// 模式（M5-2 追加，M5-3 增 SQL Server）。PostgreSQL / SQL Server 的库内含模式；
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

    /// <summary>
    /// 计划事件（M5-3 追加）。目前只有 MySQL / MariaDB 有这类对象
    /// （<c>information_schema.EVENTS</c>，由事件调度器按时间表执行）；其余方言返回空集，树也就不加这一组。
    /// </summary>
    Event,

    /// <summary>表节点下的分区子节点（M5-3；不是容器级对象集合，不能从 GetObjectsAsync 查询）。</summary>
    Partition,
}
