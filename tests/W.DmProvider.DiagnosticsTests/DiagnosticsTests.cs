using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Data;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Pooling;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Transport;
using W.DmProvider.DiagnosticsTesting;
using Xunit;

namespace W.DmProvider.DiagnosticsTests;

[CollectionDefinition("Diagnostics serial", DisableParallelization = true)]
public sealed class SerialCollection { }

[Collection("Diagnostics serial")]
[Trait("Category", "Contract")]
[Trait("Feature", "Diagnostics")]
public sealed class DiagnosticsTests
{
    [Fact]
    public void SafeFormatterDoesNotReadMessageDataInnerOrUntrustedTypeName()
    {
        const string marker = "SYNTHETIC_SQL_PASSWORD_TYPE_SECRET";
        var unknown = new UNTRUSTED_TYPE_SECRET_Exception(marker); unknown.Data["sql"] = marker;
        Assert.DoesNotContain(marker, DmDiagnostics.FormatException(unknown));
        Assert.DoesNotContain("UNTRUSTED_TYPE_SECRET", DmDiagnostics.FormatException(unknown));
        var error = new DmException(marker);
        error.SetFailureInfo(new DmFailureInfo(DmErrorKind.Server, DmFailurePhase.Receive, marker,
            DmOperationOutcome.ServerReported, null, false, serverErrorNumber: -6602));
        string safe = DmDiagnostics.FormatException(error);
        Assert.DoesNotContain(marker, safe);
        using var result = JsonDocument.Parse(safe);
        Assert.Equal("Unknown", result.RootElement.GetProperty("code").GetString());
        Assert.Equal(-6602, result.RootElement.GetProperty("server_number").GetInt32());
        Assert.Equal("ServerReported", result.RootElement.GetProperty("operation_outcome").GetString());
    }

