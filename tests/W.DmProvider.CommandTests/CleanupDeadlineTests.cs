using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class CleanupDeadlineTests
{
    [Fact]
    public void ExpiredBusinessDeadlineGetsIndependentFiniteCleanupBudget()
    {
        var time = new ManualTimeProvider();
        var session = ReadySession();
        var business = DmDeadline.Start(TimeSpan.FromMilliseconds(10), time);
        using var lease = session.BeginExecution(DmOperationPurpose.Query, business, timeoutSeconds: 0);
        time.AdvanceMilliseconds(10);
        Assert.Throws<TimeoutException>(() => business.ThrowIfExpired());
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(25));
        Assert.False(cleanup.Deadline.IsInfinite);
        Assert.Equal(25, cleanup.Deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(24);
        Assert.Equal(1, cleanup.Deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(1);
        Assert.Throws<TimeoutException>(() => cleanup.Deadline.ThrowIfExpired());
    }

    [Fact]
    public void InfiniteCommandTimeoutStillUsesFiniteCleanupBudget()
    {
        var session = ReadySession();
        using var lease = session.BeginExecution(DmOperationPurpose.Query, DmDeadline.Infinite, timeoutSeconds: 0);
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(50));
        Assert.False(cleanup.Deadline.IsInfinite);
        Assert.True(cleanup.Deadline.RemainingMilliseconds is > 0 and <= 50);
    }

    private static DmSession ReadySession()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        return session;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }
}
