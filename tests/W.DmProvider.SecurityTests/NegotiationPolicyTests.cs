using W.Dm;
using W.Dm.Internal.Legacy.A;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class NegotiationPolicyTests
{
    [Fact]
    public void PlaintextModeRequiresExplicitPolicy()
    {
        Assert.False(DmHandshakeSecurityGuard.RequiresFullTls(0, DmTransportSecurity.PlaintextAllowed));
        Assert.Throws<NotSupportedException>(() =>
            DmHandshakeSecurityGuard.RequiresFullTls(0, DmTransportSecurity.RequireTls));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FullStreamTlsModesAlwaysRequireCertificateValidatedUpgrade(int mode)
    {
        Assert.True(DmHandshakeSecurityGuard.RequiresFullTls(mode, DmTransportSecurity.RequireTls));
        Assert.True(DmHandshakeSecurityGuard.RequiresFullTls(mode, DmTransportSecurity.PlaintextAllowed));
    }

    [Theory]
    [InlineData(2)] // AUTH_ONLY leaves business traffic unencrypted.
    [InlineData(3)] // unsupported GMSSL mode.
    [InlineData(5)] // server config 5 reported wire 4; wire 5 itself was not observed.
    [InlineData(6)] // unsupported TLCP mode.
    [InlineData(1234)] // unknown server mode.
    public void AuthenticationOnlyAndUnsupportedModesFailBeforeLogin(int mode)
    {
        Assert.Throws<NotSupportedException>(() =>
            DmHandshakeSecurityGuard.RequiresFullTls(mode, DmTransportSecurity.RequireTls));
        Assert.Throws<NotSupportedException>(() =>
            DmHandshakeSecurityGuard.RequiresFullTls(mode, DmTransportSecurity.PlaintextAllowed));
    }

    [Fact]
    public void NativeMessageEncryptionNeverFallsBackToPlaintext()
    {
        Assert.Throws<NotSupportedException>(() =>
            DmHandshakeSecurityGuard.ValidateMessageSecurity(new DmConnProperty { encryptPwd = true }));
        Assert.Throws<NotSupportedException>(() =>
            DmHandshakeSecurityGuard.ValidateMessageSecurity(new DmConnProperty { encryptMsg = true }));
    }
}
