using System.Data;
using System.Data.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;

const string TestUser = "WDM_PROVIDER_TEST";
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T05",
    ["implementation"] = "W",
    ["run_id"] = Guid.NewGuid().ToString("N")
};
string stage = "arguments";
try
{
    if (args.Length != 1 || args[0] != "real") throw new ProbeFailure("usage");
    stage = "configuration";
    string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new ProbeFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("configured_account_invalid");
    builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
    builder.PersistSecurityInfo = false;
    builder.Schema = TestUser;
    if (builder.TransportSecurity != DmTransportSecurity.PlaintextAllowed || builder.PersistSecurityInfo)
        throw new ProbeFailure("explicit_transport_invalid");
    string settings = builder.ConnectionString;
    result["explicit_transport"] = "PlaintextAllowed";
    result["explicit_test_schema"] = true;
    var table = "T05_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_name"] = table;
    bool accountVerified = false, readerOwnership = false, transactionConflict = false;
    bool closeIdempotent = false, independentConnection = false, cleanupVerified = false;
    bool sameReaderFailFast = false, sameReaderNoSend = false, competingCommandNoSend = false, simultaneousWire = false;
    bool afterSendFailureClosed = false, decodeFailureClosed = false, staleCloseSafe = false;
    bool lobGetBytes = false, lobStream = false;
    bool directLobStreamOwnership = false;
    bool oldTransactionSafe = false, disposeDidNotCommit = false, prepareDisposeSafe = false;
    bool schemaTableSafe = false, cancelDoesNotReconnect = false, closeDuringHandshakeSafe = false;
    bool connectionSchemaSafe = false;
    bool primaryKeyViewVerified = false, stateCallbackOutsideLock = false;
    Exception? workError = null, cleanupError = null;
    string? workStage = null;
    try
    {
        stage = "server_identity";
        using var owner = OpenVerified(settings);
        accountVerified = true;
        stage = "create_table";
        Exec(owner, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(100), PAYLOAD BLOB)");
        var payload = Enumerable.Range(0, 96 * 1024).Select(i => (byte)(i % 251)).ToArray();
        stage = "insert_lob";
        Exec(owner, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", 1, "one");

        stage = "reader_ownership";
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT ID FROM {table} ORDER BY ID";
            using var reader = command.ExecuteReader();
            long beforeCancelSession = owner.Session.SessionId;
            cancelDoesNotReconnect = RejectsNotSupported(() => command.Cancel()) &&
                owner.Session.SessionId == beforeCancelSession;
            if (!cancelDoesNotReconnect) throw new ProbeFailure("cancel_reconnected_or_succeeded");
            using var competitor = owner.CreateCommand();
            competitor.CommandText = "SELECT 1 FROM DUAL";
            if (!RejectsWithoutWire(() => competitor.ExecuteScalar()))
                throw new ProbeFailure("second_command_while_reader_allowed");
            if (!RejectsWithoutWire(() => owner.BeginTransaction()))
                throw new ProbeFailure("transaction_while_reader_allowed");
            transactionConflict = true;
            if (!reader.Read() || reader.GetInt32(0) != 1 || reader.Read())
                throw new ProbeFailure("reader_rows_invalid");
            if (!RejectsWithoutWire(() => competitor.ExecuteScalar()))
                throw new ProbeFailure("second_command_after_read_false_allowed");
            if (reader.NextResult()) throw new ProbeFailure("unexpected_result_set");
            if (!RejectsWithoutWire(() => competitor.ExecuteScalar()))
                throw new ProbeFailure("second_command_after_nextresult_false_allowed");
            competingCommandNoSend = true;
            reader.Close();
            if (Convert.ToInt32(competitor.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new ProbeFailure("command_after_reader_close_failed");
            readerOwnership = true;
        }

        stage = "same_reader_parallel";
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT ID FROM {table} ORDER BY ID";
            using var reader = command.ExecuteReader();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            long sessionId = owner.Session.SessionId;
            DmDataReader.AfterInvocationEntered = identity =>
            {
                if (identity.SessionId != sessionId) return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("reader_barrier_timeout");
            };
            try
            {
                var firstRead = Task.Run(() => reader.Read());
                if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("reader_barrier_not_entered");
                long sends = DmWireTestHooks.SendCount, bytes = DmWireTestHooks.SentBytes;
                sameReaderFailFast = RejectsInvalidOperation(() => reader.Read());
                sameReaderNoSend = DmWireTestHooks.SendCount == sends && DmWireTestHooks.SentBytes == bytes;
                if (!sameReaderFailFast || !sameReaderNoSend) throw new ProbeFailure("parallel_read_not_failfast");
                release.Set();
                if (!await firstRead) throw new ProbeFailure("first_read_failed");
            }
            finally { release.Set(); DmDataReader.AfterInvocationEntered = null; }
        }

        stage = "transaction_reader_conflict";
        using (var tx = owner.BeginTransaction())
        {
            ExecWithTransaction(owner, tx, $"INSERT INTO {table} (ID, VAL) VALUES (2, 'two')");
            using (var command = owner.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = "SELECT 1 FROM DUAL";
                using var reader = command.ExecuteReader();
                transactionConflict = RejectsInvalidOperation(() => tx.Commit()) &&
                                      RejectsInvalidOperation(() => tx.Rollback());
                if (!transactionConflict) throw new ProbeFailure("transaction_reader_conflict_invalid");
            }
            tx.Commit();
        }
        using (var independent = OpenVerified(settings))
            if (Count(independent, table) != 2) throw new ProbeFailure("transaction_not_preserved_after_reader");

        stage = "separate_connections";
        using (var independent = OpenVerified(settings))
        {
            // A live reader on owner does not hold a process-wide session lock.
            using var command = owner.CreateCommand();
            command.CommandText = $"SELECT ID FROM {table}";
            using var reader = command.ExecuteReader();
            independentConnection = Count(independent, table) == 2;
            if (!independentConnection) throw new ProbeFailure("independent_connection_blocked");
        }

        stage = "simultaneous_wire";
        using (var peer = OpenVerified(settings))
        {
            var expected = new HashSet<long> { owner.Session.SessionId, peer.Session.SessionId };
            var seen = new ConcurrentDictionary<long, byte>();
            using var bothSent = new CountdownEvent(2);
            using var release = new ManualResetEventSlim();
            DmWireTestHooks.AfterSendBeforeReceive = (identity, _) =>
            {
                if (!expected.Contains(identity.SessionId)) return;
                if (seen.TryAdd(identity.SessionId, 0)) bothSent.Signal();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("wire_barrier_timeout");
            };
            try
            {
                var a = Task.Run(() => Scalar(owner, "SELECT 1 FROM DUAL"));
                var b = Task.Run(() => Scalar(peer, "SELECT 1 FROM DUAL"));
                simultaneousWire = bothSent.Wait(TimeSpan.FromSeconds(10));
                release.Set();
                if (!simultaneousWire) throw new ProbeFailure("sessions_serialized_at_wire");
                var values = await Task.WhenAll(a, b);
                if (values.Any(v => Convert.ToInt32(v, CultureInfo.InvariantCulture) != 1))
                    throw new ProbeFailure("parallel_wire_result_invalid");
            }
            finally { release.Set(); DmWireTestHooks.AfterSendBeforeReceive = null; }
        }

        stage = "wire_fail_closed";
        afterSendFailureClosed = FaultAndRecover(settings, afterSend: true);
        decodeFailureClosed = FaultAndRecover(settings, afterSend: false);
        if (!afterSendFailureClosed || !decodeFailureClosed)
            throw new ProbeFailure("wire_fault_not_closed");

        stage = "stale_close";
        staleCloseSafe = await CloseAndReopenAcrossAbortBarrier(settings);
        if (!staleCloseSafe) throw new ProbeFailure("stale_close_affected_new_session");

        stage = "close_during_handshake";
        closeDuringHandshakeSafe = await CloseDuringHandshake(settings);
        if (!closeDuringHandshakeSafe) throw new ProbeFailure("close_during_handshake_invalid");

        stage = "old_transaction";
        using (var c = OpenVerified(settings))
        {
            var tx = c.BeginTransaction();
            c.Close();
            c.Open();
            VerifyIdentity(c);
            long bytes = DmWireTestHooks.SentBytes;
            bool rejected = RejectsInvalidOperation(() => tx.Commit()) &&
                            RejectsInvalidOperation(() => tx.Rollback());
            bool noSend = DmWireTestHooks.SentBytes == bytes;
            tx.Dispose();
            oldTransactionSafe = rejected && noSend &&
                Convert.ToInt32(Scalar(c, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
            if (!oldTransactionSafe) throw new ProbeFailure("old_transaction_touched_new_session");
        }

        stage = "transaction_dispose_rollback";
        using (var c = OpenVerified(settings))
        {
            var tx = c.BeginTransaction();
            ExecWithTransaction(c, tx, $"INSERT INTO {table} (ID, VAL) VALUES (99, 'uncommitted')");
            tx.Dispose();
        }
        using (var readback = OpenVerified(settings))
            disposeDidNotCommit = Count(readback, table) == 2;
        if (!disposeDidNotCommit) throw new ProbeFailure("transaction_dispose_committed");

        stage = "prepare_dispose";
        using (var c = OpenVerified(settings))
        {
            var prepared = c.CreateCommand();
            prepared.CommandText = "SELECT 1 FROM DUAL";
            prepared.Prepare();
            using (var command = c.CreateCommand())
            {
                command.CommandText = "SELECT 1 FROM DUAL";
                using var reader = command.ExecuteReader();
                prepared.Dispose();
                prepareDisposeSafe = RejectsInvalidOperation(() => reader.Read());
            }
            c.Close();
            c.Open();
            VerifyIdentity(c);
            prepareDisposeSafe &= Convert.ToInt32(Scalar(c, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        }
        if (!prepareDisposeSafe) throw new ProbeFailure("prepare_dispose_not_failclosed");

        stage = "lob_setup";
        Exec(owner, $"UPDATE {table} SET PAYLOAD = :p0 WHERE ID = :p1", payload, 1);
        stage = "lob_chunks";
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT PAYLOAD FROM {table} WHERE ID = 1";
            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            if (!reader.Read()) throw new ProbeFailure("lob_row_missing");
            var observed = new byte[payload.Length];
            long offset = 0;
            while (offset < observed.Length)
            {
                long copied = reader.GetBytes(0, offset, observed, (int)offset, Math.Min(4096, observed.Length - (int)offset));
                if (copied <= 0) throw new ProbeFailure("lob_getbytes_short");
                offset += copied;
            }
            lobGetBytes = observed.SequenceEqual(payload);
            if (!lobGetBytes) throw new ProbeFailure("lob_getbytes_invalid");
        }
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT PAYLOAD FROM {table} WHERE ID = 1";
            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            if (!reader.Read()) throw new ProbeFailure("lob_stream_row_missing");
            using var stream = reader.GetStream(0);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            lobStream = memory.ToArray().SequenceEqual(payload);
            if (!lobStream) throw new ProbeFailure("lob_stream_invalid");
        }

        stage = "direct_lob_stream_ownership";
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT PAYLOAD FROM {table} WHERE ID = 1";
            using var reader = (DmDataReader)command.ExecuteReader(CommandBehavior.SequentialAccess);
            if (!reader.Read()) throw new ProbeFailure("direct_lob_row_missing");
            using var stream = reader.GetBlob((short)0).GetStream();
            if (stream is not DmBLobStream || stream.Length != payload.Length)
                throw new ProbeFailure("direct_lob_length_invalid");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            long sessionId = owner.Session.SessionId;
            DmDataReader.AfterInvocationEntered = identity =>
            {
                if (identity.SessionId != sessionId) return;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("direct_lob_barrier_timeout");
            };
            try
            {
                var heldRead = Task.Run(() => reader.Read());
                if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("direct_lob_barrier_not_entered");
                bool lengthRejected = RejectsWithoutWire(() => _ = stream.Length);
                bool seekRejected = RejectsWithoutWire(() => stream.Seek(0, SeekOrigin.Current));
                release.Set();
                if (await heldRead) throw new ProbeFailure("direct_lob_unexpected_second_row");
                reader.Close();
                bool closedRejected = RejectsWithoutWire(() => _ = stream.Length);
                directLobStreamOwnership = lengthRejected && seekRejected && closedRejected;
            }
            finally { release.Set(); DmDataReader.AfterInvocationEntered = null; }
            if (!directLobStreamOwnership) throw new ProbeFailure("direct_lob_ownership_invalid");
        }

        stage = "primary_key_dictionary_view";
        primaryKeyViewVerified = Convert.ToInt32(Scalar(owner,
            "SELECT COUNT(*) FROM ALL_CONS_COLUMNS CC JOIN ALL_CONSTRAINTS C " +
            "ON CC.OWNER=C.OWNER AND CC.CONSTRAINT_NAME=C.CONSTRAINT_NAME " +
            "WHERE C.OWNER=:p0 AND C.TABLE_NAME=:p1 AND C.CONSTRAINT_TYPE='P' " +
            "AND CC.COLUMN_NAME='ID' AND CC.POSITION=1", TestUser, table),
            CultureInfo.InvariantCulture) == 1;
        if (!primaryKeyViewVerified) throw new ProbeFailure("primary_key_dictionary_view_missing");

        stage = "state_callback_lock";
        stateCallbackOutsideLock = StateCallbacksRunWithoutLock(settings);
        if (!stateCallbackOutsideLock) throw new ProbeFailure("state_callback_lock_held");

        stage = "connection_schema";
        var tables = owner.GetSchema("Tables");
        var columns = owner.GetSchema("Columns");
        connectionSchemaSafe = tables is not null && columns is not null &&
            tables.Rows.Cast<DataRow>().Any(row =>
                string.Equals(Convert.ToString(row["TABLE_NAME"], CultureInfo.InvariantCulture), table, StringComparison.OrdinalIgnoreCase)) &&
            columns.Rows.Cast<DataRow>().Any(row =>
                string.Equals(Convert.ToString(row["TABLE_NAME"], CultureInfo.InvariantCulture), table, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Convert.ToString(row["COLUMN_NAME"], CultureInfo.InvariantCulture), "ID", StringComparison.OrdinalIgnoreCase));
        if (!connectionSchemaSafe) throw new ProbeFailure("connection_schema_missing");

        stage = "reader_schema";
        using (var command = owner.CreateCommand())
        {
            command.CommandText = $"SELECT ID FROM {table} ORDER BY ID";
            using var reader = command.ExecuteReader();
            var schemaTable = reader.GetSchemaTable();
            schemaTableSafe = schemaTable is not null && schemaTable.Rows.Count == 1 &&
                string.Equals(Convert.ToString(schemaTable.Rows[0]["ColumnName"], CultureInfo.InvariantCulture),
                    "ID", StringComparison.OrdinalIgnoreCase) &&
                schemaTable.Rows[0]["IsKey"] == DBNull.Value &&
                schemaTable.Rows[0]["IsUnique"] == DBNull.Value;
            if (!schemaTableSafe) throw new ProbeFailure("reader_schema_missing");
        }

        stage = "state_events";
        var events = new List<(ConnectionState Old, ConnectionState New)>();
        owner.StateChange += (_, e) => events.Add((e.OriginalState, e.CurrentState));
        owner.Close();
        owner.Close();
        closeIdempotent = events.Count(e => e.New == ConnectionState.Closed) == 1;
        if (!closeIdempotent) throw new ProbeFailure("duplicate_close_event");
    }
    catch (Exception ex) { workError = ex; workStage = stage; }
    stage = "cleanup";
    try
    {
        using var cleaner = OpenVerified(settings);
        if (ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
        using var check = OpenVerified(settings);
        cleanupVerified = !ObjectExists(check, table);
    }
    catch (Exception ex) { cleanupError = ex; }
    result["server_account_verified"] = accountVerified;
    result["reader_ownership_verified"] = readerOwnership;
    result["transaction_conflict_verified"] = transactionConflict;
    result["close_idempotent_verified"] = closeIdempotent;
    result["independent_connection_verified"] = independentConnection;
    result["cleanup_verified"] = cleanupVerified;
    result["same_reader_failfast_verified"] = sameReaderFailFast;
    result["same_reader_zero_send_verified"] = sameReaderNoSend;
    result["competing_command_zero_send_verified"] = competingCommandNoSend;
    result["simultaneous_wire_verified"] = simultaneousWire;
    result["after_send_failclosed_verified"] = afterSendFailureClosed;
    result["before_decode_failclosed_verified"] = decodeFailureClosed;
    result["stale_close_safe_verified"] = staleCloseSafe;
    result["lob_getbytes_verified"] = lobGetBytes;
    result["lob_stream_verified"] = lobStream;
    result["direct_lob_stream_ownership_verified"] = directLobStreamOwnership;
    result["old_transaction_safe_verified"] = oldTransactionSafe;
    result["dispose_no_implicit_commit_verified"] = disposeDidNotCommit;
    result["prepare_dispose_safe_verified"] = prepareDisposeSafe;
    result["schema_table_verified"] = schemaTableSafe;
    result["cancel_does_not_reconnect_verified"] = cancelDoesNotReconnect;
    result["close_during_handshake_verified"] = closeDuringHandshakeSafe;
    result["connection_schema_verified"] = connectionSchemaSafe;
    result["primary_key_dictionary_view_verified"] = primaryKeyViewVerified;
    result["state_callback_outside_lock_verified"] = stateCallbackOutsideLock;
    result["final_database_state"] = cleanupVerified ? "random_object_absent" : "not_verified";
    if (workError is not null)
    {
        result["work_stage"] = workStage;
        result["work_error_kind"] = workError is ProbeFailure failure ? failure.Kind : workError.GetType().Name;
        result["work_error_number"] = SafeNumber(workError);
        result["work_origin"] = workError.TargetSite?.DeclaringType?.Name;
        result["work_method"] = workError.TargetSite?.Name;
        result["work_methods"] = new StackTrace(workError, true).GetFrames()?.Take(8)
            .Select(frame => new { type = frame.GetMethod()?.DeclaringType?.Name, method = frame.GetMethod()?.Name, line = frame.GetFileLineNumber() })
            .ToArray();
    }
    if (cleanupError is not null)
    {
        result["cleanup_error_kind"] = cleanupError.GetType().Name;
        result["cleanup_error_number"] = SafeNumber(cleanupError);
        throw new ProbeFailure("cleanup_failed");
    }
    if (workError is not null) throw workError;
    if (!cleanupVerified) throw new ProbeFailure("cleanup_unverified");
    result["status"] = "real_verified";
    result["integration"] = "real_test_schema";
}
catch (Exception ex)
{
    result["status"] = "rejected";
    result["stage"] = stage;
    result["error_kind"] = ex is ProbeFailure failure ? failure.Kind : ex.GetType().Name;
    result["error_number"] = SafeNumber(ex);
    result["integration"] = "not_verified";
    Environment.ExitCode = 1;
}
Console.WriteLine(JsonSerializer.Serialize(result));

static bool FaultAndRecover(string settings, bool afterSend)
{
    using var c = OpenVerified(settings);
    long target = c.Session.SessionId;
    Action<OperationIdentity, DmOperationPurpose> injection = (identity, _) =>
    {
        if (identity.SessionId == target) throw new ProbeFailure(afterSend ? "injected_after_send" : "injected_before_decode");
    };
    if (afterSend) DmWireTestHooks.AfterSendBeforeReceive = injection;
    else DmWireTestHooks.BeforeDecode = injection;
    bool reached;
    try
    {
        try { Scalar(c, "SELECT 1 FROM DUAL"); reached = false; }
        catch (ProbeFailure failure) { reached = failure.Kind == (afterSend ? "injected_after_send" : "injected_before_decode"); }
    }
    finally
    {
        DmWireTestHooks.AfterSendBeforeReceive = null;
        DmWireTestHooks.BeforeDecode = null;
    }
    bool broken = c.Session.State == DmPhysicalSessionState.Broken &&
                  RejectsInvalidOperation(() => Scalar(c, "SELECT 1 FROM DUAL"));
    c.Close();
    c.Open();
    VerifyIdentity(c);
    return reached && broken && Convert.ToInt32(Scalar(c, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
}

static async Task<bool> CloseAndReopenAcrossAbortBarrier(string settings)
{
    using var c = OpenVerified(settings);
    long oldSessionId = c.Session.SessionId;
    using var captured = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    DmSessionTestHooks.BeforeConnectionTransportAbort = id =>
    {
        if (id != oldSessionId) return;
        captured.Set();
        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("old_abort_barrier_timeout");
    };
    try
    {
        var close = Task.Run(() => c.Close());
        if (!captured.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("old_abort_not_captured");
        c.Open();
        long newSessionId = c.Session.SessionId;
        bool beforeRelease = newSessionId != oldSessionId &&
            Convert.ToInt32(Scalar(c, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        release.Set();
        await close;
        bool afterRelease = c.Session.SessionId == newSessionId &&
            Convert.ToInt32(Scalar(c, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        return beforeRelease && afterRelease;
    }
    finally { release.Set(); DmSessionTestHooks.BeforeConnectionTransportAbort = null; }
}

static async Task<bool> CloseDuringHandshake(string settings)
{
    using var c = new DmConnection(settings);
    var events = new List<ConnectionState>();
    c.StateChange += (_, e) => events.Add(e.CurrentState);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    int calls = 0;
    DmSessionTestHooks.AfterExecutionAcquired = _ =>
    {
        if (Interlocked.Increment(ref calls) != 1) return;
        entered.Set();
        if (!release.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("handshake_barrier_timeout");
    };
    try
    {
        var opening = Task.Run(() => c.Open());
        if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new ProbeFailure("handshake_barrier_not_entered");
        var closing = Task.Run(() => c.Close());
        bool closedPromptly = closing.Wait(TimeSpan.FromSeconds(10)) && c.State == ConnectionState.Closed;
        release.Set();
        bool openingRejected;
        try { await opening; openingRejected = false; }
        catch (InvalidOperationException) { openingRejected = true; }
        await closing;
        bool closedOnce = events.Count(s => s == ConnectionState.Closed) == 1;
        c.Open();
        VerifyIdentity(c);
        c.Close();
        return closedPromptly && openingRejected && closedOnce && c.State == ConnectionState.Closed;
    }
    finally { release.Set(); DmSessionTestHooks.AfterExecutionAcquired = null; }
}

static bool StateCallbacksRunWithoutLock(string settings)
{
    using var c = new DmConnection(settings);
    bool connecting = false, opened = false, closed = false;
    c.StateChange += (_, e) =>
    {
        var work = Task.Run(() =>
        {
            _ = c.ConnectionString;
            if (e.CurrentState == ConnectionState.Closed)
            {
                c.ConnectionString = settings;
                return true;
            }
            return RejectsInvalidOperation(() => c.ConnectionString = settings);
        });
        bool completed = work.Wait(TimeSpan.FromSeconds(10)) && work.Result;
        if (e.CurrentState == ConnectionState.Connecting) connecting = completed;
        else if (e.CurrentState == ConnectionState.Open) opened = completed;
        else if (e.CurrentState == ConnectionState.Closed) closed = completed;
    };
    c.Open();
    VerifyIdentity(c);
    c.Close();
    return connecting && opened && closed;
}

static DmConnection OpenVerified(string settings)
{
    var c = new DmConnection(settings);
    try { c.Open(); VerifyIdentity(c); return c; }
    catch { c.Dispose(); throw; }
}
static void VerifyIdentity(DmConnection c)
{
    if (!string.Equals(Scalar(c, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(c.Schema, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("server_identity_invalid");
}
static bool ObjectExists(DmConnection c, string table) =>
    Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table), CultureInfo.InvariantCulture) != 0;
static int Count(DmConnection c, string table) =>
    Convert.ToInt32(Scalar(c, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
static object? Scalar(DmConnection c, string sql, params object[] values)
{ using var command = Command(c, sql, values); return command.ExecuteScalar(); }
static int Exec(DmConnection c, string sql, params object[] values)
{ using var command = Command(c, sql, values); return command.ExecuteNonQuery(); }
static int ExecWithTransaction(DmConnection c, DbTransaction tx, string sql)
{ using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = sql; return command.ExecuteNonQuery(); }
static DbCommand Command(DmConnection c, string sql, object[] values)
{
    var command = c.CreateCommand();
    command.CommandText = sql;
    for (int i = 0; i < values.Length; i++)
    {
        var p = command.CreateParameter();
        p.ParameterName = "p" + i.ToString(CultureInfo.InvariantCulture);
        p.DbType = values[i] switch { int => DbType.Int32, byte[] => DbType.Binary, _ => DbType.String };
        p.Value = values[i];
        command.Parameters.Add(p);
    }
    return command;
}
static bool RejectsInvalidOperation(Action action)
{ try { action(); return false; } catch (InvalidOperationException) { return true; } }
static bool RejectsNotSupported(Action action)
{ try { action(); return false; } catch (NotSupportedException) { return true; } }
static bool RejectsWithoutWire(Action action)
{
    long sends = DmWireTestHooks.SendCount, bytes = DmWireTestHooks.SentBytes;
    return RejectsInvalidOperation(action) &&
           DmWireTestHooks.SendCount == sends && DmWireTestHooks.SentBytes == bytes;
}
static int? SafeNumber(Exception ex)
{ try { return ex.GetType().GetProperty("Number")?.GetValue(ex) is int n ? n : null; } catch { return null; } }
sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
