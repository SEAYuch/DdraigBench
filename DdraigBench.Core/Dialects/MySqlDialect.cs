// DdraigBench — MySQL / MariaDB 方言

namespace DdraigBench.Core.Dialects;

public sealed class MySqlDialect : SqlDialectBase
{
    public static readonly MySqlDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    // MySQL 列定义顺序：NOT NULL → AUTO_INCREMENT → KEY
    public override string IdentityClause => "AUTO_INCREMENT";

    public override string BuildIdentityColumn(string quotedName, string type, bool nullable) =>
        nullable
            ? $"{quotedName} {type} AUTO_INCREMENT PRIMARY KEY"
            : $"{quotedName} {type} NOT NULL AUTO_INCREMENT PRIMARY KEY";

    protected override string FormatBinary(byte[] bytes) => $"X'{Convert.ToHexString(bytes)}'";
}
