using System.IO;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Transport;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;

namespace W.DmProvider.TransportTests;

public sealed class FrameBoundaryTests
{
    // The fields come from both legacy codecs: A/T02_02000092.cs and
    // Dm/T02_0200008D.cs. This is a frame boundary test, not a fake DM server.
    private const int HeaderSize = 64;

    [Fact]
    public void OneByteHeaderAndFragmentedBodyFormOneFrame()
    {
        byte[] frame = Frame(101, [1, 2, 3, 4, 5]);
        using var stream = new FragmentedStream(frame, 1);
        var destination = new LegacyBuffer();
        int total = DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite);
        Assert.Equal(frame.Length, total);
        Assert.Equal(frame, destination.A()[..total]);
        Assert.Equal(HeaderSize, destination.a());
        Assert.Equal(1, destination.__t02_method_06000ABD());
        Assert.Equal(frame.Length, stream.BytesRead);
        Assert.Equal(frame.Length, stream.ReadCalls);
    }

    [Fact]
    public void CoalescedFramesRemainSeparate()
    {
        byte[] first = Frame(101, [1, 2]);
        byte[] second = Frame(102, [3, 4, 5]);
        using var stream = new FragmentedStream([.. first, .. second], int.MaxValue);
        var destination = new LegacyBuffer();
        Assert.Equal(first.Length, DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite));
        Assert.Equal(first, destination.A()[..first.Length]);
        Assert.Equal(first.Length, stream.BytesRead);
        Assert.Equal(second.Length, DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite));
        Assert.Equal(second, destination.A()[..second.Length]);
    }

    [Theory]
    [InlineData(10)] // header EOF
    [InlineData(66)] // body EOF
    public void TruncatedHeaderOrBodyFails(int available)
    {
        byte[] frame = Frame(101, [1, 2, 3, 4, 5]);
        using var stream = new FragmentedStream(frame[..available], 1);
        Assert.Throws<EndOfStreamException>(() =>
            DmFrameReader.Read(stream, new LegacyBuffer(), false, DmDeadline.Infinite));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(64 * 1024 * 1024)]
    public void InvalidLengthRejectedBeforeBodyReadOrGrowth(int length)
    {
        byte[] header = FrameHeader(101, length);
        using var stream = new FragmentedStream(header, 1);
        var destination = new LegacyBuffer();
        int initialCapacity = destination.A().Length;
        Assert.Throws<InvalidDataException>(() =>
            DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite));
        Assert.Equal(HeaderSize, stream.BytesRead);
        Assert.Equal(initialCapacity, destination.A().Length);
    }

    [Fact]
    public void CorruptCrcIsRejected()
    {
        byte[] frame = CrcFrame(101, [4, 5, 6]);
        frame[^1] ^= 0x40;
        using var stream = new FragmentedStream(frame, 1);
        Assert.Throws<InvalidDataException>(() =>
            DmFrameReader.Read(stream, new LegacyBuffer(), true, DmDeadline.Infinite));
    }

    [Theory]
    [InlineData(-1, 1, 100)]
    [InlineData(2, 1, 1)]
    [InlineData(int.MaxValue, 2, int.MaxValue)]
    public void ElementCountsRejectInvalidOrOverflowBeforeAllocation(int count, int width, int remaining)
    {
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ValidateCount(count, width, remaining));
    }

    [Theory]
    [InlineData(-1, 1, 100)]
    [InlineData(2, -1, 100)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(3_000_000, 1, 3_000_000)]
    public void RowCountsRejectInvalidOrExcessiveMaterialization(int rows, int columns, int remaining)
    {
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ValidateRows(rows, columns, remaining));
    }

    [Fact]
    public void DecodedPaddingBudgetAccumulatesAcrossPackagesWithoutAllocating()
    {
        long used = 0;
        DmFrameReader.ReserveDecodedValueBytes(ref used, 32 * 1024 * 1024);
        DmFrameReader.ReserveDecodedValueBytes(ref used, 32 * 1024 * 1024);
        Assert.Equal(DmFrameReader.MaxFrameSize, used);
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ReserveDecodedValueBytes(ref used, 1));
        Assert.Equal(DmFrameReader.MaxFrameSize, used);
    }

    [Fact]
    public void DecodedPaddingBudgetRejectsOverflowAndNegativeCounts()
    {
        long overflow = long.MaxValue;
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ReserveDecodedValueBytes(ref overflow, 1));
        long negative = -1;
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ReserveDecodedValueBytes(ref negative, 0));
        long valid = 0;
        Assert.Throws<InvalidDataException>(() => DmFrameReader.ReserveDecodedValueBytes(ref valid, -1));
    }

    [Fact]
    public void HeartbeatConsumesItsBodyAndDoesNotEatNextResponse()
    {
        byte[] heartbeat = Frame(DmFrameReader.HeartbeatCommand, [8, 7, 6]);
        byte[] response = Frame(101, [9, 10]);
        using var stream = new FragmentedStream([.. heartbeat, .. response], 1);
        var destination = new LegacyBuffer();
        int total = DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite);
        Assert.Equal(response.Length, total);
        Assert.Equal(response, destination.A()[..total]);
        Assert.Equal(heartbeat.Length + response.Length, stream.BytesRead);
    }

    [Fact]
    public void HeartbeatsDoNotResetAbsoluteDeadline()
    {
        byte[] heartbeat = Frame(DmFrameReader.HeartbeatCommand, [8, 7, 6]);
        byte[] response = Frame(101, [9]);
        byte[] input = [.. heartbeat, .. heartbeat, .. heartbeat, .. response];
        int offset = 0;
        var time = new ManualTimeProvider();
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(10), time);
        void ReadExactly(byte[] destination, int start, int count)
        {
            input.AsSpan(offset, count).CopyTo(destination.AsSpan(start, count));
            offset += count;
            if (offset % heartbeat.Length == 0 && offset <= 3 * heartbeat.Length)
                time.AdvanceMilliseconds(4);
        }
        Assert.Throws<TimeoutException>(() => DmFrameReader.Read(
            ReadExactly, new LegacyBuffer(), (buffer, total) => DmFrameReader.ValidateChecksum(buffer, total, false), deadline));
        Assert.Equal(3 * heartbeat.Length, offset);
    }

    private static byte[] Frame(short command, byte[] body)
    {
        byte[] frame = new byte[HeaderSize + body.Length];
        byte[] header = FrameHeader(command, body.Length);
        header.CopyTo(frame, 0);
        body.CopyTo(frame, HeaderSize);
        return frame;
    }

    private static byte[] FrameHeader(short command, int bodyLength)
    {
        byte[] header = new byte[HeaderSize];
        BitConverter.GetBytes(command).CopyTo(header, 4);
        BitConverter.GetBytes(bodyLength).CopyTo(header, 6);
        for (int i = 0; i < 19; i++) header[19] ^= header[i];
        return header;
    }

    private static byte[] CrcFrame(short command, byte[] body)
    {
        byte[] result = new byte[HeaderSize + body.Length + 4];
        FrameHeader(command, body.Length + 4).CopyTo(result, 0);
        body.CopyTo(result, HeaderSize);
        uint crc = 0xffffffff;
        for (int i = 0; i < result.Length - 4; i++)
        {
            crc ^= result[i];
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        BitConverter.GetBytes(~crc).CopyTo(result, result.Length - 4);
        return result;
    }

    private sealed class FragmentedStream(byte[] bytes, int maxRead) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public int ReadCalls { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            int read = base.Read(buffer, offset, Math.Min(maxRead, count));
            BytesRead += read;
            return read;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }
}
