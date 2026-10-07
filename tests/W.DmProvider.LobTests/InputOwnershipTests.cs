using System.Data;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.LobTests;

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class InputOwnershipTests
{
    [Fact]
    public void PlanFreezesDescriptorAndParameterMetadataWithoutReadingOrChangingCallerValue()
    {
        var source = new UnknownInputStream([1, 2, 3]);
        var parameters = new DmParameterCollection();
        var original = new DmParameter("p", DmDbType.Blob) { Value = source };
        parameters.Add(original);
        using var plan = DmCommandPlan.Capture("SELECT :p FROM DUAL", CommandType.Text, 10, null, null, parameters);
        var frozen = Assert.IsType<DmLobInput>(((DmParameter)plan.Parameters[0]).Value);
        Assert.Same(source, frozen.SourceIdentity);
        Assert.Same(source, original.Value);
        Assert.Equal(DmDbType.Blob, plan.ParameterMetadata[0].DmSqlType);
        Assert.Equal(0, source.ReadCalls);
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameInputAcrossDistinctParametersOrRepeatedMarkerRejectsBeforeRead(bool repeatedMarker)
    {
        var source = new UnknownInputStream([1]);
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("a", DmDbType.Blob) { Value = source });
        if (!repeatedMarker) parameters.Add(new DmParameter("b", DmDbType.Blob) { Value = source });
        string sql = repeatedMarker ? "SELECT :a,:a FROM DUAL" : "SELECT :a,:b FROM DUAL";
        Assert.Throws<NotSupportedException>(() => DmCommandPlan.Capture(sql, CommandType.Text, 10, null, null, parameters));
        Assert.Equal(0, source.ReadCalls);
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceTypeAndExplicitTypeMustMatchWithoutInference(bool text)
    {
        object source = text ? new UnknownTextInput("x") : new UnknownInputStream([1]);
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter { ParameterName = "p", Value = source });
        Assert.Throws<NotSupportedException>(() => DmCommandPlan.Capture("SELECT :p FROM DUAL", CommandType.Text, 10, null, null, parameters));
        var wrong = new DmParameterCollection();
        wrong.Add(new DmParameter("p", text ? DmDbType.Blob : DmDbType.Clob) { Value = source });
        Assert.Throws<NotSupportedException>(() => DmCommandPlan.Capture("SELECT :p FROM DUAL", CommandType.Text, 10, null, null, wrong));
    }

    [Theory]
    [InlineData(DmDbType.Binary)]
    [InlineData(DmDbType.VarBinary)]
    [InlineData(DmDbType.VarChar)]
    public void UnprovenNonLobStreamPairingsAreNotSilentlyForcedToBlob(DmDbType type)
    {
        var source = new UnknownInputStream([1]);
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("p", type) { Value = source });
        Assert.Throws<NotSupportedException>(() => DmCommandPlan.Capture("SELECT :p FROM DUAL", CommandType.Text, 10, null, null, parameters));
        Assert.Equal(0, source.ReadCalls);
    }

    [Fact]
    public async Task LegacyProfileIsRejectedBeforeSourceReadOrStatementNetworkIo()
    {
        var channel = new UploadRecordingChannel();
        using var fixture = new InputSessionFixture(channel, messageVersion: 9);
        var source = new UnknownInputStream([1, 2]);
        fixture.Command.Parameters.Add(new DmParameter("p", DmDbType.Blob) { Value = source });
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Command.ExecuteNonQueryAsync());
        Assert.Equal(0, source.ReadCalls);
        Assert.Equal(0, channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task NewExecutionUsesCallerCurrentPositionWithoutRewindOrDisposal()
    {
        var source = new UnknownInputStream([1, 2, 3], 1);
        using (var first = new DmLobInput(source, DmDbType.Blob).OpenCursor(8, "UTF-8"))
        { Assert.Equal(3, await first.ReadChunkAsync()); }
        using (var second = new DmLobInput(source, DmDbType.Blob).OpenCursor(8, "UTF-8"))
        { Assert.Equal(0, await second.ReadChunkAsync()); Assert.True(second.EndOfInput); }
        Assert.False(source.Disposed);
    }

    [Fact]
    public void LegacyLengthAndArrayAccessCannotPretendDescriptorIsEmptyData()
    {
        var value = new DmParamValue();
        value.SetStreamingInput(new DmLobInput(new UnknownInputStream([1]), DmDbType.Blob));
        Assert.True(value.GetInDataBound());
        Assert.False(value.GetIsInDataNull());
        Assert.Throws<NotSupportedException>(() => value.GetStreamLen());
        byte[] buffer = new byte[8];
        Assert.Throws<NotSupportedException>(() => value.GetBytes(ref buffer, 0, 0, 8));
        value.SetInValue(new byte[] { 7 });
        Assert.Null(value.StreamingInput);
        Assert.Equal(1, value.GetStreamLen());
    }
}
