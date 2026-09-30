using System;
using System.Threading;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Sessions;

/// <summary>Owns one physical connection and serializes its protocol exchanges.</summary>
internal sealed class DmSession
{
    private static long nextSessionId;
    private readonly object gate = new();
    private long nextExecutionId;
    private long nextInvocationId;
    private long leaseGeneration = 1; // Reserved for a future pool checkout; pooling is disabled.
    private DmExecutionLease activeLease;
    private DmInvocation activeInvocation;
    private DmWireExchange activeWire;
    private WeakReference<DmDataReader> activeReader;
    private OperationIdentity activeReaderIdentity;
    private DmConnInstance transport;
    private D pendingTransport;
    private DmPhysicalSessionState state = DmPhysicalSessionState.New;
    private DmLocalTransactionState transactionState;
    private DmTransaction activeTransaction;
    private TransactionControlOperation transactionControl;
    private int? lastBusinessTransactionStatus;
    private bool handshakeComplete;
    private readonly Action<DmSession> onBroken;

    internal long SessionId { get; }
    internal DmPhysicalSessionState State { get { lock (gate) return state; } }
    internal DmLocalTransactionState TransactionState { get { lock (gate) return transactionState; } }
    internal DmTransaction ActiveTransaction { get { lock (gate) return activeTransaction; } }
    internal int? LastBusinessTransactionStatus { get { lock (gate) return lastBusinessTransactionStatus; } }

    private sealed class TransactionControlOperation
    {
        internal readonly DmTransaction Transaction;
        internal readonly OperationIdentity Identity;
        internal readonly DmTransactionControlKind Kind;
        internal bool SendAttempted;
        internal bool AckVerified;
        internal bool RecoverablePreSendFailure;
        internal TransactionControlOperation(DmTransaction transaction, OperationIdentity identity, DmTransactionControlKind kind)
        { Transaction = transaction; Identity = identity; Kind = kind; }
    }

