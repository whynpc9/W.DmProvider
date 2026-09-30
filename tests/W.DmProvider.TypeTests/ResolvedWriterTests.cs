using System.Runtime.CompilerServices;
using System.Text;
using W.Dm;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class ResolvedWriterTests
{
    private enum UnsignedCode : ulong { Maximum = ulong.MaxValue }

    [Fact]
    public void ResolvedCharacterWireUsesUnsignedUnderlyingTextAndKeepsAdvisoryFlag()
    {
        foreach (object value in new object[] { ulong.MaxValue, UnsignedCode.Maximum })
        {
            var parameter = Advisory(2);
            var encoded = Bind(parameter, value, 2);
            Assert.Equal("18446744073709551615", Encoding.UTF8.GetString(encoded.GetInValue()));
            Assert.Equal(2, encoded.GetSqlType());
            Assert.Equal((byte)2, parameter.GetTypeFlag());
        }
    }

    [Fact]
    public void ResolvedSingleWireEncodesExactDoubleInFourBytes()
    {
        var parameter = Advisory(10);
        var encoded = Bind(parameter, 1.5d, 10);
        Assert.Equal(4, encoded.GetInValue().Length);
        Assert.Equal(1.5f, BitConverter.ToSingle(encoded.GetInValue()));
        Assert.Equal(10, encoded.GetSqlType());
        Assert.Equal((byte)2, parameter.GetTypeFlag());
    }

    [Theory]
    [InlineData(0.1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolvedSingleRejectsLossyOrNonfiniteDouble(double value)
        => Assert.Throws<OverflowException>(() => Bind(Advisory(10), value, 10));

    [Fact]
    public void ResolvedBinaryDoesNotPadToAdvisoryCharacterPrecision()
    {
        var parameter = Advisory(15);
        byte[] bytes = [0, 1, 255];
        var encoded = Bind(parameter, bytes, 17);
        Assert.Equal(bytes, encoded.GetInValue());
        Assert.Equal(17, encoded.GetSqlType());
        Assert.Equal(3, encoded.GetPrec());
        Assert.Equal((byte)2, parameter.GetTypeFlag());
    }

    [Fact]
    public void ResolvedTypedNullRetainsTheResolvedIntegerWireType()
    {
        var parameter = Advisory(15);
        var encoded = Bind(parameter, DBNull.Value, 7);
        Assert.True(encoded.GetIsInDataNull());
        Assert.Equal(7, encoded.GetSqlType());
        Assert.Equal((byte)2, parameter.GetTypeFlag());
    }

    private static DmParamValue Bind(DmParameterInternal parameter, object value, int cType)
    {
        var result = new DmParamValue();
        new DmSetValue("UTF-8").SetResolvedObject(result, value, null!, string.Empty, cType, parameter);
        return result;
    }

    private static DmParameterInternal Advisory(int describedType)
    {
        // This fixture exercises metadata and codecs only; no physical connection is constructed.
        var parameter = (DmParameterInternal)RuntimeHelpers.GetUninitializedObject(typeof(DmParameterInternal));
        parameter.SetTypeFlag(2);
        parameter.SetCType(describedType);
        parameter.SetPrecision(64);
        parameter.SetScale(0);
        return parameter;
    }
}
