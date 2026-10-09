// DdraigBench — MySQL / MariaDB 方言

namespace DdraigBench.Core.Dialects;

public sealed class MySqlDialect : SqlDialectBase
{
    public static readonly MySqlDialect Instance = new();

    public override string QuoteIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    /// <summary>
    /// MySQL 默认 sql_mode 下反斜杠是转义符（<c>NO_BACKSLASH_ESCAPES</c> 未开启），
    /// 故必须先把 <c>\</c> 翻倍再翻倍单引号——否则含反斜杠的数据（Windows 路径、JSON 里的 <c>\n</c>）
    /// 会在导出 INSERT 与目录查询的库名过滤里被改写。
    /// </summary>
    protected override string Quote(string text) =>
        $"'{(text ?? string.Empty).Replace("\\", "\\\\").Replace("'", "''")}'";

    // MySQL 列定义顺序：NOT NULL → AUTO_INCREMENT → KEY
    public override string IdentityClause => "AUTO_INCREMENT";

    public override string BuildIdentityColumn(string quotedName, string type, bool nullable) =>
        nullable
            ? $"{quotedName} {type} AUTO_INCREMENT PRIMARY KEY"
            : $"{quotedName} {type} NOT NULL AUTO_INCREMENT PRIMARY KEY";

    protected override string FormatBinary(byte[] bytes) => $"X'{Convert.ToHexString(bytes)}'";
}
