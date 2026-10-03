using W.Dm;
using W.Dm.Internal.Pooling;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class DmPoolSchedulerTests
{
    [Fact]
    public async Task CreatingAndClosingBothReservePhysicalCapacityUntilShutdown()
    {
        var owner = new DmPoolOwner(1, 4);
        var lease = owner.Acquire(DmDeadline.Infinite);
        Assert.Equal(1, owner.Snapshot.Creating);
        var waiting = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        lease.MarkLeased();
        lease.BeginClosing(); // e.g. shutdown failed; permit must remain held until abort is established.
        lease.BeginClosing();
        Assert.Equal(1, owner.Snapshot.Closing);
        Assert.False(waiting.IsCompleted);
        lease.CompleteAfterTransportClosed();
        var replacement = await waiting;
        Assert.NotEqual(lease.LeaseId, replacement.LeaseId);
        lease.CompleteAfterTransportClosed();
        Assert.Equal(1, owner.Snapshot.PhysicalCount);
        replacement.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task FailedCreationReleasesExactlyOnceAndGrantsFifo()
    {
        var owner = new DmPoolOwner(1, 4);
        var failed = owner.Acquire(DmDeadline.Infinite);
        var first = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        var second = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        failed.CompleteAfterTransportClosed(); // no transport was created
        var a = await first;
        Assert.False(second.IsCompleted);
        failed.CompleteAfterTransportClosed();
        Assert.Equal(1, owner.Snapshot.PhysicalCount);
        a.CompleteAfterTransportClosed();
        var b = await second;
        b.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task QueueIsBoundedAndCanceledWaiterDoesNotDisturbBusinessLease()
    {
        var owner = new DmPoolOwner(1, 1);
        var business = owner.Acquire(DmDeadline.Infinite);
        business.MarkLeased();
        using var cancellation = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(DmDeadline.Infinite, cancellation.Token).AsTask();
        var full = Assert.Throws<DmException>(() => owner.Acquire(DmDeadline.Infinite));
        Assert.Equal("WDM_POOL_WAIT_QUEUE_FULL", full.FailureInfo.ErrorCode);
        Assert.Equal(DmOperationOutcome.NotSent, full.FailureInfo.OperationOutcome);
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => waiting);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(DmFailurePhase.PoolWait, error.FailureInfo.Phase);
        Assert.Equal(1, owner.Snapshot.Leased);
        Assert.Equal(0, owner.Snapshot.Waiting);
        business.CompleteAfterTransportClosed();
    }

    [Fact]
    public async Task CancellationOfFifoHeadAllowsNextWaiterToReceiveReleasedCapacity()
    {
        var owner = new DmPoolOwner(1, 2);
        var held = owner.Acquire(DmDeadline.Infinite);
        using var cancellation = new CancellationTokenSource();
        var first = owner.AcquireAsync(DmDeadline.Infinite, cancellation.Token).AsTask();
        var second = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAsync<DmOperationCanceledException>(() => first);
        held.CompleteAfterTransportClosed();
        (await second).CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task DeadlineUsesOriginalBudgetIncludingTimeBeforeQueueEntry()
    {
        var clock = new PoolClock();
        var owner = new DmPoolOwner(1, 3);
        var held = owner.Acquire(DmDeadline.Infinite);
        var deadline = DmDeadline.FromMilliseconds(100, clock);
        clock.Advance(TimeSpan.FromMilliseconds(70));
        var waiting = owner.AcquireAsync(deadline).AsTask();
        clock.Advance(TimeSpan.FromMilliseconds(29));
        Assert.False(waiting.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => waiting);
        Assert.Equal("WDM_POOL_WAIT_TIMEOUT", error.FailureInfo.ErrorCode);
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
        Assert.Equal(0, owner.Snapshot.Waiting);
        held.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public void ExpiredOrCanceledImmediateAcquisitionNeverReservesCapacity()
    {
        var clock = new PoolClock();
        var deadline = DmDeadline.FromMilliseconds(1, clock);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var owner = new DmPoolOwner(1, 1);
        Assert.Throws<DmTimeoutException>(() => owner.Acquire(deadline));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<DmOperationCanceledException>(() => owner.Acquire(DmDeadline.Infinite, cancellation.Token));
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task ClearChangesEpochButDoesNotDoublePhysicalCapacity()
    {
        var owner = new DmPoolOwner(1, 2);
        var old = owner.Acquire(DmDeadline.Infinite);
        old.MarkLeased();
        owner.Clear();
        var next = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        Assert.False(next.IsCompleted);
        Assert.Equal(1, owner.Snapshot.PhysicalCount);
        old.CompleteAfterTransportClosed();
        var current = await next;
        Assert.Equal(old.Epoch + 1, current.Epoch);
        current.CompleteAfterTransportClosed();
    }

    [Fact]
    public async Task StopTerminatesWaitingAndNewAdmissionsWithoutAbortingBusinessLease()
    {
        var owner = new DmPoolOwner(1, 2);
        var business = owner.Acquire(DmDeadline.Infinite);
        business.MarkLeased();
        var waiting = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        owner.Stop();
        owner.Stop();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
        Assert.Throws<ObjectDisposedException>(() => owner.Acquire(DmDeadline.Infinite));
        Assert.Equal(1, owner.Snapshot.Leased);
        business.BeginClosing();
        business.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public void StopDuringCreationPreventsDeliveryOfANewBusinessLease()
    {
        var owner = new DmPoolOwner(1, 1);
        var creating = owner.Acquire(DmDeadline.Infinite);
        owner.Stop();
        Assert.Throws<ObjectDisposedException>(() => creating.MarkLeased());
        creating.BeginClosing();
        creating.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task GrantAndCancelRaceNeverLosesOrDuplicatesPermit()
    {
        var owner = new DmPoolOwner(1, 1);
        for (int i = 0; i < 100; i++)
        {
            var first = owner.Acquire(DmDeadline.Infinite);
            using var cancellation = new CancellationTokenSource();
            var waiter = owner.AcquireAsync(DmDeadline.Infinite, cancellation.Token).AsTask();
            using var start = new ManualResetEventSlim();
            var cancel = Task.Run(() => { start.Wait(); cancellation.Cancel(); });
            var close = Task.Run(() => { start.Wait(); first.CompleteAfterTransportClosed(); });
            start.Set();
            await Task.WhenAll(cancel, close);
            try { (await waiter).CompleteAfterTransportClosed(); }
            catch (DmOperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
            Assert.True(owner.Snapshot.IsQuiescent);
        }
    }

    [Fact]
    public async Task MultipleCapacitySlotsRemainBoundedThroughConcurrentDoubleCompletion()
    {
        var owner = new DmPoolOwner(2, 10);
        var a = owner.Acquire(DmDeadline.Infinite);
        var b = owner.Acquire(DmDeadline.Infinite);
        var waiters = Enumerable.Range(0, 10).Select(_ => owner.AcquireAsync(DmDeadline.Infinite).AsTask()).ToArray();
        await Task.WhenAll(Task.Run(a.CompleteAfterTransportClosed), Task.Run(a.CompleteAfterTransportClosed));
        var first = await waiters[0];
        Assert.Equal(2, owner.Snapshot.PhysicalCount);
        Assert.False(waiters[1].IsCompleted);
        b.CompleteAfterTransportClosed();
        first.CompleteAfterTransportClosed();
        for (int i = 1; i < waiters.Length; i++) (await waiters[i]).CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
        Assert.Equal(0, owner.Snapshot.Idle);
        Assert.Equal(0, owner.Snapshot.Resetting);
    }

    [Fact]
    public async Task LeaseIdentityExhaustionStopsAdmissionsWithoutLeakingReturnedCapacityOrWaiting()
    {
        var owner = new DmPoolOwner(1, 2);
        var held = owner.Acquire(DmDeadline.Infinite);
        var first = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        var second = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        typeof(DmPoolOwner).GetField("nextLeaseId", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, long.MaxValue);
        held.CompleteAfterTransportClosed();
        var error = await Assert.ThrowsAsync<DmException>(() => first);
        Assert.Equal("WDM_POOL_LEASE_ID_EXHAUSTED", error.FailureInfo.ErrorCode);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second);
        Assert.True(owner.Snapshot.IsQuiescent);
        Assert.True(owner.Snapshot.Stopped);
    }

    [Fact]
    public void ImmediateIdentityExhaustionDoesNotReserveCreationCapacity()
    {
        var owner = new DmPoolOwner(1, 0);
        typeof(DmPoolOwner).GetField("nextLeaseId", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, long.MaxValue);
        Assert.Throws<DmException>(() => owner.Acquire(DmDeadline.Infinite));
        Assert.True(owner.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task StopAtEpochBoundaryStillTerminatesWaitersAndAllowsTransportCompletion()
    {
        var owner = new DmPoolOwner(1, 1);
        var held = owner.Acquire(DmDeadline.Infinite);
        var waiting = owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        typeof(DmPoolOwner).GetField("epoch", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(owner, long.MaxValue);
        owner.Stop();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
        held.CompleteAfterTransportClosed();
        Assert.True(owner.Snapshot.IsQuiescent);
    }
}

internal sealed class PoolClock : TimeProvider
{
    private long timestamp;
    private readonly List<ManualTimer> timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    internal void Advance(TimeSpan amount)
    {
        timestamp += amount.Ticks;
        foreach (var timer in timers.ToArray()) timer.FireIfDue();
    }
    private sealed class ManualTimer(PoolClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed) return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.timestamp + dueTime.Ticks;
            return true;
        }
        internal void FireIfDue()
        {
            if (disposed || due > clock.timestamp) return;
            due = long.MaxValue;
            callback(state);
        }
        public void Dispose() { disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
