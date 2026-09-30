// DdraigBench — PostgreSQL 方言

namespace DdraigBench.Core.Dialects;

public sealed class PostgreSqlDialect : SqlDialectBase
{
    public static readonly PostgreSqlDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    protected override string FormatBinary(byte[] bytes) => $"'\\x{Convert.ToHexString(bytes)}'::bytea";
}
