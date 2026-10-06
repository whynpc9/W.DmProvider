using System.Data;
using System.Text;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class ReaderOffsetTests
{
    [Fact]
    public async Task InlineBlobLengthValidatesPayloadBoundsBeforeReturningMetadata()
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.NewLobFlag = true;
        var reader = fixture.Reader(12, OutputLobFixture.Locator(100, []));
        Assert.Equal(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR,
            Assert.Throws<DmException>(() => reader.GetBytes(0, 0, null!, 0, 0)).Number);
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task ClobBytesUseActualEncodedPayloadForInlineAndHugeOpaqueLocators()
    {
        await using var fixture = new OutputLobFixture();
        byte[] encoded = Encoding.UTF8.GetBytes("A🙂中");
        byte[] inline = OutputLobFixture.Locator(encoded.Length, encoded);
        fixture.Instance.ConnProperty.NewLobFlag = true;
        var reader = fixture.Reader(19, inline);
        Assert.Equal(encoded.Length, reader.GetBytes(0, 0, null!, 0, 0));
        byte[] copy = new byte[encoded.Length];
        Assert.Equal(copy.Length, reader.GetBytes(0, 0, copy, 0, copy.Length));
        Assert.Equal(encoded, copy);
        Assert.Empty(fixture.Channel.Commands);
        fixture.Instance.ConnProperty.NewLobFlag = false;
        long units = (long)DmConnectionSettings.DefaultMaxMaterializedLobSize + 1;
        var huge = fixture.Reader(19, OutputLobFixture.Locator(units));
        // Locator units are opaque, so they cannot establish the encoded payload size.
        // Actual cap rejection remains covered by WholeTargetEncodingRejectsActualByteCountBeforeAllocatingEncodedPayload
        // and ActualDecodedPayloadOverCapRejectsBeforeTheNextFrameAndDoesNotCacheSuccess.
        fixture.Channel.AddLength(units);
        fixture.Channel.AddData(encoded, 97, true);
        fixture.Channel.AddLength(units);
        fixture.Channel.AddData(encoded, 97, true);
        fixture.ReleaseAll();
        Assert.Equal(encoded.Length, huge.GetBytes(0, 0, null!, 0, 0));
        byte[] remoteCopy = new byte[encoded.Length];
        Assert.Equal(remoteCopy.Length, huge.GetBytes(0, 0, remoteCopy, 0, remoteCopy.Length));
        Assert.Equal(encoded, remoteCopy);
        Assert.Equal(new short[] { 29, 32, 29, 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 0, 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task LargeBlobRangeReadsOnlyOneBoundedFrameAndLengthUsesMetadata()
    {
        await using var fixture = new OutputLobFixture();
        long length = (long)DmConnectionSettings.DefaultMaxMaterializedLobSize + 1;
        var reader = fixture.Reader(12, OutputLobFixture.Locator(length));
        Assert.Equal(length, reader.GetBytes(0, 0, null!, 0, 0));
        Assert.Throws<NotSupportedException>(() => reader.GetValue(0));
        Assert.Empty(fixture.Channel.Commands);
        fixture.Channel.AddData([0xAB, 0x42], -1); fixture.ReleaseAll();
        byte[] two = new byte[2];
        Assert.Equal(2, reader.GetBytes(0, 0, two, 0, 2));
        Assert.Equal(new byte[] { 0xAB, 0x42 }, two);
        Assert.Equal(new short[] { 32 }, fixture.Channel.Commands);
        Assert.Equal(new int[] { 2 }, fixture.Channel.ReadLengths);
    }

    [Fact]
    public async Task ClobLengthScanUsesDecodedUtf16RatherThanHugeOpaqueServerAdvance()
    {
        await using var fixture = new OutputLobFixture();
        long units = (long)DmConnectionSettings.DefaultMaxMaterializedLobSize + 19;
        var reader = fixture.Reader(19, OutputLobFixture.Locator(units));
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), units, true); fixture.ReleaseAll();
        Assert.Equal(4, reader.GetChars(0, 0, null!, 0, 0));
        Assert.Equal(new short[] { 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task SequentialClobUnitSwitchesAreRejectedAndLengthQueriesDoNotSwitchUnits()
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.NewLobFlag = true;
        byte[] payload = Encoding.UTF8.GetBytes("A🙂Z");
        byte[] inline = OutputLobFixture.Locator(payload.Length, payload);
        var charsFirst = fixture.Reader(19, inline, CommandBehavior.SequentialAccess);
        using var text = charsFirst.GetTextReader(0);
        Assert.Equal('A', text.Read());
        Assert.Throws<InvalidOperationException>(() => charsFirst.GetBytes(0, 1, new byte[1], 0, 1));
        var bytesFirst = fixture.Reader(19, inline, CommandBehavior.SequentialAccess);
        Assert.Equal(1, bytesFirst.GetBytes(0, 0, new byte[1], 0, 1));
        Assert.Equal(4, bytesFirst.GetChars(0, 0, null!, 0, 0));
        Assert.Throws<InvalidOperationException>(() => bytesFirst.GetChars(0, 1, new char[1], 0, 1));
        Assert.Throws<InvalidOperationException>(() => bytesFirst.GetTextReader(0));
        Assert.Equal(1, bytesFirst.GetBytes(0, 1, new byte[1], 0, 1));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task NullZeroReadsRetain6081AndTextZeroRejectsIntegerBeforeIo()
    {
        await using var fixture = new OutputLobFixture();
        var nullReader = OutputSequentialRows.Create(fixture, 19, null!, 7, new byte[4]);
        Assert.Equal(6081, Assert.Throws<DmException>(() => nullReader.GetChars(0, 0, new char[1], 1, 0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => nullReader.GetBytes(0, 0, new byte[1], 1, 0)).Number);
        var integer = fixture.Reader(7, new byte[4]);
        Assert.Throws<InvalidCastException>(() => integer.GetChars(0, 0, new char[1], 1, 0));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData(7)]
    public async Task BinaryLengthAndDataRejectIntegerWithoutProtocolIo(int type)
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(type);
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, null!, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, new byte[1], 0, 1));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, new byte[1], 1, 0));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("String")]
    [InlineData("Value")]
    [InlineData("Bytes")]
    public async Task LaterTypedFieldAccessInvalidatesEarlierStreamBeforeCachedOrZeroRead(string getter)
    {
        await using var fixture = new OutputLobFixture();
        int laterType = getter == "String" ? 2 : getter == "Bytes" ? 17 : 7;
        byte[] later = getter == "String" ? Encoding.UTF8.GetBytes("tail") : getter == "Bytes" ? [7, 8] : BitConverter.GetBytes(17);
        var reader = OutputSequentialRows.Create(fixture, 17, [1, 2, 3], laterType, later);
        using var stream = reader.GetStream(0);
        Assert.Equal(1, await stream.ReadAsync(new byte[1]));
        switch (getter)
        {
            case "Int32": Assert.Equal(17, reader.GetInt32(1)); break;
            case "String": Assert.Equal("tail", reader.GetString(1)); break;
            case "Value": Assert.Equal(17, reader.GetValue(1)); break;
            case "Bytes": Assert.Equal(1, reader.GetBytes(1, 0, new byte[1], 0, 1)); break;
        }
        Assert.ThrowsAny<InvalidOperationException>(() => stream.Read(new byte[1], 1, 0));
        Assert.ThrowsAny<InvalidOperationException>(() => stream.Read(new byte[1]));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => stream.ReadAsync(new byte[1], 0, 1));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task DisposedPartialTextCannotReopenOrUseFullTypedGetterInSequentialMode()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(2, Encoding.UTF8.GetBytes("A🙂Z"), CommandBehavior.SequentialAccess);
        var text = reader.GetTextReader(0);
        Assert.Equal('A', text.Read()); text.Dispose();
        Assert.Throws<InvalidOperationException>(() => reader.GetTextReader(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Equal(6097, Assert.Throws<DmException>(() => reader.GetChars(0, 0, new char[1], 0, 1)).Number);
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task LengthScanPreservesSequentialCursorAndNextRowResetsConsumption()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(2, Encoding.UTF8.GetBytes("A🙂Z"), CommandBehavior.SequentialAccess, rows: 2);
        using var text = reader.GetTextReader(0);
        Assert.Equal('A', text.Read());
        Assert.Equal(4, reader.GetChars(0, 0, null!, 0, 0));
        char[] next = new char[2];
        Assert.Equal(2, reader.GetChars(0, 1, next, 0, 2));
        Assert.Equal("🙂", new string(next));
        Assert.True(reader.Read());
        using var newText = reader.GetTextReader(0);
        Assert.Equal('A', newText.Read());
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task NonSequentialReopenAndTypedGetterMayRestartAtZero()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(2, Encoding.UTF8.GetBytes("A🙂Z"));
        var first = reader.GetTextReader(0); Assert.Equal('A', first.Read()); first.Dispose();
        using var second = reader.GetTextReader(0); Assert.Equal('A', second.Read());
        Assert.Equal("A🙂Z", reader.GetString(0));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task BlobRangeUsesBoundedSkipAndRandomCallsRestartAtZero()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12);
        fixture.Channel.AddData([10, 11], -1);
        fixture.Channel.AddData([12, 13], -1);
        fixture.Channel.AddData([10, 11], -1); fixture.ReleaseAll();
        byte[] target = new byte[2];
        Assert.Equal(2, reader.GetBytes(0, 2, target, 0, 2));
        Assert.Equal(new byte[] { 12, 13 }, target);
        Assert.Equal(2, reader.GetBytes(0, 0, target, 0, 2));
        Assert.Equal(new byte[] { 10, 11 }, target);
        Assert.Equal(new long[] { 0, 2, 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task TextLengthScanIsUtf16AndDoesNotMoveActiveReader()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(19);
        using var text = reader.GetTextReader(0);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂"), 9, true);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂"), 9, true); fixture.ReleaseAll();
        Assert.Equal(3, reader.GetChars(0, 0, null!, 0, 0));
        char[] chars = new char[3];
        Assert.Equal(3, await text.ReadAsync(chars));
        Assert.Equal("A🙂", new string(chars));
        Assert.Equal(new long[] { 0, 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task SequentialOffsetsTrackReturnedUtf16CharsAndRejectBackwards()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(19, behavior: CommandBehavior.SequentialAccess);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), 64, true); fixture.ReleaseAll();
        char[] chars = new char[2];
        Assert.Equal(2, reader.GetChars(0, 1, chars, 0, 2));
        Assert.Equal("🙂", new string(chars));
        Assert.Equal(6097, Assert.Throws<DmException>(() => reader.GetChars(0, 0, chars, 0, 1)).Number);
        Assert.Equal(1, reader.GetChars(0, 3, chars, 0, 1));
        Assert.Equal('Z', chars[0]);
    }

    [Fact]
    public async Task ZeroLengthAndInvalidRangesAreHandledBeforeProtocolIo()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12);
        byte[] bytes = new byte[2];
        Assert.Equal(0, reader.GetBytes(0, 1000, bytes, bytes.Length, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, long.MaxValue, bytes, 0, 1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetBytes(0, -1, bytes, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, bytes, int.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, bytes, 0, -1));
        Assert.Empty(fixture.Channel.Commands);
    }
}

