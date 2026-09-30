// DdraigBench — DDL 预览生成（基于 IDbFirst 元数据 + 方言引用）

using System.Text;
using DdraigBench.Core.Dialects;
using FreeSql.DatabaseModel;
// 本工程自有 Metadata.DbColumnInfo 与 FreeSql 同名；同命名空间成员优先于 using，故用别名规避
using FsColumn = FreeSql.DatabaseModel.DbColumnInfo;

namespace DdraigBench.Core.Metadata;

/// <summary>
/// 把 IDbFirst 的 <see cref="DbTableInfo"/> 渲染成可复制执行的 DDL。
/// 已知取舍：视图/触发器/函数定义 IDbFirst 不返回（见 AGENTS.md §6.2），此处只给提示与列结构。
/// </summary>
public sealed class DdlGenerator(ISqlDialect dialect)
{
    public ISqlDialect Dialect { get; } = dialect;

    public string CreateTable(DbTableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return table.Type == DbTableType.VIEW ? CreateViewPlaceholder(table) : CreateTableScript(table);
    }

    private string CreateTableScript(DbTableInfo table)
    {
        var sb = new StringBuilder();
        var target = QualifiedName(table.Schema, table.Name);

        if (!string.IsNullOrWhiteSpace(table.Comment))
        {
            sb.Append("-- ").Append(table.Comment).Append('\n');
        }

        var primaryKeys = table.Columns.Where(c => c.IsPrimary).ToList();
        var inlinePrimaryKey = primaryKeys.Count == 1 ? primaryKeys[0] : null;

        var lines = new List<string>();
        foreach (var column in table.Columns)
        {
            lines.Add(ColumnLine(column, inlinePrimaryKey));
        }

        if (inlinePrimaryKey is null && primaryKeys.Count > 0)
        {
            var name = Dialect.QuoteIdentifier($"PK_{table.Name}");
            lines.Add($"  CONSTRAINT {name} PRIMARY KEY ({ColumnList(primaryKeys)})");
        }

        foreach (var (name, foreign) in table.ForeignsDict)
        {
            lines.Add(ForeignKeyLine(name, foreign));
        }

        sb.Append("CREATE TABLE ").Append(target).Append(" (\n")
            .Append(string.Join(",\n", lines))
            .Append("\n);\n");

        foreach (var (name, index) in table.IndexesDict)
        {
            // SQLite 的内部自动索引（UNIQUE 约束生成）不可显式创建
            if (name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append('\n').Append(CreateIndexScript(name, index, target));
        }

        return sb.ToString();
    }

    private string ColumnLine(FsColumn column, FsColumn? inlinePrimaryKey)
    {
        if (ReferenceEquals(column, inlinePrimaryKey) && column.IsIdentity)
        {
            var identity = Dialect.BuildIdentityColumn(
                Dialect.QuoteIdentifier(column.Name), TypeOf(column), column.IsNullable);
            return AppendTail("  " + identity, column);
        }

        var sb = new StringBuilder("  ")
            .Append(Dialect.QuoteIdentifier(column.Name))
            .Append(' ').Append(TypeOf(column));

        if (column.IsIdentity)
        {
            sb.Append(' ').Append(Dialect.IdentityClause);
        }

        if (!column.IsNullable)
        {
            sb.Append(" NOT NULL");
        }

        if (ReferenceEquals(column, inlinePrimaryKey))
        {
            sb.Append(" PRIMARY KEY");
        }

        return AppendTail(sb.ToString(), column);
    }

    private string ForeignKeyLine(string name, DbForeignInfo foreign)
    {
        var referenced = foreign.ReferencedTable;
        var target = referenced is null
            ? string.Empty
            : QualifiedName(referenced.Schema, referenced.Name);

        return $"  CONSTRAINT {Dialect.QuoteIdentifier(name)} FOREIGN KEY ({ColumnList(foreign.Columns)})"
            + $" REFERENCES {target} ({ColumnList(foreign.ReferencedColumns)})";
    }

    private string CreateIndexScript(string name, DbIndexInfo index, string target)
    {
        var columns = string.Join(", ", index.Columns.Select(c =>
            $"{Dialect.QuoteIdentifier(c.Column?.Name ?? string.Empty)}{(c.IsDesc ? " DESC" : string.Empty)}"));

        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        return $"CREATE {unique}INDEX {Dialect.QuoteIdentifier(name)} ON {target} ({columns});";
    }

    private string CreateViewPlaceholder(DbTableInfo table)
    {
        var sb = new StringBuilder()
            .Append("-- 视图：").Append(QualifiedName(table.Schema, table.Name)).Append('\n')
            .Append("-- IDbFirst 不返回视图定义（属 AGENTS.md §6.2 缺口），此处仅列结构：\n");

        foreach (var column in table.Columns)
        {
            sb.Append("--   ").Append(column.Name).Append(' ').Append(TypeOf(column)).Append('\n');
        }

        return sb.ToString();
    }

    private string AppendTail(string definition, FsColumn column)
    {
        var sb = new StringBuilder(definition);

        if (!string.IsNullOrWhiteSpace(column.DefaultValue))
        {
            sb.Append(" DEFAULT ").Append(column.DefaultValue);
        }

        if (!string.IsNullOrWhiteSpace(column.Comment))
        {
            sb.Append(" -- ").Append(column.Comment);
        }

        return sb.ToString();
    }

    private string ColumnList(IEnumerable<FsColumn> columns) =>
        string.Join(", ", columns.Select(c => Dialect.QuoteIdentifier(c.Name)));

    private string QualifiedName(string? schema, string name) =>
        string.IsNullOrEmpty(schema)
            ? Dialect.QuoteIdentifier(name)
            : $"{Dialect.QuoteIdentifier(schema)}.{Dialect.QuoteIdentifier(name)}";

    private static string TypeOf(FsColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.DbTypeText))
        {
            return column.DbTypeText;
        }

        return string.IsNullOrWhiteSpace(column.DbTypeTextFull) ? "TEXT" : column.DbTypeTextFull;
    }
}
