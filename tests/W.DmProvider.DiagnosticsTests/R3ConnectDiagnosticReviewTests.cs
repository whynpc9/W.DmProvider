using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using W.Dm;
using W.Dm.Config;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.DiagnosticsTests;

[Collection("Diagnostics serial")]
[Trait("Category", "Contract")]
[Trait("Feature", "Diagnostics")]
public sealed class R3ConnectDiagnosticReviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublicRealStartupLoginSchemaAndCoordinatorProduceOneConnectRecord(bool asynchronous, bool schema)
    {
        await using var server = new HandshakeServer();
        await using var connection = Connection(server.Port, schema);
        using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
        long before = observer.Total();
        if (asynchronous) await connection.OpenAsync(); else connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal("8.1.5.60", connection.ServerVersion);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("success", Assert.Single(observer.Results));
        Assert.Equal(before + 1, observer.Total());
        Assert.Equal(new short[] { 200, 1 }, server.FirstRequests.Take(2));
        if (schema)
        {
            Assert.Contains((short)3, server.FirstRequests);
            Assert.Contains(server.FirstRequests, value => value is 5 or 91);
            Assert.Contains((short)4, server.FirstRequests);
        }
        if (asynchronous) await connection.CloseAsync(); else connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(false, "auth_eof")]
    [InlineData(true, "auth_eof")]
    [InlineData(false, "schema_eof")]
    [InlineData(true, "schema_eof")]
    [InlineData(false, "schema_server_error")]
    [InlineData(true, "schema_server_error")]
    public async Task AuthOrSchemaFailureNeverLeavesAnExtraSuccessfulConnect(bool asynchronous, string failure)
    {
        await using var server = new HandshakeServer(failure);
        await using var connection = Connection(server.Port, failure.StartsWith("schema", StringComparison.Ordinal));
        using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
        long before = observer.Total();
        Exception? error = asynchronous ? await Record.ExceptionAsync(() => connection.OpenAsync()) : Record.Exception(() => connection.Open());
        Assert.NotNull(error);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        string result = Assert.Single(observer.Results);
        Assert.NotEqual("success", result);
        Assert.Equal(error is DmException { ErrorKind: DmErrorKind.Server } ? "server_error" : "transport_error", result);
        Assert.Equal(before + 1, observer.Total());
    }

    [Theory]
    [InlineData("auth_block")]
    [InlineData("schema_block")]
    public async Task CancellationAfterAuthenticationOrDuringAuthenticationHasOneOriginalTokenRecord(string failure)
    {
        await using var server = new HandshakeServer(failure);
        await using var connection = Connection(server.Port, failure == "schema_block");
        using var cancellation = new CancellationTokenSource();
        using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
        long before = observer.Total();
        Task opening = connection.OpenAsync(cancellation.Token);
        await server.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => opening.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("canceled", Assert.Single(observer.Results));
        Assert.Equal(before + 1, observer.Total());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task SchemaDeadlineAfterSuccessfulAuthenticationIsOneTimeoutRatherThanSuccess()
    {
        await using var server = new HandshakeServer("schema_block");
        await using var connection = Connection(server.Port, true);
        var clock = new ControlledClock(); connection.OperationClock = clock;
        using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
        long before = observer.Total();
        Task opening = connection.OpenAsync();
        await server.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(5001);
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => opening.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("timeout", Assert.Single(observer.Results));
        Assert.Equal(before + 1, observer.Total());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingOpenNotificationIsOneRejectedConnectWithCleanup(bool asynchronous)
    {
        await using var server = new HandshakeServer();
        await using var connection = Connection(server.Port, true);
        var marker = new InvalidOperationException("Synthetic open notification failure.");
        connection.StateChange += (_, args) => { if (args.CurrentState == ConnectionState.Open) throw marker; };
        using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
        long before = observer.Total();
        Exception? error = asynchronous ? await Record.ExceptionAsync(() => connection.OpenAsync()) : Record.Exception(() => connection.Open());
        Assert.Same(marker, error);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("rejected", Assert.Single(observer.Results));
        Assert.Equal(before + 1, observer.Total());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomConnectorWithoutAnInnerHandshakeReceiptStillReportsOneConnect(bool asynchronous)
    {
        using var connection = Connection(1, false);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            candidate.m_ConnInst = new DmConnInstance(candidate);
            candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
            return ValueTask.CompletedTask;
        };
        try
        {
            using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
            long before = observer.Total();
            if (asynchronous) await connection.OpenAsync(); else connection.Open();
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("success", Assert.Single(observer.Results));
            Assert.Equal(before + 1, observer.Total());
        }
        finally { DmPendingOpenTestHooks.Handshake = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomConnectorRejectionWithoutSendingStillReportsOneFailure(bool asynchronous)
    {
        using var connection = Connection(1, false);
        var marker = new InvalidOperationException("Synthetic connector rejection.");
        DmPendingOpenTestHooks.Handshake = (_, _, _) => throw marker;
        try
        {
            using var activity = Caller(); using var observer = new ConnectObserver(activity.TraceId, activity.SpanId);
            long before = observer.Total();
            Exception? error = asynchronous ? await Record.ExceptionAsync(() => connection.OpenAsync()) : Record.Exception(() => connection.Open());
            Assert.Same(marker, error);
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("rejected", Assert.Single(observer.Results));
            Assert.Equal(before + 1, observer.Total());
            Assert.Equal(ConnectionState.Closed, connection.State);
        }
        finally { DmPendingOpenTestHooks.Handshake = null; }
    }

    [Fact]
    public async Task FailedClosedOpeningDoesNotSuppressOrCloseTheReplacementOpening()
    {
        await using var server = new HandshakeServer("auth_block_first");
        await using var connection = Connection(server.Port, true);
        using var oldActivity = Caller(); using var oldObserver = new ConnectObserver(oldActivity.TraceId, oldActivity.SpanId);
        long before = oldObserver.Total();
        Task old = connection.OpenAsync();
        await server.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await connection.CloseAsync();
        using (var nextActivity = Caller())
        using (var nextObserver = new ConnectObserver(nextActivity.TraceId, nextActivity.SpanId))
        {
            await connection.OpenAsync();
            DmSession replacement = connection.Session;
            Assert.NotNull(await Record.ExceptionAsync(() => old.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("success", Assert.Single(nextObserver.Results));
            Assert.NotEqual("success", Assert.Single(oldObserver.Results));
            Assert.Equal(before + 2, oldObserver.Total());
            Assert.Same(replacement, connection.Session);
            Assert.Equal(ConnectionState.Open, connection.State);
        }
    }

    private static Activity Caller() => new Activity("synthetic.connect.caller").SetIdFormat(ActivityIdFormat.W3C).Start();
    private static DmConnection Connection(int port, bool schema) => new(new DmConnectionStringBuilder
    {
        Server = "127.0.0.1", Port = port, User = "CONNECT_SYNTH", Password = "synthetic_only", Schema = schema ? "CONNECT_SYNTH" : "",
        TransportSecurity = DmTransportSecurity.PlaintextAllowed, ConnectTimeout = TimeSpan.FromSeconds(5), CommandTimeout = 5,
        LogLevel = LogLevel.OFF, Pooling = false, StmtPooling = false, PreparePooling = false
    }.ConnectionString);

    private sealed class ControlledClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ControlledTimer> timers = [];
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { lock (gate) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state);
            lock (gate) timers.Add(timer);
            timer.Change(dueTime, period); return timer;
        }
        internal void Advance(long milliseconds)
        {
            List<ControlledTimer> due;
            lock (gate)
            {
                now += milliseconds;
                due = timers.Where(timer => !timer.Closed && timer.Due <= now).ToList();
                foreach (var timer in due) timer.Due = long.MaxValue;
            }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ControlledTimer(ControlledClock owner, TimerCallback callback, object? state) : ITimer
        {
            internal bool Closed;
            internal long Due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (Closed) throw new ObjectDisposedException(nameof(ControlledTimer));
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.now + (long)Math.Ceiling(dueTime.TotalMilliseconds);
                    return true;
                }
            }
            internal void Fire() { lock (owner.gate) { if (Closed) return; } callback(state); }
            public void Dispose() { lock (owner.gate) Closed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class ConnectObserver : IDisposable
    {
        private readonly ActivityListener activity;
        private readonly MeterListener meter = new();
        private readonly object gate = new();
        private readonly List<string> results = [];
        private readonly Dictionary<string, long> latest = new(StringComparer.Ordinal);
        internal string[] Results { get { lock (gate) return results.ToArray(); } }
        internal ConnectObserver(ActivityTraceId trace, ActivitySpanId parent)
        {
            activity = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = value =>
                {
                    if (value.OperationName == "wdm.connect" && value.TraceId == trace && value.ParentSpanId == parent)
                        lock (gate) results.Add(value.GetTagItem("result") as string ?? "unknown");
                }
            };
            ActivitySource.AddActivityListener(activity);
            meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DmDiagnostics.MeterName && instrument.Name == "wdm.operation.total") listener.EnableMeasurementEvents(instrument);
            };
            meter.SetMeasurementEventCallback<long>((_, count, tags, _) =>
            {
                string? operation = null, result = null;
                foreach (var tag in tags) { if (tag.Key == "operation") operation = tag.Value as string; if (tag.Key == "result") result = tag.Value as string; }
                if (operation == "connect" && result != null) lock (gate) latest[result] = count;
            });
            meter.Start();
        }
        internal long Total() { meter.RecordObservableInstruments(); lock (gate) return latest.Values.Sum(); }
        public void Dispose() { meter.Dispose(); activity.Dispose(); }
    }

    // Real TCP/production Startup+Login parsing with small independent synthetic
    // golden replies. Incoming LOGIN bodies are discarded and never persisted.
    private sealed class HandshakeServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource lifetime = new();
        private readonly List<TcpClient> clients = [];
        private readonly List<Task> sessions = [];
        private readonly object gate = new();
        private readonly string failure;
        private readonly Task accept;
        private readonly List<short> firstRequests = [];
        internal readonly TaskCompletionSource Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Port { get; }
        internal short[] FirstRequests { get { lock (gate) return firstRequests.ToArray(); } }
        internal HandshakeServer(string failure = "")
        {
            this.failure = failure; listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; accept = Accept();
        }
        private async Task Accept()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(lifetime.Token);
                    lock (gate) { int ordinal = clients.Count; clients.Add(client); sessions.Add(Serve(client, ordinal)); }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (lifetime.IsCancellationRequested) { }
        }
        private async Task Serve(TcpClient client, int ordinal)
        {
            try
            {
                using NetworkStream stream = client.GetStream();
                byte[] header = new byte[64], discard = new byte[4096];
                while (!lifetime.IsCancellationRequested)
                {
                    int first = await stream.ReadAsync(header.AsMemory(0, 1), lifetime.Token);
                    if (first == 0) return;
                    await stream.ReadExactlyAsync(header.AsMemory(1), lifetime.Token);
                    short opcode = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(4));
                    int body = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6));
                    if (body < 0 || body > 1048576) throw new InvalidDataException("Synthetic client frame boundary.");
                    while (body > 0) { int count = Math.Min(body, discard.Length); await stream.ReadExactlyAsync(discard.AsMemory(0, count), lifetime.Token); Array.Clear(discard); body -= count; }
                    if (ordinal == 0) lock (gate) firstRequests.Add(opcode);
                    bool schema = opcode is 5 or 91;
                    if (opcode == 1 && failure == "auth_eof" || schema && failure == "schema_eof") return;
                    if (opcode == 1 && (failure == "auth_block" || failure == "auth_block_first" && ordinal == 0) || schema && failure == "schema_block")
                    { Blocked.TrySetResult(); await release.Task.WaitAsync(lifetime.Token); }
                    byte[] reply = opcode switch
                    {
                        200 => Startup(), 1 => Login(), 3 => Reply(3, []), 4 => Reply(4, []),
                        // Async schema nonquery advances to the independently
                        // characterized terminal NO_MORE_RESULTS response.
                        44 => Reply(0, [], 111),
                        5 or 91 when failure == "schema_server_error" => Reply(opcode, new byte[16], -6602),
                        5 or 91 => Update(opcode), _ => throw new InvalidDataException($"Unexpected synthetic handshake request opcode {opcode}.")
                    };
                    await stream.WriteAsync(reply, lifetime.Token);
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        private static byte[] Startup()
        {
            byte[] version = Encoding.ASCII.GetBytes("8.1.5.60"); byte[] body = new byte[20 + version.Length];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16), version.Length); version.CopyTo(body, 20);
            byte[] frame = Reply(200, body); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28), 1);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(48), 8); Fix(frame); return frame;
        }
        private static byte[] Login()
        {
            byte[] frame = Reply(1, new byte[52]); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29), 1);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(35), 1); BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(37), 4); Fix(frame); return frame;
        }
        private static byte[] Update(short opcode)
        {
            byte[] frame = Reply(opcode, []); BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 158);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1); Fix(frame); return frame;
        }
        private static byte[] Reply(short opcode, byte[] body, int status = 0)
        {
            byte[] frame = new byte[64 + body.Length]; BinaryPrimitives.WriteInt32LittleEndian(frame, 41);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), status); body.CopyTo(frame, 64); Fix(frame); return frame;
        }
        private static void Fix(byte[] frame) { frame[19] = 0; for (int i = 0; i < 19; i++) frame[19] ^= frame[i]; }
        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel(); release.TrySetResult(); listener.Stop();
            lock (gate) foreach (TcpClient client in clients) client.Dispose();
            await accept;
            Task[] pending; lock (gate) pending = sessions.ToArray();
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); lifetime.Dispose();
        }
    }
}
