using System;
using System.Globalization;
using System.Numerics;

namespace W.Dm;

/// <summary>An exact decimal value represented by an integer coefficient and a nonnegative scale.</summary>
public readonly struct DmDecimal
{
    private static readonly BigInteger MaxClrCoefficient = (BigInteger.One << 96) - 1;
    internal const int MaxScale = 1024;

    public BigInteger Coefficient { get; }
    public int Scale { get; }

    public DmDecimal(BigInteger coefficient, int scale)
    {
        if (scale < 0 || scale > MaxScale) throw new ArgumentOutOfRangeException(nameof(scale));
        Coefficient = coefficient;
        Scale = scale;
    }

    public static DmDecimal FromDecimal(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger coefficient = (uint)bits[0] |
            ((BigInteger)(uint)bits[1] << 32) |
            ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) coefficient = -coefficient;
        return new DmDecimal(coefficient, (bits[3] >> 16) & 0xff);
    }

    public static DmDecimal Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReadOnlySpan<char> value = text.AsSpan().Trim();
        if (value.IsEmpty || value.Length > 4096) throw new FormatException("Invalid decimal text.");
        int offset = 0;
        bool negative = false;
        if (value[offset] is '+' or '-') { negative = value[offset++] == '-'; }
        BigInteger coefficient = BigInteger.Zero;
        int digits = 0, scale = 0;
        bool dot = false;
        while (offset < value.Length)
        {
            char c = value[offset];
            if (c == '.' && !dot) { dot = true; offset++; continue; }
            if (c is 'e' or 'E') break;
            if (c < '0' || c > '9') throw new FormatException("Invalid decimal text.");
            coefficient = coefficient * 10 + (c - '0');
            digits++;
            if (dot) scale++;
            offset++;
        }
        if (digits == 0) throw new FormatException("Invalid decimal text.");
        if (offset < value.Length)
        {
            offset++;
            if (offset == value.Length) throw new FormatException("Invalid decimal exponent.");
            bool expNegative = false;
            if (value[offset] is '+' or '-') { expNegative = value[offset++] == '-'; }
            if (offset == value.Length) throw new FormatException("Invalid decimal exponent.");
            int exponent = 0;
            for (; offset < value.Length; offset++)
            {
                char c = value[offset];
                if (c < '0' || c > '9') throw new FormatException("Invalid decimal exponent.");
                exponent = checked(exponent * 10 + c - '0');
                if (exponent > MaxScale) throw new OverflowException("Decimal exponent exceeds the supported range.");
            }
            scale += expNegative ? exponent : -exponent;
        }
        if (scale < 0)
        {
            coefficient *= BigInteger.Pow(10, -scale);
            scale = 0;
        }
        if (negative) coefficient = -coefficient;
        return new DmDecimal(coefficient, scale);
    }

    public static bool TryParse(string text, out DmDecimal value)
    {
        try { value = Parse(text); return true; }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException or ArgumentNullException)
        { value = default; return false; }
    }

    public DmDecimal RescaleExact(int scale)
    {
        if (scale < 0 || scale > MaxScale) throw new ArgumentOutOfRangeException(nameof(scale));
        if (scale == Scale) return this;
        if (scale > Scale) return new DmDecimal(Coefficient * BigInteger.Pow(10, scale - Scale), scale);
        BigInteger quotient = BigInteger.DivRem(Coefficient, BigInteger.Pow(10, Scale - scale), out BigInteger remainder);
        if (!remainder.IsZero) throw new OverflowException("Decimal scale would discard nonzero digits.");
        return new DmDecimal(quotient, scale);
    }

    public BigInteger ToBigIntegerExact()
    {
        BigInteger quotient = BigInteger.DivRem(Coefficient, BigInteger.Pow(10, Scale), out BigInteger remainder);
        if (!remainder.IsZero) throw new InvalidCastException("Decimal value is not an integer.");
        return quotient;
    }

    public decimal ToDecimalExact()
    {
        BigInteger coefficient = Coefficient;
        int scale = Scale;
        while (scale > 0 && (scale > 28 || BigInteger.Abs(coefficient) > MaxClrCoefficient) && coefficient % 10 == 0)
        { coefficient /= 10; scale--; }
        if (scale > 28 || BigInteger.Abs(coefficient) > MaxClrCoefficient)
            throw new OverflowException("Decimal value cannot be represented exactly by System.Decimal.");
        bool negative = coefficient.Sign < 0;
        BigInteger magnitude = BigInteger.Abs(coefficient);
        int low = unchecked((int)(uint)(magnitude & uint.MaxValue));
        int mid = unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue));
        int high = unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue));
        return new decimal(low, mid, high, negative, (byte)scale);
    }

    public override string ToString()
    {
        string digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
        string sign = Coefficient.Sign < 0 ? "-" : string.Empty;
        if (Scale == 0) return sign + digits;
        if (digits.Length <= Scale) return sign + "0." + new string('0', Scale - digits.Length) + digits;
        return sign + digits.Insert(digits.Length - Scale, ".");
    }
}
