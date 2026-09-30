using System.Security.Cryptography.X509Certificates;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class TlsOptionsTests
{
    [Fact]
    public void EphemeralAuthorityAndServerCarryExpectedConstraintsAndSan()
    {
        using var certificates = new CertificateMaterial();
        using var server = certificates.Server();
        Assert.True(certificates.Authority.Extensions.OfType<X509BasicConstraintsExtension>()
            .Single().CertificateAuthority);
        Assert.False(server.Extensions.OfType<X509BasicConstraintsExtension>()
            .Single().CertificateAuthority);
        var san = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Contains("127.0.0.1", san.EnumerateIPAddresses().Select(ip => ip.ToString()));
    }

    [Fact]
    public void OptionsKeepConfiguredTargetAndOnlineRevocationDefault()
    {
        using var certificates = new CertificateMaterial();
        var builder = new DmConnectionStringBuilder { Server = "localhost", TlsCaCertificatePath = certificates.AuthorityPath };
        var options = DmTlsOptions.FromSettings(builder.ToSettings());
        using var scope = options.CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        Assert.Equal("localhost", options.TargetHost);
        Assert.Equal(X509RevocationMode.Online, scope.Authentication.CertificateRevocationCheckMode);
        Assert.Equal(X509RevocationMode.Online, scope.Authentication.CertificateChainPolicy!.RevocationMode);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, scope.Authentication.CertificateChainPolicy.TrustMode);
        Assert.Single(scope.Authentication.CertificateChainPolicy.CustomTrustStore);
    }

    [Fact]
    public void ExplicitNoCheckChangesOnlyRevocationPolicy()
    {
        using var certificates = new CertificateMaterial();
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", TlsCaCertificatePath = certificates.AuthorityPath,
            TlsRevocationMode = DmTlsRevocationMode.NoCheck
        };
        using var scope = DmTlsOptions.FromSettings(builder.ToSettings())
            .CreateAuthenticationScope(DmDeadline.Start(TimeSpan.FromSeconds(3)));
        Assert.Equal(X509RevocationMode.NoCheck, scope.Authentication.CertificateRevocationCheckMode);
        Assert.Equal(X509RevocationMode.NoCheck, scope.Authentication.CertificateChainPolicy!.RevocationMode);
        Assert.Equal(X509VerificationFlags.NoFlag, scope.Authentication.CertificateChainPolicy.VerificationFlags);
        Assert.Contains(scope.Authentication.CertificateChainPolicy.ApplicationPolicy.Cast<System.Security.Cryptography.Oid>(),
            policy => policy.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void ClientCertificatePasswordIsRedactedAcrossSnapshotAndBaseSetterPoison()
    {
        const string marker = "synthetic-t07-private-marker";
        var builder = new DmConnectionStringBuilder
        {
            Server = "localhost", TlsClientCertificatePath = Path.Combine(Path.GetTempPath(), "dummy.pfx"),
            TlsClientCertificatePassword = marker
        };
        var settings = builder.ToSettings();
        Assert.True(settings.TlsClientCertificatePassword == marker);
        Assert.True(settings.WithSchema("T07_SCHEMA").TlsClientCertificatePassword == marker);
        Assert.False(settings.ToConnectionString(includeSecrets: false).Contains(marker, StringComparison.Ordinal));
        Assert.False(builder.ToRedactedString().Contains(marker, StringComparison.Ordinal));
        System.Data.Common.DbConnectionStringBuilder baseView = builder;
        Assert.Throws<NotSupportedException>(() => baseView.ConnectionString =
            "server=localhost;tls_client_certificate_password=synthetic-t07-private-marker;unknown_option=1");
        Assert.Throws<InvalidOperationException>(() => _ = builder.ToSettings());
        Assert.Throws<InvalidOperationException>(() => _ = builder.ToRedactedString());
    }
}
