using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using W.Dm;

internal static partial class Program
{
    private sealed record PerformanceResult(int Connections, bool AllPendingAfterNumericSend, int BusyWorkerGrowth,
        int ThreadCountBefore, int ThreadCountWaiting, int AvailableWorkersBefore, int AvailableWorkersWaiting,
        long ManagedAllocatedBytes, double WaitingElapsedMilliseconds, double CompletionElapsedMilliseconds,
        double CompletedOperationsPerSecond, int ExecutionFrames, int UnknownCanceledOperations, int FreshNonzeroRows);

    private static async Task PerformanceAsync(int count)
    {
        Stage = "performance_" + count;
        await CheckpointAsync("performance_before_owner", new { target_waiters = count, opened_waiters = 0, closed_connections = 0 }).ConfigureAwait(false);
        await using var owner = await OpenAsync().ConfigureAwait(false);
        await using var ownerTx = await BeginAsync(owner).ConfigureAwait(false);
        await ExecAsync(owner, ownerTx, $"UPDATE {Table} SET VAL=1 WHERE ID BETWEEN 3 AND {count + 2}").ConfigureAwait(false);
        var connections = new List<DmConnection>(); var transactions = new List<DmTransaction>(); var commands = new List<DmCommand>();
        var sessions = new HashSet<long>(); var pending = new List<Task<int>>();
        var sent = new HashSet<long>(); object gate = new();
        var allSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int prepared = 0, observedCancellations = 0, verifiedConnections = 0;
        using var cancel = new CancellationTokenSource();
        Task Progress(string phase, int? canceled = null, int? verified = null)
        {
            if (canceled is int observed) observedCancellations = observed;
            if (verified is int confirmed) verifiedConnections = confirmed;
            int numericSent; lock (gate) numericSent = sent.Count;
            return CheckpointAsync(phase, new { target_waiters = count, opened_waiters = connections.Count, prepared_commands = prepared,
                numeric_sent_waiters = numericSent, pending_tasks = pending.Count(task => !task.IsCompleted),
                broken_connections = connections.Count(c => c.State == ConnectionState.Broken),
                closed_connections = connections.Count(c => c.State == ConnectionState.Closed),
                observed_cancellations = observedCancellations, verified_fresh_connections = verifiedConnections });
        }
        try
        {
            for (int index = 0; index < count; index++)
            {
                await Progress("performance_setup_open").ConfigureAwait(false);
                var c = await OpenAsync().ConfigureAwait(false); connections.Add(c); sessions.Add(Session(c));
                var tx = await BeginAsync(c).ConfigureAwait(false); transactions.Add(tx);
                var q = Command(c, tx, $"UPDATE {Table} SET VAL=:val WHERE ID={index + 3}"); commands.Add(q);
                q.Parameters.Add(new DmParameter("val", DmDbType.Int32) { Direction = ParameterDirection.Input, Value = 2 });
                await Progress("performance_setup_prepare").ConfigureAwait(false);
                await q.PrepareAsync().ConfigureAwait(false); prepared++;
                await Progress("performance_setup_ready").ConfigureAwait(false);
            }
            await Progress("performance_all_prepared").ConfigureAwait(false);
            int initialExecutions = sessions.Sum(id => Observation!.Executions(id));
            var opcodes = new ConcurrentQueue<short>();
            Report["performance_send_diagnostics"] = new { connections = count, baseline_execution_frames = initialExecutions, sent_opcodes = opcodes };
            ThreadPool.GetAvailableThreads(out int availableBefore, out _); int threadsBefore = ThreadPool.ThreadCount;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var clock = Stopwatch.StartNew();
            Observation!.OnSent = frame => {
                if (!sessions.Contains(frame.SessionId)) return;
                opcodes.Enqueue(frame.Opcode);
                if (frame.Opcode is not (6 or 13)) return;
                lock (gate) { sent.Add(frame.SessionId); if (sent.Count == count) allSent.TrySetResult(); }
            };
            foreach (var q in commands) pending.Add(q.ExecuteNonQueryAsync(cancel.Token));
            await Progress("performance_waiting_for_send_gate").ConfigureAwait(false);
            await allSent.Task.WaitAsync(Safety).ConfigureAwait(false);
            Need(pending.Count == count && pending.All(task => !task.IsCompleted), "performance_not_all_pending_after_send");
            await Progress("performance_all_pending").ConfigureAwait(false);
            int minAvailable = availableBefore, maxThreads = threadsBefore;
            // The lock and numeric send barrier establish real pending I/O. This
            // bounded interval samples the pool; it is not the pending proof.
            for (int sample = 0; sample < 3; sample++)
            {
                ThreadPool.GetAvailableThreads(out int available, out _); minAvailable = Math.Min(minAvailable, available);
                maxThreads = Math.Max(maxThreads, ThreadPool.ThreadCount);
                Need(pending.All(task => !task.IsCompleted), "performance_lock_released_early");
                if (sample < 2) await Task.Delay(125).ConfigureAwait(false);
            }
            double waitingMs = clock.Elapsed.TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            int growth = Math.Max(0, availableBefore - minAvailable);
            Need(growth < count, "performance_each_waiter_occupied_worker");
            await Progress("performance_before_cancellation").ConfigureAwait(false);
            cancel.Cancel(); int unknown = 0;
            foreach (var task in pending)
            {
                var error = await ErrorAsync(() => task).ConfigureAwait(false);
                Need(error.GetType() == typeof(DmOperationCanceledException) && Failure(error) is {
                    CancelSource: DmCancelSource.User, OperationOutcome: DmOperationOutcome.Unknown, ConnectionReusable: false },
                    "performance_cancellation_classification_invalid");
                unknown++;
                await Progress("performance_cancellation_observed", unknown).ConfigureAwait(false);
            }
            double completionMs = clock.Elapsed.TotalMilliseconds;
            int executions = sessions.Sum(id => Observation.Executions(id)) - initialExecutions;
            Need(executions == count && connections.All(c => c.State == ConnectionState.Broken) &&
                transactions.All(tx => tx.Outcome == DmTransactionOutcome.OutcomeUnknown), "performance_replay_or_state_invalid");
            Observation.OnSent = null;
            await Progress("performance_before_owner_release", unknown).ConfigureAwait(false);
            await ownerTx.RollbackAsync().ConfigureAwait(false);
            await Progress("performance_fresh_readback", unknown).ConfigureAwait(false);
            int verified = 0;
            for (int id = 3; id <= count + 2; id++)
            {
                await FreshZeroAsync(id).ConfigureAwait(false); verified++;
                await Progress("performance_fresh_readback", unknown, verified).ConfigureAwait(false);
            }
            Cases[Stage] = new PerformanceResult(count, true, growth, threadsBefore, maxThreads, availableBefore, minAvailable,
                allocated, waitingMs, completionMs, count / (completionMs / 1000), executions, unknown, 0);
        }
        finally
        {
            Observation!.OnSent = null; cancel.Cancel();
            await Progress("performance_finally_closing").ConfigureAwait(false);
            foreach (var c in connections) if (c.State is not (ConnectionState.Broken or ConnectionState.Closed)) await c.CloseAsync().ConfigureAwait(false);
            foreach (var task in pending) if (!task.IsCompleted) await ErrorAsync(() => task).ConfigureAwait(false);
            foreach (var q in commands) await q.DisposeAsync().ConfigureAwait(false);
            foreach (var tx in transactions) await tx.DisposeAsync().ConfigureAwait(false);
            foreach (var c in connections)
            {
                await c.DisposeAsync().ConfigureAwait(false);
                await Progress("performance_finally_closed").ConfigureAwait(false);
            }
        }
    }
}
