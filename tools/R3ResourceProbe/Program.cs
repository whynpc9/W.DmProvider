using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;
using W.Dm.Config;
using W.Dm.Internal.Diagnostics;
using W.DmProvider.DiagnosticsTesting;

internal static partial class Program
{
    private const string User = "WDM_PROVIDER_TEST";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T18", ["implementation"] = "W-package", ["accepted"] = false,
        ["exit_code"] = -1, ["production_release_accepted"] = false,
        ["upstream_pending"] = new[] { "T17_shared_large_value", "shared_cleanup_recovery", "complete_R3_matrix" },
        ["runtime"] = RuntimeInformation.FrameworkDescription, ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier
    };
    private static string Mode = "", Repo = "", PackageManifest = "", StageValue = "arguments", Settings = "", Table = "";
    private static string? Output;
    private static string Stage { get => StageValue; set { StageValue = value; Report["checkpoint_stage"] = value; Checkpoint(); } }
    private static void Checkpoint()
    { if (Output != null) { File.WriteAllText(Output + ".tmp", JsonSerializer.Serialize(Report)); File.Move(Output + ".tmp", Output, true); } }
    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        try
        {
            Require(args.Length == 4 && (args[0] is "offline" or "tls" or "release-environment" || args[0].StartsWith("first-init:", StringComparison.Ordinal)), "usage");
            Mode = args[0]; Repo = Path.GetFullPath(args[1]); PackageManifest = args[2]; Output = args[3]; Report["mode"] = Mode;
            Stage = "exact_package_identity"; VerifyPackage(PackageManifest);
            Report["resource_budget"] = ResourceBudget;
            if (Mode.StartsWith("first-init:", StringComparison.Ordinal)) await FirstInitializationChildAsync(Mode[11..]);
            else if (Mode == "release-environment")
            {
                foreach (string name in new[] { "DAMENG_TEST_CONNECTION_STRING", "DAMENG_TLS_TEST_CONNECTION_STRING" })
                {
                    string raw = Environment.GetEnvironmentVariable(name) ?? throw new ProbeFailure("release_environment_missing");
                    Require(new DmConnectionStringBuilder(raw).User.Equals(User, StringComparison.OrdinalIgnoreCase), "configured_test_identity_required");
                }
                Report["status"] = "release_environment_preflight_only"; Report["server_identity_verified"] = false;
            }
            else if (Mode == "offline")
            {
                await FirstInitializationMatrixAsync();
                Stage = "public_safe_formatter";
                string safe = DmDiagnostics.FormatException(new UntrustedMarkerException("SYNTHETIC_SQL_PASSWORD_SECRET"));
                Require(!safe.Contains("SYNTHETIC_", StringComparison.Ordinal) && !safe.Contains(nameof(UntrustedMarkerException), StringComparison.Ordinal), "formatter_marker_leak");
                Report["status"] = "offline_verified"; Report["integration"] = "integration_pending";
            }
            else
            {
                Stage = "configure_tls"; ConfigureTls(); await RunSoakAsync();
                Stage = "healthy_final_operation_diagnostics"; VerifyHealthyFinalOperationDiagnostics();
                Report["status"] = "tls_resource_scope_verified"; Report["integration"] = "TLS_only_upstream_pending";
            }
            Report["accepted"] = true; code = 0;
        }
        catch (Exception error)
        {
            code = error is ProbeFailure { Kind: "release_environment_missing" } ? 66 : error is ProbeFailure { Kind: "integration_pending" } ? 3 : 1;
            Report["status"] = "rejected"; Report["failed_stage"] = Report.TryGetValue("primary_failed_stage", out object? primary) ? primary : Stage;
            Report["error"] = new { classification = error is ProbeFailure p ? p.Kind : "operation_failed", safe_failure = DmDiagnostics.FormatException(error) };
        }
        Report["exit_code"] = code;
        try { Checkpoint(); Console.WriteLine(JsonSerializer.Serialize(Report)); }
        catch { Console.WriteLine("{\"task\":\"T18\",\"status\":\"rejected\",\"exit_code\":1}"); return 1; }
        return code;
    }
    private static void VerifyPackage(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var manifest = document.RootElement;
        Assembly assembly = typeof(DmConnection).Assembly;
        string expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "R3PackageVersion").Value ?? "";
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Require(expected == actual && expected == manifest.GetProperty("version").GetString() && hash == manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString(), "exact_package_identity_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "loaded_package_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3ResourceProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + expected).GetProperty("type").GetString() == "package", "exact_package_reference_required");
        Report["package_version"] = expected; Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = hash; Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
        Report["reference_kind"] = "exact_PackageReference";
    }
    private static async Task FirstInitializationChildAsync(string point)
    {
        Require(point is "normal" or "should_listen" or "instrument_published" or "sample" or "started" or "stopped" or "measurement", "unknown_first_init_point");
        Stage = "first_init_public_commit"; var before = DmDiagnosticsCore.Snapshot;
        Require(before.InitializationState == 0 && before.WorkerStarts == 0, "first_init_process_not_fresh");
        var context = new AsyncLocal<string?> { Value = "SYNTHETIC_ASYNCLOCAL_SECRET" };
        using var listener = new DiagnosticListenerCapture(point, () => context.Value);
        await using (var fixture = new ScriptedTransactionFixture())
        {
            await using var transaction = await fixture.BeginAsync(); await transaction.CommitAsync();
            Require(transaction.Outcome == DmTransactionOutcome.Committed && fixture.Channel.CommitSends == 1 && fixture.Channel.SyncCalls == 0,
                "first_init_fault_changed_public_commit");
            await fixture.Connection.CloseAsync(); Require(fixture.Quiescent, "first_init_fault_leaked_permit");
        }
        Require(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(10)), "first_init_consumer_not_drained");
        var final = DmDiagnosticsCore.Snapshot;
        Require(final.WorkerStarts == 1 && final.QueueDepth == 0 && final.QueuePeak <= 4096 && !listener.ContextLeaked && !listener.InvalidTag,
            "first_init_context_or_resource_failure");
        Require(point == "normal" ? final.InitializationState == 1 && final.CallbackFailures == 0 : final.CallbackFailures > 0, "first_init_fault_not_observed");
        Report["status"] = "first_init_case_verified"; Report["case"] = new { category = "Contract", feature = "Diagnostics", point,
            actual_public_begin_commit = true, transaction_outcome = "Committed", commit_frames = 1, scripted_fixed_ack = true,
            real_database_claimed = false, caller_context_leaked = false, final_queue_depth = final.QueueDepth,
            worker_starts = final.WorkerStarts, callback_failures = final.CallbackFailures, initialization_state = final.InitializationState };
    }
    private static async Task FirstInitializationMatrixAsync()
    {
        Stage = "first_init_isolated_process_matrix"; var cases = new List<object>();
        foreach (string point in new[] { "normal", "should_listen", "instrument_published", "sample", "started", "stopped", "measurement" })
        {
            string childOutput = Output + "." + point + ".json";
            var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("first-init:" + point);
            start.ArgumentList.Add(Repo); start.ArgumentList.Add(PackageManifest); start.ArgumentList.Add(childOutput);
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("DAMENG", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            using var process = Process.Start(start) ?? throw new ProbeFailure("first_init_child_not_started");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(limit.Token); }
            catch { process.Kill(true); throw; }
            string emitted = await stdout, errors = await stderr;
            Require(emitted.Length < 1024 * 1024 && errors.Length < 1024 * 1024 && !emitted.Contains("SYNTHETIC_", StringComparison.Ordinal) &&
                !errors.Contains("SYNTHETIC_", StringComparison.Ordinal), "first_init_child_marker_or_output_leak");
            Require(process.ExitCode == 0 && File.Exists(childOutput), "first_init_child_failed");
            using var document = JsonDocument.Parse(File.ReadAllText(childOutput)); var root = document.RootElement;
            Require(root.GetProperty("accepted").GetBoolean() && root.GetProperty("status").GetString() == "first_init_case_verified", "first_init_child_envelope_failed");
            cases.Add(root.GetProperty("case").Clone());
        }
        Report["first_init_cases"] = cases;
    }
    private static void ConfigureTls()
    {
        string raw = Environment.GetEnvironmentVariable("DAMENG_TLS_TEST_CONNECTION_STRING") ?? throw new ProbeFailure("integration_pending");
        var b = new DmConnectionStringBuilder(raw);
        Require(b.User.Equals(User, StringComparison.OrdinalIgnoreCase) && b.Server == "127.0.0.1" && b.Port == 15236, "isolated_tls_test_identity_required_before_authentication");
        b.Schema = User; b.Pooling = true; b.MaxPoolSize = 4; b.MaxPoolWaiters = 16; b.PoolAcquireTimeout = TimeSpan.FromSeconds(15);
        b.ConnectTimeout = TimeSpan.FromSeconds(15); b.CommandTimeout = 15; b.PersistSecurityInfo = false; b.StmtPooling = false; b.PreparePooling = false; b.LogLevel = LogLevel.OFF;
        b.TransportSecurity = DmTransportSecurity.RequireTls;
        string certs = Path.Combine(Repo, ".local", "t07", "certs", "client_ssl", User);
        b.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem"); b.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
        b.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem"); b.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
        Settings = b.ConnectionString; Report["explicit_transport"] = "RequireTls";
        Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
    }
    private static void VerifyHealthyFinalOperationDiagnostics()
    {
        const string failure = "healthy_operation_diagnostics_failed";
        string[] operations = ["unknown", "connect", "execute", "prepare", "fetch", "commit", "rollback", "reader_close", "metadata", "transaction_begin"];
        string[] results = ["unknown", "success", "server_error", "canceled", "timeout", "outcome_unknown", "transport_error", "rejected"];
        var expected = operations.SelectMany(operation => results.Select(result =>
            "wdm.operation.total|operation=" + operation + ";result=" + result)).ToHashSet(StringComparer.Ordinal);
        Require(Report.TryGetValue("final_public_diagnostics", out object? snapshot) && snapshot != null &&
            Report.TryGetValue("physical_connections_created", out object? physical) && physical is long created && created > 0, failure);
        JsonElement final = JsonSerializer.SerializeToElement(snapshot);
        Require(final.ValueKind == JsonValueKind.Object && final.TryGetProperty("cumulative_counts", out JsonElement cumulative) &&
            cumulative.ValueKind == JsonValueKind.Object, failure);
        Require(final.TryGetProperty("invalid_tag", out JsonElement invalidTag) && invalidTag.ValueKind == JsonValueKind.False &&
            final.TryGetProperty("caller_context_leaked", out JsonElement contextLeaked) && contextLeaked.ValueKind == JsonValueKind.False &&
            CountInRange(final, "finite_tag_combinations", 0, 128) && CountInRange(final, "activities", 1, long.MaxValue) &&
            CountInRange(final, "measurements", 1, long.MaxValue), failure);
        var buckets = new Dictionary<string, long>(StringComparer.Ordinal);
        // Require the complete final finite matrix; missing zero buckets are not evidence of health.
        foreach (JsonProperty property in final.GetProperty("cumulative_counts").EnumerateObject())
        {
            Require(property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out long count) && count >= 0, failure);
            if (!property.Name.StartsWith("wdm.operation.total", StringComparison.Ordinal)) continue;
            Require(expected.Contains(property.Name) && buckets.TryAdd(property.Name, property.Value.GetInt64()), failure);
        }
        Require(buckets.Count == expected.Count && expected.All(buckets.ContainsKey), failure);
        foreach (string operation in operations)
            Require(buckets["wdm.operation.total|operation=" + operation + ";result=transport_error"] == 0, failure);
        Require(buckets["wdm.operation.total|operation=connect;result=success"] == (long)Report["physical_connections_created"]!, failure);
        foreach (string result in results.Where(result => result != "success"))
            Require(buckets["wdm.operation.total|operation=connect;result=" + result] == 0, failure);
        // Pool cancellation and rejection belong to the separate acquire control, not physical connect.
        static bool CountInRange(JsonElement value, string name, long minimum, long maximum) =>
            value.TryGetProperty(name, out JsonElement item) && item.ValueKind == JsonValueKind.Number &&
            item.TryGetInt64(out long count) && minimum <= count && count <= maximum;
    }
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
    private sealed class UntrustedMarkerException(string message) : Exception(message);
}
