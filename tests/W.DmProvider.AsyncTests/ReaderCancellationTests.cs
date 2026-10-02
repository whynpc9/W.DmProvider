using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class ReaderCancellationTests
{
    [Fact]
    public async Task OldExecuteReaderTokenCannotCancelNewFetchButNewTokenCan()
    {
        await using var database = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        using var executeToken = new CancellationTokenSource();
        await using var reader = await command.ExecuteReaderAsync(executeToken.Token);
        using var readToken = new CancellationTokenSource();
        Task<bool> fetch = reader.ReadAsync(readToken.Token);
        await channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        executeToken.Cancel();
        Assert.False(fetch.IsCompleted);
        Assert.False(channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Busy, database.Session.State);
        readToken.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => fetch.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(readToken.Token, error.CancellationToken);
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(2, channel.FramesSent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAfterReaderReturnInvalidatesIdleReaderAndRepeatedCancelIsHarmless(bool synchronousClose)
    {
        await using var database = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        await using var reader = await command.ExecuteReaderAsync();
        command.Cancel();
        command.Cancel();
        Assert.True(reader.IsClosed);
        Assert.True(channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => reader.ReadAsync());
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        if (synchronousClose) reader.Close();
        Task close = reader.CloseAsync();
        await close;
        Assert.True(close.IsCompletedSuccessfully);
        Assert.Same(close, reader.CloseAsync());
        await reader.DisposeAsync();
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(System.Data.ConnectionState.Broken, database.Connection.State);
        command.Cancel(); // Released plan has no active execution to cancel.
        Assert.Equal(1, channel.FramesSent);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Fact]
    public async Task CancelDuringCloseReleasesReaderAndRepeatedCloseSharesItsCancellation()
    {
        await using var database = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        var reader = await command.ExecuteReaderAsync();
        Task first = reader.CloseAsync();
        await channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = reader.CloseAsync();
        Assert.Same(first, second);
        command.Cancel();
        command.Cancel();
        var firstError = await Assert.ThrowsAsync<DmOperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        var secondError = await Assert.ThrowsAsync<DmOperationCanceledException>(() => second);
        Assert.Same(firstError, secondError);
        Assert.True(first.IsCanceled);
        Assert.True(second.IsCanceled);
        Assert.Equal(DmCancelSource.Command, firstError.FailureInfo.CancelSource);
        Assert.True(reader.IsClosed);
        Assert.True(channel.IsClosed);
        Assert.Same(first, reader.CloseAsync());
        Assert.Equal(2, channel.FramesSent);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Fact]
    public async Task CancellationAfterReaderInvocationBeforeFetchPreservesCachedRowAndTransaction()
    {
        await using var fixture = new ReaderAsyncFixture(cached: true);
        using var caller = new CancellationTokenSource();
        DmDataReader.AfterInvocationEntered = _ => caller.Cancel();
        try
        {
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => fixture.Reader.ReadAsync(caller.Token));
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
            Assert.True(error.FailureInfo.ConnectionReusable);
            Assert.Equal(0, fixture.Database.Channel.Sends);
            Assert.Equal(DmPhysicalSessionState.Busy, fixture.Database.Session.State);
            Assert.Equal(DmTransactionOutcome.Active, fixture.Database.Transaction.Outcome);
        }
        finally { DmDataReader.AfterInvocationEntered = null; }
        Assert.True(await fixture.Reader.ReadAsync());
        Assert.Equal(17, await fixture.Reader.GetFieldValueAsync<int>(0));
    }

    [Fact]
    public async Task CachedReadCompletionWinsLateTokenCancellation()
    {
        await using var fixture = new ReaderAsyncFixture(cached: true);
        using var completedToken = new CancellationTokenSource();
        Assert.True(await fixture.Reader.ReadAsync(completedToken.Token));
        completedToken.Cancel();
        Assert.Equal(17, await fixture.Reader.GetFieldValueAsync<int>(0));
        Assert.False(await fixture.Reader.ReadAsync());
        Assert.Equal(0, fixture.Database.Channel.Sends);
        Assert.False(fixture.Database.Channel.IsClosed);
    }
}
