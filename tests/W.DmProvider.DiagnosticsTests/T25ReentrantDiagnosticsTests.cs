using System.Data;
using System.Diagnostics;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Pooling;
using W.Dm.Internal.Sessions;
using W.DmProvider.DiagnosticsTesting;
using Xunit;

namespace W.DmProvider.DiagnosticsTests;

[Collection("Diagnostics serial")]
[Trait("Category", "Contract")]
[Trait("Feature", "T25CallerBoundary")]
public sealed class T25ReentrantDiagnosticsTests
{
    [Fact]
    public async Task CommitStoppedCallbackCanClearAndDisposeItsSourceWhileASecondOpenIsQueued()
    {
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(DmPendingOpenTestHooks.AfterCapacityAcquired);
        Assert.Null(DmSessionTestHooks.BeforeConnectionTransportAbort);
        int capacityAcquisitions = 0;
        DmPendingOpenTestHooks.AfterCapacityAcquired = _ => Interlocked.Increment(ref capacityAcquisitions);
        try
        {
            await using var fixture = new ScriptedTransactionFixture();
            await using var transaction = await fixture.BeginAsync();
            var source = fixture.Source;
            var owner = source.Owner;
            var first = fixture.Connection;
            long firstSessionId = first.Session.SessionId;
            long initialEpoch = source.Snapshot.Epoch;
            fixture.Channel.BlockCommit = true;
            Task<DmConnection> waiting = source.OpenConnectionAsync().AsTask();
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, source.Snapshot.Waiting);
            Assert.Equal(1, source.Snapshot.Leased);
            Assert.Equal(1, capacityAcquisitions);

            using var caller = new Activity("synthetic.t25.commit.caller").SetIdFormat(ActivityIdFormat.W3C).Start();
            caller.SetTag("db.statement", "SYNTHETIC_T25_SQL_PARAMETER_SECRET");
            caller.AddBaggage("parameter", "SYNTHETIC_T25_SQL_PARAMETER_SECRET");
            caller.TraceStateString = "synthetic=SYNTHETIC_T25_SQL_PARAMETER_SECRET";
            using var observer = new ReentrantCommitObserver(caller.TraceId, caller.SpanId, source, owner, first,
                transaction, () => fixture.Channel.IsClosed);

            Task commit = transaction.CommitAsync();
            await fixture.Channel.CommitReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(commit.IsCompleted);
            Assert.Equal(DmTransactionOutcome.Committing, transaction.Outcome);
            Assert.False(observer.Completed.Task.IsCompleted);
            fixture.Channel.ReleaseCommit.TrySetResult(true);

            await commit.WaitAsync(TimeSpan.FromSeconds(5));
            CallbackObservation stopped = await observer.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(stopped.FailureHResult);
            Assert.Equal(DmTransactionOutcome.Committed, stopped.Outcome);
            Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome);
            Assert.Equal(1, fixture.Channel.CommitSends);
            Assert.Equal(1, observer.MatchedCallbacks);
            Assert.Equal(1, stopped.Before.Leased);
            Assert.Equal(1, stopped.Before.Waiting);
            Assert.Equal(1, stopped.After.Leased);
            Assert.Equal(0, stopped.After.Waiting);
            Assert.Equal(0, stopped.After.Creating);
            Assert.Equal(0, stopped.After.Closing);
            Assert.True(stopped.After.Stopped);
            Assert.Equal(initialEpoch + 2, stopped.After.Epoch); // Clear, then admission retirement.
            Assert.True(stopped.SameOwner);
            Assert.True(stopped.ConnectionStillOpen);
            Assert.False(stopped.TransportClosed);
            string safetyFlags = JsonSerializer.Serialize(stopped.RecordSafety);
            Assert.True(stopped.RecordSafety.TraceStateEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.BaggageEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.EventsEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.LinksEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.TagKeysAllowed, safetyFlags);
            Assert.True(stopped.RecordSafety.TagValuesExpected, safetyFlags);
            Assert.True(stopped.SafeRecord, safetyFlags);
            Assert.Equal(2, stopped.TagCount);
            Assert.NotEqual(ActivityStatusCode.Error, stopped.Status);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(owner, source.Owner);
            Assert.Equal(ConnectionState.Open, first.State);
            Assert.False(fixture.Channel.IsClosed);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.Equal(1, capacityAcquisitions); // The waiter did not enter a replacement capacity domain.

