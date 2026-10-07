using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Diagnostics;

namespace W.Dm.Internal.Pooling;

/// <summary>Bounds physical work, including creation and transport shutdown. No physical session is reused.</summary>
internal sealed class DmPoolOwner
{
    private readonly object gate = new();
    private readonly LinkedList<Waiter> waiters = new();
    private readonly int maximum;
    private readonly int maximumWaiters;
    private int creating, leased, closing;
    private long nextLeaseId, epoch;
    private bool stopped;
    internal event Action Quiescent;

    internal DmPoolOwner(int maxPoolSize, int maxPoolWaiters)
    {
        if (maxPoolSize < 1) throw new ArgumentOutOfRangeException(nameof(maxPoolSize));
        if (maxPoolWaiters < 0) throw new ArgumentOutOfRangeException(nameof(maxPoolWaiters));
        maximum = maxPoolSize;
        maximumWaiters = maxPoolWaiters;
    }

    internal DmPoolSnapshot Snapshot
    {
        get { lock (gate) return new(creating, leased, closing, waiters.Count, epoch, stopped); }
    }

    internal DmPoolLease Acquire(DmDeadline deadline, CancellationToken cancellationToken = default) =>
        AcquireAsync(deadline, cancellationToken).AsTask().GetAwaiter().GetResult();

    internal ValueTask<DmPoolLease> AcquireAsync(DmDeadline deadline, CancellationToken cancellationToken = default)
    {
        DmDiagnosticStamp stamp = DmDiagnosticsCore.Start();
        try
        {
            Waiter waiter = null;
            DmPoolLease immediate = null;
            lock (gate)
            {
                if (stopped) throw new ObjectDisposedException(nameof(DmPoolOwner));
                ThrowIfCanceledOrExpired(deadline, cancellationToken);
                if (creating + leased + closing < maximum && waiters.Count == 0)
                    immediate = CreateLease();
                else
                {
                    if (waiters.Count >= maximumWaiters)
                        throw PoolFailure("WDM_POOL_WAIT_QUEUE_FULL", "The connection pool waiting queue is full.");
                    waiter = new Waiter(deadline, cancellationToken);
                    waiter.Node = waiters.AddLast(waiter);
                    DmDiagnosticsCore.PoolDelta(waitingDelta: 1);
                }
            }
            if (immediate != null)
            {
                DmDiagnosticsCore.CompleteAcquire(stamp, DmDiagnosticResult.Success, false);
                return ValueTask.FromResult(immediate);
            }
            return AwaitWaiter(waiter, stamp);
        }
        catch (Exception error)
        {
            DmDiagnosticsCore.CompleteAcquire(stamp, DmDiagnosticsCore.Classify(error), false);
            throw;
        }
    }

    private async ValueTask<DmPoolLease> AwaitWaiter(Waiter waiter, DmDiagnosticStamp stamp)
    {
        // Registration, completion and disposal can invoke external code and always happen outside gate.
        CancellationTokenRegistration cancellation = default;
        DmDeadlineTimer timer = null;
        try
        {
            try
            {
                cancellation = waiter.Token.UnsafeRegister(_ => CancelWaiter(waiter, Canceled(waiter.Token)), null);
                if (!waiter.Deadline.IsInfinite)
                    timer = new DmDeadlineTimer(waiter.Deadline, () => CancelWaiter(waiter, TimedOut()));
            }
            catch (Exception error) { CancelWaiter(waiter, error); }
            DmPoolLease result = await waiter.Completion.Task.ConfigureAwait(false);
            DmDiagnosticsCore.CompleteAcquire(stamp, DmDiagnosticResult.Success, true);
            return result;
        }
        catch (Exception error)
        {
            DmDiagnosticsCore.CompleteAcquire(stamp, DmDiagnosticsCore.Classify(error), true);
            throw;
        }
        finally { cancellation.Dispose(); timer?.Dispose(); }
    }

    private void CancelWaiter(Waiter waiter, Exception error)
    {
        lock (gate)
        {
            if (waiter.Node == null) return; // A grant, stop, or another cancellation already won.
            waiters.Remove(waiter.Node);
            DmDiagnosticsCore.PoolDelta(waitingDelta: -1);
            waiter.Node = null;
        }
        waiter.Completion.TrySetException(error);
    }

    private DmPoolLease CreateLease()
    {
        if (nextLeaseId == long.MaxValue)
        {
            stopped = true;
            throw PoolFailure("WDM_POOL_LEASE_ID_EXHAUSTED", "The connection pool lease identity space is exhausted.");
        }
        long id = checked(nextLeaseId + 1);
        var result = new DmPoolLease(this, id, epoch);
        nextLeaseId = id;
        creating++;
        DmDiagnosticsCore.PoolDelta(creatingDelta: 1);
        return result;
    }

    internal void MarkLeased(DmPoolLease lease)
    {
        lock (gate)
        {
            if (lease.State != DmPoolLeaseState.Creating)
                throw new InvalidOperationException("The pool lease is no longer creating.");
            if (stopped) throw new ObjectDisposedException(nameof(DmPoolOwner));
            creating--;
            leased++;
            DmDiagnosticsCore.PoolDelta(creatingDelta: -1, leasedDelta: 1);
            lease.State = DmPoolLeaseState.Leased;
        }
    }

    internal void BeginClosing(DmPoolLease lease)
    {
        lock (gate)
        {
            if (lease.State is DmPoolLeaseState.Closing or DmPoolLeaseState.Completed) return;
            if (lease.State == DmPoolLeaseState.Creating)
            { creating--; DmDiagnosticsCore.PoolDelta(creatingDelta: -1, closingDelta: 1); }
            else { leased--; DmDiagnosticsCore.PoolDelta(leasedDelta: -1, closingDelta: 1); }
            closing++;
            lease.State = DmPoolLeaseState.Closing;
        }
    }