internal static class OutputSequentialRows
{
    internal static DmDataReader Create(OutputLobFixture fixture, int firstType, byte[] first, int laterType, byte[] later)
    {
        var instance = fixture.Instance;
        var statement = (W.Dm.Internal.Legacy.A.A)RuntimeHelpers.GetUninitializedObject(typeof(W.Dm.Internal.Legacy.A.A));
        OutputLobFixture.SetField(statement, "__t02_field_04000923", instance);
        OutputLobFixture.SetField(statement, "__t02_field_04000924", instance.GetCsi());
        OutputLobFixture.SetField(statement, "__t02_field_04000933", new DmCommand("synthetic", fixture.Connection));
        OutputLobFixture.SetField(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
        OutputLobFixture.SetField(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
        var info = new DmInfo(instance);
        info.SetColumnsInfo([new DmColumn(instance) { type = firstType, name = "FIRST" },
            new DmColumn(instance) { type = laterType, prec = laterType == 7 ? 4 : later.Length, name = "LATER" }]);
        info.SetHasResultSet(true); info.SetRowCount(1);
        OutputLobFixture.SetField(statement, "__t02_field_04000927", info);
        var cache = new DmResultSetCache(statement, 2, 1) { datas = [[[], first, later]], datasStartPos = 0 };
        OutputLobFixture.SetField(statement, "__t02_field_04000928", cache);
        var reader = new DmDataReader(cache, info, CommandBehavior.SequentialAccess);
        reader.AttachExecutionLease(fixture.Lease, ownsLease: false);
        Assert.True(reader.Read()); return reader;
    }
}
