using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
#if CANDIDATE
using W.Dm.Internal.Legacy.A;
#endif
#if OFFICIAL
using DriverConnection = Dm.DmConnection;
#else
using DriverConnection = W.Dm.DmConnection;
#endif

const string TestUser = "WDM_PROVIDER_TEST";
const string SyntheticValue = "T08-汉字-Ω";
string implementation =
#if OFFICIAL
    "O";
#elif CANDIDATE
    "W-T08-candidate";
#elif PACKAGE
    "W-T08-package";
#else
    "W-T07-baseline";
#endif
var assembly = typeof(DriverConnection).Assembly;
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T08",
    ["probe"] = args.Length == 1 && args[0] == "targeted" ? "targeted_result_trace" : "command_characterization",
    ["implementation"] = implementation,
    ["run_id"] = Guid.NewGuid().ToString("N"),
    ["assembly_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
    ["assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
    ["assembly_version"] = assembly.GetName().Version?.ToString(),
    ["opcode_observation"] =
#if CANDIDATE
        "metadata_only_internal_hooks"
#else
        "not_instrumented_in_frozen_baseline"
#endif
};
string? raw = null, table = null;
bool accountVerified = false, cleanupVerified = false;
var scenarios = new Dictionary<string, object?>();
string stage = "arguments";
Exception? workError = null, cleanupError = null;
bool targeted = args.Length == 1 && args[0] == "targeted";
bool candidateVerified = args.Length == 1 && args[0] == "candidate_verified";
try
{
    if (args.Length != 1 || args[0] is not ("real" or "targeted" or "candidate_verified"))
        throw new ProbeFailure("usage");
    stage = "configuration";
    raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new ProbeFailure("test_connection_missing");
    if (!raw.Contains("User Id=" + TestUser + ";", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("configured_account_invalid");
    table = "T08_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_name"] = table;
    stage = "server_identity";
    using (var owner = OpenVerified(raw))
    {
        accountVerified = true;
        stage = "create_table";
        Exec(owner, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(100), PAYLOAD BLOB)");
        Exec(owner, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", 1, SyntheticValue);
    }

    if (targeted)
    {
        scenarios["select_update_select"] = ReaderCase(raw,
            $"SELECT 1 FROM DUAL; UPDATE {table} SET VAL = 'T08_TARGETED' WHERE ID = 1; SELECT 2 FROM DUAL");
        using (var mixedReadback = OpenVerified(raw))
            result["mixed_final_value_after_scenario"] = SafeSyntheticValue(
                Scalar(mixedReadback, $"SELECT VAL FROM {table} WHERE ID = 1"));
        scenarios["nonquery_update_one"] = NonQueryCase(raw,
            $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", SyntheticValue, 1);
        scenarios["nonquery_update_zero"] = NonQueryCase(raw,
            $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", SyntheticValue, 999);
        scenarios["reader_update_one"] = ReaderCase(raw,
            $"UPDATE {table} SET VAL = 'T08_PURE' WHERE ID = 1");
        scenarios["reader_update_zero"] = ReaderCase(raw,
            $"UPDATE {table} SET VAL = 'T08_MISSING' WHERE ID = 999");
    }
    else
    {
    scenarios["two_selects"] = ReaderCase(raw, "SELECT 1 FROM DUAL; SELECT 2 FROM DUAL");
    scenarios["dml_then_rowcount"] = ReaderCase(raw,
        $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1; /*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT", SyntheticValue, 1);
    scenarios["dml_zero_then_rowcount"] = ReaderCase(raw,
        $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1; /*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT", SyntheticValue, 999);
    scenarios["empty_then_select"] = ReaderCase(raw,
        $"SELECT ID FROM {table} WHERE ID = -1; SELECT ID FROM {table} WHERE ID = 1");
    scenarios["later_sql_error"] = ReaderCase(raw,
        "SELECT 1 FROM DUAL; SELECT * FROM T08_MISSING_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant());
    scenarios["semicolon_literal_comment"] = ReaderCase(raw,
        "SELECT 'a;b' AS VAL FROM DUAL /* comment ; stays */; SELECT 2 FROM DUAL");
    scenarios["scalar_empty"] = ScalarCase(raw, $"SELECT ID FROM {table} WHERE ID = -1");
    scenarios["scalar_null"] = ScalarCase(raw, "SELECT CAST(NULL AS INT) FROM DUAL");
    scenarios["nonquery_later_error"] = NonQueryCase(raw,
        $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1; SELECT * FROM T08_MISSING_" +
        Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), SyntheticValue, 1);
    scenarios["update_one"] = NonQueryCase(raw, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", SyntheticValue, 1);
    scenarios["update_zero"] = NonQueryCase(raw, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", SyntheticValue, 999);
    scenarios["delete_zero"] = NonQueryCase(raw, $"DELETE FROM {table} WHERE ID = :p0", 999);
    scenarios["early_close_pending_insert"] = EarlyCloseCase(raw, table);
    if (candidateVerified)
    {
        scenarios["command_dispose_reader"] = CommandDisposeReaderCase(raw);
        scenarios["prepared_dispose"] = PreparedDisposeCase(raw);
        scenarios["schema_table_local"] = SchemaTableCase(raw, table);
        scenarios["mixed_metadata_lob"] = MixedMetadataLobCase(raw, table);
        scenarios["mixed_flags"] = MixedFlagsCase(raw, table);
        scenarios["invalid_binary_no_replay"] = InvalidBinaryNoReplayCase(raw, table);
        scenarios["generated_identity"] = GeneratedIdentityCase(raw);
        scenarios["generated_sequence"] = GeneratedSequenceCase(raw);
        scenarios["prepared_repeat_transaction"] = PreparedRepeatTransactionCase(raw, table);
        scenarios["later_error_recovery"] = LaterErrorRecoveryCase(raw);
        scenarios["delete_one"] = NonQueryCase(raw, $"DELETE FROM {table} WHERE ID = :p0", 1);
    }
    }
    using (var readback = OpenVerified(raw))
        result["server_final_row_count_before_cleanup"] = Convert.ToInt32(
            Scalar(readback, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
}
catch (Exception ex) { workError = ex; result["work_stage"] = stage; }
finally
{
    if (raw != null && table != null)
    {
        try
        {
            using var cleaner = OpenVerified(raw);
            if (ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            using var check = OpenVerified(raw);
            cleanupVerified = !ObjectExists(check, table);
        }
        catch (Exception ex) { cleanupError = ex; }
    }
}
result["server_account_verified"] = accountVerified;
result["cleanup_verified"] = cleanupVerified;
result["final_database_state"] = cleanupVerified ? "random_object_absent" : "not_verified";
result["scenarios"] = scenarios;
if (workError != null) result["work_error_kind"] = ErrorKind(workError);
if (cleanupError != null) result["cleanup_error_kind"] = ErrorKind(cleanupError);
string[] contractFailures = candidateVerified ? VerifyCandidateContracts(scenarios,
    requireProtocolTrace: implementation == "W-T08-candidate") : [];
result["contract_failures"] = contractFailures;
bool passed = workError == null && cleanupError == null && accountVerified && cleanupVerified &&
    scenarios.Count == (targeted ? 5 : candidateVerified ? 24 : 13) && contractFailures.Length == 0 &&
    (!candidateVerified || Convert.ToInt32(result["server_final_row_count_before_cleanup"], CultureInfo.InvariantCulture) == 0);
result["status"] = passed ? candidateVerified ? implementation == "W-T08-package"
    ? "package_verified" : "candidate_verified" : "observed" : "rejected";
Console.WriteLine(JsonSerializer.Serialize(result));
return passed ? 0 : 1;

static object ReaderCase(string raw, string sql, params object[] values)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        trace.Enabled = true;
        using var command = Command(connection, sql, values);
        command.CommandTimeout = 5;
        trace.Stage = "execute_reader";
        using var reader = command.ExecuteReader();
        var sequence = new List<object>();
        string? error = null;
        bool terminalObserved = false;
        for (int index = 0; index < 8; index++)
        {
            try
            {
                trace.Stage = "read_result_" + index.ToString(CultureInfo.InvariantCulture);
                int fields = reader.FieldCount;
                string[] names = Enumerable.Range(0, fields).Select(reader.GetName).ToArray();
                string[] clrTypes = Enumerable.Range(0, fields).Select(field =>
                {
                    try { return reader.GetFieldType(field).Name; }
                    catch (Exception ex) { return "error:" + ErrorKind(ex); }
                }).ToArray();
                string[] databaseTypes = Enumerable.Range(0, fields).Select(field =>
                {
                    try { return reader.GetDataTypeName(field); }
                    catch (Exception ex) { return "error:" + ErrorKind(ex); }
                }).ToArray();
                int rows = 0;
                string? first = null;
                while (rows < 8 && reader.Read())
                {
                    if (rows == 0 && fields > 0) first = SafeSyntheticValue(reader.GetValue(0));
                    rows++;
                }
                sequence.Add(new { kind = fields > 0 ? "rowset" : "update_count", field_count = fields, columns = names,
                    clr_types = clrTypes, database_types = databaseTypes,
                    rows, first_value = first, records_affected = reader.RecordsAffected });
                trace.Stage = "next_result_" + index.ToString(CultureInfo.InvariantCulture);
                if (!reader.NextResult()) { terminalObserved = true; break; }
            }
            catch (Exception ex) { error = ErrorKind(ex); break; }
        }
        object? terminalRepeat = null;
        if (terminalObserved)
        {
            var before = WireCounters();
            try
            {
                trace.Stage = "terminal_repeat";
                bool readAgain = reader.Read();
                bool nextAgain = reader.NextResult();
                var after = WireCounters();
                terminalRepeat = new { outcome = "completed", read_again = readAgain, next_again = nextAgain,
                    before = new { count = before.Count, bytes = before.Bytes },
                    after = new { count = after.Count, bytes = after.Bytes } };
            }
            catch (Exception ex)
            {
                var after = WireCounters();
                terminalRepeat = new { outcome = "error", error_kind = ErrorKind(ex),
                    before = new { count = before.Count, bytes = before.Bytes },
                    after = new { count = after.Count, bytes = after.Bytes } };
            }
        }
        trace.Stage = "close_reader";
        reader.Close();
        return new { outcome = error == null ? "completed" : "error", sequence,
            error_kind = error, records_affected = reader.RecordsAffected,
            terminal_observed = terminalObserved, terminal_repeat = terminalRepeat,
            connection_state = connection.State.ToString(), trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "execute_error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object ScalarCase(string raw, string sql)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        trace.Enabled = true;
        using var command = Command(connection, sql);
        trace.Stage = "execute_scalar";
        object? value = command.ExecuteScalar();
        return new { outcome = "completed", value_kind = value == null ? "null" :
            value == DBNull.Value ? "DBNull" : value.GetType().Name,
            first_value = SafeSyntheticValue(value), connection_state = connection.State.ToString(), trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object NonQueryCase(string raw, string sql, params object[] values)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        trace.Enabled = true;
        using var command = Command(connection, sql, values);
        command.CommandTimeout = 5;
        trace.Stage = "execute_nonquery";
        int affected = command.ExecuteNonQuery();
        return new { outcome = "completed", affected, connection_state = connection.State.ToString(), trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object EarlyCloseCase(string raw, string table)
{
    using var trace = new TraceScope();
    try
    {
        bool firstRead;
        var beforeExecute = WireCounters();
        (long? Count, long? Bytes) afterExecute = default, afterRead = default, afterClose = default;
        using (var connection = OpenVerified(raw))
        using (var command = Command(connection,
            $"SELECT 1 FROM DUAL; INSERT INTO {table} (ID, VAL) VALUES (99, 'pending')"))
        {
            beforeExecute = WireCounters();
            trace.Enabled = true;
            trace.Stage = "execute_reader";
            using var reader = command.ExecuteReader();
            afterExecute = WireCounters();
            trace.Stage = "first_read";
            firstRead = reader.Read();
            afterRead = WireCounters();
            trace.Stage = "close_reader";
            reader.Close();
            afterClose = WireCounters();
        }
        trace.Enabled = false;
        using var check = OpenVerified(raw);
        int inserted = Convert.ToInt32(Scalar(check, $"SELECT COUNT(*) FROM {table} WHERE ID = 99"),
            CultureInfo.InvariantCulture);
        if (inserted != 0) Exec(check, $"DELETE FROM {table} WHERE ID = 99");
        return new { outcome = "completed", first_read = firstRead, pending_insert_visible_after_close = inserted,
            wire_before_execute = new { count = beforeExecute.Count, bytes = beforeExecute.Bytes },
            wire_after_execute = new { count = afterExecute.Count, bytes = afterExecute.Bytes },
            wire_after_read = new { count = afterRead.Count, bytes = afterRead.Bytes },
            wire_after_close = new { count = afterClose.Count, bytes = afterClose.Bytes },
            trace = trace.Events,
            next_command_succeeded = Convert.ToInt32(Scalar(check, "SELECT 1 FROM DUAL"),
                CultureInfo.InvariantCulture) == 1 };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object CommandDisposeReaderCase(string raw)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        trace.Enabled = true;
        using var command = Command(connection, "SELECT 1 FROM DUAL; SELECT 2 FROM DUAL");
        trace.Stage = "execute_reader";
        using var reader = command.ExecuteReader();
        command.Dispose();
        trace.Stage = "read_after_command_dispose";
        bool first = reader.Read() && Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 1;
        bool secondResult = reader.NextResult();
        bool second = secondResult && reader.Read() &&
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 2;
        trace.Stage = "close_reader";
        reader.Close();
        trace.Enabled = false;
        bool ready = Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        return new { outcome = "completed", first, second, ready, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object PreparedDisposeCase(string raw)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        var command = Command(connection, "SELECT 1 FROM DUAL");
        string original = command.CommandText;
        trace.Enabled = true;
        trace.Stage = "prepare";
        command.Prepare();
        bool textPreserved = command.CommandText == original;
        trace.Stage = "dispose_prepared";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        command.Dispose();
        stopwatch.Stop();
        trace.Enabled = false;
        bool ready = Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        return new { outcome = "completed", command_text_preserved = textPreserved,
            cleanup_milliseconds = stopwatch.ElapsedMilliseconds, ready, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object SchemaTableCase(string raw, string table)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        using var command = Command(connection, $"SELECT ID, VAL FROM {table} WHERE ID = :p0", 1);
        trace.Enabled = true;
        trace.Stage = "execute_reader";
        using var reader = command.ExecuteReader();
        int fieldCount = reader.FieldCount;
        var before = WireCounters();
        trace.Stage = "get_schema_table";
        DataTable? schema = reader.GetSchemaTable();
        var after = WireCounters();
        bool local = before.Count == after.Count && before.Bytes == after.Bytes;
        object? isKey = schema != null && schema.Rows.Count > 0 ? schema.Rows[0]["IsKey"] : null;
        object? isUnique = schema != null && schema.Rows.Count > 0 ? schema.Rows[0]["IsUnique"] : null;
        trace.Stage = "close_reader";
        reader.Close();
        return new { outcome = "completed", field_count = fieldCount,
            schema_rows = schema?.Rows.Count, is_key_kind = isKey == DBNull.Value ? "DBNull" :
                isKey is bool value ? value ? "true" : "false" : "unknown",
            is_unique_kind = isUnique == DBNull.Value ? "DBNull" :
                isUnique is bool unique ? unique ? "true" : "false" : "unknown",
            no_extra_wire = local, before = new { count = before.Count, bytes = before.Bytes },
            after = new { count = after.Count, bytes = after.Bytes }, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object MixedMetadataLobCase(string raw, string table)
{
    using var trace = new TraceScope();
    try
    {
        byte[] payload = Enumerable.Range(0, 96 * 1024).Select(index => (byte)(index % 251)).ToArray();
        using var connection = OpenVerified(raw);
        if (Exec(connection, $"UPDATE {table} SET PAYLOAD = :p0 WHERE ID = :p1", payload, 1) != 1)
            throw new ProbeFailure("lob_setup_update_invalid");
        using var command = Command(connection,
            $"SELECT 1 AS FIRST_VALUE FROM DUAL; SELECT VAL, PAYLOAD FROM {table} WHERE ID = :p0", 1);
        trace.Enabled = true;
        trace.Stage = "execute_reader";
        using var reader = command.ExecuteReader();
        string[] firstNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        string[] firstTypes = Enumerable.Range(0, reader.FieldCount).Select(reader.GetDataTypeName).ToArray();
        string[] firstClrTypes = Enumerable.Range(0, reader.FieldCount).Select(field => reader.GetFieldType(field).Name).ToArray();
        bool first = reader.Read() && Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 1;
        trace.Stage = "next_result";
        if (!reader.NextResult()) throw new ProbeFailure("second_rowset_missing");
        string[] secondNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        string[] secondTypes = Enumerable.Range(0, reader.FieldCount).Select(reader.GetDataTypeName).ToArray();
        string[] secondClrTypes = Enumerable.Range(0, reader.FieldCount).Select(field => reader.GetFieldType(field).Name).ToArray();
        string? secondValueKind = reader.Read() ? reader.GetValue(0).GetType().Name : null;
        bool second = secondValueKind == "String" && string.Equals(reader.GetString(0), SyntheticValue, StringComparison.Ordinal);
        byte[] observed = new byte[payload.Length];
        long offset = 0;
        while (offset < observed.Length)
        {
            long copied = reader.GetBytes(1, offset, observed, (int)offset,
                Math.Min(4096, observed.Length - (int)offset));
            if (copied <= 0) throw new ProbeFailure("lob_short_read");
            offset += copied;
        }
        bool blob = observed.SequenceEqual(payload);
        trace.Stage = "close_reader";
        reader.Close();
        return new { outcome = "completed", first, second, blob, first_names = firstNames,
            second_names = secondNames, first_types = firstTypes, second_types = secondTypes,
            first_clr_types = firstClrTypes, second_clr_types = secondClrTypes,
            second_value_kind = secondValueKind,
            trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object MixedFlagsCase(string raw, string table)
{
    try
    {
        bool singleFirst, singleSecond, singleClosed;
        using (var connection = OpenVerified(raw))
        using (var command = Command(connection, "SELECT 1 FROM DUAL UNION ALL SELECT 2 FROM DUAL"))
        using (var reader = command.ExecuteReader(CommandBehavior.SingleRow | CommandBehavior.CloseConnection))
        {
            singleFirst = reader.Read();
            singleSecond = reader.Read();
            reader.Close();
            singleClosed = connection.State == ConnectionState.Closed;
        }
        bool sequentialBlob, sequentialClosed;
        using (var connection = OpenVerified(raw))
        using (var command = Command(connection, $"SELECT PAYLOAD FROM {table} WHERE ID = :p0", 1))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection))
        {
            if (!reader.Read()) throw new ProbeFailure("sequential_row_missing");
            using var stream = reader.GetStream(0);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            sequentialBlob = memory.Length == 96 * 1024;
            reader.Close();
            sequentialClosed = connection.State == ConnectionState.Closed;
        }
        bool schemaRejected, noWire, noDml;
        using (var connection = OpenVerified(raw))
        using (var command = Command(connection,
            $"INSERT INTO {table} (ID, VAL) VALUES (77, 'must_not_execute')"))
        {
            var before = WireCounters();
            try { using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo); schemaRejected = false; }
            catch (NotSupportedException) { schemaRejected = true; }
            var after = WireCounters();
            noWire = before.Count == after.Count && before.Bytes == after.Bytes;
            noDml = Convert.ToInt32(Scalar(connection,
                $"SELECT COUNT(*) FROM {table} WHERE ID = 77"), CultureInfo.InvariantCulture) == 0;
        }
        return new { outcome = "completed", single_first = singleFirst, single_second = singleSecond,
            single_close_connection = singleClosed, sequential_blob = sequentialBlob,
            sequential_close_connection = sequentialClosed, schema_keyinfo_rejected = schemaRejected,
            schema_keyinfo_no_wire = noWire, schema_keyinfo_no_dml = noDml };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
}

static object InvalidBinaryNoReplayCase(string raw, string table)
{
    using var trace = new TraceScope();
    try
    {
        using var connection = OpenVerified(raw);
        int beforeRows = Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
        var beforeWire = WireCounters();
        bool rejected;
        trace.Enabled = true;
        trace.Stage = "invalid_binary_execute";
        using (var command = Command(connection,
            $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", new byte[] { 91, 92, 93 }, "invalid"))
        {
            try { command.ExecuteNonQuery(); rejected = false; }
            catch (Exception) { rejected = true; }
        }
        trace.Enabled = false;
        var afterWire = WireCounters();
        string stateAfterFailure = connection.State.ToString();
        using var readback = OpenVerified(raw);
        int afterRows = Convert.ToInt32(Scalar(readback, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
        return new { outcome = "completed", rejected, rows_unchanged = beforeRows == afterRows,
            wire_sends_delta = beforeWire.Count is null || afterWire.Count is null ? (long?)null :
                afterWire.Count - beforeWire.Count, connection_state = stateAfterFailure, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object GeneratedIdentityCase(string raw)
{
    string table = "T08I_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    bool created = false, cleanup = false, generated = false, independent = false;
    int? identity = null;
    string? error = null;
    try
    {
        using var connection = OpenVerified(raw);
        Exec(connection, $"CREATE TABLE {table} (ID INT IDENTITY(1,1) PRIMARY KEY, VAL VARCHAR(100), STAMP VARCHAR(30) DEFAULT 'generated')");
        created = true;
        using (var command = Command(connection,
            $"INSERT INTO {table} (VAL) VALUES (:p0); SELECT ID, STAMP FROM {table} " +
            "WHERE SQL%ROWCOUNT = 1 AND ID = SCOPE_IDENTITY()", SyntheticValue))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                identity = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                generated = identity > 0 && string.Equals(reader.GetString(1), "generated", StringComparison.Ordinal);
            }
            reader.Close();
        }
        if (identity is int id)
        using (var readback = OpenVerified(raw))
            independent = Convert.ToInt32(Scalar(readback,
                $"SELECT COUNT(*) FROM {table} WHERE ID = :p0 AND VAL = :p1 AND STAMP = :p2",
                id, SyntheticValue, "generated"), CultureInfo.InvariantCulture) == 1;
    }
    catch (Exception ex) { error = ErrorKind(ex); }
    finally
    {
        try
        {
            using var cleaner = OpenVerified(raw);
            if (created && ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            using var check = OpenVerified(raw);
            cleanup = !ObjectExists(check, table);
        }
        catch (Exception ex) { error ??= "cleanup:" + ErrorKind(ex); }
    }
    return new { outcome = error == null ? "completed" : "error", error_kind = error,
        identity, generated_columns_verified = generated, independent_readback_verified = independent,
        cleanup_verified = cleanup };
}

static object GeneratedSequenceCase(string raw)
{
    string table = "T08Q_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    string sequence = "T08S_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    bool tableCreated = false, sequenceCreated = false, cleanup = false, generated = false, independent = false;
    long? key = null;
    string? error = null;
    try
    {
        using var connection = OpenVerified(raw);
        Exec(connection, $"CREATE SEQUENCE {sequence} START WITH 1 INCREMENT BY 1");
        sequenceCreated = true;
        Exec(connection, $"CREATE TABLE {table} (ID BIGINT PRIMARY KEY, VAL VARCHAR(100), STAMP VARCHAR(30) DEFAULT 'generated')");
        tableCreated = true;
        using (var command = Command(connection,
            $"INSERT INTO {table} (ID, VAL) VALUES ({sequence}.NEXTVAL, :p0); " +
            $"SELECT ID, STAMP FROM {table} WHERE SQL%ROWCOUNT = 1 AND ID = {sequence}.CURRVAL", SyntheticValue))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                key = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                generated = key > 0 && string.Equals(reader.GetString(1), "generated", StringComparison.Ordinal);
            }
            reader.Close();
        }
        if (key is long id)
        using (var readback = OpenVerified(raw))
            independent = Convert.ToInt32(Scalar(readback,
                $"SELECT COUNT(*) FROM {table} WHERE ID = :p0 AND VAL = :p1 AND STAMP = :p2",
                id, SyntheticValue, "generated"), CultureInfo.InvariantCulture) == 1;
    }
    catch (Exception ex) { error = ErrorKind(ex); }
    finally
    {
        try
        {
            using var cleaner = OpenVerified(raw);
            if (tableCreated && ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            if (sequenceCreated) Exec(cleaner, $"DROP SEQUENCE {sequence}");
            using var check = OpenVerified(raw);
            cleanup = !ObjectExists(check, table) && Convert.ToInt32(Scalar(check,
                "SELECT COUNT(*) FROM USER_SEQUENCES WHERE SEQUENCE_NAME = :p0", sequence),
                CultureInfo.InvariantCulture) == 0;
        }
        catch (Exception ex) { error ??= "cleanup:" + ErrorKind(ex); }
    }
    return new { outcome = error == null ? "completed" : "error", error_kind = error,
        sequence_key = key, generated_columns_verified = generated,
        independent_readback_verified = independent, cleanup_verified = cleanup };
}

static object PreparedRepeatTransactionCase(string raw, string table)
{
    try
    {
        using var connection = OpenVerified(raw);
        const string changed = "T08-TX-汉字";
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection,
            $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", SyntheticValue, 1);
        command.Transaction = transaction;
        string original = command.CommandText;
        command.Prepare();
        bool textPreserved = command.CommandText == original;
        int first = command.ExecuteNonQuery();
        command.Parameters[0].Value = changed;
        command.Prepare();
        textPreserved &= command.CommandText == original;
        int second = command.ExecuteNonQuery();
        using var reader = Command(connection, $"SELECT VAL FROM {table} WHERE ID = :p0", 1);
        reader.Transaction = transaction;
        bool transactionReadback = string.Equals(reader.ExecuteScalar()?.ToString(), changed,
            StringComparison.Ordinal);
        transaction.Commit();
        using var independent = OpenVerified(raw);
        bool independentReadback = string.Equals(Scalar(independent,
            $"SELECT VAL FROM {table} WHERE ID = :p0", 1)?.ToString(), changed, StringComparison.Ordinal);
        return new { outcome = "completed", first, second, command_text_preserved = textPreserved,
            transaction_readback = transactionReadback, independent_readback = independentReadback };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
}

static object LaterErrorRecoveryCase(string raw)
{
    try
    {
        using var connection = OpenVerified(raw);
        string missing = "T08_MISSING_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        string? error = null;
        using (var command = Command(connection, $"SELECT 1 FROM DUAL; SELECT * FROM {missing}"))
        {
            try { using var reader = command.ExecuteReader(); }
            catch (Exception ex) { error = ErrorKind(ex); }
        }
        string state = connection.State.ToString();
        bool sameConnectionReady = false;
        if (connection.State == ConnectionState.Open)
        {
            try { sameConnectionReady = Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL"),
                CultureInfo.InvariantCulture) == 1; }
            catch { }
        }
        using var independent = OpenVerified(raw);
        bool freshReady = Convert.ToInt32(Scalar(independent, "SELECT 1 FROM DUAL"),
            CultureInfo.InvariantCulture) == 1;
        return new { outcome = "completed", expected_error_kind = error,
            state_after_error = state, same_connection_ready = sameConnectionReady,
            fresh_connection_ready = freshReady };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
}

static string[] VerifyCandidateContracts(Dictionary<string, object?> scenarios, bool requireProtocolTrace)
{
    var failures = new List<string>();
    var all = JsonSerializer.SerializeToElement(scenarios);
    void Check(string name, Func<JsonElement, bool> predicate)
    {
        try
        {
            if (!predicate(all.GetProperty(name))) failures.Add(name);
        }
        catch { failures.Add(name); }
    }
    static JsonElement[] Seq(JsonElement item) => item.GetProperty("sequence").EnumerateArray().ToArray();
    static bool Completed(JsonElement item) => item.GetProperty("outcome").GetString() == "completed";
    static bool TerminalNoWire(JsonElement item)
    {
        var repeat = item.GetProperty("terminal_repeat");
        var before = repeat.GetProperty("before");
        var after = repeat.GetProperty("after");
        return item.GetProperty("terminal_observed").GetBoolean() &&
            repeat.GetProperty("outcome").GetString() == "completed" &&
            !repeat.GetProperty("read_again").GetBoolean() && !repeat.GetProperty("next_again").GetBoolean() &&
            before.GetProperty("count").GetInt64() == after.GetProperty("count").GetInt64() &&
            before.GetProperty("bytes").GetInt64() == after.GetProperty("bytes").GetInt64();
    }
    static bool TerminalTrace(JsonElement item)
    {
        var decoded = item.GetProperty("trace").EnumerateArray()
            .Where(frame => frame.GetProperty("event_kind").GetString() == "statement_decode").ToArray();
        return decoded.Count(frame => frame.GetProperty("is_terminal").GetBoolean()) == 1 &&
            decoded.Last(frame => frame.GetProperty("is_terminal").GetBoolean())
                .GetProperty("request_opcode").GetInt32() == 44;
    }
    Check("two_selects", item => Completed(item) && Seq(item).Length == 2 &&
        Seq(item)[0].GetProperty("first_value").GetString() == "1" &&
        Seq(item)[1].GetProperty("first_value").GetString() == "2" &&
        item.GetProperty("records_affected").GetInt32() == -1 && TerminalNoWire(item) &&
        (!requireProtocolTrace || TerminalTrace(item)));
    Check("empty_then_select", item => Completed(item) && Seq(item).Length == 2 &&
        Seq(item)[0].GetProperty("rows").GetInt32() == 0 &&
        Seq(item)[1].GetProperty("rows").GetInt32() == 1 &&
        item.GetProperty("records_affected").GetInt32() == -1 && TerminalNoWire(item));
    foreach (var (name, value) in new[] { ("dml_then_rowcount", "1"), ("dml_zero_then_rowcount", "0") })
        Check(name, item => Completed(item) && Seq(item).Length == 1 &&
            Seq(item)[0].GetProperty("first_value").GetString() == value &&
            item.GetProperty("records_affected").GetInt32() == -1 && TerminalNoWire(item));
    Check("later_sql_error", item => item.GetProperty("outcome").GetString() == "execute_error" &&
        item.GetProperty("error_kind").GetString()!.Contains(":-2106", StringComparison.Ordinal));
    Check("nonquery_later_error", item => item.GetProperty("outcome").GetString() == "error" &&
        item.GetProperty("error_kind").GetString()!.Contains(":-2106", StringComparison.Ordinal));
    Check("later_error_recovery", item => Completed(item) &&
        item.GetProperty("expected_error_kind").GetString()!.Contains(":-2106", StringComparison.Ordinal) &&
        item.GetProperty("fresh_connection_ready").GetBoolean() &&
        (item.GetProperty("state_after_error").GetString() == "Closed" ||
         item.GetProperty("state_after_error").GetString() == "Open" &&
         item.GetProperty("same_connection_ready").GetBoolean()));
    Check("scalar_empty", item => Completed(item) && item.GetProperty("value_kind").GetString() == "null");
    Check("scalar_null", item => Completed(item) && item.GetProperty("value_kind").GetString() == "DBNull");
    Check("semicolon_literal_comment", item => Completed(item) && Seq(item).Length == 2 &&
        Seq(item)[0].GetProperty("first_value").GetString() == "a;b" &&
        Seq(item)[1].GetProperty("first_value").GetString() == "2");
    Check("update_one", item => Completed(item) && item.GetProperty("affected").GetInt32() == 1);
    Check("update_zero", item => Completed(item) && item.GetProperty("affected").GetInt32() == 0);
    Check("delete_zero", item => Completed(item) && item.GetProperty("affected").GetInt32() == 0);
    Check("delete_one", item => Completed(item) && item.GetProperty("affected").GetInt32() == 1);
    Check("early_close_pending_insert", item => Completed(item) &&
        item.GetProperty("first_read").GetBoolean() && item.GetProperty("next_command_succeeded").GetBoolean() &&
        item.GetProperty("wire_after_close").GetProperty("count").GetInt64() -
            item.GetProperty("wire_after_read").GetProperty("count").GetInt64() == 1 &&
        (!requireProtocolTrace ||
            item.GetProperty("trace").EnumerateArray().Any(frame =>
                frame.TryGetProperty("request_opcode", out var opcode) && opcode.GetInt32() == 4) &&
            !item.GetProperty("trace").EnumerateArray().Any(frame =>
                frame.GetProperty("stage").GetString() == "close_reader" &&
                frame.TryGetProperty("request_opcode", out var opcode) && opcode.GetInt32() == 44)));
    Check("command_dispose_reader", item => Completed(item) &&
        item.GetProperty("first").GetBoolean() && item.GetProperty("second").GetBoolean() &&
        item.GetProperty("ready").GetBoolean());
    Check("prepared_dispose", item => Completed(item) &&
        item.GetProperty("command_text_preserved").GetBoolean() && item.GetProperty("ready").GetBoolean() &&
        item.GetProperty("cleanup_milliseconds").GetInt64() < 10000);
    Check("schema_table_local", item => Completed(item) &&
        item.GetProperty("schema_rows").GetInt32() == 2 && item.GetProperty("no_extra_wire").GetBoolean() &&
        item.GetProperty("is_key_kind").GetString() == "DBNull" &&
        item.GetProperty("is_unique_kind").GetString() == "DBNull");
    Check("mixed_metadata_lob", item => Completed(item) &&
        item.GetProperty("first").GetBoolean() && item.GetProperty("second").GetBoolean() &&
        item.GetProperty("blob").GetBoolean() &&
        item.GetProperty("first_names").GetArrayLength() == 1 &&
        item.GetProperty("second_names").GetArrayLength() == 2 &&
        item.GetProperty("second_names")[1].GetString() == "PAYLOAD" &&
        item.GetProperty("first_clr_types").GetArrayLength() == 1 &&
        item.GetProperty("second_clr_types").GetArrayLength() == 2 &&
        item.GetProperty("second_value_kind").GetString() == "String");
    Check("mixed_flags", item => Completed(item) &&
        item.GetProperty("single_first").GetBoolean() && !item.GetProperty("single_second").GetBoolean() &&
        item.GetProperty("single_close_connection").GetBoolean() &&
        item.GetProperty("sequential_blob").GetBoolean() &&
        item.GetProperty("sequential_close_connection").GetBoolean() &&
        item.GetProperty("schema_keyinfo_rejected").GetBoolean() &&
        item.GetProperty("schema_keyinfo_no_wire").GetBoolean() && item.GetProperty("schema_keyinfo_no_dml").GetBoolean());
    Check("invalid_binary_no_replay", item => Completed(item) &&
        item.GetProperty("rejected").GetBoolean() && item.GetProperty("rows_unchanged").GetBoolean() &&
        item.GetProperty("wire_sends_delta").GetInt64() <= 3 &&
        item.GetProperty("trace").EnumerateArray().Count(frame =>
            frame.TryGetProperty("event_kind", out var kind) && kind.GetString() == "frame" &&
            frame.TryGetProperty("request_opcode", out var opcode) && opcode.GetInt32() is 5 or 13) <= 1);
    foreach (string name in new[] { "generated_identity", "generated_sequence" })
        Check(name, item => Completed(item) && item.GetProperty("generated_columns_verified").GetBoolean() &&
            item.GetProperty("independent_readback_verified").GetBoolean() &&
            item.GetProperty("cleanup_verified").GetBoolean());
    Check("prepared_repeat_transaction", item => Completed(item) &&
        item.GetProperty("first").GetInt32() == 1 && item.GetProperty("second").GetInt32() == 1 &&
        item.GetProperty("command_text_preserved").GetBoolean() &&
        item.GetProperty("transaction_readback").GetBoolean() &&
        item.GetProperty("independent_readback").GetBoolean());
    return failures.ToArray();
}

static (long? Count, long? Bytes) WireCounters()
{
#if OFFICIAL
    return (null, null);
#else
    var hooks = typeof(DriverConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks");
    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
    return ((long?)hooks?.GetProperty("SendCount", flags)?.GetValue(null),
            (long?)hooks?.GetProperty("SentBytes", flags)?.GetValue(null));
#endif
}

static DriverConnection OpenVerified(string raw)
{
#if OFFICIAL
    var connection = new DriverConnection(raw);
#else
    var builder = new W.Dm.DmConnectionStringBuilder(raw)
    {
        TransportSecurity = W.Dm.DmTransportSecurity.PlaintextAllowed,
        PersistSecurityInfo = false,
        Schema = TestUser
    };
    var connection = new DriverConnection(builder.ConnectionString);
#endif
    try
    {
        connection.Open();
        if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser,
                StringComparison.OrdinalIgnoreCase))
            throw new ProbeFailure("server_identity_invalid");
        return connection;
    }
    catch { connection.Dispose(); throw; }
}

static bool ObjectExists(DbConnection connection, string table) =>
    Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table),
        CultureInfo.InvariantCulture) != 0;

static object? Scalar(DbConnection connection, string sql, params object[] values)
{ using var command = Command(connection, sql, values); return command.ExecuteScalar(); }
static int Exec(DbConnection connection, string sql, params object[] values)
{ using var command = Command(connection, sql, values); return command.ExecuteNonQuery(); }

static DbCommand Command(DbConnection connection, string sql, params object[] values)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    for (int index = 0; index < values.Length; index++)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p" + index.ToString(CultureInfo.InvariantCulture);
        parameter.DbType = values[index] switch
        { int => DbType.Int32, long => DbType.Int64, byte[] => DbType.Binary, _ => DbType.String };
        parameter.Value = values[index];
        command.Parameters.Add(parameter);
    }
    return command;
}

static string? SafeSyntheticValue(object? value)
{
    if (value == null) return null;
    if (value == DBNull.Value) return "DBNull";
    string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    return text.Length <= 48 ? text : text[..48];
}

static string ErrorKind(Exception error)
{
    int? number = null;
    try { if (error.GetType().GetProperty("Number")?.GetValue(error) is int value) number = value; }
    catch { }
    return number is null ? error.GetType().Name : error.GetType().Name + ":" + number;
}

sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }

sealed class TraceScope : IDisposable
{
    internal bool Enabled { get; set; }
    internal string Stage { get; set; } = "none";
    internal List<object> Events { get; } = [];

    internal TraceScope()
    {
#if CANDIDATE
        DmResultProtocolTrace.AfterFrame = (requestOpcode, responseOpcode, sqlCode, bodyLength) =>
        {
            if (Enabled) Events.Add(new { stage = Stage, event_kind = "frame", request_opcode = requestOpcode,
                response_opcode = responseOpcode, sql_code = sqlCode, wire_body_length_declared = bodyLength });
        };
        DmResultProtocolTrace.AfterStatementDecode = (requestOpcode, retStmtType, rowCount, affected,
            columnCount, hasResultSet, isTerminal) =>
        {
            if (Enabled) Events.Add(new { stage = Stage, event_kind = "statement_decode", request_opcode = requestOpcode,
                ret_stmt_type = retStmtType, row_count = rowCount, records_affected = affected,
                column_count = columnCount, has_result_set = hasResultSet, is_terminal = isTerminal });
        };
#endif
    }

    public void Dispose()
    {
#if CANDIDATE
        DmResultProtocolTrace.AfterFrame = null;
        DmResultProtocolTrace.AfterStatementDecode = null;
#endif
    }
}
