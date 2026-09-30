using System;
using System.Threading;
using W.Dm.Internal.Sessions;

namespace W.Dm.Internal.Legacy.A;

// Internal observation points only. Callbacks run outside the session state gate.
internal static class DmWireTestHooks
{
    internal static Action<OperationIdentity, DmOperationPurpose> AfterExchangeEntered;
    internal static Action<OperationIdentity, DmOperationPurpose> AfterSendBeforeReceive;
    internal static Action<OperationIdentity, DmOperationPurpose> BeforeDecode;
    internal static Action<OperationIdentity, short> AfterHandshakeExchangeEntered;
    internal static Action<int> AfterStartupNegotiatedEncryptMode;
    private static long sendCount;
    private static long sentBytes;

    internal static long SendCount => Interlocked.Read(ref sendCount);
    internal static long SentBytes => Interlocked.Read(ref sentBytes);

    internal static void RecordSentBytes(int count) => Interlocked.Add(ref sentBytes, count);

    internal static void ExchangeEntered()
    {
        var invocation = DmInvocation.Current;
        Volatile.Read(ref AfterExchangeEntered)?.Invoke(invocation.Identity, invocation.Lease.Purpose);
    }

    internal static void HandshakeFrameEncoded(short opcode)
    {
        var invocation = DmInvocation.Current;
        if (invocation?.Lease.Purpose == DmOperationPurpose.Handshake)
            Volatile.Read(ref AfterHandshakeExchangeEntered)?.Invoke(invocation.Identity, opcode);
    }

    internal static void StartupNegotiatedEncryptMode(int mode) =>
        Volatile.Read(ref AfterStartupNegotiatedEncryptMode)?.Invoke(mode);

    internal static void Sent()
    {
        Interlocked.Increment(ref sendCount);
        var invocation = DmInvocation.Current;
        Volatile.Read(ref AfterSendBeforeReceive)?.Invoke(invocation.Identity, invocation.Lease.Purpose);
    }

    internal static void ResponseReady()
    {
        var invocation = DmInvocation.Current;
        Volatile.Read(ref BeforeDecode)?.Invoke(invocation.Identity, invocation.Lease.Purpose);
    }

    internal static void Reset()
    {
        Volatile.Write(ref AfterExchangeEntered, null);
        Volatile.Write(ref AfterSendBeforeReceive, null);
        Volatile.Write(ref BeforeDecode, null);
        Volatile.Write(ref AfterHandshakeExchangeEntered, null);
        Volatile.Write(ref AfterStartupNegotiatedEncryptMode, null);
        Interlocked.Exchange(ref sendCount, 0);
        Interlocked.Exchange(ref sentBytes, 0);
    }
}
