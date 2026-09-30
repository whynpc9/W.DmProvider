using System;
using System.Threading;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Sessions;

internal sealed class DmInvocation : IDisposable
{
    [ThreadStatic] private static DmInvocation current;
    private int disposed;
    private readonly DmInvocation prior;
    internal static DmInvocation Current => current;
    internal DmExecutionLease Lease { get; }
    internal OperationIdentity Identity { get; }
    internal DmDeadline Deadline { get; }
    internal DmInvocation(DmSession session, DmExecutionLease lease, OperationIdentity identity, DmDeadline deadline)
    {
        Lease = lease; Identity = identity; Deadline = deadline;
        prior = current;
        current = this;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (ReferenceEquals(current, this)) current = prior;
        Lease.Session.EndInvocation(this);
    }
}

internal sealed class DmWireExchange : IDisposable
{
    [ThreadStatic] private static DmWireExchange current;
    internal static DmWireExchange Current => current;
    private readonly DmSession session;
    private readonly DmInvocation invocation;
    private bool completed;
    private int disposed;
    internal DmWireExchange(DmSession session, DmInvocation invocation)
    {
        if (current != null) throw new InvalidOperationException("Nested wire exchange is unsupported.");
        this.session = session; this.invocation = invocation; current = this;
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
        invocation.Deadline.ThrowIfExpired();
        completed = true;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            if (!completed && !session.IsRecoverableUnsentTransactionControl(invocation))
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
            if (ReferenceEquals(current, this)) current = null;
        }
    }
}