            var closing = new TaskCompletionSource<ClosingObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
            DmSessionTestHooks.BeforeConnectionTransportAbort = sessionId =>
            {
                if (sessionId == firstSessionId)
                    closing.TrySetResult(new(source.Snapshot, fixture.Channel.IsClosed));
            };
            await first.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            ClosingObservation beforeTransportClose = await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, beforeTransportClose.Pool.PhysicalCount);
            Assert.Equal(1, beforeTransportClose.Pool.Closing);
            Assert.False(beforeTransportClose.TransportClosed);
            Assert.True(fixture.Channel.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            Assert.True(source.Snapshot.IsQuiescent);
            Assert.Equal(0, fixture.Channel.SyncCalls);
            Assert.Equal(1, fixture.Channel.CommitSends);
            Assert.Equal(1, capacityAcquisitions);
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, observer.MatchedCallbacks);
        }
        finally
        {
            DmPendingOpenTestHooks.AfterCapacityAcquired = null;
            DmSessionTestHooks.BeforeConnectionTransportAbort = null;
        }
    }

    [Fact]
    public async Task ConfirmedPublicRollbackExportsSuccessForItsOwnCorrelatedSpan()
    {
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(DmWireTestHooks.AfterFrameSent);
        int rollbackFrames = 0;
        try
        {
            await using var fixture = new ScriptedTransactionFixture();
            await using var transaction = await fixture.BeginAsync();
            long sessionId = fixture.Connection.Session.SessionId;
            DmWireTestHooks.AfterFrameSent = (identity, opcode) =>
            {
                if (identity.SessionId == sessionId && opcode == 9) Interlocked.Increment(ref rollbackFrames);
            };
            int sentBefore = fixture.Channel.Sends;
            using var caller = new Activity("synthetic.t25.rollback.caller").SetIdFormat(ActivityIdFormat.W3C).Start();
            caller.SetTag("db.statement", "SYNTHETIC_T25_ROLLBACK_SQL_PARAMETER_SECRET");
            caller.AddBaggage("parameter", "SYNTHETIC_T25_ROLLBACK_SQL_PARAMETER_SECRET");
            caller.TraceStateString = "synthetic=SYNTHETIC_T25_ROLLBACK_SQL_PARAMETER_SECRET";
            using var observer = new RollbackObserver(caller.TraceId, caller.SpanId, transaction);
            await transaction.RollbackAsync().WaitAsync(TimeSpan.FromSeconds(5));
            RollbackObservation stopped = await observer.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DmTransactionOutcome.RolledBack, transaction.Outcome);
            Assert.Equal(DmTransactionOutcome.RolledBack, stopped.Outcome);
            Assert.Equal(1, rollbackFrames);
            Assert.Equal(1, fixture.Channel.Sends - sentBefore);
            Assert.Equal(0, fixture.Channel.CommitSends);
            Assert.Equal(1, observer.MatchedCallbacks);
            Assert.True(stopped.OperationIsRollback);
            Assert.True(stopped.ResultIsSuccess);
            Assert.NotEqual(ActivityStatusCode.Error, stopped.Status);
            string safetyFlags = JsonSerializer.Serialize(stopped.RecordSafety);
            Assert.True(stopped.RecordSafety.TraceStateEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.BaggageEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.EventsEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.LinksEmpty, safetyFlags);
            Assert.True(stopped.RecordSafety.TagKeysAllowed, safetyFlags);
            Assert.True(stopped.RecordSafety.TagValuesExpected, safetyFlags);
            Assert.True(stopped.RecordSafety.SafeRecord, safetyFlags);
            Assert.Equal(2, stopped.TagCount);
            await transaction.DisposeAsync();
            await fixture.Connection.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Channel.IsClosed);
            Assert.Equal(0, fixture.Source.Snapshot.PhysicalCount);
            Assert.True(fixture.Source.Snapshot.IsQuiescent);
            Assert.Equal(0, fixture.Channel.SyncCalls);
            Assert.Equal(1, rollbackFrames);
            Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, observer.MatchedCallbacks);
        }
        finally { DmWireTestHooks.AfterFrameSent = null; }
    }

    private sealed record RollbackObservation(DmTransactionOutcome Outcome, RecordSafety RecordSafety, int TagCount,
        bool OperationIsRollback, bool ResultIsSuccess, ActivityStatusCode Status);
    private sealed class RollbackObserver : IDisposable
    {
        private readonly ActivityListener listener;
        private int matched;
        internal int MatchedCallbacks => Volatile.Read(ref matched);
        internal TaskCompletionSource<RollbackObservation> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal RollbackObserver(ActivityTraceId traceId, ActivitySpanId parentSpanId, DmTransaction transaction)
        {
            listener = new ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName != "wdm.rollback" || activity.TraceId != traceId || activity.ParentSpanId != parentSpanId)
                        return;
                    Interlocked.Increment(ref matched);
                    bool keysAllowed = true, valuesExpected = true;
                    int tags = 0;
                    foreach (var tag in activity.TagObjects)
                    {
                        tags++;
                        keysAllowed &= tag.Key is "operation" or "result";
                        valuesExpected &= tag.Value is string value &&
                            ((tag.Key == "operation" && value == "rollback") || (tag.Key == "result" && value == "success"));
                    }
                    var flags = new RecordSafety(string.IsNullOrEmpty(activity.TraceStateString), !activity.Baggage.Any(),
                        !activity.Events.Any(), !activity.Links.Any(), keysAllowed, valuesExpected);
                    Completed.TrySetResult(new(transaction.Outcome, flags, tags,
                        activity.GetTagItem("operation") is string operation && operation == "rollback",
                        activity.GetTagItem("result") is string result && result == "success", activity.Status));
                    // Only controlled booleans/enum are observed; the worker never waits on a business task.
                }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }

    private sealed record ClosingObservation(DmPoolSnapshot Pool, bool TransportClosed);
    private sealed record CallbackObservation(DmTransactionOutcome Outcome, DmPoolSnapshot Before, DmPoolSnapshot After,
        bool SameOwner, bool ConnectionStillOpen, bool TransportClosed, bool SafeRecord, int TagCount,
        ActivityStatusCode Status, int? FailureHResult, RecordSafety RecordSafety);
    private sealed record RecordSafety(bool TraceStateEmpty, bool BaggageEmpty, bool EventsEmpty, bool LinksEmpty,
        bool TagKeysAllowed, bool TagValuesExpected)
    {
        internal bool SafeRecord => TraceStateEmpty && BaggageEmpty && EventsEmpty && LinksEmpty && TagKeysAllowed && TagValuesExpected;
    }

    private sealed class ReentrantCommitObserver : IDisposable
    {
        private readonly ActivityListener listener;
        private int matched;
        internal int MatchedCallbacks => Volatile.Read(ref matched);
        internal TaskCompletionSource<CallbackObservation> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ReentrantCommitObserver(ActivityTraceId traceId, ActivitySpanId parentSpanId, DmDataSource source,
            DmPoolOwner owner, DmConnection connection, DmTransaction transaction, Func<bool> transportClosed)
        {
            listener = new ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName != "wdm.commit" || activity.TraceId != traceId || activity.ParentSpanId != parentSpanId)
                        return;
                    Interlocked.Increment(ref matched);
                    var before = source.Snapshot;
                    DmTransactionOutcome outcome = transaction.Outcome;
                    try
                    {
                        bool traceStateEmpty = string.IsNullOrEmpty(activity.TraceStateString);
                        bool baggageEmpty = !activity.Baggage.Any();
                        bool eventsEmpty = !activity.Events.Any();
                        bool linksEmpty = !activity.Links.Any();
                        bool tagKeysAllowed = true, tagValuesExpected = true;
                        int tags = 0;
                        foreach (var tag in activity.TagObjects)
                        {
                            tags++;
                            // Inspect only fixed values; never render an untrusted diagnostic value.
                            tagKeysAllowed &= tag.Key is "operation" or "result";
                            tagValuesExpected &= tag.Value is string value &&
                                ((tag.Key == "operation" && value == "commit") || (tag.Key == "result" && value == "success"));
                        }
                        var recordSafety = new RecordSafety(traceStateEmpty, baggageEmpty, eventsEmpty, linksEmpty,
                            tagKeysAllowed, tagValuesExpected);
                        source.ClearPool();
                        source.Dispose();
                        Completed.TrySetResult(new(outcome, before, source.Snapshot, ReferenceEquals(owner, source.Owner),
                            connection.State == ConnectionState.Open, transportClosed(), recordSafety.SafeRecord, tags, activity.Status, null, recordSafety));
                    }
                    catch (Exception failure)
                    {
                        // Surface callback failure as a numeric observation; no Message/stack/secret is recorded.
                        Completed.TrySetResult(new(outcome, before, source.Snapshot, ReferenceEquals(owner, source.Owner),
                            connection.State == ConnectionState.Open, transportClosed(), false, 0, activity.Status, failure.HResult,
                            new(false, false, false, false, false, false)));
                    }
                    // No Flush, waiter wait, or business-task wait runs on the diagnostics worker.
                }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }
}
