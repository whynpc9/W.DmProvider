using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;
using W.Dm.Config;

internal static partial class Program
{
    private const string User = "WDM_PROVIDER_TEST";
    private static string Mode = "", Settings = "", Table = "", Stage = "arguments", ReportPath = "";
    private static Hooks? Observation;
    private static readonly Dictionary<string, object?> Cases = new();
    private static readonly List<Dictionary<string, object?>> CaseTimings = [];
    private static long RunStarted;
    private static readonly Dictionary<string, object?> Report = new() { ["schema_version"] = 1, ["task"] = "T14",
        ["implementation"] = "W-package", ["accepted"] = false, ["status"] = "started",
        ["native_cancel_claimed"] = false, ["server_immediate_stop_claimed"] = false };
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        string? output = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
        int code = 1;
        try
        {
            Need(args.Length == 4 && args[0] is "shared" or "tls", "usage");
            ReportPath = output!;
            Mode = args[0]; Report["mode"] = Mode;
            Stage = "package_identity"; Package(args[2]);
            Stage = "configuration"; Configure(Path.GetFullPath(args[1]));
            using (Observation = new Hooks()) await RunAsync().ConfigureAwait(false);
            Report["accepted"] = true; Report["status"] = "package_cancellation_verified"; code = 0;
        }
        catch (Exception error) { Report["status"] = "rejected"; Report["stage"] = Stage; Report["error"] = Safe(error); }
        Report["exit_code"] = code;
        string json = JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });
        try { if (output != null) await File.WriteAllTextAsync(output, json).ConfigureAwait(false); }
        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", error = Safe(error) })); return 1; }
        Console.WriteLine(json); return code;
    }

    private static void Package(string manifestPath)
    {
        var assembly = typeof(DmConnection).Assembly;
        var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement;
        string version = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "R2PackageVersion").Value!;
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Need(assembly.GetName().Name == "W.DmProvider" && assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] == version &&
            manifest.GetProperty("version").GetString() == version && manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString() == hash,
            "package_identity_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".deps.json")));
        Need(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + version).GetProperty("type").GetString() == "package", "package_reference_required");
        Report["package_version"] = version; Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = hash; Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString();
        Report["reference_kind"] = "exact_PackageReference";
    }

    private static void Configure(string repo)
    {
        string? raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING");
        Need(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var b = new DmConnectionStringBuilder(raw!);
        Need(b.User.Equals(User, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_auth");
        b.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        b.Schema = User; b.PersistSecurityInfo = false; b.ConnPooling = false; b.StmtPooling = false; b.PreparePooling = false;
        b.LogLevel = LogLevel.OFF; b.ConnectTimeout = TimeSpan.FromSeconds(20); b.CommandTimeout = 15;
        if (Mode == "tls")
        {
            Need(b.Server == "127.0.0.1" && b.Port == 15236, "isolated_tls_target_required");
            string certs = Path.Combine(repo, ".local/t07/certs/client_ssl", User);
            b.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem"); b.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
            b.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem"); b.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Settings = b.ConnectionString; Report["explicit_transport"] = b.TransportSecurity.ToString();
    }

    private static async Task RunAsync()
    {
        RunStarted = Stopwatch.GetTimestamp();
        Table = "T14_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        Report["owned_table"] = Table; Report["cases"] = Cases; Report["final_database_state"] = "unverified";
        Report["case_timings"] = CaseTimings;
        // Preserve the exact owned name before DDL, including if an external
        // process safety stop prevents finally from completing.
        Stage = "fixture_setup";
        await CheckpointAsync("before_setup").ConfigureAwait(false);
        Exception? workError = null;
        try
        {
            await using (var setup = await OpenAsync().ConfigureAwait(false))
            {
                Report["server_identity"] = User; Report["server_schema"] = User; Report["server_version"] = setup.ServerVersion;
                Need(setup.ServerVersion == (Mode == "tls" ? "8.1.4.6" : "8.1.5.60"), "server_profile_unverified");
                await ExecAsync(setup, null, $"CREATE TABLE {Table}(ID INT PRIMARY KEY, VAL INT, PAYLOAD VARCHAR(2048))").ConfigureAwait(false);
                await ExecAsync(setup, null, $"INSERT INTO {Table}(ID,VAL) SELECT LEVEL,0 FROM DUAL CONNECT BY LEVEL<=32").ConfigureAwait(false);
                await ExecAsync(setup, null, $"INSERT INTO {Table}(ID,VAL,PAYLOAD) SELECT LEVEL+1000,0,RPAD('x',1024,'x') FROM DUAL CONNECT BY LEVEL<=2048").ConfigureAwait(false);
            }
            await CheckpointAsync("after_setup").ConfigureAwait(false);
            await TimedCaseAsync("pre_cancel_and_idle_noop", PreCancelAsync).ConfigureAwait(false);
            foreach (string kind in new[] { "user", "command", "deadline" })
                await TimedCaseAsync("rowlock_" + kind, () => LockedAsync(kind)).ConfigureAwait(false);
            await TimedCaseAsync("returned_reader_token_scope", ReaderTokensAsync).ConfigureAwait(false);
            await TimedCaseAsync("idle_reader_cancel_captured_session", IdleCancelAsync).ConfigureAwait(false);
            await TimedCaseAsync("commit_sent_unknown", () => CommitRaceAsync(false)).ConfigureAwait(false);
            await TimedCaseAsync("commit_validated_ack_late_cancel", () => CommitRaceAsync(true)).ConfigureAwait(false);
            if (Mode == "shared")
            {
                await TimedCaseAsync("performance_8", () => PerformanceAsync(8)).ConfigureAwait(false);
                await TimedCaseAsync("performance_16", () => PerformanceAsync(16)).ConfigureAwait(false);
                var eight = (PerformanceResult)Cases["performance_8"]!; var sixteen = (PerformanceResult)Cases["performance_16"]!;
                Need(sixteen.BusyWorkerGrowth - eight.BusyWorkerGrowth < 8, "linear_worker_occupancy_observed");
            }
            Report["performance_scope"] = Mode == "shared" ? "measured_8_and_16_independent_connections" : "shared_profile_only";
            Need(!Observation!.Sent.Any(x => x.Opcode == 11), "unverified_native_cancel_observed");
            Report["native_cancel_frames"] = 0;
            Stage = "fresh_final_rows";
            await CheckpointAsync("before_final_readback").ConfigureAwait(false);
            await using var final = await OpenAsync().ConfigureAwait(false);
            Need(await ScalarIntAsync(final, null, $"SELECT COUNT(*) FROM {Table} WHERE VAL<>0").ConfigureAwait(false) == 0, "unexpected_final_values");
            Report["fresh_nonzero_rows_before_cleanup"] = 0;
            await CheckpointAsync("after_final_readback").ConfigureAwait(false);
        }
        catch (Exception error) { workError = error; Report["work_error"] = Safe(error); Report["work_failure_stage"] = Stage; }
        finally
        {
            Stage = "owned_object_cleanup";
            await CheckpointAsync("before_cleanup").ConfigureAwait(false);
            await using (var clean = await OpenAsync().ConfigureAwait(false))
                    if (await TableCountAsync(clean).ConfigureAwait(false) == 1) await ExecAsync(clean, null, $"DROP TABLE {Table}").ConfigureAwait(false);
            await CheckpointAsync("after_exact_cleanup_before_absence").ConfigureAwait(false);
            await using var verify = await OpenAsync().ConfigureAwait(false);
            Need(await TableCountAsync(verify).ConfigureAwait(false) == 0, "cleanup_absence_unverified");
            Report["cleanup_verified"] = true; Report["final_database_state"] = "random_object_absent";
            Report["final_verification_identity"] = User; Report["final_verification_schema"] = User;
            await CheckpointAsync("after_fresh_absence").ConfigureAwait(false);
        }
        if (workError != null) { Stage = (string)Report["work_failure_stage"]!; throw workError; }
    }

    private static async Task TimedCaseAsync(string name, Func<Task> action)
    {
        Stage = name; long start = Stopwatch.GetTimestamp();
        var timing = new Dictionary<string, object?> { ["case"] = name, ["status"] = "running",
            ["started_utc"] = DateTimeOffset.UtcNow, ["ended_utc"] = null, ["elapsed_milliseconds"] = 0 };
        CaseTimings.Add(timing);
        await CheckpointAsync("case_start").ConfigureAwait(false);
        try { await action().ConfigureAwait(false); timing["status"] = "pass"; }
        catch { timing["status"] = "rejected"; throw; }
        finally
        {
            timing["ended_utc"] = DateTimeOffset.UtcNow;
            timing["elapsed_milliseconds"] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await CheckpointAsync("case_end").ConfigureAwait(false);
        }
    }

    private static Task CheckpointAsync(string phase, object? performance = null)
    {
        // Written by the tool coordinator, never by a protocol hook.
        var checkpoint = new { schema_version = 1, task = "T14", mode = Mode, status = "running", accepted = false,
            owned_table = Table, stage = Stage, phase, completed_case_count = CaseTimings.Count(t => Equals(t["status"], "pass")),
            case_results_count = Cases.Count, expected_case_count = Mode == "shared" ? 10 : 8,
            elapsed_milliseconds = Stopwatch.GetElapsedTime(RunStarted).TotalMilliseconds, case_timings = CaseTimings,
            package_version = Report["package_version"], loaded_assembly_sha256 = Report["loaded_assembly_sha256"],
            final_database_state = Report["final_database_state"], performance };
        return File.WriteAllTextAsync(ReportPath, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<DmConnection> OpenAsync()
    {
        var c = new DmConnection(Settings); int start = Observation!.Modes.Count;
        try
        {
            await c.OpenAsync().WaitAsync(Safety).ConfigureAwait(false);
            Need(Observation.Modes.Count > start && Observation.Modes.Last() == (Mode == "tls" ? 1 : 0), "transport_mode_mismatch");
            await VerifyIdentityAsync(c).ConfigureAwait(false);
            return c;
        }
        catch { await c.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task VerifyIdentityAsync(DmConnection c)
    {
        await using var q = Command(c, null, "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
        await using var r = await q.ExecuteReaderAsync().ConfigureAwait(false);
        Need(await r.ReadAsync().ConfigureAwait(false) && await r.GetFieldValueAsync<string>(0).ConfigureAwait(false) == User &&
            await r.GetFieldValueAsync<string>(1).ConfigureAwait(false) == User, "test_identity_or_schema_mismatch");
    }

    private static DmCommand Command(DmConnection c, DbTransaction? tx, string text)
    { var q = new DmCommand(text, c) { CommandTimeout = 15 }; ((DbCommand)q).Transaction = tx; return q; }
    private static async Task<int> ExecAsync(DmConnection c, DbTransaction? tx, string text)
    { await using var q = Command(c, tx, text); return await q.ExecuteNonQueryAsync().WaitAsync(Safety).ConfigureAwait(false); }
    private static async Task<int> ScalarIntAsync(DmConnection c, DbTransaction? tx, string text)
    { await using var q = Command(c, tx, text); return Convert.ToInt32(await q.ExecuteScalarAsync().WaitAsync(Safety).ConfigureAwait(false)); }
    private static async Task<int> TableCountAsync(DmConnection c)
    { await using var q = Command(c, null, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:n"); q.Parameters.Add(new DmParameter("n", Table)); return Convert.ToInt32(await q.ExecuteScalarAsync().ConfigureAwait(false)); }
    private static async Task<DmTransaction> BeginAsync(DmConnection c) => (DmTransaction)await c.BeginTransactionAsync(IsolationLevel.ReadCommitted).ConfigureAwait(false);
    private static async Task<Exception> ErrorAsync(Func<Task> action)
    { try { await action().WaitAsync(Safety).ConfigureAwait(false); } catch (Exception error) { return error; } throw new ProbeFailure("expected_failure_missing"); }
    private static void Need(bool value, string code) { if (!value) throw new ProbeFailure(code); }
    private sealed class ProbeFailure(string code) : Exception { internal string Code { get; } = code; }
    private static DmFailureInfo? Failure(Exception error) => error is DmOperationCanceledException canceled ? canceled.FailureInfo : (error as DmException)?.FailureInfo;
    private static object Safe(Exception error) => new { type = error.GetType().FullName,
        classification = error is ProbeFailure own ? own.Code : "typed_operation_failure",
        number = error is DmException dm ? (int?)dm.Number : null, failure_info = Failure(error) is { } info ? new {
            error_kind = info.ErrorKind.ToString(), phase = info.Phase.ToString(), error_code = SafeCode(info.ErrorCode),
            operation_outcome = info.OperationOutcome.ToString(), transaction_outcome = info.TransactionOutcome?.ToString(),
            connection_reusable = info.ConnectionReusable, cancel_source = info.CancelSource.ToString(), server_error_number = info.ServerErrorNumber } : null };
    private static string SafeCode(string code) => code.Length <= 80 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "invalid_error_code";
    private static async Task FreshZeroAsync(int id)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            await using var c = await OpenAsync().ConfigureAwait(false);
            if (await ScalarIntAsync(c, null, $"SELECT VAL FROM {Table} WHERE ID={id}").ConfigureAwait(false) == 0) return;
            Need(Stopwatch.GetElapsedTime(start) < Safety, "server_final_value_unverified");
            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}
