using System;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Pooling;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Sessions;

/// <summary>Owns one unpublished physical handshake, independently of later logical opens.</summary>
internal sealed class DmPendingOpen : IDisposable
{
    internal readonly long Generation;
    internal readonly DmConnectionSettings Settings;
    internal readonly DmSession Session;
    internal readonly DmConnection Candidate;
    internal readonly CancellationToken UserToken;
    internal readonly DmDeadline PoolDeadline;
    private readonly CancellationTokenSource close = new();
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationTokenRegistration userRegistration;
    private int cancellationWinner;
    internal CancellationToken LifetimeToken => lifetime.Token;
    internal bool UserCancellationWon => Volatile.Read(ref cancellationWinner) == 1;
    internal bool CloseCancellationWon => Volatile.Read(ref cancellationWinner) == 2;

    internal void ObserveUserCancellation()
    {
        // A canceled operation may resume before this token's registration.
        // Supplement an unclaimed winner; an earlier Close remains authoritative.
        if (UserToken.IsCancellationRequested) Interlocked.CompareExchange(ref cancellationWinner, 1, 0);
    }
    internal DmPoolOwner Owner;
    internal DmPoolOwnerReference OwnerReference;
    internal DmPoolLease PoolLease;
    internal bool Published;
    internal bool HandshakeStarted;
    internal bool HandshakeAcknowledged;
    internal bool HandshakeSendAttempted;

    internal DmPendingOpen(long generation, DmConnectionSettings settings, DmSession session,
        DmConnection candidate, CancellationToken userToken, DmDeadline deadline)
    {
        Generation = generation; Settings = settings; Session = session; Candidate = candidate;
        UserToken = userToken; PoolDeadline = deadline;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(userToken, close.Token);
        userRegistration = userToken.Register(ObserveUserCancellation);
    }

    internal void CancelClose()
    {
        Interlocked.CompareExchange(ref cancellationWinner, 2, 0);
        try { close.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal DmDeadline HandshakeDeadline(TimeProvider clock)
    {
        PoolDeadline.ThrowIfExpired();
        DmDeadline deadline = PoolDeadline.EarlierOf(DmDeadline.Start(Settings.ConnectTimeout, clock));
        deadline.ThrowIfExpired();
        return deadline;
    }

    public void Dispose()
    {
        try { OwnerReference?.Dispose(); }
        finally { userRegistration.Dispose(); lifetime.Dispose(); close.Dispose(); }
    }
}

internal static class DmPendingOpenTestHooks
{
    // Null in production. Tests must initialize a complete synthetic handshake
    // on the isolated candidate; there is no automatic fallback to fake sessions.
    internal static Func<DmConnection, bool, CancellationToken, ValueTask> Handshake;
    internal static Action<DmConnection> AfterCapacityAcquired;
    internal static Action<DmConnection> BeforePublish;
}
