using W.Dm;
using W.Dm.Internal.Pooling;
using Xunit;

namespace W.DmProvider.PoolTests;

[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class DmPoolIdentityTests
{
    [Fact]
    public void EquivalentFrozenSettingsShareIdentityAndHash()
    {
        var a = new DmPoolKey(Baseline().ToSettings());
        var b = new DmPoolKey(Baseline().ToSettings());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("user")]
    [InlineData("password")]
    [InlineData("schema")]
    [InlineData("language")]
    [InlineData("connect")]
    [InlineData("command")]
    [InlineData("acquire")]
    [InlineData("idle")]
    [InlineData("cleanup")]
    [InlineData("security")]
    [InlineData("ca")]
    [InlineData("certificate")]
    [InlineData("key")]
    [InlineData("certificatePassword")]
    [InlineData("revocation")]
    [InlineData("persist")]
    [InlineData("pooling")]
    [InlineData("capacity")]
    [InlineData("waiting")]
    public void EveryEffectiveConfigDifferenceSeparatesIdentity(string field)
    {
        var original = Baseline();
        var changed = Baseline();
        switch (field)
        {
            case "host": changed.Server = "different.invalid"; break;
            case "port": changed.Port++; break;
            case "user": changed.User = "synthetic_user"; break;
            case "password": changed.Password = "synthetic_secret_rotated"; break;
            case "schema": changed.Schema = "OTHER_TEST"; break;
            case "language": changed.Language = 1; break;
            case "connect": changed.ConnectTimeout += TimeSpan.FromMilliseconds(1); break;
            case "command": changed.CommandTimeout++; break;
            case "acquire": changed.PoolAcquireTimeout += TimeSpan.FromMilliseconds(1); break;
            case "idle": changed.ReadIdleTimeout += TimeSpan.FromMilliseconds(1); break;
            case "cleanup": changed.CleanupTimeout += TimeSpan.FromMilliseconds(1); break;
            case "security": changed.TransportSecurity = DmTransportSecurity.PlaintextAllowed; break;
            case "ca": changed.TlsCaCertificatePath = "/synthetic/rotated-ca.pem"; break;
            case "certificate": changed.TlsClientCertificatePath = "/synthetic/rotated-client.pem"; break;
            case "key": changed.TlsClientPrivateKeyPath = "/synthetic/rotated-key.pem"; break;
            case "certificatePassword": changed.TlsClientCertificatePassword = "synthetic_key_password_rotated"; break;
            case "revocation": changed.TlsRevocationMode = DmTlsRevocationMode.NoCheck; break;
            case "persist": changed.PersistSecurityInfo = true; break;
            case "pooling": changed.Pooling = false; break;
            case "capacity": changed.MaxPoolSize++; break;
            case "waiting": changed.MaxPoolWaiters++; break;
        }
        Assert.NotEqual(new DmPoolKey(original.ToSettings()), new DmPoolKey(changed.ToSettings()));
    }

    [Fact]
    public void HashCollisionStillUsesFullCredentialEqualityAndCannotExposeSecrets()
    {
        var a = new DmPoolKey(Baseline().ToSettings());
        var changed = Baseline();
        changed.Password = "synthetic_rotated_secret";
        var b = new DmPoolKey(changed.ToSettings());
        var lookup = new Dictionary<DmPoolKey, string>(new ConstantHashComparer()) { [a] = "a", [b] = "b" };
        Assert.Equal(2, lookup.Count);
        Assert.Equal("a", lookup[a]);
        Assert.Equal("b", lookup[b]);
        Assert.DoesNotContain("synthetic", a.ToString());
        Assert.DoesNotContain("identity.invalid", a.ToString());
        Assert.DoesNotContain("/", a.ToString());
    }

    internal static DmConnectionStringBuilder Baseline() => new()
    {
        Server = "identity.invalid", User = "SYNTHETIC_USER", Password = "synthetic_secret",
        Schema = "SYNTHETIC_SCHEMA", Pooling = true, MaxPoolSize = 1, MaxPoolWaiters = 4,
        Language = 0, TransportSecurity = DmTransportSecurity.RequireTls,
        TlsCaCertificatePath = "/synthetic/ca.pem", TlsClientCertificatePath = "/synthetic/client.pem",
        TlsClientPrivateKeyPath = "/synthetic/key.pem", TlsClientCertificatePassword = "synthetic_key_password",
        TlsRevocationMode = DmTlsRevocationMode.Online
    };

    internal sealed class ConstantHashComparer : IEqualityComparer<DmPoolKey>
    {
        public bool Equals(DmPoolKey? x, DmPoolKey? y) => x?.Equals(y) ?? y == null;
        public int GetHashCode(DmPoolKey obj) => 7;
    }
}
