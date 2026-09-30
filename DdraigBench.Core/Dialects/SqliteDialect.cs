// DdraigBench — SQLite 方言

namespace DdraigBench.Core.Dialects;

public sealed class SqliteDialect : SqlDialectBase
{
    public static readonly SqliteDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    // SQLite 列约束：PRIMARY KEY [ASC|DESC] [冲突子句] [AUTOINCREMENT] —— AUTOINCREMENT 只能跟在 PRIMARY KEY 之后
    public override string IdentityClause => "AUTOINCREMENT";

    public override string BuildIdentityColumn(string quotedName, string type, bool nullable) =>
        $"{quotedName} {type} PRIMARY KEY AUTOINCREMENT";

    protected override string FormatBinary(byte[] bytes) => $"X'{Convert.ToHexString(bytes)}'";
}