    internal void CompleteAfterTransportClosed(DmPoolLease lease)
    {
        var completions = new List<(Waiter Waiter, DmPoolLease Lease, Exception Error)>();
        bool quiescent;
        lock (gate)
        {
            if (lease.State == DmPoolLeaseState.Completed) return;
            switch (lease.State)
            {
                case DmPoolLeaseState.Creating: creating--; DmDiagnosticsCore.PoolDelta(creatingDelta: -1); break;
                case DmPoolLeaseState.Leased: leased--; DmDiagnosticsCore.PoolDelta(leasedDelta: -1); break;
                case DmPoolLeaseState.Closing: closing--; DmDiagnosticsCore.PoolDelta(closingDelta: -1); break;
            }
            lease.State = DmPoolLeaseState.Completed;
            while (!stopped && creating + leased + closing < maximum && waiters.First != null)
            {
                Waiter waiter = waiters.First.Value;
                waiters.RemoveFirst();
                DmDiagnosticsCore.PoolDelta(waitingDelta: -1);
                waiter.Node = null;
                Exception error = waiter.Token.IsCancellationRequested ? Canceled(waiter.Token) :
                    waiter.Deadline.RemainingTime == TimeSpan.Zero ? TimedOut() : null;
                DmPoolLease granted = null;
                if (error == null)
                {
                    try { granted = CreateLease(); }
                    catch (DmException exhausted) { error = exhausted; }
                }
                completions.Add((waiter, granted, error));
            }
            if (stopped)
            {
                foreach (Waiter waiter in waiters)
                {
                    waiter.Node = null;
                    completions.Add((waiter, null, new ObjectDisposedException(nameof(DmPoolOwner))));
                }
                DmDiagnosticsCore.PoolDelta(waitingDelta: -waiters.Count);
                waiters.Clear();
            }
            quiescent = creating + leased + closing == 0 && waiters.Count == 0;
        }
        foreach (var completion in completions)
        {
            if (completion.Error != null) completion.Waiter.Completion.TrySetException(completion.Error);
            else completion.Waiter.Completion.TrySetResult(completion.Lease);
        }
        if (quiescent) Quiescent?.Invoke();
    }

    internal void Clear()
    {
        lock (gate) epoch = checked(epoch + 1);
    }

    internal void Stop()
    {
        List<Waiter> pending;
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            // Stop permanently closes admission, so no fresh lease can observe a wrapped generation.
            // Shutdown and waiter completion must remain possible even at the identity boundary.
            if (epoch != long.MaxValue) epoch++;
            pending = new List<Waiter>(waiters);
            DmDiagnosticsCore.PoolDelta(waitingDelta: -waiters.Count);
            waiters.Clear();
            foreach (Waiter waiter in pending) waiter.Node = null;
        }
        foreach (Waiter waiter in pending)
            waiter.Completion.TrySetException(new ObjectDisposedException(nameof(DmPoolOwner)));
    }

    private static void ThrowIfCanceledOrExpired(DmDeadline deadline, CancellationToken token)
    {
        if (token.IsCancellationRequested) throw Canceled(token);
        if (deadline.RemainingTime == TimeSpan.Zero) throw TimedOut();
    }

    private static DmOperationCanceledException Canceled(CancellationToken token) => new(
        new DmFailureInfo(DmErrorKind.Canceled, DmFailurePhase.PoolWait, "WDM_POOL_WAIT_CANCELED",
            DmOperationOutcome.NotSent, null, false, DmCancelSource.User), token);

    private static DmTimeoutException TimedOut() => new(
        new DmFailureInfo(DmErrorKind.Timeout, DmFailurePhase.PoolWait, "WDM_POOL_WAIT_TIMEOUT",
            DmOperationOutcome.NotSent, null, false, DmCancelSource.TotalDeadline));

    internal static DmException PoolFailure(string code, string message)
    {
        var error = new DmException(message);
        error.SetFailureInfo(new DmFailureInfo(DmErrorKind.Unknown, DmFailurePhase.PoolWait,
            code, DmOperationOutcome.NotSent, null, false));
        return error;
    }

    private sealed class Waiter
    {
        internal readonly TaskCompletionSource<DmPoolLease> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly DmDeadline Deadline;
        internal readonly CancellationToken Token;
        internal LinkedListNode<Waiter> Node;
        internal Waiter(DmDeadline deadline, CancellationToken token) { Deadline = deadline; Token = token; }
    }
}

internal enum DmPoolLeaseState { Creating, Leased, Closing, Completed }

internal sealed class DmPoolLease
{
    private readonly DmPoolOwner owner;
    internal DmPoolLeaseState State;
    internal long LeaseId { get; }
    internal long Epoch { get; }
    internal DmPoolLease(DmPoolOwner owner, long id, long epoch)
    { this.owner = owner; LeaseId = id; Epoch = epoch; }
    internal void MarkLeased() => owner.MarkLeased(this);
    internal void BeginClosing() => owner.BeginClosing(this);
    // Caller must establish physical shutdown (or prove creation never began) before completing.
    internal void CompleteAfterTransportClosed() => owner.CompleteAfterTransportClosed(this);
}

internal readonly record struct DmPoolSnapshot(int Creating, int Leased, int Closing, int Waiting, long Epoch, bool Stopped)
{
    internal int Idle => 0;
    internal int Resetting => 0;
    internal int PhysicalCount => Creating + Leased + Closing;
    internal bool IsQuiescent => PhysicalCount == 0 && Waiting == 0;
}
