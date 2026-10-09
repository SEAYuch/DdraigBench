// DdraigBench — 左侧树的统一元数据 API

using DdraigBench.Core.Sessions;

namespace DdraigBench.Core.Metadata;

public interface IMetadataExplorer
{
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbSession session, CancellationToken ct = default);

    // Table/View ← IDbFirst；Schema/Trigger/StoredProcedure/Function ← 目录查询（M5-1 / M5-2）：
    //   方言没有这类对象时返回「空集」而非报错（如 SQLite 的 Schema/存储过程）；
    //   不是"容器级对象集合"的 kind（Column/Index/ForeignKey/Database）抛 NotSupportedException。
    //   注意 database 参数是"容器"：库级对象传库名，PG 的模式级对象传模式名（见 DbObjectKind.Schema）。
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        DbSession session, string database, DbObjectKind kind, CancellationToken ct = default);

    Task<IReadOnlyList<DbColumnInfo>> GetColumnsAsync(
        DbSession session, string database, string table, CancellationToken ct = default);

    // M4-3 追加：把元数据渲染成 DDL 预览；对象不存在返回 null。
    // 视图/触发器/函数定义 IDbFirst 不返回，生成器只给提示（见 AGENTS.md §6.2）。
    Task<string?> GetDdlAsync(
        DbSession session, string database, string table, CancellationToken ct = default);
}
