using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class R3PreSendInputSourceFailureTests
{
    [Theory]
    [InlineData(false, "local", "rejected")]
    [InlineData(true, "local", "rejected")]
    [InlineData(false, "after_ack", "transport_error")]
    [InlineData(true, "after_ack", "transport_error")]
    [InlineData(false, "cancel", "canceled")]
    [InlineData(true, "cancel", "canceled")]
    public async Task PublicActivityAndObservableCounterClassifyActualInputFailureWithoutChangingRecoverability(bool asynchronous, string failure, string expectedResult)
    {
        await using var fixture = new PublicFixture(asynchronous, 12);
        await fixture.Open(asynchronous);
        using var scope = new Activity("synthetic.r3.input.diagnostics").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var observer = new FailureObserver(scope.TraceId, scope.SpanId);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        long baseline = observer.Counter(expectedResult);
        using var cancellation = new CancellationTokenSource();
        var caller = new SourceFailure(failure == "after_ack" ? 8 : 0, asynchronous);
        await using var command = fixture.Command(DmDbType.Blob, new ThrowingBytes(caller));
        if (failure == "cancel")
        {
            if (asynchronous) caller.BeforeFailure = () => cancellation.Cancel();
            else caller.BeforeFailure = () => command.Cancel();
        }
        Exception error = asynchronous ? (await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(cancellation.Token)))! :
            Record.Exception(() => command.ExecuteNonQuery())!;
        if (failure == "cancel")
        {
            // Sync public ExecuteNonQuery has no token argument, so cancel its active command instead.
            Assert.IsType<DmOperationCanceledException>(error);
        }
        else Assert.Same(caller.Error, Assert.IsType<IOException>(error));
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        var span = Assert.Single(observer.Results);
        Assert.Equal(expectedResult, span.Result); Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.True(observer.Counter(expectedResult) >= baseline + 1);
        Assert.Equal(failure == "local" ? DmPhysicalSessionState.Ready : DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.Equal(failure != "local", fixture.Channels.Single().IsClosed);
        Assert.True(caller.ObservedInvocation!.SendAttempted);
        Assert.Null(caller.ObservedInvocation.LocalInputFailureException);
        Assert.Equal(0, caller.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedAckFrameIsRejectedByTheBoundedUploadBudget(bool asynchronous)
    {
        await using var fixture = new PublicFixture(asynchronous, 12);
        await fixture.Open(asynchronous);
        fixture.Channels[^1].OversizedAck = true;
        await using var command = fixture.Command(DmDbType.Blob, new MemoryStream(new byte[16]));
        Exception error = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync())!
            : Record.Exception(() => command.ExecuteNonQuery())!;
        Assert.Contains("response budget", error!.ToString());
        Assert.Equal(1, fixture.Channels[^1].PutSends);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 3)]
    public async Task PublicFirstChunkCallerFailurePreservesPreparedCommandAndTransactionWithoutMetadataSend(bool asynchronous, bool text, int prefix)
    {
        await using var fixture = new PublicFixture(asynchronous, text ? 19 : 12, transaction: true);
        await fixture.Open(asynchronous);
        await using var transaction = asynchronous ?
            (DmTransaction)await fixture.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted) : (DmTransaction)fixture.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var channel = fixture.Channels.Single(); channel.ClearObservations();
        var caller = new SourceFailure(prefix, asynchronous);
        object input = text ? new ThrowingText(caller) : new ThrowingBytes(caller);
        await using var command = fixture.Command(text ? DmDbType.Clob : DmDbType.Blob, input, transaction);
        IOException error = asynchronous ? await Assert.ThrowsAsync<IOException>(() => command.ExecuteNonQueryAsync()) :
            Assert.Throws<IOException>(() => command.ExecuteNonQuery());
        Assert.Same(caller.Error, error);
        Assert.True(caller.SawHistoricalSend);
        Assert.True(caller.SawUnsentWire);
        Assert.NotNull(caller.ObservedInvocation);
        Assert.True(caller.ObservedInvocation!.SendAttempted); // Do not erase acknowledged Prepare history.
        Assert.True(caller.ObservedInvocation.IsDisposed);
        Assert.Null(caller.ObservedInvocation.LocalInputFailureException);
        Assert.Null(caller.ObservedInvocation.LocalInputFailureWire);
        Assert.False(caller.ObservedInvocation.ServerErrorAccepted);
        Assert.Equal(prefix, caller.Consumed);
        Assert.Equal(prefix == 0 ? 1 : 2, caller.Reads);
        Assert.Equal(new short[] { 3, 5 }, channel.Opcodes);
        Assert.Equal(1, channel.PrepareAcknowledgements);
        Assert.Equal(0, channel.MetadataSends); Assert.Equal(0, channel.PutSends); Assert.Equal(0, channel.ExecuteSends);
        Assert.False(channel.IsClosed);
        Assert.Equal(ConnectionState.Open, fixture.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Connection.Session.State);
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        Assert.Equal(1, fixture.Source.Snapshot.Leased);
        Assert.Equal(0, caller.Disposals); Assert.Equal(0, caller.Forbidden);
        Assert.Equal(0, asynchronous ? caller.SyncReads : caller.AsyncReads);
        Assert.NotNull(command.Statement);

        object replacement = text ? new UnknownTextInput("A\ud83d\ude80", 1, asynchronous) : new UnknownInputStream([1, 2, 3], 1, asynchronous);
        command.Parameters[0].Value = replacement;
        Assert.Equal(1, asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery());
        // The acknowledged statement survived; replacing the caller input does not prepare/allocate another handle.
        Assert.Equal(1, channel.PrepareAcknowledgements);
        Assert.Equal(1, channel.MetadataSends); Assert.True(channel.PutSends > 0); Assert.Equal(1, channel.ExecuteSends);
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        if (asynchronous) await transaction.CommitAsync(); else transaction.Commit();
        Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome);
        Assert.Equal(1, channel.CommitSends);
        Assert.Equal(prefix, caller.Consumed); Assert.Equal(prefix == 0 ? 1 : 2, caller.Reads);
        Assert.Equal(0, caller.Disposals); Assert.Equal(0, caller.Forbidden);
        Assert.Equal(0, asynchronous ? channel.SyncCalls : channel.AsyncCalls);
        await fixture.Connection.CloseAsync();
        Assert.True(fixture.Source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondParameterCallerFailureAfterEarlierUploadBreaksWithoutFinalExecute(bool asynchronous)
    {
        await using var fixture = new PublicFixture(asynchronous, 12, parameterCount: 2);
        await fixture.Open(asynchronous);
        var caller = new SourceFailure(0, asynchronous);
        await using var command = fixture.Command(DmDbType.Blob, new UnknownInputStream([1, 2, 3], 1, asynchronous));
        command.CommandText = "INSERT INTO SYNTHETIC VALUES (:p,:q)";
        command.Parameters.Add(new DmParameter("q", DmDbType.Blob) { Value = new ThrowingBytes(caller) });
        var error = asynchronous ? await Assert.ThrowsAsync<IOException>(() => command.ExecuteNonQueryAsync()) : Assert.Throws<IOException>(() => command.ExecuteNonQuery());
        Assert.Same(caller.Error, error);
        var channel = fixture.Channels.Single();
        Assert.Equal(1, channel.MetadataSends); Assert.Equal(1, channel.PutSends); Assert.Equal(1, channel.PutAcknowledgements);
        Assert.Equal(0, channel.ExecuteSends);
        Assert.False(caller.SawUnsentWire);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.True(channel.IsClosed); Assert.Equal(0, caller.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerFailureAfterOneAcknowledgedChunkRemainsBroken(bool asynchronous)
    {
        await using var fixture = new PublicFixture(asynchronous, 12);
        await fixture.Open(asynchronous);
        var caller = new SourceFailure(8, asynchronous);
        await using var command = fixture.Command(DmDbType.Blob, new ThrowingBytes(caller));
        var error = asynchronous ? await Assert.ThrowsAsync<IOException>(() => command.ExecuteNonQueryAsync()) : Assert.Throws<IOException>(() => command.ExecuteNonQuery());
        Assert.Same(caller.Error, error);
        var channel = fixture.Channels.Single();
        Assert.Equal(1, channel.MetadataSends); Assert.Equal(1, channel.PutSends); Assert.Equal(1, channel.PutAcknowledgements);
        Assert.Equal(0, channel.ExecuteSends); Assert.Equal(8, caller.Consumed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.True(channel.IsClosed); Assert.Equal(0, caller.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidCallerCountIsNotAcceptedAsExactReadExceptionDespiteZeroUploadSends(bool asynchronous)
    {
        await using var fixture = new PublicFixture(asynchronous, 12);
        await fixture.Open(asynchronous);
        await using var command = fixture.Command(DmDbType.Blob, new InvalidCountInput(asynchronous));
        if (asynchronous) await Assert.ThrowsAsync<IOException>(() => command.ExecuteNonQueryAsync());
        else Assert.Throws<IOException>(() => command.ExecuteNonQuery());
        var channel = fixture.Channels.Single();
        Assert.Equal(new short[] { 3, 5 }, channel.Opcodes);
        Assert.Equal(0, channel.MetadataSends); Assert.Equal(0, channel.PutSends);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.True(channel.IsClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictEncoderFailureIsNotARecoverableCallerReadReceipt(bool asynchronous)
    {
        await using var fixture = new PublicFixture(asynchronous, 19);
        await fixture.Open(asynchronous);
        await using var command = fixture.Command(DmDbType.Clob, new UnknownTextInput("\ud83d", 1, asynchronous));
        if (asynchronous) await Assert.ThrowsAsync<EncoderFallbackException>(() => command.ExecuteNonQueryAsync());
        else Assert.Throws<EncoderFallbackException>(() => command.ExecuteNonQuery());
        var channel = fixture.Channels.Single();
        Assert.Equal(new short[] { 3, 5 }, channel.Opcodes);
        Assert.True(channel.IsClosed); Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("command")]
    [InlineData("deadline")]
    public async Task ExistingTerminalCauseWinsCallerReadFailureAfterPrepare(string cause)
    {
        await using var fixture = new PublicFixture(true, 12);
        await fixture.Open(true);
        using var token = new CancellationTokenSource();
        var caller = new SourceFailure(0, true);
        await using var command = fixture.Command(DmDbType.Blob, new ThrowingBytes(caller));
        caller.BeforeFailure = () =>
        {
            if (cause == "user") token.Cancel();
            else if (cause == "command") command.Cancel();
            else fixture.Connection.Session.TerminateInvocation(DmInvocation.Current!, DmCancelSource.TotalDeadline);
        };
        Exception error = (await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(token.Token)))!;
        if (cause == "deadline") Assert.Equal(DmCancelSource.TotalDeadline, Assert.IsType<DmTimeoutException>(error).FailureInfo.CancelSource);
        else
        {
            var canceled = Assert.IsType<DmOperationCanceledException>(error);
            Assert.Equal(cause == "user" ? DmCancelSource.User : DmCancelSource.Command, canceled.FailureInfo.CancelSource);
            if (cause == "user") Assert.Equal(token.Token, canceled.CancellationToken);
        }
        Assert.Equal(new short[] { 3, 5 }, fixture.Channels.Single().Opcodes);
        Assert.True(fixture.Channels.Single().IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.Equal(0, caller.Disposals);
    }

    [Fact]
    public async Task LateFirstReadFailureAfterCloseReopenCannotPreserveOrBreakReplacementSession()
    {
        await using var fixture = new PublicFixture(true, 12);
        await fixture.Open(true);
        var caller = new SourceFailure(0, true);
        var input = new DelayedFailureInput(caller);
        await using var oldCommand = fixture.Command(DmDbType.Blob, input);
        DmSession oldSession = fixture.Connection.Session;
        Task<int> pending = oldCommand.ExecuteNonQueryAsync();
        try
        {
            await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new short[] { 3, 5 }, fixture.Channels[0].Opcodes);
            await fixture.Connection.CloseAsync();
            await fixture.Connection.OpenAsync();
            DmSession replacement = fixture.Connection.Session;
            Assert.NotSame(oldSession, replacement);
            await using var fresh = fixture.Command(DmDbType.Blob, new UnknownInputStream([4, 5, 6], 1, true));
            Assert.Equal(1, await fresh.ExecuteNonQueryAsync());
            int sends = fixture.Channels[1].Opcodes.Count;
            input.Release.TrySetResult();
            Assert.Same(caller.Error, await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.Equal(sends, fixture.Channels[1].Opcodes.Count);
            Assert.Same(replacement, fixture.Connection.Session);
            Assert.Equal(DmPhysicalSessionState.Ready, replacement.State);
            Assert.False(fixture.Channels[1].IsClosed); Assert.Equal(0, caller.Disposals);
            await oldCommand.DisposeAsync();
            Assert.False(fixture.Channels[1].IsClosed);
            await fixture.Connection.CloseAsync();
            Assert.True(fixture.Source.Snapshot.IsQuiescent);
        }
        finally
        {
            input.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private sealed class SourceFailure(int prefix, bool asyncOnly)
    {
        internal readonly IOException Error = new("Synthetic caller read failure.");
        internal int Reads, Consumed, SyncReads, AsyncReads, Disposals, Forbidden;
        internal bool SawHistoricalSend, SawUnsentWire;
        internal DmInvocation? ObservedInvocation;
        internal Action? BeforeFailure;
        internal int Next(int capacity, bool asynchronous)
        {
            if (asynchronous) AsyncReads++; else SyncReads++;
            if (asyncOnly && !asynchronous) throw new InvalidOperationException("Sync caller read forbidden.");
            Reads++;
            if (Consumed < prefix) { int count = Math.Min(prefix - Consumed, capacity); Consumed += count; return count; }
            SawHistoricalSend = DmInvocation.Current?.SendAttempted == true;
            SawUnsentWire = DmWireExchange.Current?.SendAttempted == false;
            ObservedInvocation = DmInvocation.Current;
            BeforeFailure?.Invoke();
            throw Error;
        }
        internal Exception InvalidAccess() { Forbidden++; return new InvalidOperationException("Caller input positioning forbidden."); }
    }

    private sealed class FailureObserver : IDisposable
    {
        private readonly ActivityListener activity;
        private readonly MeterListener meter = new();
        private readonly object gate = new();
        private readonly List<(string Result, ActivityStatusCode Status)> results = [];
        private readonly Dictionary<string, long> cumulative = new(StringComparer.Ordinal);
        internal (string Result, ActivityStatusCode Status)[] Results { get { lock (gate) return results.ToArray(); } }
        internal FailureObserver(ActivityTraceId trace, ActivitySpanId parent)
        {
            activity = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = value =>
                {
                    if (value.OperationName != "wdm.execute" || value.TraceId != trace || value.ParentSpanId != parent) return;
                    lock (gate) results.Add((value.GetTagItem("result") as string ?? "unknown", value.Status));
                }
            };
            ActivitySource.AddActivityListener(activity);
            meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DmDiagnostics.MeterName && instrument.Name == "wdm.operation.total") listener.EnableMeasurementEvents(instrument);
            };
            meter.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? operation = null, result = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "operation") operation = tag.Value as string;
                    if (tag.Key == "result") result = tag.Value as string;
                }
                if (operation == "execute" && result != null) lock (gate) cumulative[result] = value;
            });
            meter.Start();
        }
        internal long Counter(string result)
        {
            meter.RecordObservableInstruments();
            lock (gate) return cumulative.GetValueOrDefault(result);
        }
        public void Dispose() { meter.Dispose(); activity.Dispose(); }
    }

    private class ThrowingBytes(SourceFailure state) : Stream
    {
        protected readonly SourceFailure State = state;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => throw State.InvalidAccess();
        public override long Length => throw State.InvalidAccess();
        public override long Position { get => throw State.InvalidAccess(); set => throw State.InvalidAccess(); }
        public override int Read(byte[] buffer, int offset, int count) { int read = State.Next(count, false); buffer.AsSpan(offset, read).Fill(7); return read; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); int read = State.Next(buffer.Length, true); buffer.Span[..read].Fill(7); return ValueTask.FromResult(read); }
        protected override void Dispose(bool disposing) { State.Disposals++; }
        public override long Seek(long offset, SeekOrigin origin) => throw State.InvalidAccess();
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingText(SourceFailure state) : TextReader
    {
        public override int Read(char[] buffer, int offset, int count) { int read = state.Next(count, false); buffer.AsSpan(offset, read).Fill('x'); return read; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); int read = state.Next(buffer.Length, true); buffer.Span[..read].Fill('x'); return ValueTask.FromResult(read); }
        public override string ReadToEnd() => throw state.InvalidAccess();
        protected override void Dispose(bool disposing) { state.Disposals++; }
    }

    private sealed class InvalidCountInput(bool asynchronous) : ThrowingBytes(new SourceFailure(0, asynchronous))
    {
        public override int Read(byte[] buffer, int offset, int count) => -1;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => ValueTask.FromResult(-1);
    }

    private sealed class DelayedFailureInput(SourceFailure state) : ThrowingBytes(state)
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            Entered.TrySetResult(); await Release.Task.ConfigureAwait(false);
            return State.Next(buffer.Length, true);
        }
    }

    private sealed class PublicFixture : IAsyncDisposable
    {
        internal readonly DmDataSource Source;
        internal readonly DmConnection Connection;
        internal readonly List<GoldenChannel> Channels = [];
        internal PublicFixture(bool asynchronous, int type, int parameterCount = 1, bool transaction = false)
        {
            Source = new DmDataSource(new DmConnectionStringBuilder
            {
                Server = "127.0.0.1", User = "R3_INPUT_SYNTH", Password = "synthetic_only", Pooling = true, MaxPoolSize = 1,
                MaxPoolWaiters = 4, CommandTimeout = 10, StmtPooling = false, PreparePooling = false,
                TransportSecurity = DmTransportSecurity.PlaintextAllowed
            }.ConnectionString);
            Connection = Source.CreateConnection();
            DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
            {
                var channel = new GoldenChannel(asynchronous, type, parameterCount, transaction); Channels.Add(channel);
                var instance = new DmConnInstance(candidate); candidate.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60"; instance.ConnProperty.ServerEncoding = "UTF-8"; instance.ConnProperty.msgVersion = 21;
                if (transaction) instance.ConnProperty.IsolationLevel = IsolationLevel.Serializable; // Force a real public SET receipt for ReadCommitted.
                instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = 8;
                var protocol = instance.GetCsi(); object wire = protocol.A();
                ((DmTransport)Get(wire, "transport")!).Dispose(); Set(wire, "transport", new DmTransport(channel));
                Set(wire, "__t02_field_04000AAD", false); Set(protocol, "__t02_field_04000ABD", false);
                candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
                return ValueTask.CompletedTask;
            };
        }
        internal async Task Open(bool asynchronous) { if (asynchronous) await Connection.OpenAsync(); else Connection.Open(); }
        internal DmCommand Command(DmDbType type, object value, DmTransaction? transaction = null)
        {
            var command = new DmCommand("INSERT INTO SYNTHETIC VALUES (:p)", Connection) { Transaction = transaction };
            command.Parameters.Add(new DmParameter("p", type) { Value = value }); return command;
        }
        public async ValueTask DisposeAsync()
        {
            try { await Connection.DisposeAsync(); await Source.DisposeAsync(); }
            finally { DmPendingOpenTestHooks.Handshake = null; DmLobInputTestHooks.AfterChunk = null; }
        }
        private static object? Get(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
        private static void Set(object value, string name, object? field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
    }

    // Independent fixed allocation/prepare/SET TRANSACTION/update/control layouts.
    // Expected reply bytes never invoke the production encoder or a live database.
    private sealed class GoldenChannel(bool asyncOnly, int type, int parameterCount, bool transaction) : IDmByteChannel
    {
        private byte[] response = [];
        private int offset;
        private short request;
        private bool isolationPending = transaction;
        internal readonly List<short> Opcodes = [];
        internal int MetadataSends, PutSends, PutAcknowledgements, ExecuteSends, PrepareAcknowledgements, CommitSends, SyncCalls, AsyncCalls;
        // When set, the PUT ACK reply declares a body beyond the upload ACK budget.
        internal bool OversizedAck;
        public bool IsClosed { get; private set; }
        internal void ClearObservations() { Opcodes.Clear(); MetadataSends = PutSends = PutAcknowledgements = ExecuteSends = PrepareAcknowledgements = CommitSends = SyncCalls = AsyncCalls = 0; }
        private int SendCore(byte[] buffer, int start, int count)
        {
            if (IsClosed) throw new IOException("Retired synthetic transport.");
            request = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(start + 4)); Opcodes.Add(request);
            switch (request)
            {
                case 3: response = Reply(3, []); break;
                case 5 when isolationPending:
                    isolationPending = false; response = Reply(0, [1, 0, 0]); BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(20), 150); Fix(response); break;
                case 5: response = Prepare(); break;
                case 90: MetadataSends++; response = Reply(90, []); break;
                case 26: PutSends++; response = OversizedAck ? OversizedAckFrame() : Reply(261, Convert.FromHexString("020102030405060708090A0B0C0D0E0F1011121314")); break;
                case 6:
                case 13: ExecuteSends++; response = Reply(request, []); BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(20), 158); BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(24), 1); Fix(response); break;
                case 8: CommitSends++; response = Reply(0, []); break;
                case 9: response = Reply(0, []); break;
                case 4: response = Reply(4, []); break;
                case 44: response = Reply(0, [], 111); break;
                default: throw new InvalidOperationException("Unexpected synthetic opcode.");
            }
            offset = 0; return count;
        }
        public int Send(byte[] buffer, int start, int count, int timeout)
        { SyncCalls++; if (asyncOnly) throw new InvalidOperationException("Sync network forbidden."); return SendCore(buffer, start, count); }
        public int Receive(byte[] buffer, int start, int count, int timeout)
        { SyncCalls++; if (asyncOnly) throw new InvalidOperationException("Sync network forbidden."); return ReceiveCore(buffer, start, count); }
        public ValueTask<int> SendAsync(byte[] buffer, int start, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, start, count)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int start, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReceiveCore(buffer, start, count)); }
        private int ReceiveCore(byte[] buffer, int start, int count)
        {
            int read = Math.Min(count, response.Length - offset); response.AsSpan(offset, read).CopyTo(buffer.AsSpan(start, read)); offset += read;
            if (offset == response.Length && request == 26) PutAcknowledgements++;
            if (offset == response.Length && request == 5 && BinaryPrimitives.ReadInt16LittleEndian(response.AsSpan(22)) == parameterCount) PrepareAcknowledgements++;
            return read;
        }
        private byte[] Prepare()
        {
            byte[] body = new byte[33 * parameterCount];
            for (int index = 0; index < parameterCount; index++)
            {
                Span<byte> entry = body.AsSpan(index * 33, 33);
                BinaryPrimitives.WriteInt32LittleEndian(entry, type); BinaryPrimitives.WriteInt32LittleEndian(entry[12..], 1);
                BinaryPrimitives.WriteInt16LittleEndian(entry[24..], 1); entry[32] = (byte)(index == 0 ? 'p' : 'q');
            }
            byte[] frame = Reply(5, body); BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), (short)parameterCount); Fix(frame); return frame;
        }
        private static byte[] Reply(short opcode, byte[] body, int status = 0)
        {
            byte[] frame = new byte[64 + body.Length]; BinaryPrimitives.WriteInt32LittleEndian(frame, 41);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), status); body.CopyTo(frame, 64); Fix(frame); return frame;
        }
        // A header whose declared body exceeds the ACK budget; the reader must
        // reject at header validation before allocating or reading the body.
        private static byte[] OversizedAckFrame()
        {
            byte[] frame = new byte[64]; BinaryPrimitives.WriteInt32LittleEndian(frame, 41);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), 261);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), 5000); Fix(frame); return frame;
        }
        private static void Fix(byte[] frame) { frame[19] = 0; for (int index = 0; index < 19; index++) frame[19] ^= frame[index]; }
        public void Dispose() => IsClosed = true;
    }
}
