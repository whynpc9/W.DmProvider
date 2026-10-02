using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.AsyncTests;

public sealed class CommandCancellationTests
{
    [Theory]
    [InlineData("reader", false)]
    [InlineData("reader", true)]
    [InlineData("nonquery", false)]
    [InlineData("nonquery", true)]
    [InlineData("scalar", false)]
    [InlineData("scalar", true)]
    [InlineData("prepare", false)]
    [InlineData("prepare", true)]
    public async Task CancellationAfterLeaseBeforeInvocationPreservesActiveTransaction(string method, bool byCommand)
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        using var caller = new CancellationTokenSource();
        DmCommand.AfterExecutionCaptured = _ => { if (byCommand) command.Cancel(); else caller.Cancel(); };
        try
        {
            Task call = Start(command, method, caller.Token);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            if (byCommand) Assert.Equal(DmCancelSource.Command, Assert.IsType<DmOperationCanceledException>(error).FailureInfo.CancelSource);
            else Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(0, fixture.Channel.Sends);
            Assert.False(fixture.Channel.IsClosed);
            Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
            Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
            command.CommandText = "SELECT 2 FROM DUAL";
        }
        finally { DmCommand.AfterExecutionCaptured = null; }
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("lease-before-publication")]
    public async Task CommandCancelIsStickyBeforeTheCapturedLeaseIsPublished(string boundary)
    {
        await using var database = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        if (boundary == "plan") DmCommand.AfterPlanCaptured = command.Cancel;
        else DmSessionTestHooks.AfterExecutionAcquired = _ => command.Cancel();
        try
        {
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => command.ExecuteReaderAsync());
            Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
            Assert.True(error.FailureInfo.ConnectionReusable);
            Assert.Equal(0, database.Channel.Sends);
            Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
            Assert.Equal(DmTransactionOutcome.Active, database.Transaction.Outcome);
            command.CommandText = "SELECT 2 FROM DUAL";
        }
        finally
        {
            DmCommand.AfterPlanCaptured = null;
            DmSessionTestHooks.AfterExecutionAcquired = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreSendCancellationRetainsExistingPreparedHandle(bool byCommand)
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        Statement statement = CancellationApiFixture.InstallStatement(command, fixture);
        statement.__t02_method_0600088A(true);
        var metadata = Array.Empty<W.Dm.Internal.Execution.DmParameterMetadata>();
        ReaderAsyncFixture.SetField(command, "preparedMetadata", metadata);
        using var caller = new CancellationTokenSource();
        DmTransportTestHooks.BeforeSendAttempt = _ => { if (byCommand) command.Cancel(); else caller.Cancel(); };
        try
        {
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => command.PrepareAsync(caller.Token));
            Assert.Equal(byCommand ? DmCancelSource.Command : DmCancelSource.User, error.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
            Assert.True(error.FailureInfo.ConnectionReusable);
            Assert.Same(statement, command.Statement);
            Assert.Same(metadata, typeof(DmCommand).GetField("preparedMetadata", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(command));
            Assert.False(statement.P());
            Assert.True(statement.b());
            Assert.Equal(0, fixture.Channel.Sends);
            Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
            Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        }
        finally
        {
            DmTransportTestHooks.Reset();
            // Synthetic handle has no server object; release it locally after assertions.
            statement.o();
        }
    }

    [Theory]
    [InlineData("half-send", false)]
    [InlineData("half-send", true)]
    [InlineData("half-receive", false)]
    [InlineData("half-receive", true)]
    public async Task PartialIoTerminationBreaksOnlyCapturedSessionWithoutReplay(string mode, bool byCommand)
    {
        await using var fixture = new TransactionAsyncFixture();
        var channel = new CancellationBarrierChannel(mode);
        CancellationApiFixture.InstallChannel(fixture, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        using var caller = new CancellationTokenSource();
        Task call = command.ExecuteReaderAsync(caller.Token);
        await channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (byCommand) command.Cancel(); else caller.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(byCommand ? DmCancelSource.Command : DmCancelSource.User, error.FailureInfo.CancelSource);
        if (!byCommand) Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(channel.IsClosed);
        Assert.Equal(1, channel.FramesSent);
        Assert.True(mode == "half-send" ? channel.SentBytes > 0 : channel.ReceivedBytes > 0);
    }

    [Fact]
    public async Task InactiveCommandCancelCannotInterruptAnotherCommand()
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var idle = new DmCommand("SELECT 0 FROM DUAL", fixture.Connection, fixture.Transaction);
        await using var busy = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        using var caller = new CancellationTokenSource();
        Task call = busy.ExecuteReaderAsync(caller.Token);
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        idle.Cancel();
        idle.Cancel();
        Assert.False(call.IsCompleted);
        Assert.False(fixture.Channel.IsClosed);
        caller.Cancel();
        await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
    }

    [Fact]
    public async Task StaleCapturedCommandExecutionCannotCancelLaterCommand()
    {
        await using var fixture = new TransactionAsyncFixture();
        await using var first = new DmCommand("SELECT 1 FROM DUAL", fixture.Connection, fixture.Transaction);
        DmExecutionLease? old = null;
        using var preSend = new CancellationTokenSource();
        DmCommand.AfterExecutionCaptured = lease => { old = lease; preSend.Cancel(); };
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.PrepareAsync(preSend.Token)); }
        finally { DmCommand.AfterExecutionCaptured = null; }
        Assert.NotNull(old);
        await using var next = new DmCommand("SELECT 2 FROM DUAL", fixture.Connection, fixture.Transaction);
        using var nextToken = new CancellationTokenSource();
        Task call = next.PrepareAsync(nextToken.Token);
        await fixture.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        old!.Cancel(); // Captured callback resumes only after the next execution is sending.
        first.Cancel();
        Assert.False(fixture.Channel.IsClosed);
        Assert.False(call.IsCompleted);
        nextToken.Cancel();
        await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
    }

    [Fact]
    public async Task CancelInterruptsSynchronousCommandByClosingItsCapturedPhysicalSession()
    {
        await using var database = new TransactionAsyncFixture();
        var channel = new SynchronousCancellationChannel();
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        Task call = Task.Run(command.Prepare); // Exercise the supported synchronous facade on a test worker.
        await channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        command.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.Equal(ConnectionState.Broken, database.Connection.State);
        Assert.True(channel.IsClosed);
        Assert.Equal(1, channel.Sends);
        command.Cancel();
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        database.Connection.Close(); // Only explicit connection close changes Broken to Closed.
        Assert.Equal(DmPhysicalSessionState.Closed, database.Session.State);
        Assert.Equal(ConnectionState.Closed, database.Connection.State);
        Assert.Equal(1, channel.Sends);
    }

    internal static Task Start(DmCommand command, string method, CancellationToken token) => method switch
    {
        "reader" => command.ExecuteReaderAsync(token),
        "nonquery" => command.ExecuteNonQueryAsync(token),
        "scalar" => command.ExecuteScalarAsync(token),
        _ => command.PrepareAsync(token)
    };
}

