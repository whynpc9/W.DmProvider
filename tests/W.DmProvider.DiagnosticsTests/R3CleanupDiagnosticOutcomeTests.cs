using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.DiagnosticsTests;

[Collection("Diagnostics serial")]
[Trait("Category", "Contract")]
[Trait("Feature", "Diagnostics")]
public sealed class R3CleanupDiagnosticOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicNonQueryReportsExecutionAndSuccessfulCleanupWithoutCompletingCleanup(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        int result = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
        Assert.Equal(-1, result);
        Assert.Equal(new short[] { 3, 5, 44, 4 }, fixture.Channel.Opcodes);
        AssertCleanupDidNotCompleteBusiness(Assert.Single(fixture.Channel.CloseInvocations));
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        await observer.AssertResults(("execute", "success", 2));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicReaderCachedGettersAndRepeatedDisposeAddNoNetworkSpan(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        var reader = asynchronous ? await command.ExecuteReaderAsync() : command.ExecuteReader();
        Assert.True(asynchronous ? await reader.ReadAsync() : reader.Read());
        await observer.AssertResults(("fetch", "success", 2));
        int sends = fixture.Channel.Opcodes.Count;
        Assert.Equal(17, reader.GetInt32(0));
        Assert.Equal(17, reader.GetInt32(0));
        Assert.False(reader.IsDBNull(0));
        Assert.Equal(sends, fixture.Channel.Opcodes.Count);
        await observer.AssertResults(("fetch", "success", 2));
        if (asynchronous) await reader.DisposeAsync(); else reader.Dispose();
        Assert.True(reader.IsClosed);
        AssertCleanupDidNotCompleteBusiness(Assert.Single(fixture.Channel.CloseInvocations));
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        await observer.AssertResults(("fetch", "success", 3));
        if (asynchronous) await reader.DisposeAsync(); else reader.Dispose();
        Assert.Equal(new short[] { 3, 5, 4 }, fixture.Channel.Opcodes);
        command.CommandText = "SELECT NEXT_SYNTHETIC_VALUE";
        await observer.AssertResults(("fetch", "success", 3));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublicPreparedDisposeOrConnectionChangeReportsOnlyAcknowledgedCleanup(bool asynchronous, bool rebind)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        if (asynchronous) await command.PrepareAsync(); else command.Prepare();
        if (rebind) command.Connection = null;
        else if (asynchronous) await command.DisposeAsync();
        else command.Dispose();
        Assert.Equal(new short[] { 3, 5, 4 }, fixture.Channel.Opcodes);
        AssertCleanupDidNotCompleteBusiness(Assert.Single(fixture.Channel.CloseInvocations));
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        await observer.AssertResults(("prepare", "success", 1), ("execute", "success", 1));
        if (rebind) Assert.Null(command.Connection);
        else fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedConnectionPreparedHandleCleanupWithoutWireDoesNotInventSuccess(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        if (asynchronous) await command.PrepareAsync(); else command.Prepare();
        await observer.AssertResults(("prepare", "success", 1));
        if (asynchronous) await fixture.Connection.CloseAsync(); else fixture.Connection.Close();
        Assert.Equal(ConnectionState.Closed, fixture.Connection.State);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Closed, fixture.Session.State);
        if (asynchronous) await command.DisposeAsync(); else command.Dispose();
        Assert.Equal(new short[] { 3, 5 }, fixture.Channel.Opcodes);
        Assert.Empty(fixture.Channel.CloseInvocations);
        await observer.AssertResults(("prepare", "success", 1));
        if (asynchronous) await command.DisposeAsync(); else command.Dispose();
        await observer.AssertResults(("prepare", "success", 1));
    }

    [Theory]
    [InlineData(false, "truncated")]
    [InlineData(true, "truncated")]
    [InlineData(false, "io")]
    [InlineData(true, "io")]
    [InlineData(false, "deadline")]
    [InlineData(true, "deadline")]
    public async Task FailedCloseNeverReceivesSuccessAndStillReleasesReaderOwnership(bool asynchronous, string failure)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        var reader = asynchronous ? await command.ExecuteReaderAsync() : command.ExecuteReader();
        fixture.Channel.CloseFailure = failure;
        Exception? error = asynchronous ? await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask()) :
            Record.Exception(() => reader.Dispose());
        Assert.NotNull(error);
        if (failure == "io") Assert.Same(fixture.Channel.ReceiveFailure, error);
        if (failure == "truncated") Assert.IsType<EndOfStreamException>(error);
        if (failure == "deadline") Assert.IsType<DmTimeoutException>(error);
        Assert.True(reader.IsClosed);
        Assert.True(fixture.Channel.IsClosed);
        Assert.True(fixture.Session.State is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed);
        var cleanup = Assert.Single(fixture.Channel.CloseInvocations);
        Assert.True(cleanup.SendAttempted); Assert.False(cleanup.Completed); Assert.True(cleanup.IsDisposed);
        Assert.Equal(failure == "deadline" ? DmCancelSource.TotalDeadline : DmCancelSource.None, cleanup.TerminalCause);
        await observer.AssertResults(("fetch", "success", 1), ("fetch", failure == "deadline" ? "timeout" : "transport_error", 1));
        int sends = fixture.Channel.Opcodes.Count;
        if (asynchronous)
            Assert.Same(error, await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask()));
        else reader.Dispose();
        command.CommandText = "SELECT NEXT_SYNTHETIC_VALUE"; // The reader released its command plan even after cleanup failed.
        Assert.Equal(sends, fixture.Channel.Opcodes.Count);
        await observer.AssertResults(("fetch", "success", 1), ("fetch", failure == "deadline" ? "timeout" : "transport_error", 1));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulOldStatementCloseCannotTurnTheFailingBusinessSecondWireIntoSuccess(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        if (asynchronous) await command.PrepareAsync(); else command.Prepare();
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        // A public binding change invalidates the prepared statement. The next API
        // closes that old handle in its business invocation, then allocates again.
        command.CommandText = "SELECT ? FROM SYNTHETIC_TABLE";
        command.Parameters.Add(new DmParameter("value", 17));
        fixture.Channel.FailSecondAllocation = true;
        Exception? error = asynchronous ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync()) :
            Record.Exception(() => command.ExecuteNonQuery());
        Assert.IsType<EndOfStreamException>(error);
        Assert.Equal(new short[] { 3, 5, 4, 3 }, fixture.Channel.Opcodes);
        var business = Assert.Single(fixture.Channel.CloseInvocations);
        Assert.Same(business, fixture.Channel.FailedAllocationInvocation);
        Assert.True(business.SendAttempted); Assert.False(business.Completed); Assert.True(business.IsDisposed);
        Assert.True(fixture.Channel.IsClosed);
        await observer.AssertResults(("execute", "transport_error", 1));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicNonDefaultTransactionBeginReportsConfigurationCleanupAndActivationSuccess(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        fixture.Channel.ConfiguringTransaction = true;
        DmTransaction transaction = asynchronous ? (DmTransaction)await fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable) :
            (DmTransaction)fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        fixture.Channel.ConfiguringTransaction = false;
        Assert.Equal(IsolationLevel.Serializable, transaction.IsolationLevel);
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        Assert.Equal(new short[] { 3, 5, 4 }, fixture.Channel.Opcodes);
        AssertCleanupDidNotCompleteBusiness(Assert.Single(fixture.Channel.CloseInvocations));
        await observer.AssertResults(("transaction_begin", "success", 3));
        if (asynchronous) await transaction.RollbackAsync(); else transaction.Rollback();
        Assert.Equal(DmTransactionOutcome.RolledBack, transaction.Outcome);
        Assert.Equal(new short[] { 3, 5, 4, 9 }, fixture.Channel.Opcodes);
        await observer.AssertResults(("transaction_begin", "success", 3), ("rollback", "success", 1));
        await transaction.DisposeAsync();
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false, "truncated")]
    [InlineData(true, "truncated")]
    [InlineData(false, "deadline")]
    [InlineData(true, "deadline")]
    public async Task PublicTransactionBeginFailedCleanupCannotActivateOrReportCleanupSuccess(bool asynchronous, string failure)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        fixture.Channel.ConfiguringTransaction = true;
        fixture.Channel.CloseFailure = failure;
        Exception? error = asynchronous ? await Record.ExceptionAsync(() => fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable).AsTask()) :
            Record.Exception(() => fixture.Connection.BeginTransaction(IsolationLevel.Serializable));
        Assert.NotNull(error);
        var cleanup = Assert.Single(fixture.Channel.CloseInvocations);
        Assert.True(cleanup.SendAttempted); Assert.False(cleanup.Completed); Assert.True(cleanup.IsDisposed);
        Assert.Equal(new short[] { 3, 5, 4 }, fixture.Channel.Opcodes);
        Assert.Equal(ConnectionState.Closed, fixture.Connection.State);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Closed, fixture.Session.State);
        if (failure == "deadline")
        {
            var timeout = Assert.IsType<DmTimeoutException>(error);
            Assert.Equal(DmFailurePhase.Cleanup, timeout.FailureInfo.Phase);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, timeout.FailureInfo.OperationOutcome);
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, timeout.FailureInfo.TransactionOutcome);
            Assert.False(timeout.FailureInfo.ConnectionReusable);
        }
        else Assert.IsType<EndOfStreamException>(error);
        await observer.AssertResults(("transaction_begin", "success", 1), ("transaction_begin", failure == "deadline" ? "timeout" : "transport_error", 1));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false, "nonquery")]
    [InlineData(true, "nonquery")]
    [InlineData(false, "scalar")]
    [InlineData(true, "scalar")]
    [InlineData(false, "reader")]
    [InlineData(true, "reader")]
    public async Task VerifiedServerErrorSurvivesSuccessfulIndependentCleanupWithoutFalseTransportError(bool asynchronous, string method)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        fixture.Channel.ConfiguringTransaction = true;
        DmTransaction transaction = asynchronous ? (DmTransaction)await fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable) :
            (DmTransaction)fixture.Connection.BeginTransaction(IsolationLevel.Serializable);
        fixture.Channel.ConfiguringTransaction = false;
        int beforeCloses = fixture.Channel.CloseInvocations.Count;
        await using var command = new DmCommand("INSERT INTO SYNTHETIC_TABLE VALUES (17)", fixture.Connection, transaction);
        fixture.Channel.ServerError = true;
        DmException error = asynchronous ? await Assert.ThrowsAsync<DmException>(() => method switch
        {
            "nonquery" => command.ExecuteNonQueryAsync(),
            "scalar" => command.ExecuteScalarAsync(),
            "reader" => command.ExecuteReaderAsync(),
            _ => throw new InvalidOperationException("Unexpected public command method.")
        }) : Assert.Throws<DmException>(() =>
        {
            _ = method switch
            {
                "nonquery" => (object)command.ExecuteNonQuery(),
                "scalar" => command.ExecuteScalar(),
                "reader" => command.ExecuteReader(),
                _ => throw new InvalidOperationException("Unexpected public command method.")
            };
        });
        Assert.NotNull(error.FailureInfo);
        Assert.Equal(-2106, error.Number);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.Equal(-2106, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmCancelSource.None, error.FailureInfo.CancelSource);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.False(fixture.Channel.IsClosed);
        Assert.Equal(beforeCloses + 1, fixture.Channel.CloseInvocations.Count);
        AssertCleanupDidNotCompleteBusiness(fixture.Channel.CloseInvocations[^1]);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        string operation = method == "reader" ? "fetch" : "execute";
        Assert.Equal(new short[] { 3, 5, 4, 3, asynchronous ? (short)5 : (short)91, 4 }, fixture.Channel.Opcodes);
        await observer.AssertResults(("transaction_begin", "success", 3), (operation, "server_error", 1), (operation, "success", 1));
        fixture.Channel.ServerError = false;
        if (asynchronous) await transaction.RollbackAsync(); else transaction.Rollback();
        Assert.Equal(DmTransactionOutcome.RolledBack, transaction.Outcome);
        await transaction.DisposeAsync();
        await observer.AssertResults(("transaction_begin", "success", 3), (operation, "server_error", 1), (operation, "success", 1), ("rollback", "success", 1));
        fixture.Channel.AssertPath(asynchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseConnectionCallbackFailureAfterCloseAckCannotClaimWholeCleanupSuccess(bool asynchronous)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        var reader = asynchronous ? await command.ExecuteReaderAsync(CommandBehavior.CloseConnection) :
            command.ExecuteReader(CommandBehavior.CloseConnection);
        var marker = new InvalidOperationException("Synthetic close notification failure.");
        StateChangeEventHandler callback = (_, args) => { if (args.CurrentState == ConnectionState.Closed) throw marker; };
        fixture.Connection.StateChange += callback;
        try
        {
            Exception? error = asynchronous ? await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask()) :
                Record.Exception(() => reader.Dispose());
            Assert.Same(marker, error);
        }
        finally { fixture.Connection.StateChange -= callback; }
        Assert.True(reader.IsClosed); Assert.True(fixture.Channel.IsClosed);
        Assert.False(Assert.Single(fixture.Channel.CloseInvocations).Completed);
        await observer.AssertResults(("fetch", "success", 1), ("fetch", "transport_error", 1));
    }

    [Theory]
    [InlineData(false, "stopped")]
    [InlineData(true, "stopped")]
    [InlineData(false, "measurement")]
    [InlineData(true, "measurement")]
    public async Task ThrowingDiagnosticListenersCannotChangeSuccessfulPublicCleanup(bool asynchronous, string fault)
    {
        await using var fixture = new CleanupFixture();
        await fixture.Open(asynchronous);
        using var caller = Caller(); using var observer = new CleanupObserver(caller);
        await observer.Baseline();
        using var throwing = new PublicListener(fault);
        long failures = DmDiagnosticsCore.Snapshot.CallbackFailures;
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", fixture.Connection);
        Assert.Equal(-1, asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery());
        await observer.AssertResults(("execute", "success", 2));
        if (fault == "measurement") Assert.ThrowsAny<Exception>(() => throwing.Meter.RecordObservableInstruments());
        else throwing.Meter.RecordObservableInstruments();
        Assert.True(DmDiagnosticsCore.Snapshot.CallbackFailures > failures);
        AssertCleanupDidNotCompleteBusiness(Assert.Single(fixture.Channel.CloseInvocations));
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    private static Activity Caller() => new Activity("synthetic.cleanup.caller").SetIdFormat(ActivityIdFormat.W3C).Start();
    private static void AssertCleanupDidNotCompleteBusiness(DmInvocation cleanup)
    {
        Assert.True(cleanup.SendAttempted);
        Assert.True(cleanup.IsDisposed);
        Assert.False(cleanup.Completed);
        Assert.Equal(DmCancelSource.None, cleanup.TerminalCause);
    }

    // Public APIs, session coordinator and protocol codecs run unchanged. Only
    // STARTUP/LOGIN is replaced by the established synthetic connector hook;
    // allocate/prepare/execute/close/transaction traffic uses the real byte wire.
    private sealed class CleanupFixture : IAsyncDisposable
    {
        internal readonly CleanupChannel Channel;
        internal readonly DmDataSource Source;
        internal DmConnection Connection = null!;
        internal DmSession Session = null!;
        private readonly CleanupClock clock = new();
        internal CleanupFixture()
        {
            Channel = new CleanupChannel(clock);
            Source = new DmDataSource(new DmConnectionStringBuilder
            {
                Server = "127.0.0.1", User = "CLEANUP_SYNTH", Password = "synthetic_only", Pooling = false,
                TransportSecurity = DmTransportSecurity.PlaintextAllowed, CommandTimeout = 5,
                CleanupTimeout = TimeSpan.FromSeconds(1)
            }.ConnectionString);
            DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
            {
                var instance = new DmConnInstance(candidate); candidate.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60"; instance.ConnProperty.ServerEncoding = "UTF-8";
                object protocol = instance.GetCsi(); object wire = instance.GetCsi().A();
                ((DmTransport)Field(wire, "transport")!).Dispose();
                Set(wire, "transport", new DmTransport(Channel)); Set(wire, "__t02_field_04000AAD", false);
                Set(protocol, "__t02_field_04000ABD", false);
                candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
                return ValueTask.CompletedTask;
            };
        }
        internal async Task Open(bool asynchronous)
        {
            Connection = Source.CreateConnection(); Connection.OperationClock = clock;
            if (asynchronous) await Connection.OpenAsync(); else Connection.Open();
            Session = Connection.Session;
            Assert.Equal(ConnectionState.Open, Connection.State);
            Assert.Empty(Channel.Opcodes);
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        }
        public async ValueTask DisposeAsync()
        {
            try { if (Connection != null) await Connection.DisposeAsync(); }
            finally { try { await Source.DisposeAsync(); } finally { DmPendingOpenTestHooks.Handshake = null; } }
        }
        private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
        private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
    }

    private sealed class CleanupChannel(CleanupClock clock) : IDmByteChannel
    {
        private byte[] response = [];
        private int position, allocations;
        private short opcode;
        internal readonly List<short> Opcodes = [];
        internal readonly List<DmInvocation> CloseInvocations = [];
        internal DmInvocation? FailedAllocationInvocation;
        internal bool FailSecondAllocation, ConfiguringTransaction, ServerError;
        internal string CloseFailure = "";
        internal readonly IOException ReceiveFailure = new("Synthetic close I/O failure.");
        internal int SyncCalls, AsyncCalls;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout)
        { SyncCalls++; return SendCore(buffer, offset, count); }
        public int Receive(byte[] buffer, int offset, int count, int timeout)
        { SyncCalls++; return ReceiveCore(buffer, offset, count); }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, offset, count)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReceiveCore(buffer, offset, count)); }
        private int SendCore(byte[] buffer, int offset, int count)
        {
            opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Opcodes.Add(opcode);
            Assert.Contains(opcode, new short[] { 3, 4, 5, 91, 44, 8, 9 });
            bool query = opcode == 91 || opcode == 5 && buffer[offset + 21] == 1;
            if (opcode == 3)
            {
                allocations++;
                if (FailSecondAllocation && allocations == 2)
                { FailedAllocationInvocation = DmInvocation.Current; response = []; }
                else { response = Frame(opcode, []); BinaryPrimitives.WriteInt32LittleEndian(response, 41 + allocations); Checksum(response); }
            }
            else if (opcode == 4)
            {
                CloseInvocations.Add(DmInvocation.Current);
                response = Frame(opcode, []);
                if (CloseFailure == "truncated") response = response[..31];
            }
            else if (opcode is 5 or 91)
            {
                if (query && ServerError)
                { response = Frame(opcode, new byte[16]); BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(10), -2106); Checksum(response); }
                else if (query && ConfiguringTransaction)
                {
                    response = Frame(opcode, new byte[] { 3, 0, 0 });
                    BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(20), 150);
                    Checksum(response);
                }
                else response = query ? Row(opcode) : Prepare(opcode);
            }
            else if (opcode == 44)
            {
                // The verified RESULT_SET_EMPTY terminal is an opcode-0 reply
                // with SQL code 111, rather than an arbitrary empty result.
                response = Frame(0, []);
                BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(10), 111);
                Checksum(response);
            }
            else response = Frame(0, []); // Validated COMMIT/ROLLBACK control ACK.
            position = 0;
            return count;
        }
        private int ReceiveCore(byte[] buffer, int offset, int count)
        {
            if (opcode == 4 && position == 0)
            {
                if (CloseFailure == "io") throw ReceiveFailure;
                if (CloseFailure == "deadline")
                {
                    clock.Advance(1001);
                    DmInvocation.Current.ThrowIfTerminated();
                    throw new InvalidOperationException("The cleanup deadline did not terminate its invocation.");
                }
            }
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read)); position += read;
            return read;
        }
        internal void AssertPath(bool asynchronous)
        {
            Assert.True(asynchronous ? AsyncCalls > 0 : SyncCalls > 0);
            Assert.Equal(0, asynchronous ? SyncCalls : AsyncCalls);
        }
        public void Dispose() => IsClosed = true;
        private static byte[] Prepare(short opcode)
        {
            byte[] frame = Frame(opcode, []);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160); Checksum(frame);
            return frame;
        }
        private static byte[] Row(short opcode)
        {
            // Independent plain INT32 column descriptor and one cached row:
            // 32 descriptor bytes, five ASCII name bytes, two row-prefix bytes,
            // 12 rowid bytes, two ordinal bytes, two length bytes, four data bytes.
            byte[] body = new byte[59];
            BinaryPrimitives.WriteInt32LittleEndian(body, 7);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 4);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(24), 5);
            new byte[] { 86, 65, 76, 85, 69 }.CopyTo(body, 32);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(53), 4);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(55), 17);
            byte[] frame = Frame(opcode, body);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 1);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1);
            Checksum(frame); return frame;
        }
        private static byte[] Frame(short operation, byte[] body)
        {
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), operation);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            body.CopyTo(frame, 64); Checksum(frame); return frame;
        }
        private static void Checksum(byte[] frame)
        { frame[19] = 0; for (int index = 0; index < 19; index++) frame[19] ^= frame[index]; }
    }

    private sealed class CleanupObserver : IDisposable
    {
        private static readonly HashSet<string> operations = new(StringComparer.Ordinal) { "execute", "prepare", "fetch", "transaction_begin", "rollback" };
        private readonly ActivityListener listener;
        private readonly MeterListener meter = new();
        private readonly object gate = new();
        private readonly Dictionary<string, long> activities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> counters = new(StringComparer.Ordinal);
        private Dictionary<string, long> baseline = new(StringComparer.Ordinal);
        internal CleanupObserver(Activity caller)
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId != caller.TraceId || activity.ParentSpanId != caller.SpanId) return;
                    string? operation = activity.GetTagItem("operation") as string;
                    if (operation == null || !operations.Contains(operation)) return;
                    string result = activity.GetTagItem("result") as string ?? "unknown";
                    Assert.Equal(result == "success" ? ActivityStatusCode.Unset : ActivityStatusCode.Error, activity.Status);
                    lock (gate) activities[operation + "|" + result] = activities.GetValueOrDefault(operation + "|" + result) + 1;
                }
            };
            ActivitySource.AddActivityListener(listener);
            meter.InstrumentPublished = (instrument, owner) =>
            { if (instrument.Meter.Name == DmDiagnostics.MeterName && instrument.Name == "wdm.operation.total") owner.EnableMeasurementEvents(instrument); };
            meter.SetMeasurementEventCallback<long>((_, count, tags, _) =>
            {
                string? operation = null, result = null;
                foreach (var tag in tags) { if (tag.Key == "operation") operation = tag.Value as string; if (tag.Key == "result") result = tag.Value as string; }
                if (operation != null && result != null && operations.Contains(operation))
                    lock (gate) counters[operation + "|" + result] = count;
            });
            meter.Start();
        }
        internal async Task Baseline()
        {
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            meter.RecordObservableInstruments();
            lock (gate) { baseline = new Dictionary<string, long>(counters, StringComparer.Ordinal); activities.Clear(); }
        }
        internal async Task AssertResults(params (string Operation, string Result, long Count)[] expected)
        {
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            meter.RecordObservableInstruments();
            lock (gate)
            {
                var actualMetrics = counters.ToDictionary(pair => pair.Key, pair => pair.Value - baseline.GetValueOrDefault(pair.Key), StringComparer.Ordinal)
                    .Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                var expectedBuckets = expected.Select(item => new KeyValuePair<string, long>(item.Operation + "|" + item.Result, item.Count))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                Assert.Equal(expectedBuckets, actualMetrics);
                Assert.Equal(expectedBuckets, activities.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
            }
        }
        public void Dispose() { meter.Dispose(); listener.Dispose(); }
    }

    private sealed class CleanupClock : TimeProvider
    {
        private readonly List<CleanupTimer> timers = [];
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new CleanupTimer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        internal void Advance(long milliseconds)
        {
            now += milliseconds;
            foreach (var timer in timers.Where(timer => !timer.Closed && timer.Due <= now).ToArray())
            { timer.Due = long.MaxValue; timer.Fire(); }
        }
        private sealed class CleanupTimer(CleanupClock owner, TimerCallback callback, object? state) : ITimer
        {
            internal bool Closed;
            internal long Due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Closed) throw new ObjectDisposedException(nameof(CleanupTimer));
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.now + (long)Math.Ceiling(dueTime.TotalMilliseconds);
                return true;
            }
            internal void Fire() { if (!Closed) callback(state); }
            public void Dispose() => Closed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
