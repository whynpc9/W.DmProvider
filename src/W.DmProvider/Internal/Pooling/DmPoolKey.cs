using System;

namespace W.Dm.Internal.Pooling;

/// <summary>Full effective configuration equality. Hash collisions never establish pool identity.</summary>
internal sealed class DmPoolKey : IEquatable<DmPoolKey>
{
    private readonly DmConnectionSettings settings;
    internal DmPoolKey(DmConnectionSettings settings) =>
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public bool Equals(DmPoolKey other)
    {
        if (other == null) return false;
        DmConnectionSettings a = settings, b = other.settings;
        return Equal(a.Host, b.Host) && a.Port == b.Port && Equal(a.User, b.User) &&
            Equal(a.Password, b.Password) && Equal(a.Schema, b.Schema) && a.Language == b.Language &&
            a.ConnectTimeout == b.ConnectTimeout && a.CommandTimeout == b.CommandTimeout &&
            a.PoolAcquireTimeout == b.PoolAcquireTimeout && a.ReadIdleTimeout == b.ReadIdleTimeout &&
            a.CleanupTimeout == b.CleanupTimeout && a.TransportSecurity == b.TransportSecurity &&
            Equal(a.TlsCaCertificatePath, b.TlsCaCertificatePath) &&
            Equal(a.TlsClientCertificatePath, b.TlsClientCertificatePath) &&
            Equal(a.TlsClientPrivateKeyPath, b.TlsClientPrivateKeyPath) &&
            Equal(a.TlsClientCertificatePassword, b.TlsClientCertificatePassword) &&
            a.TlsRevocationMode == b.TlsRevocationMode && a.PersistSecurityInfo == b.PersistSecurityInfo &&
            a.Pooling == b.Pooling && a.MaxPoolSize == b.MaxPoolSize && a.MaxPoolWaiters == b.MaxPoolWaiters &&
            a.MaxMessageSize == b.MaxMessageSize && a.MaxMaterializedLobSize == b.MaxMaterializedLobSize &&
            a.LobChunkSize == b.LobChunkSize;
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    public override bool Equals(object obj) => obj is DmPoolKey key && Equals(key);
    public override int GetHashCode()
    {
        DmConnectionSettings s = settings;
        var hash = new HashCode();
        hash.Add(s.Host, StringComparer.Ordinal); hash.Add(s.Port);
        hash.Add(s.User, StringComparer.Ordinal); hash.Add(s.Password, StringComparer.Ordinal);
        hash.Add(s.Schema, StringComparer.Ordinal); hash.Add(s.Language);
        hash.Add(s.ConnectTimeout); hash.Add(s.CommandTimeout); hash.Add(s.PoolAcquireTimeout);
        hash.Add(s.ReadIdleTimeout); hash.Add(s.CleanupTimeout); hash.Add(s.TransportSecurity);
        hash.Add(s.TlsCaCertificatePath, StringComparer.Ordinal);
        hash.Add(s.TlsClientCertificatePath, StringComparer.Ordinal);
        hash.Add(s.TlsClientPrivateKeyPath, StringComparer.Ordinal);
        hash.Add(s.TlsClientCertificatePassword, StringComparer.Ordinal);
        hash.Add(s.TlsRevocationMode); hash.Add(s.PersistSecurityInfo);
        hash.Add(s.Pooling); hash.Add(s.MaxPoolSize); hash.Add(s.MaxPoolWaiters);
        hash.Add(s.MaxMessageSize); hash.Add(s.MaxMaterializedLobSize); hash.Add(s.LobChunkSize);
        return hash.ToHashCode();
    }
    public override string ToString() => "DmPoolKey (configuration hidden)";
}
