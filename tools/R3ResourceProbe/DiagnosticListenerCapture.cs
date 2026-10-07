using System.Diagnostics;
using System.Diagnostics.Metrics;
using W.Dm;

internal sealed class DiagnosticListenerCapture : IDisposable
{
    internal static readonly HashSet<string> AllowedNames = new(StringComparer.Ordinal)
    {
        "wdm.connection.created", "wdm.connection.closed", "wdm.pool.connections", "wdm.pool.waiters", "wdm.pool.registry.entries",
        "wdm.pool.acquire.total", "wdm.pool.acquire.duration", "wdm.pool.wait.duration", "wdm.operation.total", "wdm.operation.duration",
        "wdm.connection.discard.total", "wdm.network.bytes", "wdm.lob.chunks", "wdm.diagnostics.dropped", "wdm.diagnostics.callback_failures"
    };
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    { "pool_acquire", "connect", "execute", "prepare", "fetch", "commit", "rollback", "reader_close", "metadata", "transaction_begin", "unknown" };
    private static readonly Dictionary<string, HashSet<string>> Values = new(StringComparer.Ordinal)
    {
        ["operation"] = Operations,
        ["result"] = new(StringComparer.Ordinal) { "success", "server_error", "canceled", "timeout", "outcome_unknown", "transport_error", "rejected", "unknown" },
        ["reason"] = new(StringComparer.Ordinal) { "reset_not_verified", "failed", "broken" },
        ["direction"] = new(StringComparer.Ordinal) { "sent", "received", "read", "write" },
        ["state"] = new(StringComparer.Ordinal) { "creating", "leased", "closing", "idle", "resetting" },
        ["kind"] = new(StringComparer.Ordinal) { "binary", "text" }
    };
    private readonly ActivityListener activity;
    private readonly MeterListener meter = new();
    private readonly object gate = new();
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly HashSet<string> tagCombinations = new(StringComparer.Ordinal);
    private long activities, measurements;
    private readonly Dictionary<string, long> cumulative = new(StringComparer.Ordinal);
    internal bool InvalidTag, ContextLeaked;
    internal DiagnosticListenerCapture(string? fault = null, Func<string?>? context = null)
    {
        activity = new ActivityListener
        {
            ShouldListenTo = source =>
            {
                if (source.Name != DmDiagnostics.ActivitySourceName) return false;
                if (context?.Invoke() != null) ContextLeaked = true;
                ThrowAt(fault, "should_listen"); return true;
            },
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            { if (context?.Invoke() != null || !string.IsNullOrEmpty(options.Parent.TraceState)) ContextLeaked = true; ThrowAt(fault, "sample"); return ActivitySamplingResult.AllData; },
            ActivityStarted = span => { ObserveActivity(span, context); ThrowAt(fault, "started"); },
            ActivityStopped = span => { ObserveActivity(span, context); ThrowAt(fault, "stopped"); }
        };
        ActivitySource.AddActivityListener(activity);
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != DmDiagnostics.MeterName) return;
            if (context?.Invoke() != null) ContextLeaked = true;
            ThrowAt(fault, "instrument_published");
            lock (gate) { if (AllowedNames.Contains(instrument.Name)) names.Add(instrument.Name); else InvalidTag = true; }
            listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { ObserveMetric(instrument, tags, context, value); ThrowAt(fault, "measurement"); });
        meter.SetMeasurementEventCallback<double>((instrument, _, tags, _) => { ObserveMetric(instrument, tags, context); ThrowAt(fault, "measurement"); });
        meter.Start();
    }
    private static void ThrowAt(string? point, string current)
    { if (point == current) throw new Exception("SYNTHETIC_CALLBACK_SQL_PASSWORD_SECRET"); }
    private void ObserveActivity(Activity span, Func<string?>? context)
    {
        lock (gate)
        {
            activities++;
            if (context?.Invoke() != null || !string.IsNullOrEmpty(span.TraceStateString) || span.Baggage.Any()) ContextLeaked = true;
            if (!span.OperationName.StartsWith("wdm.", StringComparison.Ordinal) || !Operations.Contains(span.OperationName[4..]) || span.Events.Any()) InvalidTag = true;
            foreach (var tag in span.TagObjects) CheckTag(tag.Key, tag.Value);
        }
    }
    private void ObserveMetric(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags, Func<string?>? context, long? value = null)
    {
        lock (gate)
        {
            measurements++; if (context?.Invoke() != null) ContextLeaked = true;
            if (!AllowedNames.Contains(instrument.Name)) InvalidTag = true;
            foreach (var tag in tags) CheckTag(tag.Key, tag.Value);
            if (instrument is ObservableCounter<long> && value.HasValue && AllowedNames.Contains(instrument.Name) && !InvalidTag)
                cumulative[instrument.Name + "|" + string.Join(";", tags.ToArray().Select(t => t.Key + "=" + t.Value).Order(StringComparer.Ordinal))] = value.Value;
        }
    }
    private void CheckTag(string key, object? value)
    {
        if (key == "terminal" && value is bool terminal) { tagCombinations.Add("terminal=" + terminal); return; }
        if (value is not string text || !Values.TryGetValue(key, out var allowed) || !allowed.Contains(text)) { InvalidTag = true; return; }
        if (tagCombinations.Count >= 128) { InvalidTag = true; return; }
        tagCombinations.Add(key + "=" + text);
    }
    internal void ReadObservableInstruments() => meter.RecordObservableInstruments();
    internal object Snapshot { get { lock (gate) return new { activities, measurements, instruments = names.Order(StringComparer.Ordinal).ToArray(),
        finite_tag_combinations = tagCombinations.Count, invalid_tag = InvalidTag, caller_context_leaked = ContextLeaked,
        cumulative_counts = new Dictionary<string, long>(cumulative) }; } }
    internal long Total(string instrument, string? tag = null)
    { lock (gate) return cumulative.Where(p => p.Key.StartsWith(instrument + "|", StringComparison.Ordinal) && (tag == null || p.Key.Contains(tag, StringComparison.Ordinal))).Sum(p => p.Value); }
    internal long ActivityCount { get { lock (gate) return activities; } }
    internal long MeasurementCount { get { lock (gate) return measurements; } }
    public void Dispose() { meter.Dispose(); activity.Dispose(); }
}
