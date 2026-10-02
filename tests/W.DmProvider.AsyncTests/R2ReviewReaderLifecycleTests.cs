using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class R2ReviewReaderLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidatedReaderRetainsItsFrozenPlanUntilUnconditionalCleanup(bool synchronousClose)
    {
        await using var database = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        // The reader and its plan are returned by the actual command/protocol path.
        await using var reader = (DmDataReader)await command.ExecuteReaderAsync();
        var root = Assert.IsType<DmExecutionLease>(GetField(reader, "executionLease"));
        var plan = Assert.IsType<DmCommandPlan>(GetField(reader, "commandPlan"));
        var gate = Assert.IsType<DmCommandPlanGate>(GetField(command, "commandPlanGate"));
        Assert.False(reader.IsClosed);
        Assert.Same(root, GetField(database.Session, "activeLease"));
        Assert.Equal(DmOperationPurpose.Reader, root.Purpose);

        command.Cancel();
        Assert.Equal(ConnectionState.Broken, database.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.True(channel.IsClosed);
        for (int observation = 0; observation < 3; observation++)
        {
            Assert.True(reader.IsClosed);
            Assert.True(gate.IsExecuting);
            Assert.Same(plan, GetField(command, "activePlan"));
            Assert.Same(plan, GetField(reader, "commandPlan"));
            Assert.Same(root, GetField(reader, "executionLease"));
            Assert.Same(root, GetField(database.Session, "activeLease"));
            Assert.Equal("SELECT 1 FROM DUAL", plan.Sql);
            Assert.Throws<InvalidOperationException>(() => command.CommandText = "SELECT 2 FROM DUAL");
            Assert.Throws<InvalidOperationException>(() => command.Parameters.Add(new DmParameter("p", 1)));
            command.Cancel();
        }
        Assert.Equal("SELECT 1 FROM DUAL", command.CommandText);
        Assert.Empty(command.Parameters.Cast<DmParameter>());
        var canceled = await Assert.ThrowsAsync<DmOperationCanceledException>(() => reader.ReadAsync());
        Assert.Equal(DmCancelSource.Command, canceled.FailureInfo.CancelSource);
        Assert.True(gate.IsExecuting);
        Assert.Equal(1, channel.FramesSent);

        // IsClosed reports unusability; disposal must not be guarded by !IsClosed.
        await Cleanup(reader, synchronousClose);
        Assert.False(gate.IsExecuting);
        Assert.Null(GetField(command, "activePlan"));
        Assert.Null(GetField(reader, "commandPlan"));
        Assert.Null(GetField(reader, "executionLease"));
        Assert.Null(GetField(database.Session, "activeLease"));
        Assert.Throws<ObjectDisposedException>(() => root.BeginInvocation());
        command.CommandText = "SELECT 2 FROM DUAL";
        command.Parameters.Add(new DmParameter("p", 1));
        command.Cancel();
        Assert.Equal(ConnectionState.Broken, database.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(1, channel.FramesSent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OldReaderCleanupAndCancelCannotAffectReplacementSession(bool synchronousClose)
    {
        await using var database = new TransactionAsyncFixture();
        await using var replacement = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel("query-then-fetch");
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        await using var reader = (DmDataReader)await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        var oldRoot = Assert.IsType<DmExecutionLease>(GetField(reader, "executionLease"));
        command.Cancel();
        Assert.True(reader.IsClosed);
        await database.Connection.CloseAsync();
        Assert.Equal(ConnectionState.Closed, database.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Closed, database.Session.State);
        Assert.Null(database.Connection.Session);

        // Install a fully initialized synthetic handshake result after explicit
        // Close, as in CapturedCloseCannotDetachAReplacementPhysicalSession.
        // This tests session identity without claiming a real Open/TLS handshake.
        ReaderAsyncFixture.SetField(database.Connection, "session", replacement.Session);
        database.Connection.m_ConnInst = replacement.Connection.m_ConnInst;
        database.Connection.do_State = ConnectionState.Open;
        Assert.NotEqual(database.Session.SessionId, replacement.Session.SessionId);
        using var newRoot = replacement.Session.BeginExecution(DmOperationPurpose.Query);
        using var newInvocation = newRoot.BeginInvocation();

        // First cleanup occurs while another physical session owns an invocation.
        // CloseConnection on the stale reader must target only its captured owner.
        await Cleanup(reader, synchronousClose);
        Assert.Throws<ObjectDisposedException>(() => oldRoot.BeginInvocation());
        command.CommandText = "SELECT 2 FROM DUAL";
        for (int repeat = 0; repeat < 3; repeat++)
        {
            command.Cancel();
            reader.Close();
            await reader.CloseAsync();
            await reader.DisposeAsync();
            Assert.Same(replacement.Session, database.Connection.Session);
            Assert.Equal(ConnectionState.Open, database.Connection.State);
            Assert.Equal(DmPhysicalSessionState.Busy, replacement.Session.State);
            Assert.Same(newRoot, GetField(replacement.Session, "activeLease"));
            Assert.False(replacement.Channel.IsClosed);
            Assert.Equal(DmTransactionOutcome.Active, replacement.Transaction.Outcome);
            Assert.Equal(0, replacement.Channel.Sends);
            Assert.Same(newInvocation, GetField(replacement.Session, "activeInvocation"));
            newInvocation.ThrowIfTerminated();
        }
        newInvocation.Complete();
        Assert.Equal(1, channel.FramesSent);
    }

    private static async Task Cleanup(DmDataReader reader, bool synchronousClose)
    {
        if (synchronousClose) reader.Close();
        else await reader.DisposeAsync();
        Task close = reader.CloseAsync();
        await close;
        Assert.True(close.IsCompletedSuccessfully);
        Assert.Same(close, reader.CloseAsync());
        await reader.DisposeAsync();
        Assert.True(reader.IsClosed);
    }

    private static object? GetField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
}
