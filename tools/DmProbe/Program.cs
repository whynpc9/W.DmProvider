using System.Data;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dm;
using Dm.Config;

var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
var runId = Guid.NewGuid().ToString("N");
try
{
    if (args.Length is < 1 or > 2 || args[0] is not ("offline" or "official"))
        throw new ProbeFailure("usage", "invalid arguments");
    var repo = Path.GetFullPath(args.Length == 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "eng", "probe-scenarios.json")));
    var fixturePath = Path.Combine(repo, "tests", "fixtures", "baseline", "parameter-cases.json");
    var cases = ReadCases(fixturePath);
    var asset = CheckAsset(repo);
    var common = new
    {
        schema_version = 1, implementation = "O", host = "local-minimal-net10",
        run_id = runId, configuration_fingerprint = runId,
        source_commit = (string?)null,
        source_commit_missing_reason = "Official binary package has no verified source commit.",
        repository_baseline_commit = "860988296cb95deeb600328cb7bcd2160237a94f",
        driver_asset_sha256 = asset.Sha256,
        driver_mvid = asset.Mvid, driver_assembly_version = asset.Version,
        asset_compile = asset.Compile, asset_runtime = asset.Runtime,
        selected_asset = asset.LogicalPath, loaded_asset = asset.LoadedPath,
        loaded_asset_location = asset.LocationKind,
        runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        scenario_catalog_count = catalog.RootElement.GetProperty("scenarios").GetArrayLength()
    };
    if (args[0] == "offline")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { status = "offline_verified", common,
            fixture_case_count = cases.Length, integration = "integration_pending",
            results = new[] {
                new { scenario_id = "open_close", status = "pending", skipped_reason = "test_connection_not_requested" },
                new { scenario_id = "parameter_select", status = "pending", skipped_reason = "test_connection_not_requested" }
            } }, json));
        return;
    }
    var connectionString = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString)) throw new ProbeFailure("test_connection_missing", "missing test connection");
    // Parse final effective values; duplicate keys and aliases must not bypass the account gate.
    var options = new DmConnectionStringBuilder(connectionString);
    if (!string.Equals(options.User, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("test_connection_identity_invalid", "invalid test account");
    options.ConnPooling = false;
    options.StmtPooling = false;
    options.PreparePooling = false;
    options.LogLevel = LogLevel.OFF;
    using var connection = new DmConnection(options.ConnectionString);
    connection.Open();
    var identity = Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim();
    if (!string.Equals(identity, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("server_identity_mismatch", "server identity does not match the test user");
    var serverVersion = connection.ServerVersion;
    var profile = new { server_version = serverVersion, test_account_verified = true, instance_id = runId };
    var observations = new List<Observation>();
    foreach (var item in cases)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ? FROM DUAL";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p";
            parameter.DbType = DbType.String;
            parameter.Value = item.Value;
            command.Parameters.Add(parameter);
            var value = command.ExecuteScalar()?.ToString();
            observations.Add(new(item.Id, value == item.Value,
                value is null ? null : Sha256(Encoding.UTF8.GetBytes(value)), null, null));
        }
        catch (Exception ex)
        {
            observations.Add(new(item.Id, false, null, ex.GetType().Name, ErrorCode(ex)));
        }
    }
    var finalIdentity = Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim();
    if (!string.Equals(finalIdentity, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("server_identity_changed", "server identity changed during probe");
    connection.Close();
    var allEqual = observations.All(x => x.Equal);
    Console.WriteLine(JsonSerializer.Serialize(new { status = allEqual ? "observed" : "failed", common, profile,
        results = new object[] {
            new { scenario_id = "open_close", observed_result = "open_close_completed", final_database_state = "not_checked",
                sql_scope = "read_only", passed = true },
            new { scenario_id = "parameter_select", observed_result = observations, final_database_state = "not_checked",
                sql_scope = "read_only", passed = allEqual }
        }, evidence_level = "real_official_local_host" }, json));
    if (!allEqual) Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", implementation = "O", run_id = runId,
        error_kind = ex is ProbeFailure failure ? failure.Kind : ex.GetType().Name,
        error_code = ErrorCode(ex), integration = "not_verified", final_database_state = "not_checked" }, json));
    Environment.ExitCode = 1;
}