    [Fact]
    public async Task RealPublicMetricsAndActivitiesHideSensitiveContextAndHaveFiniteTags()
    {
        var dummy = new AsyncLocal<string?>();
        const string marker = "SYNTHETIC_CONTEXT_SQL_SCHEMA_ENDPOINT_SECRET";
        using var listener = new PublicListener(); listener.ContextProbe = () => dummy.Value;
        using var activity = new Activity("synthetic.caller").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = marker; activity.AddBaggage("secret", marker); dummy.Value = marker;
        try
        {
            var before = DmDiagnosticsCore.Snapshot;
            await using (var fixture = new ScriptedTransactionFixture())
            {
                await using var transaction = await fixture.BeginAsync(); await transaction.CommitAsync();
                Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome);
                await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
            }
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
            listener.Meter.RecordObservableInstruments();
            Assert.True(listener.ActivityCount > 0); Assert.True(listener.MeasurementCount > 0);
            Assert.False(listener.ContextLeaked); Assert.False(listener.InvalidTag);
            Assert.True(listener.Names.All(PublicListener.AllowedInstruments.Contains));
            Assert.Equal(before.Dropped, DmDiagnosticsCore.Snapshot.Dropped);
            Assert.DoesNotContain(marker, listener.SafeObservedText);
            Assert.DoesNotContain("SYNTHETIC_USER_SECRET", listener.SafeObservedText);
            Assert.DoesNotContain("SYNTHETIC_PASSWORD_SECRET", listener.SafeObservedText);
        }
        finally { dummy.Value = null; }
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("started")]
    [InlineData("stopped")]
    [InlineData("measurement")]
    public async Task ThrowingExportCallbacksCannotChangeActualPublicCommitAckOrOutcomeUnknown(string point)
    {
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        long failures = DmDiagnosticsCore.Snapshot.CallbackFailures;
        using var listener = new PublicListener(point);
        await using (var fixture = new ScriptedTransactionFixture())
        {
            await using var transaction = await fixture.BeginAsync(); await transaction.CommitAsync();
            Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome); Assert.Equal(1, fixture.Channel.CommitSends);
            await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent); Assert.Equal(0, fixture.Channel.SyncCalls);
        }
        await using (var fixture = new ScriptedTransactionFixture())
        {
            await using var transaction = await fixture.BeginAsync(); fixture.Channel.EofAtCommit = true;
            var error = await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => transaction.CommitAsync());
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
            Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
            Assert.False(error.FailureInfo.ConnectionReusable); Assert.Equal(1, fixture.Channel.CommitSends);
            await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
        }
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.True(DmDiagnosticsCore.Snapshot.CallbackFailures > failures);
    }

    [Fact]
    public async Task ThrowingListenersPreserveActualAfterSendCancellationTokenAndUnknownCommitFirstCause()
    {
        using var listener = new PublicListener("stopped");
        await using var fixture = new ScriptedTransactionFixture(); await using var transaction = await fixture.BeginAsync();
        fixture.Channel.BlockCommit = true; using var cancellation = new CancellationTokenSource();
        Task commit = transaction.CommitAsync(cancellation.Token);
        await fixture.Channel.CommitReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var error = await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => commit);
        var cause = Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(cancellation.Token, cause.CancellationToken);
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome); Assert.Equal(1, fixture.Channel.CommitSends);
        await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ActualSentCommitEofExportsOutcomeUnknownRatherThanSuccess()
    {
        using var listener = new PublicListener();
        await using var fixture = new ScriptedTransactionFixture(); await using var transaction = await fixture.BeginAsync();
        fixture.Channel.EofAtCommit = true;
        await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => transaction.CommitAsync());
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.True(listener.ResultCount("wdm.commit", "outcome_unknown") > 0);
        Assert.Equal(0, listener.ResultCount("wdm.commit", "success"));
        await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
    }

    [Fact]
    public async Task CoordinatorHarnessOutstandingCommitWireExportsTheFinalUnknownStateAfterEndCleanup()
    {
        using var listener = new PublicListener();
        await using var fixture = new ScriptedTransactionFixture(); await using var transaction = await fixture.BeginAsync();
        using var lease = fixture.Connection.Session.BeginExecution(DmOperationPurpose.TransactionControl);
        var invocation = lease.BeginInvocation();
        fixture.Connection.Session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
        using var exchange = fixture.Connection.Session.BeginWireExchange();
        object wire = fixture.Connection.m_ConnInst.GetCsi().A();
        var transport = (DmTransport)wire.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wire)!;
        byte[] frame = new byte[64]; BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), 8);
        await transport.SendAllAsync(frame, 0, frame.Length, DmDeadline.Infinite, 0);
        invocation.Dispose(); // Deliberate coordinator misuse: an owned sent wire lacks its ACK/Complete.
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, invocation.DiagnosticTransactionOutcome);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.True(listener.ResultCount("wdm.commit", "outcome_unknown") > 0);
        Assert.Equal(0, listener.ResultCount("wdm.commit", "success"));
        await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyntheticCursorNoProgressAndTruncationKeepOriginalFailuresAndNeverReplay(bool truncated)
    {
        using var listener = new PublicListener("measurement");
        var session = new DmSession(); session.CompleteHandshakeForTests();
        using var lease = session.BeginExecution(DmOperationPurpose.Reader);
        var locator = new AbstractLob(AbstractLob.LOB_FLAG_BYTE, null!) { readOver = truncated }; int calls = 0;
        using var cursor = new DmLobReadCursor(locator, default, false, lease, () => { }, 8,
            (_, _) => throw new InvalidOperationException("Sync cursor fetch forbidden."),
            (_, _, _) => { calls++; return Task.FromResult(truncated ? new Data(2, [1, 2]) : new Data(0, [])); }, false, "UTF-8");
        cursor.SetKnownWireLength(4);
        await Assert.ThrowsAsync<InvalidDataException>(() => cursor.ReadBytesAsync(new byte[8], default).AsTask());
        Assert.Equal(1, calls); Assert.Equal(DmPhysicalSessionState.Broken, session.State);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => cursor.ReadBytesAsync(new byte[8], default).AsTask());
        Assert.Equal(1, calls); Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task SlowCallbackAndSaturatedQueueDoNotBlockPublicCommitOrLeakPermits()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var listener = new PublicListener();
        listener.StopBarrier = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("synthetic callback barrier"); };
        long dropped = DmDiagnosticsCore.Snapshot.Dropped;
        DmDiagnosticsCore.RecordTestCompletion(DmDiagnosticOperation.Execute, DmDiagnosticResult.Success);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            await using var fixture = new ScriptedTransactionFixture(); await using var transaction = await fixture.BeginAsync();
            for (int i = 0; i < 4200; i++) DmDiagnosticsCore.RecordTestCompletion();
            await transaction.CommitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome);
            await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
            var snapshot = DmDiagnosticsCore.Snapshot;
            Assert.Equal(4096, snapshot.QueueCapacity); Assert.InRange(snapshot.QueueDepth, 0, 4096); Assert.InRange(snapshot.QueuePeak, 0, 4096);
            Assert.True(snapshot.Dropped > dropped); Assert.Equal(1, snapshot.WorkerStarts);
        }
        finally { listener.StopBarrier = null; release.Set(); }
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(15)));
        listener.Meter.RecordObservableInstruments();
        var final = DmDiagnosticsCore.Snapshot;
        Assert.Equal(final.ConnectionsCreated, listener.Total("wdm.connection.created"));
        Assert.Equal(final.ConnectionsClosed, listener.Total("wdm.connection.closed"));
        Assert.Equal(final.ConnectionsCreated, final.ConnectionsClosed);
        Assert.Equal(final.Operations, listener.Total("wdm.operation.total"));
    }

    [Fact]
    public async Task LatePublicListenerSeesPastCumulativeTotalsAndFailedObservationDoesNotClearThem()
    {
        await using (var fixture = new ScriptedTransactionFixture())
        {
            await using var transaction = await fixture.BeginAsync(); await transaction.CommitAsync(); await fixture.Connection.CloseAsync();
        }
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        using (var throwing = new PublicListener("measurement"))
            Assert.ThrowsAny<Exception>(() => throwing.Meter.RecordObservableInstruments()); // Caller owns this explicit SDK invocation.
        using var normal = new PublicListener(); normal.Meter.RecordObservableInstruments();
        var snapshot = DmDiagnosticsCore.Snapshot;
        Assert.Equal(snapshot.ConnectionsCreated, normal.Total("wdm.connection.created"));
        Assert.Equal(snapshot.ConnectionsClosed, normal.Total("wdm.connection.closed"));
        Assert.Equal(snapshot.Acquires, normal.Total("wdm.pool.acquire.total"));
        Assert.Equal(snapshot.Operations, normal.Total("wdm.operation.total"));
        Assert.Equal(snapshot.SentBytes, normal.Total("wdm.network.bytes", "direction=sent"));
        Assert.Equal(snapshot.ReceivedBytes, normal.Total("wdm.network.bytes", "direction=received"));
        Assert.Equal(snapshot.LobReadChunks, normal.Total("wdm.lob.chunks", "direction=read"));
        Assert.Equal(snapshot.LobWriteChunks, normal.Total("wdm.lob.chunks", "direction=write"));
        normal.Meter.RecordObservableInstruments();
        Assert.Equal(snapshot.ConnectionsCreated, normal.Total("wdm.connection.created")); // Latest cumulative bucket, not sum of polls.
        Assert.False(normal.InvalidTag);
    }

    [Fact]
    public async Task FixedTenThousandLeaseStressAndFourRegistryChurnRoundsFinishAtZeroWithoutDrops()
    {
        using var listener = new PublicListener(); Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        var before = DmDiagnosticsCore.Snapshot; var owner = new DmPoolOwner(1, 4); int completed = 0;
        for (int index = 0; index < 10000; index++)
        {
            var held = owner.Acquire(DmDeadline.Infinite);
            if (index % 7 != 0) held.MarkLeased(); // creating failure otherwise; no transport was instantiated.
            if (index % 11 == 0)
            {
                using var cancellation = new CancellationTokenSource();
                Task<DmPoolLease> pending = owner.AcquireAsync(DmDeadline.Infinite, cancellation.Token).AsTask(); cancellation.Cancel();
                await Assert.ThrowsAsync<DmOperationCanceledException>(() => pending);
            }
            if (index % 13 == 0) owner.Clear();
            held.BeginClosing(); Assert.Equal(1, owner.Snapshot.Closing); held.BeginClosing(); held.CompleteAfterTransportClosed(); held.CompleteAfterTransportClosed();
            Assert.True(owner.Snapshot.IsQuiescent); completed++;
            if (index % 32 == 31) Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        }
        for (int round = 0; round < 4; round++)
        {
            var registry = new DmPoolRegistry(128, expiration: TimeSpan.Zero); var references = new List<DmPoolOwnerReference>();
            for (int key = 0; key < 128; key++) references.Add(registry.GetOrCreate(new DmConnectionStringBuilder
            { User = "SYNTHETIC_USER_SECRET_" + key, Password = "SYNTHETIC_PASSWORD_SECRET", Pooling = true }.ToSettings()));
            Assert.Equal(128, registry.Count);
            Assert.Throws<DmException>(() => registry.GetOrCreate(new DmConnectionStringBuilder { User = "overflow", Pooling = true }.ToSettings()));
            foreach (var reference in references) reference.Dispose();
            using var after = registry.GetOrCreate(new DmConnectionStringBuilder { User = "next", Pooling = true }.ToSettings());
            Assert.Equal(1, registry.Count);
        }
        owner.Stop(); Assert.Equal(10000, completed);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        var final = DmDiagnosticsCore.Snapshot;
        Assert.Equal(before.Dropped, final.Dropped); Assert.Equal(before.PoolCreating, final.PoolCreating);
        Assert.Equal(before.PoolLeased, final.PoolLeased); Assert.Equal(before.PoolClosing, final.PoolClosing); Assert.Equal(before.PoolWaiting, final.PoolWaiting);
        Assert.Equal(0, final.QueueDepth); Assert.InRange(final.QueuePeak, 0, 4096); Assert.InRange(final.RegistryEntries, 0, 128);
        Assert.False(listener.InvalidTag); Assert.DoesNotContain("SYNTHETIC_USER_SECRET", listener.SafeObservedText);
    }

    [Fact]
    public async Task ActualCachedGetterAndLazyStreamConstructionDoNotExportRejectedOrErrorCompletions()
    {
        await using var fixture = new ScriptedTransactionFixture(); await using var transaction = await fixture.BeginAsync(); await transaction.CommitAsync();
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        using var listener = new PublicListener();
        using var lease = fixture.Connection.Session.BeginExecution(DmOperationPurpose.Reader);
        var instance = fixture.Connection.m_ConnInst; var statement = (W.Dm.Internal.Legacy.A.A)RuntimeHelpers.GetUninitializedObject(typeof(W.Dm.Internal.Legacy.A.A));
        var command = new DmCommand("synthetic.cached", fixture.Connection);
        void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
        Set(statement, "__t02_field_04000923", instance); Set(statement, "__t02_field_04000924", instance.GetCsi()); Set(statement, "__t02_field_04000933", command);
        Set(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b()); Set(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
        var info = new DmInfo(instance); info.SetColumnsInfo([new DmColumn(instance) { type = 12, name = "VALUE" }]); info.SetHasResultSet(true); info.SetRowCount(1);
        Set(statement, "__t02_field_04000927", info);
        byte[] locator = new byte[49]; locator[0] = 1; BinaryPrimitives.WriteInt32LittleEndian(locator.AsSpan(9), 2); locator[47] = 17; locator[48] = 148;
        var cache = new DmResultSetCache(statement, 1, 1) { datas = [[[], locator]], datasStartPos = 0 };
        Set(statement, "__t02_field_04000928", cache);
        var reader = new DmDataReader(cache, info, CommandBehavior.Default); reader.AttachExecutionLease(lease, ownsLease: false);
        Assert.True(reader.Read()); int sends = fixture.Channel.Sends;
        Assert.Equal(new byte[] { 17, 148 }, Assert.IsType<byte[]>(reader.GetValue(0)));
        using var stream = reader.GetStream(0); Assert.Equal(sends, fixture.Channel.Sends);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, listener.ErrorActivities); Assert.Equal(0, listener.RejectedActivities);
        long successful = listener.SuccessActivities;
        Assert.Equal(6032, Assert.Throws<DmException>(() => reader.GetValue(-1)).Number);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(successful, listener.SuccessActivities); Assert.Equal(sends, fixture.Channel.Sends);
        await fixture.Connection.CloseAsync(); Assert.True(fixture.Quiescent);
    }

    private sealed class UNTRUSTED_TYPE_SECRET_Exception(string message) : Exception(message);
}

