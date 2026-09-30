using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dm;
using Dm.Config;

const string TestUser = "WDM_PROVIDER_TEST";
const string OfficialHash = "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b";
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
var implementation = args.Length > 1 ? args[1] : "unknown";
var runId = Guid.NewGuid().ToString("N");
var stage = "arguments";
try
{
    if (args.Length != 3 || args[0] is not ("offline" or "integration" or "api" or "diagnose-open") || implementation is not ("O" or "R"))
        throw new HarnessFailure("usage");
    var repo = Path.GetFullPath(args[2]);
    stage = "asset";
    var asset = AssetInfo(implementation, repo);
    if (args[0] == "diagnose-open")
    {
        var rawDiagnostic = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(rawDiagnostic)) throw new HarnessFailure("test_connection_missing");
        var options = new DmConnectionStringBuilder(rawDiagnostic);
        if (!string.Equals(options.User, TestUser, StringComparison.OrdinalIgnoreCase)) throw new HarnessFailure("test_account_invalid");
        options.ConnPooling = false;
        options.StmtPooling = false;
        options.PreparePooling = false;
        options.LogLevel = LogLevel.OFF;
        var events = new Queue<object>();
        void OnFirstChance(object? _, FirstChanceExceptionEventArgs e)
        {
            try
            {
                var frames = new StackTrace(e.Exception, false).GetFrames() ?? [];
                events.Enqueue(new { kind = e.Exception.GetType().Name, number = SafeError(e.Exception).Number,
                    methods = frames.Take(8).Select(f => f.GetMethod() is { } m ? (m.DeclaringType?.FullName ?? "") + "." + m.Name : "<unknown>").ToArray() });
                if (events.Count > 64) events.Dequeue();
            }
            catch { }
        }
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        try
        {
            using var connection = OpenChecked(options.ConnectionString);
            Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, status = "opened", asset,
                test_account_verified = true, first_chance = events.ToArray() }, json));
        }
        catch (Exception ex)
        {
            var error = SafeError(ex);
            Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, status = "failed",
                error_stage = ex.Data["t02_phase"] as string, error_kind = error.Kind, error_number = error.Number,
                diagnostic_chain = DiagnosticChain(ex), first_chance = events.ToArray() }, json));
            Environment.ExitCode = 1;
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance; }
        return;
    }
    if (args[0] == "api")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, asset, public_api = PublicApi() }, json));
        return;
    }
    stage = "resources";
    var resources = ResourceCheck();
    stage = "known_defect";
    var defect = KnownDefect();
    stage = "baseline_fixture";
    var baseline = ReadBaseline(Path.Combine(repo, "tests/fixtures/baseline/parameter-cases.json"));
    stage = "restoration_fixture";
    var restoration = ReadRestoration(Path.Combine(repo, "tests/fixtures/restoration/scenarios.json"));
    stage = "repair_cases";
    var repairCases = RepairCases(Path.Combine(repo, "tests/fixtures/restoration/bcd-cases.json"));
    stage = "structural_slots";
    var structuralSlots = StructuralSlots(implementation);
    if (args[0] == "offline")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, run_id = runId,
            status = resources.All(x => x.Readable) && defect.Observed && repairCases.All(x => x.Passed) && structuralSlots.Passed ? "offline_verified" : "failed",
            integration = "integration_pending", asset, resources, known_official_defect = defect, repair_cases = repairCases,
            structural_slots = structuralSlots,
            baseline_fixture_count = baseline.Count, restoration_fixture_count = restoration.Rows.Count,
            scenarios = ScenarioIds().Select(id => new { scenario_id = id, status = id == "known_official_defects" ? "observed_offline" : "pending" }) }, json));
        if (resources.Any(x => !x.Readable) || !defect.Observed || repairCases.Any(x => !x.Passed) || !structuralSlots.Passed) Environment.ExitCode = 1;
        return;
    }
    stage = "connection";
    var raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(raw)) throw new HarnessFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase)) throw new HarnessFailure("test_account_invalid");
    builder.ConnPooling = false;
    builder.StmtPooling = false;
    builder.PreparePooling = false;
    builder.LogLevel = LogLevel.OFF;
    var connectionString = builder.ConnectionString;
    using (var identityConnection = OpenChecked(connectionString)) { }
    var results = new List<ScenarioResult>();
    results.Add(OpenClose(connectionString));
    results.Add(RunReadOnly("parameter_select", connectionString, c => ParameterSelect(c, baseline)));
    results.Add(RunTable("unicode_crud", connectionString, "(ID INT PRIMARY KEY, VAL VARCHAR(400))", (c, table) => UnicodeCrud(c, table, restoration.Rows)));
    results.Add(RunTable("transaction_commit_rollback", connectionString, "(ID INT PRIMARY KEY, VAL VARCHAR(40))", (c, table) => Transactions(c, connectionString, table)));
    results.Add(RunTable("dml_affected_rows", connectionString, "(ID INT PRIMARY KEY, VAL VARCHAR(40))", (c, table) => AffectedRows(c, table)));
    results.Add(RunTable("dml_generated_key", connectionString, "(ID INT IDENTITY(1,1) PRIMARY KEY, VAL VARCHAR(40))", (c, table) => GeneratedKey(c, table)));
    results.Add(RunReadOnly("failure_then_select", connectionString, FailureThenSelect));
    results.Add(RunTable("lob", connectionString, "(ID INT PRIMARY KEY, VAL BLOB)", (c, table) => Lob(c, table, restoration.Lob)));
    results.Add(new ScenarioResult("known_official_defects", defect.Observed ? "observed" : "failed", defect, null, null, null, true, defect.Observed));
    var success = results.All(x => x.CleanupVerified && x.Status == "observed" && x.Passed) &&
        resources.All(x => x.Readable) && repairCases.All(x => x.Passed) && structuralSlots.Passed;
    Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, run_id = runId,
        status = success ? "observed" : "failed", integration = "real_test_schema", test_account_verified = true,
        asset, resources, repair_cases = repairCases, structural_slots = structuralSlots, scenarios = results }, json));
    if (!success) Environment.ExitCode = 1;
}
catch (Exception ex)
{
    var error = SafeError(ex);
    Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 1, implementation, run_id = runId,
        status = "rejected", error_stage = ex.Data["t02_phase"] as string ?? stage, error_kind = ex is HarnessFailure h ? h.Kind : error.Kind,
        error_number = error.Number, diagnostic_chain = DiagnosticChain(ex), integration = "not_verified" }, json));
    Environment.ExitCode = 1;
}

