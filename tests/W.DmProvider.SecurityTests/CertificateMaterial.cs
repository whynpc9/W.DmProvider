using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace W.DmProvider.SecurityTests;

internal sealed class CertificateMaterial : IDisposable
{
    private readonly RSA caKey = RSA.Create(2048);
    private readonly List<RSA> leafKeys = [];
    private readonly List<X509Certificate2> leaves = [];
    private readonly string directory;

    internal X509Certificate2 Authority { get; }
    internal string AuthorityPath { get; }
    internal string ClientCertificatePath => Path.Combine(directory, "client-cert.pem");
    internal string ClientPrivateKeyPath => Path.Combine(directory, "client-key.pem");
    internal string ClientPfxPath => Path.Combine(directory, "client-cert.pfx");

    internal CertificateMaterial()
    {
        directory = Path.Combine(Path.GetTempPath(), "wdm-t07-certs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var request = new CertificateRequest("CN=WDM-T07-CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        Authority = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(30));
        AuthorityPath = Path.Combine(directory, "ca-cert.pem");
        WritePrivate(AuthorityPath, Authority.ExportCertificatePem());
    }

    internal X509Certificate2 Server(string dnsName = "localhost", bool includeIp = true,
        bool valid = true, bool serverAuth = true)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return Issue("CN=" + dnsName, serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2",
            request =>
            {
                var san = new SubjectAlternativeNameBuilder();
                san.AddDnsName(dnsName);
                if (includeIp) san.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(san.Build());
            }, valid ? now.AddDays(-1) : now.AddDays(-4), valid ? now.AddDays(10) : now.AddDays(-2));
    }

    internal X509Certificate2 Client(bool writePemFiles = false)
    {
        var certificate = Issue("CN=WDM_PROVIDER_TEST", "1.3.6.1.5.5.7.3.2", null,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
        if (writePemFiles)
        {
            WritePrivate(ClientCertificatePath, certificate.ExportCertificatePem());
            WritePrivate(ClientPrivateKeyPath, certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());
        }
        return certificate;
    }

    internal void WriteClientPfx(string password)
    {
        var certificate = Client();
        File.WriteAllBytes(ClientPfxPath, certificate.Export(X509ContentType.Pkcs12, password));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ClientPfxPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private X509Certificate2 Issue(string subject, string extendedKeyUsage,
        Action<CertificateRequest>? configure, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        RSA key = RSA.Create(2048);
        leafKeys.Add(key);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var purposes = new OidCollection { new Oid(extendedKeyUsage) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        configure?.Invoke(request);
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        using var issued = request.Create(Authority, notBefore, notAfter, serial);
        var withKey = issued.CopyWithPrivateKey(key);
        leaves.Add(withKey);
        return withKey;
    }

    private static void WritePrivate(string path, string contents)
    {
        File.WriteAllText(path, contents);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Dispose()
    {
        foreach (var leaf in leaves) leaf.Dispose();
        foreach (var key in leafKeys) key.Dispose();
        Authority.Dispose();
        caKey.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
