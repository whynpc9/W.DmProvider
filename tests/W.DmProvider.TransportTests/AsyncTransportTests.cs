using System.IO;
using System.Net;
using System.Net.Sockets;
using W.Dm;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;

namespace W.DmProvider.TransportTests;

public sealed class AsyncTransportTests
{
    [Fact]
    public async Task PendingSendAndReadUseOnlyAsyncChannelAndReassembleShortIo()
    {
        using var channel = new BarrierChannel([2, 3, 5, 7], chunk: 1);
        using var transport = new DmTransport(channel);
        Task send = transport.SendAllAsync([11, 13, 17], 0, 3, DmDeadline.Infinite, 0).AsTask();
        Assert.False(send.IsCompleted);
        Assert.Equal(1, channel.SendCalls);
        channel.Release();
        await send.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 11, 13, 17 }, channel.Sent);
        byte[] result = new byte[4];
        await transport.ReadExactlyAsync(result, 0, 4, DmDeadline.Infinite, 0);
        Assert.Equal(new byte[] { 2, 3, 5, 7 }, result);
        Assert.Equal(4, channel.ReceiveCalls);
    }

    [Fact]
    public async Task ReadStaysPendingUntilBarrierRelease()
    {
        using var channel = new BarrierChannel([31]);
        using var transport = new DmTransport(channel);
        byte[] result = new byte[1];
        Task read = transport.ReadExactlyAsync(result, 0, 1, DmDeadline.Infinite, 0).AsTask();
        Assert.False(read.IsCompleted);
        Assert.Equal(1, channel.ReceiveCalls);
        channel.Release();
        await read.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(31, result[0]);
    }

    [Fact]
    public async Task ZeroProgressSendAndTruncatedReceiveFail()
    {
        using var sending = new BarrierChannel([], chunk: 0);
        sending.Release();
        using var sendTransport = new DmTransport(sending);
        await Assert.ThrowsAsync<IOException>(() => sendTransport.SendAllAsync([1], 0, 1, DmDeadline.Infinite, 0).AsTask());
        Assert.Equal(1, sending.SendCalls);
        using var receiving = new BarrierChannel([1], chunk: 1);
        receiving.Release();
        using var readTransport = new DmTransport(receiving);
        await Assert.ThrowsAsync<EndOfStreamException>(() => readTransport.ReadExactlyAsync(new byte[2], 0, 2, DmDeadline.Infinite, 0).AsTask());
        Assert.Equal(2, receiving.ReceiveCalls);
    }

    [Fact]
    public async Task PreCanceledSendDoesNotTouchChannelAndCanBeRetried()
    {
        using var channel = new BarrierChannel([]);
        using var transport = new DmTransport(channel);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.SendAllAsync([1], 0, 1, DmDeadline.Infinite, 0, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, channel.SendCalls);
        Assert.False(transport.IsClosed);
        channel.Release();
        await transport.SendAllAsync([1], 0, 1, DmDeadline.Infinite, 0);
    }

    [Fact]
    public async Task PendingReadHonorsCallerCancellationAndClose()
    {
        using var channel = new BarrierChannel([]);
        using var transport = new DmTransport(channel);
        using var cancellation = new CancellationTokenSource();
        Task read = transport.ReadSomeAsync(new byte[1], 0, 1, DmDeadline.Infinite, 0, cancellation.Token).AsTask();
        Assert.False(read.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Task second = transport.ReadSomeAsync(new byte[1], 0, 1, DmDeadline.Infinite, 0).AsTask();
        Assert.False(second.IsCompleted);
        transport.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, channel.DisposeCalls);
    }

    [Fact]
    public async Task DnsAndConnectRemainPendingAndShareCancellation()
    {
        var dns = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var transport = new DmTransport("barrier.local", 1,
            (_, _) => dns.Task,
            async (_, _, token) => { entered.SetResult(); await connect.Task.WaitAsync(token); });
        Task opening = transport.OpenAsync(DmDeadline.Infinite, cancellation.Token);
        Assert.False(opening.IsCompleted);
        Assert.False(entered.Task.IsCompleted);
        dns.SetResult([IPAddress.Loopback]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(opening.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(transport.IsClosed);
    }

    [Fact]
    public async Task SocketReadAndTlsAuthenticationSuspendWhilePeerWithholdsReply()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var transport = new DmTransport("127.0.0.1", port);
        Task<Socket> accepting = listener.AcceptSocketAsync();
        await transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        using Socket peer = await accepting.WaitAsync(TimeSpan.FromSeconds(3));
        using var readCancellation = new CancellationTokenSource();
        Task read = transport.ReadSomeAsync(new byte[1], 0, 1, DmDeadline.Infinite, 0, readCancellation.Token).AsTask();
        Assert.False(read.IsCompleted);
        readCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));

        var settings = new DmConnectionStringBuilder { Server = "127.0.0.1", Port = port }.ToSettings();
        using var tlsCancellation = new CancellationTokenSource();
        Task tls = transport.UpgradeTlsAsync(DmTlsOptions.FromSettings(settings), DmDeadline.Infinite, tlsCancellation.Token);
        Assert.False(tls.IsCompleted);
        tlsCancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tls.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(tlsCancellation.Token, error.CancellationToken);
        Assert.True(transport.IsClosed);
    }

    [Fact]
    public async Task FrameReaderAwaitsFragmentsAndConsumesHeartbeatBody()
    {
        byte[] heartbeat = Frame(DmFrameReader.HeartbeatCommand, [8, 9]);
        byte[] response = Frame(101, [31, 37]);
        using var channel = new BarrierChannel([.. heartbeat, .. response], chunk: 1);
        using var transport = new DmTransport(channel);
        var result = new LegacyBuffer();
        Task<int> reading = DmFrameReader.ReadAsync((buffer, offset, count, token) =>
            transport.ReadExactlyAsync(buffer, offset, count, DmDeadline.Infinite, 0, token),
            result, (buffer, total) => DmFrameReader.ValidateChecksum(buffer, total, false), DmDeadline.Infinite,
            validateHeader: DmFrameReader.ValidateHeaderChecksum).AsTask();
        Assert.False(reading.IsCompleted);
        channel.Release();
        Assert.Equal(response.Length, await reading.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(response, result.A()[..response.Length]);
        Assert.Equal(heartbeat.Length + response.Length, channel.ReceiveCalls);
    }

    [Fact]
    public async Task FrameReaderRejectsHeaderBeforeReadingBody()
    {
        byte[] input = Frame(101, [1]);
        input[19] ^= 1;
        using var channel = new BarrierChannel(input);
        channel.Release();
        using var transport = new DmTransport(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => DmFrameReader.ReadAsync((buffer, offset, count, token) =>
            transport.ReadExactlyAsync(buffer, offset, count, DmDeadline.Infinite, 0, token),
            new LegacyBuffer(), (buffer, total) => DmFrameReader.ValidateChecksum(buffer, total, false),
            DmDeadline.Infinite, validateHeader: DmFrameReader.ValidateHeaderChecksum).AsTask());
        Assert.Equal(64, channel.BytesRead);
    }

    [Fact]
    public async Task StreamFrameReaderUsesAsyncReadAndRejectsCorruptHeartbeat()
    {
        byte[] heartbeat = Frame(DmFrameReader.HeartbeatCommand, [1]);
        heartbeat[19] ^= 1;
        using var stream = new AsyncOnlyStream(heartbeat);
        Task<int> reading = DmFrameReader.ReadAsync(stream, new LegacyBuffer(), false, DmDeadline.Infinite).AsTask();
        Assert.False(reading.IsCompleted);
        stream.Release();
        await Assert.ThrowsAsync<InvalidDataException>(() => reading.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(64, stream.Position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(64 * 1024 * 1024)]
    public async Task AsyncFrameRejectsInvalidLengthBeforeBodyAllocation(int length)
    {
        byte[] header = Frame(101, []);
        BitConverter.GetBytes(length).CopyTo(header, 6);
        using var channel = new BarrierChannel(header);
        channel.Release();
        using var transport = new DmTransport(channel);
        var destination = new LegacyBuffer();
        int capacity = destination.A().Length;
        await Assert.ThrowsAsync<InvalidDataException>(() => DmFrameReader.ReadAsync((buffer, offset, count, token) =>
            transport.ReadExactlyAsync(buffer, offset, count, DmDeadline.Infinite, 0, token), destination,
            (buffer, total) => DmFrameReader.ValidateChecksum(buffer, total, false), DmDeadline.Infinite).AsTask());
        Assert.Equal(64, channel.BytesRead);
        Assert.Equal(capacity, destination.A().Length);
    }

    private sealed class AsyncOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.SetResult();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous stream read is forbidden.");
        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken token = default)
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            int count = Math.Min(Math.Min(1, destination.Length), bytes.Length - (int)Position);
            bytes.AsMemory((int)Position, count).CopyTo(destination);
            Position += count;
            return count;
        }
    }

    [Fact]
    public async Task InvocationAndWireOwnershipFlowAcrossAwaitWithoutChangingSiblingContext()
    {
        var first = new DmSession();
        first.CompleteHandshakeForTests();
        using var lease = first.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        using var exchange = first.BeginWireExchange();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Child()
        {
            var second = new DmSession();
            second.CompleteHandshakeForTests();
            using var childLease = second.BeginExecution(DmOperationPurpose.Query);
            using var childInvocation = childLease.BeginInvocation();
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            Assert.Same(childInvocation, DmInvocation.Current);
            // Parent wire ownership remains inherited, and nested wire use is rejected.
            Assert.Throws<InvalidOperationException>(() => second.BeginWireExchange());
        }
        Task child = Child();
        await entered.Task;
        Assert.Same(invocation, DmInvocation.Current);
        Assert.Same(exchange, DmWireExchange.Current);
        first.RequireActiveWireExchange();
        release.SetResult();
        await child;
        Assert.Same(invocation, DmInvocation.Current);
        first.RequireActiveWireExchange();
        exchange.Complete();
    }

    [Fact]
    public async Task VirtualClockExpiresPendingReadWithoutSocketTimeoutOrWorkerSleep()
    {
        var clock = new TimerClock();
        using var channel = new BarrierChannel([]);
        using var transport = new DmTransport(channel);
        Task read = transport.ReadSomeAsync(new byte[1], 0, 1,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock), 0).AsTask();
        Assert.False(read.IsCompleted);
        clock.Advance(100);
        await Assert.ThrowsAsync<TimeoutException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ReadIdleBudgetIsIndependentAndCannotRenewTotalDeadline()
    {
        var clock = new TimerClock();
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock);
        using var first = new DmIoCancellation(deadline, default, default, 60);
        clock.Advance(40);
        Assert.False(first.Token.IsCancellationRequested);
        using var next = new DmIoCancellation(deadline, default, default, 60);
        clock.Advance(59);
        Assert.False(next.Token.IsCancellationRequested);
        Assert.Equal(1, deadline.RemainingMilliseconds);
        clock.Advance(1);
        Assert.True(next.Token.IsCancellationRequested);
        using var channel = new BarrierChannel([]);
        using var transport = new DmTransport(channel);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.ReadSomeAsync(new byte[1], 0, 1, deadline, 60).AsTask());
        Assert.Equal(0, channel.ReceiveCalls);
    }

    [Fact]
    public async Task HeartbeatProgressNeverRenewsAsyncFrameDeadline()
    {
        byte[] heartbeat = Frame(DmFrameReader.HeartbeatCommand, [1]);
        byte[] input = [.. heartbeat, .. heartbeat, .. heartbeat];
        var clock = new TimerClock();
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(10), clock);
        int offset = 0;
        ValueTask Read(byte[] buffer, int start, int count, CancellationToken token)
        {
            input.AsSpan(offset, count).CopyTo(buffer.AsSpan(start, count));
            offset += count;
            if (offset % heartbeat.Length == 0) clock.Advance(4);
            return ValueTask.CompletedTask;
        }
        await Assert.ThrowsAsync<TimeoutException>(() => DmFrameReader.ReadAsync(Read, new LegacyBuffer(),
            (buffer, total) => DmFrameReader.ValidateChecksum(buffer, total, false), deadline).AsTask());
        Assert.Equal(3 * heartbeat.Length, offset);
    }

    [Fact]
    public async Task DisposedChildInvocationDoesNotRemainAmbientInCapturedFlow()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        using var lease = session.BeginExecution(DmOperationPurpose.Query);
        var invocation = lease.BeginInvocation();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Child()
        {
            await released.Task;
            Assert.Null(DmInvocation.Current);
        }
        Task child = Child();
        invocation.Dispose();
        Assert.Null(DmInvocation.Current);
        released.SetResult();
        await child;
    }

    private sealed class TimerClock : TimeProvider
    {
        private long timestamp;
        private readonly List<ManualTimer> timers = [];
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(long milliseconds)
        {
            timestamp += milliseconds;
            foreach (var timer in timers.ToArray()) timer.FireIfDue(timestamp);
        }
        private sealed class ManualTimer(TimerClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long due = long.MaxValue;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (disposed) return false;
                due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.timestamp + (long)dueTime.TotalMilliseconds;
                return true;
            }
            public void FireIfDue(long now)
            {
                if (disposed || now < due) return;
                due = long.MaxValue;
                callback(state);
            }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static byte[] Frame(short command, byte[] body)
    {
        byte[] result = new byte[64 + body.Length];
        BitConverter.GetBytes(command).CopyTo(result, 4);
        BitConverter.GetBytes(body.Length).CopyTo(result, 6);
        for (int i = 0; i < 19; i++) result[19] ^= result[i];
        body.CopyTo(result, 64);
        return result;
    }

    private sealed class BarrierChannel(byte[] input, int chunk = int.MaxValue) : IDmByteChannel
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int offset;
        public List<byte> Sent { get; } = [];
        public int SendCalls { get; private set; }
        public int ReceiveCalls { get; private set; }
        public int BytesRead => offset;
        public int DisposeCalls { get; private set; }
        public bool IsClosed => DisposeCalls != 0;
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("Synchronous send is forbidden.");
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("Synchronous receive is forbidden.");
        public void Release() => release.TrySetResult();
        public async ValueTask<int> SendAsync(byte[] buffer, int start, int count, CancellationToken token)
        {
            SendCalls++;
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            int n = Math.Min(chunk, count);
            Sent.AddRange(buffer.AsSpan(start, n).ToArray());
            return n;
        }
        public async ValueTask<int> ReceiveAsync(byte[] buffer, int start, int count, CancellationToken token)
        {
            ReceiveCalls++;
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            int n = Math.Min(Math.Min(chunk, count), input.Length - offset);
            input.AsSpan(offset, n).CopyTo(buffer.AsSpan(start, n));
            offset += n;
            return n;
        }
        public void Dispose() => DisposeCalls++;
    }
}
