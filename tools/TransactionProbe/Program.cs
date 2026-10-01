using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
#if OFFICIAL
using DriverConnection = Dm.DmConnection;
#else
using DriverConnection = W.Dm.DmConnection;
#endif

const string TestUser = "WDM_PROVIDER_TEST";
const string SyntheticValue = "T10-事务-Ω";
string implementation =
#if OFFICIAL
    "O";
#elif CANDIDATE
    "W-T10-candidate";
#else
    "W-T08-baseline";
#endif
var driverAssembly = typeof(DriverConnection).Assembly;
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T10",
    ["probe"] = "transaction_profile",
    ["implementation"] = implementation,
    ["run_id"] = Guid.NewGuid().ToString("N"),
    ["assembly_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverAssembly.Location))).ToLowerInvariant(),
    ["assembly_mvid"] = driverAssembly.ManifestModule.ModuleVersionId.ToString("D"),
    ["assembly_version"] = driverAssembly.GetName().Version?.ToString(),
    ["frame_hook_available"] = implementation != "O"
};
string? raw = null, table = null, ddlTable = null;
bool accountVerified = false, cleanupVerified = false;
string stage = "arguments";
Exception? workError = null, cleanupError = null;
var scenarios = new Dictionary<string, object?>();
bool ddlMarkersOnly = args.Length == 1 && args[0] == "ddl-marker";
bool ddlVariantsOnly = args.Length == 1 && args[0] == "ddl-variants";
bool quotedSavepointOnly = args.Length == 1 && args[0] == "quoted-savepoint";
bool candidateVerified = args.Length == 1 && args[0] == "candidate_verified";
bool candidateAckCloseRace = args.Length == 1 && args[0] == "candidate_ack_close_race";
try
{
    if (args.Length != 1 || args[0] is not ("profile" or "ddl-marker" or "ddl-variants" or "quoted-savepoint" or "candidate_verified" or "candidate_ack_close_race"))
        throw new ProbeFailure("usage");
#if !CANDIDATE
    if (candidateVerified || candidateAckCloseRace) throw new ProbeFailure("candidate_mode_requires_candidate_assembly");
#endif
    stage = "configuration";
    raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new ProbeFailure("test_connection_missing");
    if (!raw.Contains("User Id=" + TestUser + ";", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("configured_account_invalid");
    table = "T10_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    ddlTable = "T10D_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_names"] = new[] { table, ddlTable };
    stage = "server_identity";
    using (var owner = OpenVerified(raw))
    {
        accountVerified = true;
        result["server_version"] = owner.ServerVersion;
        stage = "create_table";
        Exec(owner, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(100))");
    }
    if (candidateVerified)
    {
#if CANDIDATE
        foreach (var pair in CandidateCases.Run(raw, table, ddlTable)) scenarios[pair.Key] = pair.Value;
#endif
    }
    else if (candidateAckCloseRace)
    {
#if CANDIDATE
        foreach (var pair in CandidateCases.RunAckCloseRace(raw, table)) scenarios[pair.Key] = pair.Value;
#endif
    }
    else if (quotedSavepointOnly)
    {
        scenarios["quoted_sql_savepoint"] = QuotedSavepointSql(raw, table);
    }
    else if (ddlVariantsOnly)
    {
        scenarios["ddl_variants"] = DdlVariants(raw, table, ddlTable);
    }
    else if (!ddlMarkersOnly)
    {
        scenarios["empty_begin_commit"] = EmptyBeginCommit(raw);
        scenarios["commit_insert"] = InsertCommit(raw, table, 1);
        scenarios["rollback_insert"] = InsertRollback(raw, table, 2);
        scenarios["ado_savepoint"] = SavepointAdo(raw, table);
        scenarios["sql_savepoint"] = SavepointSql(raw, table);
        scenarios["isolation_begin"] = IsolationProfile(raw);
    }
    if (!quotedSavepointOnly && !ddlVariantsOnly && !candidateVerified && !candidateAckCloseRace)
        scenarios["ddl_implicit_commit"] = DdlBoundary(raw, table, ddlTable);
    using var finalReadback = OpenVerified(raw);
    result["server_rows_before_cleanup"] = Convert.ToInt32(Scalar(finalReadback,
        $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
}
catch (Exception ex) { workError = ex; result["work_stage"] = stage; }
finally
{
    if (raw != null && table != null && ddlTable != null)
    {
        try
        {
            using var cleaner = OpenVerified(raw);
            if (ObjectExists(cleaner, ddlTable)) Exec(cleaner, $"DROP TABLE {ddlTable}");
            if (ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            using var check = OpenVerified(raw);
            cleanupVerified = !ObjectExists(check, table) && !ObjectExists(check, ddlTable);
        }
        catch (Exception ex) { cleanupError = ex; }
    }
}
result["server_account_verified"] = accountVerified;
result["cleanup_verified"] = cleanupVerified;
result["final_database_state"] = cleanupVerified ? "random_objects_absent" : "not_verified";
result["scenarios"] = scenarios;
if (workError != null) result["work_error_kind"] = ErrorKind(workError);
if (cleanupError != null) result["cleanup_error_kind"] = ErrorKind(cleanupError);
string[] failures = [];
#if CANDIDATE
if (candidateVerified) failures = CandidateCases.Verify(scenarios);
if (candidateAckCloseRace) failures = CandidateCases.VerifyAckCloseRace(scenarios);
if (quotedSavepointOnly || (!candidateVerified && !candidateAckCloseRace && !ddlMarkersOnly && !ddlVariantsOnly))
{
    var savepointFailures = new List<string>(failures);
    foreach (string name in quotedSavepointOnly ? new[] { "quoted_sql_savepoint" } : new[] { "sql_savepoint" })
    {
        var row = JsonSerializer.SerializeToElement(scenarios.GetValueOrDefault(name));
        if (row.ValueKind != JsonValueKind.Object ||
            !row.TryGetProperty("outcome", out var outcome) || outcome.GetString() != "completed" ||
            !row.TryGetProperty("before_visible", out var before) || !before.GetBoolean() ||
            !row.TryGetProperty("after_absent", out var after) || !after.GetBoolean() ||
            !row.TryGetProperty("raw_control_rejected_without_wire", out var fenced) || !fenced.GetBoolean() ||
            !row.TryGetProperty("mixed_api_raw_verified", out var mixed) || !mixed.GetBoolean())
            savepointFailures.Add(name + ":driver_api_or_raw_control_fence");
    }
    failures = savepointFailures.ToArray();
}
#endif
result["contract_failures"] = failures;
bool passed = workError == null && cleanupError == null && accountVerified && cleanupVerified &&
    scenarios.Count == (candidateVerified ? 10 : candidateAckCloseRace || ddlMarkersOnly || ddlVariantsOnly || quotedSavepointOnly ? 1 : 7) &&
    failures.Length == 0;
result["status"] = passed ? candidateVerified ? "candidate_verified" : candidateAckCloseRace ? "focused_race_verified" : "observed" : "rejected";
Console.WriteLine(JsonSerializer.Serialize(result));
return passed ? 0 : 1;

static object EmptyBeginCommit(string raw)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        var before = WireCounters();
        trace.Stage = "begin";
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var afterBegin = WireCounters();
        trace.Stage = "commit";
        transaction.Commit();
        var afterCommit = WireCounters();
        return new { outcome = "completed", isolation = transaction.IsolationLevel.ToString(),
            begin_send_delta = Delta(before, afterBegin), commit_send_delta = Delta(afterBegin, afterCommit),
            connection_state = connection.State.ToString(), trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object InsertCommit(string raw, string table, int id)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        trace.Stage = "begin";
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        trace.Stage = "insert";
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, SyntheticValue);
        trace.Stage = "commit";
        transaction.Commit();
        trace.Stage = "readback";
        using var readback = OpenVerified(raw);
        bool committed = Count(readback, table, id) == 1;
        return new { outcome = "completed", committed, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object InsertRollback(string raw, string table, int id)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        trace.Stage = "begin";
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        trace.Stage = "insert";
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, SyntheticValue);
        trace.Stage = "rollback";
        transaction.Rollback();
        trace.Stage = "readback";
        using var readback = OpenVerified(raw);
        bool rolledBack = Count(readback, table, id) == 0;
        return new { outcome = "completed", rolled_back = rolledBack, trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object SavepointAdo(string raw, string table)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        bool supports = transaction.SupportsSavepoints;
        if (!supports) { transaction.Rollback(); return new { outcome = "unsupported", supports_savepoints = false, trace = trace.Events }; }
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (3, 'before')");
        trace.Stage = "save";
        transaction.Save("T10_ADO_A");
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (4, 'after')");
        trace.Stage = "rollback_to";
        transaction.Rollback("T10_ADO_A");
        trace.Stage = "release";
        transaction.Release("T10_ADO_A");
        trace.Stage = "commit";
        transaction.Commit();
        using var readback = OpenVerified(raw);
        return new { outcome = "completed", supports_savepoints = true,
            before_visible = Count(readback, table, 3) == 1, after_absent = Count(readback, table, 4) == 0,
            trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object SavepointSql(string raw, string table)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (5, 'before')");
        trace.Stage = "savepoint_sql";
#if CANDIDATE
        transaction.Save("T10_SQL_A");
        bool fenced = RejectReservedSavepointControl(connection, transaction);
        ExecInTransaction(connection, transaction, "SAVEPOINT \"T10_RAW_A\"");
#else
        ExecInTransaction(connection, transaction, "SAVEPOINT T10_SQL_A");
#endif
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (6, 'after')");
        trace.Stage = "rollback_to_sql";
#if CANDIDATE
        ExecInTransaction(connection, transaction, "ROLLBACK TO SAVEPOINT \"T10_RAW_A\"");
        ExecInTransaction(connection, transaction, "RELEASE SAVEPOINT \"T10_RAW_A\"");
        transaction.Rollback("T10_SQL_A");
#else
        ExecInTransaction(connection, transaction, "ROLLBACK TO SAVEPOINT T10_SQL_A");
#endif
        trace.Stage = "release_sql";
#if CANDIDATE
        transaction.Release("T10_SQL_A");
        bool mixed = VerifyEarlyRawControlInvalidation(connection, transaction);
#else
        ExecInTransaction(connection, transaction, "RELEASE SAVEPOINT T10_SQL_A");
#endif
        trace.Stage = "commit";
        transaction.Commit();
        using var readback = OpenVerified(raw);
        return new { outcome = "completed", before_visible = Count(readback, table, 5) == 1,
            after_absent = Count(readback, table, 6) == 0,
#if CANDIDATE
            savepoint_entry = "mixed_driver_api_and_user_sql", raw_control_rejected_without_wire = fenced,
            mixed_api_raw_verified = mixed,
#endif
            trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object QuotedSavepointSql(string raw, string table)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope();
    try
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (11, 'before')");
        trace.Stage = "save_quoted";
#if CANDIDATE
        transaction.Save("WSP_1");
        bool fenced = RejectReservedSavepointControl(connection, transaction);
        using (var rawSave = Command(connection, transaction, "SAVEPOINT \"T10_QUOTED_RAW\""))
            rawSave.ExecuteScalar();
#else
        ExecInTransaction(connection, transaction, "SAVEPOINT \"WSP_1\"");
#endif
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (12, 'after')");
        trace.Stage = "rollback_to_quoted";
#if CANDIDATE
        using (var rawRollback = Command(connection, transaction, "ROLLBACK TO \"T10_QUOTED_RAW\""))
        using (var reader = rawRollback.ExecuteReader()) { while (reader.Read()) { } }
        ExecInTransaction(connection, transaction, "RELEASE SAVEPOINT \"T10_QUOTED_RAW\"");
        transaction.Rollback("WSP_1");
#else
        ExecInTransaction(connection, transaction, "ROLLBACK TO SAVEPOINT \"WSP_1\"");
#endif
        trace.Stage = "release_quoted";
#if CANDIDATE
        transaction.Release("WSP_1");
        bool mixed = VerifyEarlyRawControlInvalidation(connection, transaction);
#else
        ExecInTransaction(connection, transaction, "RELEASE SAVEPOINT \"WSP_1\"");
#endif
        trace.Stage = "commit";
        transaction.Commit();
        using var readback = OpenVerified(raw);
        return new { outcome = "completed", before_visible = Count(readback, table, 11) == 1,
            after_absent = Count(readback, table, 12) == 0,
#if CANDIDATE
            savepoint_entry = "mixed_driver_api_and_user_sql", raw_control_rejected_without_wire = fenced,
            mixed_api_raw_verified = mixed,
#endif
            trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

#if CANDIDATE
static bool RejectReservedSavepointControl(DbConnection connection, DbTransaction transaction)
{
    var before = WireCounters();
    foreach (string sql in new[]
    {
        "SAVEPOINT \"WSP_1\"", "SAVEPOINT wsp_2", "/*prefix*/ SAVEPOINT /*target*/ \"wSp_2\"",
        "ROLLBACK", "ROLLBACK TO \"WSP_1\"", "ROLLBACK TO SAVEPOINT /*target*/ \"wsp_2\"",
        "RELEASE SAVEPOINT \"WSP_1\"", "RELEASE SAVEPOINT /*target*/ \"wSp_2\"",
        "SELECT 1 FROM DUAL; RELEASE SAVEPOINT user_owned"
    })
    {
        bool rejected = false;
        try { ExecInTransaction(connection, transaction, sql); }
        catch (NotSupportedException) { rejected = true; }
        if (!rejected) throw new ProbeFailure("raw_savepoint_control_accepted");
    }
    var after = WireCounters();
    if (before.Count is null || before.Bytes is null || before != after ||
        transaction.GetType().GetProperty("Outcome")?.GetValue(transaction)?.ToString() != "Active")
        throw new ProbeFailure("raw_savepoint_control_sent_or_changed_outcome");
    return true;
}

static bool VerifyEarlyRawControlInvalidation(DbConnection connection, DbTransaction transaction)
{
    foreach (bool release in new[] { false, true })
    {
        ExecInTransaction(connection, transaction, "SAVEPOINT \"T10_EARLY_RAW\"");
        transaction.Save("T10_LATER_API");
        ExecInTransaction(connection, transaction, release ? "RELEASE SAVEPOINT \"T10_EARLY_RAW\"" :
            "ROLLBACK TO SAVEPOINT \"T10_EARLY_RAW\"");
        var before = WireCounters();
        bool invalidated = false;
        try { transaction.Rollback("T10_LATER_API"); }
        catch (InvalidOperationException) { invalidated = true; }
        if (!invalidated || before != WireCounters())
            throw new ProbeFailure("raw_early_control_left_stale_api_point");
        if (!release) ExecInTransaction(connection, transaction, "RELEASE SAVEPOINT \"T10_EARLY_RAW\"");
        // The local transaction remains usable after the invalidated handle.
        transaction.Save("T10_CONTINUE_API");
        transaction.Release("T10_CONTINUE_API");
    }
    return true;
}
#endif

static object IsolationProfile(string raw)
{
    var outcomes = new List<object>();
    foreach (var isolation in new[] { IsolationLevel.ReadCommitted, IsolationLevel.ReadUncommitted,
                                      IsolationLevel.Serializable, IsolationLevel.Unspecified })
    {
        using var connection = OpenVerified(raw);
        using var trace = new TraceScope();
        try
        {
            trace.Stage = "begin_" + isolation;
            using var transaction = connection.BeginTransaction(isolation);
            using var command = Command(connection, transaction, "SELECT 1 FROM DUAL");
            bool query = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            transaction.Rollback();
            outcomes.Add(new { requested = isolation.ToString(), outcome = "accepted",
                reported = transaction.IsolationLevel.ToString(), query, trace = trace.Events.ToArray() });
        }
        catch (Exception ex) { outcomes.Add(new { requested = isolation.ToString(), outcome = "rejected",
            error_kind = ErrorKind(ex), trace = trace.Events.ToArray() }); }
    }
    return new { outcome = "observed", levels = outcomes };
}

static object DdlBoundary(string raw, string table, string ddlTable)
{
    using var connection = OpenVerified(raw);
    using var trace = new TraceScope(connection, TransactionMarkers);
    try
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        object afterBegin = TransactionMarkers(connection);
        ExecInTransaction(connection, transaction, $"INSERT INTO {table} (ID, VAL) VALUES (7, 'before_ddl')");
        object afterInsert = TransactionMarkers(connection);
        trace.Stage = "ddl";
        ExecInTransaction(connection, transaction, $"CREATE TABLE {ddlTable} (ID INT)");
        object afterDdl = TransactionMarkers(connection);
        trace.Stage = "rollback_after_ddl";
        string? rollbackError = null;
        try { transaction.Rollback(); }
        catch (Exception ex) { rollbackError = ErrorKind(ex); }
        object afterRollback = TransactionMarkers(connection);
        using var readback = OpenVerified(raw);
        return new { outcome = "observed", row_before_ddl_visible = Count(readback, table, 7) == 1,
            ddl_table_exists = ObjectExists(readback, ddlTable), rollback_error_kind = rollbackError,
            markers = new { after_begin = afterBegin, after_insert = afterInsert,
                after_ddl = afterDdl, after_rollback = afterRollback },
            trace = trace.Events };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex), trace = trace.Events }; }
}

static object DdlVariants(string raw, string table, string ddlTable)
{
    try
    {
        using (var initializer = OpenVerified(raw))
            Exec(initializer, $"CREATE TABLE {ddlTable} (ID INT)");
        var variants = new List<object>();
        foreach (var (name, id, ddl) in new[]
        {
            ("alter", 21, $"ALTER TABLE {ddlTable} ADD EXTRA INT"),
            ("truncate", 22, $"TRUNCATE TABLE {ddlTable}"),
            ("drop", 23, $"DROP TABLE {ddlTable}")
        })
        {
            using var connection = OpenVerified(raw);
            using var trace = new TraceScope(connection, TransactionMarkers);
            try
            {
                using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                trace.Stage = "insert_" + name;
                ExecInTransaction(connection, transaction,
                    $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, SyntheticValue);
                trace.Stage = name;
                ExecInTransaction(connection, transaction, ddl);
                trace.Stage = "rollback_after_" + name;
                string? rollbackError = null;
                try { transaction.Rollback(); }
                catch (Exception ex) { rollbackError = ErrorKind(ex); }
                using var readback = OpenVerified(raw);
                variants.Add(new { name, outcome = "observed",
                    preceding_insert_visible = Count(readback, table, id) == 1,
                    target_exists = ObjectExists(readback, ddlTable),
                    rollback_error_kind = rollbackError, trace = trace.Events.ToArray() });
            }
            catch (Exception ex)
            {
                variants.Add(new { name, outcome = "error", error_kind = ErrorKind(ex),
                    trace = trace.Events.ToArray() });
                break;
            }
        }
        return new { outcome = "observed", variants };
    }
    catch (Exception ex) { return new { outcome = "error", error_kind = ErrorKind(ex) }; }
}

static object TransactionMarkers(DbConnection connection)
{
#if OFFICIAL
    return new { trx_status = (int?)null, trans_finish = (bool?)null };
#else
    try
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? instance = typeof(DriverConnection).GetMethod("GetConnInstance", flags)?.Invoke(connection, null);
        if (instance == null) return new { trx_status = (int?)null, trans_finish = (bool?)null };
        int? status = instance.GetType().GetField("trxStatus", flags)?.GetValue(instance) is int value ? value : null;
        bool? finished = instance.GetType().GetMethod("getTransFinish", flags)?.Invoke(instance, null) is bool flag ? flag : null;
        return new { trx_status = status, trans_finish = finished };
    }
    catch { return new { trx_status = (int?)null, trans_finish = (bool?)null }; }
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
static int Count(DbConnection connection, string table, int id) =>
    Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE ID = :p0", id),
        CultureInfo.InvariantCulture);
static object? Scalar(DbConnection connection, string sql, params object[] values)
{ using var command = Command(connection, null, sql, values); return command.ExecuteScalar(); }
static int Exec(DbConnection connection, string sql, params object[] values)
{ using var command = Command(connection, null, sql, values); return command.ExecuteNonQuery(); }
static int ExecInTransaction(DbConnection connection, DbTransaction transaction, string sql, params object[] values)
{ using var command = Command(connection, transaction, sql, values); return command.ExecuteNonQuery(); }
static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params object[] values)
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
static long? Delta((long? Count, long? Bytes) before, (long? Count, long? Bytes) after) =>
    before.Count is null || after.Count is null ? null : after.Count - before.Count;
static string ErrorKind(Exception error)
{
    int? number = null;
    try { if (error.GetType().GetProperty("Number")?.GetValue(error) is int value) number = value; }
    catch { }
    return number is null ? error.GetType().Name : error.GetType().Name + ":" + number;
}

sealed class TraceScope : IDisposable
{
    private readonly FieldInfo? field;
    private readonly FieldInfo? statementField;
    internal string Stage { get; set; } = "none";
    internal List<object> Events { get; } = [];
    internal TraceScope(DbConnection? owner = null, Func<DbConnection, object>? markers = null)
    {
#if !OFFICIAL
        var type = typeof(DriverConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace");
        field = type?.GetField("AfterFrame", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, (Action<short, short, int, int>)((request, response, code, bodyLength) =>
            Events.Add(new { stage = Stage, request_opcode = request, response_opcode = response,
                sql_code = code, wire_body_length_declared = bodyLength })));
        if (owner != null && markers != null)
        {
            statementField = type?.GetField("AfterStatementDecode", BindingFlags.Static | BindingFlags.NonPublic);
            statementField?.SetValue(null, (Action<short, int, long, long, int, bool, bool>)
                ((request, resultType, rowCount, affected, columns, hasRowset, terminal) =>
                    Events.Add(new { stage = Stage, event_kind = "statement_decode", request_opcode = request,
                        ret_stmt_type = resultType, row_count = rowCount, records_affected = affected,
                        column_count = columns, has_result_set = hasRowset, is_terminal = terminal,
                        physical = markers(owner) })));
        }
#endif
    }
    public void Dispose()
    {
        field?.SetValue(null, null);
        statementField?.SetValue(null, null);
    }
}

sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
