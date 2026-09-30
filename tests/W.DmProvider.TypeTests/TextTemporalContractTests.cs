using System.Text;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TextTemporalContractTests
{
    [Fact]
    public void Utf8KeepsSupplementaryAndCombiningCharacters()
    {
        const string value = "T09-中文-😀-e\u0301";
        var bytes = DmTextCodec.EncodeStrict(value, "utf-8");
        Assert.Equal(Encoding.UTF8.GetByteCount(value), bytes.Length);
        Assert.Equal(value, DmTextCodec.DecodeStrict(bytes, 0, bytes.Length, "utf-8"));
    }

    [Fact]
    public void Gb18030KeepsRepresentableChineseText()
    {
        const string value = "达梦中文";
        var bytes = DmTextCodec.EncodeStrict(value, "gb18030");
        Assert.Equal(value, DmTextCodec.DecodeStrict(bytes, 0, bytes.Length, "gb18030"));
    }

    [Fact]
    public void UnpairedSurrogateAndUnmappableCharactersRejectWithoutReplacement()
    {
        Assert.Throws<EncoderFallbackException>(() => DmTextCodec.EncodeStrict("\uD800", "utf-8"));
        Assert.Throws<EncoderFallbackException>(() => DmTextCodec.EncodeStrict("😀", "BIG5"));
        Assert.Throws<DecoderFallbackException>(() => DmTextCodec.DecodeStrict([0xC0, 0xAF], 0, 2, "utf-8"));
    }

    [Fact]
    public void LegacyTimeAndTimestampRejectDiscardedSeventhDigit()
    {
        var time = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234567));
        var timestamp = new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1234567);
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(time, 15, 6));
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(timestamp, 16, 6));
    }

    [Fact]
    public void TimezoneAwareTimestampRejectsDiscardedTickOrUnsupportedSeventhDigit()
    {
        var value = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567);
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(value, 23, 6));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(value, 27, 7));
        Assert.Throws<NotSupportedException>(() => DmTemporalCodec.NormalizeForWire(value, 16, 6));
    }

    [Fact]
    public void RepresentableTemporalValuesPreserveKindOffsetAndNegativeInterval()
    {
        var date = new DateOnly(2024, 2, 29);
        var normalizedDate = Assert.IsType<DateTime>(DmTemporalCodec.NormalizeForWire(date, 14, 0));
        Assert.Equal(DateTimeKind.Unspecified, normalizedDate.Kind);
        Assert.Equal(new DateTime(2024, 2, 29), normalizedDate);

        var timestamp = new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1234560);
        Assert.Equal(timestamp, DmTemporalCodec.NormalizeForWire(timestamp, 16, 6));
        var offset = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5)).AddTicks(1234560);
        Assert.Equal(offset, DmTemporalCodec.NormalizeForWire(offset, 23, 6));
        var interval = -(TimeSpan.FromDays(2) + TimeSpan.FromHours(3) + TimeSpan.FromTicks(1234560));
        Assert.Equal(interval, DmTemporalCodec.NormalizeForWire(interval, 21, 6));
    }

    [Fact]
    public void DateCannotSilentlyDropATimeComponent()
    {
        var value = new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified);
        Assert.Throws<OverflowException>(() => DmTemporalCodec.NormalizeForWire(value, 14, 0));
    }

    private const int DayToSecondMicroseconds = (DmIntervalDT.QUA_DHMS << 8) | (9 << 4) | 6;

    [Theory]
    [InlineData(10L)]
    [InlineData(-10L)]
    public void IntervalPreservesPositiveAndNegativeOneMicrosecond(long ticks)
    {
        var value = TimeSpan.FromTicks(ticks);
        Assert.Equal(value, new DmIntervalDT(value, DayToSecondMicroseconds).ToTimeSpanExact());
    }

    [Fact]
    public void IntervalPreservesNegativeMultidayFraction()
    {
        var value = -(TimeSpan.FromDays(2) + TimeSpan.FromHours(3) +
            TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(1234560));
        Assert.Equal(value, new DmIntervalDT(value, DayToSecondMicroseconds).ToTimeSpanExact());
    }

    [Theory]
    [InlineData(1000L, 4, "00.0001")]
    [InlineData(10000L, 3, "00.001")]
    [InlineData(1000000L, 4, "00.1000")]
    public void IntervalFractionTextPreservesLeadingAndTrailingZeros(long ticks, int scale, string fraction)
    {
        var interval = new DmIntervalDT(TimeSpan.FromTicks(ticks),
            (DmIntervalDT.QUA_DHMS << 8) | (9 << 4) | scale);
        Assert.Contains(":" + fraction + "'", interval.ToString(), StringComparison.Ordinal);
        Assert.Equal(ticks, interval.ToTimeSpanExact().Ticks);
    }

    [Fact]
    public void IntervalRejectsTimeSpanMinValueAtMicrosecondScale()
        => Assert.Throws<OverflowException>(() =>
            new DmIntervalDT(TimeSpan.MinValue, DayToSecondMicroseconds).ToTimeSpanExact());
}
