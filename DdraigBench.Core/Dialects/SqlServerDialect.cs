// DdraigBench — SQL Server 方言

using System.Globalization;

namespace DdraigBench.Core.Dialects;

/// <summary>SQL Server 标识符、分页与字面量语法（FreeSql Provider.SqlServer 配套）。</summary>
public sealed class SqlServerDialect : SqlDialectBase
{
    public static readonly SqlServerDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>
    /// PagedSql wraps the user statement as a derived table. SQL Server rejects a bare ORDER BY inside
    /// that derived table unless it is paired with TOP/OFFSET/FOR XML, and stripping it would alter semantics.
    /// Keep automatic server paging disabled until a T-SQL-aware wrapper is implemented.
    /// </summary>
    public override bool SupportsServerPaging => false;

    public override string BuildPaging(int limit, long offset) =>
        $"ORDER BY (SELECT 1) OFFSET {offset.ToString(CultureInfo.InvariantCulture)} ROWS " +
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