static string[] ScenarioIds() => ["open_close", "parameter_select", "unicode_crud", "transaction_commit_rollback",
    "dml_affected_rows", "dml_generated_key", "failure_then_select", "lob", "known_official_defects"];

static DmConnection OpenChecked(string connectionString)
{
    var connection = new DmConnection(connectionString);
    var phase = "connection_open";
    try
    {
        connection.Open();
        phase = "server_identity_query";
        if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase))
            throw new HarnessFailure("server_identity_mismatch");
        return connection;
    }
    catch (Exception ex) { try { ex.Data["t02_phase"] = phase; } catch { } connection.Dispose(); throw; }
}

static ScenarioResult OpenClose(string connectionString)
{
    try
    {
        using var connection = OpenChecked(connectionString);
        var opened = connection.State == ConnectionState.Open;
        connection.Close();
        var closed = connection.State == ConnectionState.Closed;
        var observation = new { opened, closed };
        return new("open_close", opened && closed ? "observed" : "failed", observation, null, null, null, true, opened && closed);
    }
    catch (Exception ex) { var e = SafeError(ex); return new("open_close", "failed", null, e.Kind, e.Number, null, true, false); }
}

static ScenarioResult RunReadOnly(string id, string connectionString, Func<DmConnection, object> run)
{
    try
    {
        using var connection = OpenChecked(connectionString);
        var observation = run(connection);
        var passed = CheckObservation(id, observation);
        return new(id, passed ? "observed" : "failed", observation, null, null, null, true, passed);
    }
    catch (Exception ex) { var e = SafeError(ex); return new(id, "failed", null, e.Kind, e.Number, null, true, false); }
}