static object? Scalar(DmConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
}

static Case[] ReadCases(string path)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1) throw new ProbeFailure("fixture_schema_invalid", "invalid fixture");
    var entries = doc.RootElement.GetProperty("cases").EnumerateArray().Select(x => new Case(
        x.GetProperty("id").GetString()!, x.GetProperty("value").GetString()!,
        x.GetProperty("utf8_sha256").GetString()!)).ToArray();
    if (entries.Length == 0 || entries.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Value.Length == 0
        || !string.Equals(Sha256(Encoding.UTF8.GetBytes(x.Value)), x.Utf8Sha256, StringComparison.OrdinalIgnoreCase))
        || entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        throw new ProbeFailure("fixture_hash_invalid", "invalid fixture");
    return entries;
}

static Asset CheckAsset(string repo)
{
    var assetsPath = Path.Combine(repo, "tools", "DmProbe", "obj", "project.assets.json");
    using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
    var target = assets.RootElement.GetProperty("targets").GetProperty("net10.0")
        .GetProperty("DM.DmProvider/8.3.1.47463");
    var compile = target.GetProperty("compile").EnumerateObject().Single().Name.Replace('\\', '/');
    var runtime = target.GetProperty("runtime").EnumerateObject().Single().Name.Replace('\\', '/');
    const string logical = "lib/net9.0/DM.DmProvider.dll";
    if (compile != logical || runtime != logical) throw new ProbeFailure("asset_selection_mismatch", "incorrect package asset");
    var assembly = typeof(DmConnection).Assembly;
    var loaded = assembly.Location;
    if (!File.Exists(loaded)) throw new ProbeFailure("loaded_asset_missing", "loaded asset unavailable");
    var source = Path.Combine(repo, "packages", "extracted", "lib", "net9.0", "DM.DmProvider.dll");
    var expected = Sha256(File.ReadAllBytes(source));
    var actual = Sha256(File.ReadAllBytes(loaded));
    using var stream = File.OpenRead(loaded);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    var mvid = md.GetGuid(md.GetModuleDefinition().Mvid).ToString();
    using var sourceStream = File.OpenRead(source);
    using var sourcePe = new PEReader(sourceStream);
    var sourceMvid = sourcePe.GetMetadataReader().GetGuid(sourcePe.GetMetadataReader().GetModuleDefinition().Mvid).ToString();
    if (expected != actual || mvid != sourceMvid) throw new ProbeFailure("loaded_asset_mismatch", "loaded asset differs from snapshot");
    var relativeLoaded = Path.GetRelativePath(repo, loaded).Replace('\\', '/');
    var contained = !relativeLoaded.StartsWith("../", StringComparison.Ordinal) && relativeLoaded != "..";
    var locationKind = contained && relativeLoaded.StartsWith(".local/t01/nuget-packages/", StringComparison.Ordinal)
        ? "isolated-nuget-cache" : contained ? "host-output" : "external-runtime-location";
    return new Asset(compile, runtime, logical,
        contained ? relativeLoaded : "external-runtime-location", locationKind, actual, mvid,
        assembly.GetName().Version?.ToString());
}

static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

static int? ErrorCode(Exception ex)
{
    var property = ex.GetType().GetProperty("Code") ?? ex.GetType().GetProperty("ErrorCode");
    return property?.GetValue(ex) is int code ? code : null;
}

sealed record Case(string Id, string Value, string Utf8Sha256);
sealed record Observation(string Id, bool Equal, string? ObservedUtf8Sha256, string? ExceptionKind, int? ErrorCode);
sealed record Asset(string Compile, string Runtime, string LogicalPath, string LoadedPath, string LocationKind, string Sha256,
    string Mvid, string? Version);
sealed class ProbeFailure(string kind, string message) : Exception(message) { public string Kind { get; } = kind; }
