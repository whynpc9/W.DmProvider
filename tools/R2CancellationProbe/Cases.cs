using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using W.Dm;

internal static partial class Program
{
    private static async Task PreCancelAsync()
    {
        Stage = "pre_cancel_and_idle_noop";
        await using var c = await OpenAsync().ConfigureAwait(false);
        await using var tx = await BeginAsync(c).ConfigureAwait(false);
        await using var q = Command(c, tx, $"UPDATE {Table} SET VAL=9 WHERE ID=1");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        long before = Observation!.SendCount;
        var commandError = await ErrorAsync(() => q.ExecuteNonQueryAsync(canceled.Token)).ConfigureAwait(false);
        var commitError = await ErrorAsync(() => tx.CommitAsync(canceled.Token)).ConfigureAwait(false);
        q.Cancel(); q.Cancel();
        long sent = Observation.SendCount - before;
        Need(commandError is OperationCanceledException a && a.CancellationToken == canceled.Token &&
            commitError is OperationCanceledException b && b.CancellationToken == canceled.Token && sent == 0 &&
            tx.Outcome == DmTransactionOutcome.Active && c.State == ConnectionState.Open, "pre_cancel_changed_state");
        Need(await ScalarIntAsync(c, tx, "SELECT 1 FROM DUAL").ConfigureAwait(false) == 1, "pre_cancel_not_reusable");
        await tx.RollbackAsync().ConfigureAwait(false);
        Cases[Stage] = new { command_error = Safe(commandError), commit_error = Safe(commitError), sends = sent,
            active_preserved = true, connection_reusable = true, no_active_cancel_noop = true, new_token_query = 1 };
    }

    private static async Task LockedAsync(string kind)
    {
        Stage = "rowlock_" + kind;
        await using var owner = await OpenAsync().ConfigureAwait(false);
        await using var ownerTx = await BeginAsync(owner).ConfigureAwait(false);
        await ExecAsync(owner, ownerTx, $"UPDATE {Table} SET VAL=1 WHERE ID=1").ConfigureAwait(false);
        await using var waiter = await OpenAsync().ConfigureAwait(false);
        await using var waiterTx = await BeginAsync(waiter).ConfigureAwait(false);
        await using var q = Command(waiter, waiterTx, $"UPDATE {Table} SET VAL=:val WHERE ID=1");
        q.Parameters.Add(new DmParameter("val", DmDbType.Int32) { Direction = ParameterDirection.Input, Value = 2 });
        await q.PrepareAsync().ConfigureAwait(false); q.CommandTimeout = kind == "deadline" ? 1 : 15;
        long session = Session(waiter), created = Observation!.TcpCreated;
        int executeBefore = Observation.Executions(session);
        var opcodes = new ConcurrentQueue<short>();
        Report["rowlock_send_diagnostics"] = new { stage = Stage, baseline_execution_frames = executeBefore, sent_opcodes = opcodes };
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Observation.OnSent = frame => {
            if (frame.SessionId != session) return;
            opcodes.Enqueue(frame.Opcode);
            if (frame.Opcode is 6 or 13) sent.TrySetResult();
        };
        using var user = new CancellationTokenSource();
        Task<int>? pending = null;
        Exception failure;
        try
        {
            pending = q.ExecuteNonQueryAsync(user.Token);
            await sent.Task.WaitAsync(Safety).ConfigureAwait(false);
            Need(!pending.IsCompleted, "rowlock_did_not_wait_after_send");
            if (kind == "user") user.Cancel();
            if (kind == "command") { q.Cancel(); q.Cancel(); }
            failure = await ErrorAsync(() => pending).ConfigureAwait(false);
        }
        finally { Observation.OnSent = null; if (pending is { IsCompleted: false }) { waiter.Close(); await ErrorAsync(() => pending).ConfigureAwait(false); } }
        Need(waiter.State == ConnectionState.Broken && waiterTx.Outcome == DmTransactionOutcome.OutcomeUnknown,
            "sent_cancel_must_break_unknown");
        var info = Failure(failure); Need(info != null && info.OperationOutcome == DmOperationOutcome.Unknown &&
            info.TransactionOutcome == DmTransactionOutcome.OutcomeUnknown && !info.ConnectionReusable && info.ServerErrorNumber == null,
            "sent_failure_metadata_invalid");
        Need(kind == "deadline" ? failure.GetType() == typeof(DmTimeoutException) && info!.ErrorKind == DmErrorKind.Timeout &&
            info.CancelSource == DmCancelSource.TotalDeadline && ((DmException)failure).Number == 0 && !((DmException)failure).IsTransient :
            failure.GetType() == typeof(DmOperationCanceledException) && info!.ErrorKind == DmErrorKind.Canceled &&
            info.CancelSource == (kind == "user" ? DmCancelSource.User : DmCancelSource.Command), "sent_failure_reason_invalid");
        if (kind == "user") Need(((OperationCanceledException)failure).CancellationToken == user.Token, "original_user_token_lost");
        if (kind == "command") Need(((OperationCanceledException)failure).CancellationToken.CanBeCanceled &&
            ((OperationCanceledException)failure).CancellationToken != user.Token, "command_owned_token_missing");
        int executions = Observation.Executions(session) - executeBefore;
        long reconnects = Observation.TcpCreated - created;
        Need(executions == 1 && reconnects == 0, "sent_execution_reconnect_or_replay");
        await ownerTx.RollbackAsync().ConfigureAwait(false);
        await FreshZeroAsync(1).ConfigureAwait(false);
        Cases[Stage] = new { pending_after_numeric_send = true, execution_frames = executions, automatic_reconnections = reconnects,
            baseline_execution_frames = executeBefore, sent_opcodes = opcodes.ToArray(),
            connection_state = waiter.State.ToString(), transaction_outcome = waiterTx.Outcome.ToString(), failure = Safe(failure),
            fresh_final_value = 0, token_contract_verified = true, server_immediate_stop_claimed = false };
    }

