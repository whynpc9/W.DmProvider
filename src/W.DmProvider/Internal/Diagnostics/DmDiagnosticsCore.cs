using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace W.Dm.Internal.Diagnostics;

internal enum DmDiagnosticOperation { Unknown, Connect, Execute, Prepare, Fetch, Commit, Rollback, ReaderClose, Metadata, TransactionBegin }
internal enum DmDiagnosticResult { Unknown, Success, ServerError, Canceled, Timeout, OutcomeUnknown, TransportError, Rejected }
internal enum DmDiagnosticReason { ResetNotVerified, Broken, Failed }
internal enum DmDiagnosticLobKind { Binary, Text }
internal readonly record struct DmDiagnosticStamp(long Timestamp, long UtcTicks, ActivityTraceId TraceId, ActivitySpanId SpanId, ActivityTraceFlags Flags);
internal readonly record struct DmDiagnosticsSnapshot(long ConnectionsCreated, long ConnectionsClosed,
    long PoolCreating, long PoolLeased, long PoolClosing, long PoolWaiting, long RegistryEntries,
    int QueueDepth, int QueueCapacity, int QueuePeak, long Dropped, long CallbackFailures,
    long SentBytes, long ReceivedBytes, long LobReadChunks, long LobWriteChunks,
    long Acquires, long Operations, int InitializationState, int WorkerStarts, long ExportedRecords)
{
    internal long PoolIdle => 0;
    internal long PoolResetting => 0;
}

/// <summary>Business paths store numbers/value records only. All diagnostics sinks live on one consumer.</summary>
internal static class DmDiagnosticsCore
{
    internal const int QueueCapacity = 4096;
    private enum RecordKind { Operation, Acquire }
    private readonly record struct Completion(RecordKind Kind, DmDiagnosticOperation Operation, DmDiagnosticResult Result,
        DmDiagnosticStamp Start, long EndUtcTicks, double DurationMilliseconds, bool Flag);
    private const int OperationCount = 10, ResultCount = 8;
    private static readonly long[] acquireBuckets = new long[ResultCount];
    private static readonly long[] operationBuckets = new long[OperationCount * ResultCount];
    private static readonly long[] discardBuckets = new long[3];
    private static readonly long[] lobBuckets = new long[8];
    private static readonly Channel<Completion> Queue = Channel.CreateBounded<Completion>(new BoundedChannelOptions(QueueCapacity)
    { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait });
    private static long created, closed, creating, leased, closing, waiting, registry, dropped, callbackFailures,
        sent, received, readChunks, writeChunks, acquires, operations, accepted, processed, exported;
    private static int depth, peak, started, initialization;
    private static Task worker;
    private static ActivitySource source;
    private static Meter meter;
    private static Histogram<double> acquireDuration, waitDuration, operationDuration;

    internal static DmDiagnosticsSnapshot Snapshot => new(
        Interlocked.Read(ref created), Interlocked.Read(ref closed), Interlocked.Read(ref creating), Interlocked.Read(ref leased),
        Interlocked.Read(ref closing), Interlocked.Read(ref waiting), Interlocked.Read(ref registry),
        Volatile.Read(ref depth), QueueCapacity, Volatile.Read(ref peak), Interlocked.Read(ref dropped), Interlocked.Read(ref callbackFailures),
        Interlocked.Read(ref sent), Interlocked.Read(ref received), Interlocked.Read(ref readChunks), Interlocked.Read(ref writeChunks),
        Interlocked.Read(ref acquires), Interlocked.Read(ref operations), Volatile.Read(ref initialization), Volatile.Read(ref started), Interlocked.Read(ref exported));

