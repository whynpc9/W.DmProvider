using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class TransactionCancellationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PreSendTerminationPreservesActiveTransaction(bool commit, bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        using var caller = new CancellationTokenSource();
        DmTransportTestHooks.BeforeControlSendAttempt = (_, _) =>
        {
            if (cancellation) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = commit ? fixture.Transaction.CommitAsync(caller.Token) : fixture.Transaction.RollbackAsync(caller.Token);
            if (cancellation)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
                Assert.Equal(caller.Token, error.CancellationToken);
                var driverError = Assert.IsType<DmOperationCanceledException>(error);
                Assert.Equal(DmTransactionOutcome.Active, driverError.FailureInfo.TransactionOutcome);
                Assert.Equal(fixture.Transaction.Outcome, driverError.FailureInfo.TransactionOutcome);
            }
            else
            {
                var error = await Assert.ThrowsAsync<DmTimeoutException>(() => call);
                Assert.NotNull(error.FailureInfo);
                Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
                Assert.Equal(fixture.Transaction.Outcome, error.FailureInfo.TransactionOutcome);
            }
            Assert.Equal(0, fixture.Channel.Sends);
            Assert.False(fixture.Channel.IsClosed);
            Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
            Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
            Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminationAfterExecutionAcquisitionDoesNotSend(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        using var caller = new CancellationTokenSource();
        DmSessionTestHooks.AfterExecutionAcquired = _ =>
        {
            if (cancellation) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = fixture.Transaction.CommitAsync(caller.Token);
            if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            else await Assert.ThrowsAsync<DmTimeoutException>(() => call);
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsentSavepointTerminationLeavesActiveStackUnchanged(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        using var caller = new CancellationTokenSource();
        DmSessionTestHooks.AfterExecutionAcquired = _ =>
        {
            if (cancellation) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = fixture.Transaction.SaveAsync("synthetic", caller.Token);
            if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            else await Assert.ThrowsAsync<DmTimeoutException>(() => call);
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.False(fixture.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        // Missing-name rejection proves the unsuccessful SAVE did not publish a
        // stack entry, and must itself produce no protocol request.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transaction.ReleaseAsync("synthetic"));
        Assert.Equal(0, fixture.Channel.Sends);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartialCommitTerminationIsUnknownAndNeverReplayed(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        fixture.Channel.BlockAfterPartialSend = true;
        using var caller = new CancellationTokenSource();
        Task call = fixture.Transaction.CommitAsync(caller.Token);
        await fixture.Channel.PartialSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancellation)
        {
            caller.Cancel();
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            caller.Cancel();
        }
        var unknown = await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => call);
        if (cancellation)
        {
            var cause = Assert.IsAssignableFrom<OperationCanceledException>(unknown.InnerException);
            Assert.Equal(caller.Token, cause.CancellationToken);
        }
        else Assert.IsType<DmTimeoutException>(unknown.InnerException);
        Assert.False(unknown.IsTransient);
        Assert.NotNull(unknown.FailureInfo);
        Assert.Equal(DmErrorKind.OutcomeUnknown, unknown.ErrorKind);
        Assert.Equal("WDM_COMMIT_UNKNOWN", unknown.DriverErrorCode);
        Assert.Equal(DmFailurePhase.Commit, unknown.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.Unknown, unknown.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, unknown.FailureInfo.TransactionOutcome);
        Assert.False(unknown.FailureInfo.ConnectionReusable);
        Assert.Equal(cancellation ? DmCancelSource.User : DmCancelSource.TotalDeadline, unknown.FailureInfo.CancelSource);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
        Assert.Equal(System.Data.ConnectionState.Broken, fixture.Connection.State);
        Assert.True(fixture.Channel.IsClosed);
        await fixture.Transaction.DisposeAsync();
        Assert.Equal(2, fixture.Channel.Sends);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidatedSuccessWinsCancellationAtTheAckBarrier(bool commit)
    {
        await using var fixture = new TransactionAsyncFixture();
        using var caller = new CancellationTokenSource();
        DmTransportTestHooks.BeforeControlAck = (_, _) => caller.Cancel();
        try
        {
            Task call = commit ? fixture.Transaction.CommitAsync(caller.Token) : fixture.Transaction.RollbackAsync(caller.Token);
            await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Channel.ReleaseResponse.TrySetResult(true);
            await call.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { DmTransportTestHooks.Reset(); }
        Assert.Equal(commit ? DmTransactionOutcome.Committed : DmTransactionOutcome.RolledBack, fixture.Transaction.Outcome);
        await fixture.Transaction.DisposeAsync();
        Assert.Equal(1, fixture.Channel.Sends);
    }

    [Fact]
    public async Task CommitUsesFrozenCommandBudgetAndIgnoresConnectTimeout()
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 0,
            connectTimeout: TimeSpan.FromMilliseconds(1));
        Task call = fixture.Transaction.CommitAsync();
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False(call.IsCompleted);
        Assert.False(fixture.Channel.IsClosed);
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DmTransactionOutcome.Committed, fixture.Transaction.Outcome);
    }

    [Fact]
    public async Task InfiniteCommandBudgetStillHasFiniteDisposeRollback()
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 0,
            cleanupTimeout: TimeSpan.FromMilliseconds(500));
        Task call = fixture.Transaction.DisposeAsync().AsTask();
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(1, fixture.Channel.Sends);
    }
}

// Deterministic timer-backed monotonic clock: Advance synchronously dispatches
// callbacks so a timeout-vs-token race has a defined first event, without sleeps.
internal sealed class CancellationTestClock : TimeProvider
{
    private long timestamp;
    private readonly object gate = new();
    private readonly List<ClockTimer> timers = new();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ClockTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
            timer.Change(dueTime, period);
        }
        return timer;
    }
    internal void Advance(TimeSpan elapsed)
    {
        ClockTimer[] snapshot;
        long now;
        lock (gate)
        {
            now = Interlocked.Add(ref timestamp, elapsed.Ticks);
            snapshot = timers.ToArray();
        }
        foreach (ClockTimer timer in snapshot) timer.Fire(now);
    }
    private sealed class ClockTimer(CancellationTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private long period = -1;
        private volatile bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan periodTime)
        {
            if (disposed) return false;
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.timestamp + dueTime.Ticks;
            period = periodTime == Timeout.InfiniteTimeSpan ? -1 : periodTime.Ticks;
            return true;
        }
        internal void Fire(long now)
        {
            if (disposed || due > now) return;
            due = period <= 0 ? long.MaxValue : now + period;
            callback(state);
        }
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
