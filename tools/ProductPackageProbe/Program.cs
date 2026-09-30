using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using W.Dm;

const string TestUser = "WDM_PROVIDER_TEST";
var stage = "arguments";
var result = new Dictionary<string, object?> { ["schema_version"] = 1, ["implementation"] = "W" };
try
{
    if (args.Length != 3 || args[0] is not ("offline" or "real")) throw new ProbeFailure("usage");
    var repo = Path.GetFullPath(args[1]);
    var manifest = JsonDocument.Parse(File.ReadAllText(args[2])).RootElement;
    var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "tests/fixtures/product-identity/contract.json"))).RootElement;
    var smoke = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "tests/fixtures/product-identity/smoke-cases.json"))).RootElement;
    if (contract.GetProperty("schema_version").GetInt32() != 1 || smoke.GetProperty("schema_version").GetInt32() != 1)
        throw new ProbeFailure("fixture_schema_invalid");
    stage = "package_asset";
    var assembly = typeof(DmConnection).Assembly;
    var assetPath = Path.GetFullPath(assembly.Location);
    var expectedHash = manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString();
    var actualHash = Sha(File.ReadAllBytes(assetPath));
    var expectedLoadedPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "app", "W.DmProvider.dll");
    if (actualHash != expectedHash || assembly.GetName().Name != "W.DmProvider" ||
        contract.GetProperty("assembly_name").GetString() != "W.DmProvider" ||
        !string.Equals(assetPath, expectedLoadedPath, StringComparison.Ordinal))
        throw new ProbeFailure("loaded_asset_mismatch");
    var expectedVersion = manifest.GetProperty("version").GetString();
    var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (informationalVersion != expectedVersion) throw new ProbeFailure("assembly_version_mismatch");
    if (assembly.GetReferencedAssemblies().Any(x => x.Name?.Contains("DM.DmProvider", StringComparison.OrdinalIgnoreCase) == true) ||
        AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name?.Equals("DM.DmProvider", StringComparison.OrdinalIgnoreCase) == true))
        throw new ProbeFailure("official_reference_loaded");
    var depsPath = Path.Combine(AppContext.BaseDirectory, "ProductPackageProbe.deps.json");
    if (!File.Exists(depsPath) || File.ReadAllText(depsPath).Contains("DM.DmProvider", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("deps_invalid");
    result["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
    result["loaded_assembly_sha256"] = actualHash;
    result["assembly_name"] = assembly.GetName().Name;
    result["informational_version"] = informationalVersion;
    result["actual_references"] = assembly.GetReferencedAssemblies().Select(x => x.Name).OrderBy(x => x).ToArray();
    stage = "core_types";
    var prefix = contract.GetProperty("namespace").GetString() + ".";
    var checkedTypes = new List<string>();
    foreach (var item in contract.GetProperty("core_types").EnumerateArray())
    {
        var name = item.GetString()!;
        var type = assembly.GetType(name, throwOnError: false);
        if (type is null || !type.IsPublic || type.Assembly != assembly || !name.StartsWith(prefix, StringComparison.Ordinal))
            throw new ProbeFailure("core_type_invalid");
        checkedTypes.Add(name);
    }
    result["core_types"] = checkedTypes;
    stage = "factory";
    var factoryType = assembly.GetType("W.Dm.DmClientFactory", throwOnError: true)!;
    var instance = factoryType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
        ?? factoryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    if (instance is not DbProviderFactory factory || factory.CanCreateBatch)
        throw new ProbeFailure("factory_or_batch_invalid");
    DbProviderFactories.RegisterFactory("W.DmProvider", factory);
    if (!ReferenceEquals(DbProviderFactories.GetFactory("W.DmProvider"), factory))
        throw new ProbeFailure("factory_registration_invalid");
    var factoryChecks = new List<object>();
    foreach (var item in contract.GetProperty("factory_cases").EnumerateArray())
    {
        var method = item.GetProperty("method").GetString()!;
        var expected = item.GetProperty("type").GetString()!;
        var returned = factoryType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)?.Invoke(factory, null);
        if (returned is null || returned.GetType().FullName != expected || returned.GetType().Assembly != assembly)
            throw new ProbeFailure("factory_return_invalid");
        factoryChecks.Add(new { method, type = expected });
        (returned as IDisposable)?.Dispose();
    }
    result["factory_registration"] = "W.DmProvider";
    result["factory_cases"] = factoryChecks;
    result["can_create_batch"] = factory.CanCreateBatch;
    stage = "resources";
    var resourceBase = contract.GetProperty("resource_name").GetString()!;
    var resourceName = resourceBase + ".resources";
    var resourceKey = contract.GetProperty("resource_key").GetString()!;
    var expectedValues = contract.GetProperty("resource_value_sha256");
    var resources = new List<object>();
    foreach (var tag in contract.GetProperty("cultures").EnumerateArray().Select(x => x.GetString()!))
    {
        var directAssembly = tag == "neutral" ? assembly :
            Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, tag, "W.DmProvider.resources.dll"));
        if (tag != "neutral" &&
            (directAssembly.GetName().Name != "W.DmProvider.resources" ||
             directAssembly.GetName().CultureName != tag))
            throw new ProbeFailure("satellite_identity_invalid");
        var path = directAssembly.Location;
        var assetKey = tag == "neutral" ? "lib/net10.0/W.DmProvider.dll" :
            $"lib/net10.0/{tag}/W.DmProvider.resources.dll";
        if (Sha(File.ReadAllBytes(path)) != manifest.GetProperty("assets").GetProperty(assetKey).GetString())
            throw new ProbeFailure("satellite_hash_mismatch");
        var directName = tag == "neutral" ? resourceName : resourceBase + "." + tag + ".resources";
        using var stream = directAssembly.GetManifestResourceStream(directName);
        if (stream is null) throw new ProbeFailure("direct_resource_missing");
        using var reader = new ResourceReader(stream);
        var entries = reader.GetEnumerator();
        string? value = null;
        while (entries.MoveNext()) if (Equals(entries.Key, resourceKey)) value = entries.Value as string;
        if (value is null || Sha(Encoding.UTF8.GetBytes(value)) != expectedValues.GetProperty(tag).GetString())
            throw new ProbeFailure("direct_resource_value_invalid");
        resources.Add(new { culture = tag, satellite_sha256 = tag == "neutral" ? null : Sha(File.ReadAllBytes(path)),
            value_sha256 = Sha(Encoding.UTF8.GetBytes(value)) });
    }
    result["resources"] = resources;
    stage = "fixture";
    var rows = smoke.GetProperty("rows").EnumerateArray().Select(x => new Row(
        x.GetProperty("id").ValueKind == JsonValueKind.Number ? x.GetProperty("id").GetInt32() :
            int.Parse(x.GetProperty("id").GetString()!, CultureInfo.InvariantCulture),
        x.GetProperty("value").GetString()!, x.GetProperty("utf8_sha256").GetString()!)).ToArray();
    if (rows.Length is < 1 or > 8 || rows.Select(x => x.Id).Distinct().Count() != rows.Length ||
        rows.Any(x => Sha(Encoding.UTF8.GetBytes(x.Value)) != x.Sha256))
        throw new ProbeFailure("smoke_fixture_invalid");
    result["fixture_rows"] = rows.Length;
    if (args[0] == "offline")
    {
        result["status"] = "offline_verified";
        result["integration"] = "integration_pending";
    }
    else
    {
        stage = "real";
        var raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(raw)) throw new ProbeFailure("test_connection_missing");
        var builderType = assembly.GetType("W.Dm.DmConnectionStringBuilder", throwOnError: true)!;
        var builder = (DbConnectionStringBuilder)Activator.CreateInstance(builderType)!;
        builder.ConnectionString = raw;
        var user = builderType.GetProperty("User")?.GetValue(builder)?.ToString();
        if (!string.Equals(user, TestUser, StringComparison.OrdinalIgnoreCase)) throw new ProbeFailure("test_account_invalid");
        foreach (var key in new[] { "ConnPooling", "StmtPooling", "PreparePooling" })
            builderType.GetProperty(key)?.SetValue(builder, false);
        var logLevel = builderType.GetProperty("LogLevel");
        if (logLevel is null || !logLevel.PropertyType.IsEnum ||
            !Enum.IsDefined(logLevel.PropertyType, "OFF"))
            throw new ProbeFailure("log_level_off_unavailable");
        logLevel.SetValue(builder, Enum.Parse(logLevel.PropertyType, "OFF"));
        var connectionString = builder.ConnectionString;
        var table = "T03_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        bool cleanupVerified = false;
        int finalRows = -1;
        Exception? workError = null;
        Exception? cleanupError = null;
        try
        {
            using var c = Open(factory, connectionString);
            Exec(c, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(400))");
            foreach (var row in rows)
            {
                if (Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", null, row.Id, row.Value) != 1)
                    throw new ProbeFailure("insert_count_invalid");
                var observed = Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", null, row.Id)?.ToString();
                if (observed is null || Sha(Encoding.UTF8.GetBytes(observed)) != row.Sha256)
                    throw new ProbeFailure("unicode_read_invalid");
                if (Exec(c, $"UPDATE {table} SET VAL = :p0 WHERE ID = :p1", null, row.Value + "-u", row.Id) != 1 ||
                    Scalar(c, $"SELECT VAL FROM {table} WHERE ID = :p0", null, row.Id)?.ToString() != row.Value + "-u" ||
                    Exec(c, $"DELETE FROM {table} WHERE ID = :p0", null, row.Id) != 1)
                    throw new ProbeFailure("unicode_update_delete_invalid");
            }
            using (var tx = c.BeginTransaction())
            {
                Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 901, "commit");
                tx.Commit();
            }
            using (var independent = Open(factory, connectionString))
                if (Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture) != 1)
                    throw new ProbeFailure("commit_not_visible");
            using (var tx = c.BeginTransaction())
            {
                Exec(c, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", tx, 902, "rollback");
                tx.Rollback();
            }
            using (var independent = Open(factory, connectionString))
                finalRows = Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture);
            if (finalRows != 1) throw new ProbeFailure("rollback_visible");
        }
        catch (Exception ex) { workError = ex; }
        try
        {
            // Check only this run's random, unqualified table even if CREATE
            // completed on the server but returned an error to the client.
            using var cleaner = Open(factory, connectionString);
            if (Convert.ToInt32(Scalar(cleaner, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", null, table), CultureInfo.InvariantCulture) != 0)
                Exec(cleaner, $"DROP TABLE {table}");
            using var verify = Open(factory, connectionString);
            cleanupVerified = Convert.ToInt32(Scalar(verify, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", null, table), CultureInfo.InvariantCulture) == 0;
        }
        catch (Exception ex) { cleanupError = ex; }
        result["cleanup_verified"] = cleanupVerified;
        if (cleanupError is not null)
        {
            result["cleanup_error_kind"] = cleanupError.GetType().Name;
            result["cleanup_error_number"] = SafeNumber(cleanupError);
            if (workError is not null)
            {
                result["work_error_kind"] = workError.GetType().Name;
                result["work_error_number"] = SafeNumber(workError);
            }
            throw new ProbeFailure("cleanup_failed");
        }
        if (workError is not null) throw workError;
        if (!cleanupVerified) throw new ProbeFailure("cleanup_unverified");
        result["status"] = "real_verified";
        result["integration"] = "real_test_schema";
        result["server_account_verified"] = true;
        result["final_rows_before_cleanup"] = finalRows;
    }
}
catch (Exception ex)
{
    var e = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
    result["status"] = "rejected";
    result["stage"] = stage;
    result["error_kind"] = e is ProbeFailure p ? p.Kind : e.GetType().Name;
    result["error_number"] = SafeNumber(e);
    Environment.ExitCode = 1;
}
Console.WriteLine(JsonSerializer.Serialize(result));

static DbConnection Open(DbProviderFactory factory, string connectionString)
{
    var c = factory.CreateConnection() ?? throw new ProbeFailure("connection_factory_null");
    try
    {
        c.ConnectionString = connectionString;
        c.Open();
        if (!string.Equals(Scalar(c, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser, StringComparison.OrdinalIgnoreCase))
            throw new ProbeFailure("server_account_invalid");
        return c;
    }
    catch { c.Dispose(); throw; }
}
static object? Scalar(DbConnection c, string sql, DbTransaction? tx = null, params object[] values)
{ using var cmd = Command(c, sql, tx, values); return cmd.ExecuteScalar(); }
static int Exec(DbConnection c, string sql, DbTransaction? tx = null, params object[] values)
{ using var cmd = Command(c, sql, tx, values); return cmd.ExecuteNonQuery(); }
static DbCommand Command(DbConnection c, string sql, DbTransaction? tx, object[] values)
{
    var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.Transaction = tx;
    for (var i = 0; i < values.Length; i++)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "p" + i.ToString(CultureInfo.InvariantCulture);
        p.DbType = values[i] is int ? DbType.Int32 : DbType.String;
        p.Value = values[i];
        cmd.Parameters.Add(p);
    }
    return cmd;
}
static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
static int? SafeNumber(Exception e)
{
    try { return e.GetType().GetProperty("Number")?.GetValue(e) is int n ? n : null; }
    catch { return null; }
}
sealed record Row(int Id, string Value, string Sha256);
sealed class ProbeFailure(string kind) : Exception { public string Kind { get; } = kind; }
