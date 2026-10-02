using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.AsyncTests;

public sealed class CommandReaderAsyncTests
{
    [Theory]
    [InlineData("reader")]
    [InlineData("nonquery")]
    [InlineData("scalar")]
    [InlineData("prepare")]
    public async Task CommandMethodsWaitForAsyncAllocationReplyAndReleaseTheirFrozenPlanOnFailure(string method)
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        Task operation = method switch
        {
            "reader" => command.ExecuteReaderAsync(),
            "nonquery" => command.ExecuteNonQueryAsync(),
            "scalar" => command.ExecuteScalarAsync(),
            _ => command.PrepareAsync()
        };
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => command.CommandText = "SELECT 2 FROM DUAL");
        Assert.Throws<InvalidOperationException>(() => command.Parameters.Add(new DmParameter("p", 1)));
        fixture.Channel.ReleaseResponse.TrySetResult(false);
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.Sends);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("nonquery")]
    [InlineData("scalar")]
    [InlineData("prepare")]
    public async Task PreCanceledCommandsSendNoBytesAndPreserveActiveTransaction(string method)
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task operation = method switch
        {
            "reader" => command.ExecuteReaderAsync(cancellation.Token),
            "nonquery" => command.ExecuteNonQueryAsync(cancellation.Token),
            "scalar" => command.ExecuteScalarAsync(cancellation.Token),
            _ => command.PrepareAsync(cancellation.Token)
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, fixture.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("next")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task ReaderNetworkMethodsWaitForAsyncReply(string method)
    {
        await using var fixture = new ReaderAsyncFixture(cached: false);
        Task operation = method switch
        {
            "fetch" => fixture.Reader.ReadAsync(),
            "next" => fixture.Reader.NextResultAsync(),
            "close" => fixture.Reader.CloseAsync(),
            _ => fixture.Reader.DisposeAsync().AsTask()
        };
        await fixture.Database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Database.Session.State);
        fixture.Database.Channel.ReleaseResponse.TrySetResult(false);
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Database.Session.State);
        Assert.Equal(1, fixture.Database.Channel.Sends);
        if (method is "close" or "dispose")
        {
            var expected = Assert.IsType<EndOfStreamException>(error);
            Assert.True(fixture.Reader.IsClosed);
            Assert.True(fixture.Database.Channel.IsClosed);
            Task repeat = fixture.Reader.CloseAsync();
            Assert.True(repeat.IsFaulted);
            var repeated = await Assert.ThrowsAsync<EndOfStreamException>(() => repeat);
            Assert.Same(expected, repeated);
            Assert.Equal(1, fixture.Database.Channel.Sends);
            fixture.RecordAssertedCloseFailure(expected);
        }
    }

    [Fact]
    public async Task RepeatedReaderCloseSharesOnePendingCleanupAndReleasesRootLeaseAfterAck()
    {
        await using var fixture = new ReaderAsyncFixture(cached: false);
        Task first = fixture.Reader.CloseAsync();
        await fixture.Database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = fixture.Reader.CloseAsync();
        Assert.Same(first, second);
        Assert.False(second.IsCompleted);
        fixture.Database.Channel.ReleaseResponse.TrySetResult(true);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Reader.IsClosed);
        Assert.Equal(1, fixture.Database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Database.Session.State);
        using var next = fixture.Database.Session.BeginExecution(DmOperationPurpose.Query);
    }

    [Fact]
    public async Task ReentrantCloseFromResponseHookObservesThePublishedPendingTask()
    {
        await using var fixture = new ReaderAsyncFixture(cached: false);
        Task? reentrant = null;
        bool pendingAtReentry = false;
        W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterFrame = (_, _, _, _) =>
        {
            reentrant = fixture.Reader.CloseAsync();
            pendingAtReentry = !reentrant.IsCompleted;
        };
        Task first = fixture.Reader.CloseAsync();
        try
        {
            await fixture.Database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Database.Channel.ReleaseResponse.TrySetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(first, reentrant);
            Assert.True(pendingAtReentry);
            Assert.Equal(1, fixture.Database.Channel.Sends);
        }
        finally { W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterFrame = null; }
    }

    [Fact]
    public async Task PreparedCommandDisposeWaitsForAsyncCloseAck()
    {
        await using var database = new TransactionAsyncFixture();
        var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        var statement = ReaderAsyncFixture.BareStatement(database.Connection.m_ConnInst, command);
        command.Statement = statement;
        ReaderAsyncFixture.SetField(command, "statementSession", database.Session);
        ReaderAsyncFixture.SetField(command, "statementConnection", database.Connection);
        Task dispose = command.DisposeAsync().AsTask();
        await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(dispose.IsCompleted);
        database.Channel.ReleaseResponse.TrySetResult(true);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        await command.DisposeAsync();
        Assert.Equal(1, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        Assert.True(statement.P());
    }

    [Fact]
    public async Task CachedRowsAndValuesNeedNoArtificialWaitAndNoWireIo()
    {
        await using var fixture = new ReaderAsyncFixture(cached: true);
        Task<bool> read = fixture.Reader.ReadAsync();
        Assert.True(read.IsCompletedSuccessfully);
        Assert.True(await read);
        Assert.Equal(17, await fixture.Reader.GetFieldValueAsync<int>(0));
        Assert.False(await fixture.Reader.IsDBNullAsync(0));
        Assert.NotNull(await fixture.Reader.GetSchemaTableAsync());
        Assert.False(await fixture.Reader.ReadAsync());
        Assert.Equal(0, fixture.Database.Channel.Sends);
    }

    [Fact]
    public async Task PreCanceledReadLeavesCachedRowAvailableForNextToken()
    {
        await using var fixture = new ReaderAsyncFixture(cached: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reader.ReadAsync(cancellation.Token));
        Assert.True(await fixture.Reader.ReadAsync(CancellationToken.None));
        Assert.Equal(17, await fixture.Reader.GetFieldValueAsync<int>(0));
        Assert.Equal(0, fixture.Database.Channel.Sends);
    }
}

