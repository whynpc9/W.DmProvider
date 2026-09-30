using System;
using W.Dm;

namespace W.Dm.Internal.Legacy.A;

/// <summary>Rejects unverified server security modes before legacy cipher setup.</summary>
internal static class DmHandshakeSecurityGuard
{
    internal static void Validate(DmConnProperty property)
    {
        if (property == null) throw new ArgumentNullException(nameof(property));
        if (property.Encrypt != 0 || property.encryptPwd || property.encryptMsg)
            throw new NotSupportedException("Negotiated native transport security is unsupported.");
    }

    internal static bool RequiresFullTls(int negotiatedMode, DmTransportSecurity policy)
    {
        if (policy is not (DmTransportSecurity.RequireTls or DmTransportSecurity.PlaintextAllowed))
            throw new NotSupportedException("Unknown client transport security policy.");
        return negotiatedMode switch
        {
            0 when policy == DmTransportSecurity.PlaintextAllowed => false,
            0 => throw new NotSupportedException("The server did not negotiate TLS transport."),
            1 or 4 => true,
            5 => throw new NotSupportedException("Wire TLS mode 5 has not been verified."),
            2 => throw new NotSupportedException("Authentication-only TLS does not protect the business stream."),
            3 or 6 => throw new NotSupportedException("The negotiated TLS protocol mode is unsupported."),
            _ => throw new NotSupportedException("Unknown negotiated transport security mode.")
        };
    }

    internal static void ValidateMessageSecurity(DmConnProperty property)
    {
        if (property == null) throw new ArgumentNullException(nameof(property));
        if (property.encryptPwd || property.encryptMsg)
            throw new NotSupportedException("Negotiated native message encryption is unsupported.");
    }
}