internal sealed class PublicListener : IDisposable
{
    internal static readonly HashSet<string> AllowedInstruments = new(StringComparer.Ordinal)
    {
        "wdm.connection.created", "wdm.connection.closed", "wdm.pool.connections", "wdm.pool.waiters", "wdm.pool.registry.entries",
        "wdm.pool.acquire.total", "wdm.pool.acquire.duration", "wdm.pool.wait.duration", "wdm.operation.total", "wdm.operation.duration",
        "wdm.connection.discard.total", "wdm.network.bytes", "wdm.lob.chunks", "wdm.diagnostics.dropped", "wdm.diagnostics.callback_failures"
    };
    private readonly ActivityListener activity;
    internal readonly MeterListener Meter = new();
    private readonly object gate = new();
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> results = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> cumulative = new(StringComparer.Ordinal);
    internal long ActivityCount, MeasurementCount, ErrorActivities, RejectedActivities, SuccessActivities;
    internal bool InvalidTag, ContextLeaked;
    internal Func<string?>? ContextProbe;
    internal Action? StopBarrier;
    internal string SafeObservedText { get { lock (gate) return string.Join("|", observed); } }
    internal PublicListener(string? fault = null)
    {
        activity = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => { if (fault == "sample") throw new Exception("SYNTHETIC_CALLBACK_SECRET"); return ActivitySamplingResult.AllData; },
            ActivityStarted = value => { if (fault == "started") throw new Exception("SYNTHETIC_CALLBACK_SECRET"); ObserveActivity(value); },
            ActivityStopped = value => { StopBarrier?.Invoke(); if (fault == "stopped") throw new Exception("SYNTHETIC_CALLBACK_SECRET"); ObserveActivity(value); }
        };
        ActivitySource.AddActivityListener(activity);
        Meter.InstrumentPublished = (instrument, listener) =>
        { if (instrument.Meter.Name == DmDiagnostics.MeterName) { lock (gate) Names.Add(instrument.Name); listener.EnableMeasurementEvents(instrument); } };
        Meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => ObserveMetric(instrument, tags, fault, value));
        Meter.SetMeasurementEventCallback<double>((instrument, _, tags, _) => ObserveMetric(instrument, tags, fault));
        Meter.Start();
    }
    private void ObserveActivity(Activity value)
    {
        lock (gate)
        {
            ActivityCount++; if (ContextProbe?.Invoke() != null || !string.IsNullOrEmpty(value.TraceStateString) || value.Baggage.Any()) ContextLeaked = true;
            if (value.Status == ActivityStatusCode.Error) ErrorActivities++;
            if (value.GetTagItem("result") as string == "rejected") RejectedActivities++;
            if (value.GetTagItem("result") as string == "success") SuccessActivities++;
            string result = value.GetTagItem("result") as string ?? "unknown";
            string key = value.OperationName + "|" + result; results[key] = results.GetValueOrDefault(key) + 1;
            observed.Add(value.OperationName);
            foreach (var tag in value.TagObjects) CheckTag(tag.Key, tag.Value);
            if (value.Events.Any()) InvalidTag = true;
        }
    }
    internal long ResultCount(string operation, string result) { lock (gate) return results.GetValueOrDefault(operation + "|" + result); }
    internal long Total(string instrument, string? tag = null)
    { lock (gate) return cumulative.Where(p => p.Key.StartsWith(instrument + "|", StringComparison.Ordinal) && (tag == null || p.Key.Contains(tag, StringComparison.Ordinal))).Sum(p => p.Value); }
    private void ObserveMetric(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags, string? fault, long? value = null)
    {
        if (fault == "measurement") throw new Exception("SYNTHETIC_CALLBACK_SECRET");
        lock (gate)
        {
            MeasurementCount++; Names.Add(instrument.Name); if (!AllowedInstruments.Contains(instrument.Name)) InvalidTag = true;
            foreach (var tag in tags) CheckTag(tag.Key, tag.Value);
            if (instrument is ObservableCounter<long> && value.HasValue)
                cumulative[instrument.Name + "|" + string.Join(";", tags.ToArray().Select(t => t.Key + "=" + t.Value).Order(StringComparer.Ordinal))] = value.Value;
        }
    }
    private void CheckTag(string name, object? value)
    {
        if (name is not ("operation" or "result" or "reason" or "direction" or "state" or "kind" or "terminal") || value is not (string or bool)) InvalidTag = true;
        if (value is string text) { if (observed.Count < 256) observed.Add(name + "=" + text); else InvalidTag = true; }
    }
    public void Dispose() { Meter.Dispose(); activity.Dispose(); }
}