    internal static DmDiagnosticStamp Start()
    {
        Activity current = Activity.Current;
        return new(Stopwatch.GetTimestamp(), DateTime.UtcNow.Ticks, current?.TraceId ?? default, current?.SpanId ?? default,
            current?.ActivityTraceFlags ?? ActivityTraceFlags.None);
    }
    internal static DmDiagnosticResult Classify(Exception error) => error switch
    {
        DmCommitOutcomeUnknownException => DmDiagnosticResult.OutcomeUnknown,
        DmTimeoutException => DmDiagnosticResult.Timeout,
        OperationCanceledException => DmDiagnosticResult.Canceled,
        DmException { ErrorKind: DmErrorKind.Server } => DmDiagnosticResult.ServerError,
        DmException { ErrorKind: DmErrorKind.OutcomeUnknown } => DmDiagnosticResult.OutcomeUnknown,
        DmException { ErrorKind: DmErrorKind.Timeout } => DmDiagnosticResult.Timeout,
        DmException { ErrorKind: DmErrorKind.Canceled } => DmDiagnosticResult.Canceled,
        DmException { ErrorKind: DmErrorKind.Transport } => DmDiagnosticResult.TransportError,
        _ => DmDiagnosticResult.Rejected
    };
    internal static void PoolDelta(int creatingDelta = 0, int leasedDelta = 0, int closingDelta = 0, int waitingDelta = 0)
    {
        Interlocked.Add(ref creating, creatingDelta); Interlocked.Add(ref leased, leasedDelta);
        Interlocked.Add(ref closing, closingDelta); Interlocked.Add(ref waiting, waitingDelta);
    }
    internal static void RegistryDelta(int delta) => Interlocked.Add(ref registry, delta);
    internal static void ConnectionCreated() => Interlocked.Increment(ref created);
    internal static void ConnectionClosed() => Interlocked.Increment(ref closed);
    internal static void Discard(DmDiagnosticReason reason) =>
        Interlocked.Increment(ref discardBuckets[(uint)reason < 3 ? (int)reason : (int)DmDiagnosticReason.Failed]);
    internal static void NetworkBytes(int count, bool sending)
    {
        if (count <= 0) return;
        if (sending) Interlocked.Add(ref sent, count); else Interlocked.Add(ref received, count);
    }
    internal static void LobChunk(bool writing, bool text, bool terminal)
    {
        if (writing) Interlocked.Increment(ref writeChunks); else Interlocked.Increment(ref readChunks);
        Interlocked.Increment(ref lobBuckets[(writing ? 4 : 0) + (text ? 2 : 0) + (terminal ? 1 : 0)]);
    }
    internal static void CompleteAcquire(DmDiagnosticStamp start, DmDiagnosticResult result, bool queued)
    {
        result = NormalizeResult(result);
        Interlocked.Increment(ref acquires); Interlocked.Increment(ref acquireBuckets[(int)result]);
        Complete(RecordKind.Acquire, DmDiagnosticOperation.Unknown, result, start, queued);
    }
    internal static void CompleteOperation(DmDiagnosticStamp start, DmDiagnosticOperation operation, DmDiagnosticResult result)
    {
        operation = NormalizeOperation(operation); result = NormalizeResult(result);
        Interlocked.Increment(ref operations); Interlocked.Increment(ref operationBuckets[(int)operation * ResultCount + (int)result]);
        Complete(RecordKind.Operation, operation, result, start, false);
    }
    internal static void RecordTestCompletion(DmDiagnosticOperation operation = DmDiagnosticOperation.Execute,
        DmDiagnosticResult result = DmDiagnosticResult.Success) => CompleteOperation(Start(), operation, result);

    private static void Complete(RecordKind kind, DmDiagnosticOperation operation, DmDiagnosticResult result, DmDiagnosticStamp start, bool flag)
    {
        double milliseconds = Math.Max(0, Stopwatch.GetElapsedTime(start.Timestamp).TotalMilliseconds);
        Write(new(kind, operation, result, start, DateTime.UtcNow.Ticks, milliseconds, flag));
    }
    private static DmDiagnosticOperation NormalizeOperation(DmDiagnosticOperation operation) =>
        (uint)operation < OperationCount ? operation : DmDiagnosticOperation.Unknown;
    private static DmDiagnosticResult NormalizeResult(DmDiagnosticResult result) =>
        (uint)result < ResultCount ? result : DmDiagnosticResult.Unknown;