    private static async Task ReaderTokensAsync()
    {
        Stage = "returned_reader_token_scope";
        await using var c = await OpenAsync().ConfigureAwait(false);
        await using var q = Command(c, null, $"SELECT ID,PAYLOAD FROM {Table} WHERE ID>1000 ORDER BY ID");
        using var old = new CancellationTokenSource(); using var next = new CancellationTokenSource();
        await using var r = await q.ExecuteReaderAsync(old.Token).ConfigureAwait(false);
        old.Cancel(); int before = Observation!.Responses.Count(op => op == 7), rows = 0;
        Need(await r.ReadAsync(next.Token).ConfigureAwait(false) && Observation.Responses.Count(op => op == 7) == before &&
            await r.GetFieldValueAsync<int>(0, next.Token).ConfigureAwait(false) == 1001 &&
            (await r.GetFieldValueAsync<string>(1, next.Token).ConfigureAwait(false)).Length == 1024, "reader_new_token_cached_row_invalid");
        rows = 1;
        while (await r.ReadAsync(next.Token).ConfigureAwait(false))
        {
            rows++; Need(await r.GetFieldValueAsync<int>(0, next.Token).ConfigureAwait(false) == rows + 1000 &&
                (await r.GetFieldValueAsync<string>(1, next.Token).ConfigureAwait(false)).Length == 1024, "reader_new_token_value_invalid");
        }
        int fetch = Observation.Responses.Count(op => op == 7) - before;
        Need(rows == 2048 && fetch > 0 && c.State == ConnectionState.Open, "returned_reader_old_token_affected_fetch");
        Cases[Stage] = new { rows, cached_first_read = true, actual_fetch_frames = fetch, old_execute_token_canceled = true, new_read_token_usable = true };
    }