static ScenarioResult RunTable(string id, string connectionString, string schema, Func<DmConnection, string, object> run)
{
    var table = "T02_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    object? observation = null;
    string? errorKind = null;
    int? errorNumber = null;
    var cleanupVerified = false;
    int? finalRowCount = null;
    var passed = false;
    try
    {
        using var connection = OpenChecked(connectionString);
        NonQuery(connection, $"CREATE TABLE {table} {schema}");
        observation = run(connection, table);
        passed = CheckObservation(id, observation);
        using var independent = OpenChecked(connectionString);
        finalRowCount = Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
        passed &= finalRowCount == ExpectedFinalRows(id);
    }
    catch (Exception ex) { var e = SafeError(ex); errorKind = e.Kind; errorNumber = e.Number; }
    finally
    {
        try
        {
            using var cleaner = OpenChecked(connectionString);
            // Only this run's random, unqualified table is eligible for cleanup.
            if (Convert.ToInt32(Scalar(cleaner, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table), CultureInfo.InvariantCulture) != 0)
                NonQuery(cleaner, $"DROP TABLE {table}");
            using var verifier = OpenChecked(connectionString);
            cleanupVerified = Convert.ToInt32(Scalar(verifier, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table), CultureInfo.InvariantCulture) == 0;
        }
        catch (Exception ex)
        {
            var e = SafeError(ex);
            errorKind ??= "Cleanup_" + e.Kind;
            errorNumber ??= e.Number;
            cleanupVerified = false;
        }
    }
    return new(id, errorKind is null && cleanupVerified && passed ? "observed" : "failed", observation,
        errorKind, errorNumber, finalRowCount, cleanupVerified, passed);
}

static int ExpectedFinalRows(string id) => id switch
{
    "transaction_commit_rollback" or "dml_generated_key" or "lob" => 1,
    _ => 0
};

