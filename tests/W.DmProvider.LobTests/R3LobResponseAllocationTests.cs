using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "LobResponseBudget")]
public sealed class R3LobResponseAllocationTests
{
    private const int PayloadBudget = 128 * 1024;
    private const int BodyBudget = PayloadBudget + 27;

    [Theory]
    [InlineData(false, 32, 0)]
    [InlineData(true, 32, 0)]
    [InlineData(false, 269, 0)]
    [InlineData(true, 269, 0)]
    [InlineData(false, 32, -5504)]
    [InlineData(true, 32, -5504)]
    [InlineData(false, 101, 0)]
    [InlineData(true, 101, 0)]
    public async Task ActualMessagePathRejectsOversizedHeaderBeforeBodyGrowthOrPayloadCopy(
        bool asynchronous, short responseCommand, int serverError)
    {
        await using var fixture = new OutputLobFixture();
        var channel = Install(fixture, Header(responseCommand, BodyBudget + 1, serverError));
        var protocol = fixture.Instance.GetCsi();
        byte[] initialStorage = protocol.__t02_field_04000AB9.A();
        var lob = fixture.Clob(1);
        int payloadCopies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => payloadCopies++;
        try
        {
            await ExpectMalformed(fixture, lob, asynchronous, 1);
            Assert.Equal(new short[] { 32 }, channel.Commands);
            Assert.Equal(64, channel.BytesRead);
            Assert.Equal(0, channel.BodyReadCalls);
            Assert.Same(initialStorage, protocol.__t02_field_04000AB9.A());
            Assert.Equal(0, payloadCopies);
            Assert.Equal(asynchronous ? 0 : 2, channel.SynchronousCalls);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextPayloadSecondBoundRejectsBeforeCopyEvenWhenBodyFitsFrameBudget(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        byte[] payload = new byte[PayloadBudget + 1];
        byte[] body = LobBody(payload, 1);
        Assert.True(body.Length < BodyBudget);
        var channel = Install(fixture, Frame(32, body));
        int payloadCopies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => payloadCopies++;
        try
        {
            await ExpectMalformed(fixture, fixture.Clob(1), asynchronous, 1);
            Assert.Equal(64 + body.Length, channel.BytesRead);
            Assert.Equal(1, channel.BodyReadCalls);
            Assert.Equal(0, payloadCopies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExactTextPayloadBudgetAllowsOpaqueAdvanceAndCrcTrailer(bool asynchronous, bool crc)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.crcBody = crc;
        byte[] payload = new byte[PayloadBudget];
        payload[0] = 0x41; payload[^1] = 0x5A;
        byte[] frame = Frame(32, LobBody(payload, 97), crc: crc);
        if (crc) Assert.Equal(BodyBudget + 64, frame.Length);
        var channel = Install(fixture, frame);
        int copied = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = count => copied += count;
        try
        {
            Data data = await ReadFrame(fixture, fixture.Clob(1), asynchronous, 1);
            Assert.Equal(PayloadBudget, data.value.Length);
            Assert.Equal(0x41, data.value[0]); Assert.Equal(0x5A, data.value[^1]);
            Assert.Equal(97, data.len);
            Assert.Equal(PayloadBudget, copied);
            Assert.Equal(new int[] { 1 }, channel.RequestLengths);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitLargeBinaryRequestKeepsByteContract(bool asynchronous)
    {
        const int bytesRequested = 200 * 1024;
        await using var fixture = new OutputLobFixture();
        byte[] payload = new byte[bytesRequested]; payload[0] = 0xA7; payload[^1] = 0x52;
        var channel = Install(fixture, Frame(32, LobBody(payload, -1)));
        Data data = await ReadFrame(fixture, fixture.Blob(bytesRequested), asynchronous, bytesRequested);
        Assert.Equal(bytesRequested, data.value.Length);
        Assert.Equal(-1, data.len);
        Assert.Equal(0xA7, data.value[0]); Assert.Equal(0x52, data.value[^1]);
        Assert.Equal(new int[] { bytesRequested }, channel.RequestLengths);
    }

    [Theory]
    [InlineData(false, 200 * 1024, 200 * 1024 + 28)]
    [InlineData(true, 200 * 1024, 200 * 1024 + 28)]
    [InlineData(false, int.MaxValue, 64 * 1024 * 1024 - 63)]
    [InlineData(true, int.MaxValue, 64 * 1024 * 1024 - 63)]
    public async Task BinaryBudgetGrowsOnlyToRequestedBytesAndNeverBeyondGlobalCeiling(
        bool asynchronous, int requested, int advertisedBody)
    {
        await using var fixture = new OutputLobFixture();
        var channel = Install(fixture, Header(32, advertisedBody));
        // Retain the actual receiving codec: a sent invalid response detaches it
        // from the instance, but its buffer still proves whether growth occurred.
        var protocol = fixture.Instance.GetCsi();
        byte[] initialStorage = protocol.__t02_field_04000AB9.A();
        int copies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => copies++;
        try
        {
            await ExpectMalformed(fixture, fixture.Blob(requested), asynchronous, requested);
            Assert.Equal(64, channel.BytesRead);
            Assert.Equal(0, channel.BodyReadCalls);
            Assert.Same(initialStorage, protocol.__t02_field_04000AB9.A());
            Assert.Equal(0, copies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortBinaryRequestStillRejectsExcessPayloadBeforeCopy(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        Install(fixture, Frame(32, LobBody([1, 2], -1)));
        int copies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => copies++;
        try
        {
            await ExpectMalformed(fixture, fixture.Blob(2), asynchronous, 1);
            Assert.Equal(0, copies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmallRequestAllowsBoundedServerErrorWithoutLobPayloadCopy(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        // Three empty diagnostic fields followed by the length-prefixed synthetic error text.
        byte[] errorBody = new byte[16 + 4096];
        BinaryPrimitives.WriteInt32LittleEndian(errorBody.AsSpan(12), 4096);
        Array.Fill(errorBody, (byte)'E', 16, 4096);
        var channel = Install(fixture, Frame(32, errorBody, error: -5504));
        int copies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => copies++;
        try
        {
            var lob = fixture.Blob(1);
            DmException error = asynchronous
                ? await Assert.ThrowsAsync<DmException>(() => ReadFrame(fixture, lob, true, 1))
                : Assert.Throws<DmException>(() => ReadFrame(fixture, lob, false, 1).GetAwaiter().GetResult());
            Assert.Equal(-5504, error.Number);
            Assert.Equal(64 + errorBody.Length, channel.BytesRead);
            Assert.Equal(0, copies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task MalformedMetadataCannotReachPayloadCopy(bool asynchronous, int malformedCase)
    {
        await using var fixture = new OutputLobFixture();
        byte[] body = LobBody([0x41], -1);
        switch (malformedCase)
        {
            case 0: BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), uint.MaxValue); break;
            case 1: body[0] = 2; break;
            case 2: BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(11), -1); break;
            case 3: body = [.. body, 0, 0, 0]; break;
        }
        Install(fixture, Frame(32, body));
        int copies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => copies++;
        try
        {
            await ExpectMalformed(fixture, fixture.Clob(1), asynchronous, 1);
            Assert.Equal(0, copies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptCrcCannotReachPayloadCopy(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.crcBody = true;
        byte[] frame = Frame(32, LobBody([0x41], 1), crc: true);
        frame[^1] ^= 1;
        Install(fixture, frame);
        int copies = 0;
        var previousObserver = GET_LOB_DATA.PayloadAllocationObserver.Value;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => copies++;
        try
        {
            await ExpectMalformed(fixture, fixture.Clob(1), asynchronous, 1);
            Assert.Equal(0, copies);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = previousObserver; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealTextMessagePipelinePreservesAllIndependentUtf8AndGb18030Cuts(bool asynchronous)
    {
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("small_fixed_text_vectors").EnumerateArray())
        {
            string encoding = vector.GetProperty("reference_encoding_name").GetString()!;
            string expected = vector.GetProperty("text").GetString()!;
            byte[] encoded = Convert.FromHexString(vector.GetProperty("encoded_bytes_hex").GetString()!);
            for (int cut = 1; cut < encoded.Length; cut++)
            {
                await using var fixture = new OutputLobFixture();
                fixture.Instance.ConnProperty.ServerEncoding = encoding;
                using var text = fixture.Reader(19).GetTextReader(0);
                fixture.Channel.AddData(encoded[..cut], 97);
                fixture.Channel.AddData(encoded[cut..], -1, true);
                fixture.ReleaseAll();
                var actual = new StringBuilder();
                char[] one = new char[1];
                int count;
                while ((count = asynchronous ? await text.ReadAsync(one) : text.Read(one, 0, 1)) != 0)
                    actual.Append(one, 0, count);
                Assert.Equal(expected, actual.ToString());
                Assert.Equal(new long[] { 0, 97 }, fixture.Channel.ReadPositions);
                Assert.Equal(new int[] { 2, 2 }, fixture.Channel.ReadLengths);
                Assert.True(encoded.Length > 2);
                if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
            }
        }
    }

    private static async Task ExpectMalformed(OutputLobFixture fixture, AbstractLob lob, bool asynchronous, int requested)
    {
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidDataException>(() => ReadFrame(fixture, lob, true, requested));
        else
        {
            // InvalidDataException derives from SystemException, so the legacy
            // IOException catch does not translate malformed responses to 6001.
            Assert.Throws<InvalidDataException>(() => ReadFrame(fixture, lob, false, requested).GetAwaiter().GetResult());
        }
    }

    private static async Task<Data> ReadFrame(OutputLobFixture fixture, AbstractLob lob, bool asynchronous, int requested)
    {
        using var invocation = fixture.Lease.BeginInvocation();
        return asynchronous
            ? await fixture.Instance.GetCsi().ReadLobAsync(lob, 0, requested)
            : fixture.Instance.GetCsi().A(lob, 0, requested);
    }

    private static RecordingChannel Install(OutputLobFixture fixture, byte[] bytes)
    {
        var channel = new RecordingChannel(bytes);
        var wire = fixture.Instance.GetCsi().A();
        var field = wire.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((DmTransport)field.GetValue(wire)!).Dispose();
        field.SetValue(wire, new DmTransport(channel));
        return channel;
    }

    private static byte[] Header(short command, int length, int error = 0)
    {
        byte[] header = new byte[64];
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(4), command);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6), length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), error);
        for (int index = 0; index < 19; index++) header[19] ^= header[index];
        return header;
    }

    private static byte[] LobBody(byte[] payload, long advance)
    {
        byte[] body = new byte[19 + payload.Length + (advance < 0 ? 0 : 4)];
        body[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), payload.Length);
        payload.CopyTo(body, 19);
        if (advance >= 0)
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(19 + payload.Length), checked((uint)advance));
        return body;
    }

    private static byte[] Frame(short command, byte[] body, int error = 0, bool crc = false)
    {
        byte[] frame = new byte[64 + body.Length + (crc ? 4 : 0)];
        Header(command, frame.Length - 64, error).CopyTo(frame, 0);
        body.CopyTo(frame, 64);
        if (crc)
        {
            uint value = 0xffffffff;
            for (int index = 0; index < frame.Length - 4; index++)
            {
                value ^= frame[index];
                for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(frame.Length - 4), ~value);
        }
        return frame;
    }

    private sealed class RecordingChannel(byte[] bytes) : IDmByteChannel
    {
        internal List<short> Commands { get; } = [];
        internal List<int> RequestLengths { get; } = [];
        internal int BytesRead { get; private set; }
        internal int BodyReadCalls { get; private set; }
        internal int SynchronousCalls { get; private set; }
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            SynchronousCalls++;
            return SendAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            SynchronousCalls++;
            return ReceiveAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            short command = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Commands.Add(command);
            if (command == 32) RequestLengths.Add(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset + 109)));
            return ValueTask.FromResult(count);
        }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BytesRead >= 64) BodyReadCalls++;
            int copied = Math.Min(count, bytes.Length - BytesRead);
            bytes.AsSpan(BytesRead, copied).CopyTo(buffer.AsSpan(offset, copied));
            BytesRead += copied;
            return ValueTask.FromResult(copied);
        }
        public void Dispose() => IsClosed = true;
    }
}
