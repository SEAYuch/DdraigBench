// DdraigBench — 结果集列类型推断与排序比较（纯逻辑，无 UI 依赖）

using System.Globalization;

namespace DdraigBench.Core.Querying;

/// <summary>
/// 结果集某一列的<b>运行时</b>类型。注意 <see cref="QueryResult"/> 只有列名、没有类型元数据
/// （§4 的契约：永远返回无类型结果），故类型只能按已读回的值推断。
/// </summary>
public enum ResultCellKind
{
    /// <summary>兜底：字符串、GUID、JSON 等一切无法归类的值，以及混排的列。</summary>
    Text,

    Number,

    DateTime,

    Boolean,

    /// <summary>二进制列（BLOB → <c>byte[]</c>）。</summary>
    Binary,
}

/// <summary>
/// 结果网格的"列类型感知"底座：按样本推断列类型、按类型比较两个单元格。
/// 排序只作用于<b>当前页已读回的行</b>（服务端分页下我们手里没有整表），
/// 这正是 Kinds 站在 Core 里的原因——它只认识 <see cref="QueryResult"/> 的原始值。
/// </summary>
public static class ResultCellKinds
{
    /// <summary>
    /// 推断某一列的运行时类型：全列都是 null（或没有样本行）时按 <see cref="ResultCellKind.Text"/> 处理；
    /// 出现两种以上类型就退回文本（宁可按字符串比，也不要"有时按数值比、有时按文本比"）。
    /// </summary>
    public static ResultCellKind Infer(IReadOnlyList<object?[]> rows, int index)
    {
        ResultCellKind? kind = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var value = Cell(rows[i], index);
            if (value is null)
            {
                continue;
            }

            var current = Classify(value);
            if (kind is null)
            {
                kind = current;
            }
            else if (kind != current)
            {
                return ResultCellKind.Text;
            }
        }

        return kind ?? ResultCellKind.Text;
    }

    /// <summary>单个值的类型归类。</summary>
    public static ResultCellKind Classify(object value) => value switch
    {
        bool => ResultCellKind.Boolean,
        byte[] => ResultCellKind.Binary,
        DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan => ResultCellKind.DateTime,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => ResultCellKind.Number,
        _ => ResultCellKind.Text,
    };

    /// <summary>
    /// 单元格比较。<b>null 永远排在最后</b>（升序降序都一样）：把 null 当成"最小值"再取反，
    /// 会让降序时空值冲到最前面，那是用户最不想看到的。
    /// 数值按数值比（"10" &gt; "9"，不是字符串比较），日期按时间点比，其余按序数文本比。
    /// </summary>
    public static int Compare(object? left, object? right, bool descending = false)
    {
        if (left is null || right is null)
        {
            if (left is null && right is null)
            {
                return 0;
            }

            return left is null ? 1 : -1;
        }

        var result = CompareValues(left, right);
        return descending ? -result : result;
    }

    /// <summary>
    /// 返回按某列排序后的<b>新</b>列表；相同值的行保持原有相对顺序（稳定排序，
    /// 否则每次点同一列都可能把同分行的次序打乱）。
    /// </summary>
    public static List<object?[]> Sorted(IReadOnlyList<object?[]> rows, int index, bool descending)
    {
        var indexed = new List<(object?[] Row, int Order)>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            indexed.Add((rows[i], i));
        }

        indexed.Sort((a, b) =>
        {
            var result = Compare(Cell(a.Row, index), Cell(b.Row, index), descending);
            return result != 0 ? result : a.Order.CompareTo(b.Order);
        });

        var sorted = new List<object?[]>(indexed.Count);
        foreach (var (row, _) in indexed)
        {
            sorted.Add(row);
        }

        return sorted;
    }

    /// <summary>取第 <paramref name="index"/> 个单元格；行短于列数（不该发生）时当 null。</summary>
    public static object? Cell(object?[]? row, int index) =>
        row is null || index < 0 || index >= row.Length ? null : row[index];

    private static int CompareValues(object left, object right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return CompareBytes(leftBytes, rightBytes);
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            // ulong 可能超出 decimal 范围，溢出时退回 double（精度换不抛异常）
            try
            {
                return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
            }
            catch (OverflowException)
            {
                return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDouble(right, CultureInfo.InvariantCulture));
            }
        }

        if (IsTemporal(left) && IsTemporal(right))
        {
            return TemporalTicks(left).CompareTo(TemporalTicks(right));
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool.CompareTo(rightBool);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        var common = Math.Min(left.Length, right.Length);
        for (var i = 0; i < common; i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static bool IsNumeric(object value) => value is byte or sbyte or short or ushort
        or int or uint or long or ulong or float or double or decimal;

    private static bool IsTemporal(object value) => value is DateTime or DateTimeOffset
        or DateOnly or TimeOnly or TimeSpan;

    private static long TemporalTicks(object value) => value switch
    {
        DateTime dt => dt.Ticks,
        DateTimeOffset dto => dto.UtcTicks,
        DateOnly d => d.DayNumber,
        TimeOnly t => t.Ticks,
        TimeSpan ts => ts.Ticks,
        _ => 0,
    };
}
