using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using W.Dm;
using W.Dm.Internal.Transport;
using W.Dm.util;
using Xunit;

namespace W.DmProvider.LobTests;

[CollectionDefinition("R3 legacy CLOB ranges", DisableParallelization = true)]
public sealed class R3LegacyClobRangeCollection { }

[Collection("R3 legacy CLOB ranges")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class R3LegacyClobRangeReviewTests
{
    [Theory]
    [InlineData(false, 4, false)]
    [InlineData(true, 4, false)]
    [InlineData(false, 5, true)]
    [InlineData(true, 5, true)]
    public async Task RemoteZeroLengthValidatesDecodedUtf16OffsetIncludingEmojiAndOpaqueAdvance(bool asynchronous, int offset, bool invalid)
    {
        await using var fixture = new OutputLobFixture();
        var clob = fixture.Clob(900); // Deliberately not a decoded UTF-16 length.
        fixture.Channel.AddData([0x41, 0xF0, 0x9F], 37);
        fixture.Channel.AddData([0x99, 0x82, 0x5A], 11, true); fixture.ReleaseAll();
        if (asynchronous)
        {
            if (invalid) await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => clob.GetSubStringUnderOwnerAsync(offset, 0, default));
            else Assert.Equal("", await clob.GetSubStringUnderOwnerAsync(offset, 0, default));
            Assert.Equal(0, fixture.Channel.SynchronousCalls);
        }
        else
        {
            if (invalid) Assert.Throws<ArgumentOutOfRangeException>(() => clob.GetString(offset, 0));
            else Assert.Equal("", clob.GetString(offset, 0));
        }
        Assert.Equal(new short[] { 32, 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 0, 37 }, fixture.Channel.ReadPositions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteZeroAtOriginNeedsNoIoAndOversizedRequestReturnsOnlyActualShortText(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        var clob = fixture.Clob(900);
        if (asynchronous) Assert.Equal("", await clob.GetSubStringUnderOwnerAsync(0, 0, default));
        else Assert.Equal("", clob.GetString(0, 0));
        Assert.Empty(fixture.Channel.Commands);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), 97, true); fixture.ReleaseAll();
        string actual = asynchronous ? await clob.GetSubStringUnderOwnerAsync(0, int.MaxValue, default) : clob.GetString(0, int.MaxValue);
        Assert.Equal("A🙂Z", actual);
        Assert.Equal(new short[] { 32 }, fixture.Channel.Commands);
        if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData("local", false)]
    [InlineData("local", true)]
    [InlineData("inline", false)]
    [InlineData("inline", true)]
    [InlineData("fetchAll", false)]
    [InlineData("fetchAll", true)]
    public async Task CachedLayoutsClampReturnedPayloadBeforeCapAndShareOffsetValidation(string layout, bool asynchronous)
    {
        const string text = "A🙂Z";
        await using var fixture = new OutputLobFixture();
        DmClob clob;
        if (layout == "local") clob = new DmClob(text, fixture.Instance);
        else if (layout == "inline") clob = fixture.Clob(Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetBytes(text));
        else { clob = fixture.Clob(900); clob.data = text; clob.fetchAll = true; }
        string actual = asynchronous ? await clob.GetSubStringUnderOwnerAsync(0, int.MaxValue, default) : clob.GetString(0, int.MaxValue);
        Assert.Equal(text, actual);
        if (asynchronous)
        {
            Assert.Equal("", await clob.GetSubStringUnderOwnerAsync(text.Length, 0, default));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => clob.GetSubStringUnderOwnerAsync(text.Length + 1, 0, default));
        }
        else
        {
            Assert.Equal("", clob.GetString(text.Length, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clob.GetString(text.Length + 1, 0));
        }
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task IndependentLegacyOffsetScanDoesNotAdvanceTheReadersActiveTextFlow()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(19);
        var clob = reader.GetClob(0);
        using var text = reader.GetTextReader(0);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), 97, true);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), 97, true); fixture.ReleaseAll();
        Assert.Equal("", await clob.GetSubStringUnderOwnerAsync(4, 0, default));
        char[] first = new char[1]; Assert.Equal(1, await text.ReadAsync(first.AsMemory())); Assert.Equal('A', first[0]);
        Assert.Equal(new long[] { 0, 0 }, fixture.Channel.ReadPositions); Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualReturnedPayloadOverCapIsRejectedWithBoundedSyntheticFrames(bool asynchronous)
    {
        int characters = DmConnectionSettings.DefaultMaxMaterializedLobSize / sizeof(char) + 1;
        await using var fixture = new LogicalRemote(characters);
        if (asynchronous) await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Clob.GetSubStringUnderOwnerAsync(0, int.MaxValue, default));
        else Assert.Throws<NotSupportedException>(() => fixture.Clob.GetString(0, int.MaxValue));
        Assert.Equal(characters, fixture.Channel.GeneratedCharacters);
        Assert.True(fixture.Channel.PeakRetainedFrameBytes <= 64 + 19 + 8192 + 4);
        Assert.Equal(4097, fixture.Channel.GetRequests);
        Assert.Equal(0, fixture.Channel.LengthRequests);
        if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData("db_string")]
    [InlineData("db_bytes")]
    [InlineData("longvarchar")]
    [InlineData("n2db_from_clob")]
    [InlineData("n2db_object")]
    [InlineData("n2db_to_clob")]
    [InlineData("complex_frame")]
    [Trait("Feature", "LegacyInternalConversion")]
    public async Task WholeConversionsPreserveSupplementaryTextWhenOpaqueLengthIsThree(string conversion)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.property[DmConst.PROP_KEY_LOB_MODE] = 1;
        var column = new DmColumn(fixture.Instance) { type = 19, typeName = conversion == "longvarchar" ? "LONGVARCHAR" : "CLOB" };
        byte[] locator = OutputLobFixture.Locator(3);
        var clob = fixture.Clob(3);
        fixture.Channel.AddLength(3);
        fixture.Channel.AddData([0x41, 0x42], 2);
        fixture.Channel.AddData([0xF0, 0x9F, 0x99, 0x82], 1, true); fixture.ReleaseAll();
        byte[] expectedUtf8 = [0x41, 0x42, 0xF0, 0x9F, 0x99, 0x82];
        using var invocation = fixture.Lease.BeginInvocation();
        switch (conversion)
        {
            case "db_string": Assert.Equal("AB🙂", DB2N.toString(locator, column, fixture.Connection)); break;
            case "db_bytes": Assert.Equal(expectedUtf8, DB2N.toBytes(locator, column, fixture.Connection)); break;
            case "longvarchar":
                Assert.False(fixture.Connection.lobFetchAll());
                Assert.Equal("AB🙂", Assert.IsType<string>(DB2N.toObject(locator, column, fixture.Connection, new Dictionary<string, Type>())));
                break;
            case "n2db_from_clob": Assert.Equal(expectedUtf8, N2DB.fromClob(clob, new DmColumn(fixture.Instance) { type = 2 }, fixture.Connection)); break;
            case "n2db_object": Assert.Equal(expectedUtf8, N2DB.fromObject(clob, column, fixture.Connection)); break;
            case "n2db_to_clob": Assert.Equal(expectedUtf8, N2DB.toClob(clob, column, fixture.Connection)); break;
            case "complex_frame":
                var method = typeof(ComplexTypeData).GetMethod("convertLobToBytes", BindingFlags.NonPublic | BindingFlags.Static)!;
                byte[] framed = (byte[])method.Invoke(null, [clob, 19, "UTF-8"])!;
                Assert.Equal(expectedUtf8.Length, BinaryPrimitives.ReadInt32LittleEndian(framed));
                Assert.Equal(expectedUtf8.Length + 4, framed.Length); Assert.Equal(expectedUtf8, framed[4..]);
                break;
        }
        Assert.Equal(new short[] { 29, 32, 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 0, 2 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    [Trait("Feature", "LegacyInternalConversion")]
    public async Task WholeBytesUseStrictTargetCharsetAndIndependentGoldenPayload()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        var golden = vectors.RootElement.GetProperty("small_fixed_text_vectors").EnumerateArray()
            .Single(value => value.GetProperty("reference_encoding_name").GetString() == "GB18030");
        string text = golden.GetProperty("text").GetString()!;
        byte[] expected = Convert.FromHexString(golden.GetProperty("encoded_bytes_hex").GetString()!);
        await using var fixture = new OutputLobFixture();
        var clob = new DmClob(text, fixture.Instance);
        fixture.Instance.ConnProperty.ServerEncoding = "GB18030";
        Assert.Equal(expected, N2DB.toClob(clob, new DmColumn(fixture.Instance) { type = 19 }, fixture.Connection));
        Assert.Throws<EncoderFallbackException>(() => new DmClob("\uD800", fixture.Instance).MaterializeBytesUnderOwner("UTF-8"));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    [Trait("Feature", "LegacyInternalConversion")]
    public async Task WholeTargetEncodingRejectsActualByteCountBeforeAllocatingEncodedPayload()
    {
        await using var fixture = new OutputLobFixture();
        int characters = DmConnectionSettings.DefaultMaxMaterializedLobSize / 3 + 1;
        var clob = new DmClob(new string('中', characters), fixture.Instance);
        Assert.True((long)characters * sizeof(char) < DmConnectionSettings.DefaultMaxMaterializedLobSize);
        Assert.Throws<NotSupportedException>(() => clob.MaterializeBytesUnderOwner("UTF-8"));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    [Trait("Feature", "LegacyInternalConversion")]
    public async Task WholeConversionCannotUseAnAttachedLobFromAnOldReaderRow()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(19, rows: 2); var old = reader.GetClob(0);
        Assert.True(reader.Read());
        Assert.Throws<InvalidOperationException>(() => N2DB.toClob(old, new DmColumn(fixture.Instance) { type = 19 }, fixture.Connection));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    [Trait("Feature", "LegacyInternalConversion")]
    public async Task FramingCorrectionDoesNotEnableComplexDecodingOrNativeBulkLoading()
    {
        await using var fixture = new OutputLobFixture();
        byte[] goldenFrame = [6, 0, 0, 0, 0x41, 0x42, 0xF0, 0x9F, 0x99, 0x82];
        var output = (ComplexTypeData)RuntimeHelpers.GetUninitializedObject(typeof(ComplexTypeData));
        var method = typeof(ComplexTypeData).GetMethod("bytesToClob", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wrapped = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [goldenFrame, output, null, "UTF-8"]));
        Assert.IsType<NotSupportedException>(wrapped.InnerException);
        Assert.Throws<NotSupportedException>(() => new FldrStatement(fixture.Connection, null!));
        Assert.Empty(fixture.Channel.Commands);
    }

    private sealed class LogicalRemote : IAsyncDisposable
    {
        private readonly OutputLobFixture fixture = new();
        internal readonly LogicalTextChannel Channel;
        internal DmClob Clob { get; }
        internal LogicalRemote(int characters)
        {
            Channel = new LogicalTextChannel(characters);
            fixture.Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = 8192;
            object wire = fixture.Instance.GetCsi().A();
            var field = wire.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((DmTransport)field.GetValue(wire)!).Dispose(); field.SetValue(wire, new DmTransport(Channel));
            Clob = fixture.Clob(-1);
        }
        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }

    // Generates one frame at a time; retains neither all replies nor a giant logical input value.
    private sealed class LogicalTextChannel(int characters) : IDmByteChannel
    {
        private byte[] response = [];
        private int offset;
        private long serverOffset;
        internal int GeneratedCharacters, PeakRetainedFrameBytes, GetRequests, LengthRequests, SynchronousCalls;
        public bool IsClosed { get; private set; }
        public int Send(byte[] bytes, int start, int count, int timeoutMilliseconds)
        { SynchronousCalls++; return SendCore(bytes, start, count); }
        public int Receive(byte[] bytes, int start, int count, int timeoutMilliseconds)
        { SynchronousCalls++; return ReceiveCore(bytes, start, count); }
        public ValueTask<int> SendAsync(byte[] bytes, int start, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(bytes, start, count)); }
        public ValueTask<int> ReceiveAsync(byte[] bytes, int start, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReceiveCore(bytes, start, count)); }
        private int SendCore(byte[] bytes, int start, int count)
        {
            short operation = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(start + 4));
            if (operation == 29) LengthRequests++;
            Assert.Equal((short)32, operation);
            Assert.Equal(serverOffset, BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(start + 101)));
            int emitted = Math.Min(8192, characters - GeneratedCharacters);
            GetRequests++; GeneratedCharacters += emitted;
            bool end = GeneratedCharacters == characters;
            response = new byte[64 + 19 + emitted + 4]; offset = 0;
            BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(4), 32);
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(6), response.Length - 64);
            for (int index = 0; index < 19; index++) response[19] ^= response[index];
            response[64] = end ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(65), emitted);
            response.AsSpan(83, emitted).Fill((byte)'x');
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(83 + emitted), 37);
            serverOffset += 37;
            PeakRetainedFrameBytes = Math.Max(PeakRetainedFrameBytes, response.Length);
            return count;
        }
        private int ReceiveCore(byte[] bytes, int start, int count)
        {
            int copied = Math.Min(count, response.Length - offset);
            response.AsSpan(offset, copied).CopyTo(bytes.AsSpan(start, copied)); offset += copied; return copied;
        }
        public void Dispose() { IsClosed = true; response = []; }
    }
}
