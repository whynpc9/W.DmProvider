using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class TransactionAsyncTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommitAndRollbackWaitForAnAsyncReplyWithoutSynchronousIo(bool commit)
    {
        await using var fixture = new TransactionAsyncFixture();
        Task operation = commit ? fixture.Transaction.CommitAsync() : fixture.Transaction.RollbackAsync();
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(commit ? DmTransactionOutcome.Committing : DmTransactionOutcome.RollingBack,
            fixture.Transaction.Outcome);
        Assert.Equal(1, fixture.Channel.Sends);
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(commit ? DmTransactionOutcome.Committed : DmTransactionOutcome.RolledBack,
            fixture.Transaction.Outcome);
        Assert.Equal(1, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Fact]
    public async Task DisposeAsyncWaitsForRollbackAndUsesOnlyOneControlRequest()
    {
        await using var fixture = new TransactionAsyncFixture();
        Task disposal = fixture.Transaction.DisposeAsync().AsTask();
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(disposal.IsCompleted);
        fixture.Channel.ReleaseResponse.TrySetResult(true);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Transaction.DisposeAsync();
        Assert.Equal(DmTransactionOutcome.RolledBack, fixture.Transaction.Outcome);
        Assert.Equal(DmTransaction.DisposeStatus.Disposed, fixture.Transaction._disposeStatus);
        Assert.Equal(1, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Fact]
    public async Task LostCommitResponseIsUnknownAndDisposeDoesNotReplayCommitOrRollback()
    {
        await using var fixture = new TransactionAsyncFixture();
        Task commit = fixture.Transaction.CommitAsync();
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Channel.ReleaseResponse.TrySetResult(false);
        var error = await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => commit);
        Assert.False(error.IsTransient);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
        Assert.True(fixture.Channel.IsClosed);
        await fixture.Transaction.DisposeAsync();
        Assert.Equal(1, fixture.Channel.Sends);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("save")]
    [InlineData("release")]
    [InlineData("rollback_savepoint")]
    public async Task PreCancellationLeavesTransactionActiveAndNeverAcquiresLease(string operation)
    {
        await using var fixture = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int acquired = 0;
        DmSessionTestHooks.AfterExecutionAcquired = _ => acquired++;
        try
        {
            Task call = operation switch
            {
                "commit" => fixture.Transaction.CommitAsync(cancellation.Token),
                "rollback" => fixture.Transaction.RollbackAsync(cancellation.Token),
                "save" => fixture.Transaction.SaveAsync("synthetic", cancellation.Token),
                "release" => fixture.Transaction.ReleaseAsync("synthetic", cancellation.Token),
                _ => fixture.Transaction.RollbackAsync("synthetic", cancellation.Token)
            };
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Equal(0, acquired);
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Fact]
    public async Task CancelAfterCommitSendProducesUnknownOutcomeWithoutRetry()
    {
        await using var fixture = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        Task commit = fixture.Transaction.CommitAsync(cancellation.Token);
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => commit);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(1, fixture.Channel.Sends);
    }
}

// The actual connection/session/transaction/protocol path is bound to a channel
// whose sync methods throw. A reply barrier proves async waiting without sleeps.
internal sealed class TransactionAsyncFixture : IAsyncDisposable
{
    internal DmConnection Connection { get; }
    internal DmSession Session { get; }
    internal DmTransaction Transaction { get; }
    internal ReplyBarrierChannel Channel { get; } = new();

    internal TransactionAsyncFixture(TimeProvider? clock = null, int commandTimeout = 5,
        TimeSpan? connectTimeout = null, TimeSpan? cleanupTimeout = null)
    {
        Connection = new DmConnection(new DmConnectionStringBuilder
        {
            Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only",
            CommandTimeout = commandTimeout, ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5),
            CleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(5)
        }.ConnectionString);
        Connection.OperationClock = clock ?? TimeProvider.System;
        Session = new DmSession(broken =>
        {
            if (ReferenceEquals(Connection.Session, broken)) Connection.do_State = ConnectionState.Broken;
        });
        SetField(Connection, "session", Session);
        Session.BeginConnecting();
        using (var lease = Session.BeginExecution(DmOperationPurpose.Handshake))
        using (var invocation = lease.BeginInvocation())
        {
            var instance = new DmConnInstance(Connection);
            Connection.m_ConnInst = instance;
            instance.ConnProperty.ServerVersion = "8.1.5.60";
            instance.ConnProperty.AutoCommit = false;
            var protocol = instance.GetCsi();
            var transport = protocol.A();
            ((DmTransport)GetField(transport, "transport")!).Dispose();
            SetField(transport, "transport", new DmTransport(Channel));
            SetField(transport, "__t02_field_04000AAD", false);
            SetField(protocol, "__t02_field_04000ABD", false);
            Session.CompleteHandshake();
        }
        Connection.do_State = ConnectionState.Open;
        Session.SetTransactionState(DmLocalTransactionState.Starting);
        using (var lease = Session.BeginExecution(DmOperationPurpose.TransactionControl))
        using (var invocation = lease.BeginInvocation())
        {
            Transaction = new DmTransaction(Connection.m_ConnInst);
            Connection.m_ConnInst.Transaction = Transaction;
            Session.ActivateTransaction(Transaction, invocation.Identity);
        }
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    private static object? GetField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);
    private static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}

internal sealed class ReplyBarrierChannel : IDmByteChannel
{
    internal TaskCompletionSource<bool> ReceiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> ReleaseResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends { get; private set; }
    internal bool BlockAfterPartialSend { get; set; }
    internal TaskCompletionSource<bool> PartialSendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous send is forbidden in async tests.");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous receive is forbidden in async tests.");
    public async ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sends++;
        if (BlockAfterPartialSend)
        {
            if (Sends == 1) return 1;
            PartialSendEntered.TrySetResult(true);
            await ReleaseResponse.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return count;
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ReceiveEntered.TrySetResult(true);
        bool reply = await ReleaseResponse.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!reply) return 0;
        // Complete, validated opcode-0 success frame (64 zero bytes), as in the
        // verified commit/rollback server profile. No diagnostic body is present.
        Array.Clear(buffer, offset, count);
        return count;
    }
    public void Dispose()
    {
        IsClosed = true;
        ReleaseResponse.TrySetResult(false);
    }
}
