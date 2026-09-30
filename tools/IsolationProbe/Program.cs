using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
#if OFFICIAL || RESTORED
using DriverConnection = Dm.DmConnection;
#else
using DriverConnection = W.Dm.DmConnection;
#endif

internal static partial class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static int verifiedConnections;
    private static readonly string Implementation =
#if OFFICIAL
        "O";
#elif RESTORED
        "R";
#elif FROZEN
        "W-T10-frozen";
#else
        "W-T11-candidate";
#endif
    private static readonly string[] Shapes = ["single_update", "update_rowcount", "insert_key", "savepoint_keyword", "savepoint_ef", "repeated_prepare"];
    private static readonly IsolationLevel[] Levels = [IsolationLevel.ReadCommitted, IsolationLevel.ReadUncommitted, IsolationLevel.Serializable];

    internal static int Main(string[] args)
    {
        var assembly = typeof(DriverConnection).Assembly;
        var report = new Dictionary<string, object?> {
            ["schema_version"] = 1, ["task"] = "T11", ["implementation"] = Implementation,
            ["assembly_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            ["assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            ["evidence_kind"] = "ADO_matrix_observation", ["fixture_execution_status"] = "not_run" };
        string? raw = null;
        string table = "T11_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        string insertTable = "T11I_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        bool identity = false, cleaned = false;
        object? workFailure = null;
        var levels = new List<object>();
        bool candidate = args.Length == 1 && args[0] is "candidate-matrix" or "candidate-litmus" or "candidate-begin-trace";
        bool publicEntry = args.Length == 1 && args[0] is "public-matrix" or "public-litmus";
        bool litmus = args.Length == 1 && args[0] is "candidate-litmus" or "public-litmus";
        report["entry"] = publicEntry ? "public" : candidate ? "internal_profile" : "public_baseline";
        bool beginOnly = args.Length == 1 && args[0] == "candidate-begin-trace";
        if (beginOnly) report["evidence_kind"] = "isolation_begin_trace_observation";
        if (litmus) report["evidence_kind"] = "server_litmus_observation";
        bool minimal = args.Length == 1 && args[0] == "causal";
        report["matrix_scope"] = beginOnly ? "begin_trace_only" : minimal ? "minimal_causal" : "full_ADO";
        try
        {
            if (args.Length != 1 || args[0] is not ("matrix" or "candidate-matrix" or "candidate-litmus" or "candidate-begin-trace" or "public-matrix" or "public-litmus" or "causal")) throw new InvalidOperationException("usage");
#if !CANDIDATE
            if (candidate || publicEntry) throw new InvalidOperationException("candidate_mode_requires_candidate_binary");
#endif
            raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("test_connection_missing");
            if (!raw.Contains("User Id=" + TestUser + ";", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("configured_identity_invalid");
            using (var setup = OpenVerified(raw))
            {
                identity = true;
                report["server_version"] = setup.ServerVersion;
                if (!beginOnly)
                {
                    Execute(setup, null, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL INT)");
                    Execute(setup, null, $"CREATE TABLE {insertTable} (ID INT IDENTITY(1,1) PRIMARY KEY, VAL INT)");
                }
                report["objects_created"] = !beginOnly;
            }
            if (litmus) levels.AddRange(RunLitmus(raw, table, candidate));
            else
            {
            int caseId = 100;
            foreach (IsolationLevel level in Levels)
            {
                object? beginFailure = null;
                bool? retainedControlStatement = null, publicResetReadCommitted = null;
                var beginTrace = new List<object>();
                try
                {
                    using var connection = OpenVerified(raw);
                    using (var trace = new FrameTrace(beginTrace, connection))
                    using (var transaction = Begin(connection, level, candidate))
                    {
                        retainedControlStatement = ProbeSafety.TransactionStatement(transaction) != null;
                        transaction.Rollback();
                    }
                    using var reset = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                    publicResetReadCommitted = reset.IsolationLevel == IsolationLevel.ReadCommitted;
                    reset.Rollback();
                }
                catch (Exception ex) { beginFailure = ProbeSafety.Failure(ex); }
                var observations = new List<object>();
                if (beginFailure == null && !beginOnly)
                {
                    bool[] interventions = Implementation is "O" or "R" ? [false, true] : [false];
                    foreach (bool causal in interventions)
                    foreach (bool parameterized in new[] { false, true })
                    foreach (string shape in minimal ? new[] { "update_rowcount" } : Shapes)
                        observations.Add(RunCase(raw, table, insertTable, ++caseId, level, candidate, causal, parameterized, shape));
                }
                levels.Add(new { isolation = level.ToString(), begin_succeeded = beginFailure == null,
                    begin_failure = beginFailure, control_statement_retained = retainedControlStatement,
                    control_trace = beginTrace, public_reset_read_committed = publicResetReadCommitted, cases = observations,
                    scope = beginFailure == null ? "observed" : "public_level_rejected_no_case_execution" });
            }
            }
            report["levels"] = levels;
        }
        catch (Exception ex) { workFailure = ProbeSafety.Failure(ex); }
        finally
        {
            if (raw != null && identity)
            {
                try
                {
                    using var cleaner = OpenVerified(raw);
                    foreach (string name in new[] { insertTable, table })
                        if (ObjectExists(cleaner, name)) Execute(cleaner, null, $"DROP TABLE {name}");
                    using var fresh = OpenVerified(raw);
                    cleaned = !ObjectExists(fresh, table) && !ObjectExists(fresh, insertTable);
                }
                catch (Exception ex) { report["cleanup_failure"] = ProbeSafety.Failure(ex); }
            }
        }
        report["server_account_verified"] = identity;
        report["verified_connection_count"] = verifiedConnections;
        report["cleanup_verified"] = cleaned;
        report["final_database_state"] = cleaned ? "unique_objects_absent" : "cleanup_unverified";
        report["work_failure"] = workFailure;
        bool passed = workFailure == null && cleaned && levels.Count == 3;
        report["status"] = passed ? "matrix_observed" : "rejected";
        Console.WriteLine(JsonSerializer.Serialize(report));
        return passed ? 0 : 1;
    }

    private static object RunCase(string raw, string table, string insertTable, int id, IsolationLevel level,
        bool candidate, bool causal, bool parameterized, string shape)
    {
        var commands = new List<object>();
        var results = new List<object>();
        object? failure = null, endFailure = null, finalFailure = null;
        string? outcome = null;
        long? finalValue = null, finalInsertRows = null;
        bool ended = false;
        bool? retainedControlStatement = null;
        bool commit = shape == "repeated_prepare";
        using (var seed = OpenVerified(raw)) Execute(seed, null, $"INSERT INTO {table}(ID,VAL) VALUES ({id},0)");
        using var connection = OpenVerified(raw);
        DbTransaction? transaction = null;
        try
        {
            transaction = Begin(connection, level, candidate);
            retainedControlStatement = ProbeSafety.TransactionStatement(transaction) != null;
            if (shape is "savepoint_keyword" or "savepoint_ef")
            {
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    int before = repeat * 2 + 1;
                    int after = before + 1;
                    AuditNonQuery(connection, transaction, Update(table, id, parameterized, before),
                        parameterized ? before : null, causal, commands, results);
                    AuditNonQuery(connection, transaction, "SAVEPOINT \"T11_SAVE\"", null, causal, commands, results);
                    AuditReader(connection, transaction, Update(table, id, parameterized, after) + "; SELECT SQL%ROWCOUNT;",
                        parameterized ? after : null, causal, commands, results, prepare: false, repeat: 1);
                    AuditNonQuery(connection, transaction, shape == "savepoint_ef" ? "ROLLBACK TO \"T11_SAVE\"" :
                        "ROLLBACK TO SAVEPOINT \"T11_SAVE\"", null, causal, commands, results);
                    using var read = Command(connection, transaction, $"SELECT VAL FROM {table} WHERE ID={id}", null, causal, commands);
                    if (Convert.ToInt64(read.ExecuteScalar(), CultureInfo.InvariantCulture) != before)
                        throw new InvalidOperationException("savepoint_value_mismatch");
                    AuditNonQuery(connection, transaction, "RELEASE SAVEPOINT \"T11_SAVE\"", null, causal, commands, results);
                }
            }
            else
            {
                string sql = shape == "insert_key"
                    ? $"INSERT INTO {insertTable}(VAL) VALUES ({(parameterized ? ":p0" : id.ToString(CultureInfo.InvariantCulture))}); SELECT ID FROM {insertTable} WHERE ID=SCOPE_IDENTITY();"
                    : Update(table, id, parameterized, 1) + (shape == "update_rowcount" ? "; SELECT SQL%ROWCOUNT;" : "");
                using var command = Command(connection, transaction, sql, parameterized ? (shape == "insert_key" ? id : 1) : null, causal, commands);
                if (shape == "repeated_prepare")
                {
                    try { command.Prepare(); }
                    finally { commands.Add(new { phase = "after_prepare", snapshot = ProbeSafety.Snapshot(command, transaction) }); }
                }
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    if (shape != "insert_key")
                    {
                        if (parameterized) command.Parameters[0].Value = repeat + 1;
                        else command.CommandText = Update(table, id, false, repeat + 1) +
                            (shape == "update_rowcount" ? "; SELECT SQL%ROWCOUNT;" : "");
                    }
                    try
                    {
                        if (shape is "update_rowcount" or "insert_key") results.Add(ReadAll(command, shape == "insert_key" ? "key" : "rowcount", transaction, commands));
                        else
                        {
                            int affected = command.ExecuteNonQuery();
                            results.Add(new { affected });
                            if (affected != 1) throw new InvalidOperationException("affected_count_mismatch");
                        }
                    }
                    finally { commands.Add(new { phase = "after_execute", snapshot = ProbeSafety.Snapshot(command, transaction) }); }
                }
            }
            if (commit) transaction.Commit(); else transaction.Rollback();
            ended = true;
        }
        catch (Exception ex) { failure = ProbeSafety.Failure(ex); }
        finally
        {
            if (transaction != null)
            {
                if (!ended)
                    try { transaction.Rollback(); ended = true; }
                    catch (Exception ex) { endFailure = ProbeSafety.Failure(ex); }
                outcome = transaction.GetType().GetProperty("Outcome")?.GetValue(transaction)?.ToString();
            }
            // The intervention never edits a shared statement owner or the transaction.
            // End the physical session before inspecting independent final state.
            try { connection.Close(); } catch (Exception ex) { endFailure ??= ProbeSafety.Failure(ex); }
            try { transaction?.Dispose(); } catch (Exception ex) { endFailure ??= ProbeSafety.Failure(ex); }
        }
        try
        {
            using var fresh = OpenVerified(raw);
            finalValue = Convert.ToInt64(Scalar(fresh, $"SELECT VAL FROM {table} WHERE ID={id}"), CultureInfo.InvariantCulture);
            finalInsertRows = Convert.ToInt64(Scalar(fresh, $"SELECT COUNT(*) FROM {insertTable} WHERE VAL={id}"), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { finalFailure = ProbeSafety.Failure(ex); }
        bool correct = failure == null && endFailure == null && finalFailure == null && ended &&
            finalValue == (commit ? 2 : 0) && finalInsertRows == 0;
        return new { case_id = id, shape, parameterized, entry = candidate ? "internal_profile" : "public", causal_intervention = causal ? "clear_inherited_user_statement_only" : "none",
            requested_isolation = level.ToString(), requested_end = commit ? "commit" : "rollback",
            functional_contract = correct, failure, end_failure = endFailure, final_failure = finalFailure,
            transaction_outcome = outcome, control_statement_retained = retainedControlStatement, connection_closed = connection.State == ConnectionState.Closed,
            final_value = finalValue, final_insert_rows = finalInsertRows, commands, results };
    }

    private static string Update(string table, int id, bool parameterized, int value)
        => $"UPDATE {table} SET VAL={(parameterized ? ":p0" : value.ToString(CultureInfo.InvariantCulture))} WHERE ID={id}";

    private static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql, int? value,
        bool causal, List<object> observations)
    {
        var command = connection.CreateCommand();
        try
        {
            command.CommandTimeout = 5;
            command.CommandText = sql;
            if (value is { } number) Add(command, "p0", number);
            command.Transaction = transaction;
            observations.Add(new { phase = "after_transaction_assignment", snapshot = ProbeSafety.Snapshot(command, transaction) });
            bool changed = causal && ProbeSafety.ClearInheritedStatement(command, transaction);
            if (causal) observations.Add(new { phase = "after_causal_intervention", changed,
                snapshot = ProbeSafety.Snapshot(command, transaction) });
            return command;
        }
        catch { command.Dispose(); throw; }
    }

    private static void AuditNonQuery(DbConnection connection, DbTransaction transaction, string sql, int? value,
        bool causal, List<object> observations, List<object> results)
    {
        using var command = Command(connection, transaction, sql, value, causal, observations);
        try { results.Add(new { affected = command.ExecuteNonQuery() }); }
        finally { observations.Add(new { phase = "after_execute", snapshot = ProbeSafety.Snapshot(command, transaction) }); }
    }

    private static void AuditReader(DbConnection connection, DbTransaction transaction, string sql, int? value,
        bool causal, List<object> observations, List<object> results, bool prepare, int repeat)
    {
        using var command = Command(connection, transaction, sql, value, causal, observations);
        if (prepare) command.Prepare();
        for (int index = 0; index < repeat; index++)
            try { results.Add(ReadAll(command, "rowcount", transaction, observations)); }
            finally { observations.Add(new { phase = "after_execute", snapshot = ProbeSafety.Snapshot(command, transaction) }); }
    }

    private static object ReadAll(DbCommand command, string expectedResult, DbTransaction transaction, List<object> observations)
    {
        var rowsets = new List<object>();
        using var reader = command.ExecuteReader();
        observations.Add(new { phase = "after_execute_reader", snapshot = ProbeSafety.Snapshot(command, transaction, reader) });
        int set = 0;
        var allValues = new List<long>();
        do
        {
            var values = new List<long>();
            int rows = 0;
            while (reader.Read())
            {
                rows++;
                if (reader.FieldCount > 0 && !reader.IsDBNull(0)) values.Add(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
                if (rows > 8) throw new InvalidOperationException("row_bound_exceeded");
            }
            allValues.AddRange(values);
            rowsets.Add(new { columns = reader.FieldCount, rows, values });
            if (++set > 8) throw new InvalidOperationException("resultset_bound_exceeded");
            observations.Add(new { phase = "before_next_result", snapshot = ProbeSafety.Snapshot(command, transaction, reader) });
        } while (reader.NextResult()); // O/R wrong-owner SQL is read in this path before their EF flag check.
        if (allValues.Count != 1 || (expectedResult == "rowcount" ? allValues[0] != 1 : allValues[0] <= 0))
            throw new InvalidOperationException("compound_result_value_mismatch");
        return new { rowsets, reader_closed_after_dispose = true };
    }

    private sealed class FrameTrace : IDisposable
    {
        private readonly DriverConnection connection;
        private readonly List<object> events;
        internal FrameTrace(List<object> events, DriverConnection connection)
        {
            this.connection = connection;
            this.events = events;
#if CANDIDATE
            W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterFrame = (request, response, code, length) =>
                events.Add(new { event_kind = "validated_frame", request_opcode = request, response_opcode = response,
                    sql_code = code, body_length = length, configuration = ConfigurationSnapshot() });
            W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterStatementDecode =
                (request, resultType, rows, affected, columns, rowset, terminal) =>
                    events.Add(new { event_kind = "statement_decode", request_opcode = request,
                        ret_stmt_type = resultType, row_count = rows, records_affected = affected,
                        column_count = columns, has_result_set = rowset, is_terminal = terminal,
                        configuration = ConfigurationSnapshot(), control_level = ControlLevelSnapshot(request, resultType) });
#endif
        }
        private object ControlLevelSnapshot(short request, int resultType)
        {
#if CANDIDATE
            try
            {
                if (request != 5 || resultType is not (150 or 166))
                    return new { available = false, skip = "not_verified_SET_response_shape" };
                var physical = connection.GetConnInstance();
                var candidates = physical?.Stmts.Where(item => !item.P() &&
                    item.F()?.GetRetStmtType() == resultType && item.__t02_field_04000926.k() == 4 &&
                    ((item.C()?.StartsWith("SET TRANSACTION ISOLATION LEVEL", StringComparison.OrdinalIgnoreCase) ?? false) ||
                     (item.f()?.CommandText?.StartsWith("SET TRANSACTION ISOLATION LEVEL", StringComparison.OrdinalIgnoreCase) ?? false)))
                    .ToArray();
                if (candidates == null || candidates.Length != 1)
                    return new { available = false, skip = "control_statement_not_unique", candidate_count = candidates?.Length };
                var statement = candidates[0];
                var body = statement.__t02_field_04000926;
                int before = body.a();
                short level = body.C(64); // Absolute bounded read; does not advance the buffer cursor.
                return new { available = true, response_level = level, body_length = body.k(),
                    remaining = body.a(false), cursor_unchanged = before == body.a(),
                    owner_serial_flag = statement.f()?.GetStmtSerial(), transaction_serial_flag = physical?.Transaction?.GetStmtSerial() };
            }
            catch { return new { available = false, skip = "metadata_unavailable" }; }
#else
            return new { available = false };
#endif
        }
        private object ConfigurationSnapshot()
        {
#if CANDIDATE
            try
            {
                var physical = connection.GetConnInstance();
                var isolation = physical?.ConnProperty.IsolationLevel;
                return new { available = physical != null, isolation = isolation?.ToString(),
                    isolation_value = isolation == null ? (int?)null : (int)isolation,
                    auto_commit = physical?.GetAutoCommit(), connection_state = connection.State.ToString() };
            }
            catch { return new { available = false }; } // Observation must never change the actual business outcome.
#else
            return new { available = false };
#endif
        }
        public void Dispose()
        {
#if CANDIDATE
            W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterFrame = null;
            W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterStatementDecode = null;
            events.Add(new { event_kind = "trace_end", configuration = ConfigurationSnapshot() });
#endif
        }
    }

    private static DbTransaction Begin(DriverConnection connection, IsolationLevel level, bool candidate)
    {
#if CANDIDATE
        if (candidate)
        {
            // The candidate has an explicit friend-assembly profile entry. Calling it
            // directly preserves product exception frames without reflection wrapping.
            return connection.BeginProfileProbeTransaction(level);
        }
#endif
        return connection.BeginTransaction(level);
    }

    private static DriverConnection OpenVerified(string raw)
    {
#if OFFICIAL || RESTORED
        var connection = new DriverConnection(raw);
#else
        var builder = new W.Dm.DmConnectionStringBuilder(raw) {
            TransportSecurity = W.Dm.DmTransportSecurity.PlaintextAllowed, PersistSecurityInfo = false, Schema = TestUser };
#if CANDIDATE
        builder.CommandTimeout = 5;
#endif
        var connection = new DriverConnection(builder.ConnectionString);
#endif
        try
        {
            connection.Open();
            if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("server_identity_invalid");
            Interlocked.Increment(ref verifiedConnections);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void Add(DbCommand command, string name, int value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name; parameter.DbType = DbType.Int32; parameter.Value = value;
        command.Parameters.Add(parameter);
    }
    private static object? Scalar(DbConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandTimeout = 5; command.CommandText = sql; return command.ExecuteScalar(); }
    private static void Execute(DbConnection connection, DbTransaction? transaction, string sql)
    { using var command = connection.CreateCommand(); command.CommandTimeout = 5; command.CommandText = sql; command.Transaction = transaction; command.ExecuteNonQuery(); }
    private static bool ObjectExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand(); command.CommandTimeout = 5;
        command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0";
        var parameter = command.CreateParameter(); parameter.ParameterName = "p0"; parameter.DbType = DbType.String; parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }
}
