using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using Xunit;

namespace W.DmProvider.LobTests;

// Regression: a successful legacy LOB getter must complete its own outer
// invocation. A reused ambient cursor invocation must not leave the public
// operation recorded as fetch/transport_error.
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class R3LegacyLobFetchDiagnosticTests
{
    [Fact]
    public async Task PublicBlobGetBytesReportsFetchSuccessWithoutTransportError()
    {
        await using var fixture = new OutputLobFixture();
        var blob = fixture.Blob(3);
        fixture.Channel.AddData([1, 2], -1);
        fixture.Channel.AddData([3], -1, true);
        fixture.ReleaseAll();
        using var scope = new Activity("synthetic.r3.legacy.fetch").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var observer = new FetchObserver(scope.TraceId, scope.SpanId);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        long successBaseline = observer.Counter("success");
        byte[] value = blob.GetBytes(0, 3);
        Assert.Equal(new byte[] { 1, 2, 3 }, value);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        var span = Assert.Single(observer.Results);
        Assert.Equal("success", span.Result);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.True(observer.Counter("success") >= successBaseline + 1);
    }

    [Fact]
    public async Task PublicClobGetSubStringReportsFetchSuccessWithoutTransportError()
    {
        await using var fixture = new OutputLobFixture();
        byte[] encoded = Encoding.UTF8.GetBytes("A中");
        var clob = fixture.Clob(encoded.Length);
        fixture.Channel.AddData(encoded, encoded.Length, true);
        fixture.ReleaseAll();
        using var scope = new Activity("synthetic.r3.legacy.fetch").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var observer = new FetchObserver(scope.TraceId, scope.SpanId);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        long successBaseline = observer.Counter("success");
        Assert.Equal("A中", clob.getSubString(0, 6));
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        var span = Assert.Single(observer.Results);
        Assert.Equal("success", span.Result);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.True(observer.Counter("success") >= successBaseline + 1);
    }

    [Fact]
    public async Task InternalAsyncLegacyLobOperationsReportFetchSuccessWithoutTransportError()
    {
        await using var fixture = new OutputLobFixture();
        var blob = fixture.Blob(2);
        fixture.Channel.AddData([9, 8], -1, true);
        byte[] encoded = Encoding.UTF8.GetBytes("Z🙂");
        var clob = fixture.Clob(encoded.Length);
        fixture.Channel.AddData(encoded, encoded.Length, true);
        fixture.ReleaseAll();
        using var scope = new Activity("synthetic.r3.legacy.fetch.async").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var observer = new FetchObserver(scope.TraceId, scope.SpanId);
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        long successBaseline = observer.Counter("success");
        await blob.LoadAllDataUnderOwnerAsync(default);
        Assert.Equal(new byte[] { 9, 8 }, blob.GetBytes(0, 2));
        Assert.Equal("Z🙂", await clob.GetSubStringUnderOwnerAsync(0, 4, default));
        Assert.True(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, observer.Results.Count(value => value.Result == "success"));
        Assert.DoesNotContain(observer.Results, value => value.Result == "transport_error");
        Assert.True(observer.Counter("success") >= successBaseline + 2);
    }

    private sealed class FetchObserver : IDisposable
    {
        private readonly ActivityListener activity;
        private readonly MeterListener meter = new();
        private readonly object gate = new();
        private readonly List<(string Result, ActivityStatusCode Status)> results = [];
        private readonly Dictionary<string, long> cumulative = new(StringComparer.Ordinal);
        internal (string Result, ActivityStatusCode Status)[] Results { get { lock (gate) return results.ToArray(); } }
        internal FetchObserver(ActivityTraceId trace, ActivitySpanId parent)
        {
            activity = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DmDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = value =>
                {
                    if (value.OperationName != "wdm.fetch" || value.TraceId != trace || value.ParentSpanId != parent) return;
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
                if (operation == "fetch" && result != null) lock (gate) cumulative[result] = value;
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
}
