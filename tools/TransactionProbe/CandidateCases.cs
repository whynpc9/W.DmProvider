using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;

internal static class CandidateCases
{
    private const string TestUser = "WDM_PROVIDER_TEST";

    internal static Dictionary<string, object?> Run(string raw, string table, string ddlTable) => new()
    {
        ["empty_begin_commit"] = EmptyBeginCommit(raw),
        ["commit_rollback"] = CommitRollback(raw, table),
        ["active_dispose_reader"] = ActiveDisposeReader(raw, table),
        ["ownership"] = Ownership(raw, table),
        ["legacy_field_tamper"] = LegacyFieldTamper(raw, table),
        ["control_faults"] = ControlFaults(raw, table),
        ["savepoints"] = Savepoints(raw, table),
        ["second_error_rollback"] = SecondErrorRollback(raw, table),
        ["ddl_completed_externally"] = DdlCompletedExternally(raw, table, ddlTable),
        ["ambient_and_isolation"] = AmbientAndIsolation(raw, table)
    };

    internal static string[] Verify(Dictionary<string, object?> cases)
    {
        var failures = new List<string>();
        JsonElement root = JsonSerializer.SerializeToElement(cases);
        void Check(string id, Func<JsonElement, bool> predicate)
        {
            try { if (!predicate(root.GetProperty(id))) failures.Add(id); }
            catch { failures.Add(id); }
        }
        static bool Done(JsonElement row) => row.GetProperty("outcome").GetString() == "completed";
        Check("empty_begin_commit", row => Done(row) && row.GetProperty("begin_send_delta").GetInt64() == 0 &&
            row.GetProperty("commit_ack_verified").GetBoolean() && row.GetProperty("final_outcome").GetString() == "Committed");
        Check("commit_rollback", row => Done(row) && row.GetProperty("commit_visible").GetBoolean() &&
            row.GetProperty("rollback_absent").GetBoolean() && row.GetProperty("terminal_dispose_no_wire").GetBoolean() &&
            row.GetProperty("commit_outcome").GetString() == "Committed" &&
            row.GetProperty("rollback_outcome").GetString() == "RolledBack");
        Check("active_dispose_reader", row => Done(row) && row.GetProperty("read_started").GetBoolean() &&
            row.GetProperty("dispose_rolled_back").GetBoolean() && row.GetProperty("fresh_absent").GetBoolean() &&
            row.GetProperty("connection_reusable_or_closed").GetBoolean());
        Check("ownership", row => Done(row) && row.GetProperty("nested_rejected").GetBoolean() &&
            row.GetProperty("unbound_command_rejected_without_wire").GetBoolean() &&
            row.GetProperty("direct_control_sql_rejected_without_wire").GetBoolean() &&
            row.GetProperty("ddl_shapes_rejected_active_then_rollback").GetBoolean() &&
            row.GetProperty("old_transaction_rejected_after_reopen").GetBoolean());
        Check("legacy_field_tamper", row => Done(row) &&
            row.GetProperty("valid_setter_rejected").GetBoolean() &&
            row.GetProperty("clear_rejected").GetBoolean() &&
            row.GetProperty("unbound_command_rejected_without_wire").GetBoolean() &&
            row.GetProperty("dispose_rolled_back").GetBoolean() &&
            row.GetProperty("fresh_absent").GetBoolean());
        Check("control_faults", row => Done(row) &&
            row.GetProperty("pre_send_active_then_rollback").GetBoolean() &&
            row.GetProperty("attempt_zero_byte_unknown").GetBoolean() &&
            row.GetProperty("validated_response_without_ack_unknown").GetBoolean() &&
            row.GetProperty("ack_then_late_close_committed").GetBoolean() &&
            row.GetProperty("validated_ack_close_before_confirmation_committed").GetBoolean() &&
            row.GetProperty("fresh_final_states_verified").GetBoolean() &&
            row.GetProperty("absolute_deadline_semantics").GetBoolean());
        Check("savepoints", row => Done(row) && row.GetProperty("supports_savepoints").GetBoolean() &&
            row.GetProperty("unicode_name_safe").GetBoolean() && row.GetProperty("duplicate_replaced").GetBoolean() &&
            row.GetProperty("later_invalidated").GetBoolean() && row.GetProperty("invalid_names_zero_wire").GetBoolean() &&
            row.GetProperty("fresh_rows_verified").GetBoolean());
        Check("second_error_rollback", row => Done(row) && row.GetProperty("later_error_observed").GetBoolean() &&
            row.GetProperty("rollback_confirmed").GetBoolean() && row.GetProperty("fresh_absent").GetBoolean());
        Check("ddl_completed_externally", row => Done(row) &&
            row.GetProperty("completed_externally").GetBoolean() &&
            row.GetProperty("old_rollback_rejected").GetBoolean() &&
            row.GetProperty("fresh_insert_visible").GetBoolean() && row.GetProperty("fresh_ddl_visible").GetBoolean() &&
            row.GetProperty("alter_completed_externally").GetBoolean() &&
            row.GetProperty("truncate_completed_externally").GetBoolean() &&
            row.GetProperty("drop_completed_externally").GetBoolean());
        Check("ambient_and_isolation", row => Done(row) &&
            row.GetProperty("read_committed_verified").GetBoolean() &&
            row.GetProperty("unsupported_levels_rejected").GetBoolean() &&
            row.GetProperty("enlist_true_rejected").GetBoolean() &&
            row.GetProperty("nonempty_enlist_method_rejected").GetBoolean() &&
            row.GetProperty("ambient_default_not_enlisted").GetBoolean() &&
            row.GetProperty("aborted_ambient_local_commit").GetBoolean() &&
            row.GetProperty("aborted_ambient_local_rollback").GetBoolean());
        return failures.ToArray();
    }

