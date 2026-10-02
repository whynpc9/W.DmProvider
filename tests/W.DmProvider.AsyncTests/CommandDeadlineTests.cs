using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class CommandDeadlineTests
{
    [Theory]
    [InlineData("reader")]
    [InlineData("nonquery")]
    [InlineData("scalar")]
    [InlineData("prepare")]
    public async Task DeadlineBeforeSendPreservesPreparedHandleAndActiveTransaction(string method)
    {
        var clock = new CancellationTestClock();
        await using var database = new TransactionAsyncFixture(clock);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction) { CommandTimeout = 1 };
        var statement = CancellationApiFixture.InstallStatement(command, database);
        statement.__t02_method_0600088A(true);
        var metadata = Array.Empty<W.Dm.Internal.Execution.DmParameterMetadata>();
        ReaderAsyncFixture.SetField(command, "preparedMetadata", metadata);
        DmTransportTestHooks.BeforeSendAttempt = _ => clock.Advance(TimeSpan.FromSeconds(1));
        try
        {
            var error = await Assert.ThrowsAsync<DmTimeoutException>(() => CommandCancellationTests.Start(command, method, default));
            Assert.Equal(DmErrorKind.Timeout, error.ErrorKind);
            Assert.Equal("WDM_TIMEOUT", error.DriverErrorCode);
            Assert.Equal(0, error.Number);
            Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
            Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
            Assert.True(error.FailureInfo.ConnectionReusable);
            Assert.Same(statement, command.Statement);
            Assert.Same(metadata, typeof(DmCommand).GetField("preparedMetadata", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(command));
            Assert.False(statement.P());
            Assert.Equal(0, database.Channel.Sends);
            Assert.Equal(DmTransactionOutcome.Active, database.Transaction.Outcome);
            Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        }
        finally { DmTransportTestHooks.Reset(); statement.o(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstTerminalCauseWinsTokenAndDeadlineRace(bool timeoutFirst)
    {
        var clock = new CancellationTestClock();
        await using var database = new TransactionAsyncFixture(clock);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction) { CommandTimeout = 1 };
        using var caller = new CancellationTokenSource();
        Task call = command.PrepareAsync(caller.Token);
        await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (timeoutFirst) { clock.Advance(TimeSpan.FromSeconds(1)); caller.Cancel(); }
        else { caller.Cancel(); clock.Advance(TimeSpan.FromSeconds(1)); }
        Exception? error = await Record.ExceptionAsync(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        if (timeoutFirst)
        {
            var timeout = Assert.IsType<DmTimeoutException>(error);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo.CancelSource);
        }
        else
        {
            var canceled = Assert.IsType<DmOperationCanceledException>(error);
            Assert.Equal(caller.Token, canceled.CancellationToken);
            Assert.Equal(DmCancelSource.User, canceled.FailureInfo.CancelSource);
        }
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(1, database.Channel.Sends);
    }

    [Fact]
    public async Task HeartbeatDoesNotRenewCommandTotalDeadline()
    {
        var clock = new CancellationTestClock();
        await using var database = new TransactionAsyncFixture(clock);
        var channel = new CancellationBarrierChannel("heartbeat");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction) { CommandTimeout = 1 };
        Task call = command.PrepareAsync();
        await channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(call.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.True(channel.IsClosed);
        Assert.Equal(1, channel.FramesSent);
        Assert.Equal(64, channel.ReceivedBytes);
    }

    [Fact]
    public async Task InfiniteCommandDeadlineStillAllowsUserCancellationAndRejectsNegativeValues()
    {
        var clock = new CancellationTestClock();
        await using var database = new TransactionAsyncFixture(clock);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction) { CommandTimeout = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => command.CommandTimeout = -1);
        using var caller = new CancellationTokenSource();
        Task call = command.PrepareAsync(caller.Token);
        await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromDays(100));
        Assert.False(call.IsCompleted);
        caller.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
    }

    [Fact]
    public async Task BusinessTimeBetweenReadsDoesNotConsumeNextPublicCallBudget()
    {
        var clock = new CancellationTestClock();
        await using var fixture = new ReaderAsyncFixture(cached: true, clock: clock, timeoutSeconds: 1);
        Assert.True(await fixture.Reader.ReadAsync());
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(17, await fixture.Reader.GetFieldValueAsync<int>(0));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await fixture.Reader.ReadAsync());
        Assert.False(fixture.Database.Channel.IsClosed);
        Assert.Equal(0, fixture.Database.Channel.Sends);
    }

    [Fact]
    public async Task ReadBudgetStartsFreshAndFetchUsesSameFrozenBudget()
    {
        var clock = new CancellationTestClock();
        await using var database = new TransactionAsyncFixture(clock);
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction) { CommandTimeout = 1 };
        CancellationApiFixture.InstallStatement(command, database);
        await using var reader = await command.ExecuteReaderAsync();
        clock.Advance(TimeSpan.FromDays(1));
        Task<bool> read = reader.ReadAsync();
        await channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1000, 1000 }, channel.Budgets);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(read.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
        Assert.Equal(2, channel.FramesSent);
        Assert.True(channel.IsClosed);
    }
}
