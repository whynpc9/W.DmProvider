using System;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Sessions;

internal sealed class DmExecutionLease : IDisposable, IAsyncDisposable
{
    private int disposed;
    private readonly DmExecutionLease owner;
    private int invocationStarted;
    internal DmSession Session { get; }
    internal OperationIdentity Identity { get; }
    internal DmOperationPurpose Purpose { get; }
    internal DmDeadline Deadline { get; }
    internal int TimeoutSeconds { get; }
    internal DmExecutionLease(DmSession session, OperationIdentity identity, DmOperationPurpose purpose, DmDeadline deadline, int timeoutSeconds)
    { Session = session; Identity = identity; Purpose = purpose; Deadline = deadline; TimeoutSeconds = timeoutSeconds; }
    private DmExecutionLease(DmExecutionLease owner)
    { this.owner = owner; Session = owner.Session; Identity = owner.Identity; Purpose = owner.Purpose; Deadline = owner.Deadline; TimeoutSeconds = owner.TimeoutSeconds; }
    internal DmExecutionLease Borrow() => new(this);
    internal DmInvocation BeginInvocation()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DmExecutionLease));
        DmExecutionLease root = owner ?? this;
        DmDeadline deadline = root.Purpose == DmOperationPurpose.Reader && Interlocked.Exchange(ref root.invocationStarted, 1) != 0
            ? root.Deadline.RenewBudget(TimeSpan.FromSeconds(root.TimeoutSeconds))
            : root.Deadline;
        return Session.BeginInvocation(root, deadline);
    }
    internal DmInvocation BeginCleanupInvocation(TimeSpan positiveTimeout)
    {
        if (positiveTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(positiveTimeout));
        DmExecutionLease root = owner ?? this;
        return BeginCleanupInvocation(DmDeadline.Start(positiveTimeout, root.Deadline.Clock));
    }
    internal DmInvocation BeginCleanupInvocation(DmDeadline finiteDeadline)
    {
        if (finiteDeadline.IsInfinite) throw new ArgumentException("Cleanup must have a finite deadline.", nameof(finiteDeadline));
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DmExecutionLease));
        finiteDeadline.ThrowIfExpired();
        DmExecutionLease root = owner ?? this;
        // Cleanup is a distinct child of the same execution. It has its own finite
        // budget and never renews the command's deadline or bypasses the child gate.
        return Session.BeginInvocation(root, finiteDeadline);
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0 && owner == null) Session.EndExecution(this); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
