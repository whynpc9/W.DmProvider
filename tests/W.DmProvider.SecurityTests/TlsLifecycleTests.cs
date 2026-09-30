using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class TlsLifecycleTests
{
    [Fact]
    public async Task AbsoluteConnectDeadlineIncludesPendingTlsHandshake()
    {
        using var certificates = new CertificateMaterial();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            DmTransportTestHooks.Reset();
            using var transport = new DmTransport("127.0.0.1", port);
            var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(300));
            transport.Open(deadline);
            using var peer = await accepting.WaitAsync(TimeSpan.FromSeconds(3));
            var options = Options(certificates, port);
            Assert.Throws<TimeoutException>(() => transport.UpgradeTls(options, deadline));
            Assert.True(transport.IsClosed);
            Assert.Equal(1, DmTransportTestHooks.FailedTlsUpgrades);
            Assert.Equal(DmTransportTestHooks.CreatedTcpSockets, DmTransportTestHooks.DisposedTcpSockets);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task CloseAbortsPendingTlsAuthenticationAndDisposesSocketOnce()
    {
        using var certificates = new CertificateMaterial();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            DmTransportTestHooks.Reset();
            using var transport = new DmTransport("127.0.0.1", port);
            transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(10)));
            using var peer = await accepting.WaitAsync(TimeSpan.FromSeconds(3));
            var options = Options(certificates, port);
            Task<Exception?> pending = Task.Run(() => Capture(() =>
                transport.UpgradeTls(options, DmDeadline.Start(TimeSpan.FromSeconds(10)))));
            byte[] hello = new byte[1];
            Assert.Equal(1, await peer.GetStream().ReadAsync(hello).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            var stopwatch = Stopwatch.StartNew();
            transport.Close();
            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.NotNull(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(transport.IsClosed);
            Assert.Equal(1, DmTransportTestHooks.CreatedTcpSockets);
            Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
            Assert.Equal(0, DmTransportTestHooks.SuccessfulTlsUpgrades);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task CloseAbortsBlockedTlsReadAndDisposesSocketOnce()
    {
        using var certificates = new CertificateMaterial();
        using var serverCertificate = certificates.Server();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var peerRelease = new ManualResetEventSlim();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task peer = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync();
                using var ssl = new SslStream(accepted.GetStream());
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, timeout.Token);
                peerRelease.Wait(TimeSpan.FromSeconds(5));
            });
            DmTransportTestHooks.Reset();
            using var transport = new DmTransport("127.0.0.1", port);
            transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(5)));
            transport.UpgradeTls(Options(certificates, port), DmDeadline.Start(TimeSpan.FromSeconds(5)));
            using var entered = new ManualResetEventSlim();
            DmTransportTestHooks.BeforeTlsRead = () => entered.Set();
            try
            {
                Task<Exception?> reading = Task.Run(() => Capture(() => transport.ReadExactly(
                    new byte[1], 0, 1, DmDeadline.Start(TimeSpan.FromSeconds(10)), 0)));
                Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
                var stopwatch = Stopwatch.StartNew();
                transport.Close();
                stopwatch.Stop();
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
                Assert.NotNull(await reading.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal(1, DmTransportTestHooks.CreatedTcpSockets);
                Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
            }
            finally
            {
                DmTransportTestHooks.BeforeTlsRead = null;
                peerRelease.Set();
                await peer.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
        finally { listener.Stop(); }
    }

    private static DmTlsOptions Options(CertificateMaterial certificates, int port)
    {
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", Port = port, TransportSecurity = DmTransportSecurity.RequireTls,
            TlsCaCertificatePath = certificates.AuthorityPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        };
        return DmTlsOptions.FromSettings(builder.ToSettings());
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }
}
