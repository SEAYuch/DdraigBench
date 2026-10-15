// DdraigBench — SQL Server 方言

using System.Globalization;

namespace DdraigBench.Core.Dialects;

/// <summary>SQL Server 标识符、分页与字面量语法（FreeSql Provider.SqlServer 配套）。</summary>
public sealed class SqlServerDialect : SqlDialectBase
{
    public static readonly SqlServerDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>
    /// SQL Server OFFSET/FETCH applies directly to the user's top-level ordered query. PagedSql must not
    /// wrap it as a derived table: that rejects bare ORDER BY or changes its semantics.
    /// </summary>
    public override bool SupportsServerPaging => true;

    public override bool RequiresTopLevelOrderByForPaging => true;

    /// <summary>T-SQL 的 ORDER BY 不接受输出列序号（<c>ORDER BY 1</c> 被当常量表达式，不排序）。</summary>
    public override bool SupportsOrdinalOrderBy => false;

    public override string BuildPaging(int limit, long offset) =>
        $"OFFSET {offset.ToString(CultureInfo.InvariantCulture)} ROWS " +
        $"FETCH NEXT {limit.ToString(CultureInfo.InvariantCulture)} ROWS ONLY";

    public override string FormatLiteral(object? value) => value is bool boolean
        ? boolean ? "1" : "0"
        : base.FormatLiteral(value);

    public override string IdentityClause => "IDENTITY(1,1)";

    public override string BuildIdentityColumn(string quotedName, string type, bool nullable) =>
        nullable
            ? $"{quotedName} {type} IDENTITY(1,1) PRIMARY KEY"
            : $"{quotedName} {type} IDENTITY(1,1) NOT NULL PRIMARY KEY";

    protected override string FormatBinary(byte[] bytes) => $"0x{Convert.ToHexString(bytes)}";
}
