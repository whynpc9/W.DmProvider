using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using W.Dm;
using W.Dm.Internal.Transport;

namespace W.DmProvider.SecurityTests;

internal static class TlsLoopbackHarness
{
    private const string PfxCanary = "SyntheticT07Pfx_9A";
    internal sealed record Outcome(bool ClientAuthenticated, bool ServerAuthenticated,
        bool ClientCertificateAccepted, SslProtocols ClientProtocol, long CreatedSockets, long DisposedSockets,
        long SuccessfulUpgrades, long FailedUpgrades);

    internal static async Task<Outcome> ConnectAsync(CertificateMaterial certificates,
        X509Certificate2 serverCertificate, string targetHost, string trustRootPath,
        bool requireClientCertificate = false, bool sendClientCertificate = false, bool usePfxClient = false,
        Action<Exception, DmTransport>? onClientFailure = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        bool acceptedClientCertificate = false;
        Task<bool> server = Task.Run(async () =>
        {
            try
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var ssl = new SslStream(peer.GetStream(), leaveInnerStreamOpen: false);
                var serverOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    EnabledSslProtocols = SslProtocols.None,
                    ClientCertificateRequired = requireClientCertificate,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    {
                        if (!requireClientCertificate) return true;
                        if (cert is not X509Certificate2 client) return false;
                        using var chain = new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(certificates.Authority);
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
                        acceptedClientCertificate = chain.Build(client);
                        return acceptedClientCertificate;
                    }
                };
                await ssl.AuthenticateAsServerAsync(serverOptions, timeout.Token);
                return ssl.IsAuthenticated && ssl.IsEncrypted;
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
            {
                return false;
            }
        }, timeout.Token);

        var builder = new DmConnectionStringBuilder
        {
            Server = targetHost,
            Port = port,
            TransportSecurity = DmTransportSecurity.RequireTls,
            TlsCaCertificatePath = trustRootPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        };
        if (sendClientCertificate)
        {
            if (usePfxClient)
            {
                certificates.WriteClientPfx(PfxCanary);
                builder.TlsClientCertificatePath = certificates.ClientPfxPath;
                builder.TlsClientCertificatePassword = PfxCanary;
                var settings = builder.ToSettings();
                if (settings.ToConnectionString(includeSecrets: false).Contains(PfxCanary, StringComparison.Ordinal) ||
                    settings.WithSchema("T07_SCHEMA").ToConnectionString(includeSecrets: false)
                        .Contains(PfxCanary, StringComparison.Ordinal))
                    throw new InvalidOperationException("TLS secret appeared in a redacted snapshot.");
            }
            else
            {
                certificates.Client(writePemFiles: true);
                builder.TlsClientCertificatePath = certificates.ClientCertificatePath;
                builder.TlsClientPrivateKeyPath = certificates.ClientPrivateKeyPath;
            }
        }
        var options = DmTlsOptions.FromSettings(builder.ToSettings());
        DmTransportTestHooks.Reset();
        bool clientAuthenticated = false;
        SslProtocols protocol = SslProtocols.None;
        using (var transport = new DmTransport("127.0.0.1", port))
        {
            try
            {
                transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(5)));
                transport.UpgradeTls(options, DmDeadline.Start(TimeSpan.FromSeconds(5)));
                clientAuthenticated = true;
                protocol = DmTransportTestHooks.LastNegotiatedTlsProtocol;
                await server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or TimeoutException or
                                       OperationCanceledException or ObjectDisposedException)
            {
                clientAuthenticated = false;
                onClientFailure?.Invoke(ex, transport);
            }
        }
        bool serverAuthenticated = await server.WaitAsync(TimeSpan.FromSeconds(5));
        return new Outcome(clientAuthenticated, serverAuthenticated, acceptedClientCertificate, protocol,
            DmTransportTestHooks.CreatedTcpSockets, DmTransportTestHooks.DisposedTcpSockets,
            DmTransportTestHooks.SuccessfulTlsUpgrades, DmTransportTestHooks.FailedTlsUpgrades);
    }
}