    private static async Task IdleCancelAsync()
    {
        Stage = "idle_reader_cancel_captured_session";
        await using var c = await OpenAsync().ConfigureAwait(false);
        await using var q = Command(c, null, $"SELECT ID FROM {Table} WHERE ID>1000 ORDER BY ID");
        await using var r = await q.ExecuteReaderAsync().ConfigureAwait(false);
        long oldSession = Session(c); q.Cancel(); q.Cancel();
        var failure = await ErrorAsync(async () => { await r.ReadAsync().ConfigureAwait(false); }).ConfigureAwait(false);
        Need(c.State == ConnectionState.Broken && failure.GetType() == typeof(DmOperationCanceledException) &&
            Failure(failure) is { CancelSource: DmCancelSource.Command, OperationOutcome: DmOperationOutcome.Unknown, ConnectionReusable: false },
            "idle_reader_cancel_did_not_break_with_typed_cause");
        await c.CloseAsync().ConfigureAwait(false); await c.OpenAsync().ConfigureAwait(false);
        long newSession = Session(c); Need(oldSession != newSession, "explicit_reopen_session_not_new");
        await VerifyIdentityAsync(c).ConfigureAwait(false);
        q.Cancel(); await r.DisposeAsync().ConfigureAwait(false);
        Need(c.State == ConnectionState.Open && await ScalarIntAsync(c, null, "SELECT 1 FROM DUAL").ConfigureAwait(false) == 1,
            "old_reader_cancel_or_dispose_harmed_new_session");
        Cases[Stage] = new { future_read_failed = true, failure = Safe(failure), old_session_replaced = true,
            stale_cancel_and_dispose_new_session_usable = true };
    }

    private static async Task CommitRaceAsync(bool lateAck)
    {
        Stage = lateAck ? "commit_validated_ack_late_cancel" : "commit_sent_unknown";
        await using var c = await OpenAsync().ConfigureAwait(false);
        await using var tx = await BeginAsync(c).ConfigureAwait(false);
        await ExecAsync(c, tx, $"UPDATE {Table} SET VAL=1 WHERE ID=2").ConfigureAwait(false);
        using var cancel = new CancellationTokenSource();
        long session = Session(c), created = Observation!.TcpCreated; int before = Observation.Commits(session); int callbacks = 0;
        if (lateAck) Observation.BeforeCommitAck = id => { if (id == session) { callbacks++; cancel.Cancel(); } };
        else Observation.OnSent = frame => { if (frame.SessionId == session && frame.Opcode == 8) { callbacks++; cancel.Cancel(); } };
        Exception? error = null;
        try { await tx.CommitAsync(cancel.Token).WaitAsync(Safety).ConfigureAwait(false); }
        catch (Exception failure) { error = failure; }
        finally { Observation.OnSent = null; Observation.BeforeCommitAck = null; }
        int commits = Observation.Commits(session) - before;
        long reconnects = Observation.TcpCreated - created;
        Need(callbacks == 1 && commits == 1 && reconnects == 0, "commit_race_missing_or_replayed");
        if (lateAck) Need(tx.Outcome == DmTransactionOutcome.Committed && error == null, "validated_ack_reversed_by_cancel");
        else Need(error?.GetType() == typeof(DmCommitOutcomeUnknownException) && tx.Outcome == DmTransactionOutcome.OutcomeUnknown &&
            c.State == ConnectionState.Broken && Failure(error) is { OperationOutcome: DmOperationOutcome.Unknown,
                TransactionOutcome: DmTransactionOutcome.OutcomeUnknown, ConnectionReusable: false } && !((DmException)error).IsTransient,
            "sent_commit_unknown_not_prioritized");
        await using var fresh = await OpenAsync().ConfigureAwait(false);
        int value = await ScalarIntAsync(fresh, null, $"SELECT VAL FROM {Table} WHERE ID=2").ConfigureAwait(false);
        Need(lateAck ? value == 1 : value is 0 or 1, "commit_fresh_value_invalid");
        Cases[Stage] = new { boundary = lateAck ? "validated_success_before_ack_confirmation" : "sent_before_response_read",
            commit_frames = commits, automatic_reconnections = reconnects, transaction_outcome = tx.Outcome.ToString(),
            connection_state = c.State.ToString(), failure = error == null ? null : Safe(error), fresh_final_value = value,
            automatic_replay_performed = false };
        // This is a separate fixture reset after independent readback, never a retry of the unknown transaction.
        await ExecAsync(fresh, null, $"UPDATE {Table} SET VAL=0 WHERE ID=2").ConfigureAwait(false);
    }
}