    private static object EmptyBeginCommit(string raw)
    {
        using var diagnostic = new DiagnosticTraceScope();
        try
        {
            using var connection = OpenVerified(raw);
            long before = DmWireTestHooks.SendCount;
            using var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            long afterBegin = DmWireTestHooks.SendCount;
            bool ack = false;
            DmTransportTestHooks.AfterControlFrameValidated = (_, kind, request, response, code) =>
                ack = kind == DmTransactionControlKind.Commit && request == 8 && response == 0 && code == 0;
            try { transaction.Commit(); }
            finally { DmTransportTestHooks.Reset(); }
            return new { outcome = "completed", begin_send_delta = afterBegin - before,
                commit_ack_verified = ack, final_outcome = transaction.Outcome.ToString(), diagnostic_bodies = diagnostic.Events };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static object CommitRollback(string raw, string table)
    {
        using var diagnostic = new DiagnosticTraceScope();
        try
        {
            DmTransactionOutcome committed;
            using (var connection = OpenVerified(raw))
            using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (101, 'committed')");
                transaction.Commit();
                committed = transaction.Outcome;
                long before = DmWireTestHooks.SendCount;
                transaction.Dispose();
                if (DmWireTestHooks.SendCount != before)
                    throw new InvalidOperationException("terminal_dispose_sent_wire");
            }
            using var firstReadback = OpenVerified(raw);
            bool visible = Count(firstReadback, table, 101) == 1;
            DmTransactionOutcome rolledBack;
            using (var connection = OpenVerified(raw))
            using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (102, 'rolled_back')");
                transaction.Rollback();
                rolledBack = transaction.Outcome;
                transaction.Dispose();
            }
            using var secondReadback = OpenVerified(raw);
            return new { outcome = "completed", commit_visible = visible,
                rollback_absent = Count(secondReadback, table, 102) == 0,
                terminal_dispose_no_wire = true, commit_outcome = committed.ToString(),
                rollback_outcome = rolledBack.ToString(), diagnostic_bodies = diagnostic.Events };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static object ActiveDisposeReader(string raw, string table)
    {
        try
        {
            using var connection = OpenVerified(raw);
            var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (103, 'dispose')");
            bool readStarted;
            using (var command = Command(connection, transaction, $"SELECT ID FROM {table} WHERE ID = 103"))
            using (var reader = command.ExecuteReader())
            {
                readStarted = reader.Read();
                transaction.Dispose();
            }
            bool reusableOrClosed = connection.State == ConnectionState.Closed;
            if (connection.State == ConnectionState.Open)
            {
                try { reusableOrClosed = Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL"),
                    CultureInfo.InvariantCulture) == 1; }
                catch { reusableOrClosed = false; }
            }
            using var readback = OpenVerified(raw);
            return new { outcome = "completed", read_started = readStarted,
                dispose_rolled_back = transaction.Outcome == DmTransactionOutcome.RolledBack,
                fresh_absent = Count(readback, table, 103) == 0,
                connection_reusable_or_closed = reusableOrClosed };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static object Ownership(string raw, string table)
    {
        try
        {
            using var connection = OpenVerified(raw);
            var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            bool nested;
            try { using var _ = connection.BeginTransaction(); nested = false; }
            catch (InvalidOperationException) { nested = true; }
            long before = DmWireTestHooks.SendCount;
            bool unbound;
            try { Scalar(connection, "SELECT 1 FROM DUAL"); unbound = false; }
            catch (InvalidOperationException) { unbound = true; }
            bool noWire = DmWireTestHooks.SendCount == before;
            long beforeDirect = DmWireTestHooks.SendCount;
            bool directControlRejected = Capture(() => Exec(connection, transaction, "COMMIT"))
                is NotSupportedException;
            directControlRejected &= Capture(() => Exec(connection, transaction, "ROLLBACK"))
                is NotSupportedException;
            directControlRejected &= Capture(() => Exec(connection, transaction,
                "SET TRANSACTION READ ONLY")) is NotSupportedException;
            directControlRejected &= DmWireTestHooks.SendCount == beforeDirect;
            connection.Close();
            connection.Open();
            bool oldRejected;
            try { transaction.Commit(); oldRejected = false; }
            catch (InvalidOperationException) { oldRejected = true; }
            return new { outcome = "completed", nested_rejected = nested,
                unbound_command_rejected_without_wire = unbound && noWire,
                direct_control_sql_rejected_without_wire = directControlRejected,
                ddl_shapes_rejected_active_then_rollback = RejectedDdlShapes(raw, table),
                old_transaction_rejected_after_reopen = oldRejected };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static bool RejectedDdlShapes(string raw, string table)
    {
        using var connection = OpenVerified(raw);
        using var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (126, 'ddl_guard')");
        long before = DmWireTestHooks.SendCount;
        bool mixed = Capture(() => Exec(connection, transaction,
            $"SELECT 1 FROM DUAL; ALTER TABLE {table} ADD T10_MIX INT")) is NotSupportedException;
        bool parameterized = Capture(() => Exec(connection, transaction,
            $"ALTER TABLE {table} ADD T10_BIND INT DEFAULT :p0", 1)) is NotSupportedException;
        bool unchanged = DmWireTestHooks.SendCount == before && transaction.Outcome == DmTransactionOutcome.Active;
        transaction.Rollback();
        using var readback = OpenVerified(raw);
        return mixed && parameterized && unchanged && transaction.Outcome == DmTransactionOutcome.RolledBack &&
            Count(readback, table, 126) == 0;
    }

    private static object LegacyFieldTamper(string raw, string table)
    {
        try
        {
            using var connection = OpenVerified(raw);
            var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (119, 'field_tamper')");
            bool validRejected = Capture(() => transaction.Valid = false) is NotSupportedException;
            bool clearRejected = Capture(transaction.Clear) is NotSupportedException;
            transaction._disposeStatus = DmTransaction.DisposeStatus.Disposed;
            long before = DmWireTestHooks.SendCount;
            bool unboundRejected = Capture(() => Scalar(connection, "SELECT 1 FROM DUAL"))
                is InvalidOperationException;
            bool noWire = DmWireTestHooks.SendCount == before;
            transaction.Dispose();
            bool rolledBack = transaction.Outcome == DmTransactionOutcome.RolledBack;
            using var readback = OpenVerified(raw);
            return new { outcome = "completed", valid_setter_rejected = validRejected,
                clear_rejected = clearRejected,
                unbound_command_rejected_without_wire = unboundRejected && noWire,
                dispose_rolled_back = rolledBack, fresh_absent = Count(readback, table, 119) == 0 };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private enum CommitFault { BeforeAttempt, AfterMarked, AfterValidated, LateClose, CloseBeforeAck }

    private sealed record FaultObservation(bool ExpectedException, string Outcome, int FreshRows,
        bool NoCommitBytes, bool AckMetadataObserved, bool ConnectionClosed, int CommitSendDelta,
        bool ClosedBeforeAckRelease = false, object? FailureDiagnostic = null);

    private static object ControlFaults(string raw, string table)
    {
        try
        {
            FaultObservation before = RunCommitFault(raw, table, 104, CommitFault.BeforeAttempt);
            FaultObservation marked = RunCommitFault(raw, table, 105, CommitFault.AfterMarked);
            FaultObservation validated = RunCommitFault(raw, table, 106, CommitFault.AfterValidated);
            FaultObservation late = RunCommitFault(raw, table, 107, CommitFault.LateClose);
            FaultObservation closeBeforeAck = RunCommitFault(raw, table, 127, CommitFault.CloseBeforeAck);
            var deadline = RunControlDeadlineCases(raw, table);
            return new
            {
                outcome = "completed",
                pre_send_active_then_rollback = before.ExpectedException && before.Outcome == "RolledBack" &&
                    before.FreshRows == 0 && before.NoCommitBytes && before.CommitSendDelta == 0,
                attempt_zero_byte_unknown = marked.ExpectedException && marked.Outcome == "OutcomeUnknown" &&
                    marked.FreshRows == 0 && marked.NoCommitBytes && marked.ConnectionClosed,
                validated_response_without_ack_unknown = validated.ExpectedException &&
                    validated.Outcome == "OutcomeUnknown" && validated.FreshRows == 1 &&
                    validated.AckMetadataObserved && validated.ConnectionClosed,
                ack_then_late_close_committed = !late.ExpectedException && late.Outcome == "Committed" &&
                    late.FreshRows == 1 && late.AckMetadataObserved && late.ConnectionClosed,
                validated_ack_close_before_confirmation_committed = !closeBeforeAck.ExpectedException &&
                    closeBeforeAck.Outcome == "Committed" && closeBeforeAck.FreshRows == 1 &&
                    closeBeforeAck.AckMetadataObserved && closeBeforeAck.ConnectionClosed && closeBeforeAck.ClosedBeforeAckRelease,
                fresh_final_states_verified = before.FreshRows == 0 && marked.FreshRows == 0 &&
                    validated.FreshRows == 1 && late.FreshRows == 1 && closeBeforeAck.FreshRows == 1,
                absolute_deadline_semantics = deadline.PreSendExpiredActive &&
                    deadline.AfterAttemptUnknown && deadline.ZeroIsInfinite,
                deadline,
                observations = new { before, marked, validated, late, closeBeforeAck }
            };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private sealed record DeadlineObservation(bool PreSendExpiredActive, bool AfterAttemptUnknown,
        bool ZeroIsInfinite, int UnknownFreshRows);

    private static DeadlineObservation RunControlDeadlineCases(string raw, string table)
    {
        bool preSend;
        var preClock = new ManualTimeProvider();
        using (var connection = OpenVerified(raw, preClock, 1))
        using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (120, 'deadline_pre')");
            long before = DmWireTestHooks.SendCount;
            DmWireTestHooks.AfterExchangeEntered = (_, purpose) =>
            {
                if (purpose == DmOperationPurpose.TransactionControl)
                    preClock.AdvanceMilliseconds(1000);
            };
            Exception? failure;
            try { failure = Capture(transaction.Commit); }
            finally { DmWireTestHooks.AfterExchangeEntered = null; }
            preSend = failure is TimeoutException && transaction.Outcome == DmTransactionOutcome.Active &&
                DmWireTestHooks.SendCount == before;
            transaction.Rollback();
        }
        using (var readback = OpenVerified(raw))
            preSend &= Count(readback, table, 120) == 0;

        bool afterAttempt;
        var afterClock = new ManualTimeProvider();
        using (var connection = OpenVerified(raw, afterClock, 1))
        using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (121, 'deadline_after')");
            bool marked = false;
            DmTransportTestHooks.AfterControlAttemptMarked = (_, kind) =>
            {
                if (kind != DmTransactionControlKind.Commit) return;
                marked = true;
                afterClock.AdvanceMilliseconds(1000);
            };
            Exception? failure;
            try { failure = Capture(transaction.Commit); }
            finally { DmTransportTestHooks.Reset(); }
            afterAttempt = marked && failure is DmCommitOutcomeUnknownException unknown &&
                !unknown.IsTransient && transaction.Outcome == DmTransactionOutcome.OutcomeUnknown &&
                connection.State == ConnectionState.Closed;
        }
        int unknownRows;
        using (var readback = OpenVerified(raw))
            unknownRows = Count(readback, table, 121);
        afterAttempt &= unknownRows is 0 or 1;

        bool infinite;
        var infiniteClock = new ManualTimeProvider();
        using (var connection = OpenVerified(raw, infiniteClock, 0))
        using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (122, 'deadline_zero')");
            DmWireTestHooks.AfterExchangeEntered = (_, purpose) =>
            {
                if (purpose == DmOperationPurpose.TransactionControl)
                    infiniteClock.AdvanceMilliseconds(60000);
            };
            Exception? failure;
            try { failure = Capture(transaction.Commit); }
            finally { DmWireTestHooks.AfterExchangeEntered = null; }
            infinite = failure == null && transaction.Outcome == DmTransactionOutcome.Committed;
        }
        using (var readback = OpenVerified(raw))
            infinite &= Count(readback, table, 122) == 1;
        return new DeadlineObservation(preSend, afterAttempt, infinite, unknownRows);
    }

    private static FaultObservation RunCommitFault(string raw, string table, int id, CommitFault phase)
    {
        using var connection = OpenVerified(raw);
        using var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, "fault");
        DmTransportTestHooks.Reset();
        long bytesBefore = DmWireTestHooks.SentBytes;
        long sendsBefore = DmWireTestHooks.SendCount;
        bool ackObserved = false;
        bool closedBeforeAckRelease = false;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            if (phase == CommitFault.BeforeAttempt)
                DmTransportTestHooks.BeforeControlSendAttempt = (_, kind) =>
                {
                    if (kind == DmTransactionControlKind.Commit)
                        throw new InvalidOperationException("synthetic_pre_send");
                };
            if (phase == CommitFault.AfterMarked)
                DmTransportTestHooks.AfterControlAttemptMarked = (_, kind) =>
                {
                    if (kind == DmTransactionControlKind.Commit)
                        throw new IOException("synthetic_attempt_without_bytes");
                };
            if (phase == CommitFault.AfterValidated)
                DmTransportTestHooks.AfterControlFrameValidated = (_, kind, request, response, code) =>
                {
                    if (kind != DmTransactionControlKind.Commit) return;
                    ackObserved = request == 8 && response == 0 && code == 0;
                    throw new IOException("synthetic_ack_classification_loss");
                };
            if (phase is CommitFault.LateClose or CommitFault.CloseBeforeAck)
            {
                DmTransportTestHooks.AfterControlFrameValidated = (_, kind, request, response, code) =>
                {
                    if (kind == DmTransactionControlKind.Commit)
                        ackObserved = request == 8 && response == 0 && code == 0;
                };
                DmTransportTestHooks.BeforeControlAck = (_, kind) =>
                {
                    if (kind != DmTransactionControlKind.Commit) return;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("synthetic_ack_barrier");
                };
            }
            Exception? failure = null;
            if (phase is CommitFault.LateClose or CommitFault.CloseBeforeAck)
            {
                Task<Exception?> pending = Task.Run(() => Capture(transaction.Commit));
                if (!entered.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("ack_barrier_not_entered");
                if (phase == CommitFault.CloseBeforeAck)
                {
                    // The Commit worker has a fully validated success response but
                    // cannot classify the ACK until this barrier is released.
                    try
                    {
                        connection.Close();
                        closedBeforeAckRelease = connection.State == ConnectionState.Closed;
                    }
                    finally { release.Set(); }
                    if (!pending.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("commit_after_close_ack_barrier_did_not_finish");
                    failure = pending.GetAwaiter().GetResult();
                }
                else
                {
                    release.Set();
                    failure = pending.GetAwaiter().GetResult();
                    connection.Close();
                }
            }
            else failure = Capture(transaction.Commit);

            DmTransportTestHooks.Reset();
            string outcomeBeforeDispose = transaction.Outcome.ToString();
            bool closed = connection.State == ConnectionState.Closed;
            long commitBytes = DmWireTestHooks.SentBytes - bytesBefore;
            int sendDelta = checked((int)(DmWireTestHooks.SendCount - sendsBefore));
            if (phase == CommitFault.BeforeAttempt && transaction.Outcome == DmTransactionOutcome.Active)
            {
                transaction.Rollback();
                outcomeBeforeDispose = transaction.Outcome.ToString();
            }
            transaction.Dispose();
            using var readback = OpenVerified(raw);
            int rows = Count(readback, table, id);
            bool expectedError = phase switch
            {
                CommitFault.BeforeAttempt => failure is InvalidOperationException,
                CommitFault.AfterMarked or CommitFault.AfterValidated =>
                    failure is DmCommitOutcomeUnknownException unknown && !unknown.IsTransient,
                _ => failure == null
            };
            return new FaultObservation(phase is CommitFault.LateClose or CommitFault.CloseBeforeAck ? failure != null : expectedError,
                outcomeBeforeDispose, rows, commitBytes == 0, ackObserved, closed, sendDelta, closedBeforeAckRelease, SafeFailure(failure));
        }
        finally
        {
            release.Set();
            DmTransportTestHooks.Reset();
        }
    }

    internal static Dictionary<string, object?> RunAckCloseRace(string raw, string table)
    {
        try
        {
            var observation = RunCommitFault(raw, table, 127, CommitFault.CloseBeforeAck);
            return new() { ["ack_close_race"] = new { outcome = "completed",
                confirmed_committed = !observation.ExpectedException && observation.Outcome == "Committed" &&
                    observation.FreshRows == 1 && observation.AckMetadataObserved && observation.ConnectionClosed &&
                    observation.ClosedBeforeAckRelease,
                observation } };
        }
        catch (Exception ex)
        {
            return new() { ["ack_close_race"] = new { outcome = "error", failure = SafeFailure(ex) } };
        }
    }

    internal static string[] VerifyAckCloseRace(Dictionary<string, object?> cases)
    {
        try
        {
            var row = JsonSerializer.SerializeToElement(cases).GetProperty("ack_close_race");
            return row.GetProperty("outcome").GetString() == "completed" &&
                row.GetProperty("confirmed_committed").GetBoolean() ? [] : ["ack_close_race"];
        }
        catch { return ["ack_close_race"]; }
    }

    private static object? SafeFailure(Exception? failure)
    {
        if (failure == null) return null;
        var chain = new List<object>();
        for (Exception? current = failure; current != null && chain.Count < 8; current = current.InnerException)
        {
            // Method identity and source line only. Messages, parameters, SQL,
            // environment data and filesystem paths are never serialized.
            var frames = new StackTrace(current, fNeedFileInfo: true).GetFrames() ?? [];
            chain.Add(new { type = current.GetType().FullName,
                frames = frames.Take(8).Select(frame => new {
                    declaring_type = frame.GetMethod()?.DeclaringType?.FullName,
                    method = frame.GetMethod()?.Name, line = frame.GetFileLineNumber() }).ToArray() });
        }
        return new { exception_chain = chain };
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static object Savepoints(string raw, string table)
    {
        try
        {
            using var connection = OpenVerified(raw);
            using var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            if (!transaction.SupportsSavepoints)
                return new { outcome = "unsupported", supports_savepoints = false };
            long beforeInvalid = DmWireTestHooks.SendCount;
            bool nulRejected = Capture(() => transaction.Save("bad\0name")) is ArgumentException;
            bool longRejected = Capture(() => transaction.Save(new string('X', 129))) is ArgumentException;
            bool invalidNoWire = nulRejected && longRejected && DmWireTestHooks.SendCount == beforeInvalid;

            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (108, 'before')");
            const string hostileName = "用户'\";DROP TABLE should_not_run;--";
            transaction.Save(hostileName);
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (109, 'after')");
            transaction.Rollback(hostileName);
            transaction.Release(hostileName);

            transaction.Save("dup");
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (110, 'dup_before')");
            transaction.Save("dup");
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (111, 'dup_after')");
            transaction.Rollback("dup");
            transaction.Release("dup");

            transaction.Save("outer");
            transaction.Save("inner");
            transaction.Rollback("outer");
            long beforeInvalidated = DmWireTestHooks.SendCount;
            bool laterInvalidated = Capture(() => transaction.Release("inner")) is InvalidOperationException &&
                DmWireTestHooks.SendCount == beforeInvalidated;
            transaction.Release("outer");
            transaction.Commit();
            using var readback = OpenVerified(raw);
            bool beforeVisible = Count(readback, table, 108) == 1;
            bool afterAbsent = Count(readback, table, 109) == 0;
            bool dupBefore = Count(readback, table, 110) == 1;
            bool dupAfter = Count(readback, table, 111) == 0;
            return new { outcome = "completed", supports_savepoints = true,
                unicode_name_safe = beforeVisible && afterAbsent,
                duplicate_replaced = dupBefore && dupAfter,
                later_invalidated = laterInvalidated,
                invalid_names_zero_wire = invalidNoWire,
                fresh_rows_verified = beforeVisible && afterAbsent && dupBefore && dupAfter };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static object SecondErrorRollback(string raw, string table)
    {
        using var diagnostic = new DiagnosticTraceScope();
        try
        {
            using var connection = OpenVerified(raw);
            using var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (114, 'before_error')");
            string missing = "T10_MISSING_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            Exception? laterError;
            using (var command = Command(connection, transaction,
                $"SELECT 1 FROM DUAL; SELECT * FROM {missing}"))
                laterError = Capture(() => { using var reader = command.ExecuteReader(); });
            bool observed = laterError != null && ErrorKind(laterError).Contains(":-2106", StringComparison.Ordinal);
            Exception? rollbackError = Capture(transaction.Rollback);
            using var readback = OpenVerified(raw);
            return new { outcome = "completed", later_error_observed = observed,
                rollback_confirmed = rollbackError == null && transaction.Outcome == DmTransactionOutcome.RolledBack,
                fresh_absent = Count(readback, table, 114) == 0,
                rollback_error_kind = rollbackError == null ? null : ErrorKind(rollbackError),
                diagnostic_bodies = diagnostic.Events };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private sealed class DiagnosticTraceScope : IDisposable
    {
        internal List<object> Events { get; } = [];
        internal DiagnosticTraceScope()
            => DmTransactionProtocolTrace.AfterDiagnosticBody = (_, request, response, code, length, lengths, remaining, complete) =>
                Events.Add(new { request_opcode = request, response_opcode = response, sql_code = code,
                    decoded_body_length = length, diagnostic_lengths = lengths.ToArray(), remaining,
                    complete_structure = complete });
        public void Dispose() => DmTransactionProtocolTrace.AfterDiagnosticBody = null;
    }

    private static object DdlCompletedExternally(string raw, string table, string ddlTable)
    {
        try
        {
            bool completed;
            bool oldRollbackRejected;
            Exception? ddlError;
            using (var connection = OpenVerified(raw))
            using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (115, 'before_ddl')");
                ddlError = Capture(() => Exec(connection, transaction,
                    $"CREATE TABLE {ddlTable} (ID INT)"));
                completed = transaction.Outcome == DmTransactionOutcome.CompletedExternally;
                long beforeOldRollback = DmWireTestHooks.SendCount;
                oldRollbackRejected = Capture(transaction.Rollback) is InvalidOperationException &&
                    DmWireTestHooks.SendCount == beforeOldRollback;
            }
            using var readback = OpenVerified(raw);
            bool createVisible = Count(readback, table, 115) == 1 && ObjectExists(readback, ddlTable);
            bool altered = DdlVariantCandidate(raw, table, ddlTable, 123,
                $"ALTER TABLE {ddlTable} ADD EXTRA INT", expectedTargetExists: true);
            bool truncated = DdlVariantCandidate(raw, table, ddlTable, 124,
                $"TRUNCATE TABLE {ddlTable}", expectedTargetExists: true);
            bool dropped = DdlVariantCandidate(raw, table, ddlTable, 125,
                $"DROP TABLE {ddlTable}", expectedTargetExists: false);
            return new { outcome = "completed", completed_externally = completed,
                old_rollback_rejected = oldRollbackRejected,
                fresh_insert_visible = createVisible, fresh_ddl_visible = createVisible,
                alter_completed_externally = altered,
                truncate_completed_externally = truncated,
                drop_completed_externally = dropped,
                ddl_error_kind = ddlError == null ? null : ErrorKind(ddlError) };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static bool DdlVariantCandidate(string raw, string table, string ddlTable, int id,
        string ddl, bool expectedTargetExists)
    {
        bool completed;
        bool rollbackRejected;
        using (var connection = OpenVerified(raw))
        using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            Exec(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, "before_ddl");
            Exec(connection, transaction, ddl);
            completed = transaction.Outcome == DmTransactionOutcome.CompletedExternally;
            long beforeOldRollback = DmWireTestHooks.SendCount;
            rollbackRejected = Capture(transaction.Rollback) is InvalidOperationException &&
                DmWireTestHooks.SendCount == beforeOldRollback;
        }
        using var readback = OpenVerified(raw);
        return completed && rollbackRejected && Count(readback, table, id) == 1 &&
            ObjectExists(readback, ddlTable) == expectedTargetExists;
    }

    private static object AmbientAndIsolation(string raw, string table)
    {
        try
        {
            bool readCommitted;
            using (var connection = OpenVerified(raw))
            using (var transaction = (DmTransaction)connection.BeginTransaction(IsolationLevel.Unspecified))
            using (var command = Command(connection, transaction, "SELECT 1 FROM DUAL"))
            {
                readCommitted = transaction.IsolationLevel == IsolationLevel.ReadCommitted &&
                    Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
                transaction.Commit();
            }
            bool verifiedIsolationProfile;
            using (var profile = OpenVerified(raw))
                verifiedIsolationProfile = string.Equals(profile.ServerVersion, "8.1.5.60", StringComparison.Ordinal);
            var unsupportedLevels = new List<IsolationLevel> { IsolationLevel.RepeatableRead, IsolationLevel.Snapshot, IsolationLevel.Chaos };
            if (!verifiedIsolationProfile)
                unsupportedLevels.AddRange([IsolationLevel.ReadUncommitted, IsolationLevel.Serializable]);
            bool unsupported = true;
            foreach (var level in unsupportedLevels)
            {
                using var connection = OpenVerified(raw);
                Exception? error = Capture(() => { using var transaction = connection.BeginTransaction(level); });
                unsupported &= error is NotSupportedException;
            }
            bool enlistTrueRejected = Capture(() => _ = new DmConnectionStringBuilder("enlist=true"))
                is NotSupportedException;
            bool enlistMethodRejected;
            using (var connection = OpenVerified(raw))
            using (var ambient = new CommittableTransaction())
                enlistMethodRejected = Capture(() => connection.EnlistTransaction(ambient)) is NotSupportedException;

            bool ambientNotEnlisted;
            using (var scope = new TransactionScope(TransactionScopeOption.Required,
                       TransactionScopeAsyncFlowOption.Enabled))
            {
                using var connection = OpenVerified(raw);
                Exec(connection, null, $"INSERT INTO {table} (ID, VAL) VALUES (116, 'ambient')");
                // The scope intentionally ends without Complete.
            }
            using (var readback = OpenVerified(raw))
                ambientNotEnlisted = Count(readback, table, 116) == 1;
            bool abortedCommit, abortedRollback;
            using (var ambient = new CommittableTransaction())
            {
                ambient.Rollback();
                Transaction? previous = Transaction.Current;
                Transaction.Current = ambient;
                try
                {
                    using var connection = OpenVerified(raw);
                    using var local = (DmTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
                    Exec(connection, local, $"INSERT INTO {table} (ID, VAL) VALUES (117, 'aborted_ambient_commit')");
                    local.Save("ambient_sp");
                    local.Release("ambient_sp");
                    local.Commit();
                    abortedCommit = local.Outcome == DmTransactionOutcome.Committed;
                    using var rollbackConnection = OpenVerified(raw);
                    using var rollback = (DmTransaction)rollbackConnection.BeginTransaction(IsolationLevel.ReadCommitted);
                    Exec(rollbackConnection, rollback,
                        $"INSERT INTO {table} (ID, VAL) VALUES (118, 'aborted_ambient_rollback')");
                    rollback.Rollback();
                    abortedRollback = rollback.Outcome == DmTransactionOutcome.RolledBack;
                }
                finally { Transaction.Current = previous; }
            }
            using (var readback = OpenVerified(raw))
            {
                abortedCommit &= Count(readback, table, 117) == 1;
                abortedRollback &= Count(readback, table, 118) == 0;
            }
            return new { outcome = "completed", read_committed_verified = readCommitted,
                unsupported_levels_rejected = unsupported,
                enlist_true_rejected = enlistTrueRejected,
                nonempty_enlist_method_rejected = enlistMethodRejected,
                ambient_default_not_enlisted = ambientNotEnlisted,
                aborted_ambient_local_commit = abortedCommit,
                aborted_ambient_local_rollback = abortedRollback };
        }
        catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
    }

    private static DmConnection OpenVerified(string raw, TimeProvider? transactionClock = null,
        int? commandTimeoutSeconds = null)
    {
        var builder = new DmConnectionStringBuilder(raw)
        { TransportSecurity = DmTransportSecurity.PlaintextAllowed, PersistSecurityInfo = false, Schema = TestUser };
        if (commandTimeoutSeconds is { } seconds) builder.CommandTimeout = seconds;
        var connection = new DmConnection(builder.ConnectionString);
        if (transactionClock != null) connection.TransactionClock = transactionClock;
        try
        {
            connection.Open();
            if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("server_identity_invalid");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    private static int Count(DmConnection connection, string table, int id) =>
        Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE ID = :p0", id),
            CultureInfo.InvariantCulture);
    private static bool ObjectExists(DmConnection connection, string table) =>
        Convert.ToInt32(Scalar(connection,
            "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table),
            CultureInfo.InvariantCulture) != 0;
    private static object? Scalar(DmConnection connection, string sql, params object[] values)
    { using var command = Command(connection, null, sql, values); return command.ExecuteScalar(); }
    private static int Exec(DmConnection connection, DbTransaction? transaction, string sql, params object[] values)
    { using var command = Command(connection, transaction, sql, values); return command.ExecuteNonQuery(); }
    private static DbCommand Command(DmConnection connection, DbTransaction? transaction, string sql, params object[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p" + index.ToString(CultureInfo.InvariantCulture);
            parameter.DbType = values[index] is int ? DbType.Int32 : DbType.String;
            parameter.Value = values[index];
            command.Parameters.Add(parameter);
        }
        return command;
    }
    private static string ErrorKind(Exception error)
    {
        int? number = null;
        try { if (error.GetType().GetProperty("Number")?.GetValue(error) is int value) number = value; }
        catch { }
        return number is null ? error.GetType().Name : error.GetType().Name + ":" + number;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }
}
