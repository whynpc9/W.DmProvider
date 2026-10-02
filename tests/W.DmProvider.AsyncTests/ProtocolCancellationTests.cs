using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class ProtocolCancellationTests
{
    [Fact]
    public async Task CancellationAtWireEntrySendsNothingAndPreservesTheActiveTransaction()
    {
        await using var database = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        using var lease = database.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(cancellation.Token);
        DmWireTestHooks.AfterExchangeEntered = (_, _) => cancellation.Cancel();
        try
        {
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => AllocateAsync(database)).ConfigureAwait(false);
            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
            Assert.True(failure.FailureInfo.ConnectionReusable);
            Assert.False(invocation.SendAttempted);
            Assert.Equal(0, database.Channel.Sends);
            Assert.False(database.Channel.IsClosed);
            Assert.Equal(DmTransactionOutcome.Active, database.Transaction.Outcome);
        }
        finally { DmWireTestHooks.Reset(); }
        invocation.Dispose();
        lease.Dispose();
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        using var next = database.Session.BeginExecution(DmOperationPurpose.Query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWaitingProtocolResponseReportsTheCapturedUserOrCommandCause(bool commandCancel)
    {
        await using var database = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        using var lease = database.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(cancellation.Token);
        Task allocation = AllocateAsync(database);
        await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.False(allocation.IsCompleted);
        if (commandCancel) lease.Cancel();
        else cancellation.Cancel();
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => allocation.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        Assert.Equal(commandCancel ? lease.CommandCancellationToken : cancellation.Token, failure.CancellationToken);
        Assert.Equal(commandCancel ? DmCancelSource.Command : DmCancelSource.User, failure.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, failure.FailureInfo.OperationOutcome);
        Assert.False(failure.FailureInfo.ConnectionReusable);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(1, database.Channel.Sends);
    }

    [Fact]
    public async Task TransportAbortIOExceptionStillReportsCommandCancellation()
    {
        await using var database = new TransactionAsyncFixture();
        using var channel = new AbortOnlyProtocolChannel();
        var wireTransport = database.Connection.m_ConnInst.GetCsi().A();
        var field = wireTransport.GetType().GetField("transport",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((DmTransport)field.GetValue(wireTransport)!).Dispose();
        field.SetValue(wireTransport, new DmTransport(channel));
        using var lease = database.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        Task allocation = AllocateAsync(database);
        await channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.False(allocation.IsCompleted);
        lease.Cancel();
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => allocation.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
        Assert.Equal(lease.CommandCancellationToken, failure.CancellationToken);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(1, channel.Sends);
        Assert.True(channel.IsClosed);
    }

    [Fact]
    public async Task ACommandCauseAtTheSentFrameBarrierCannotBeReplacedByLaterUserCancellation()
    {
        await using var database = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        using var lease = database.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(cancellation.Token);
        short observedOpcode = -1;
        DmWireTestHooks.AfterFrameSent = (identity, opcode) =>
        {
            Assert.Equal(invocation.Identity, identity);
            observedOpcode = opcode;
            lease.Cancel();
            cancellation.Cancel();
        };
        try
        {
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => AllocateAsync(database)).ConfigureAwait(false);
            Assert.Equal((short)3, observedOpcode);
            Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
            Assert.Equal(lease.CommandCancellationToken, failure.CancellationToken);
            Assert.Equal(1, database.Channel.Sends);
            Assert.True(database.Channel.IsClosed);
        }
        finally { DmWireTestHooks.Reset(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AValidatedControlAckKeepsItsTerminalOutcomeWhenCancellationArrivesAtAck(bool commit)
    {
        await using var database = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        DmTransportTestHooks.BeforeControlAck = (_, _) => cancellation.Cancel();
        try
        {
            Task operation = commit ? database.Transaction.CommitAsync(cancellation.Token)
                : database.Transaction.RollbackAsync(cancellation.Token);
            await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            database.Channel.ReleaseResponse.TrySetResult(true);
            await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.Equal(commit ? DmTransactionOutcome.Committed : DmTransactionOutcome.RolledBack,
                database.Transaction.Outcome);
            Assert.Equal(1, database.Channel.Sends);
            Assert.True(database.Channel.IsClosed);
            await database.Transaction.DisposeAsync().ConfigureAwait(false);
            Assert.Equal(1, database.Channel.Sends);
        }
        finally { DmTransportTestHooks.BeforeControlAck = null; }
    }

    private static Task<bool> AllocateAsync(TransactionAsyncFixture database)
    {
        var statement = ReaderAsyncFixture.BareStatement(database.Connection.m_ConnInst,
            new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction));
        return database.Connection.m_ConnInst.GetCsi().AllocateStatementAsync(statement, new b(), new b(), false);
    }
}

// Deliberately ignores the read token: disposing the captured transport wakes it
// with IOException, as a socket close can. The protocol must use its first cause.
internal sealed class AbortOnlyProtocolChannel : IDmByteChannel
{
    internal TaskCompletionSource ReceiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends { get; private set; }
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous protocol send is forbidden.");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous protocol receive is forbidden.");
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sends++;
        return ValueTask.FromResult(count);
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ReceiveEntered.TrySetResult();
        await aborted.Task.ConfigureAwait(false);
        throw new IOException("The synthetic captured transport was aborted.");
    }
    public void Dispose()
    {
        IsClosed = true;
        aborted.TrySetResult();
    }
}
