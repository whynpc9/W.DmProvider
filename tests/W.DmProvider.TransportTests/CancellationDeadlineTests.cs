using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class CancellationDeadlineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstTerminalEventWinsEvenIfOtherTokenChangesBeforeCatch(bool userFirst)
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        fixture.Channel.BlockSendAfter = 0;
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock));
        using var invocation = lease.BeginInvocation(source.Token);
        using var wire = fixture.Session.BeginWireExchange();
        Task send = fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken).AsTask();
        await fixture.Channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (userFirst) { source.Cancel(); clock.Advance(100); }
        else { clock.Advance(100); source.Cancel(); }
        Exception? error = await Record.ExceptionAsync(() => send.WaitAsync(TimeSpan.FromSeconds(3)));
        if (userFirst)
        {
            var canceled = Assert.IsType<DmOperationCanceledException>(error);
            Assert.Equal(source.Token, canceled.CancellationToken);
            Assert.Equal(DmCancelSource.User, canceled.FailureInfo.CancelSource);
        }
        else
        {
            var timeout = Assert.IsType<DmTimeoutException>(error);
            Assert.Equal(DmErrorKind.Timeout, timeout.ErrorKind);
            Assert.Equal("WDM_TIMEOUT", timeout.DriverErrorCode);
            Assert.Equal(0, timeout.Number);
            Assert.False(timeout.IsTransient);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo!.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, timeout.FailureInfo.OperationOutcome);
        }
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public void PreSendDeadlineAndIndependentCleanupKeepActiveTransaction()
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock));
        using (var invocation = lease.BeginInvocation())
        {
            using (fixture.Session.BeginWireExchange())
            {
                clock.Advance(100);
                var error = Assert.Throws<DmTimeoutException>(() => invocation.ThrowIfTerminated());
                Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo!.OperationOutcome);
                Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
                Assert.True(error.FailureInfo.ConnectionReusable);
                Assert.False(invocation.ShouldAbortAfterFailure(error));
            }
        }
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(20));
        Assert.False(cleanup.CancellationToken.IsCancellationRequested);
        Assert.Equal(20, cleanup.Deadline.RemainingMilliseconds);
        clock.Advance(19);
        Assert.Equal(1, cleanup.Deadline.RemainingMilliseconds);
        clock.Advance(1);
        Assert.Throws<DmTimeoutException>(() => cleanup.ThrowIfTerminated());
        Assert.Equal(0, fixture.Channel.SendCalls);
        Assert.Equal(0, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task OldCallerCancellationDoesNotContaminateFiniteCleanup()
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock), timeoutSeconds: 1);
        using (var call = lease.BeginInvocation(source.Token))
        {
            source.Cancel();
            Assert.Throws<DmOperationCanceledException>(() => call.ThrowIfTerminated());
        }
        clock.Advance(200); // The canceled public call's absolute deadline has also expired.
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(30));
        Assert.Equal(CancellationToken.None, cleanup.UserCancellationToken);
        Assert.False(cleanup.CancellationToken.IsCancellationRequested);
        Assert.Equal(30, cleanup.Deadline.RemainingMilliseconds);
        Assert.Equal(DmFailurePhase.Cleanup, cleanup.Phase);
        using var wire = fixture.Session.BeginWireExchange();
        await fixture.Transport.SendAllAsync([1], 0, 1, cleanup.Deadline, 0, cleanup.CancellationToken);
        wire.Complete(); cleanup.Complete();
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.False(fixture.Transport.IsClosed);
    }

    [Fact]
    public void CanceledRootCannotUseBrokenPhysicalSessionForCleanup()
    {
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using (var call = lease.BeginInvocation())
        {
            fixture.Session.TryBeginSendAttempt(call);
            call.Complete();
        }
        lease.Cancel();
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Throws<InvalidOperationException>(() => lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(30)));
        var clock = new CancellationClock();
        var expiredCleanup = DmDeadline.Start(TimeSpan.FromMilliseconds(1), clock);
        clock.Advance(1);
        Assert.Throws<InvalidOperationException>(() => lease.BeginCleanupInvocation(expiredCleanup));
        Assert.Throws<InvalidOperationException>(() => fixture.Session.BeginWireExchange());
        Assert.Equal(0, fixture.Channel.SendCalls);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task CleanupIgnoresOldRootCommandCauseButHonorsNewCancellationAfterSend()
    {
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        lease.Cancel(); // No send attempt: the original root cancellation leaves the session usable.
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromSeconds(1));
        Assert.False(cleanup.IsTerminated);
        Assert.Equal(CancellationToken.None, cleanup.UserCancellationToken);
        Assert.Throws<InvalidOperationException>(() => lease.BeginCleanupInvocation(TimeSpan.FromSeconds(1)));
        using var wire = fixture.Session.BeginWireExchange();
        fixture.Channel.BlockSendAfter = 0;
        Task send = fixture.Transport.SendAllAsync([1], 0, 1, cleanup.Deadline, 0, cleanup.CancellationToken).AsTask();
        await fixture.Channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lease.Cancel(); // A fresh Cancel while the child owns a send is still effective.
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        Assert.Equal(DmFailurePhase.Send, error.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task IdleProgressCanRenewIdleCapButCannotRenewInvocationTotalBudget()
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock), timeoutSeconds: 1);
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        await fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
        fixture.Channel.OnProgress = () => clock.Advance(30);
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => fixture.Transport.ReadExactlyAsync(
            new byte[5], 0, 5, invocation.Deadline, 40, invocation.CancellationToken).AsTask());
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo!.CancelSource);
        Assert.Equal(4, fixture.Channel.ReceiveCalls);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task IdleTimeoutHasDistinctStableCause()
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        fixture.Channel.BlockReceiveAfter = 0;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock));
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        await fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
        Task read = fixture.Transport.ReadSomeAsync(new byte[1], 0, 1, invocation.Deadline, 40, invocation.CancellationToken).AsTask();
        await fixture.Channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(40);
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => read.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(DmCancelSource.IdleTimeout, error.FailureInfo!.CancelSource);
        Assert.Equal(60, invocation.Deadline.RemainingMilliseconds);
    }

    [Fact]
    public void ReaderBusinessTimeBetweenInvocationsDoesNotConsumeNextReadBudget()
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader,
            DmDeadline.FromSeconds(1, clock), timeoutSeconds: 1);
        using (var first = lease.BeginInvocation())
        {
            clock.Advance(900);
            first.Complete();
        }
        clock.Advance(5000); // User processing, outside any public Read invocation.
        using var next = lease.BeginInvocation();
        Assert.Equal(1000, next.Deadline.RemainingMilliseconds);
        clock.Advance(999);
        next.ThrowIfTerminated();
        clock.Advance(1);
        Assert.Throws<DmTimeoutException>(() => next.ThrowIfTerminated());
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Fact]
    public void UnlimitedDeadlineRejectsNegativeValuesAndCleanupCannotBeUnlimited()
    {
        var clock = new CancellationClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => DmDeadline.FromSeconds(-1, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => DmDeadline.FromMilliseconds(-1, clock));
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, DmDeadline.FromSeconds(0, clock));
        using (var invocation = lease.BeginInvocation())
        {
            clock.Advance(100000);
            invocation.ThrowIfTerminated();
            Assert.True(invocation.Deadline.IsInfinite);
            invocation.Complete();
        }
        Assert.Throws<ArgumentException>(() => lease.BeginCleanupInvocation(DmDeadline.Infinite));
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.BeginCleanupInvocation(TimeSpan.Zero));
    }

    [Fact]
    public void LargestPublicTimeoutConstructsSystemTimersWithoutOverflow()
    {
        var deadline = DmDeadline.FromSeconds(int.MaxValue);
        using var budget = new DmIoCancellation(deadline, default, default);
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, deadline);
        using var invocation = lease.BeginInvocation();
        Assert.False(budget.Token.IsCancellationRequested);
        Assert.False(invocation.IsTerminated);
        invocation.Complete();
    }

    [Fact]
    public void LargeDeadlineTimerRearmsBoundedSlicesWithoutExpiringEarly()
    {
        var clock = new CancellationClock();
        long slice = int.MaxValue;
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(slice * 2 + 100), clock);
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, deadline);
        using var invocation = lease.BeginInvocation();
        using var budget = new DmIoCancellation(deadline, invocation.CancellationToken, default);
        clock.Advance(slice);
        Assert.False(invocation.IsTerminated);
        Assert.False(budget.Token.IsCancellationRequested);
        clock.Advance(slice);
        Assert.False(invocation.IsTerminated);
        Assert.False(budget.Token.IsCancellationRequested);
        clock.Advance(99);
        Assert.False(invocation.IsTerminated);
        clock.Advance(1);
        var error = Assert.Throws<DmTimeoutException>(() => invocation.ThrowIfTerminated());
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo!.CancelSource);
        Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
        Assert.True(budget.Token.IsCancellationRequested);
    }

    [Fact]
    public void ImmediateChangeCallbackCanRearmOnlyAfterTimerFieldIsAssigned()
    {
        var clock = new ImmediateChangeClock(fireOnFirstChange: true);
        int expired = 0;
        using var timer = new DmDeadlineTimer(DmDeadline.Start(TimeSpan.FromMilliseconds(10), clock), () => expired++);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.CreatedDueTime);
        Assert.Equal(2, clock.ChangedDueTimes.Count); // Initial arm + the synchronous early tick's rearm.
        Assert.All(clock.ChangedDueTimes, due => Assert.Equal(TimeSpan.FromMilliseconds(10), due));
        Assert.Equal(0, expired);
        clock.AdvanceTicks(TimeSpan.FromMilliseconds(10).Ticks);
        clock.Fire();
        Assert.Equal(1, expired);
    }

    [Fact]
    public void PositiveSubMillisecondBudgetUsesOneMillisecondSliceAndChecksTrueDeadline()
    {
        var clock = new ImmediateChangeClock(fireOnFirstChange: false);
        int expired = 0;
        using var timer = new DmDeadlineTimer(DmDeadline.Start(TimeSpan.FromTicks(5000), clock), () => expired++);
        Assert.Equal(TimeSpan.FromMilliseconds(1), Assert.Single(clock.ChangedDueTimes));
        clock.AdvanceTicks(4999);
        clock.Fire(); // Deliberately early callback, with one monotonic tick remaining.
        Assert.Equal(0, expired);
        Assert.Equal(2, clock.ChangedDueTimes.Count);
        Assert.All(clock.ChangedDueTimes, due => Assert.Equal(TimeSpan.FromMilliseconds(1), due));
        clock.AdvanceTicks(1);
        clock.Fire();
        Assert.Equal(1, expired);
    }

    [Fact]
    public void BeginConfigurationPreSendFailureUsesPreparePhase()
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Starting);
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl);
        using var invocation = lease.BeginInvocation(source.Token);
        source.Cancel();
        var error = Assert.Throws<DmOperationCanceledException>(() => invocation.ThrowIfTerminated());
        Assert.Equal(DmFailurePhase.Prepare, error.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.Starting, error.FailureInfo.TransactionOutcome);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
        Assert.Equal(0, fixture.Channel.SendCalls);
    }

    [Fact]
    public void TransactionLeaseOnlyCommandCancellationDoesNotClaimCommitPhase()
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl);
        lease.Cancel();
        var error = Assert.Throws<DmOperationCanceledException>(() => lease.BeginInvocation());
        Assert.Equal(DmFailurePhase.Prepare, error.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        Assert.True(error.FailureInfo.ConnectionReusable);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ControlPreSendFailureUsesItsExplicitKindPhase(bool commit, bool userCancellation)
    {
        var clock = new CancellationClock();
        using var fixture = new CancellationFixture();
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        fixture.Session.SetTransactionState(DmLocalTransactionState.Starting);
        using (var beginLease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl))
        using (var beginInvocation = beginLease.BeginInvocation())
            fixture.Session.ActivateTransaction(transaction, beginInvocation.Identity);
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl,
            DmDeadline.Start(TimeSpan.FromMilliseconds(100), clock));
        using var invocation = lease.BeginInvocation(source.Token);
        var kind = commit ? DmTransactionControlKind.Commit : DmTransactionControlKind.Rollback;
        fixture.Session.BeginTransactionControl(transaction, invocation.Identity, kind);
        Exception error;
        DmFailureInfo eventSnapshot;
        using (fixture.Session.BeginWireExchange())
        {
            if (userCancellation) source.Cancel();
            else clock.Advance(100);
            error = Assert.ThrowsAny<Exception>(() => invocation.ThrowIfTerminated());
            DmFailureInfo info = eventSnapshot = userCancellation ? Assert.IsType<DmOperationCanceledException>(error).FailureInfo :
                Assert.IsType<DmTimeoutException>(error).FailureInfo!;
            Assert.Equal(commit ? DmFailurePhase.Commit : DmFailurePhase.Rollback, info.Phase);
            Assert.Equal(DmOperationOutcome.NotSent, info.OperationOutcome);
            Assert.Equal(commit ? DmTransactionOutcome.Committing : DmTransactionOutcome.RollingBack, info.TransactionOutcome);
            Assert.Equal(userCancellation ? DmCancelSource.User : DmCancelSource.TotalDeadline, info.CancelSource);
            Assert.True(info.ConnectionReusable);
        }
        Assert.False(fixture.Session.EndTransactionControl(invocation));
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        var originalInner = error.InnerException;
        if (!userCancellation) source.Cancel(); // A later user signal must not replace the recorded timeout.
        Exception settled = invocation.TranslateFailure(error);
        Assert.Same(error, settled);
        Assert.Same(originalInner, settled.InnerException);
        DmFailureInfo settledInfo = userCancellation ? Assert.IsType<DmOperationCanceledException>(settled).FailureInfo :
            Assert.IsType<DmTimeoutException>(settled).FailureInfo!;
        Assert.NotSame(eventSnapshot, settledInfo);
        Assert.Equal(commit ? DmTransactionOutcome.Committing : DmTransactionOutcome.RollingBack, eventSnapshot.TransactionOutcome);
        Assert.Equal(DmTransactionOutcome.Active, settledInfo.TransactionOutcome);
        Assert.Equal(eventSnapshot.Phase, settledInfo.Phase);
        Assert.Equal(eventSnapshot.CancelSource, settledInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.NotSent, settledInfo.OperationOutcome);
        Assert.True(settledInfo.ConnectionReusable);
        if (userCancellation) Assert.Equal(source.Token, ((DmOperationCanceledException)settled).CancellationToken);
        Assert.Same(settled, invocation.TranslateFailure(settled));
        Assert.Equal(0, fixture.Channel.SendCalls);
    }

    [Fact]
    public void CommitUnknownMetadataKeepsFirstCauseWithoutRecommendingReplay()
    {
        using var fixture = new CancellationFixture();
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl);
        using var invocation = lease.BeginInvocation(source.Token);
        fixture.Session.TryBeginSendAttempt(invocation);
        source.Cancel();
        var info = invocation.CreateFailureInfo().WithCommitUnknown();
        Assert.Equal(DmErrorKind.OutcomeUnknown, info.ErrorKind);
        Assert.Equal("WDM_COMMIT_UNKNOWN", info.ErrorCode);
        Assert.Equal(DmFailurePhase.Commit, info.Phase);
        Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, info.TransactionOutcome);
        Assert.Equal(DmCancelSource.User, info.CancelSource);
        Assert.False(info.ConnectionReusable);
        Assert.Null(info.ServerErrorNumber);
    }
}

