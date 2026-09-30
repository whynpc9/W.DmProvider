using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using W.Dm;

const string TestUser = "WDM_PROVIDER_TEST";
var stage = "arguments";
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["implementation"] = "W",
    ["task"] = "T04",
    ["run_id"] = Guid.NewGuid().ToString("N")
};
try
{
    if (args.Length != 1 || args[0] != "real") throw new ProbeFailure("usage");
    stage = "configuration";
    var raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(raw)) throw new ProbeFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("configured_account_invalid");
    builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
    builder.PersistSecurityInfo = false;
    builder.Schema = TestUser;
    if (builder.TransportSecurity != DmTransportSecurity.PlaintextAllowed || builder.PersistSecurityInfo)
        throw new ProbeFailure("explicit_transport_not_applied");
    var password = builder.Password;
    if (string.IsNullOrEmpty(password)) throw new ProbeFailure("configured_password_missing");
    var connectionString = builder.ConnectionString;
    result["explicit_transport"] = DmTransportSecurity.PlaintextAllowed.ToString();
    result["persist_security_info"] = false;
    result["explicit_test_schema"] = true;
    stage = "server_identity";
    var table = "T04_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_name"] = table;
    bool cleanupVerified = false;
    bool connectingMutationRejected = false;
    bool openMutationRejected = false;
    bool redactedOpen = false;
    bool redactedInOpenEvent = false;
    bool openEventReentryRejected = false;
    bool redactedClosed = false;
    bool clonedAuthenticationWorked = false;
    bool reopenedAuthenticationWorked = false;
    bool failedReopenRetainedRedaction = false;
    bool serverAccountVerified = false;
    int finalRows = -1;
    Exception? workError = null;
    Exception? cleanupError = null;
    string? workStage = null;
    try
    {
        using var c = NewConnection(connectionString);
        c.StateChange += (_, change) =>
        {
            if (change.CurrentState == ConnectionState.Open)
            {
                redactedInOpenEvent = IsRedacted(c, password);
                openEventReentryRejected = RejectsInvalidOperation(() => c.ConnectionString = connectionString) &&
                                           RejectsInvalidOperation(() => c.Schema = "OTHER") &&
                                           RejectsInvalidOperation(() => c.Open());
            }
            if (change.CurrentState == ConnectionState.Connecting)
            {
                var textRejected = RejectsInvalidOperation(() => c.ConnectionString = connectionString);
                var schemaRejected = RejectsInvalidOperation(() => c.Schema = "OTHER");
                connectingMutationRejected = textRejected && schemaRejected;
            }
        };
        c.Open();
        VerifyIdentity(c);
        serverAccountVerified = true;
        redactedOpen = IsRedacted(c, password);
        openMutationRejected = RejectsInvalidOperation(() => c.ConnectionString = connectionString) &&
                               RejectsInvalidOperation(() => c.Schema = "OTHER");
        if (!connectingMutationRejected || !openMutationRejected || !redactedOpen || !redactedInOpenEvent || !openEventReentryRejected)
            throw new ProbeFailure("state_or_redaction_invalid");
        stage = "basic_crud";
        Exec(c, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(400))");
        var unicode = "T04-中文-測試-🌱";
        if (Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", null, 1, unicode) != 1)
            throw new ProbeFailure("insert_count_invalid");
        var observed = Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", null, 1)?.ToString();
        if (Hash(observed ?? "") != Hash(unicode)) throw new ProbeFailure("unicode_read_invalid");
        if (Exec(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", null, unicode + "-u", 1) != 1)
            throw new ProbeFailure("update_count_invalid");
        if (Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", null, 1)?.ToString() != unicode + "-u")
            throw new ProbeFailure("update_read_invalid");
        if (Exec(c, $"DELETE FROM {table} WHERE ID = :p0", null, 1) != 1)
            throw new ProbeFailure("delete_count_invalid");
        stage = "transaction";
        using (var tx = c.BeginTransaction())
        {
            Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 901, "commit");
            tx.Commit();
        }
        using (var independent = OpenVerified(connectionString))
            if (Count(independent, table) != 1) throw new ProbeFailure("commit_not_visible");
        using (var tx = c.BeginTransaction())
        {
            Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 902, "rollback");
            tx.Rollback();
        }
        using (var independent = OpenVerified(connectionString))
            finalRows = Count(independent, table);
        if (finalRows != 1) throw new ProbeFailure("rollback_visible");
        stage = "closed_reopen_clone";
        c.Close();
        redactedClosed = IsRedacted(c, password);
        if (!redactedClosed) throw new ProbeFailure("closed_redaction_invalid");
        bool injectedFailureReached = false;
        StateChangeEventHandler failOnReconnect = (_, change) =>
        {
            if (change.CurrentState != ConnectionState.Connecting) return;
            injectedFailureReached = true;
            throw new InvalidOperationException("Synthetic reopen interruption.");
        };
        c.StateChange += failOnReconnect;
        try
        {
            try { c.Open(); throw new ProbeFailure("failed_reopen_accepted"); }
            catch (InvalidOperationException) when (injectedFailureReached) { }
        }
        finally { c.StateChange -= failOnReconnect; }
        failedReopenRetainedRedaction = injectedFailureReached && c.State == ConnectionState.Closed && IsRedacted(c, password);
        if (!failedReopenRetainedRedaction) throw new ProbeFailure("failed_reopen_redaction_invalid");
        using (var clone = c.Clone())
        {
            if (!IsRedacted(clone, password)) throw new ProbeFailure("clone_public_redaction_invalid");
            clone.Open();
            VerifyIdentity(clone);
            clonedAuthenticationWorked = IsRedacted(clone, password);
        }
        c.Open();
        VerifyIdentity(c);
        reopenedAuthenticationWorked = IsRedacted(c, password);
        c.Close();
        if (!clonedAuthenticationWorked || !reopenedAuthenticationWorked)
            throw new ProbeFailure("snapshot_authentication_invalid");
    }
    catch (Exception ex) { workError = ex; workStage = stage; }
    stage = "cleanup";
    try
    {
        // Only this run's unqualified random table is inspected and removed.
        using var cleaner = OpenVerified(connectionString);
        if (Convert.ToInt32(Scalar(cleaner, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", null, table), CultureInfo.InvariantCulture) != 0)
            Exec(cleaner, $"DROP TABLE {table}");
        using var verify = OpenVerified(connectionString);
        cleanupVerified = Convert.ToInt32(Scalar(verify, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", null, table), CultureInfo.InvariantCulture) == 0;
    }
    catch (Exception ex) { cleanupError = ex; }
    result["server_account_verified"] = serverAccountVerified;
    result["connecting_mutation_rejected"] = connectingMutationRejected;
    result["open_mutation_rejected"] = openMutationRejected;
    result["redacted_open"] = redactedOpen;
    result["redacted_in_open_event"] = redactedInOpenEvent;
    result["open_event_reentry_rejected"] = openEventReentryRejected;
    result["redacted_closed"] = redactedClosed;
    result["clone_authentication_verified"] = clonedAuthenticationWorked;
    result["reopen_authentication_verified"] = reopenedAuthenticationWorked;
    result["failed_reopen_retained_redaction"] = failedReopenRetainedRedaction;
    result["final_rows_before_cleanup"] = finalRows;
    result["cleanup_verified"] = cleanupVerified;
    result["final_database_state"] = cleanupVerified ? "random_object_absent" : "not_verified";
    if (workError is not null)
    {
        result["work_stage"] = workStage;
        result["work_error_kind"] = workError is ProbeFailure workFailure ? workFailure.Kind : workError.GetType().Name;
        result["work_error_number"] = SafeNumber(workError);
        result["work_guard"] = SafeGuardCode(workError);
        result["work_origin"] = SafeOrigin(workError);
        result["work_methods"] = SafeMethods(workError);
    }
    if (cleanupError is not null)
    {
        result["cleanup_error_kind"] = cleanupError.GetType().Name;
        result["cleanup_error_number"] = SafeNumber(cleanupError);
        result["cleanup_guard"] = SafeGuardCode(cleanupError);
        result["cleanup_origin"] = SafeOrigin(cleanupError);
        result["cleanup_methods"] = SafeMethods(cleanupError);
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

static DmConnection NewConnection(string connectionString) => new(connectionString);
static DmConnection OpenVerified(string connectionString)
{
    var c = NewConnection(connectionString);
    try { c.Open(); VerifyIdentity(c); return c; }
    catch { c.Dispose(); throw; }
}
static void VerifyIdentity(DmConnection c)
{
    if (!string.Equals(Scalar(c, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("server_account_invalid");
    if (!string.Equals(c.Schema, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("test_schema_invalid");
}
static bool IsRedacted(DmConnection c, string password) =>
    c.Password is null && !c.ConnectionString.Contains(password, StringComparison.Ordinal);
static bool RejectsInvalidOperation(Action action)
{
    try { action(); return false; }
    catch (InvalidOperationException) { return true; }
}
static int Count(DmConnection c, string table) =>
    Convert.ToInt32(Scalar(c, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
static object? Scalar(DmConnection c, string sql, DbTransaction? tx = null, params object[] values)
{ using var command = Command(c, sql, tx, values); return command.ExecuteScalar(); }
static int Exec(DmConnection c, string sql, DbTransaction? tx = null, params object[] values)
{ using var command = Command(c, sql, tx, values); return command.ExecuteNonQuery(); }
static DbCommand Command(DmConnection c, string sql, DbTransaction? tx, object[] values)
{
    var command = c.CreateCommand();
    command.CommandText = sql;
    command.Transaction = tx;
    for (var index = 0; index < values.Length; index++)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p" + index.ToString(CultureInfo.InvariantCulture);
        parameter.DbType = values[index] is int ? DbType.Int32 : DbType.String;
        parameter.Value = values[index];
        command.Parameters.Add(parameter);
    }
    return command;
}
static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static int? SafeNumber(Exception ex)
{
    try { return ex.GetType().GetProperty("Number")?.GetValue(ex) is int n ? n : null; }
    catch { return null; }
}
static string? SafeGuardCode(Exception ex) => ex is NotSupportedException ? ex.Message switch
{
    "Verified TLS transport is not implemented." => "policy_mismatch",
    "Negotiated native transport security is unsupported." => "peer_security_required",
    "Legacy TLS transport is unsupported." => "legacy_tls_blocked",
    _ => "other_unsupported"
} : null;
static string? SafeOrigin(Exception ex)
{
    var type = ex.TargetSite?.DeclaringType?.FullName;
    var method = ex.TargetSite?.Name;
    return type is null || method is null ? null : type + "." + method;
}
static string[] SafeMethods(Exception ex) => new StackTrace(ex).GetFrames()?
    .Select(frame => frame.GetMethod())
    .Where(method => method?.DeclaringType?.Assembly == typeof(DmConnection).Assembly)
    .Select(method => method!.DeclaringType!.FullName + "." + method.Name)
    .Take(12).ToArray() ?? [];
sealed class ProbeFailure(string kind) : Exception { public string Kind { get; } = kind; }
