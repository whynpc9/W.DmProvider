using System;
using System.Globalization;

namespace W.Dm.Internal.Types;

internal static class DmTemporalCodec
{
    private static readonly long[] TickQuantumByScale =
        { 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1 };

    // The legacy 8/10-byte timestamp and TIME encoders store microseconds.
    // Extended 9/11-byte seven-digit types passed the T12 O-seed/W-read profile
    // on 8.1.5.60; other server versions retain the seven-digit gate.
    internal static object NormalizeForWire(object value, int cType, int scale, string serverVersion = null)
    {
        if (value == null || value is DBNull) return value;
        if (IsText(cType)) return value switch
        {
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            _ => value
        };

        switch (value)
        {
            case DateOnly date:
                if (cType != 14) throw new NotSupportedException("DateOnly requires a DATE parameter.");
                return date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            case TimeOnly time:
                if (cType != 15) throw new NotSupportedException("TimeOnly requires a TIME parameter.");
                ValidateFraction(time.Ticks, scale, 6);
                return DateTime.MinValue.Date.AddTicks(time.Ticks);
            case DateTime dateTime:
                ValidateDateTime(dateTime, cType, scale, serverVersion);
                return dateTime;
			case DateTimeOffset offset:
				if (cType is not (23 or 27))
					throw new NotSupportedException("DateTimeOffset requires a timezone-aware timestamp parameter.");
				if (offset.Offset.TotalMinutes <= -780 || offset.Offset.TotalMinutes > 840)
					throw new OverflowException("Timezone offset is outside the server temporal range.");
				ValidateFraction(offset.Ticks, scale, cType == 23 ? 6 : 7, serverVersion);
                return offset;
            case TimeSpan span:
                if (cType != 21) throw new NotSupportedException("TimeSpan requires INTERVAL DAY TO SECOND.");
                int secondsScale = scale & 0xF;
                ValidateFraction(span.Ticks, secondsScale, 6);
                return span;
            default:
                return value;
        }
    }

    private static void ValidateDateTime(DateTime value, int cType, int scale, string serverVersion)
    {
        switch (cType)
        {
            case 14:
                if (value.TimeOfDay != TimeSpan.Zero)
                    throw new OverflowException("DATE cannot represent the supplied time component.");
                return;
            case 15:
                if (value.Date != DateTime.MinValue.Date)
                    throw new NotSupportedException("Use TimeOnly for a TIME parameter without a date component.");
                ValidateFraction(value.Ticks, scale, 6);
                return;
            case 16:
                ValidateFraction(value.Ticks, scale, 6);
                return;
            case 26:
                ValidateFraction(value.Ticks, scale, 7, serverVersion);
                return;
            default:
                throw new NotSupportedException("DateTime requires a non-timezone DATE, TIME, or TIMESTAMP parameter.");
        }
    }

    private static void ValidateFraction(long ticks, int encodedScale, int wireMaxScale, string serverVersion = null)
    {
        int digits = encodedScale > 9 ? encodedScale & 0xF : encodedScale;
        if (digits < 0 || digits > wireMaxScale || digits > 7)
            throw new NotSupportedException("Temporal fractional precision is unsupported by this wire type.");
        if (digits == 7 && wireMaxScale == 7 && !string.Equals(serverVersion, "8.1.5.60", StringComparison.Ordinal))
            throw new NotSupportedException("Seven-digit timestamp precision is not verified for this server profile.");
        if (ticks % TickQuantumByScale[digits] != 0)
            throw new OverflowException("Temporal value cannot be represented at the declared scale.");
    }

    internal static void ValidateWireNanoseconds(int nanoseconds, int encodedScale)
    {
        int digits = encodedScale > 9 ? encodedScale & 0xF : encodedScale;
        if (digits < 0 || digits > 9 || nanoseconds < 0 || nanoseconds >= 1_000_000_000)
            throw new OverflowException("Server temporal fraction is outside the declared range.");
        int quantum = 1;
        for (int index = digits; index < 9; index++) quantum = checked(quantum * 10);
        if (nanoseconds % quantum != 0)
            throw new OverflowException("Server temporal fraction exceeds the declared scale.");
    }

    private static bool IsText(int cType) => cType is 0 or 1 or 2 or 19 or 54;
}
