using System;
using System.Threading;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Sessions;

internal sealed class DmInvocation : IDisposable
{
    private static readonly AsyncLocal<DmInvocation> ambient = new();
    private int disposed;
    private readonly DmInvocation prior;
    private readonly CancellationTokenSource operationCancellation = new();
    private readonly CancellationToken operationToken;
    private CancellationTokenRegistration userRegistration;
    private DmDeadlineTimer deadlineTimer;
    internal DmCancelSource TerminalCause;
    internal CancellationToken TerminalToken;
    private volatile bool sendAttempted;
    internal bool SendAttempted { get => sendAttempted; set => sendAttempted = value; }
    internal bool HasSendAttempt => SendAttempted;
    internal bool IsTerminated => Lease.Session.GetTerminalCause(this) != DmCancelSource.None;
    internal DmFailurePhase Phase { get; set; }
    private volatile bool completed;
    internal bool Completed { get => completed; set => completed = value; }
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    internal void ActivateCancellation()
    {
        userRegistration = UserCancellationToken.UnsafeRegister(_ =>
            Lease.Session.TerminateInvocation(this, DmCancelSource.User, UserCancellationToken), null);
        if (!Deadline.IsInfinite)
            deadlineTimer = new DmDeadlineTimer(Deadline, () =>
                Lease.Session.TerminateInvocation(this, DmCancelSource.TotalDeadline));
    }
    internal void ThrowIfTerminated()
    {
        if (!Completed)
        {
            try { Deadline.ThrowIfExpired(); }
            catch (TimeoutException) { Lease.Session.TerminateInvocation(this, DmCancelSource.TotalDeadline); }
        }
        DmCancelSource cause = Lease.Session.GetTerminalCause(this);
        if (cause != DmCancelSource.None) throw TranslateFailure(null);
    }
    internal bool ShouldAbortAfterFailure(Exception error)
    {
        if (error is DmException server && server.HasVerifiedServerResponse && server.VerifiedResponseIdentity == Identity)
            return false;
        return SendAttempted || (!IsTerminated && error is not (OperationCanceledException or TimeoutException or DmTimeoutException));
    }
    internal DmFailureInfo CreateFailureInfo(Exception error = null) => Lease.Session.CreateFailureInfo(this, error);
    internal Exception TranslateFailure(Exception error)
    {
        if (error is DmException serverError && serverError.HasVerifiedServerResponse &&
            serverError.VerifiedResponseIdentity == Identity)
        {
            serverError.SetFailureInfo(CreateFailureInfo(error));
            return error;
        }
        DmCancelSource cause = Lease.Session.GetTerminalCause(this);
        if (cause == DmCancelSource.None && error is TimeoutException)
        {
            Lease.Session.TerminateInvocation(this, DmCancelSource.TotalDeadline);
            cause = Lease.Session.GetTerminalCause(this);
        }
        if (cause is DmCancelSource.TotalDeadline or DmCancelSource.IdleTimeout)
        {
            DmFailureInfo info = CreateFailureInfo(error);
            if (error is DmTimeoutException timeout)
            {
                timeout.SetFailureInfo(info);
                return timeout;
            }
            return new DmTimeoutException(info, error);
        }
        if (cause is DmCancelSource.User or DmCancelSource.Command)
        {
            DmFailureInfo info = CreateFailureInfo(error);
            if (error is DmOperationCanceledException canceled)
            {
                canceled.SetFailureInfo(info);
                return canceled;
            }
            return new DmOperationCanceledException(info,
                cause == DmCancelSource.User ? TerminalToken : Lease.CommandCancellationToken, error);
        }
        if (error is DmException driverError && driverError.FailureInfo == null)
            driverError.SetFailureInfo(CreateFailureInfo(error));
        return error ?? new InvalidOperationException("The operation has no terminal failure.");
    }
    // Store the value itself: a mutable shared holder would let a child flow
    // overwrite its parent's (or sibling's) owner. Begin is deliberately synchronous.
    internal static DmInvocation Current
    {
        get
        {
            DmInvocation value = ambient.Value;
            while (value != null && Volatile.Read(ref value.disposed) != 0) value = value.prior;
            return value;
        }
    }
    internal DmExecutionLease Lease { get; }
    internal OperationIdentity Identity { get; }
    internal DmDeadline Deadline { get; }
    internal CancellationToken UserCancellationToken { get; }
    internal CancellationToken CancellationToken => operationToken;
    internal void SignalTermination() { try { operationCancellation.Cancel(); } catch (ObjectDisposedException) { } }
    internal void Complete() => Lease.Session.CompleteInvocation(this);
    internal DmInvocation(DmSession session, DmExecutionLease lease, OperationIdentity identity, DmDeadline deadline, CancellationToken cancellationToken = default)
    {
        operationToken = operationCancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        Lease = lease; Identity = identity; Deadline = deadline; UserCancellationToken = cancellationToken;
        Phase = lease.Purpose switch { DmOperationPurpose.Handshake => DmFailurePhase.Connect,
            DmOperationPurpose.Reader => DmFailurePhase.Fetch, DmOperationPurpose.TransactionControl => DmFailurePhase.Prepare,
            _ => DmFailurePhase.Prepare };
        prior = Current;
        ambient.Value = this;
    }
    public void Dispose()
    {
        // Restore this flow even if another captured flow already disposed the owner.
        if (ReferenceEquals(ambient.Value, this)) ambient.Value = prior;
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // Unregister outside the session gate: disposal may wait for an in-flight callback.
        userRegistration.Dispose();
        deadlineTimer?.Dispose();
        Lease.Session.EndInvocation(this);
        operationCancellation.Dispose();
    }
}

internal sealed class DmWireExchange : IDisposable
{
    private static readonly AsyncLocal<DmWireExchange> ambient = new();
    internal static DmWireExchange Current => ambient.Value is { } value && Volatile.Read(ref value.disposed) == 0 ? value : null;
    private readonly DmSession session;
    private readonly DmInvocation invocation;
    private bool completed;
    private int disposed;
    internal DmWireExchange(DmSession session, DmInvocation invocation)
    {
        if (Current != null) throw new InvalidOperationException("Nested wire exchange is unsupported.");
        this.session = session; this.invocation = invocation; ambient.Value = this;
    }
    internal bool BelongsTo(DmSession candidate) => ReferenceEquals(session, candidate);
    internal void CompleteValidatedServerError(OperationIdentity identity)
    {
        if (invocation.Identity != identity)
            throw new InvalidOperationException("Server error response belongs to another invocation.");
        session.RequireActiveWireExchange();
        completed = true;
    }
    internal void Complete()
    {
        session.RequireActiveWireExchange();
        invocation.ThrowIfTerminated();
        completed = true;
    }
    public void Dispose()
    {
        if (ReferenceEquals(ambient.Value, this)) ambient.Value = null;
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            if (!completed && !session.IsRecoverableUnsentFailure(invocation))
            {
                DmDetachedTransport captured = session.Detach(invocation.Identity);
                try
                {
                    if (captured != null) DmSessionTestHooks.BeforeTransportAbort?.Invoke(invocation.Identity);
                }
                finally { captured?.AbortTransport(); }
            }
        }
        finally
        {
            session.EndWireExchange(this);
        }
    }
}