internal static class CancellationApiFixture
{
    internal static void InstallChannel(TransactionAsyncFixture fixture, IDmByteChannel channel)
    {
        object wrapper = fixture.Connection.m_ConnInst.GetCsi().A();
        var field = wrapper.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((DmTransport)field.GetValue(wrapper)!).Dispose();
        field.SetValue(wrapper, new DmTransport(channel));
    }

    internal static Statement InstallStatement(DmCommand command, TransactionAsyncFixture fixture)
    {
        Statement statement = ReaderAsyncFixture.BareStatement(fixture.Connection.m_ConnInst, command);
        statement.F().SetColumnsInfo([]);
        command.Statement = statement;
        ReaderAsyncFixture.SetField(command, "statementSession", fixture.Session);
        ReaderAsyncFixture.SetField(command, "statementConnection", fixture.Connection);
        return statement;
    }
}

// One frame is interrupted at an explicit partial-I/O boundary, or one query
// response is delivered before a reader fetch blocks. No timing-based sleeps.
internal sealed class CancellationBarrierChannel(string mode) : IDmByteChannel
{
    internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int FramesSent { get; private set; }
    internal int SentBytes { get; private set; }
    internal int ReceivedBytes { get; private set; }
    internal List<int> Budgets { get; } = [];
    private int sendCalls;
    private int receiveCalls;
    private short opcode;
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous send is forbidden in this async fixture.");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Synchronous receive is forbidden in this async fixture.");
    public async ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        sendCalls++;
        if (mode == "half-send" && sendCalls == 2)
        {
            Blocked.TrySetResult();
            await Release.Task.WaitAsync(token).ConfigureAwait(false);
        }
        if (sendCalls == 1 || mode != "half-send")
        {
            FramesSent++;
            opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Budgets.Add(DmInvocation.Current.Deadline.RemainingMilliseconds);
        }
        int sent = mode == "half-send" && sendCalls == 1 ? Math.Max(1, count / 2) : count;
        SentBytes += sent;
        return sent;
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        receiveCalls++;
        if (mode == "query-then-fetch" && receiveCalls == 1)
        {
            byte[] frame = Frame(opcode);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), long.MaxValue);
            // Zero columns and zero cached rows still produce a rowset requiring Fetch.
            frame.CopyTo(buffer, offset);
            ReceivedBytes += frame.Length;
            return frame.Length;
        }
        if (mode == "heartbeat" && receiveCalls == 1)
        {
            byte[] heartbeat = Frame(W.Dm.Internal.Protocol.DmFrameReader.HeartbeatCommand);
            heartbeat.CopyTo(buffer, offset);
            ReceivedBytes += heartbeat.Length;
            return heartbeat.Length;
        }
        if (mode == "half-receive" && receiveCalls == 1)
        {
            int partial = Math.Max(1, count / 2);
            Array.Clear(buffer, offset, partial);
            ReceivedBytes += partial;
            return partial;
        }
        Blocked.TrySetResult();
        await Release.Task.WaitAsync(token).ConfigureAwait(false);
        return 0;
    }
    public void Dispose() { IsClosed = true; Release.TrySetResult(); }
    internal static byte[] Frame(short command)
    {
        byte[] frame = new byte[64];
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), command);
        for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
        return frame;
    }
}

internal sealed class SynchronousCancellationChannel : IDmByteChannel
{
    internal TaskCompletionSource ReceiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends { get; private set; }
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds) { Sends++; return count; }
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        ReceiveEntered.TrySetResult();
        released.Task.GetAwaiter().GetResult();
        return 0;
    }
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => throw new InvalidOperationException("This fixture must exercise synchronous send.");
    public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => throw new InvalidOperationException("This fixture must exercise synchronous receive.");
    public void Dispose() { IsClosed = true; released.TrySetResult(); }
}
