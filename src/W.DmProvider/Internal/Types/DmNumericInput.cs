using System;
using System.Globalization;
using System.Numerics;

namespace W.Dm.Internal.Types;

internal static class DmNumericInput
{
    internal static object ToEnumUnderlying(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Enum number) return value;
        TypeCode code = Type.GetTypeCode(Enum.GetUnderlyingType(number.GetType()));
        return code switch
        {
            TypeCode.SByte => Convert.ToSByte(number, CultureInfo.InvariantCulture),
            TypeCode.Byte => Convert.ToByte(number, CultureInfo.InvariantCulture),
            TypeCode.Int16 => Convert.ToInt16(number, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => Convert.ToUInt16(number, CultureInfo.InvariantCulture),
            TypeCode.Int32 => Convert.ToInt32(number, CultureInfo.InvariantCulture),
            TypeCode.UInt32 => Convert.ToUInt32(number, CultureInfo.InvariantCulture),
            TypeCode.Int64 => Convert.ToInt64(number, CultureInfo.InvariantCulture),
            TypeCode.UInt64 => Convert.ToUInt64(number, CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException("Unsupported enum underlying type.")
        };
    }

    internal static DmDecimal ToExactDecimal(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = ToEnumUnderlying(value);
        return value switch
        {
            DmDecimal number => number,
            DmXDec legacy => DmNumericCodec.DecodeDecimal(legacy.WireValue),
            decimal number => DmDecimal.FromDecimal(number),
            BigInteger number => new DmDecimal(number, 0),
            sbyte number => new DmDecimal(number, 0),
            byte number => new DmDecimal(number, 0),
            short number => new DmDecimal(number, 0),
            ushort number => new DmDecimal(number, 0),
            int number => new DmDecimal(number, 0),
            uint number => new DmDecimal(number, 0),
            long number => new DmDecimal(number, 0),
            ulong number => new DmDecimal(number, 0),
            string text => DmDecimal.Parse(text),
            _ => throw new InvalidCastException("Value is not an exact decimal input.")
        };
    }

    internal static byte[] EncodeDecimalInput(object value, int precision, int scale) =>
        DmNumericCodec.EncodeDecimal(ToExactDecimal(value), precision, scale);

    internal static byte[] EncodeScaledInt64Input(object value, int precision, int scale) =>
        DmNumericCodec.EncodeScaledInt64(ToExactDecimal(value), precision, scale);

    internal static BigInteger ToIntegerExact(object value, BigInteger minimum, BigInteger maximum)
    {
        if (minimum > maximum) throw new ArgumentException("Invalid integer range.");
        ArgumentNullException.ThrowIfNull(value);
        value = ToEnumUnderlying(value);
        BigInteger integer = value switch
        {
            float number when float.IsFinite(number) && MathF.Truncate(number) == number => new BigInteger(number),
            double number when double.IsFinite(number) && Math.Truncate(number) == number => new BigInteger(number),
            float or double => throw new InvalidCastException("Floating input is not a finite integer."),
            _ => ToExactDecimal(value).ToBigIntegerExact()
        };
        if (integer < minimum || integer > maximum) throw new OverflowException("Integer exceeds target range.");
        return integer;
    }

    internal static object ValidateFiniteFloating(object value) => value switch
    {
        float number when float.IsFinite(number) => number,
        double number when double.IsFinite(number) => number,
        float or double => throw new OverflowException("Non-finite floating values are unsupported."),
        _ => throw new InvalidCastException("Value is not a floating input.")
    };
}