static bool CheckObservation(string id, object observation)
{
    var x = JsonSerializer.SerializeToElement(observation, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    return id switch
    {
        "parameter_select" => x.GetArrayLength() > 0 && x.EnumerateArray().All(r => r.GetProperty("equal").GetBoolean()),
        "unicode_crud" => x.GetArrayLength() > 0 && x.EnumerateArray().All(r =>
            r.GetProperty("inserted").GetInt32() == 1 && r.GetProperty("read_equal").GetBoolean() &&
            r.GetProperty("updated").GetInt32() == 1 && r.GetProperty("update_equal").GetBoolean() &&
            r.GetProperty("deleted").GetInt32() == 1),
        "transaction_commit_rollback" => x.GetProperty("after_commit_independent").GetInt32() == 1 &&
            x.GetProperty("after_rollback_independent").GetInt32() == 1,
        "dml_affected_rows" => x.GetProperty("update_one").GetInt32() == 1 && x.GetProperty("update_zero").GetInt32() == 0 &&
            x.GetProperty("delete_one").GetInt32() == 1 && x.GetProperty("delete_zero").GetInt32() == 0 &&
            x.GetProperty("combined_rowcount").ValueKind == JsonValueKind.Number && x.GetProperty("combined_rowcount").GetInt64() == 0,
        "dml_generated_key" => x.GetProperty("generated_key").ValueKind == JsonValueKind.Number &&
            x.GetProperty("generated_key").GetInt64() > 0 && x.GetProperty("persisted_rows").GetInt32() == 1,
        "failure_then_select" => x.GetProperty("first_failed").GetBoolean() && x.GetProperty("next_select_one").GetBoolean(),
        "lob" => x.GetProperty("equal").GetBoolean(),
        _ => false
    };
}

static object ParameterSelect(DmConnection c, List<TextCase> cases)
{
    var results = new List<object>();
    foreach (var item in cases)
    {
        var observed = Scalar(c, "SELECT :p0 FROM DUAL", item.Value)?.ToString();
        results.Add(new { item.Id, equal = observed == item.Value, observed_sha256 = observed is null ? null : Hash(Encoding.UTF8.GetBytes(observed)) });
    }
    return results;
}

static object UnicodeCrud(DmConnection c, string table, List<TextCase> rows)
{
    var checks = new List<object>();
    foreach (var row in rows)
    {
        var id = int.Parse(row.Id, CultureInfo.InvariantCulture);
        var inserted = NonQuery(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", id, row.Value);
        var observed = Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", id)?.ToString();
        var updated = NonQuery(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", row.Value + "-u", id);
        var updateValue = Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", id)?.ToString();
        var deleted = NonQuery(c, $"DELETE FROM {table} WHERE ID = :p0", id);
        checks.Add(new { row.Id, inserted, read_equal = observed == row.Value,
            read_sha256 = observed is null ? null : Hash(Encoding.UTF8.GetBytes(observed)), updated,
            update_equal = updateValue == row.Value + "-u", deleted });
    }
    return checks;
}

static object Transactions(DmConnection c, string connectionString, string table)
{
    using (var tx = c.BeginTransaction())
    {
        NonQueryTx(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 1, "commit");
        tx.Commit();
    }
    int afterCommit;
    using (var verify = OpenChecked(connectionString))
        afterCommit = Convert.ToInt32(Scalar(verify, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
    using (var tx = c.BeginTransaction())
    {
        NonQueryTx(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 2, "rollback");
        tx.Rollback();
    }
    int afterRollback;
    using (var verify = OpenChecked(connectionString))
        afterRollback = Convert.ToInt32(Scalar(verify, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
    return new { after_commit_independent = afterCommit, after_rollback_independent = afterRollback };
}

static object AffectedRows(DmConnection c, string table)
{
    NonQuery(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", 1, "base");
    var updateOne = NonQuery(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", "changed", 1);
    var updateZero = NonQuery(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", "changed", 2);
    var deleteOne = NonQuery(c, $"DELETE FROM {table} WHERE ID = :p0", 1);
    var deleteZero = NonQuery(c, $"DELETE FROM {table} WHERE ID = :p0", 2);
    // The exact S05 EF shape must remain one CommandText; never split on ';'.
    var combined = ReadFirstNumber(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1;\n/*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT;", "none", 2);
    return new { update_one = updateOne, update_zero = updateZero, delete_one = deleteOne,
        delete_zero = deleteZero, combined_rowcount = combined };
}

static object GeneratedKey(DmConnection c, string table)
{
    // S05's DML + SCOPE_IDENTITY() query is sent intact to the provider.
    var key = ReadFirstNumber(c, $"INSERT INTO {table} (VAL) VALUES (:p0);\nSELECT ID FROM {table} WHERE SQL%ROWCOUNT = 1 AND ID = SCOPE_IDENTITY();", "generated");
    var persisted = Convert.ToInt32(Scalar(c, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
    return new { generated_key = key, persisted_rows = persisted };
}

static object FailureThenSelect(DmConnection c)
{
    string? firstKind = null;
    int? firstNumber = null;
    try { Scalar(c, "SELECT T02_MISSING_COLUMN FROM DUAL"); }
    catch (Exception ex) { var e = SafeError(ex); firstKind = e.Kind; firstNumber = e.Number; }
    var next = Scalar(c, "SELECT 1 FROM DUAL");
    return new { first_failed = firstKind is not null, first_error_kind = firstKind,
        first_error_number = firstNumber, next_select_one = Convert.ToInt32(next, CultureInfo.InvariantCulture) == 1 };
}

static object Lob(DmConnection c, string table, LobCase lob)
{
    var bytes = Enumerable.Range(0, lob.Length).Select(i => unchecked((byte)((i * 31 + 7) % 256))).ToArray();
    if (!string.Equals(Hash(bytes), lob.Sha256, StringComparison.OrdinalIgnoreCase)) throw new HarnessFailure("lob_fixture_hash_invalid");
    NonQuery(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", 1, bytes);
    using var command = Command(c, $"SELECT VAL FROM {table} WHERE ID = :p0", null, 1);
    using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
    if (!reader.Read()) return new { length = 0L, sha256 = (string?)null, equal = false };
    using var stream = new MemoryStream();
    var buffer = new byte[8192];
    long offset = 0;
    while (true)
    {
        var n = reader.GetBytes(0, offset, buffer, 0, buffer.Length);
        if (n == 0) break;
        stream.Write(buffer, 0, (int)n);
        offset += n;
        if (offset > lob.Length + 1) throw new HarnessFailure("lob_length_exceeded");
    }
    return new { length = offset, sha256 = Hash(stream.ToArray()), equal = offset == lob.Length && Hash(stream.ToArray()) == lob.Sha256 };
}

static long? ReadFirstNumber(DmConnection c, string sql, params object[] values)
{
    using var command = Command(c, sql, null, values);
    using var reader = command.ExecuteReader();
    do { if (reader.FieldCount > 0 && reader.Read()) return Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture); }
    while (reader.NextResult());
    return null;
}

static object? Scalar(DmConnection c, string sql, params object[] values)
{ using var command = Command(c, sql, null, values); return command.ExecuteScalar(); }
static int NonQuery(DmConnection c, string sql, params object[] values) => NonQueryTx(c, sql, null, values);
static int NonQueryTx(DmConnection c, string sql, DbTransaction? tx, params object[] values)
{ using var command = Command(c, sql, tx, values); return command.ExecuteNonQuery(); }
static DbCommand Command(DmConnection c, string sql, DbTransaction? tx, params object[] values)
{
    var command = c.CreateCommand();
    command.CommandText = sql;
    command.Transaction = tx;
    for (var i = 0; i < values.Length; i++)
    {
        var p = command.CreateParameter();
        p.ParameterName = "p" + i.ToString(CultureInfo.InvariantCulture);
        p.DbType = values[i] is byte[] ? DbType.Binary : values[i] is int ? DbType.Int32 : DbType.String;
        p.Value = values[i];
        command.Parameters.Add(p);
    }
    return command;
}

static List<TextCase> ReadBaseline(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new HarnessFailure("baseline_fixture_schema_invalid");
    var rows = doc.RootElement.GetProperty("cases").EnumerateArray().Select(x => new TextCase(
        x.GetProperty("id").GetString()!, x.GetProperty("value").GetString()!, x.GetProperty("utf8_sha256").GetString()!)).ToList();
    CheckRows(rows);
    return rows;
}
static RestorationFixture ReadRestoration(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new HarnessFailure("restoration_fixture_schema_invalid");
    var rows = doc.RootElement.GetProperty("unicode_rows").EnumerateArray().Select(x => new TextCase(
        x.GetProperty("id").GetRawText(), x.GetProperty("value").GetString()!, x.GetProperty("utf8_sha256").GetString()!)).ToList();
    CheckRows(rows);
    var lob = doc.RootElement.GetProperty("lob");
    var result = new LobCase(lob.GetProperty("length").GetInt32(), lob.GetProperty("algorithm").GetString()!, lob.GetProperty("sha256").GetString()!);
    if (result.Length is < 1 or > 1048576 || result.Algorithm != "(index * 31 + 7) % 256") throw new HarnessFailure("lob_fixture_invalid");
    return new(rows, result);
}
static void CheckRows(List<TextCase> rows)
{
    if (rows.Count == 0 || rows.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
        rows.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Value.Length == 0 ||
        !string.Equals(Hash(Encoding.UTF8.GetBytes(x.Value)), x.Sha256, StringComparison.OrdinalIgnoreCase)))
        throw new HarnessFailure("fixture_hash_invalid");
}

static List<RepairObservation> RepairCases(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new HarnessFailure("repair_fixture_schema_invalid");
    var entries = doc.RootElement.GetProperty("cases").EnumerateArray().Select(x => (
        Id: x.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture), Value: x.GetProperty("value").GetString()!,
        Expected: x.GetProperty("expected_hex").GetString()!)).ToArray();
    if (entries.Length == 0 || entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length ||
        entries.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Expected.Length % 2 != 0 ||
            x.Expected.Any(ch => !Uri.IsHexDigit(ch)))) throw new HarnessFailure("repair_fixture_invalid");
    var type = typeof(DmConnection).Assembly.GetType("Dm.DmSetValue", throwOnError: true)!;
    var instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, args: ["UTF-8"], culture: CultureInfo.InvariantCulture) ?? throw new HarnessFailure("repair_type_unavailable");
    var method = type.GetMethod("decStringToBcd", BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, types: [typeof(string)], modifiers: null) ?? throw new HarnessFailure("repair_method_unavailable");
    var results = new List<RepairObservation>();
    foreach (var entry in entries)
    {
        try
        {
            var actual = method.Invoke(instance, [entry.Value]) as byte[];
            var hex = actual is null ? null : Convert.ToHexStringLower(actual);
            results.Add(new(entry.Id, hex, hex == entry.Expected.ToLowerInvariant(), null));
        }
        catch (Exception ex)
        {
            var error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            results.Add(new(entry.Id, null, false, SafeError(error).Kind));
        }
    }
    return results;
}

static SlotObservation StructuralSlots(string implementation)
{
    var assembly = typeof(DmConnection).Assembly;
    var baseType = assembly.GetType("A.a");
    var derivedType = assembly.GetType("A.D");
    var messageType = assembly.GetType("A.b");
    if (baseType is null || derivedType is null || messageType is null)
        return new(false, false, false, false, false);
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    static bool Matches(MethodInfo m, Type messageType)
    {
        var p = m.GetParameters();
        return m.ReturnType == messageType && p.Length == 4 && p[0].ParameterType == messageType &&
            p[1].ParameterType == typeof(int) && p[2].ParameterType == typeof(bool) && p[3].ParameterType == typeof(bool);
    }
    var bases = baseType.GetMethods(flags).Where(m => Matches(m, messageType)).ToArray();
    var derived = derivedType.GetMethods(flags).Where(m => Matches(m, messageType)).ToArray();
    var exactCount = bases.Length == 1 && derived.Length == 1;
    if (!exactCount) return new(false, false, false, false, false);
    var expectedName = implementation == "O" ? "a" : "__t02_method_06000A4D";
    return new(true, bases[0].IsVirtual, derived[0].IsVirtual,
        derived[0].GetBaseDefinition() == bases[0] && derivedType.BaseType == baseType,
        bases[0].Name == expectedName && derived[0].Name == expectedName);
}

static object AssetInfo(string implementation, string repo)
{
    var assembly = typeof(DmConnection).Assembly;
    var path = assembly.Location;
    var sha = Hash(File.ReadAllBytes(path));
    var expected = implementation == "O" ? OfficialHash : null;
    if (implementation == "O" && sha != expected || implementation == "R" && sha == OfficialHash)
        throw new HarnessFailure("driver_asset_identity_invalid");
    if (implementation == "R")
    {
        var restoredPath = Path.Combine(repo, ".local/t02/restored/bin/Debug/net9.0/DM.DmProvider.dll");
        if (!File.Exists(restoredPath) || Hash(File.ReadAllBytes(restoredPath)) != sha ||
            AssemblyName.GetAssemblyName(restoredPath).Name != "DM.DmProvider" ||
            Mvid(restoredPath) != assembly.ManifestModule.ModuleVersionId)
            throw new HarnessFailure("restored_asset_mismatch");
    }
    var relative = Path.GetRelativePath(repo, path).Replace('\\', '/');
    if (relative.StartsWith("../", StringComparison.Ordinal) || !relative.StartsWith("tools/BaselineComparison/", StringComparison.Ordinal))
        throw new HarnessFailure("driver_asset_location_invalid");
    return new { sha256 = sha, mvid = assembly.ManifestModule.ModuleVersionId.ToString(),
        assembly_name = assembly.GetName().Name, assembly_version = assembly.GetName().Version?.ToString(),
        loaded_path = relative, runtime = RuntimeInformation.FrameworkDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(), expected_official_sha256 = expected };
}

static List<ResourceObservation> ResourceCheck()
{
    var assembly = typeof(DmConnection).Assembly;
    var manager = new ResourceManager(typeof(DmErrorDefinition));
    const string key = "message.error";
    var result = new List<ResourceObservation>();
    foreach (var tag in new[] { "neutral", "en", "zh-CN", "zh-HK", "zh-TW" })
    {
        var culture = tag == "neutral" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(tag);
        var satellitePath = tag == "neutral" ? null : Path.Combine(Path.GetDirectoryName(assembly.Location)!, tag, "DM.DmProvider.resources.dll");
        var exists = satellitePath is null || File.Exists(satellitePath);
        string? valueHash = null;
        var direct = false;
        try
        {
            var set = manager.GetResourceSet(culture, true, false);
            var value = set?.GetString(key);
            direct = value is not null;
            if (value is not null) valueHash = Hash(Encoding.UTF8.GetBytes(value));
        }
        catch { }
        result.Add(new(tag, exists && direct, exists, satellitePath is null ? null : exists ? Hash(File.ReadAllBytes(satellitePath)) : null, valueHash));
    }
    return result;
}

static string[] PublicApi()
{
    var lines = new List<string>();
    foreach (var type in typeof(DmConnection).Assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        lines.Add("T " + (type.IsInterface ? "interface " : type.IsEnum ? "enum " : type.IsValueType ? "struct " : "class ") +
            TypeName(type) + " : " + TypeName(type.BaseType) + " implements " +
            string.Join(",", type.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal)));
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var member in type.GetMembers(flags))
        {
            string? signature = member switch
            {
                ConstructorInfo ctor when Visible(ctor) => "ctor " + MethodModifiers(ctor) + "(" + Parameters(ctor.GetParameters()) + ")",
                MethodInfo method when Visible(method) => "method " + MethodModifiers(method) + " " + TypeName(method.ReturnType) + " " + method.Name +
                    (method.IsGenericMethodDefinition ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">" : "") +
                    "(" + Parameters(method.GetParameters()) + ")",
                PropertyInfo property when property.GetAccessors(true).Any(Visible) => "property " + TypeName(property.PropertyType) + " " + property.Name +
                    "[" + Parameters(property.GetIndexParameters()) + "]{" +
                    string.Join(",", property.GetAccessors(true).Where(Visible).Select(a =>
                        (a.Name.StartsWith("get_", StringComparison.Ordinal) ? "get " : "set ") + MethodModifiers(a))) + "}",
                EventInfo evt when (evt.GetAddMethod(true) is { } add && Visible(add)) ||
                                   (evt.GetRemoveMethod(true) is { } remove && Visible(remove)) =>
                    "event " + TypeName(evt.EventHandlerType) + " " + evt.Name + "{" +
                    string.Join(",", new[] { evt.GetAddMethod(true), evt.GetRemoveMethod(true) }.Where(a => a is not null && Visible(a))
                        .Select(a => a!.Name.StartsWith("add_", StringComparison.Ordinal) ? "add " + MethodModifiers(a) : "remove " + MethodModifiers(a))) + "}",
                FieldInfo field when VisibleField(field) => "field " + FieldModifiers(field) + " " + TypeName(field.FieldType) + " " + field.Name +
                    (field.IsLiteral ? " = " + Constant(field.GetRawConstantValue()) : ""),
                _ => null
            };
            if (signature is not null) lines.Add("M " + type.FullName + " " + signature);
        }
    }
    return lines.Order(StringComparer.Ordinal).ToArray();
}

static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
static bool VisibleField(FieldInfo field) => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
static string MethodModifiers(MethodBase method) =>
    (method.IsPublic ? "public" : method.IsFamily ? "protected" : "protected_internal") +
    (method.IsStatic ? " static" : "") + (method.IsAbstract ? " abstract" : "") +
    (method.IsVirtual ? " virtual" : "") + (method.IsFinal ? " final" : "");
static string FieldModifiers(FieldInfo field) =>
    (field.IsPublic ? "public" : field.IsFamily ? "protected" : "protected_internal") +
    (field.IsStatic ? " static" : "") + (field.IsInitOnly ? " readonly" : "") +
    (field.IsLiteral ? " const" : "");
static string TypeName(Type? type)
{
    if (type is null) return "<null>";
    if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
    if (type.IsPointer) return TypeName(type.GetElementType()) + "*";
    if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
    if (type.IsGenericParameter) return type.Name;
    if (type.IsGenericType)
        return (type.GetGenericTypeDefinition().FullName ?? type.Name) + "<" +
            string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
    return type.FullName ?? type.Name;
}
static string Parameters(ParameterInfo[] parameters) => string.Join(",", parameters.Select(p =>
    (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + TypeName(p.ParameterType) + " " + p.Name +
    (p.IsOptional ? " optional=" + Constant(p.RawDefaultValue) : "")));
static string Constant(object? value) => value switch
{
    null => "null",
    string s => JsonSerializer.Serialize(s),
    char c => JsonSerializer.Serialize(c.ToString()),
    bool b => b ? "true" : "false",
    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<unknown>"
};

static DefectObservation KnownDefect()
{
    var b = new DmConnectionStringBuilder { User = "SYNTHETIC_USER", Password = "SYNTHETIC_PASSWORD" };
    b.InitialCatalog = "SYNTHETIC_CATALOG";
    return new(b.User == "SYNTHETIC_CATALOG" && b.Password == "SYNTHETIC_CATALOG",
        b.User == "SYNTHETIC_CATALOG", b.Password == "SYNTHETIC_CATALOG");
}

static (string Kind, int? Number) SafeError(Exception ex)
{
    int? number = null;
    try
    {
        var value = ex.GetType().GetProperty("Number", BindingFlags.Public | BindingFlags.Instance)?.GetValue(ex);
        if (value is int i) number = i;
    }
    catch { }
    return (ex.GetType().Name, number);
}
static object[] DiagnosticChain(Exception ex)
{
    var chain = new List<object>();
    for (var current = ex; current is not null && chain.Count < 4; current = current.InnerException!)
    {
        var frames = new StackTrace(current, fNeedFileInfo: false).GetFrames() ?? [];
        chain.Add(new { kind = current.GetType().Name, number = SafeError(current).Number,
            methods = frames.Take(16).Select(f => f.GetMethod() is { } m ? (m.DeclaringType?.FullName ?? "") + "." + m.Name : "<unknown>").ToArray() });
    }
    return chain.ToArray();
}
static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
static Guid Mvid(string path)
{
    using var file = File.OpenRead(path);
    using var pe = new PEReader(file);
    var metadata = pe.GetMetadataReader();
    return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
}

sealed record TextCase(string Id, string Value, string Sha256);
sealed record LobCase(int Length, string Algorithm, string Sha256);
sealed record RestorationFixture(List<TextCase> Rows, LobCase Lob);
sealed record ResourceObservation(string Culture, bool Readable, bool SatelliteExists, string? SatelliteSha256, string? ValueSha256);
sealed record DefectObservation(bool Observed, bool UserPolluted, bool PasswordPolluted);
sealed record RepairObservation(string Id, string? ActualHex, bool Passed, string? ErrorKind);
sealed record SlotObservation(bool SignatureUnique, bool BaseVirtual, bool DerivedVirtual, bool SameBaseDefinition, bool ExpectedName)
{
    public bool Passed => SignatureUnique && BaseVirtual && DerivedVirtual && SameBaseDefinition && ExpectedName;
}
sealed record ScenarioResult(string ScenarioId, string Status, object? Observation, string? ErrorKind, int? ErrorNumber,
    int? FinalRowCount, bool CleanupVerified, bool Passed);
sealed class HarnessFailure(string kind) : Exception { public string Kind { get; } = kind; }
