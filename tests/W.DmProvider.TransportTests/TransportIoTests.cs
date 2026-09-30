using System.IO;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class TransportIoTests
{
    [Fact]
    public void ShortSendWritesEveryByteInOrder()
    {
        using var channel = new FragmentedChannel(sendChunk: 1);
        using var transport = new DmTransport(channel);
        byte[] bytes = [10, 11, 12, 13, 14];
        var observed = new List<int>();
        transport.SendAll(bytes, 0, bytes.Length, DmDeadline.Infinite, 0, observed.Add);
        Assert.Equal(bytes, channel.Sent.ToArray());
        Assert.Equal([1, 1, 1, 1, 1], observed);
        Assert.Equal(5, channel.SendCalls);
    }

    [Fact]
    public void ZeroProgressSendFailsWithoutRetryLoop()
    {
        using var channel = new FragmentedChannel(sendChunk: 0);
        using var transport = new DmTransport(channel);
        Assert.Throws<IOException>(() => transport.SendAll([1, 2], 0, 2, DmDeadline.Infinite, 0));
        Assert.Equal(1, channel.SendCalls);
        Assert.Empty(channel.Sent);
    }

    [Fact]
    public void OneByteReceivesReassembleExactBuffer()
    {
        using var channel = new FragmentedChannel(receiveChunk: 1, inbound: [2, 3, 5, 7]);
        using var transport = new DmTransport(channel);
        byte[] destination = new byte[4];
        transport.ReadExactly(destination, 0, destination.Length, DmDeadline.Infinite, 0);
        Assert.Equal([2, 3, 5, 7], destination);
        Assert.Equal(4, channel.ReceiveCalls);
    }

    [Fact]
    public void EofDuringExactReadIsTruncation()
    {
        using var channel = new FragmentedChannel(receiveChunk: 1, inbound: [2, 3]);
        using var transport = new DmTransport(channel);
        Assert.Throws<EndOfStreamException>(() => transport.ReadExactly(new byte[3], 0, 3, DmDeadline.Infinite, 0));
        Assert.Equal(3, channel.ReceiveCalls);
    }

    [Fact]
    public void CloseReleasesInjectedChannelOnce()
    {
        using var channel = new FragmentedChannel();
        using var transport = new DmTransport(channel);
        transport.Close();
        transport.Close();
        Assert.True(transport.IsClosed);
        Assert.Equal(1, channel.DisposeCalls);
    }

    private sealed class FragmentedChannel(int sendChunk = int.MaxValue, int receiveChunk = int.MaxValue,
        byte[]? inbound = null) : IDmByteChannel
    {
        private readonly byte[] inbound = inbound ?? [];
        private int inboundOffset;
        public List<byte> Sent { get; } = [];
        public int SendCalls { get; private set; }
        public int ReceiveCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool IsClosed => DisposeCalls != 0;

        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            SendCalls++;
            int n = Math.Min(sendChunk, count);
            Sent.AddRange(buffer.AsSpan(offset, n).ToArray());
            return n;
        }

        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            ReceiveCalls++;
            int n = Math.Min(Math.Min(receiveChunk, count), inbound.Length - inboundOffset);
            if (n == 0) return 0;
            Array.Copy(inbound, inboundOffset, buffer, offset, n);
            inboundOffset += n;
            return n;
        }

        public void Dispose() => DisposeCalls++;
    }
}
