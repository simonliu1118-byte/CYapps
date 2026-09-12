using System.Globalization;
using System.Numerics;

namespace CYInvoice.Core;

/// <summary>Exact signed fixed-point value with seven decimal places.</summary>
public readonly record struct FixedDecimal(long ScaledValue)
{
    public const long Scale = 10_000_000;

    public static FixedDecimal Parse(string text)
    {
        var value = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
        if (value.Length == 0)
        {
            throw new FormatException("數值不可空白");
        }

        var negative = false;
        if (value[0] is '-' or '+')
        {
            negative = value[0] == '-';
            value = value[1..];
        }

        var parts = value.Split('.');
        if (parts.Length > 2 || (parts[0].Length == 0 && (parts.Length == 1 || parts[1].Length == 0)))
        {
            throw new FormatException("數值格式錯誤");
        }

        var whole = parts[0].Length == 0 ? "0" : parts[0];
        if (!DigitsOnly(whole))
        {
            throw new FormatException("數值格式錯誤");
        }

        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if (parts.Length == 2 && (fraction.Length == 0 || fraction.Length > 7 || !DigitsOnly(fraction)))
        {
            throw new FormatException("最多只能輸入小數點後 7 位");
        }

        fraction = fraction.PadRight(7, '0');
        var scaled = BigInteger.Parse(whole + fraction, CultureInfo.InvariantCulture);
        if (negative)
        {
            scaled = -scaled;
        }

        return new FixedDecimal(ToInt64(scaled));
    }

    public static FixedDecimal FromInt64(long value) => new(ToInt64((BigInteger)value * Scale));

    public static FixedDecimal Add(FixedDecimal left, FixedDecimal right) =>
        new(ToInt64((BigInteger)left.ScaledValue + right.ScaledValue));

    public static FixedDecimal Multiply(FixedDecimal left, FixedDecimal right) =>
        new(RoundedQuotient((BigInteger)left.ScaledValue * right.ScaledValue, Scale));

    public static FixedDecimal Divide(FixedDecimal left, FixedDecimal right)
    {
        if (right.ScaledValue == 0)
        {
            throw new DivideByZeroException("除數不可為零");
        }

        return new FixedDecimal(RoundedQuotient((BigInteger)left.ScaledValue * Scale, right.ScaledValue));
    }

    public static FixedDecimal MultiplyRatio(FixedDecimal value, long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("除數不可為零");
        }

        return new FixedDecimal(RoundedQuotient((BigInteger)value.ScaledValue * numerator, denominator));
    }

    public long RoundInt64() => RoundedQuotient(ScaledValue, Scale);

    public override string ToString()
    {
        var absolute = BigInteger.Abs(ScaledValue).ToString(CultureInfo.InvariantCulture).PadLeft(8, '0');
        var whole = absolute[..^7];
        var fraction = absolute[^7..].TrimEnd('0');
        var result = fraction.Length == 0 ? whole : $"{whole}.{fraction}";
        return ScaledValue < 0 && result != "0" ? "-" + result : result;
    }

    private static long RoundedQuotient(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("除數不可為零");
        }

        var negative = numerator.Sign * denominator.Sign < 0;
        var quotient = BigInteger.DivRem(BigInteger.Abs(numerator), BigInteger.Abs(denominator), out var remainder);
        if (remainder * 2 >= BigInteger.Abs(denominator))
        {
            quotient++;
        }

        return ToInt64(negative ? -quotient : quotient);
    }

    private static long ToInt64(BigInteger value) =>
        value >= long.MinValue && value <= long.MaxValue
            ? (long)value
            : throw new OverflowException("數值超出範圍");

    private static bool DigitsOnly(string value) => value.All(character => character is >= '0' and <= '9');
}
