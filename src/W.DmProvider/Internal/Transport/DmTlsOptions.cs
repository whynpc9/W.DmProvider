using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using W.Dm;

namespace W.Dm.Internal.Transport;

/// <summary>Immutable TLS input. Its diagnostic form never includes certificate material.</summary>
internal sealed class DmTlsOptions
{
    private readonly string caCertificatePath;
    private readonly string clientCertificatePath;
    private readonly string clientPrivateKeyPath;
    private readonly string clientCertificatePassword;
    private readonly DmTlsRevocationMode revocationMode;

    internal string TargetHost { get; }

    private DmTlsOptions(DmConnectionSettings settings)
    {
        TargetHost = settings.Host;
        caCertificatePath = settings.TlsCaCertificatePath;
        clientCertificatePath = settings.TlsClientCertificatePath;
        clientPrivateKeyPath = settings.TlsClientPrivateKeyPath;
        clientCertificatePassword = settings.TlsClientCertificatePassword;
        revocationMode = settings.TlsRevocationMode;
        if (string.IsNullOrWhiteSpace(TargetHost)) throw new ArgumentException("A TLS target host is required.");
    }

    internal static DmTlsOptions FromSettings(DmConnectionSettings settings) =>
        new(settings ?? throw new ArgumentNullException(nameof(settings)));

    internal DmTlsOptionsScope CreateAuthenticationScope(DmDeadline deadline)
    {
        var owned = new List<X509Certificate2>();
        try
        {
            X509RevocationMode revocation = revocationMode == DmTlsRevocationMode.Online
                ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            var authentication = new SslClientAuthenticationOptions
            {
                TargetHost = TargetHost,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = revocation,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                AllowRenegotiation = false
            };

            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.System,
                RevocationMode = revocation,
                RevocationFlag = X509RevocationFlag.ExcludeRoot,
                VerificationFlags = X509VerificationFlags.NoFlag,
                UrlRetrievalTimeout = deadline.IsInfinite
                    ? TimeSpan.FromSeconds(15)
                    : TimeSpan.FromMilliseconds(deadline.RemainingMilliseconds)
            };
            policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // TLS Web Server Authentication

            if (caCertificatePath.Length != 0)
            {
                var ca = X509CertificateLoader.LoadCertificateFromFile(caCertificatePath);
                owned.Add(ca);
                ValidateCaCertificate(ca);
                policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                policy.CustomTrustStore.Add(ca);
            }
            authentication.CertificateChainPolicy = policy;

            if (clientCertificatePath.Length != 0)
            {
                X509Certificate2 client = clientPrivateKeyPath.Length != 0
                    ? clientCertificatePassword.Length == 0
                        ? X509Certificate2.CreateFromPemFile(clientCertificatePath, clientPrivateKeyPath)
                        : X509Certificate2.CreateFromEncryptedPemFile(clientCertificatePath,
                            clientCertificatePassword.AsSpan(), clientPrivateKeyPath)
                    : X509CertificateLoader.LoadPkcs12FromFile(clientCertificatePath,
                        clientCertificatePassword,
                        // macOS PFX keys require a temporary keychain. .NET removes it when
                        // the certificate held by this TLS scope is disposed.
                        OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
                owned.Add(client);
                if (!client.HasPrivateKey) throw new AuthenticationException("TLS client certificate has no private key.");
                authentication.ClientCertificates = new X509CertificateCollection { client };
            }
            return new DmTlsOptionsScope(authentication, owned);
        }
        catch (TimeoutException)
        {
            foreach (var certificate in owned) certificate.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or AuthenticationException)
        {
            foreach (var certificate in owned) certificate.Dispose();
            throw new AuthenticationException("TLS certificate configuration is invalid.");
        }
    }

    private static void ValidateCaCertificate(X509Certificate2 certificate)
    {
        bool isCa = false;
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension constraints)
                isCa = constraints.CertificateAuthority;
            if (extension is X509KeyUsageExtension usage &&
                (usage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0)
                throw new AuthenticationException("TLS trust certificate cannot sign certificates.");
        }
        if (!isCa) throw new AuthenticationException("TLS trust certificate is not a CA certificate.");
    }

    public override string ToString() => "DmTlsOptions";
}

internal sealed class DmTlsOptionsScope : IDisposable
{
    private readonly IReadOnlyList<X509Certificate2> certificates;
    private int disposed;
    internal SslClientAuthenticationOptions Authentication { get; }

    internal DmTlsOptionsScope(SslClientAuthenticationOptions authentication, IReadOnlyList<X509Certificate2> certificates)
    {
        Authentication = authentication;
        this.certificates = certificates;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        foreach (var certificate in certificates) certificate.Dispose();
    }
}