    internal void ActivateTransaction(DmTransaction transaction, OperationIdentity beginIdentity)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        lock (gate)
        {
            if (state != DmPhysicalSessionState.Busy || transactionState != DmLocalTransactionState.Starting ||
                activeTransaction != null || activeLease?.Identity.ExecutionId != beginIdentity.ExecutionId ||
                activeInvocation?.Identity != beginIdentity || beginIdentity.SessionId != SessionId ||
                beginIdentity.LeaseGeneration != leaseGeneration)
                throw new InvalidOperationException("Transaction start lost its session owner.");
            activeTransaction = transaction;
            lastBusinessTransactionStatus = null;
            transactionState = DmLocalTransactionState.Active;
            transaction.SetOutcomeFromSession(DmTransactionOutcome.Active);
        }
    }

    internal void BeginTransactionControl(DmTransaction transaction, OperationIdentity identity, DmTransactionControlKind kind)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        lock (gate)
        {
            if (state != DmPhysicalSessionState.Busy || transactionState != DmLocalTransactionState.Active ||
                !ReferenceEquals(activeTransaction, transaction) || transactionControl != null ||
                activeLease?.Purpose != DmOperationPurpose.TransactionControl ||
                activeInvocation?.Identity != identity || identity.SessionId != SessionId ||
                identity.LeaseGeneration != leaseGeneration)
                throw new InvalidOperationException("Transaction control has no active owner.");
            transactionControl = new TransactionControlOperation(transaction, identity, kind);
            transactionState = kind == DmTransactionControlKind.Commit
                ? DmLocalTransactionState.Committing : DmLocalTransactionState.RollingBack;
            transaction.SetOutcomeFromSession(kind == DmTransactionControlKind.Commit
                ? DmTransactionOutcome.Committing : DmTransactionOutcome.RollingBack);
        }
    }

    // Called immediately before the first channel.Send of the captured control.
    // A write that throws without reporting any bytes may still have reached the server.
    internal void MarkTransactionSendAttempt(DmInvocation invocation)
    {
        if (invocation == null) return;
        lock (gate)
        {
            if (transactionControl?.Identity == invocation.Identity &&
                ReferenceEquals(activeInvocation, invocation) &&
                state == DmPhysicalSessionState.Busy)
            {
                transactionControl.SendAttempted = true;
                transactionControl.RecoverablePreSendFailure = false;
            }
        }
    }

    internal void MarkRecoverablePreSendFailure(DmInvocation invocation)
    {
        if (invocation == null) return;
        lock (gate)
        {
            if (transactionControl?.Identity == invocation.Identity &&
                ReferenceEquals(activeInvocation, invocation) && !transactionControl.SendAttempted &&
                state == DmPhysicalSessionState.Busy)
                transactionControl.RecoverablePreSendFailure = true;
        }
    }

    internal bool IsCurrentTransactionControl(DmInvocation invocation, out DmTransactionControlKind kind)
    {
        lock (gate)
        {
            if (invocation != null && transactionControl?.Identity == invocation.Identity)
            {
                kind = transactionControl.Kind;
                return true;
            }
        }
        kind = default;
        return false;
    }

    internal bool IsRecoverableUnsentTransactionControl(DmInvocation invocation)
    {
        lock (gate)
            return invocation != null && transactionControl?.Identity == invocation.Identity &&
                transactionControl.RecoverablePreSendFailure &&
                !transactionControl.SendAttempted && !transactionControl.AckVerified;
    }

    // The protocol caller invokes this only after full frame and success-code validation.
    // Keep the captured operation after Detach so an already received response can
    // still confirm the old transaction without touching a reopened connection.
    internal void ConfirmTransactionAck(DmInvocation invocation, DmTransactionControlKind kind)
    {
        if (invocation == null) throw new InvalidOperationException("Transaction ACK has no invocation.");
        lock (gate)
        {
            TransactionControlOperation control = transactionControl;
            if (control == null || control.Identity != invocation.Identity || control.Kind != kind ||
                !control.SendAttempted || control.AckVerified)
                throw new InvalidOperationException("Transaction ACK has no matching send attempt.");
            control.AckVerified = true;
            transactionState = kind == DmTransactionControlKind.Commit
                ? DmLocalTransactionState.Committed : DmLocalTransactionState.RolledBack;
            control.Transaction.SetOutcomeFromSession(kind == DmTransactionControlKind.Commit
                ? DmTransactionOutcome.Committed : DmTransactionOutcome.RolledBack);
            if (ReferenceEquals(activeTransaction, control.Transaction)) activeTransaction = null;
        }
    }

    internal bool EndTransactionControl(DmInvocation invocation)
    {
        if (invocation == null) return false;
        lock (gate)
        {
            TransactionControlOperation control = transactionControl;
            if (control == null || control.Identity != invocation.Identity) return false;
            transactionControl = null;
            if (control.AckVerified) return true;
            if (control.SendAttempted || state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
            {
                transactionState = DmLocalTransactionState.OutcomeUnknown;
                control.Transaction.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
                if (ReferenceEquals(activeTransaction, control.Transaction)) activeTransaction = null;
            }
            else
            {
                transactionState = DmLocalTransactionState.Active;
                control.Transaction.SetOutcomeFromSession(DmTransactionOutcome.Active);
            }
            return false;
        }
    }

    // Preserve business response status before request-44 terminal frames can
    // overwrite the legacy DmConnInstance cache. Only a verified profile's
    // CREATE/ALTER/DROP/TRUNCATE completion currently establishes an external commit.
    internal bool ObserveTransactionResponse(OperationIdentity identity,
        short requestOpcode, int returnStatementType, int serverTransactionStatus,
        bool isTerminal, bool executionVsPrepare, string serverVersion)
    {
        if (isTerminal || !executionVsPrepare || requestOpcode == 0) return false;
        lock (gate)
        {
            if (activeInvocation?.Identity != identity ||
                activeLease?.Purpose is not (DmOperationPurpose.Query or DmOperationPurpose.Reader) ||
                transactionState != DmLocalTransactionState.Active || activeTransaction == null)
                return false;
            lastBusinessTransactionStatus = serverTransactionStatus;
            if (requestOpcode != 5 ||
                returnStatementType is not (129 or 146 or 139 or 194) || serverTransactionStatus != 0 ||
                !string.Equals(serverVersion, "8.1.5.60", StringComparison.Ordinal)) return false;
            DmTransaction transaction = activeTransaction;
            activeTransaction = null;
            transactionState = DmLocalTransactionState.CompletedExternally;
            transaction.SetOutcomeFromSession(DmTransactionOutcome.CompletedExternally);
            transaction.RecordFailure("ddl_implicit_commit_observed");
            transaction.Clear();
            return true;
        }
    }

    // Reader registration captures only the owner. Cleanup runs outside the
    // session gate, and a stale reader can never be selected for a new lease.
    internal void RegisterReader(DmExecutionLease lease, DmDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(reader);
        lock (gate)
        {
            if (!ReferenceEquals(activeLease, lease) || lease.Purpose != DmOperationPurpose.Reader ||
                state != DmPhysicalSessionState.Busy || activeReader != null)
                throw new InvalidOperationException("Reader does not own the current execution.");
            activeReaderIdentity = lease.Identity;
            activeReader = new WeakReference<DmDataReader>(reader);
        }
    }

    internal void UnregisterReader(DmExecutionLease lease, DmDataReader reader)
    {
        if (lease == null || reader == null) return;
        lock (gate)
        {
            if (activeReaderIdentity != lease.Identity || activeReader == null ||
                !activeReader.TryGetTarget(out DmDataReader current) || !ReferenceEquals(current, reader)) return;
            activeReader = null;
            activeReaderIdentity = default;
        }
    }

    internal bool TryGetActiveReader(out DmDataReader reader)
    {
        lock (gate)
        {
            if (activeLease?.Identity == activeReaderIdentity &&
                activeLease.Purpose == DmOperationPurpose.Reader &&
                activeReader != null && activeReader.TryGetTarget(out reader)) return true;
        }
        reader = null;
        return false;
    }

    internal DmSession(Action<DmSession> onBroken = null)
    {
        SessionId = NextPositive(ref nextSessionId);
        this.onBroken = onBroken;
    }

    private static long NextPositive(ref long counter)
    {
        while (true)
        {
            long current = Volatile.Read(ref counter);
            if (current == long.MaxValue) throw new InvalidOperationException("Session identity space exhausted.");
            long next = current + 1;
            if (Interlocked.CompareExchange(ref counter, next, current) == current) return next;
        }
    }

    internal void BeginConnecting()
    {
        lock (gate)
        {
            if (state != DmPhysicalSessionState.New) throw new InvalidOperationException("Session has already started.");
            state = DmPhysicalSessionState.Connecting;
        }
    }

    internal void BeginAuthenticating()
    {
        lock (gate)
        {
            if (state != DmPhysicalSessionState.Connecting) throw new InvalidOperationException("Session is not connecting.");
            state = DmPhysicalSessionState.Authenticating;
        }
    }

    internal void CompleteHandshake()
    {
        lock (gate)
        {
            if (state is not (DmPhysicalSessionState.Connecting or DmPhysicalSessionState.Authenticating or DmPhysicalSessionState.Busy))
                throw new InvalidOperationException("Session handshake cannot complete.");
            if (activeLease?.Purpose != DmOperationPurpose.Handshake) throw new InvalidOperationException("Handshake lease is missing.");
            if (transport == null) throw new InvalidOperationException("Physical transport is missing.");
            handshakeComplete = true;
        }
    }

    internal void AttachTransport(DmConnInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (gate)
        {
            if (state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed || transport != null || activeLease?.Purpose != DmOperationPurpose.Handshake)
                throw new InvalidOperationException("Transport cannot attach to this session.");
            transport = instance;
            pendingTransport = null;
        }
    }

    internal void RegisterPendingTransport(D pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        bool rejected;
        lock (gate)
        {
            rejected = state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed || pendingTransport != null || transport != null;
            if (!rejected) pendingTransport = pending;
        }
        if (rejected)
        {
            pending.C();
            throw new InvalidOperationException("Handshake transport belongs to a closed session.");
        }
    }

    internal DmExecutionLease BeginExecution(DmOperationPurpose purpose, DmDeadline deadline = default, int timeoutSeconds = 0)
    {
        DmDetachedTransport toAbort = null;
        DmExecutionLease result = null;
        lock (gate)
        {
            bool handshake = purpose == DmOperationPurpose.Handshake && state is DmPhysicalSessionState.Connecting or DmPhysicalSessionState.Authenticating;
            if (!handshake && state != DmPhysicalSessionState.Ready) throw new InvalidOperationException("Connection is not ready or another operation owns it.");
            if (activeLease != null) throw new InvalidOperationException("Another operation owns this connection.");
            if (nextExecutionId == long.MaxValue)
            {
                toAbort = BreakAndCaptureUnderLock();
            }
            else
            {
                var identity = new OperationIdentity(SessionId, leaseGeneration, ++nextExecutionId, 0);
                result = new DmExecutionLease(this, identity, purpose, deadline, timeoutSeconds);
                activeLease = result;
                if (!handshake) state = DmPhysicalSessionState.Busy;
            }
        }
        toAbort?.AbortTransport();
        if (result == null) onBroken?.Invoke(this);
        if (result == null) throw new InvalidOperationException("Execution identity space exhausted.");
        try { DmSessionTestHooks.AfterExecutionAcquired?.Invoke(result.Identity); }
        catch { result.Dispose(); throw; }
        return result;
    }

    internal DmExecutionLease BorrowHandshakeExecution()
    {
        lock (gate)
        {
            if (activeLease?.Purpose != DmOperationPurpose.Handshake || activeInvocation != null ||
                state is not (DmPhysicalSessionState.Connecting or DmPhysicalSessionState.Authenticating))
                throw new InvalidOperationException("Handshake cannot be borrowed.");
            return activeLease.Borrow();
        }
    }

    internal DmInvocation BeginInvocation(DmExecutionLease lease, DmDeadline deadline)
    {
        DmDetachedTransport toAbort = null;
        DmInvocation result = null;
        lock (gate)
        {
            if (!ReferenceEquals(activeLease, lease) || state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
                throw new InvalidOperationException("Execution lease is stale.");
            if (activeInvocation != null) throw new InvalidOperationException("Another invocation owns this execution.");
            if (nextInvocationId == long.MaxValue)
            {
                toAbort = BreakAndCaptureUnderLock();
                activeLease = null;
            }
            else
            {
                var identity = lease.Identity with { InvocationId = ++nextInvocationId };
                result = new DmInvocation(this, lease, identity, deadline);
                activeInvocation = result;
            }
        }
        toAbort?.AbortTransport();
        if (result == null) onBroken?.Invoke(this);
        if (result == null) throw new InvalidOperationException("Invocation identity space exhausted.");
        return result;
    }

    internal void RequireWireOwnership(DmInvocation invocation)
    {
        lock (gate)
        {
            if (invocation == null || !ReferenceEquals(activeInvocation, invocation) ||
                !ReferenceEquals(activeLease, invocation.Lease) ||
                invocation.Identity.SessionId != SessionId || invocation.Identity.LeaseGeneration != leaseGeneration ||
                state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
                throw new InvalidOperationException("Wire exchange has no current session owner.");
        }
    }

    internal DmWireExchange BeginWireExchange()
    {
        var invocation = DmInvocation.Current;
        RequireWireOwnership(invocation);
        lock (gate)
        {
            if (activeWire != null) throw new InvalidOperationException("Another wire exchange is active.");
            if (!ReferenceEquals(activeInvocation, invocation) || !ReferenceEquals(activeLease, invocation.Lease) ||
                state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
                throw new InvalidOperationException("Wire owner became stale.");
            activeWire = new DmWireExchange(this, invocation);
            return activeWire;
        }
    }

    internal void RequireActiveWireExchange()
    {
        DmWireExchange exchange = DmWireExchange.Current;
        if (exchange == null || !exchange.BelongsTo(this))
            throw new InvalidOperationException("Socket access requires a complete owned wire exchange.");
        lock (gate) if (!ReferenceEquals(activeWire, exchange)) throw new InvalidOperationException("Wire exchange is stale.");
        RequireWireOwnership(DmInvocation.Current);
    }

    internal void EndWireExchange(DmWireExchange exchange)
    {
        lock (gate) if (ReferenceEquals(activeWire, exchange)) activeWire = null;
    }

    internal void EndInvocation(DmInvocation invocation)
    {
        DmDetachedTransport toAbort = null;
        lock (gate)
        {
            if (!ReferenceEquals(activeInvocation, invocation)) return;
            if (activeWire != null)
            {
                activeWire = null;
                activeLease = null;
                toAbort = BreakAndCaptureUnderLock();
            }
            activeInvocation = null;
        }
        toAbort?.AbortTransport();
        if (toAbort != null) onBroken?.Invoke(this);
    }

    internal void EndExecution(DmExecutionLease lease)
    {
        DmDetachedTransport toAbort = null;
        lock (gate)
        {
            if (!ReferenceEquals(activeLease, lease)) return;
            if (activeInvocation != null || activeWire != null || (lease.Purpose == DmOperationPurpose.Handshake && !handshakeComplete))
            {
                activeInvocation = null;
                activeWire = null;
                toAbort = BreakAndCaptureUnderLock();
            }
            activeLease = null;
            if (activeReaderIdentity == lease.Identity)
            {
                activeReader = null;
                activeReaderIdentity = default;
            }
            if (state == DmPhysicalSessionState.Busy || (lease.Purpose == DmOperationPurpose.Handshake && state is DmPhysicalSessionState.Connecting or DmPhysicalSessionState.Authenticating))
                state = DmPhysicalSessionState.Ready;
        }
        toAbort?.AbortTransport();
        if (toAbort != null) onBroken?.Invoke(this);
    }

    internal void SetTransactionState(DmLocalTransactionState next)
    {
        lock (gate)
        {
            if (state is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed) throw new InvalidOperationException("Session is unavailable.");
            transactionState = next;
        }
    }

    internal bool IsCurrent(long sessionId, long generation)
    {
        lock (gate) return SessionId == sessionId && leaseGeneration == generation && state is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed);
    }

    internal bool IsCurrent(OperationIdentity identity) => IsCurrent(identity.SessionId, identity.LeaseGeneration);

    // Atomic identity check, state change and transport detachment. Caller closes the captured instance outside the gate.
    private DmDetachedTransport CaptureTransportUnderLock()
    {
        var captured = new DmDetachedTransport(transport, pendingTransport);
        transport = null;
        pendingTransport = null;
        return captured;
    }

    private DmDetachedTransport BreakAndCaptureUnderLock()
    {
        state = DmPhysicalSessionState.Broken;
        if (transactionState is DmLocalTransactionState.Starting or DmLocalTransactionState.Active or
            DmLocalTransactionState.Committing or DmLocalTransactionState.RollingBack)
            transactionState = DmLocalTransactionState.OutcomeUnknown;
        if (activeTransaction != null)
            activeTransaction.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
        return CaptureTransportUnderLock();
    }

    internal DmDetachedTransport Detach(OperationIdentity? expected = null)
    {
        DmDetachedTransport captured;
        lock (gate)
        {
            if (expected is { } id &&
                (id.SessionId != SessionId || id.LeaseGeneration != leaseGeneration ||
                 activeLease?.Identity.ExecutionId != id.ExecutionId ||
                 (id.InvocationId != 0 && activeInvocation?.Identity.InvocationId != id.InvocationId))) return null;
            if (state == DmPhysicalSessionState.Closed) return null;
            activeInvocation = null;
            activeLease = null;
            activeWire = null;
            activeReader = null;
            activeReaderIdentity = default;
            captured = BreakAndCaptureUnderLock();
        }
        if (expected != null)
        {
            try { onBroken?.Invoke(this); }
            catch { /* Caller must receive the captured transport for abort. */ }
        }
        return captured;
    }

    internal void MarkClosed() { lock (gate) state = DmPhysicalSessionState.Closed; }

    internal void SeedCountersForTests(long executionId, long invocationId, long generation)
    {
        lock (gate)
        {
            if (activeLease != null || executionId < 0 || invocationId < 0 || generation <= 0) throw new InvalidOperationException();
            nextExecutionId = executionId;
            nextInvocationId = invocationId;
            leaseGeneration = generation;
        }
    }

    internal static void SeedSessionCounterForTests(long value) => Interlocked.Exchange(ref nextSessionId, value);
    internal void CompleteHandshakeForTests()
    {
        lock (gate)
        {
            if (state != DmPhysicalSessionState.New) throw new InvalidOperationException();
            handshakeComplete = true;
            state = DmPhysicalSessionState.Ready;
        }
    }
}

internal sealed class DmDetachedTransport
{
    private DmConnInstance instance;
    private D pending;
    internal DmDetachedTransport(DmConnInstance instance, D pending) { this.instance = instance; this.pending = pending; }
    internal void AbortTransport()
    {
        DmConnInstance capturedInstance = Interlocked.Exchange(ref instance, null);
        D capturedPending = Interlocked.Exchange(ref pending, null);
        try { capturedInstance?.AbortTransport(); }
        finally { capturedPending?.C(); }
    }
}

internal static class DmSessionTestHooks
{
    internal static Action<OperationIdentity> AfterExecutionAcquired;
    internal static Action<OperationIdentity> BeforeTransportAbort;
    internal static Action<long> BeforeConnectionTransportAbort;
}
