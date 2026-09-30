using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
#if OFFICIAL || RESTORED
using DriverConnection = Dm.DmConnection;
#else
using DriverConnection = W.Dm.DmConnection;
#endif

internal static partial class Program
{
    private sealed record ReadObservation(long? Value, object? Failure, long CompletionTick);
    private sealed record WriteObservation(bool Committed, int? Affected, object? Failure, long CompletionTick);

    private static IEnumerable<object> RunLitmus(string raw, string table, bool profileEntry)
    {
        yield return Visibility(raw, table, 501, IsolationLevel.ReadCommitted, profileEntry);
        yield return Visibility(raw, table, 502, IsolationLevel.ReadUncommitted, profileEntry);
        yield return SerializableRepeat(raw, table, 503, profileEntry);
    }

    private static object Visibility(string raw, string table, int id, IsolationLevel level, bool profileEntry)
    {
        using (var seed = OpenVerified(raw)) Execute(seed, null, $"INSERT INTO {table}(ID,VAL) VALUES ({id},0)");
        using var writer = OpenVerified(raw);
        using var observer = OpenVerified(raw);
        using var writeTransaction = Begin(writer, IsolationLevel.ReadCommitted, candidate: profileEntry);
        using var readTransaction = Begin(observer, level, candidate: profileEntry);
        var snapshots = new List<object>();
        using var command = Command(observer, readTransaction, $"SELECT VAL FROM {table} WHERE ID={id}", null, false, snapshots);
        command.CommandTimeout = 10;
        Task<ReadObservation>? pending = null;
        ReadObservation? first = null;
        object? failure = null;
        bool writeEnded = false, readEnded = false, joined = false, beforeCommit = false;
        long? second = null, final = null;
        long? writerCommitStartTick = null;
        object? sentIdentity = null;
        try
        {
            command.Prepare();
            snapshots.Add(new { phase = "after_prepare", snapshot = ProbeSafety.Snapshot(command, readTransaction) });
            // A's completed ExecuteNonQuery is the acknowledgement that its write exists.
            Execute(writer, writeTransaction, $"UPDATE {table} SET VAL=1 WHERE ID={id}");
            using (var sent = new SentBarrier(observer))
            {
                pending = Task.Run(() => CaptureRead(command));
                if (!sent.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("observer_execute_send_not_observed");
                sentIdentity = sent.Identity;
                beforeCommit = pending.Wait(TimeSpan.FromSeconds(2));
                if (beforeCommit) first = pending.GetAwaiter().GetResult();
                writerCommitStartTick = Stopwatch.GetTimestamp();
                writeTransaction.Commit(); writeEnded = true;
                if (!pending.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("observer_not_completed_after_commit");
                joined = true;
                first ??= pending.GetAwaiter().GetResult();
                beforeCommit = first.CompletionTick < writerCommitStartTick;
            }
            second = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            snapshots.Add(new { phase = "after_committed_reread", snapshot = ProbeSafety.Snapshot(command, readTransaction) });
            readTransaction.Commit(); readEnded = true;
        }
        catch (Exception ex) { failure = ProbeSafety.Failure(ex); }
        finally
        {
            if (!writeEnded) TryRollback(writeTransaction);
            if (!readEnded) TryRollback(readTransaction);
            if (pending != null && !joined) joined = JoinOrClose(pending, observer);
            observer.Close(); writer.Close();
        }
        try { using var fresh = OpenVerified(raw); final = Convert.ToInt64(Scalar(fresh, $"SELECT VAL FROM {table} WHERE ID={id}"), CultureInfo.InvariantCulture); }
        catch (Exception ex) { failure ??= ProbeSafety.Failure(ex); }
        bool returnedCorrectly = first?.Failure == null && first?.Value is 0 or 1;
        bool visibilityCorrect = returnedCorrectly && (!beforeCommit || level != IsolationLevel.ReadCommitted || first?.Value == 0);
        return new { isolation = level.ToString(), entry = profileEntry ? "internal_profile" : "public", shape = "uncommitted_then_committed_read", functional_contract =
                failure == null && joined && writeEnded && readEnded && visibilityCorrect && second == 1 && final == 1,
            scope = "tested_SERVER_8_1_5_60_statement_schedule_only", observer_execute_sent = sentIdentity != null,
            sent_operation = sentIdentity, first_completed_before_writer_commit = beforeCommit,
            precommit_strategy = beforeCommit ? "returned_before_commit" : "awaited_commit_within_deadline",
            first_read = first, writer_commit_start_tick = writerCommitStartTick, committed_reread = second, fresh_final_value = final,
            writer_commit_confirmed = writeEnded, observer_end_confirmed = readEnded, worker_joined = joined,
            failure, snapshots };
    }

    private static object SerializableRepeat(string raw, string table, int id, bool profileEntry)
    {
        using (var seed = OpenVerified(raw)) Execute(seed, null, $"INSERT INTO {table}(ID,VAL) VALUES ({id},0)");
        using var observer = OpenVerified(raw);
        using var writer = OpenVerified(raw);
        using var readTransaction = Begin(observer, IsolationLevel.Serializable, candidate: profileEntry);
        using var writeTransaction = Begin(writer, IsolationLevel.ReadCommitted, candidate: profileEntry);
        var snapshots = new List<object>();
        using var read = Command(observer, readTransaction, $"SELECT VAL FROM {table} WHERE ID={id}", null, false, snapshots);
        using var write = Command(writer, writeTransaction, $"UPDATE {table} SET VAL=1 WHERE ID={id}", null, false, snapshots);
        read.CommandTimeout = 5; write.CommandTimeout = 15;
        Task<WriteObservation>? pending = null;
        WriteObservation? written = null;
        object? failure = null, sentIdentity = null;
        long? first = null, second = null, final = null;
        long? readerEndStartTick = null;
        bool writerCompletedBeforeRepeat = false;
        bool completedBeforeReaderEnd = false, readerEnded = false, joined = false;
        try
        {
            // Prepare A on A's connection before observing the actual execute send.
            write.Prepare();
            snapshots.Add(new { phase = "writer_after_prepare", snapshot = ProbeSafety.Snapshot(write, writeTransaction) });
            first = Convert.ToInt64(read.ExecuteScalar(), CultureInfo.InvariantCulture);
            snapshots.Add(new { phase = "after_first_read", snapshot = ProbeSafety.Snapshot(read, readTransaction) });
            using (var sent = new SentBarrier(writer))
            {
                pending = Task.Run(() => CaptureWrite(write, writeTransaction));
                if (!sent.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("writer_execute_send_not_observed");
                sentIdentity = sent.Identity;
                completedBeforeReaderEnd = pending.Wait(TimeSpan.FromSeconds(2));
                if (completedBeforeReaderEnd) written = pending.GetAwaiter().GetResult();
                writerCompletedBeforeRepeat = pending.IsCompleted;
                if (writerCompletedBeforeRepeat) written ??= pending.GetAwaiter().GetResult();
                second = Convert.ToInt64(read.ExecuteScalar(), CultureInfo.InvariantCulture);
                snapshots.Add(new { phase = "before_reader_end", snapshot = ProbeSafety.Snapshot(read, readTransaction) });
                readerEndStartTick = Stopwatch.GetTimestamp();
                readTransaction.Commit(); readerEnded = true;
                if (!pending.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("writer_not_completed_after_reader_end");
                written ??= pending.GetAwaiter().GetResult(); joined = true;
                completedBeforeReaderEnd = written.CompletionTick < readerEndStartTick;
            }
        }
        catch (Exception ex) { failure = ProbeSafety.Failure(ex); }
        finally
        {
            if (!readerEnded) TryRollback(readTransaction); // Release a locking reader before waiting for A.
            observer.Close();
            if (pending != null && !joined)
            {
                joined = JoinOrClose(pending, writer);
                if (joined) written ??= pending.GetAwaiter().GetResult();
            }
            if (written?.Committed != true) TryRollback(writeTransaction);
            writer.Close();
        }
        try { using var fresh = OpenVerified(raw); final = Convert.ToInt64(Scalar(fresh, $"SELECT VAL FROM {table} WHERE ID={id}"), CultureInfo.InvariantCulture); }
        catch (Exception ex) { failure ??= ProbeSafety.Failure(ex); }
        return new { isolation = IsolationLevel.Serializable.ToString(), entry = profileEntry ? "internal_profile" : "public", shape = "repeat_read_vs_concurrent_update",
            functional_contract = failure == null && joined && readerEnded && first == 0 && second == 0 &&
                written?.Committed == true && written.Affected == 1 && written.Failure == null && final == 1 &&
                (writerCompletedBeforeRepeat || written.CompletionTick >= readerEndStartTick),
            scope = "tested_SERVER_8_1_5_60_statement_schedule_only", writer_execute_sent = sentIdentity != null,
            sent_operation = sentIdentity, writer_completed_before_reader_end = completedBeforeReaderEnd,
            writer_completed_before_repeat_read = writerCompletedBeforeRepeat, reader_end_start_tick = readerEndStartTick,
            strategy = written?.Failure != null ? "writer_failure_requires_separate_profile_review" :
                writerCompletedBeforeRepeat ? "committed_writer_with_repeatable_snapshot" :
                written?.CompletionTick >= readerEndStartTick ? "writer_completed_after_reader_end_attempt" : "ambiguous_completion_requires_review",
            first_read = first, repeat_read = second, writer = written, reader_end_confirmed = readerEnded,
            worker_joined = joined, fresh_final_value = final, failure, snapshots };
    }

    private static ReadObservation CaptureRead(DbCommand command)
    {
        try { return new(Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture), null, Stopwatch.GetTimestamp()); }
        catch (Exception ex) { return new(null, ProbeSafety.Failure(ex), Stopwatch.GetTimestamp()); }
    }
    private static WriteObservation CaptureWrite(DbCommand command, DbTransaction transaction)
    {
        int? affected = null;
        try { affected = command.ExecuteNonQuery(); transaction.Commit(); return new(true, affected, null, Stopwatch.GetTimestamp()); }
        catch (Exception ex) { return new(false, affected, ProbeSafety.Failure(ex), Stopwatch.GetTimestamp()); }
    }
    private static void TryRollback(DbTransaction transaction)
    { try { transaction.Rollback(); } catch { /* The final row and confirmed-end flags are still required. */ } }
    private static bool JoinOrClose(Task pending, DbConnection capturedConnection)
    {
        if (pending.Wait(TimeSpan.FromSeconds(2))) return true;
        capturedConnection.Close();
        return pending.Wait(TimeSpan.FromSeconds(5));
    }

    private sealed class SentBarrier : IDisposable
    {
        private readonly ManualResetEventSlim sent = new();
        private readonly object gate = new();
        private bool disposed;
        internal object? Identity { get; private set; }
        internal SentBarrier(DriverConnection connection)
        {
#if CANDIDATE
            long session = connection.Session?.SessionId ?? throw new InvalidOperationException("sender_session_unavailable");
            W.Dm.Internal.Sessions.OperationIdentity? expected = null;
            W.Dm.Internal.Legacy.A.DmWireTestHooks.AfterExchangeEntered = (identity, _) =>
            {
                lock (gate) if (!disposed && identity.SessionId == session && expected == null) expected = identity;
            };
            W.Dm.Internal.Legacy.A.DmWireTestHooks.AfterSendBeforeReceive = (identity, _) =>
            {
                lock (gate)
                {
                    if (disposed || expected != identity || identity.SessionId != session) return;
                    Identity = new { session_id = identity.SessionId, lease_generation = identity.LeaseGeneration,
                        execution_id = identity.ExecutionId, invocation_id = identity.InvocationId };
                    sent.Set();
                }
            };
#else
            throw new InvalidOperationException("litmus_requires_candidate_binary");
#endif
        }
        internal bool Wait(TimeSpan bound) => sent.Wait(bound);
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
#if CANDIDATE
                W.Dm.Internal.Legacy.A.DmWireTestHooks.AfterExchangeEntered = null;
                W.Dm.Internal.Legacy.A.DmWireTestHooks.AfterSendBeforeReceive = null;
#endif
                sent.Dispose(); // Captured late callbacks see disposed under the same gate.
            }
        }
    }
}
