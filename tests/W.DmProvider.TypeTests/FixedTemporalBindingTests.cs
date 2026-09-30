using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class FixedTemporalBindingTests
{
    [Theory]
    [InlineData(16, 26)]
    [InlineData(26, 16)]
    [InlineData(23, 27)]
    [InlineData(27, 23)]
    public void LegacyAndExtendedTimestampWireTypesAreCompatibleOnlyWithinTheirFamily(int selected, int server)
        => Assert.True(Compatible(selected, server));

    [Theory]
    [InlineData(16, 23)]
    [InlineData(16, 27)]
    [InlineData(26, 23)]
    [InlineData(26, 27)]
    [InlineData(23, 16)]
    [InlineData(23, 26)]
    [InlineData(27, 16)]
    [InlineData(27, 26)]
    [InlineData(14, 16)]
    [InlineData(14, 26)]
    [InlineData(15, 16)]
    [InlineData(15, 26)]
    [InlineData(16, 14)]
    [InlineData(26, 14)]
    [InlineData(16, 15)]
    [InlineData(26, 15)]
    [InlineData(22, 23)]
    [InlineData(22, 27)]
    public void DateTimeAndTimezoneFamiliesRemainDistinct(int selected, int server)
        => Assert.False(Compatible(selected, server));

    [Theory]
    [InlineData(DmDbType.DateTime, 26, 9)]
    [InlineData(DmDbType.DateTimeOffset, 27, 11)]
    public void SelectingACompatibleFixedTimestampRetainsTheServersTypeAndPrecision(
        DmDbType selected, int server, int precision)
    {
        var metadata = Metadata(server, precision, flag: 1);
        Select(metadata, selected);
        Assert.Equal(server, metadata.GetCType());
        Assert.Equal(precision, metadata.GetPrecision());
        Assert.Equal(7, metadata.GetScale());
        Assert.Equal((byte)1, metadata.GetTypeFlag());
    }

    [Theory]
    [InlineData(DmDbType.DateTime, 27)]
    [InlineData(DmDbType.DateTimeOffset, 26)]
    [InlineData(DmDbType.Date, 26)]
    [InlineData(DmDbType.Time, 26)]
    public void ConflictingFixedTemporalMetadataRejectsBeforeItCanBeRebound(DmDbType selected, int server)
    {
        var metadata = Metadata(server, 11, flag: 1);
        var wrapper = Assert.Throws<TargetInvocationException>(() => Select(metadata, selected));
        Assert.IsType<InvalidOperationException>(wrapper.InnerException);
        Assert.Equal(server, metadata.GetCType());
        Assert.Equal(7, metadata.GetScale());
        Assert.Equal((byte)1, metadata.GetTypeFlag());
    }

    [Theory]
    [InlineData(DmDbType.DateTime, 26, 16)]
    [InlineData(DmDbType.DateTimeOffset, 27, 23)]
    public void AdvisoryMetadataStillUsesTheSelectedClientType(DmDbType selected, int server, int client)
    {
        var metadata = Metadata(server, 11, flag: 2);
        Select(metadata, selected);
        Assert.Equal(client, metadata.GetCType());
        Assert.Equal(6, metadata.GetScale());
        Assert.Equal((byte)2, metadata.GetTypeFlag());
    }

    private static bool Compatible(int selected, int server)
        => (bool)typeof(DmCommand).GetMethod("CompatibleFixedServerType", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [selected, server])!;

    private static void Select(DmParameterInternal metadata, DmDbType selected)
        => typeof(DmCommand).GetMethod("SelectBoundCType", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [metadata, selected, DmParameterTypeSource.ExplicitDbType]);

    private static DmParameterInternal Metadata(int server, int precision, byte flag)
    {
        // Only metadata is accessed. No socket, connection or parameter-value collection is created.
        var value = (DmParameterInternal)RuntimeHelpers.GetUninitializedObject(typeof(DmParameterInternal));
        value.SetCType(server);
        value.SetPrecision(precision);
        value.SetScale(7);
        value.SetTypeFlag(flag);
        return value;
    }
}
