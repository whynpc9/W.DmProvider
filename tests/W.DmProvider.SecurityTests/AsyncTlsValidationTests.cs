using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class AsyncTlsValidationTests
{
    [Theory]
    [InlineData(false, "localhost")] // Valid hostname, unrelated trust authority.
    [InlineData(true, "wrong.invalid")] // Valid authority, certificate identity mismatch.
    public async Task InvalidTrustOrHostnameRejectsAsyncUpgradeAndPreventsRawFallback(bool trustIssuer, string targetHost)
    {
        using var issuer = new CertificateMaterial();
        using var unrelated = new CertificateMaterial();
        using var serverCertificate = issuer.Server();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task server = ServeAsync(listener, serverCertificate, timeout.Token);
        var options = Options(targetHost, port, trustIssuer ? issuer.AuthorityPath : unrelated.AuthorityPath);
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("127.0.0.1", port);
        await transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(5)), timeout.Token);
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            transport.UpgradeTlsAsync(options, DmDeadline.Start(TimeSpan.FromSeconds(5)), timeout.Token));

        Assert.True(transport.IsClosed);
        Assert.Equal(0, DmTransportTestHooks.SuccessfulTlsUpgrades);
        Assert.Equal(1, DmTransportTestHooks.FailedTlsUpgrades);
        Assert.Equal(1, DmTransportTestHooks.CreatedTcpSockets);
        Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
        // The failed upgrade destroys its captured TCP channel: neither async
        // I/O nor the retained R1 sync entrypoint can silently send plaintext.
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.SendAllAsync([1], 0, 1, DmDeadline.Infinite, 0).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.ReadSomeAsync(new byte[1], 0, 1, DmDeadline.Infinite, 0).AsTask());
        Assert.Throws<ObjectDisposedException>(() =>
            transport.SendAll([1], 0, 1, DmDeadline.Infinite, 0));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            transport.UpgradeTlsAsync(options, DmDeadline.Infinite));
        transport.Close();
        Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TrustedMatchingCertificateAuthenticatesThroughAsyncUpgrade()
    {
        using var issuer = new CertificateMaterial();
        using var certificate = issuer.Server();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task server = ServeAsync(listener, certificate, timeout.Token);
        DmTransportTestHooks.Reset();
        using var transport = new DmTransport("127.0.0.1", port);
        await transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(5)), timeout.Token);
        await transport.UpgradeTlsAsync(Options("localhost", port, issuer.AuthorityPath),
            DmDeadline.Start(TimeSpan.FromSeconds(5)), timeout.Token);
        Assert.False(transport.IsClosed);
        Assert.Equal(1, DmTransportTestHooks.SuccessfulTlsUpgrades);
        Assert.Equal(0, DmTransportTestHooks.FailedTlsUpgrades);
        Assert.True(DmTransportTestHooks.LastNegotiatedTlsProtocol is SslProtocols.Tls12 or SslProtocols.Tls13);
        transport.Close();
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, DmTransportTestHooks.DisposedTcpSockets);
    }

    private static DmTlsOptions Options(string targetHost, int port, string authorityPath) =>
        DmTlsOptions.FromSettings(new DmConnectionStringBuilder
        {
            Server = targetHost,
            Port = port,
            TransportSecurity = DmTransportSecurity.RequireTls,
            TlsCaCertificatePath = authorityPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        }.ToSettings());

    // Start directly on the async accept/authenticate APIs. No worker thread is
    // held while the client completes its handshake or closes a rejected channel.
    private static async Task ServeAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken token)
    {
        using var peer = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        using var ssl = new SslStream(peer.GetStream());
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, token).ConfigureAwait(false);
            // TLS 1.3 can complete on the server before the client rejects its
            // certificate. Wait for the resulting client close in either case.
            await ssl.ReadAsync(new byte[1], token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is AuthenticationException or IOException)
        {
            // The rejecting client can abort authentication or send a TLS alert.
        }
    }
}
