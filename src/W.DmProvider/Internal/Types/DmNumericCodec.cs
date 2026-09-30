using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace W.Dm.Internal.Types;

/// <summary>Validated DECIMAL base-100 wire conversion. CType 24 uses scaled Int64 instead.</summary>
internal static class DmNumericCodec
{
    private const int MaxWireLength = 21;
    private const int MaxGroups = 20;
    private const int MaxPrecision = 38;

    internal static byte[] EncodeDecimal(DmDecimal value, int precision = 0, int scale = -1)
    {
        DmDecimal prepared = ApplyDeclaration(value, precision, scale);
        if (prepared.Coefficient.IsZero) return new byte[] { 128 };

        bool negative = prepared.Coefficient.Sign < 0;
        BigInteger unscaled = BigInteger.Abs(prepared.Coefficient);
        if ((prepared.Scale & 1) != 0) unscaled *= 10;
        int fractionalGroups = (prepared.Scale + 1) / 2;
        var groups = new List<int>();
        while (!unscaled.IsZero)
        {
            unscaled = BigInteger.DivRem(unscaled, 100, out BigInteger group);
            groups.Add((int)group);
        }
        groups.Reverse();
        int exponent = groups.Count - 1 - fractionalGroups;
        while (groups.Count > 0 && groups[^1] == 0) groups.RemoveAt(groups.Count - 1);
        if (exponent is < -64 or > 61 || groups.Count is < 1 or > MaxGroups)
            throw new OverflowException("DECIMAL exceeds the verified base-100 wire range.");

        bool terminator = negative && groups.Count < MaxGroups;
        var result = new byte[1 + groups.Count + (terminator ? 1 : 0)];
        result[0] = checked((byte)(negative ? 62 - exponent : 193 + exponent));
        for (int i = 0; i < groups.Count; i++)
            result[i + 1] = checked((byte)(negative ? 101 - groups[i] : groups[i] + 1));
        if (terminator) result[^1] = 102;
        return result;
    }

    internal static DmDecimal DecodeDecimal(ReadOnlySpan<byte> wire, int? declaredScale = null)
    {
        if (wire.IsEmpty || wire.Length > MaxWireLength) throw new InvalidDataException("Invalid DECIMAL wire length.");
		if (wire[0] == 128)
        {
            if (wire.Length != 1) throw new InvalidDataException("Invalid zero DECIMAL payload.");
            return declaredScale is { } zeroScale ? new DmDecimal(BigInteger.Zero, zeroScale) : new DmDecimal(BigInteger.Zero, 0);
        }
        bool negative;
        int exponent;
		if (wire[0] is >= 129 and <= 254) { negative = false; exponent = wire[0] - 193; }
        else if (wire[0] is >= 1 and <= 126) { negative = true; exponent = 62 - wire[0]; }
        else throw new InvalidDataException("Invalid DECIMAL sign or exponent.");
        if (exponent is < -64 or > 61) throw new InvalidDataException("DECIMAL exponent exceeds the verified range.");

        BigInteger coefficient = BigInteger.Zero;
        int groupCount = 0;
        for (int i = 1; i < wire.Length; i++)
        {
            int group;
            if (negative && wire[i] == 102)
            {
                if (i != wire.Length - 1) throw new InvalidDataException("DECIMAL terminator is not final.");
                break;
            }
            group = negative ? 101 - wire[i] : wire[i] - 1;
            if (group is < 0 or > 99) throw new InvalidDataException("Invalid DECIMAL base-100 digit.");
            coefficient = coefficient * 100 + group;
            groupCount++;
        }
        if (groupCount is < 1 or > MaxGroups || coefficient.IsZero)
            throw new InvalidDataException("Invalid DECIMAL digit sequence.");
        int lowestPower = exponent - groupCount + 1;
        int scale = lowestPower < 0 ? checked(-2 * lowestPower) : 0;
        if (lowestPower > 0) coefficient *= BigInteger.Pow(100, lowestPower);
        if (negative) coefficient = -coefficient;
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        DmDecimal value = new DmDecimal(coefficient, scale);
        if (declaredScale is { } target)
        {
            try { value = value.RescaleExact(target); }
            catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
            { throw new InvalidDataException("DECIMAL value conflicts with declared scale.", ex); }
        }
        return value;
    }

    internal static byte[] EncodeScaledInt64(DmDecimal value, int precision, int scale)
    {
        DmDecimal prepared = ApplyDeclaration(value, precision, scale);
        if (scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
        BigInteger coefficient = prepared.RescaleExact(scale).Coefficient;
        if (coefficient < long.MinValue || coefficient > long.MaxValue)
            throw new OverflowException("Scaled DEC_INT64 coefficient exceeds Int64.");
        byte[] result = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(result, (long)coefficient);
        return result;
    }

    internal static DmDecimal DecodeScaledInt64(ReadOnlySpan<byte> wire, int scale)
    {
        if (wire.Length != 8) throw new InvalidDataException("DEC_INT64 requires eight wire bytes.");
        return new DmDecimal(BinaryPrimitives.ReadInt64LittleEndian(wire), scale);
    }

    private static DmDecimal ApplyDeclaration(DmDecimal value, int precision, int scale)
    {
        if (precision < 0 || precision > MaxPrecision) throw new ArgumentOutOfRangeException(nameof(precision));
		if (scale < -1 || scale > MaxPrecision || (precision > 0 && scale > precision))
			throw new ArgumentOutOfRangeException(nameof(scale));
		if (scale >= 0) value = value.RescaleExact(scale);
		else
		{
			// An unspecified scale has no trailing-zero requirement. Count the
			// significant coefficient, rather than rejecting 1230.0 as five digits.
			while (value.Scale > 0 && value.Coefficient % 10 == 0)
				value = new DmDecimal(value.Coefficient / 10, value.Scale - 1);
		}
        int digits = BigInteger.Abs(value.Coefficient).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        if (precision > 0 && digits > precision)
            throw new OverflowException("DECIMAL exceeds declared precision.");
        if (digits > MaxPrecision)
            throw new OverflowException("DECIMAL exceeds supported precision.");
        return value;
    }
}
