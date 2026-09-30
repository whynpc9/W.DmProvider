using System.IO;
using System.Numerics;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class NumericCodecTests
{
    [Theory]
    [InlineData("0.1", new byte[] { 192, 11 })]
    [InlineData("-0.1", new byte[] { 63, 91, 102 })]
    [InlineData("0.01", new byte[] { 192, 2 })]
    [InlineData("-0.01", new byte[] { 63, 100, 102 })]
    [InlineData("1.23", new byte[] { 193, 2, 24 })]
    [InlineData("-1.23", new byte[] { 62, 100, 78, 102 })]
    public void FixedBase100VectorsEncodeAndDecodeExactly(string text, byte[] expected)
    {
        var decimalValue = DmDecimal.Parse(text);
        Assert.Equal(expected, DmNumericCodec.EncodeDecimal(decimalValue));
        Assert.Equal(text, DmNumericCodec.DecodeDecimal(expected).ToString());
    }

    [Fact]
    public void PositiveSmallExponentHeadersRemainValid()
    {
        Assert.Equal("0.1", DmNumericCodec.DecodeDecimal([192, 11]).ToString());
        Assert.Equal(new DmDecimal(BigInteger.One, 128).ToString(),
            DmNumericCodec.DecodeDecimal([129, 2]).ToString());
    }

    [Theory]
    [InlineData(new byte[] { 193, 0 })]
    [InlineData(new byte[] { 62, 0, 102 })]
    [InlineData(new byte[] { 62, 100, 102, 100 })]
    [InlineData(new byte[] { 128, 2 })]
    public void MalformedWireIsRejected(byte[] wire)
        => Assert.Throws<InvalidDataException>(() => DmNumericCodec.DecodeDecimal(wire));

    [Fact]
    public void WireLongerThanTwentyOneBytesIsRejected()
        => Assert.Throws<InvalidDataException>(() => DmNumericCodec.DecodeDecimal(new byte[22]));

    [Fact]
    public void DmDecimalPreservesCoefficientScaleAndTrailingZeros()
    {
        var value = DmDecimal.Parse("-1.2300");
        Assert.Equal(new BigInteger(-12300), value.Coefficient);
        Assert.Equal(4, value.Scale);
        Assert.Equal("-1.2300", value.ToString());
        Assert.Equal(-1.23m, value.ToDecimalExact());
    }

    [Fact]
    public void ExactClrDecimalReductionRemovesOnlyZeroDigits()
    {
        Assert.Equal(0.1m, DmDecimal.Parse("0.10000000000000000000000000000").ToDecimalExact());
        Assert.Equal(decimal.MaxValue,
            DmDecimal.Parse("79228162514264337593543950335.0").ToDecimalExact());
        Assert.Throws<OverflowException>(() =>
            DmDecimal.Parse("0.00000000000000000000000000001").ToDecimalExact());
        Assert.Throws<OverflowException>(() =>
            DmDecimal.Parse("79228162514264337593543950336").ToDecimalExact());
    }

    [Fact]
    public void UnknownScaleUsesSignificantDigitsWhileExplicitScaleRetainsDeclaration()
    {
        var value = DmDecimal.Parse("1230.0");
        Assert.NotEmpty(DmNumericCodec.EncodeDecimal(value, precision: 4, scale: -1));
        Assert.Throws<OverflowException>(() =>
            DmNumericCodec.EncodeDecimal(value, precision: 4, scale: 1));
    }

    private enum SignedCode : int { Negative = -10, Positive = 7, Large = 1000 }
    private enum ByteCode : byte { Maximum = byte.MaxValue }
    private enum ULongCode : ulong { Maximum = ulong.MaxValue }

    [Fact]
    public void EnumUnderlyingValuesAreNotNameOrdinals()
    {
        Assert.Equal(-10, DmNumericInput.ToEnumUnderlying(SignedCode.Negative));
        Assert.Equal(7, DmNumericInput.ToEnumUnderlying(SignedCode.Positive));
        Assert.Equal(1000, DmNumericInput.ToEnumUnderlying(SignedCode.Large));
        Assert.Equal(byte.MaxValue, DmNumericInput.ToEnumUnderlying(ByteCode.Maximum));
        Assert.Equal(ulong.MaxValue, DmNumericInput.ToEnumUnderlying(ULongCode.Maximum));
    }

    [Fact]
    public void IntegerConversionRejectsFractionsAndOverflow()
    {
        Assert.Equal(new BigInteger(ulong.MaxValue),
            DmNumericInput.ToIntegerExact(ULongCode.Maximum, BigInteger.Zero, ulong.MaxValue));
        Assert.Throws<InvalidCastException>(() =>
            DmNumericInput.ToIntegerExact(1.5m, int.MinValue, int.MaxValue));
        Assert.Throws<OverflowException>(() =>
            DmNumericInput.ToIntegerExact(ulong.MaxValue, int.MinValue, int.MaxValue));
    }

    [Fact]
    public void NonFiniteFloatingInputIsRejected()
    {
        Assert.Throws<OverflowException>(() => DmNumericInput.ValidateFiniteFloating(double.NaN));
        Assert.Throws<OverflowException>(() => DmNumericInput.ValidateFiniteFloating(double.PositiveInfinity));
    }
}
