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

        var lines = new List<(string Definition, string? Comment)>();
        foreach (var column in table.Columns)
        {
            lines.Add(ColumnLine(column, inlinePrimaryKey));
        }

        if (inlinePrimaryKey is null && primaryKeys.Count > 0)
        {
            var name = Dialect.QuoteIdentifier($"PK_{table.Name}");
            lines.Add(($"  CONSTRAINT {name} PRIMARY KEY ({ColumnList(primaryKeys)})", null));
        }

        foreach (var (name, foreign) in table.ForeignsDict)
        {
            lines.Add((ForeignKeyLine(name, foreign), null));
        }

        sb.Append("CREATE TABLE ").Append(target).Append(" (\n")
            .Append(RenderBody(lines))
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

    /// <summary>
    /// 拼装列/约束清单。**逗号必须写在注释之前**：`-- ` 是行注释，会一直吃到行尾，
    /// 若先写注释再补逗号，逗号会被吞掉 → 任何"非最后一列带列注释"的 CREATE TABLE 都是语法错误。
    /// 故逐行按「定义 → 逗号 → 注释 → 换行」的顺序输出（最后一列不带逗号、不带尾换行）。
    /// </summary>
    private static string RenderBody(IReadOnlyList<(string Definition, string? Comment)> lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var (definition, comment) = lines[i];
            var separator = i < lines.Count - 1;

            sb.Append(definition);
            if (separator)
            {
                sb.Append(',');
            }

            if (comment is not null)
            {
                sb.Append(" -- ").Append(comment);
            }

            if (separator)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private (string Definition, string? Comment) ColumnLine(FsColumn column, FsColumn? inlinePrimaryKey)
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

    /// <summary>
    /// 追加列尾的 DEFAULT 与列注释。注释**不**直接拼进定义串——由 <see cref="RenderBody"/>
    /// 负责放在逗号之后，否则行注释会吞掉逗号（见 H4）。
    /// </summary>
    private static (string Definition, string? Comment) AppendTail(string definition, FsColumn column)
    {
        var withDefault = string.IsNullOrWhiteSpace(column.DefaultValue)
            ? definition
            : definition + " DEFAULT " + column.DefaultValue;

        return (withDefault, string.IsNullOrWhiteSpace(column.Comment) ? null : column.Comment);
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
