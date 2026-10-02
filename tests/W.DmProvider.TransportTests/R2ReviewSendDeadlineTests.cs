using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class R2ReviewSendDeadlineTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("prepare")]
    [InlineData("fetch")]
    public async Task DeadlineExpiringBetweenBudgetChecksPreservesUnsentSession(string operation)
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        var clock = new BudgetCheckClock();
        var purpose = operation == "fetch" ? DmOperationPurpose.Reader : DmOperationPurpose.Query;
        var phase = operation switch
        {
            "prepare" => DmFailurePhase.Prepare,
            "fetch" => DmFailurePhase.Fetch,
            _ => DmFailurePhase.Send
        };
        using (var lease = fixture.Session.BeginExecution(purpose, DmDeadline.FromMilliseconds(100, clock)))
        using (var invocation = lease.BeginInvocation())
        {
            invocation.Phase = phase;
            TimeoutException raw;
            using (fixture.Session.BeginWireExchange())
            {
                // SendAllAsync's entry check and DmIoCancellation's invocation
                // check both see a live budget. Only its following direct
                // deadline check expires, before any send attempt is marked.
                clock.ExpireOnRead(3);
                raw = await Assert.ThrowsAsync<TimeoutException>(() => fixture.Transport.SendAllAsync(
                    [1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken).AsTask());
                Assert.Equal(3, clock.ReadsSinceArmed);
                Assert.False(invocation.SendAttempted);
            }

            // Deliberately dispose the wire BEFORE TranslateFailure: translation
            // also records a raw timeout and would conceal the original defect.
            Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
            Assert.False(fixture.Transport.IsClosed);
            Assert.Equal(0, fixture.Channel.SendCalls);
            Assert.Equal(0, fixture.Channel.DisposeCalls);
            Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
            Assert.Equal(DmCancelSource.TotalDeadline, fixture.Session.GetTerminalCause(invocation));
            var timeout = Assert.IsType<DmTimeoutException>(invocation.TranslateFailure(raw));
            Assert.Same(raw, timeout.InnerException);
            Assert.Equal(0, timeout.Number);
            Assert.NotNull(timeout.FailureInfo);
            Assert.Equal(DmErrorKind.Timeout, timeout.FailureInfo.ErrorKind);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo.CancelSource);
            Assert.Equal(phase, timeout.FailureInfo.Phase);
            Assert.Equal(DmOperationOutcome.NotSent, timeout.FailureInfo.OperationOutcome);
            Assert.Equal(DmTransactionOutcome.Active, timeout.FailureInfo.TransactionOutcome);
            Assert.True(timeout.FailureInfo.ConnectionReusable);
            Assert.Null(timeout.FailureInfo.ServerErrorNumber);
        }
        await AssertFreshSend(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawDeadlineCatchPreservesEarlierCancellationAndToken(bool commandCancellation)
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var caller = new CancellationTokenSource();
        var clock = new BudgetCheckClock();
        using (var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, DmDeadline.FromMilliseconds(100, clock)))
        using (var invocation = lease.BeginInvocation(caller.Token))
        {
            TimeoutException raw;
            using (fixture.Session.BeginWireExchange())
            {
                clock.ExpireOnRead(3, () =>
                {
                    if (commandCancellation) lease.Cancel();
                    else caller.Cancel();
                });
                raw = await Assert.ThrowsAsync<TimeoutException>(() => fixture.Transport.SendAllAsync(
                    [1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken).AsTask());
                Assert.Equal(3, clock.ReadsSinceArmed);
            }
            var canceled = Assert.IsType<DmOperationCanceledException>(invocation.TranslateFailure(raw));
            Assert.Equal(commandCancellation ? DmCancelSource.Command : DmCancelSource.User, canceled.FailureInfo.CancelSource);
            Assert.Equal(commandCancellation ? lease.CommandCancellationToken : caller.Token, canceled.CancellationToken);
            Assert.Same(raw, canceled.InnerException);
            Assert.Equal(DmOperationOutcome.NotSent, canceled.FailureInfo.OperationOutcome);
            Assert.Equal(DmTransactionOutcome.Active, canceled.FailureInfo.TransactionOutcome);
            Assert.True(canceled.FailureInfo.ConnectionReusable);
            Assert.False(invocation.SendAttempted);
            Assert.Equal(0, fixture.Channel.SendCalls);
            Assert.Equal(0, fixture.Channel.DisposeCalls);
            Assert.False(fixture.Transport.IsClosed);
        }
        await AssertFreshSend(fixture);
    }

    [Fact]
    public async Task SameRawDeadlineAfterPartialSendStillBreaksCapturedSession()
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        var clock = new BudgetCheckClock();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, DmDeadline.FromMilliseconds(100, clock));
        using var invocation = lease.BeginInvocation();
        using (fixture.Session.BeginWireExchange())
        {
            // After the first byte, the two post-send checks and the next
            // invocation check pass; the second budget's direct check expires.
            fixture.Channel.OnProgress = () => clock.ExpireOnRead(4);
            var raw = await Assert.ThrowsAsync<TimeoutException>(() => fixture.Transport.SendAllAsync(
                [1, 2], 0, 2, invocation.Deadline, 0, invocation.CancellationToken).AsTask());
            Assert.Equal(4, clock.ReadsSinceArmed);
            Assert.True(invocation.SendAttempted);
            Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
            Assert.True(fixture.Transport.IsClosed);
            var timeout = Assert.IsType<DmTimeoutException>(invocation.TranslateFailure(raw));
            Assert.NotNull(timeout.FailureInfo);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, timeout.FailureInfo.OperationOutcome);
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, timeout.FailureInfo.TransactionOutcome);
            Assert.False(timeout.FailureInfo.ConnectionReusable);
        }
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    private static async Task AssertFreshSend(CancellationFixture fixture)
    {
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        using (var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query))
        using (var invocation = lease.BeginInvocation())
        using (var wire = fixture.Session.BeginWireExchange())
        {
            await fixture.Transport.SendAllAsync([2], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
            wire.Complete();
            invocation.Complete();
        }
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.Equal(0, fixture.Channel.DisposeCalls);
        Assert.False(fixture.Transport.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
    }

    // Timer dispatch is deliberately deferred to model a deadline reached
    // before its scheduled callback executes. Timestamp reads alone control
    // the exact checking gap; no wall-clock waits or probabilistic race exist.
    private sealed class BudgetCheckClock : TimeProvider
    {
        private long timestamp;
        private int expireOnRead;
        private Action? beforeExpiration;
        internal int ReadsSinceArmed { get; private set; }
        public override long TimestampFrequency => 1000;
        internal void ExpireOnRead(int read, Action? beforeExpiration = null)
        {
            expireOnRead = read;
            ReadsSinceArmed = 0;
            this.beforeExpiration = beforeExpiration;
        }
        public override long GetTimestamp()
        {
            if (expireOnRead != 0 && ++ReadsSinceArmed == expireOnRead)
            {
                beforeExpiration?.Invoke();
                timestamp = 100;
            }
            return timestamp;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new DeferredTimer();
        private sealed class DeferredTimer : ITimer
        {
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