internal sealed class ReaderAsyncFixture : IAsyncDisposable
{
    internal TransactionAsyncFixture Database { get; }
    internal DmDataReader Reader { get; }
    private readonly DmCommand command;
    private EndOfStreamException? assertedCloseFailure;
    internal void RecordAssertedCloseFailure(EndOfStreamException failure) => assertedCloseFailure = failure;
    internal ReaderAsyncFixture(bool cached, TimeProvider? clock = null, int timeoutSeconds = 5)
    {
        Database = new TransactionAsyncFixture(clock);
        var instance = Database.Connection.m_ConnInst;
        command = new DmCommand("SELECT 17 FROM DUAL", Database.Connection, Database.Transaction);
        var statement = BareStatement(instance, command);
        var info = new DmInfo(instance);
        info.SetColumnsInfo([new DmColumn(instance) { type = 7, prec = 4, name = "VALUE" }]);
        info.SetHasResultSet(true);
        info.SetRowCount(cached ? 1 : long.MaxValue);
        SetField(statement, "__t02_field_04000927", info);
        var cache = new DmResultSetCache(statement, 1, cached ? 1 : long.MaxValue)
        {
            datas = cached ? new byte[][][] { [[], BitConverter.GetBytes(17)] } : [],
            datasStartPos = 0
        };
        SetField(statement, "__t02_field_04000928", cache);
        Reader = new DmDataReader(cache, info, CommandBehavior.Default);
        Reader.AttachExecutionLease(Database.Session.BeginExecution(DmOperationPurpose.Reader,
            DmDeadline.FromSeconds(timeoutSeconds, clock), timeoutSeconds));
    }
    public async ValueTask DisposeAsync()
    {
        // Fixture shutdown aborts its captured transport before disposing its reader;
        // normal cleanup itself is independently covered by the reply-barrier tests.
        await Database.DisposeAsync();
        try { await Reader.DisposeAsync(); }
        catch (EndOfStreamException error) when (
            ReferenceEquals(error, assertedCloseFailure) && Database.Channel.IsClosed && Reader.IsClosed)
        {
            // Only the exact EOF already asserted by the negative cleanup test is
            // repeated by the reader's shared faulted close task. Other failures escape.
        }
        finally { await command.DisposeAsync(); }
    }
    internal static Statement BareStatement(DmConnInstance instance, DmCommand command)
    {
        var statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
        SetField(statement, "__t02_field_04000923", instance);
        SetField(statement, "__t02_field_04000924", instance.GetCsi());
        SetField(statement, "__t02_field_04000933", command);
        SetField(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
        SetField(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
        SetField(statement, "__t02_field_04000927", new DmInfo(instance));
        return statement;
    }
    internal static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}
