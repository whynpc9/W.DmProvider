using W.Dm.Internal.Transport;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Legacy.A;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class DeadlineTests
{
    [Fact]
    public void SharedBudgetCountsElapsedTimeAcrossSequentialPhases()
    {
        var time = new ManualTimeProvider();
        var deadline = DmDeadline.Start(TimeSpan.FromMilliseconds(100), time);

        Assert.Equal(100, deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(41); // DNS consumed part of the connect budget.
        Assert.Equal(59, deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(58); // TCP and LOGIN share the original deadline.
        Assert.Equal(1, deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(1);
        Assert.Throws<TimeoutException>(() => _ = deadline.RemainingMilliseconds);
        Assert.Throws<TimeoutException>(() => deadline.ThrowIfExpired());
    }

    [Fact]
    public void InfiniteDeadlineDoesNotExpireAsClockAdvances()
    {
        var deadline = DmDeadline.Infinite;
        Assert.True(deadline.IsInfinite);
        deadline.ThrowIfExpired();
    }

    [Fact]
    public void ReaderInvocationsGetFreshBudgetWhileQueryKeepsItsRootBudget()
    {
        var time = new ManualTimeProvider();
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        using (var reader = session.BeginExecution(DmOperationPurpose.Reader,
                   DmDeadline.Start(TimeSpan.FromMilliseconds(100), time), timeoutSeconds: 1))
        {
            using (var first = reader.BeginInvocation())
            {
                Assert.Equal(100, first.Deadline.RemainingMilliseconds);
            }
            time.AdvanceMilliseconds(80);
            using (var second = reader.BeginInvocation())
            {
                Assert.Equal(1000, second.Deadline.RemainingMilliseconds);
                Assert.Equal(20, reader.Deadline.RemainingMilliseconds);
            }
        }
        using (var query = session.BeginExecution(DmOperationPurpose.Query,
                   DmDeadline.Start(TimeSpan.FromMilliseconds(100), time)))
        {
            using (var first = query.BeginInvocation()) { }
            time.AdvanceMilliseconds(40);
            using var second = query.BeginInvocation();
            Assert.Equal(60, second.Deadline.RemainingMilliseconds);
        }
    }

    [Fact]
    public void MessageIdleCapUsesOperationPurposeWithoutChangingAbsoluteDeadline()
    {
        Assert.Equal(0, B.MessageIdleTimeout(DmOperationPurpose.Handshake, 900));
        Assert.Equal(900, B.MessageIdleTimeout(DmOperationPurpose.Query, 900));
        Assert.Equal(0, B.MessageIdleTimeout(DmOperationPurpose.Query, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => B.MessageIdleTimeout(DmOperationPurpose.Query, -1));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }
}
