using System.Buffers.Binary;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Transport;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;

namespace W.DmProvider.TransportTests;

[Trait("Category", "Contract")]
[Trait("Feature", "LobResponseBudget")]
public sealed class R3LobFrameBudgetTests
{
    // Client policy, independent of the opaque units in a text LOB request.
    private const int BodyBudget = 128 * 1024 + 27;

    [Theory]
    [InlineData(false, 32, 0)]
    [InlineData(true, 32, 0)]
    [InlineData(false, 269, 0)]
    [InlineData(true, 269, 0)]
    [InlineData(false, 32, -5504)]
    [InlineData(true, 32, -5504)]
    [InlineData(false, 101, 0)]
    [InlineData(true, 101, 0)]
    public async Task HeaderBudgetRejectsBeforeBodyReadOrDestinationGrowth(bool asynchronous, short command, int error)
    {
        using var input = new CountingStream(Header(command, BodyBudget + 1, error));
        var destination = new LegacyBuffer();
        byte[] initialStorage = destination.A();
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await DmFrameReader.ReadAsync(input, destination, false, DmDeadline.Infinite,
                    maxResponseBodyLength: BodyBudget));
        else
            Assert.Throws<InvalidDataException>(() => DmFrameReader.Read(input, destination, false,
                DmDeadline.Infinite, BodyBudget));
        Assert.Equal(64, input.BytesRead);
        Assert.Equal(64L, input.Position);
        Assert.Equal(0, input.BodyReadCalls);
        Assert.Same(initialStorage, destination.A());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompressionRemainsUnsupportedBeforeBodyRead(bool asynchronous)
    {
        byte[] header = Header(32, BodyBudget);
        header[18] = 1;
        ChecksumHeader(header);
        using var input = new CountingStream(header);
        var destination = new LegacyBuffer();
        byte[] initialStorage = destination.A();
        if (asynchronous)
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await DmFrameReader.ReadAsync(input, destination, false, DmDeadline.Infinite,
                    maxResponseBodyLength: BodyBudget));
        else
            Assert.Throws<NotSupportedException>(() => DmFrameReader.Read(input, destination, false,
                DmDeadline.Infinite, BodyBudget));
        Assert.Equal(64, input.BytesRead);
        Assert.Equal(64L, input.Position);
        Assert.Equal(0, input.BodyReadCalls);
        Assert.Same(initialStorage, destination.A());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HeartbeatAndResponseKeepChecksumSemanticsWithinBudget(bool asynchronous, bool crc)
    {
        byte[] heartbeat = Frame(269, [8, 7, 6], crc);
        byte[] response = Frame(32, [4, 5, 6], crc);
        using var input = new CountingStream([.. heartbeat, .. response]);
        var destination = new LegacyBuffer();
        int length = asynchronous
            ? await DmFrameReader.ReadAsync(input, destination, crc, DmDeadline.Infinite,
                maxResponseBodyLength: BodyBudget)
            : DmFrameReader.Read(input, destination, crc, DmDeadline.Infinite, BodyBudget);
        Assert.Equal(response.Length, length);
        Assert.Equal(response, destination.A()[..length]);
        Assert.Equal(heartbeat.Length + response.Length, input.BytesRead);
        Assert.Equal((long)(heartbeat.Length + response.Length), input.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptHeartbeatCrcCannotBeSkipped(bool asynchronous)
    {
        byte[] heartbeat = Frame(269, [8, 7, 6], true);
        heartbeat[^1] ^= 1;
        using var input = new CountingStream([.. heartbeat, .. Frame(32, [1], true)]);
        var destination = new LegacyBuffer();
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await DmFrameReader.ReadAsync(input, destination, true, DmDeadline.Infinite,
                    maxResponseBodyLength: BodyBudget));
        else
            Assert.Throws<InvalidDataException>(() => DmFrameReader.Read(input, destination, true,
                DmDeadline.Infinite, BodyBudget));
        Assert.Equal(heartbeat.Length, input.BytesRead);
        Assert.Equal((long)heartbeat.Length, input.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultNonLobBudgetStillAcceptsLargerResponses(bool asynchronous)
    {
        byte[] body = new byte[256 * 1024];
        body[0] = 0xA7; body[^1] = 0x52;
        byte[] frame = Frame(101, body, false);
        using var input = new CountingStream(frame);
        var destination = new LegacyBuffer();
        int total = asynchronous
            ? await DmFrameReader.ReadAsync(input, destination, false, DmDeadline.Infinite)
            : DmFrameReader.Read(input, destination, false, DmDeadline.Infinite);
        Assert.Equal(frame.Length, total);
        Assert.Equal(frame.Length, input.BytesRead);
        Assert.Equal((long)frame.Length, input.Position);
        Assert.Equal(0xA7, destination.A()[64]);
        Assert.Equal(0x52, destination.A()[total - 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalTotalFrameCeilingStillRejectsHeaderOnly(bool asynchronous)
    {
        using var input = new CountingStream(Header(101, 64 * 1024 * 1024 - 64 + 1));
        var destination = new LegacyBuffer();
        byte[] initialStorage = destination.A();
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await DmFrameReader.ReadAsync(input, destination, false, DmDeadline.Infinite));
        else
            Assert.Throws<InvalidDataException>(() => DmFrameReader.Read(input, destination, false, DmDeadline.Infinite));
        Assert.Equal(64, input.BytesRead);
        Assert.Equal(64L, input.Position);
        Assert.Equal(0, input.BodyReadCalls);
        Assert.Same(initialStorage, destination.A());
    }

    private static byte[] Header(short command, int length, int error = 0)
    {
        byte[] header = new byte[64];
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(4), command);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6), length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), error);
        ChecksumHeader(header);
        return header;
    }

    private static void ChecksumHeader(byte[] header)
    {
        header[19] = 0;
        for (int index = 0; index < 19; index++) header[19] ^= header[index];
    }

    private static byte[] Frame(short command, byte[] body, bool crc)
    {
        byte[] frame = new byte[64 + body.Length + (crc ? 4 : 0)];
        Header(command, frame.Length - 64).CopyTo(frame, 0);
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

    private sealed class CountingStream(byte[] bytes) : Stream
    {
        // Composition prevents MemoryStream.Read(Span) from delegating through
        // this observer's virtual Read(byte[]) and counting the same read twice.
        private readonly MemoryStream source = new(bytes, writable: false);
        internal int BytesRead { get; private set; }
        internal int BodyReadCalls { get; private set; }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= 64) BodyReadCalls++;
            int read = source.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Position >= 64) BodyReadCalls++;
            int read = source.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
    }
}
