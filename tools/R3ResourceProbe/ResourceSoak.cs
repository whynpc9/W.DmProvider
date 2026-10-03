using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Transport;

internal static partial class Program
{
    private static readonly object ResourceBudget = new
    {
        declared_before_measurement = true, warmup_seconds = 60, measurement_seconds = 600, minimum_completed_cycles = 500,
        maximum_concurrency = 4, sample_interval_seconds = 5, minimum_measurement_samples = 121,
        quiescent_seconds = 30, minimum_quiescent_samples = 3, total_process_seconds = 900, force_gc = false,
        fixed_blob_bytes = 40960, rss_growth_bytes = 256L * 1024 * 1024, gc_heap_growth_bytes = 128L * 1024 * 1024,
        thread_growth = 16, fd_growth = 16, quiescent_fd_growth = 8,
        window_seconds = 60, rss_half_window_median_growth_bytes = 64L * 1024 * 1024,
        gc_heap_half_window_median_growth_bytes = 32L * 1024 * 1024,
        second_half_thread_growth_per_100_cycles = 1.0, second_half_fd_growth_per_100_cycles = 1.0,
        scope = "TLS_resource_soak_not_T17_large_value_or_full_R3_matrix"
    };
    private static long Completed, Commits, Rollbacks, MeasuredCycles;
    private static int ActiveCycles, PeakConcurrency;
    private static Exception? FirstWorkerFailure;
    private static string ExpectedServerVersion = "";
    private static readonly List<Sample> Samples = [];
    private static readonly byte[] Payload = Enumerable.Range(0, 40960).Select(i => (byte)((17 + 131 * i) & 255)).ToArray();
    private static async Task RunSoakAsync()
    {
        using var listener = new DiagnosticListenerCapture();
        Table = "T18R_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(); Report["owned_objects"] = new[] { Table };
        bool attempted = false; Exception? work = null, cleanup = null;
        var initial = DmDiagnosticsCore.Snapshot; var snapshotTransportCreated = DmTransportTestHooks.CreatedTcpSockets;
        var snapshotTransportClosed = DmTransportTestHooks.DisposedTcpSockets;
        using var network = new NetworkObservation();
        await using var source = new DmDataSource(Settings);
        try
        {
            Stage = "create_unique_resource_fixture";
            await using (var setup = await OpenVerifiedAsync(source, recordProfile: true))
            {
                Require(await ObjectCountAsync(setup) == 0, "unique_resource_object_already_exists"); attempted = true;
                await ExecuteAsync(setup, $"CREATE TABLE {Table}(ID INT PRIMARY KEY,B BLOB)");
                for (int index = 0; index < 4; index++) await ExecuteAsync(setup, $"INSERT INTO {Table}(ID) VALUES({index})");
            }
            Stage = "real_wait_cancel_clear_dispose_control"; await WaitClearDisposeControlAsync();
            Stage = "fixed_warmup_and_measurement";
            Stopwatch clock = Stopwatch.StartNew(); using var stop = new CancellationTokenSource();
            Task[] workers = Enumerable.Range(0, 4).Select(id => WorkerAsync(id, source, clock, stop)).ToArray();
            try
            {
                await DelayUntilAsync(clock, 60, workers); long baselineCycles = Interlocked.Read(ref Completed);
                Sample baseline = CaptureSample(clock.Elapsed.TotalSeconds, "baseline", source); Samples.Add(baseline);
                Report["measurement_baseline"] = baseline;
                for (int index = 0; index <= 120; index++)
                {
                    await DelayUntilAsync(clock, 60 + index * 5, workers);
                    ThrowIfWorkerFailed(); listener.ReadObservableInstruments();
                    Samples.Add(CaptureSample(clock.Elapsed.TotalSeconds, "measurement", source));
                    Report["samples"] = Samples; Checkpoint();
                }
                MeasuredCycles = Interlocked.Read(ref Completed) - baselineCycles;
            }
            finally { stop.Cancel(); await Task.WhenAll(workers); }
            ThrowIfWorkerFailed();
            Stage = "fixed_quiescent_sampling";
            Require(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)), "diagnostic_export_did_not_drain");
            await Task.Delay(TimeSpan.FromSeconds(30));
            for (int index = 0; index < 3; index++)
            {
                if (index != 0) await Task.Delay(TimeSpan.FromSeconds(5));
                listener.ReadObservableInstruments(); Samples.Add(CaptureSample(clock.Elapsed.TotalSeconds, "quiescent", source));
                Report["samples"] = Samples; Checkpoint();
            }
            Report["diagnostic_listener"] = listener.Snapshot;
            Require(!listener.InvalidTag && !listener.ContextLeaked && listener.ActivityCount > 0 && listener.MeasurementCount > 0, "public_diagnostics_invalid_or_unobserved");
            Require(MeasuredCycles >= 500 && PeakConcurrency <= 4 && Samples.Count(s => s.Phase == "measurement") >= 121 &&
                Samples.Count(s => s.Phase == "quiescent") >= 3, "fixed_resource_work_or_sample_budget_not_met");
        }
        catch (Exception error) { work = error; Report["work_failure"] = DmDiagnostics.FormatException(error); Report["primary_failed_stage"] = Stage; }
        finally
        {
            Stage = "fresh_unique_resource_cleanup";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync(source)) if (attempted && await ObjectCountAsync(cleaner) == 1) await ExecuteAsync(cleaner, "DROP TABLE " + Table);
                await using (var fresh = await OpenVerifiedAsync(source)) Require(await ObjectCountAsync(fresh) == 0, "unique_resource_absence_failed");
                Report["cleanup_verified"] = true; Report["final_database_state"] = "unique_owned_objects_absent";
                Report["final_verification_identity"] = User; Report["final_verification_schema"] = User;
            }
            catch (Exception error) { cleanup = error; Report["cleanup_failure"] = DmDiagnostics.FormatException(error); }
        }
        bool drained = await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10));
        var final = DmDiagnosticsCore.Snapshot;
        listener.ReadObservableInstruments(); Report["final_public_diagnostics"] = listener.Snapshot;
        Report["completed_cycles"] = Completed; Report["measurement_completed_cycles"] = MeasuredCycles;
        Report["confirmed_commits"] = Commits; Report["confirmed_rollbacks"] = Rollbacks; Report["peak_concurrency"] = PeakConcurrency;
        Report["final_diagnostics_snapshot"] = final;
        Report["physical_connections_created"] = DmTransportTestHooks.CreatedTcpSockets - snapshotTransportCreated;
        Report["physical_connections_disposed"] = DmTransportTestHooks.DisposedTcpSockets - snapshotTransportClosed;
        Report["diagnostic_created_delta"] = final.ConnectionsCreated - initial.ConnectionsCreated;
        Report["diagnostic_closed_delta"] = final.ConnectionsClosed - initial.ConnectionsClosed;
        Report["dropped_delta"] = final.Dropped - initial.Dropped; Report["network_io_counts"] = network.Counts;
        Exception? audit = null;
        try
        {
            Require(drained, "final_diagnostics_not_drained");
            Require(final.PoolCreating == 0 && final.PoolLeased == 0 && final.PoolClosing == 0 && final.PoolWaiting == 0 &&
                final.QueueDepth == 0 && final.QueueCapacity == 4096 && final.QueuePeak <= 4096 && final.RegistryEntries <= 128, "final_resource_state_not_zero_or_bounded");
            Require(final.Dropped == initial.Dropped && final.CallbackFailures == initial.CallbackFailures, "healthy_listener_dropped_or_failed");
            Require(listener.Total("wdm.connection.created") == final.ConnectionsCreated && listener.Total("wdm.connection.closed") == final.ConnectionsClosed &&
                listener.Total("wdm.pool.acquire.total") == final.Acquires && listener.Total("wdm.operation.total") == final.Operations &&
                listener.Total("wdm.network.bytes", "direction=sent") == final.SentBytes && listener.Total("wdm.network.bytes", "direction=received") == final.ReceivedBytes &&
                listener.Total("wdm.lob.chunks", "direction=read") == final.LobReadChunks && listener.Total("wdm.lob.chunks", "direction=write") == final.LobWriteChunks,
                "public_cumulative_bucket_counts_do_not_match_atomic_state");
            Require(DmTransportTestHooks.CreatedTcpSockets - snapshotTransportCreated == DmTransportTestHooks.DisposedTcpSockets - snapshotTransportClosed &&
                final.ConnectionsCreated - initial.ConnectionsCreated == final.ConnectionsClosed - initial.ConnectionsClosed && network.AsyncOnly, "physical_or_async_resource_balance_failed");
        }
        catch (Exception error) { audit = error; Report["secondary_failure"] = DmDiagnostics.FormatException(error); }
        if (work != null) throw work; if (cleanup != null) throw cleanup;
        if (audit != null) throw audit;
        // Budget arithmetic/trends are independently validated from the raw numeric samples by validate.py.
        Report["resource_measurement_complete"] = true;
    }
    private static async Task WorkerAsync(int id, DmDataSource source, Stopwatch clock, CancellationTokenSource stop)
    {
        while (!stop.IsCancellationRequested && clock.Elapsed.TotalSeconds < 660)
        {
            int active = Interlocked.Increment(ref ActiveCycles), previous;
            while (active > (previous = Volatile.Read(ref PeakConcurrency)) && Interlocked.CompareExchange(ref PeakConcurrency, active, previous) != previous) { }
            try
            {
                await using var connection = await OpenVerifiedAsync(source);
                await using var transaction = (DmTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                long cycle = Interlocked.Read(ref Completed);
                await using (var update = new DmCommand($"UPDATE {Table} SET B=:value WHERE ID={id}", connection) { CommandTimeout = 15, Transaction = transaction })
                {
                    using var input = new MemoryStream(Payload, writable: false);
                    update.Parameters.Add(new DmParameter("value", DmDbType.Blob) { Value = cycle % 2 == 0 ? input : Payload });
                    Require(await update.ExecuteNonQueryAsync() == 1, "resource_cycle_update_failed");
                }
                await using (var query = new DmCommand($"SELECT B FROM {Table} WHERE ID={id}", connection) { CommandTimeout = 15, Transaction = transaction })
                await using (var reader = await query.ExecuteReaderAsync())
                {
                    Require(await reader.ReadAsync(), "resource_cycle_row_missing"); await using var stream = reader.GetStream(0);
                    byte[] buffer = new byte[8191]; int total = 0, count;
                    while ((count = await stream.ReadAsync(buffer)) != 0)
                    { Require(Payload.AsSpan(total, count).SequenceEqual(buffer.AsSpan(0, count)), "resource_cycle_blob_mismatch"); total += count; }
                    Require(total == Payload.Length, "resource_cycle_blob_length_failed");
                }
                if (cycle % 4 == 0) { await transaction.RollbackAsync(); Require(transaction.Outcome == DmTransactionOutcome.RolledBack, "resource_cycle_rollback_unconfirmed"); Interlocked.Increment(ref Rollbacks); }
                else { await transaction.CommitAsync(); Require(transaction.Outcome == DmTransactionOutcome.Committed, "resource_cycle_commit_unconfirmed"); Interlocked.Increment(ref Commits); }
                if (cycle % 31 == 0) source.ClearPool();
                await connection.CloseAsync(); Interlocked.Increment(ref Completed);
            }
            catch (Exception error) { Interlocked.CompareExchange(ref FirstWorkerFailure, error, null); stop.Cancel(); return; }
            finally { Interlocked.Decrement(ref ActiveCycles); }
        }
    }
    private static void ThrowIfWorkerFailed() { if (FirstWorkerFailure != null) throw FirstWorkerFailure; }
    private static async Task DelayUntilAsync(Stopwatch clock, double seconds, Task[] workers)
    {
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            ThrowIfWorkerFailed(); if (workers.Any(t => t.IsFaulted)) await Task.WhenAll(workers);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, seconds - clock.Elapsed.TotalSeconds)));
        }
    }
    private static Sample CaptureSample(double elapsed, string phase, DmDataSource source)
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        long? rss = Try(() => process.WorkingSet64), threads = Try(() => (long)process.Threads.Count), handles = Try(() => (long)process.HandleCount);
        long? fds = null;
        try { string path = OperatingSystem.IsLinux() ? "/proc/self/fd" : OperatingSystem.IsMacOS() ? "/dev/fd" : ""; if (path.Length != 0) fds = Directory.EnumerateFileSystemEntries(path).LongCount(); } catch { }
        var d = DmDiagnosticsCore.Snapshot; var p = source.Snapshot;
        return new Sample(elapsed, phase, Interlocked.Read(ref Completed), rss, GC.GetGCMemoryInfo().HeapSizeBytes,
            GC.GetTotalAllocatedBytes(false), threads, fds, handles, p.Creating, p.Leased, p.Closing, p.Waiting, d.RegistryEntries,
            d.QueueDepth, d.QueuePeak, d.ConnectionsCreated, d.ConnectionsClosed, d.SentBytes, d.ReceivedBytes, d.LobReadChunks, d.LobWriteChunks);
    }
    private static long? Try(Func<long> read) { try { return read(); } catch { return null; } }
    private sealed record Sample(double ElapsedSeconds, string Phase, long CompletedCycles, long? RssBytes, long GcHeapBytes, long AllocatedBytes,
        long? Threads, long? FileDescriptors, long? Handles, int PoolCreating, int PoolLeased, int PoolClosing, int PoolWaiting,
        long RegistryEntries, int QueueDepth, int QueuePeak, long ConnectionsCreated, long ConnectionsClosed,
        long SentBytes, long ReceivedBytes, long LobReadChunks, long LobWriteChunks);
    private static async Task WaitClearDisposeControlAsync()
    {
        var b = new DmConnectionStringBuilder(Settings) { MaxPoolSize = 1 }; await using var source = new DmDataSource(b.ConnectionString);
        await using var held = await OpenVerifiedAsync(source); await using var waiting = source.CreateConnection();
        using var cancel = new CancellationTokenSource(); long created = DmTransportTestHooks.CreatedTcpSockets;
        Task pending = waiting.OpenAsync(cancel.Token); Require(source.Snapshot.Waiting == 1, "resource_control_not_waiting"); cancel.Cancel();
        try { await pending; throw new ProbeFailure("resource_wait_cancel_missing"); }
        catch (DmOperationCanceledException error) { Require(error.CancellationToken == cancel.Token && error.FailureInfo.OperationOutcome == DmOperationOutcome.NotSent, "resource_wait_cancel_wrong_identity"); }
        Require(DmTransportTestHooks.CreatedTcpSockets == created, "resource_wait_cancel_created_socket"); source.ClearPool();
        await using var stoppedWaiter = source.CreateConnection(); Task stopped = stoppedWaiter.OpenAsync(); Require(source.Snapshot.Waiting == 1, "resource_dispose_control_not_waiting");
        await source.DisposeAsync();
        try { await stopped; throw new ProbeFailure("resource_dispose_wait_missing"); } catch (ObjectDisposedException) { }
        await VerifyIdentityAsync(held); await held.CloseAsync(); Require(source.Snapshot.IsQuiescent, "resource_control_pool_leak");
        Report["wait_clear_dispose_control"] = new { actual_wait_cancel_not_sent = true, clear_kept_capacity = true, dispose_wait_terminated = true, existing_lease_completed = true };
    }
    private static async Task<DmConnection> OpenVerifiedAsync(DmDataSource source, bool recordProfile = false)
    { var c = source.CreateConnection(); try { await c.OpenAsync(); await VerifyIdentityAsync(c, recordProfile); return c; } catch { await c.DisposeAsync(); throw; } }
    private static async Task VerifyIdentityAsync(DmConnection c, bool recordProfile = false)
    {
        await using var command = new DmCommand("SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL", c) { CommandTimeout = 15 };
        await using var reader = await command.ExecuteReaderAsync(); Require(await reader.ReadAsync() && reader.GetString(0) == User && reader.GetString(1) == User, "real_resource_identity_or_schema_mismatch");
        Require(Regex.IsMatch(c.ServerVersion, @"^\d+(\.\d+){1,5}$"), "numeric_resource_server_version_required");
        if (ExpectedServerVersion.Length != 0) Require(c.ServerVersion == ExpectedServerVersion, "resource_server_profile_changed");
        if (recordProfile)
        {
            ExpectedServerVersion = c.ServerVersion;
            Report["server_identity"] = User; Report["server_schema"] = User; Report["server_version"] = c.ServerVersion;
        }
    }
    private static async Task ExecuteAsync(DmConnection c, string sql)
    { await using var command = new DmCommand(sql, c) { CommandTimeout = 15 }; await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ObjectCountAsync(DmConnection c)
    { await using var command = new DmCommand("SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0", c) { CommandTimeout = 15 }; command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = Table }); return Convert.ToInt32(await command.ExecuteScalarAsync()); }
    private sealed class NetworkObservation : IDisposable
    {
        private readonly FieldInfo hook = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!.GetField("BeforeNetworkIo", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly Dictionary<string, long> counts = new(); private readonly object gate = new(); private bool unknown;
        internal NetworkObservation()
        {
            Require(hook.GetValue(null) == null, "isolated_network_observer_required");
            foreach (string operation in new[] { "connect", "tls", "send", "receive" }) { counts["async_" + operation] = 0; counts["sync_" + operation] = 0; }
            hook.SetValue(null, (Action<bool, string>)((asynchronous, operation) => { lock (gate) { string key = (asynchronous ? "async_" : "sync_") + operation; if (counts.ContainsKey(key)) counts[key]++; else unknown = true; } }));
        }
        internal Dictionary<string, long> Counts { get { lock (gate) return new(counts); } }
        internal bool AsyncOnly { get { lock (gate) return !unknown && counts.Where(p => p.Key.StartsWith("sync_", StringComparison.Ordinal)).All(p => p.Value == 0) &&
            counts["async_connect"] > 0 && counts["async_tls"] > 0 && counts["async_send"] > 0 && counts["async_receive"] > 0; } }
        public void Dispose() => hook.SetValue(null, null);
    }
}
