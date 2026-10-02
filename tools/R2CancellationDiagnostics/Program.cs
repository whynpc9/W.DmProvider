using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;
using W.Dm.Config;

internal static partial class Program
{
    private const string User = "WDM_PROVIDER_TEST", RecoveredTable = "T14_921060586F914E658A7FD736";
    private const string OldHash = "3eaa6a45872304d07489e713d3e913b92547367f78e2dd86327de6407a50e36c";
    private const string NewHash = "fdea777db6150aeac6ecbe8c69a17329d3089b1364ee5c7f9b2cf4637e52f431";
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<string, object?> Report = new() { ["schema_version"] = 1, ["task"] = "T14-diagnostics",
        ["accepted"] = false, ["read_only"] = true, ["objects_created"] = 0, ["ddl_performed"] = false,
        ["connect_timeout_seconds"] = 20, ["command_timeout_seconds"] = 15, ["recovered_table"] = RecoveredTable };
    private static readonly List<Dictionary<string, object?>> Cases = [];
    private static string Settings = "", Output = "";
    private static Hooks? Trace;

    public static async Task<int> Main(string[] args)
    {
        int code = 1; string? output = args.Length == 6 ? Path.GetFullPath(args[4]) : null;
        try
        {
            Need(args.Length == 6 && args[0] is "health" or "readers" && args[1] is "old-before" or "new" or "old-after", "usage");
            Output = output!;
            string mode = args[0], lane = args[1]; Report["mode"] = mode; Report["lane"] = lane;
            Package(args[3], lane);
            if (mode == "readers") Gate(args[2], args[5]);
            Configure();
            Report["numeric_journal"] = Path.GetFileName(Output) + ".frames.jsonl";
            using (Trace = new Hooks(Output + ".frames.jsonl"))
            {
                Report["sent_opcode_hook_supported"] = Trace.SentSupported; Report["cases"] = Cases;
                string[] names = mode == "health" ? ["unit_literal", "unit_int32_parameter", "catalog_literal", "catalog_varchar_parameter",
                    "scalar_unit_literal", "scalar_catalog_literal", "scalar_catalog_varchar_parameter"] :
                    ["reader_baseline", "reader_old_execute_token_canceled"];
                foreach (string name in names)
                    if (!await CaseAsync(name, mode == "readers").ConfigureAwait(false)) throw new DiagnosticFailure("lane_stopped_after_case_failure");
            }
            Report["status"] = mode == "health" ? "diagnostic_health_verified" : "diagnostic_readers_verified";
            Report["accepted"] = true; code = 0;
        }
        catch (Exception error) { Report["status"] = "rejected"; Report["error"] = Safe(error); }
        if (Trace != null)
        {
            try { await Trace.FlushAsync().ConfigureAwait(false); }
            catch (Exception error) { Report["status"] = "rejected"; Report["accepted"] = false; Report["journal_error"] = Safe(error); code = 1; }
        }
        Report["exit_code"] = code;
        string json = JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });
        try { if (output != null) await File.WriteAllTextAsync(output, json).ConfigureAwait(false); }
        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", error = Safe(error) })); return 1; }
        Console.WriteLine(json); return code;
    }

    private static void Package(string manifestPath, string lane)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath)); var manifest = document.RootElement;
        var assembly = typeof(DmConnection).Assembly;
        string version = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "DiagnosticPackageVersion").Value!;
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Need(hash == (lane == "new" ? NewHash : OldHash) && manifest.GetProperty("version").GetString() == version &&
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] == version &&
            manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString() == hash, "exact_lane_package_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".deps.json")));
        Need(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + version).GetProperty("type").GetString() == "package", "exact_package_reference_required");
        Report["package_version"] = version; Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = hash; Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString();
        Report["reference_kind"] = "exact_PackageReference";
    }

    private static void Configure()
    {
        string? raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING"); Need(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var b = new DmConnectionStringBuilder(raw!); Need(b.User.Equals(User, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_auth");
        b.TransportSecurity = DmTransportSecurity.PlaintextAllowed; b.Schema = User; b.PersistSecurityInfo = false;
        b.ConnPooling = false; b.StmtPooling = false; b.PreparePooling = false; b.LogLevel = LogLevel.OFF;
        b.ConnectTimeout = TimeSpan.FromSeconds(20); b.CommandTimeout = 15; Settings = b.ConnectionString;
        Report["explicit_transport"] = "PlaintextAllowed";
    }

    private static async Task<bool> CaseAsync(string name, bool readerCase)
    {
        var entry = new Dictionary<string, object?> { ["case"] = name, ["status"] = "started", ["substage"] = "Open",
            ["rows_read"] = 0, ["rows_validated"] = 0, ["execute_returned"] = false };
        Cases.Add(entry); long start = Stopwatch.GetTimestamp(); int frames = Trace!.Frames.Count, sent = Trace.Sent.Count, modes = Trace.Modes.Count;
        entry["started_timestamp"] = start; entry["frame_baseline_count"] = frames; entry["sent_baseline_count"] = sent;
        entry["invocation_baseline_count"] = Trace.Invocations.Count;
        var c = new DmConnection(Settings); Exception? failure = null;
        try
        {
            await StepAsync(entry, "Open").ConfigureAwait(false);
            await c.OpenAsync().WaitAsync(Safety).ConfigureAwait(false);
            int[] negotiated = Trace.Modes.Skip(modes).ToArray(); Need(negotiated.SequenceEqual(new[] { 0 }), "mode_zero_required");
            entry["negotiated_encrypt_mode"] = 0;
            await QueryAsync(c, entry, "Identity", "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL", null, false, identity: true).ConfigureAwait(false);
            entry["identity_verified"] = true; entry["schema_verified"] = true;
            if (readerCase)
            {
                entry["api"] = "ExecuteReaderAsync_new_Read_token";
                await QueryAsync(c, entry, "Query", "SELECT CAST(LEVEL AS INT) AS ID,RPAD('x',1024,'x') AS PAYLOAD FROM DUAL CONNECT BY LEVEL<=2048", null,
                    name == "reader_old_execute_token_canceled", large: true).ConfigureAwait(false);
            }
            else
            {
                string sql = name switch { "unit_literal" or "scalar_unit_literal" => "SELECT 1 FROM DUAL", "unit_int32_parameter" => "SELECT :value FROM DUAL",
                    "catalog_literal" or "scalar_catalog_literal" => "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME='" + RecoveredTable + "'",
                    _ => "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:name" };
                DmParameter? parameter = name switch {
                    "unit_int32_parameter" => new DmParameter("value", DmDbType.Int32) { Value = 1, Direction = ParameterDirection.Input },
                    "catalog_varchar_parameter" or "scalar_catalog_varchar_parameter" => new DmParameter("name", DmDbType.VarChar) { Value = RecoveredTable, Direction = ParameterDirection.Input, Size = RecoveredTable.Length },
                    _ => null };
                bool scalar = name.StartsWith("scalar_", StringComparison.Ordinal);
                entry["api"] = scalar ? "ExecuteScalarAsync" : "ExecuteReaderAsync_first_row_then_Close";
                if (scalar) entry["implicit_reader_progress_observable"] = false;
                if (scalar) await ScalarAsync(c, entry, sql, parameter).ConfigureAwait(false);
                else await QueryAsync(c, entry, "Query", sql, parameter, false).ConfigureAwait(false);
                Need((int)entry["observed_integer"]! == (name.Contains("unit_", StringComparison.Ordinal) ? 1 : 0), "read_only_result_mismatch");
            }
        }
        catch (Exception error) { failure = error; RecordFailure(entry, error); }
        finally
        {
            await StepAsync(entry, "Connection.Close").ConfigureAwait(false);
            try { await c.DisposeAsync().AsTask().WaitAsync(Safety).ConfigureAwait(false); }
            catch (Exception error) { entry["connection_close_error"] = Safe(error); if (failure == null) { failure = error; RecordFailure(entry, error); } }
            entry["elapsed_milliseconds"] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            entry["numeric_frames"] = Trace.Frames.Skip(frames).ToArray(); entry["numeric_sent_opcodes"] = Trace.Sent.Skip(sent).ToArray();
            entry["invocation_samples"] = Trace.Invocations.Skip((int)entry["invocation_baseline_count"]!).ToArray();
            entry["status"] = failure == null ? "pass" : "rejected";
            await ProgressAsync().ConfigureAwait(false);
        }
        return failure == null;
    }

    private static async Task ScalarAsync(DmConnection c, Dictionary<string, object?> entry, string sql, DmParameter? parameter)
    {
        var q = new DmCommand(sql, c) { CommandTimeout = 15 }; if (parameter != null) q.Parameters.Add(parameter);
        Exception? failure = null;
        try
        {
            await StepAsync(entry, "Scalar.Execute").ConfigureAwait(false);
            object? value = await q.ExecuteScalarAsync().WaitAsync(Safety).ConfigureAwait(false);
            Need(value != null && value != DBNull.Value, "scalar_null_result");
            entry["execute_returned"] = true; entry["observed_integer"] = Convert.ToInt32(value);
        }
        catch (Exception error) { failure = error; RecordFailure(entry, error); }
        finally
        {
            await StepAsync(entry, "Scalar.Command.Close").ConfigureAwait(false);
            try { await q.DisposeAsync().AsTask().WaitAsync(Safety).ConfigureAwait(false); }
            catch (Exception error) { entry["scalar_command_close_error"] = Safe(error); if (failure == null) { failure = error; RecordFailure(entry, error); } }
        }
        if (failure != null) throw failure;
    }

    private static async Task QueryAsync(DmConnection c, Dictionary<string, object?> entry, string prefix, string sql,
        DmParameter? parameter, bool cancelOld, bool identity = false, bool large = false)
    {
        var q = new DmCommand(sql, c) { CommandTimeout = 15 }; if (parameter != null) q.Parameters.Add(parameter);
        System.Data.Common.DbDataReader? reader = null; Exception? failure = null;
        using var executeToken = new CancellationTokenSource(); using var readToken = new CancellationTokenSource();
        int fetchBefore = Trace!.Frames.Count(x => x.RequestOpcode == 7);
        try
        {
            await StepAsync(entry, prefix + ".Execute").ConfigureAwait(false);
            reader = await q.ExecuteReaderAsync(executeToken.Token).WaitAsync(Safety).ConfigureAwait(false);
            if (!identity) { entry["execute_returned"] = true; entry["old_execute_token_canceled"] = false; }
            if (cancelOld) { executeToken.Cancel(); entry["old_execute_token_canceled"] = true; }
            int rows = 0;
            int lastCheckpointFetch = 0;
            while (true)
            {
                bool rowCheckpoint = !large || rows == 0 || rows % 100 == 0 || rows == 2048;
                entry["substage"] = prefix + ".Read";
                if (rowCheckpoint) await StepAsync(entry, prefix + ".Read").ConfigureAwait(false);
                bool hasRow = await reader.ReadAsync(readToken.Token).WaitAsync(Safety).ConfigureAwait(false);
                entry["actual_fetch_frames"] = Trace.Frames.Count(x => x.RequestOpcode == 7) - fetchBefore;
                if (!hasRow) break;
                rows++; if (!identity) entry["rows_read"] = rows;
                bool fetchCheckpoint = (int)entry["actual_fetch_frames"]! != lastCheckpointFetch;
                lastCheckpointFetch = (int)entry["actual_fetch_frames"]!;
                entry["substage"] = prefix + ".Fields";
                if (!large) await StepAsync(entry, prefix + ".Fields").ConfigureAwait(false);
                if (identity) Need(rows == 1 && await reader.GetFieldValueAsync<string>(0, readToken.Token).ConfigureAwait(false) == User &&
                    await reader.GetFieldValueAsync<string>(1, readToken.Token).ConfigureAwait(false) == User, "test_identity_or_schema_mismatch");
                else if (large)
                {
                    Need(await reader.GetFieldValueAsync<int>(0, readToken.Token).ConfigureAwait(false) == rows &&
                        (await reader.GetFieldValueAsync<string>(1, readToken.Token).ConfigureAwait(false)).Length == 1024, "reader_value_mismatch");
                    if (rows == 1) entry["cached_first_read"] = (int)entry["actual_fetch_frames"]! == 0;
                }
                else { Need(rows == 1, "scalar_extra_row"); entry["observed_integer"] = Convert.ToInt32(await reader.GetFieldValueAsync<object>(0, readToken.Token).ConfigureAwait(false)); }
                if (!identity) entry["rows_validated"] = rows;
                if (large && (fetchCheckpoint || rows == 1 || rows % 100 == 0 || rows == 2048))
                    await StepAsync(entry, prefix + ".Fields").ConfigureAwait(false);
                // Scalar obtains the first row then closes/drains its reader.
                // Keep Close separately observable instead of consuming EOF first.
                if (!large) break;
            }
            Need(rows == (large ? 2048 : 1), "result_row_count_mismatch");
            if (large) Need((int)entry["actual_fetch_frames"]! > 0, "actual_fetch_missing");
        }
        catch (Exception error) { failure = error; RecordFailure(entry, error); }
        finally
        {
            await StepAsync(entry, prefix + ".Close").ConfigureAwait(false);
            try { if (reader != null) await reader.DisposeAsync().AsTask().WaitAsync(Safety).ConfigureAwait(false); }
            catch (Exception error) { entry[prefix + "_reader_close_error"] = Safe(error); if (failure == null) { failure = error; RecordFailure(entry, error); } }
            try { await q.DisposeAsync().AsTask().WaitAsync(Safety).ConfigureAwait(false); }
            catch (Exception error) { entry[prefix + "_command_close_error"] = Safe(error); if (failure == null) { failure = error; RecordFailure(entry, error); } }
        }
        if (failure != null) throw failure;
    }

    private static void RecordFailure(Dictionary<string, object?> entry, Exception error)
    {
        if (!entry.ContainsKey("failure_substage"))
        {
            entry["failure_substage"] = entry["substage"]; entry["failure"] = Safe(error);
            entry["rows_before_failure"] = entry["rows_read"]; entry["validated_rows_before_failure"] = entry["rows_validated"];
        }
    }
    private static async Task StepAsync(Dictionary<string, object?> entry, string substage)
    {
        entry["substage"] = substage;
        entry["elapsed_milliseconds"] = Stopwatch.GetElapsedTime((long)entry["started_timestamp"]!).TotalMilliseconds;
        entry["numeric_frames"] = Trace!.Frames.Skip((int)entry["frame_baseline_count"]!).ToArray();
        entry["numeric_sent_opcodes"] = Trace.Sent.Skip((int)entry["sent_baseline_count"]!).ToArray();
        entry["invocation_samples"] = Trace.Invocations.Skip((int)entry["invocation_baseline_count"]!).ToArray();
        short[] sent = Trace.Sent.Skip((int)entry["sent_baseline_count"]!).ToArray();
        Frame[] frames = Trace.Frames.Skip((int)entry["frame_baseline_count"]!).ToArray();
        entry["last_sent_opcode"] = sent.Length == 0 ? (short?)null : sent[^1];
        entry["last_response_request_opcode"] = frames.Length == 0 ? (short?)null : frames[^1].RequestOpcode;
        await ProgressAsync().ConfigureAwait(false);
    }
    private static Task ProgressAsync() => File.WriteAllTextAsync(Output,
        JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true }));
    private static object Safe(Exception error)
    {
        object? info = error.GetType().GetProperty("FailureInfo")?.GetValue(error);
        object? Read(string name) => info?.GetType().GetProperty(name)?.GetValue(info);
        string? EnumValue(string name) => Read(name) is Enum value && Enum.IsDefined(value.GetType(), value) ? value.ToString() : null;
        string? code = Read("ErrorCode") as string;
        return new { type = error.GetType().FullName, classification = error is DiagnosticFailure own ? own.Code : "operation_failed",
            number = error is DmException dm ? (int?)dm.Number : null, failure_info_supported = info != null,
            failure_info = info == null ? null : new { error_kind = EnumValue("ErrorKind"), phase = EnumValue("Phase"),
                error_code = code != null && code.Length <= 80 && code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ? code : "invalid_error_code",
                operation_outcome = EnumValue("OperationOutcome"), transaction_outcome = EnumValue("TransactionOutcome"),
                cancel_source = EnumValue("CancelSource"), connection_reusable = Read("ConnectionReusable") is bool reusable ? (bool?)reusable : null,
                server_error_number = Read("ServerErrorNumber") is int number ? (int?)number : null } };
    }
    private static void Need(bool value, string code) { if (!value) throw new DiagnosticFailure(code); }
    private sealed class DiagnosticFailure(string code) : Exception { internal string Code { get; } = code; }
}
