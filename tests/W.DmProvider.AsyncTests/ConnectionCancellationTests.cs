using System.Data;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class ConnectionCancellationTests
{
    [Fact]
    public async Task PreCanceledOpenCreatesNoSocketAndRaisesNoStateNotification()
    {
        await using var connection = new DmConnection(new DmConnectionStringBuilder
        {
            Server = "synthetic.invalid", User = "synthetic_test", Password = "synthetic_only"
        }.ConnectionString);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        int changes = 0;
        connection.StateChange += (_, _) => changes++;
        long before = DmTransportTestHooks.CreatedTcpSockets;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.OpenAsync(caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(before, DmTransportTestHooks.CreatedTcpSockets);
        Assert.Equal(0, changes);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Null(connection.Session);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BeginCanceledOrExpiredAfterLeaseAcquisitionRestoresOriginalSession(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await fixture.Transaction.RollbackAsync();
        var originalTransaction = fixture.Connection.m_ConnInst.Transaction;
        var originalState = fixture.Session.TransactionState;
        bool autoCommit = fixture.Connection.m_ConnInst.GetAutoCommit();
        int sends = fixture.Channel.Sends;
        using var caller = new CancellationTokenSource();
        DmSessionTestHooks.AfterExecutionAcquired = _ =>
        {
            if (cancellation) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, caller.Token).AsTask();
            if (cancellation)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
                Assert.Equal(caller.Token, error.CancellationToken);
            }
            else await Assert.ThrowsAsync<DmTimeoutException>(() => call);
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Same(fixture.Session, fixture.Connection.Session);
        Assert.Same(originalTransaction, fixture.Connection.m_ConnInst.Transaction);
        Assert.Equal(originalState, fixture.Session.TransactionState);
        Assert.Equal(autoCommit, fixture.Connection.m_ConnInst.GetAutoCommit());
        Assert.Equal(sends, fixture.Channel.Sends);
        Assert.False(fixture.Channel.IsClosed);
        Assert.Equal(ConnectionState.Open, fixture.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        // A fresh operation proves that rollback of provisional Begin state left
        // the original session usable, rather than merely preserving a property.
        var next = (DmTransaction)await fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        Assert.Equal(DmTransactionOutcome.Active, next.Outcome);
        await next.RollbackAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsentIsolationConfigurationRestoresProvisionalBeginState(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = new TransactionAsyncFixture(clock, commandTimeout: 1);
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await fixture.Transaction.RollbackAsync();
        var priorTransaction = fixture.Connection.m_ConnInst.Transaction;
        var priorState = fixture.Session.TransactionState;
        var priorIsolation = fixture.Connection.m_ConnInst.ConnProperty.IsolationLevel;
        int sends = fixture.Channel.Sends;
        using var caller = new CancellationTokenSource();
        DmTransportTestHooks.BeforeSendAttempt = _ =>
        {
            Assert.Equal(DmLocalTransactionState.Starting, fixture.Session.TransactionState);
            if (cancellation) caller.Cancel();
            else clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable, caller.Token).AsTask();
            if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            else await Assert.ThrowsAsync<DmTimeoutException>(() => call);
        }
        finally { DmTransportTestHooks.Reset(); }
        Assert.Same(priorTransaction, fixture.Connection.m_ConnInst.Transaction);
        Assert.Equal(priorState, fixture.Session.TransactionState);
        Assert.Equal(priorIsolation, fixture.Connection.m_ConnInst.ConnProperty.IsolationLevel);
        Assert.True(fixture.Connection.m_ConnInst.GetAutoCommit());
        Assert.Equal(sends, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.False(fixture.Channel.IsClosed);
    }

    [Fact]
    public async Task CancelAfterCommitCompletesCannotAbortTheNextTransaction()
    {
        await using var fixture = new TransactionAsyncFixture();
        using var oldCaller = new CancellationTokenSource();
        Task commit = fixture.Transaction.CommitAsync(oldCaller.Token);
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await commit;
        var next = (DmTransaction)await fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        oldCaller.Cancel();
        Assert.Equal(DmTransactionOutcome.Committed, fixture.Transaction.Outcome);
        Assert.Equal(DmTransactionOutcome.Active, next.Outcome);
        Assert.False(fixture.Channel.IsClosed);
        Assert.Equal(ConnectionState.Open, fixture.Connection.State);
        await next.RollbackAsync();
    }

    [Fact]
    public async Task CapturedCloseCannotDetachAReplacementPhysicalSession()
    {
        await using var old = new TransactionAsyncFixture();
        await using var replacement = new TransactionAsyncFixture();
        var captured = old.Session;
        await old.Connection.CloseAsync();
        // Install a fully initialized synthetic handshake result on the logical
        // connection. This isolates the generation check from DNS/TLS behavior.
        typeof(DmConnection).GetField("session", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(old.Connection, replacement.Session);
        old.Connection.m_ConnInst = replacement.Connection.m_ConnInst;
        old.Connection.do_State = ConnectionState.Open;
        old.Connection.CloseExpectedSession(captured);
        Assert.Same(replacement.Session, old.Connection.Session);
        Assert.Equal(ConnectionState.Open, old.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Ready, replacement.Session.State);
        Assert.False(replacement.Channel.IsClosed);
        Assert.Equal(DmTransactionOutcome.Active, replacement.Transaction.Outcome);
        Assert.Equal(0, replacement.Channel.Sends);
    }

    [Fact]
    public void NegativeConnectAndCommandBudgetsAreRejectedBeforeNetworking()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DmConnectionStringBuilder { ConnectTimeout = TimeSpan.FromTicks(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new DmConnectionStringBuilder { CommandTimeout = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => DmDeadline.FromSeconds(-1));
    }
}
