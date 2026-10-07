using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.DiagnosticsTests;

[Collection("Diagnostics serial")]
[Trait("Category", "Contract")]
[Trait("Feature", "Diagnostics")]
public sealed class R3InternalPagedReadDiagnosticTests
{
    [Fact]
    public async Task OwnInternalReadCompletesRealPagedFetchAndCachedRowsAndEndAddNoDiagnostic()
    {
        await using var fixture = new PagedFixture();
        await fixture.Open();
        var reader = fixture.OpenReader();
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        Assert.True(reader.do_Read());
        Assert.Equal(17, reader.do_GetInt32(0));
        Assert.Equal(new short[] { 3, 5 }, fixture.Channel.Opcodes);
        await observer.AssertResults();

        Assert.True(reader.do_Read());
        Assert.Equal(18, reader.do_GetInt32(0));
        var fetch = Assert.Single(fixture.Channel.FetchInvocations);
        Assert.True(fetch.SendAttempted); Assert.True(fetch.Completed); Assert.True(fetch.IsDisposed);
        Assert.Equal(1, Assert.Single(fixture.Channel.FetchStarts));
        await observer.AssertResults(("fetch", "success", 1));

        Assert.True(reader.do_Read());
        Assert.Equal(19, reader.do_GetInt32(0));
        Assert.False(reader.do_Read());
        Assert.False(reader.do_Read());
        Assert.Equal(new short[] { 3, 5, 7 }, fixture.Channel.Opcodes);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Connection.Session.State);
        await observer.AssertResults(("fetch", "success", 1));
        Assert.Equal(0, fixture.Channel.AsyncCalls);
    }

    [Fact]
    public async Task OwnInternalReadCompletesAcknowledgedEmptyPageWithoutInventingAnotherEndFetch()
    {
        await using var fixture = new PagedFixture();
        await fixture.Open();
        var reader = fixture.OpenReader();
        Assert.True(reader.do_Read());
        fixture.Channel.EmptyPage = true;
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        Assert.False(reader.do_Read());
        Assert.True(Assert.Single(fixture.Channel.FetchInvocations).Completed);
        Assert.False(reader.do_Read());
        Assert.Equal(new short[] { 3, 5, 7 }, fixture.Channel.Opcodes);
        await observer.AssertResults(("fetch", "success", 1));
    }

    [Fact]
    public async Task PublicGetSchemaTablesPreservesMetadataPurposeAcrossRealPagedReadAndCleanup()
    {
        await using var fixture = new PagedFixture(metadata: true);
        await fixture.Open();
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        DataTable table = fixture.Connection.GetSchema("Tables");
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("SYNTHETIC_SCHEMA", table.Rows[0]["TABLE_SCHEMA"]);
        Assert.Equal("FIRST_TABLE", table.Rows[0]["TABLE_NAME"]);
        Assert.Equal("UTAB", table.Rows[0]["TABLE_TYPE"]);
        Assert.Equal("SECOND_VIEW", table.Rows[1]["TABLE_NAME"]);
        Assert.Equal("VIEW", table.Rows[1]["TABLE_TYPE"]);
        var fetch = Assert.Single(fixture.Channel.FetchInvocations);
        Assert.Equal(DmOperationPurpose.Metadata, fetch.Lease.Purpose);
        Assert.True(fetch.Completed); Assert.True(fetch.IsDisposed);
        Assert.Equal(new short[] { 3, 91, 7, 4 }, fixture.Channel.Opcodes);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Connection.Session.State);
        await observer.AssertResults(("metadata", "success", 3));
        Assert.Equal(0, fixture.Channel.AsyncCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorrowedAmbientInvocationRemainsUncompletedUntilItsOwnerCompletes(bool metadata)
    {
        await using var fixture = new PagedFixture(metadata);
        await fixture.Open();
        using var lease = metadata ? fixture.Connection.Session.BeginExecution(DmOperationPurpose.Metadata) : null;
        var reader = fixture.OpenReader(lease);
        var readerLease = lease ?? (DmExecutionLease)Field(reader, "executionLease")!;
        Assert.True(reader.do_Read());
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        using (var outer = readerLease.BeginInvocation())
        {
            Assert.True(reader.do_Read());
            Assert.Same(outer, Assert.Single(fixture.Channel.FetchInvocations));
            Assert.True(outer.SendAttempted); Assert.False(outer.Completed); Assert.False(outer.IsDisposed);
            await observer.AssertResults();
            // The outer owner may still perform work after the nested read.
            if (metadata) Assert.Equal("SECOND_VIEW", reader.do_GetString(1));
            else Assert.Equal(18, reader.do_GetInt32(0));
            Assert.False(outer.Completed);
            outer.ThrowIfTerminated(); outer.Complete();
        }
        await observer.AssertResults((metadata ? "metadata" : "fetch", "success", 1));
    }

    [Theory]
    [InlineData("server")]
    [InlineData("truncated")]
    [InlineData("io")]
    public async Task FailedRealPagedReadNeverCompletesOrReportsSuccessAndPreservesItsFailure(string failure)
    {
        await using var fixture = new PagedFixture();
        await fixture.Open();
        var reader = fixture.OpenReader();
        Assert.True(reader.do_Read());
        fixture.Channel.FetchFailure = failure;
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        Exception? error = Record.Exception(() => reader.do_Read());
        if (failure == "server") Assert.Equal(-2106, Assert.IsType<DmException>(error).Number);
        else if (failure == "truncated") Assert.IsType<EndOfStreamException>(error);
        else Assert.Same(fixture.Channel.ReceiveFailure, error);
        var fetch = Assert.Single(fixture.Channel.FetchInvocations);
        Assert.True(fetch.SendAttempted); Assert.False(fetch.Completed); Assert.True(fetch.IsDisposed);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.Equal(new short[] { 3, 5, 7 }, fixture.Channel.Opcodes);
        // The legacy FETCH server-error path has no accepted server receipt.
        // Keep its existing transport_error classification; this fix only
        // completes a successful owned internal invocation.
        await observer.AssertResults(("fetch", "transport_error", 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminationAfterWholeFetchAckBeforeCompletionWinsOverSuccess(bool deadline)
    {
        await using var fixture = new PagedFixture();
        await fixture.Open();
        var reader = fixture.OpenReader();
        Assert.True(reader.do_Read());
        fixture.Channel.AfterFetchAck = invocation =>
        {
            Assert.True(invocation.SendAttempted); Assert.False(invocation.Completed);
            if (deadline) invocation.Lease.Session.TerminateInvocation(invocation, DmCancelSource.TotalDeadline);
            else fixture.CancelCommand();
        };
        using var caller = Caller(); using var observer = new PagedObserver(caller);
        await observer.Baseline();

        Exception? error = Record.Exception(() => reader.do_Read());
        DmFailureInfo info = deadline ? Assert.IsType<DmTimeoutException>(error).FailureInfo :
            Assert.IsType<DmOperationCanceledException>(error).FailureInfo;
        Assert.Equal(deadline ? DmCancelSource.TotalDeadline : DmCancelSource.Command, info.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
        Assert.False(info.ConnectionReusable);
        var fetch = Assert.Single(fixture.Channel.FetchInvocations);
        Assert.Equal(1, fixture.Channel.FetchAcksDelivered);
        Assert.False(fetch.Completed); Assert.True(fetch.IsDisposed);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Connection.Session.State);
        Assert.Equal(new short[] { 3, 5, 7 }, fixture.Channel.Opcodes);
        // The complete success response reached the real receive path, but
        // termination won before the wire/reader owner could Complete.
        await observer.AssertResults(("fetch", deadline ? "timeout" : "canceled", 1));
    }

    private static Activity Caller() => new Activity("synthetic.internal.paged.read").SetIdFormat(ActivityIdFormat.W3C).Start();
    private static object? Field(object value, string name) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);

    // Real public command/GetSchema and FETCH codecs run unchanged. Only the
    // synthetic STARTUP/LOGIN hook and the byte channel replace a live server.
    private sealed class PagedFixture : IAsyncDisposable
    {
        internal readonly PagedChannel Channel;
        private readonly DmDataSource source;
        internal DmConnection Connection = null!;
        private DmCommand? command;
        private DmDataReader? reader;
        internal PagedFixture(bool metadata = false)
        {
            Channel = new PagedChannel(metadata);
            source = new DmDataSource(new DmConnectionStringBuilder
            {
                Server = "127.0.0.1", User = "PAGED_SYNTH", Password = "synthetic_only", Pooling = false,
                TransportSecurity = DmTransportSecurity.PlaintextAllowed, CommandTimeout = 5
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
        internal async Task Open()
        {
            Connection = source.CreateConnection(); Connection.Open();
            Assert.Equal(ConnectionState.Open, Connection.State);
            Assert.Empty(Channel.Opcodes);
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        }
        internal DmDataReader OpenReader(DmExecutionLease? lease = null)
        {
            command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", Connection);
            reader = lease == null ? (DmDataReader)command.ExecuteReader() : command.ExecuteInternalReader(lease, CommandBehavior.Default);
            return reader;
        }
        internal void CancelCommand() => command!.Cancel();
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Connection != null) await Connection.CloseAsync();
                if (reader != null) await reader.DisposeAsync();
                if (command != null) await command.DisposeAsync();
            }
            finally
            {
                try { await source.DisposeAsync(); }
                finally { DmPendingOpenTestHooks.Handshake = null; }
            }
        }
    }

    private sealed class PagedChannel(bool metadata) : IDmByteChannel
    {
        private byte[] response = [];
        private int position;
        private short opcode;
        internal readonly List<short> Opcodes = [];
        internal readonly List<DmInvocation> FetchInvocations = [];
        internal readonly List<long> FetchStarts = [];
        internal int AsyncCalls;
        internal int FetchAcksDelivered;
        internal Action<DmInvocation>? AfterFetchAck;
        internal bool EmptyPage;
        internal string FetchFailure = "";
        internal readonly IOException ReceiveFailure = new("Synthetic paged read I/O failure.");
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout) => SendCore(buffer, offset, count);
        public int Receive(byte[] buffer, int offset, int count, int timeout) => ReceiveCore(buffer, offset, count);
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, offset, count)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { AsyncCalls++; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReceiveCore(buffer, offset, count)); }
        private int SendCore(byte[] buffer, int offset, int count)
        {
            opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Opcodes.Add(opcode);
            if (opcode == 3)
            {
                response = Frame(opcode, []);
                BinaryPrimitives.WriteInt32LittleEndian(response, 41); Checksum(response);
            }
            else if (opcode is 5 or 91) response = Query(opcode);
            else if (opcode == 4) response = Frame(opcode, []);
            else if (opcode == 7)
            {
                FetchInvocations.Add(DmInvocation.Current);
                FetchStarts.Add(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 20)));
                Assert.True(DmInvocation.Current.SendAttempted);
                response = Fetch();
                if (FetchFailure == "server")
                {
                    response = Frame(opcode, new byte[16]);
                    BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(10), -2106); Checksum(response);
                }
                else if (FetchFailure == "truncated") response = response[..31];
            }
            else throw new InvalidOperationException($"Unexpected synthetic paged read opcode {opcode}.");
            position = 0; return count;
        }
        private int ReceiveCore(byte[] buffer, int offset, int count)
        {
            if (opcode == 7 && FetchFailure == "io") throw ReceiveFailure;
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read)); position += read;
            if (opcode == 7 && read > 0 && position == response.Length && FetchFailure == "")
            {
                FetchAcksDelivered++;
                AfterFetchAck?.Invoke(DmInvocation.Current);
            }
            return read;
        }
        public void Dispose() => IsClosed = true;

        private byte[] Query(short operation)
        {
            string[] names = metadata ? ["OWNER", "OBJECT_NAME", "OBJECT_TYPE"] : ["VALUE"];
            byte[][] values = metadata ? Text("SYNTHETIC_SCHEMA", "FIRST_TABLE", "TABLE") : [BitConverter.GetBytes(17)];
            var body = new List<byte>();
            foreach (string name in names)
            {
                byte[] descriptor = new byte[32];
                BinaryPrimitives.WriteInt32LittleEndian(descriptor, metadata ? 2 : 7);
                BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(4), metadata ? 128 : 4);
                BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(12), 1);
                BinaryPrimitives.WriteInt16LittleEndian(descriptor.AsSpan(24), (short)name.Length);
                body.AddRange(descriptor); body.AddRange(Encoding.ASCII.GetBytes(name));
            }
            body.AddRange(Row(values));
            byte[] frame = Frame(operation, body.ToArray());
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), (short)names.Length);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), long.MaxValue);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1);
            Checksum(frame); return frame;
        }
        private byte[] Fetch()
        {
            byte[] body = EmptyPage ? [] : metadata ? Row(Text("SYNTHETIC_SCHEMA", "SECOND_VIEW", "VIEW")) :
                Row([BitConverter.GetBytes(18)]).Concat(Row([BitConverter.GetBytes(19)])).ToArray();
            byte[] frame = Frame(7, body);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20), EmptyPage ? 1 : metadata ? 2 : 3);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28), EmptyPage ? 0 : metadata ? 1 : 2);
            Checksum(frame); return frame;
        }
        private static byte[][] Text(params string[] values) => values.Select(Encoding.UTF8.GetBytes).ToArray();
        private static byte[] Row(byte[][] values)
        {
            // Plain row: two prefix bytes, 12-byte rowid, two ordinal bytes
            // per column, then a UINT16 length and bytes for each value.
            var row = new List<byte>(new byte[14 + 2 * values.Length]);
            foreach (byte[] value in values)
            {
                byte[] length = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(length, checked((ushort)value.Length));
                row.AddRange(length); row.AddRange(value);
            }
            return row.ToArray();
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

    private sealed class PagedObserver : IDisposable
    {
        private readonly ActivityListener listener;
        private readonly MeterListener meter = new();
        private readonly object gate = new();
        private readonly Dictionary<string, long> activities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> counters = new(StringComparer.Ordinal);
        private Dictionary<string, long> baseline = new(StringComparer.Ordinal);
        private readonly List<(string Result, ActivityStatusCode Status)> statuses = [];
        internal PagedObserver(Activity caller)
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId != caller.TraceId || activity.ParentSpanId != caller.SpanId) return;
                    string? operation = activity.GetTagItem("operation") as string;
                    if (operation is not ("fetch" or "metadata")) return;
                    string result = activity.GetTagItem("result") as string ?? "unknown";
                    lock (gate)
                    {
                        string key = operation + "|" + result;
                        activities[key] = activities.GetValueOrDefault(key) + 1;
                        statuses.Add((result, activity.Status));
                    }
                }
            };
            ActivitySource.AddActivityListener(listener);
            meter.InstrumentPublished = (instrument, owner) =>
            { if (instrument.Meter.Name == DmDiagnostics.MeterName && instrument.Name == "wdm.operation.total") owner.EnableMeasurementEvents(instrument); };
            meter.SetMeasurementEventCallback<long>((_, count, tags, _) =>
            {
                string? operation = null, result = null;
                foreach (var tag in tags) { if (tag.Key == "operation") operation = tag.Value as string; if (tag.Key == "result") result = tag.Value as string; }
                if ((operation is "fetch" or "metadata") && result != null)
                    lock (gate) counters[operation + "|" + result] = count;
            });
            meter.Start();
        }
        internal async Task Baseline()
        {
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            meter.RecordObservableInstruments();
            lock (gate) { baseline = new Dictionary<string, long>(counters, StringComparer.Ordinal); activities.Clear(); statuses.Clear(); }
        }
        internal async Task AssertResults(params (string Operation, string Result, long Count)[] expected)
        {
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
            meter.RecordObservableInstruments();
            lock (gate)
            {
                var actual = counters.ToDictionary(pair => pair.Key, pair => pair.Value - baseline.GetValueOrDefault(pair.Key), StringComparer.Ordinal)
                    .Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                var buckets = expected.Select(item => new KeyValuePair<string, long>(item.Operation + "|" + item.Result, item.Count))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                Assert.Equal(buckets, actual);
                Assert.Equal(buckets, activities.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
                foreach (var status in statuses)
                    Assert.Equal(status.Result == "success" ? ActivityStatusCode.Unset : ActivityStatusCode.Error, status.Status);
            }
        }
        public void Dispose() { meter.Dispose(); listener.Dispose(); }
    }
}