    private static void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
        try
        {
            void Launch()
            {
                worker = Task.Run(ConsumeAsync);
                _ = worker.ContinueWith(static faulted =>
                {
                    _ = faulted.Exception; // Observe an unexpected infrastructure fault; never export it.
                    Interlocked.Increment(ref callbackFailures); Volatile.Write(ref initialization, 2);
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            if (ExecutionContext.IsFlowSuppressed()) Launch();
            else { using (ExecutionContext.SuppressFlow()) Launch(); }
        }
        catch { Volatile.Write(ref initialization, 2); Interlocked.Increment(ref callbackFailures); }
    }
    private static void Write(Completion record)
    {
        // Only completed operations/acquisitions reach this lossy span/duration
        // queue. Their process totals already reside in lossless atomic buckets.
        // A dropped first completion still qualifies the single sink initializer.
        EnsureStarted();
        int reserved;
        while (true)
        {
            int current = Volatile.Read(ref depth);
            if (current >= QueueCapacity) { Interlocked.Increment(ref dropped); return; }
            reserved = current + 1;
            if (Interlocked.CompareExchange(ref depth, reserved, current) == current) break;
        }
        int previous;
        while (reserved > (previous = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, reserved, previous) != previous) { }
        Interlocked.Increment(ref accepted);
        if (!Queue.Writer.TryWrite(record))
        { Interlocked.Decrement(ref depth); Interlocked.Decrement(ref accepted); Interlocked.Increment(ref dropped); }
    }

    private static async Task ConsumeAsync()
    {
        try
        {
            InitializeSinks();
            while (await Queue.Reader.WaitToReadAsync().ConfigureAwait(false))
                while (Queue.Reader.TryRead(out Completion record))
                {
                    Interlocked.Decrement(ref depth);
                    Activity prior = Activity.Current;
                    try { if (Volatile.Read(ref initialization) == 1) Export(record); }
                    catch { Interlocked.Increment(ref callbackFailures); }
                    finally { Activity.Current = prior; Interlocked.Increment(ref processed); }
                }
        }
        catch { Interlocked.Increment(ref callbackFailures); Volatile.Write(ref initialization, 2); }
    }
    private static void InitializeSinks()
    {
        Activity prior = Activity.Current;
        try
        {
            source = new ActivitySource(DmDiagnostics.ActivitySourceName, "0.1.0.0");
            meter = new Meter(DmDiagnostics.MeterName, "0.1.0.0");
            // Late observers see process accumulation, not a sum of surviving
            // queue events. External telemetry delivery remains best-effort.
            meter.CreateObservableCounter<long>("wdm.connection.created", () => Interlocked.Read(ref created));
            meter.CreateObservableCounter<long>("wdm.connection.closed", () => Interlocked.Read(ref closed));
            meter.CreateObservableCounter<long>("wdm.pool.acquire.total", ObserveAcquire);
            meter.CreateObservableCounter<long>("wdm.operation.total", ObserveOperations);
            meter.CreateObservableCounter<long>("wdm.connection.discard.total", ObserveDiscards);
            meter.CreateObservableCounter<long>("wdm.network.bytes", ObserveNetwork, "By");
            meter.CreateObservableCounter<long>("wdm.lob.chunks", ObserveLob);
            acquireDuration = meter.CreateHistogram<double>("wdm.pool.acquire.duration", "ms");
            waitDuration = meter.CreateHistogram<double>("wdm.pool.wait.duration", "ms");
            operationDuration = meter.CreateHistogram<double>("wdm.operation.duration", "ms");
            meter.CreateObservableGauge<long>("wdm.pool.connections", ObservePool);
            meter.CreateObservableGauge<long>("wdm.pool.waiters", () => Interlocked.Read(ref waiting));
            meter.CreateObservableGauge<long>("wdm.pool.registry.entries", () => Interlocked.Read(ref registry));
            meter.CreateObservableGauge<long>("wdm.diagnostics.dropped", () => Interlocked.Read(ref dropped));
            meter.CreateObservableGauge<long>("wdm.diagnostics.callback_failures", () => Interlocked.Read(ref callbackFailures));
            Volatile.Write(ref initialization, 1);
        }
        catch
        {
            Volatile.Write(ref initialization, 2); Interlocked.Increment(ref callbackFailures);
            try { meter?.Dispose(); } catch { Interlocked.Increment(ref callbackFailures); }
            try { source?.Dispose(); } catch { Interlocked.Increment(ref callbackFailures); }
            meter = null; source = null;
        }
        finally { Activity.Current = prior; }
    }
    private static IEnumerable<Measurement<long>> ObservePool() => new[]
    {
        new Measurement<long>(Interlocked.Read(ref creating), new KeyValuePair<string, object>("state", "creating")),
        new Measurement<long>(Interlocked.Read(ref leased), new KeyValuePair<string, object>("state", "leased")),
        new Measurement<long>(Interlocked.Read(ref closing), new KeyValuePair<string, object>("state", "closing")),
        new Measurement<long>(0, new KeyValuePair<string, object>("state", "idle")),
        new Measurement<long>(0, new KeyValuePair<string, object>("state", "resetting"))
    };

    private static IEnumerable<Measurement<long>> ObserveAcquire()
    {
        for (int result = 0; result < ResultCount; result++)
            yield return new Measurement<long>(Interlocked.Read(ref acquireBuckets[result]),
                new KeyValuePair<string, object>("result", ResultName((DmDiagnosticResult)result)));
    }
    private static IEnumerable<Measurement<long>> ObserveOperations()
    {
        for (int operation = 0; operation < OperationCount; operation++)
            for (int result = 0; result < ResultCount; result++)
                yield return new Measurement<long>(Interlocked.Read(ref operationBuckets[operation * ResultCount + result]),
                    new KeyValuePair<string, object>("operation", OperationName((DmDiagnosticOperation)operation)),
                    new KeyValuePair<string, object>("result", ResultName((DmDiagnosticResult)result)));
    }
    private static IEnumerable<Measurement<long>> ObserveDiscards()
    {
        for (int reason = 0; reason < discardBuckets.Length; reason++)
            yield return new Measurement<long>(Interlocked.Read(ref discardBuckets[reason]),
                new KeyValuePair<string, object>("reason", ReasonName((DmDiagnosticReason)reason)));
    }
    private static IEnumerable<Measurement<long>> ObserveNetwork() => new[]
    {
        new Measurement<long>(Interlocked.Read(ref sent), new KeyValuePair<string, object>("direction", "sent")),
        new Measurement<long>(Interlocked.Read(ref received), new KeyValuePair<string, object>("direction", "received"))
    };
    private static IEnumerable<Measurement<long>> ObserveLob()
    {
        for (int writing = 0; writing < 2; writing++)
            for (int text = 0; text < 2; text++)
                for (int terminal = 0; terminal < 2; terminal++)
                    yield return new Measurement<long>(Interlocked.Read(ref lobBuckets[writing * 4 + text * 2 + terminal]),
                        new KeyValuePair<string, object>("direction", writing == 1 ? "write" : "read"),
                        new KeyValuePair<string, object>("kind", text == 1 ? "text" : "binary"),
                        new KeyValuePair<string, object>("terminal", terminal == 1));
    }

    private static string OperationName(DmDiagnosticOperation operation) => operation switch
    {
        DmDiagnosticOperation.Connect => "connect", DmDiagnosticOperation.Execute => "execute", DmDiagnosticOperation.Prepare => "prepare",
        DmDiagnosticOperation.Fetch => "fetch", DmDiagnosticOperation.Commit => "commit", DmDiagnosticOperation.Rollback => "rollback",
        DmDiagnosticOperation.ReaderClose => "reader_close", DmDiagnosticOperation.Metadata => "metadata",
        DmDiagnosticOperation.TransactionBegin => "transaction_begin", _ => "unknown"
    };
    private static string ResultName(DmDiagnosticResult result) => result switch
    {
        DmDiagnosticResult.Success => "success", DmDiagnosticResult.ServerError => "server_error", DmDiagnosticResult.Canceled => "canceled",
        DmDiagnosticResult.Timeout => "timeout", DmDiagnosticResult.OutcomeUnknown => "outcome_unknown",
        DmDiagnosticResult.TransportError => "transport_error", DmDiagnosticResult.Rejected => "rejected", _ => "unknown"
    };
    private static string ReasonName(DmDiagnosticReason reason) => reason switch
    { DmDiagnosticReason.ResetNotVerified => "reset_not_verified", DmDiagnosticReason.Broken => "broken", _ => "failed" };
    private static void Isolate(Action callback) { try { callback(); } catch { Interlocked.Increment(ref callbackFailures); } }
    private static void Export(Completion record)
    {
        var result = new KeyValuePair<string, object>("result", ResultName(record.Result));
        var operation = new KeyValuePair<string, object>("operation", OperationName(record.Operation));
        switch (record.Kind)
        {
            case RecordKind.Acquire:
                Isolate(() => acquireDuration.Record(record.DurationMilliseconds, result));
                Isolate(() => waitDuration.Record(record.Flag ? record.DurationMilliseconds : 0, result));
                ExportSpan(record, "pool_acquire", result); break;
            case RecordKind.Operation:
                Isolate(() => operationDuration.Record(record.DurationMilliseconds, operation, result));
                ExportSpan(record, OperationName(record.Operation), result); break;
        }
        Interlocked.Increment(ref exported);
    }
    private static void ExportSpan(Completion record, string name, KeyValuePair<string, object> result)
    {
        Activity prior = Activity.Current;
        Activity activity = null;
        try
        {
            var parent = new ActivityContext(record.Start.TraceId, record.Start.SpanId, record.Start.Flags);
            activity = source.StartActivity("wdm." + name, ActivityKind.Client, parent,
                new[] { result }, startTime: new DateTimeOffset(new DateTime(record.Start.UtcTicks, DateTimeKind.Utc)));
            if (activity != null)
            {
                activity.SetTag("operation", name);
                if (record.Result != DmDiagnosticResult.Success) activity.SetStatus(ActivityStatusCode.Error);
                activity.SetEndTime(new DateTime(Math.Max(record.Start.UtcTicks, record.EndUtcTicks), DateTimeKind.Utc));
                Isolate(activity.Stop);
            }
        }
        catch { Interlocked.Increment(ref callbackFailures); }
        finally
        {
            if (activity != null) Isolate(activity.Dispose);
            Activity.Current = prior;
        }
    }

    internal static async Task<bool> FlushAsync(TimeSpan timeout, CancellationToken token = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        long target = Interlocked.Read(ref accepted);
        long start = Stopwatch.GetTimestamp();
        while (Interlocked.Read(ref processed) < target)
        {
            token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(start) >= timeout) return false;
            await Task.Delay(1, token).ConfigureAwait(false);
        }
        return true;
    }
}
