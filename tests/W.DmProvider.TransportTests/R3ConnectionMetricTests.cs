using System.Net;
using System.Net.Sockets;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransportTests;

// The connection metric counts established physical connections, not candidate
// sockets: a failed address attempt records neither create nor close.
[Trait("Category", "Contract")]
[Trait("Feature", "Transport")]
public sealed class R3ConnectionMetricTests
{
    [Fact]
    public void FailedAddressCandidateIsNotCountedAsAnEstablishedConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var refused = IPAddress.Parse("192.0.2.1"); // TEST-NET-1; the connector rejects it deterministically.
            DmTransportTestHooks.Reset();
            long createdBefore = DmDiagnosticsCore.Snapshot.ConnectionsCreated;
            using var transport = new DmTransport("metric.invalid", port,
                (_, _) => Task.FromResult(new[] { refused, IPAddress.Loopback }),
                (socket, endpoint, token) => endpoint.Address.Equals(refused)
                    ? throw new SocketException((int)SocketError.ConnectionRefused)
                    : socket.ConnectAsync(endpoint, token));
            Task<Socket> accepting = listener.AcceptSocketAsync();
            transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(5)));
            using Socket peer = accepting.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.Equal(2, DmTransportTestHooks.CreatedTcpSockets); // Both candidate sockets exist for the hook.
            Assert.Equal(createdBefore + 1, DmDiagnosticsCore.Snapshot.ConnectionsCreated);
            long closedBefore = DmDiagnosticsCore.Snapshot.ConnectionsClosed;
            transport.Close();
            Assert.Equal(closedBefore + 1, DmDiagnosticsCore.Snapshot.ConnectionsClosed);
            Assert.Equal(2, DmTransportTestHooks.DisposedTcpSockets);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task FullyFailedConnectRecordsNoConnectionMetric()
    {
        long createdBefore = DmDiagnosticsCore.Snapshot.ConnectionsCreated;
        long closedBefore = DmDiagnosticsCore.Snapshot.ConnectionsClosed;
        using var transport = new DmTransport("metric.invalid", 9,
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }),
            (_, _, _) => throw new SocketException((int)SocketError.ConnectionRefused));
        await Assert.ThrowsAnyAsync<SocketException>(() => transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(5))));
        transport.Close();
        Assert.Equal(createdBefore, DmDiagnosticsCore.Snapshot.ConnectionsCreated);
        Assert.Equal(closedBefore, DmDiagnosticsCore.Snapshot.ConnectionsClosed);
    }
}
