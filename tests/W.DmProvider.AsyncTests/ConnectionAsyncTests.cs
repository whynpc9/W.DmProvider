using System.Data;
using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class ConnectionAsyncTests
{
    [Fact]
    public async Task PreCanceledOpenDoesNotValidateOrChangeConnectionState()
    {
        await using var connection = new DmConnection();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var changes = new List<StateChangeEventArgs>();
        connection.StateChange += (_, change) => changes.Add(change);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.OpenAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Null(connection.Session);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task PreCanceledBeginPreservesTheActiveTransactionAndAcquiresNoLease()
    {
        await using var fixture = new TransactionAsyncFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int acquired = 0;
        DmSessionTestHooks.AfterExecutionAcquired = _ => acquired++;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation.Token));
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Equal(0, acquired);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        Assert.Equal(0, fixture.Channel.Sends);
    }

    [Fact]
    public async Task MetadataAndDatabaseSwitchingExplicitlyRejectAsyncFallback()
    {
        await using var fixture = new TransactionAsyncFixture();
        int acquired = 0;
        DmSessionTestHooks.AfterExecutionAcquired = _ => acquired++;
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Connection.GetSchemaAsync());
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Connection.GetSchemaAsync("Tables"));
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Connection.GetSchemaAsync("Tables", []));
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Connection.ChangeDatabaseAsync("synthetic"));
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }
        Assert.Equal(0, acquired);
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Fact]
    public async Task CloseAndDisposeOnlyDetachTransportWithoutSendingProtocolFrames()
    {
        var fixture = new TransactionAsyncFixture();
        var changes = new List<StateChangeEventArgs>();
        fixture.Connection.StateChange += (_, change) => changes.Add(change);
        await fixture.Connection.CloseAsync();
        await fixture.Connection.CloseAsync();
        await fixture.Connection.DisposeAsync();
        await fixture.Connection.DisposeAsync();
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(ConnectionState.Closed, fixture.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Closed, fixture.Session.State);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, fixture.Transaction.Outcome);
        Assert.Single(changes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Connection.OpenAsync(CancellationToken.None));
    }
}
