using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class ExtendedTemporalPrecisionTests
{
    private const string VerifiedServer = "8.1.5.60";

    private static DateTime Timestamp => new DateTime(2026, 7, 23, 14, 15, 16, DateTimeKind.Unspecified).AddTicks(1);
    private static DateTimeOffset OffsetTimestamp(int minutes) => new DateTimeOffset(2026, 7, 23, 14, 15, 16,
        TimeSpan.FromMinutes(minutes)).AddTicks(1);

    [Fact]
    public void VerifiedExtendedTimestampMatchesOfficialNineByteGoldenAndExactTick()
    {
        // Official 8.3.1.47463 net9 asset, T12 temporal-round2/seed.json.
        // 0xC8 in byte 5 holds 100ns; the final zero bytes are not a zero fraction.
        DateTime normalized = Assert.IsType<DateTime>(DmTemporalCodec.NormalizeForWire(Timestamp, 26, 7, VerifiedServer));
        byte[] wire = null!;
        DmDateTime.DmdtEncodeFast2(ref wire, normalized);
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), wire);
        DateTime decoded = new DmDateTime(DmDateTime.DmTimeFromRec4(wire, 26), 9).GetTimestamp();
        Assert.Equal(Timestamp.Ticks, decoded.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, decoded.Kind);
    }

    [Theory]
    [InlineData(0, "EA87BBEE81C80000000000")]
    [InlineData(480, "EA87BBEE81C8000000E001")]
    [InlineData(-300, "EA87BBEE81C8000000D4FE")]
    [InlineData(330, "EA87BBEE81C80000004A01")]
    public void VerifiedExtendedOffsetMatchesOfficialElevenByteGoldenAndOriginalOffset(int minutes, string golden)
    {
        // Frozen O pure encoders and O public writes -> W cross-read on 8.1.5.60.
        DateTimeOffset expected = OffsetTimestamp(minutes);
        var normalized = Assert.IsType<DateTimeOffset>(DmTemporalCodec.NormalizeForWire(expected, 27, 7, VerifiedServer));
        byte[] wire = null!;
        DmDateTime.Dmdt2TzEncodeFast2(ref wire, normalized.DateTime, (short)normalized.Offset.TotalMinutes);
        Assert.Equal(Convert.FromHexString(golden), wire);
        DateTimeOffset decoded = new DmDateTime(DmDateTime.DmTimeFromRec4(wire, 27), 11).GetTimestampTZ();
        Assert.Equal(expected.Ticks, decoded.Ticks);
        Assert.Equal(expected.Offset, decoded.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8.1.5.59")]
    [InlineData("8.1.5.61")]
    [InlineData("8.1.5.60 ")]
    public void UnknownOrDifferentServerProfileStillRejectsSevenDigits(string? serverVersion)
    {
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 26, 7, serverVersion));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 27, 7, serverVersion));
    }

    [Fact]
    public void LegacySixDigitTypesStillRejectSeventhTickOnVerifiedServer()
    {
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 16, 6, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 23, 6, VerifiedServer));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 16, 7, VerifiedServer));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 23, 7, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 26, 6, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 27, 6, VerifiedServer));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void VerifiedProfileDoesNotOpenSubTickInputPrecision(int scale)
    {
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 26, scale, VerifiedServer));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 27, scale, VerifiedServer));
    }

    [Fact]
    public void VerifiedProfileDoesNotAllowCrossTimezoneTypesOrUnrepresentableOffsets()
    {
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 27, 7, VerifiedServer));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(480), 26, 7, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(OffsetTimestamp(-780), 27, 7, VerifiedServer));
    }

    [Fact]
    public void ExistingDateTimeAndIntervalBoundariesRemainStrict()
    {
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(Timestamp, 14, 0, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(TimeOnly.FromDateTime(Timestamp), 15, 6, VerifiedServer));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(TimeSpan.FromTicks(1), 21, 6, VerifiedServer));
    }
}
