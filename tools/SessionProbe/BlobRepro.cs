using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
#if W_BLOB
using W.Dm;
#else
using Dm;
using Dm.Config;
#endif

const string TestUser = "WDM_PROVIDER_TEST";
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T05",
    ["probe"] = "three_parameter_blob_insert",
    ["implementation"] = args.Length == 1 ? args[0] : "invalid",
    ["run_id"] = Guid.NewGuid().ToString("N")
};
string stage = "arguments";
try
{
#if W_BLOB
    if (args.Length != 1 || args[0] != "W") throw new ReproFailure("usage");
#else
    if (args.Length != 1 || args[0] is not ("O" or "R")) throw new ReproFailure("usage");
#endif
    stage = "configuration";
    var raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new ReproFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ReproFailure("configured_account_invalid");
#if W_BLOB
    builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
    builder.Schema = TestUser;
    builder.PersistSecurityInfo = false;
#else
    builder.ConnPooling = false;
    builder.StmtPooling = false;
    builder.PreparePooling = false;
    builder.LogLevel = LogLevel.OFF;
#endif
    string settings = builder.ConnectionString;
    var assembly = typeof(DmConnection).Assembly;
    result["assembly_name"] = assembly.GetName().Name;
    result["assembly_version"] = assembly.GetName().Version?.ToString();
    result["assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
    result["assembly_sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
    string table = "T05B_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_name"] = table;
    result["explicit_test_schema"] = true;
    result["transport_policy"] = args[0] == "W" ? "PlaintextAllowed" : "baseline_provider_configuration";
    bool identityVerified = false, insertSucceeded = false, readbackVerified = false, cleanupVerified = false;
    bool readerSchemaSucceeded = false;
    bool readerSchemaIsKey = false;
    string? readerSchemaErrorKind = null;
    int? readerSchemaErrorNumber = null;
    Exception? workError = null, cleanupError = null;
    string? workStage = null;
    try
    {
        stage = "identity";
        using var c = OpenVerified(settings);
        identityVerified = true;
        stage = "create";
        Exec(c, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(100), PAYLOAD BLOB)");
        var payload = Enumerable.Range(0, 96 * 1024).Select(i => (byte)(i % 251)).ToArray();
        stage = "insert_three_parameters";
        insertSucceeded = Exec(c, $"INSERT INTO {table} (ID, VAL, PAYLOAD) VALUES (:p0, :p1, :p2)", 1, "one", payload) == 1;
        stage = "readback";
        using var independent = OpenVerified(settings);
        using (var command = Command(independent, $"SELECT PAYLOAD FROM {table} WHERE ID = 1", []))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            if (reader.Read())
            {
                var actual = new byte[payload.Length];
                long offset = 0;
                while (offset < actual.Length)
                {
                    long n = reader.GetBytes(0, offset, actual, (int)offset, Math.Min(4096, actual.Length - (int)offset));
                    if (n <= 0) break;
                    offset += n;
                }
                readbackVerified = offset == payload.Length && actual.SequenceEqual(payload);
            }
        }
        stage = "reader_schema";
        try
        {
            using var command = Command(c, $"SELECT ID FROM {table} ORDER BY ID", []);
            using var reader = command.ExecuteReader();
            var schema = reader.GetSchemaTable();
            readerSchemaSucceeded = schema is not null && schema.Rows.Count == 1;
            readerSchemaIsKey = readerSchemaSucceeded && schema!.Rows[0]["IsKey"] is true;
        }
        catch (Exception ex)
        {
            readerSchemaErrorKind = ex.GetType().Name;
            readerSchemaErrorNumber = SafeNumber(ex);
        }
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
    result["server_account_verified"] = identityVerified;
    result["insert_succeeded"] = insertSucceeded;
    result["independent_readback_verified"] = readbackVerified;
    result["reader_schema_succeeded"] = readerSchemaSucceeded;
    result["reader_schema_is_key"] = readerSchemaIsKey;
    result["reader_schema_error_kind"] = readerSchemaErrorKind;
    result["reader_schema_error_number"] = readerSchemaErrorNumber;
    result["cleanup_verified"] = cleanupVerified;
    result["final_database_state"] = cleanupVerified ? "random_object_absent" : "not_verified";
    if (workError is not null)
    {
        result["work_stage"] = workStage;
        result["work_error_kind"] = workError is ReproFailure failure ? failure.Kind : workError.GetType().Name;
        result["work_error_number"] = SafeNumber(workError);
        result["work_methods"] = SafeMethods(workError);
    }
    if (cleanupError is not null)
    {
        result["cleanup_error_kind"] = cleanupError.GetType().Name;
        result["cleanup_error_number"] = SafeNumber(cleanupError);
    }
    result["status"] = cleanupVerified ? "observed" : "rejected";
    if (!cleanupVerified) Environment.ExitCode = 1;
}
catch (Exception ex)
{
    result["status"] = "rejected";
    result["stage"] = stage;
    result["error_kind"] = ex is ReproFailure failure ? failure.Kind : ex.GetType().Name;
    result["error_number"] = SafeNumber(ex);
    Environment.ExitCode = 1;
}
Console.WriteLine(JsonSerializer.Serialize(result));

static DmConnection OpenVerified(string settings)
{
    var c = new DmConnection(settings);
    try
    {
        c.Open();
        if (!string.Equals(Scalar(c, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(c.Schema, TestUser, StringComparison.OrdinalIgnoreCase))
            throw new ReproFailure("server_identity_invalid");
        return c;
    }
    catch { c.Dispose(); throw; }
}
static bool ObjectExists(DmConnection c, string table) =>
    Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table), CultureInfo.InvariantCulture) != 0;
static object? Scalar(DmConnection c, string sql, params object[] values)
{ using var command = Command(c, sql, values); return command.ExecuteScalar(); }
static int Exec(DmConnection c, string sql, params object[] values)
{ using var command = Command(c, sql, values); return command.ExecuteNonQuery(); }
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
static int? SafeNumber(Exception ex)
{ try { return ex.GetType().GetProperty("Number")?.GetValue(ex) is int n ? n : null; } catch { return null; } }
static object[] SafeMethods(Exception ex) => new StackTrace(ex, true).GetFrames()?.Take(12)
    .Select(f => (object)new { type = f.GetMethod()?.DeclaringType?.Name, method = f.GetMethod()?.Name, line = f.GetFileLineNumber() }).ToArray() ?? [];
sealed class ReproFailure(string kind) : Exception { internal string Kind { get; } = kind; }
