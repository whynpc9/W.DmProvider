using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;
using W.Dm.Config;

internal static class Program
{
    private const string Version = "0.1.0-r3.t16.20261002143244", User = "WDM_PROVIDER_TEST";
    private const string PackageSha = "a15d5009659e1f78f00a49efe7c1caf0cd43974b69169e405dfc794ef5275a44";
    private const string DllSha = "f70627e5d957f3de86ec33070474ab1543c162c5f6321f93312ad87c4535290f";
    private const string Mvid = "654b6ddd-b0ff-4408-8e1b-b93bd075c1da";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T17-upload-profile", ["phase"] = "capability_characterization",
        ["implementation"] = "W-package", ["accepted"] = false, ["exit_code"] = -1,
        ["streaming_api_acceptance_claimed"] = false, ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier
    };
    private static string Mode = "", Settings = "", Table = "", StageValue = "arguments";
    private static string? Output;
    private static string Text = "";
    private static byte[] Bytes = [];
    private static Observe Hooks = null!;
    private static string Stage { get => StageValue; set { StageValue = value; Report["checkpoint_stage"] = value; Checkpoint(); } }
    private static void Checkpoint()
    { if (Output != null) { File.WriteAllText(Output + ".tmp", JsonSerializer.Serialize(Report)); File.Move(Output + ".tmp", Output, true); } }
    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        try
        {
            Require(args.Length == 4 && args[0] is "offline" or "shared" or "tls", "usage");
            Mode = args[0]; Output = args[3]; Report["mode"] = Mode;
            Stage = "accepted_package_identity"; VerifyPackage(args[2]);
            byte[] fixture = File.ReadAllBytes(Path.Combine(args[1], "tests", "fixtures", "r3-lob", "vectors.json"));
            using (var document = JsonDocument.Parse(fixture)) Text = document.RootElement.GetProperty("out_row_profile_large").GetProperty("value").GetString()!;
            Require(Encoding.UTF8.GetByteCount(Text) >= 128 * 1024, "large_string_control_required");
            Bytes = new byte[128 * 1024 + 17]; for (int i = 0; i < Bytes.Length; i++) Bytes[i] = (byte)((17 + 131 * i) & 255);
            Report["fixture_sha256"] = Hash(fixture);
            Report["input_manifest"] = new { blob_bytes = Bytes.Length, blob_seed = 17, blob_stride = 131, blob_sha256 = Hash(Bytes),
                clob_utf16_chars = Text.Length, clob_utf8_bytes = Encoding.UTF8.GetByteCount(Text), clob_utf8_sha256 = Hash(Encoding.UTF8.GetBytes(Text)),
                parameter_inputs = "existing_byte_array_and_string", command_seconds = 30, connect_seconds = 15, process_seconds = 90 };
            if (Mode == "offline")
            { Report["status"] = "offline_verified"; Report["integration"] = "integration_pending"; }
            else
            {
                Configure(Path.GetFullPath(args[1]));
                using (Hooks = new Observe()) await RunAsync();
                Report["status"] = "upload_ack_characterized"; Report["integration"] = "real_test_schema";
            }
            code = 0; Report["accepted"] = true;
        }
        catch (Exception error)
        {
            bool pending = error is ProbeFailure { Kind: "integration_pending" };
            Report["status"] = pending ? "integration_pending" : "rejected"; Report["integration"] = pending ? "integration_pending" : "not_accepted";
            Report["failed_stage"] = Report.TryGetValue("primary_failed_stage", out object? prior) ? prior : Stage;
            Report["error"] = SafeError(error); code = pending ? 3 : 1;
        }
        Report["exit_code"] = code;
        try { Checkpoint(); Console.WriteLine(JsonSerializer.Serialize(Report)); }
        catch { Console.WriteLine("{\"task\":\"T17-upload-profile\",\"status\":\"rejected\",\"exit_code\":1}"); return 1; }
        return code;
    }
    private static void VerifyPackage(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var manifest = document.RootElement; var assembly = typeof(DmConnection).Assembly;
        string requested = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "R3PackageVersion").Value ?? "";
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        Require(requested == Version && actual == Version && manifest.GetProperty("version").GetString() == Version &&
            manifest.GetProperty("package_sha256").GetString() == PackageSha && Hash(File.ReadAllBytes(assembly.Location)) == DllSha &&
            manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString() == DllSha &&
            assembly.ManifestModule.ModuleVersionId.ToString("D") == Mvid &&
            Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "accepted_package_identity_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3UploadProfileProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + Version).GetProperty("type").GetString() == "package", "package_reference_required");
        Report["package_version"] = Version; Report["package_sha256"] = PackageSha; Report["loaded_assembly_sha256"] = DllSha;
        Report["loaded_assembly_mvid"] = Mvid; Report["reference_kind"] = "exact_PackageReference";
    }
    private static void Configure(string repo)
    {
        string raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
        var b = new DmConnectionStringBuilder(raw); Require(string.Equals(b.User, User, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        b.Schema = User; b.Pooling = false; b.StmtPooling = false; b.PreparePooling = false; b.LogLevel = LogLevel.OFF;
        b.PersistSecurityInfo = false; b.ConnectTimeout = TimeSpan.FromSeconds(15); b.CommandTimeout = 30;
        b.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        if (Mode == "tls")
        {
            Require(b.Server == "127.0.0.1" && b.Port == 15236, "isolated_tls_target_required");
            string certificates = Path.Combine(repo, ".local", "t07", "certs", "client_ssl", User);
            b.TlsCaCertificatePath = Path.Combine(certificates, "ca-cert.pem"); b.TlsClientCertificatePath = Path.Combine(certificates, "client-cert.pem");
            b.TlsClientPrivateKeyPath = Path.Combine(certificates, "client-key.pem"); b.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Settings = b.ConnectionString; Report["explicit_transport"] = b.TransportSecurity.ToString();
    }
    private static async Task RunAsync()
    {
        Table = "T17U_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(); Report["owned_objects"] = new[] { Table };
        bool attempted = false; Exception? primary = null, cleanup = null;
        try
        {
            Stage = "create_unique_object";
            await using var connection = await OpenAsync(); Require(await ObjectCountAsync(connection) == 0, "unique_object_already_exists"); attempted = true;
            await ExecuteAsync(connection, $"CREATE TABLE {Table}(ID INT PRIMARY KEY,B BLOB,C CLOB)");
            Stage = "existing_array_upload";
            await using (var insert = new DmCommand($"INSERT INTO {Table}(ID,B) VALUES(1,:p0)", connection) { CommandTimeout = 30 })
            { insert.Parameters.Add(new DmParameter("p0", DmDbType.Blob) { Value = Bytes }); Require(await insert.ExecuteNonQueryAsync() == 1, "array_upload_failed"); }
            Stage = "existing_string_upload";
            await using (var insert = new DmCommand($"UPDATE {Table} SET C=:p0 WHERE ID=1", connection) { CommandTimeout = 30 })
            { insert.Parameters.Add(new DmParameter("p0", DmDbType.Clob) { Value = Text }); Require(await insert.ExecuteNonQueryAsync() == 1, "string_upload_failed"); }
            await using var control = new DmCommand($"SELECT COUNT(*) FROM {Table} WHERE ID=1", connection) { CommandTimeout = 30 };
            Require(Convert.ToInt32(await control.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1, "committed_row_control_failed");
        }
        catch (Exception error) { primary = error; Report["work_error"] = SafeError(error); Report["primary_failed_stage"] = Stage; }
        finally
        {
            Stage = "fresh_exact_cleanup";
            try
            {
                await using (var cleaner = await OpenAsync()) if (attempted && await ObjectCountAsync(cleaner) == 1) await ExecuteAsync(cleaner, "DROP TABLE " + Table);
                await using (var fresh = await OpenAsync()) Require(await ObjectCountAsync(fresh) == 0, "unique_object_absence_failed");
                Report["cleanup_verified"] = true; Report["final_database_state"] = "unique_owned_objects_absent";
                Report["final_verification_identity"] = User; Report["final_verification_schema"] = User;
            }
            catch (Exception error) { cleanup = error; Report["cleanup_error"] = SafeError(error); }
        }
        Report["opcode26_sent_count"] = Hooks.Sent; Report["opcode26_ack_header_count"] = Hooks.Headers.Count;
        Report["opcode26_before_decode_count"] = Hooks.Decodes; Report["ack_headers"] = Hooks.Headers;
        Report["response_opcodes_observed"] = Hooks.Headers.Select(h => h.ResponseOpcode).Distinct().Order().ToArray();
        Report["body_lengths_observed"] = Hooks.Headers.Select(h => h.BodyLength).Distinct().Order().ToArray();
        Report["positive_statuses_observed"] = Hooks.Headers.All(h => h.SqlStatus >= 0);
        Report["physical_connections_created"] = Hooks.Created; Report["physical_connections_disposed"] = Hooks.Disposed;
        Report["negotiated_encrypt_modes"] = Hooks.Modes; Report["network_io_counts"] = Hooks.Counts;
        if (primary != null) throw primary; if (cleanup != null) throw cleanup;
        Require(Hooks.Sent > 0 && Hooks.Sent == Hooks.Headers.Count && Hooks.Sent == Hooks.Decodes, "opcode26_ack_coverage_not_proven");
        Require(Hooks.Headers.All(h => h.SqlStatus >= 0), "positive_ack_status_not_proven");
        Require(Hooks.Created == Hooks.Disposed && Hooks.Created == Hooks.Modes.Count && Hooks.AsyncOnly(Mode == "tls"), "physical_or_async_accounting_failed");
        // These are observed numeric headers. No response opcode or token/body length is guessed or enforced.
        Report["case"] = new { category = "state_evidence", status = "characterized", request_opcode = 26,
            one_ack_header_per_sent_chunk = true, before_decode_coverage = true, opaque_body_read = false,
            response_opcode_policy = "observed_not_assumed", streaming_input_claimed = false };
    }
    private static async Task<DmConnection> OpenAsync()
    {
        var c = new DmConnection(Settings);
        try
        {
            await c.OpenAsync(); await using var command = new DmCommand("SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL", c) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync() && reader.GetString(0) == User && reader.GetString(1) == User, "test_identity_or_schema_mismatch");
            Require(Regex.IsMatch(c.ServerVersion, @"^\d+(\.\d+){1,5}$"), "numeric_server_version_required");
            Report["server_identity"] = User; Report["server_schema"] = User; Report["server_version"] = c.ServerVersion; return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }
    private static async Task ExecuteAsync(DmConnection c, string sql)
    { await using var command = new DmCommand(sql, c) { CommandTimeout = 30 }; await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ObjectCountAsync(DmConnection c)
    {
        await using var command = new DmCommand("SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0", c) { CommandTimeout = 30 };
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = Table });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private static object SafeError(Exception error) => new { type = error.GetType().FullName,
        classification = error is ProbeFailure p ? p.Kind : error is DmException ? "provider_error" : "unclassified_failure",
        failure_kind = (error as DmException)?.FailureInfo?.ErrorKind.ToString(), operation_outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString(),
        number = error is DmException dm ? (int?)dm.Number : null };
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
    private sealed record Header(short RequestOpcode, short ResponseOpcode, int SqlStatus, int BodyLength);
    private sealed class Observe : IDisposable
    {
        private readonly Type transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!;
        private readonly Type trace = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!;
        private readonly FieldInfo sentHook, headerHook, decodeHook, modeHook, ioHook;
        private readonly long initialCreated, initialDisposed;
        private readonly Dictionary<string, long> counts = new();
        internal readonly List<Header> Headers = [];
        internal readonly List<int> Modes = [];
        internal long Sent, Decodes;
        private bool unknownIo;
        internal Observe()
        {
            Type wire = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            sentHook = wire.GetField("AfterFrameSent", flags)!; headerHook = trace.GetField("AfterFrame", flags)!;
            decodeHook = wire.GetField("BeforeDecode", flags)!; modeHook = wire.GetField("AfterStartupNegotiatedEncryptMode", flags)!;
            ioHook = transport.GetField("BeforeNetworkIo", flags)!;
            foreach (var field in new[] { sentHook, headerHook, decodeHook, modeHook, ioHook }) Require(field != null && field.GetValue(null) == null, "isolated_hooks_required");
            foreach (string operation in new[] { "connect", "tls", "send", "receive" })
            { counts["async_" + operation] = 0; counts["sync_" + operation] = 0; }
            initialCreated = Counter("CreatedTcpSockets"); initialDisposed = Counter("DisposedTcpSockets");
            sentHook.SetValue(null, Bridge(sentHook, values => { if ((short)values[1] == 26) Sent++; }));
            headerHook.SetValue(null, (Action<short, short, int, int>)((request, response, status, length) =>
            { if (request == 26) { Require(Headers.Count < 1024, "ack_header_count_bound"); Headers.Add(new Header(request, response, status, length)); } }));
            decodeHook.SetValue(null, Bridge(decodeHook, _ =>
            { if ((short)trace.GetProperty("CurrentRequestOpcode", flags)!.GetValue(null)! == 26) Decodes++; }));
            modeHook.SetValue(null, (Action<int>)(mode => Modes.Add(mode)));
            ioHook.SetValue(null, (Action<bool, string>)((asynchronous, operation) =>
            { string key = (asynchronous ? "async_" : "sync_") + operation; if (counts.ContainsKey(key)) counts[key]++; else unknownIo = true; }));
        }
        private static Delegate Bridge(FieldInfo field, Action<object[]> action)
        {
            var parameters = field.FieldType.GenericTypeArguments.Select(t => Expression.Parameter(t)).ToArray();
            return Expression.Lambda(field.FieldType, Expression.Invoke(Expression.Constant(action),
                Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))))), parameters).Compile();
        }
        private long Counter(string name) => (long)transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        internal long Created => Counter("CreatedTcpSockets") - initialCreated;
        internal long Disposed => Counter("DisposedTcpSockets") - initialDisposed;
        internal Dictionary<string, long> Counts => new(counts);
        internal bool AsyncOnly(bool tls) => !unknownIo && counts.Where(p => p.Key.StartsWith("sync_", StringComparison.Ordinal)).All(p => p.Value == 0) &&
            counts["async_connect"] > 0 && counts["async_send"] > 0 && counts["async_receive"] > 0 &&
            (tls ? counts["async_tls"] > 0 : counts["async_tls"] == 0) && Modes.All(m => m == (Mode == "tls" ? 1 : 0));
        public void Dispose() { foreach (var field in new[] { sentHook, headerHook, decodeHook, modeHook, ioHook }) field.SetValue(null, null); }
    }
}