internal sealed class CancellationClock : TimeProvider
{
    private long timestamp;
    private readonly List<ManualTimer> timers = [];
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => timestamp;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    internal void Advance(long milliseconds)
    {
        timestamp += milliseconds;
        foreach (var timer in timers.ToArray()) timer.FireIfDue(timestamp);
    }
    private sealed class ManualTimer(CancellationClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed) return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.timestamp + (long)dueTime.TotalMilliseconds;
            return true;
        }
        internal void FireIfDue(long now)
        {
            if (disposed || now < due) return;
            due = long.MaxValue;
            callback(state);
        }
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

internal sealed class ImmediateChangeClock(bool fireOnFirstChange) : TimeProvider
{
    private long timestamp;
    private readonly bool immediate = fireOnFirstChange;
    private ImmediateTimer? timer;
    internal TimeSpan CreatedDueTime { get; private set; }
    internal List<TimeSpan> ChangedDueTimes { get; } = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        CreatedDueTime = dueTime;
        timer = new ImmediateTimer(this, callback, state);
        if (dueTime != Timeout.InfiniteTimeSpan) timer.Change(dueTime, period);
        return timer;
    }
    internal void AdvanceTicks(long ticks) => timestamp += ticks;
    internal void Fire() => timer!.Fire();
    private sealed class ImmediateTimer(ImmediateChangeClock clock, TimerCallback callback, object? state) : ITimer
    {
        private bool firedDuringChange;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed) return false;
            clock.ChangedDueTimes.Add(dueTime);
            if (clock.immediate && !firedDuringChange)
            {
                firedDuringChange = true;
                callback(state);
            }
            return true;
        }
        internal void Fire() { if (!disposed) callback(state); }
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
