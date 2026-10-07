using System.Buffers.Binary;
using System.Text;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class LobMaterializationReviewTests
{
    private const int Limit = DmConnectionSettings.DefaultMaxMaterializedLobSize;

    [Fact]
    public void ReturnedPayloadAccountingAcceptsExactBoundaryAndRejectsTheNextUnit()
    {
        // Metadata only: no 64 MiB allocation is needed to test the inclusive limit.
        Assert.Equal(Limit, DmLobMaterialization.Bytes(Limit));
        Assert.Equal(Limit / 2, DmLobMaterialization.Characters(Limit / 2));
        Assert.Equal(Limit / 4, DmLobMaterialization.HexInput(Limit / 4));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.Bytes((long)Limit + 1));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.Characters((long)Limit / 2 + 1));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.HexInput((long)Limit / 4 + 1));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.Characters(long.MaxValue));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.HexInput(long.MaxValue));
        Assert.Throws<NotSupportedException>(() => DmLobMaterialization.Bytes(-1));
    }

    [Theory]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public void LongRowFixtureDecodesIts64BitLengthBeforeMaterialization(long length)
    {
        // Assert the input really carries the huge length through the current decoder;
        // otherwise a zero/unknown length can fall through to the fake transport.
        using var blob = new ReaderReviewTests.ReaderFixture(12, Locator(length));
        Assert.Equal(length, blob.Reader.GetBlob(0).bytesLength);
        using var clob = new ReaderReviewTests.ReaderFixture(19, Locator(length));
        Assert.Equal(length, clob.Reader.GetClob(0).bytesLength);
    }

    [Theory]
    [InlineData(67108865L)]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public void BlobFullGettersRejectSyntheticHugeLocatorBeforeReadOrAllocation(long length)
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(12, Locator(length));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetValue(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetFieldValue<byte[]>(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
        // The fake connection has no transport: attempting a read would fail differently.
    }

    [Theory]
    [InlineData(67108865L)]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public void BlobLengthMetadataCanExceedMaterializationCapWithoutTransport(long length)
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(12, Locator(length));
        Assert.Equal(length, fixture.Reader.GetBytes(0, 0, null!, 0, 0));
        // No transport is installed: a GETLEN or payload request cannot succeed.
    }

    [Fact]
    public void HexRejectsAtItsSmallerInputBoundaryBeforeReading()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(12, Locator(Limit / 4 + 1));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
    }

    [Theory]
    [InlineData(33554433L)]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public void CachedClobFullGettersUseActualDecodedTextInsteadOfAdvertisedLength(long length)
    {
        const string text = "A🚂中Z";
        using var fixture = new ReaderReviewTests.ReaderFixture(19, [], text, clobLength: length);
        Assert.Equal(text, fixture.Reader.GetString(0));
        Assert.Equal(text, fixture.Reader.GetValue(0));
        Assert.Equal(text, fixture.Reader.GetFieldValue<string>(0));
        Assert.Equal(Encoding.UTF8.GetByteCount(text), fixture.Reader.GetBytes(0, 0, null!, 0, 0));
    }

    [Theory]
    [InlineData(12, 67108865L)]
    public void InlineMetadataRejectsBeforeTryingToDecodeMissingPayload(int type, long length)
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(type, Locator(length, inline: true));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetValue(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
    }

    [Fact]
    public void InlineClobRejectsInvalidEncodedLengthBeforeDecode()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(19, Locator(Limit / 2 + 1, inline: true));
        Assert.Equal(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR,
            Assert.Throws<DmException>(() => fixture.Reader.GetString(0)).Number);
    }

    [Fact]
    public void NullClobCharAndBytesGettersUse6081()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(19, null);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetChars(0, 0, null!, 0, 0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetChar(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetBytes(0, 0, null!, 0, 0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetString(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<string>(0)).Number);
    }

    [Fact]
    public void BlobFetchAllRejectsKnownByteLengthsBeforeReading()
    {
        using var blob = new ReaderReviewTests.ReaderFixture(12, Locator(Limit + 1));
        Assert.Throws<NotSupportedException>(() => blob.Reader.GetBlob(0).loadAllData());
    }

    [Fact]
    public void InlineClobBoundedSliceAndIndependentUtf16ScanUseTheActualPayload()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(19, [], "A中🙂", clobLength: long.MaxValue);
        char[] prefix = new char[2];
        Assert.Equal(2, fixture.Reader.GetChars(0, 0, prefix, 0, 2));
        Assert.Equal("A中", new string(prefix));
        Assert.Equal(4, fixture.Reader.GetChars(0, 0, null!, 0, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A中e\u0301🙂")]
    public void SmallInlineClobPreservesUnicodeAndEncodedBytes(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        using var fixture = new ReaderReviewTests.ReaderFixture(19, Locator(bytes.Length, inline: true, payload: bytes));
        Assert.Equal(text, fixture.Reader.GetString(0));
        Assert.Equal(text, fixture.Reader.GetValue(0));
        Assert.Equal(text, fixture.Reader.GetFieldValue<string>(0));
        Assert.Equal(text.Length, fixture.Reader.GetChars(0, 0, null!, 0, 0));
        byte[] copy = new byte[bytes.Length + 1];
        Assert.Equal(bytes.Length, fixture.Reader.GetBytes(0, 0, copy, 0, bytes.Length));
        Assert.Equal(bytes, copy[..bytes.Length]);
    }

    [Fact]
    public void SmallInlineBlobReturnsPayloadRatherThanLocatorHex()
    {
        byte[] payload = [0x00, 0xAB, 0xFF, 0x42];
        using var fixture = new ReaderReviewTests.ReaderFixture(12, Locator(payload.Length, inline: true, payload: payload));
        Assert.Equal(payload, fixture.Reader.GetFieldValue<byte[]>(0));
        Assert.Equal("00ABFF42", fixture.Reader.GetString(0));
        Assert.Equal(payload.Length, fixture.Reader.GetBytes(0, 0, null!, 0, 0));
    }

    [Fact]
    public void TypedNullsUse6081WhileObjectAndDbNullPreserveSqlNull()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(7, null);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetInt32(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetGuid(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<Guid>(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<string>(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<byte[]>(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DmDecimal>(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DateTime>(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<int?>(0)).Number);
        Assert.Same(DBNull.Value, fixture.Reader.GetValue(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetFieldValue<object>(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetFieldValue<DBNull>(0));
    }

    // Current msgVersion >= 9 decoder: 13-byte prefix + 8-byte group/file/page
    // + 6-byte table/column + 12-byte row ID = offset 39 for the long-row length.
    // The inline data offset remains getHeadSize() == 47; it is a different field.
    private static byte[] Locator(long length, bool inline = false, byte[]? payload = null)
    {
        bool longRow = !inline && length > int.MaxValue;
        byte[] locator = new byte[47 + (payload?.Length ?? 0)];
        locator[0] = inline ? (byte)1 : longRow ? (byte)4 : (byte)2;
        BinaryPrimitives.WriteInt32LittleEndian(locator.AsSpan(9), longRow ? 0 : checked((int)length));
        if (longRow) BinaryPrimitives.WriteInt64LittleEndian(locator.AsSpan(39), length);
        if (payload != null) payload.CopyTo(locator, 47);
        return locator;
    }
}
