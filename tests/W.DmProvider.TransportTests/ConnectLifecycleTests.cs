using System.Net;
using System.Net.Sockets;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class ConnectLifecycleTests
{
    [Fact]
    public void ConstructorDoesNotResolveOrConnect()
    {
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("127.0.0.1", 1);
        Assert.Equal(0, DmTransportTestHooks.AttemptedTcpConnections);
        Assert.Equal(0, DmTransportTestHooks.SuccessfulTcpConnections);
        Assert.False(transport.IsClosed);
    }

    [Fact]
    public void LocalTcpOpenKeepsExactlyOneSuccessfulSocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            DmTransportTestHooks.Reset();
            using var transport = new DmTransport("127.0.0.1", port);
            transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(3)));
            using var peer = listener.AcceptSocket();
            Assert.Equal(1, DmTransportTestHooks.SuccessfulTcpConnections);
            Assert.Equal(1, DmTransportTestHooks.AttemptedTcpConnections);
            transport.Close();
            Assert.True(transport.IsClosed);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void ExpiredDeadlineRejectsBeforeAnyTcpAttemptAndClosesTransport()
    {
        var time = new ManualTimeProvider();
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(1), time);
        time.AdvanceMilliseconds(1);
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("127.0.0.1", 1);
        Assert.Throws<TimeoutException>(() => transport.Open(deadline));
        Assert.Equal(0, DmTransportTestHooks.AttemptedTcpConnections);
        Assert.Equal(0, DmTransportTestHooks.SuccessfulTcpConnections);
        Assert.True(transport.IsClosed);
    }

    [Fact]
    public void RefusedLocalTcpConnectClosesTransport()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("127.0.0.1", port);
        Assert.Throws<SocketException>(() => transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(3))));
        Assert.Equal(1, DmTransportTestHooks.AttemptedTcpConnections);
        Assert.Equal(0, DmTransportTestHooks.SuccessfulTcpConnections);
        Assert.True(transport.IsClosed);
    }

    [Fact]
    public void MultipleAddressesConsumeOneDeadlineAndReleaseFailedCandidate()
    {
        var time = new ManualTimeProvider();
        var remainingAtAttempt = new List<int>();
        var addresses = new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            int connectors = 0;
            DmTransportTestHooks.Reset();
            using var transport = new DmTransport("fixture.local", port,
                (_, _) => Task.FromResult(addresses),
                (socket, endpoint, token) =>
                {
                    connectors++;
                    if (connectors == 1) throw new SocketException((int)SocketError.ConnectionRefused);
                    return socket.ConnectAsync(endpoint, token);
                },
                (_, remaining) =>
                {
                    remainingAtAttempt.Add(remaining);
                    if (remainingAtAttempt.Count == 1) time.AdvanceMilliseconds(600);
                });
            transport.Open(DmDeadline.Start(TimeSpan.FromMilliseconds(1000), time));
            using var peer = listener.AcceptSocket();
            Assert.Equal([1000, 400], remainingAtAttempt);
            Assert.Equal(2, DmTransportTestHooks.AttemptedTcpConnections);
            Assert.Equal(1, DmTransportTestHooks.SuccessfulTcpConnections);
            Assert.Equal(2, DmTransportTestHooks.CreatedTcpSockets);
            Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
            transport.Close();
            Assert.Equal(2, DmTransportTestHooks.DisposedTcpSockets);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void ExhaustedBudgetBetweenAddressAttemptsStopsBeforeCreatingSecondSocket()
    {
        var time = new ManualTimeProvider();
        var addresses = new[] { IPAddress.Parse("127.0.0.2"), IPAddress.Loopback };
        int addressVisits = 0;
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("fixture.local", 1,
            (_, _) => Task.FromResult(addresses),
            (_, _, _) =>
            {
                time.AdvanceMilliseconds(1000);
                throw new SocketException((int)SocketError.ConnectionRefused);
            },
            (_, _) => addressVisits++);
        Assert.Throws<TimeoutException>(() => transport.Open(DmDeadline.Start(TimeSpan.FromMilliseconds(1000), time)));
        Assert.Equal(1, addressVisits);
        Assert.Equal(1, DmTransportTestHooks.CreatedTcpSockets);
        Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
        Assert.True(transport.IsClosed);
    }

    [Fact]
    public async Task CloseDuringPendingTcpConnectDisposesCandidateOnce()
    {
        using var entered = new ManualResetEventSlim();
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("fixture.local", 1,
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback }),
            async (_, _, token) =>
            {
                entered.Set();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        Task opening = Task.Run(() => transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(10))));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        transport.Close();
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => opening.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(error is OperationCanceledException or ObjectDisposedException);
        Assert.True(transport.IsClosed);
        Assert.Equal(1, DmTransportTestHooks.CreatedTcpSockets);
        Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
        Assert.Equal(0, DmTransportTestHooks.SuccessfulTcpConnections);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }
}
