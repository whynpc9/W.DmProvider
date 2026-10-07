using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class ServerErrorClassificationTests
{
    [Theory]
    [InlineData(false, "8.1.4.6", -6602, false)]
    [InlineData(true, "8.1.4.6", -6602, false)]
    [InlineData(false, "8.1.4.6", -6602, true)]
    [InlineData(true, "8.1.4.6", -6602, true)]
    [InlineData(false, "8.1.5.60", -6602, true)]
    [InlineData(true, "8.1.5.60", -6602, true)]
    [InlineData(false, "8.1.4.6", -2106, true)]
    [InlineData(true, "8.1.4.6", -2106, true)]
    [InlineData(false, "8.1.5.60", -2106, false)]
    [InlineData(true, "8.1.5.60", -2106, false)]
    public async Task CompleteStatementErrorHasServerReceiptButGenericSessionIsDiscarded(bool asynchronous, string version, int number, bool transaction)
    {
        using var fixture = new ErrorFixture(version, transaction);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        invocation.Phase = DmFailurePhase.Receive;
        var wire = fixture.Session.BeginWireExchange();
        var response = Response(number, Body("SYNTH", "TABLE", "COL", "diagnostic"));
        var error = await CaptureAsync(asynchronous, response, fixture);
        wire.Dispose();
        Assert.Same(error, invocation.TranslateFailure(error));
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.True(invocation.Completed);
        Assert.True(invocation.ShouldAbortAfterFailure(error));
        Assert.Equal(number, error.Number);
        Assert.Equal(number, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmFailurePhase.Receive, error.FailureInfo.Phase);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
        if (transaction) Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
        Assert.Equal(0, response.a(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingExactTransactionWhitelistKeepsSessionAndReceipt(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-2106, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.False(invocation.ShouldAbortAfterFailure(error));
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedTransactionOwnerCannotEnterPreservationWhitelist(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        fixture.Instance.Transaction = null;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-2106, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
    }

    [Fact]
    public void PrepareErrorIsStrictlyClassifiedWithoutAddingSessionPreservation()
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = Assert.Throws<DmException>(() => c.A(Response(-2106, Body("", "", "", "duplicate")), fixture.Statement));
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.False(error.FailureInfo.ConnectionReusable);
    }

    [Theory]
    [InlineData(false, "negative_length")]
    [InlineData(true, "negative_length")]
    [InlineData(false, "truncated_string")]
    [InlineData(true, "truncated_string")]
    [InlineData(false, "overlong_string")]
    [InlineData(true, "overlong_string")]
    [InlineData(false, "missing_fourth_length")]
    [InlineData(true, "missing_fourth_length")]
    [InlineData(false, "trailing")]
    [InlineData(true, "trailing")]
    [InlineData(false, "bad_boundary")]
    [InlineData(true, "bad_boundary")]
    public async Task MalformedBodyCannotBecomeServerReported(bool asynchronous, string kind)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        byte[] body = kind switch
        {
            "negative_length" => BitConverter.GetBytes(-1),
            "truncated_string" => [6, 0, 0, 0, 65],
            "overlong_string" => BitConverter.GetBytes(int.MaxValue),
            "missing_fourth_length" => new byte[12],
            "trailing" => new byte[17],
            _ => new byte[16]
        };
        var response = Response(-6602, body);
        if (kind == "bad_boundary") response.G(63);
        Exception error;
        if (asynchronous) error = await Assert.ThrowsAsync<InvalidDataException>(() => c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty));
        else error = Assert.Throws<InvalidDataException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
        wire.Dispose();
        Assert.Equal(DmErrorKind.Transport, invocation.CreateFailureInfo(error).ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.False(invocation.ServerErrorAccepted);
        Assert.False(invocation.Completed);
    }

    [Theory]
    [InlineData("UTF-8", false)]
    [InlineData("UTF-8", true)]
    [InlineData("GB18030", false)]
    [InlineData("GB18030", true)]
    public async Task InvalidDeclaredCharsetTailCannotBecomeVerifiedReceipt(string charset, bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        fixture.Instance.ConnProperty.ServerEncoding = charset;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        byte[] invalid = charset == "UTF-8" ? [0xc3] : [0x81, 0x30, 0x81];
        byte[] body = new byte[16 + invalid.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), invalid.Length);
        invalid.CopyTo(body, 16);
        var response = Response(-6602, body);
        Exception failure = asynchronous ? await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty)) :
            Assert.Throws<DecoderFallbackException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
        wire.Dispose();
        Assert.False(invocation.ServerErrorAccepted);
        Assert.Equal(DmErrorKind.Transport, invocation.CreateFailureInfo(failure).ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("command", false)]
    [InlineData("deadline", false)]
    [InlineData("idle", false)]
    [InlineData("user", true)]
    [InlineData("command", true)]
    [InlineData("deadline", true)]
    [InlineData("idle", true)]
    public void ReceiptAndCancellationHaveOneWinner(string cause, bool receiptFirst)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        void Cancel()
        {
            if (cause == "user") caller.Cancel();
            else if (cause == "command") lease.Cancel();
            else fixture.Session.TerminateInvocation(invocation, cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline);
        }
        if (receiptFirst)
        {
            fixture.Session.AcceptServerError(invocation, wire, error, false);
            Cancel();
            Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
            Assert.True(error.HasVerifiedServerResponse);
            wire.Dispose();
            Assert.Same(error, invocation.TranslateFailure(error));
            Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
            Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        }
        else
        {
            Cancel();
            Exception failure = Record.Exception(() => fixture.Session.AcceptServerError(invocation, wire, error, false))!;
            Assert.NotNull(failure);
            Assert.False(error.HasVerifiedServerResponse);
            Assert.False(invocation.Completed);
            wire.Dispose();
            failure = invocation.TranslateFailure(error);
            if (cause is "user" or "command")
            {
                var canceled = Assert.IsType<DmOperationCanceledException>(failure);
                Assert.Equal(cause == "user" ? caller.Token : lease.CommandCancellationToken, canceled.CancellationToken);
                Assert.Equal(cause == "user" ? DmCancelSource.User : DmCancelSource.Command, canceled.FailureInfo.CancelSource);
                Assert.Equal(DmOperationOutcome.Unknown, canceled.FailureInfo.OperationOutcome);
            }
            else
            {
                var timedOut = Assert.IsType<DmTimeoutException>(failure);
                Assert.Equal(cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline, timedOut.FailureInfo.CancelSource);
                Assert.Equal(DmOperationOutcome.Unknown, timedOut.FailureInfo.OperationOutcome);
            }
        }
    }

    [Fact]
    public void ActualDeadlineBudgetExpiredBeforeReceiptCannotBePromotedEvenIfTimerHasNotFired()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        var clock = new SilentDeadlineClock();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, DmDeadline.Start(TimeSpan.FromSeconds(1), clock));
        using var invocation = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var failure = Assert.Throws<DmTimeoutException>(() => fixture.Session.AcceptServerError(invocation, wire, error, false));
        Assert.Equal(DmCancelSource.TotalDeadline, failure.FailureInfo.CancelSource);
        Assert.False(error.HasVerifiedServerResponse);
        wire.Dispose();
    }

    [Theory]
    [InlineData("user")]
    [InlineData("command")]
    [InlineData("deadline")]
    [InlineData("idle")]
    public void PreservedReceiptWinnerCannotBeChangedByLateReaderCancellation(string cause)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-2106, new byte[16]), "UTF-8"));
        fixture.Session.AcceptServerError(invocation, wire, error, true);
        if (cause == "user") caller.Cancel();
        else if (cause == "command") lease.Cancel();
        else fixture.Session.TerminateInvocation(invocation, cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Fact]
    public void SuccessfulCompletedReaderCallStillAllowsCommandCancellationOfItsIdleCursor()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        invocation.Complete();
        Assert.False(invocation.ServerErrorAccepted);
        lease.Cancel();
        Assert.Equal(DmCancelSource.Command, lease.TerminalCause);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Fact]
    public async Task DiagnosticTraceCallbackCanCancelAfterBodyValidationBeforeAcceptance()
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        bool traced = false;
        DmTransactionProtocolTrace.AfterDiagnosticBody = (_, _, _, _, _, _, remaining, complete) =>
        {
            traced = true;
            Assert.True(complete);
            Assert.Equal(0, remaining);
            caller.Cancel();
        };
        try
        {
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => c.AAsync(
                Response(-2106, Body("", "", "", "duplicate")), fixture.Statement, fixture.Instance.ConnProperty));
            Assert.True(traced);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(DmErrorKind.Canceled, error.FailureInfo.ErrorKind);
            Assert.False(invocation.ServerErrorAccepted);
        }
        finally { DmTransactionProtocolTrace.AfterDiagnosticBody = null; wire.Dispose(); }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    public void StaleIdentityCannotAcceptReceiptOrModifyAnotherActiveInvocation(string wrong)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var original = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var identity = wrong == "session" ? original.Identity with { SessionId = original.Identity.SessionId + 1 } :
            original.Identity with { LeaseGeneration = original.Identity.LeaseGeneration + 1 };
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        using (var stale = new DmInvocation(fixture.Session, lease, identity, DmDeadline.Infinite))
        {
            SetField(fixture.Session, "activeInvocation", stale);
            try { Assert.Throws<InvalidOperationException>(() => fixture.Session.AcceptServerError(stale, wire, error, false)); }
            finally { SetField(fixture.Session, "activeInvocation", original); }
        }
        Assert.False(error.HasVerifiedServerResponse);
        Assert.False(original.Completed);
        Assert.Equal(DmCancelSource.None, original.TerminalCause);
        wire.Complete(); wire.Dispose();
    }

    [Fact]
    public void WrongOwnerCannotAcceptReceiptForAnotherSession()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        var other = new DmSession();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        Assert.Throws<InvalidOperationException>(() => other.AcceptServerError(invocation, wire, error, false));
        Assert.False(error.HasVerifiedServerResponse);
        Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
        wire.Complete(); wire.Dispose();
    }

    [Fact]
    public void DisposedWrongWireCannotMarkReceiptOrCompleteCurrentWire()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var old = fixture.Session.BeginWireExchange();
        old.Complete(); old.Dispose();
        var current = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        Assert.Throws<InvalidOperationException>(() => fixture.Session.AcceptServerError(invocation, old, error, false));
        Assert.False(error.HasVerifiedServerResponse);
        Assert.False(invocation.Completed);
        Assert.Same(current, DmWireExchange.Current);
        current.Complete(); current.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDataSourceShortcutKeepsServerFailureAndClosesTransportBeforeReturningPermit(bool asynchronous)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new StatementErrorChannel(-6602);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, channel);
            candidate.m_ConnInst.ConnProperty.ServerVersion = "8.1.4.6";
            candidate.m_ConnInst.ConnProperty.ServerEncoding = "UTF-8";
            return ValueTask.CompletedTask;
        };
        using var command = source.CreateCommand("INSERT INTO SYNTHETIC VALUES (1)");
        DmException error = asynchronous ? await Assert.ThrowsAsync<DmException>(() => command.ExecuteNonQueryAsync()) :
            Assert.Throws<DmException>(() => command.ExecuteNonQuery());
        Assert.True(channel.IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.Equal(-6602, error.FailureInfo.ServerErrorNumber);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(asynchronous ? 0 : 2, channel.SyncSends);
        Assert.Equal(asynchronous ? 2 : 0, channel.AsyncSends);
        Assert.Equal(new short[] { 3, asynchronous ? (short)5 : (short)91 }, channel.Opcodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizedSynchronousExecutionSendFailurePropagatesWithoutReplayAndReturnsCapacityAfterAbort(bool internalNonReader)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new StatementErrorChannel(-6602, failExecutionSend: true);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, channel);
            candidate.m_ConnInst.ConnProperty.ServerVersion = "8.1.4.6";
            candidate.m_ConnInst.ConnProperty.ServerEncoding = "UTF-8";
            return ValueTask.CompletedTask;
        };
        IOException error;
        if (internalNonReader)
        {
            using var connection = source.OpenConnection();
            using var command = new DmCommand("INSERT INTO SYNTHETIC VALUES (1)", connection);
            using var lease = connection.BeginExecution(DmOperationPurpose.Query);
            error = Assert.Throws<IOException>(() => command.ExecuteInternalNonQuery(lease));
            Assert.True(channel.IsClosed);
            connection.Close();
        }
        else
        {
            using var command = source.CreateCommand("INSERT INTO SYNTHETIC VALUES (1)");
            error = Assert.Throws<IOException>(() => command.ExecuteNonQuery());
        }
        Assert.Same(channel.SendFailure, error);
        Assert.Equal(new short[] { 3, 91 }, channel.Opcodes);
        Assert.Equal(2, channel.SyncSends);
        Assert.Equal(0, channel.AsyncSends);
        Assert.True(channel.IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.NotNull(channel.ExecutionInvocation);
        var info = channel.ExecutionInvocation!.CreateFailureInfo(error);
        Assert.Equal(DmErrorKind.Transport, info.ErrorKind);
        Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
        Assert.False(info.ConnectionReusable);
        Assert.Null(info.ServerErrorNumber);
    }

    private static async Task<DmException> CaptureAsync(bool asynchronous, b response, ErrorFixture fixture) =>
        asynchronous ? await Assert.ThrowsAsync<DmException>(() => c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty)) :
            Assert.Throws<DmException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
    private static byte[] Body(params string[] values)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
            foreach (string value in values) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        return body.ToArray();
    }
    private static b Response(int number, byte[] body)
    { var response = new b(Frame(5, number, body)); response.G(64); return response; }
    private static byte[] Frame(short opcode, int number, byte[] body)
    {
        byte[] frame = new byte[64 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), number);
        body.CopyTo(frame, 64);
        for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
        return frame;
    }
    private static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);

    private sealed class ErrorFixture : IDisposable
    {
        internal readonly SyntheticPoolHandshake Handshake = new();
        internal readonly DmDataSource Source = new(PoolApiSettings.Text);
        internal readonly DmConnection Connection;
        internal readonly DmCommand Command;
        internal readonly Statement Statement;
        internal DmSession Session => Connection.Session ?? captured;
        private readonly DmSession captured;
        internal DmConnInstance Instance => Connection.m_ConnInst ?? instance;
        private readonly DmConnInstance instance;
        internal ErrorFixture(string version, bool transaction)
        {
            Connection = Source.OpenConnection();
            captured = Connection.Session;
            instance = Connection.m_ConnInst;
            instance.ConnProperty.ServerVersion = version;
            instance.ConnProperty.ServerEncoding = "UTF-8";
            if (transaction)
            {
                Session.SetTransactionState(DmLocalTransactionState.Starting);
                using var lease = Session.BeginExecution(DmOperationPurpose.TransactionControl);
                using var invocation = lease.BeginInvocation();
                var tx = new DmTransaction(instance);
                instance.Transaction = tx;
                Session.ActivateTransaction(tx, invocation.Identity);
            }
            Command = new DmCommand("synthetic", Connection);
            Statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
            SetField(Statement, "__t02_field_04000923", instance);
            SetField(Statement, "__t02_field_04000924", instance.GetCsi());
            SetField(Statement, "__t02_field_04000933", Command);
            SetField(Statement, "__t02_field_04000925", new b());
            SetField(Statement, "__t02_field_04000926", new b());
            SetField(Statement, "__t02_field_04000927", new DmInfo(instance));
        }
        public void Dispose()
        { Command.Dispose(); Connection.Dispose(); Source.Dispose(); Handshake.Dispose(); }
    }

    private sealed class StatementErrorChannel(int number, bool failExecutionSend = false) : IDmByteChannel
    {
        private byte[] reply = [];
        private int position;
        internal int SyncSends, AsyncSends;
        internal readonly List<short> Opcodes = [];
        internal readonly IOException SendFailure = new("Synthetic execution send failed.");
        internal DmInvocation? ExecutionInvocation;
        public bool IsClosed { get; private set; }
        private int SendCore(byte[] buffer, int offset, int count)
        {
            short opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Opcodes.Add(opcode);
            if (opcode != 3)
            {
                ExecutionInvocation = DmInvocation.Current;
                if (failExecutionSend) throw SendFailure;
            }
            reply = Frame(opcode, opcode == 3 ? 0 : number, opcode == 3 ? [] : new byte[16]);
            position = 0;
            return count;
        }
        private int ReadCore(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(count, reply.Length - position);
            reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read;
            return read;
        }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncSends++; return SendCore(buffer, offset, count); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) => ReadCore(buffer, offset, count);
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); AsyncSends++; return ValueTask.FromResult(SendCore(buffer, offset, count)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReadCore(buffer, offset, count)); }
        public void Dispose() => IsClosed = true;
    }

    private sealed class SilentDeadlineClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(TimeSpan amount) => timestamp += amount.Ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new SilentTimer();
        private sealed class SilentTimer : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
