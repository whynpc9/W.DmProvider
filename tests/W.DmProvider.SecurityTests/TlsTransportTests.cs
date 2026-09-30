using System.Security.Authentication;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class TlsTransportTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public async Task ValidCaAndMatchingDnsOrIpSanUpgradeTls(string target)
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, target, certificates.AuthorityPath);
        Assert.True(result.ClientAuthenticated);
        Assert.True(result.ServerAuthenticated);
        Assert.True(result.ClientProtocol is SslProtocols.Tls12 or SslProtocols.Tls13);
        Assert.Equal(1, result.SuccessfulUpgrades);
        Assert.Equal(0, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task UnknownCaFailsClosedWithoutSuccessfulUpgrade()
    {
        using var certificates = new CertificateMaterial();
        using var stranger = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", stranger.AuthorityPath);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(0, result.SuccessfulUpgrades);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task ExpiredServerCertificateFailsClosed()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server(valid: false);
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task WrongHostFailsEvenWithTrustedCa()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "wrong.local", certificates.AuthorityPath);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task ClientAuthOnlyLeafCannotMasqueradeAsServer()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server(serverAuth: false);
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task MutualTlsAcceptsExplicitClientCertificate()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath,
            requireClientCertificate: true, sendClientCertificate: true);
        Assert.True(result.ClientAuthenticated);
        Assert.True(result.ServerAuthenticated);
        Assert.True(result.ClientCertificateAccepted);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task MutualTlsAcceptsPasswordProtectedPlatformScopedPfx()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath,
            requireClientCertificate: true, sendClientCertificate: true, usePfxClient: true);
        Assert.True(result.ClientAuthenticated);
        Assert.True(result.ServerAuthenticated);
        Assert.True(result.ClientCertificateAccepted);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task PlatformScopedPfxStillRejectsUnknownServerCaAndCloses()
    {
        using var certificates = new CertificateMaterial();
        using var stranger = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", stranger.AuthorityPath,
            requireClientCertificate: true, sendClientCertificate: true, usePfxClient: true);
        Assert.False(result.ClientAuthenticated);
        Assert.Equal(1, result.FailedUpgrades);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }

    [Fact]
    public async Task MutualTlsRejectsMissingClientCertificate()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        var result = await TlsLoopbackHarness.ConnectAsync(certificates, server, "localhost", certificates.AuthorityPath,
            requireClientCertificate: true);
        Assert.False(result.ServerAuthenticated);
        Assert.False(result.ClientCertificateAccepted);
        Assert.Equal(result.CreatedSockets, result.DisposedSockets);
    }
}
