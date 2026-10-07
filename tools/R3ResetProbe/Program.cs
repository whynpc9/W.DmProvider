using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;
using W.Dm.Config;

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T15", ["implementation"] = "W-package",
        ["accepted"] = false, ["status"] = "started", ["exit_code"] = -1, ["session_reset_verified"] = false,
        ["reuse_policy"] = "discard", ["handshake_optimization_claimed"] = false,
        ["runtime"] = RuntimeInformation.FrameworkDescription, ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier
    };
    private static readonly Dictionary<string, object?> Ledger = new();
    private static string CurrentStage = "arguments", Mode = "", Settings = "";
    private static string? ReportPath;
    private static string Stage
    {
        get => CurrentStage;
        set
        {
            CurrentStage = value;
            Report["checkpoint_stage"] = value;
            if (ReportPath != null) WriteAtomicReport(ReportPath, JsonSerializer.Serialize(Report));
        }
    }
    private static Observation? Observe;

    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        string? output = args.Length == 4 ? args[3] : null;
        try
        {
            Require(args.Length == 4 && args[0] is "offline" or "shared" or "tls", "usage");
            ReportPath = output;
            Mode = args[0];
            Report["mode"] = Mode;
            Stage = "exact_package_identity";
            VerifyPackage(args[2]);
            Report["public_api_verified"] = new[] { "DmConnection.OpenAsync", "DmConnection.CloseAsync", "DmCommand.PrepareAsync", "DmDataReader.ReadAsync" };
            foreach (var item in new[] { (typeof(DmConnection), "OpenAsync", new[] { typeof(CancellationToken) }),
                (typeof(DmConnection), "CloseAsync", Type.EmptyTypes), (typeof(DmCommand), "PrepareAsync", new[] { typeof(CancellationToken) }),
                (typeof(DmDataReader), "ReadAsync", new[] { typeof(CancellationToken) }) })
            {
                var method = item.Item1.GetMethod(item.Item2, item.Item3);
                Require(method?.DeclaringType == item.Item1 && method.IsVirtual, "provider_async_override_required");
            }
            InitializeLedger();
            Report["capability_ledger"] = Ledger;
            if (Mode == "offline")
            {
                Report["status"] = "offline_verified";
                Report["integration"] = "integration_pending";
            }
            else
            {
                Stage = "configure";
                Configure(Path.GetFullPath(args[1]));
                using (Observe = new Observation()) await RunRealAsync().ConfigureAwait(false);
                Report["status"] = "discard_verified";
                Report["integration"] = "real_test_schema";
            }
            Report["accepted"] = true;
            code = 0;
        }
        catch (Exception error)
        {
            bool pending = error is ProbeFailure { Kind: "integration_pending" };
            Report["status"] = pending ? "integration_pending" : "rejected";
            Report["integration"] = pending ? "integration_pending" : "not_accepted";
            Report["failed_stage"] = Stage;
            Report["error"] = SafeError(error);
            code = pending ? 3 : 1;
        }
        Report["exit_code"] = code;
        string json = JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });
        try { if (output != null) WriteAtomicReport(output, json); }
        catch { Console.WriteLine("{\"status\":\"rejected\",\"classification\":\"report_write_failed\",\"exit_code\":1}"); return 1; }
        Console.WriteLine(json);
        return code;
    }

    private static void WriteAtomicReport(string path, string json)
    {
        string temporary = path + ".checkpoint.tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
    }

    private static void InitializeLedger()
    {
        foreach (string name in new[] { "transaction", "autocommit", "isolation", "schema", "role_authorization",
            "nls_language", "timezone", "temporary_objects", "session_variables", "session_locks", "open_reader",
            "prepared_statement", "package_procedure_state" })
            Ledger[name] = new { status = "not_proven", same_session_reset_proven = false, reason = "no_verified_full_session_reset" };
        Report["reset_source_audit"] = new
        {
            status = "no_verified_session_reset_found", scope = "current_source_and_S09",
            statement_reset = "Internal/Legacy/A/A.Async.cs:ResetAsync -> B.Async.cs:AAsync(A) -> statement_handle",
            full_server_reset_invoked = false, guessed_opcode = false, elevated_procedure_invoked = false
        };
    }

    private static void VerifyPackage(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = doc.RootElement;
        var assembly = typeof(DmConnection).Assembly;
        string expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "R3PackageVersion").Value ?? throw new ProbeFailure("missing_package_version");
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Require(expected == actual && expected == manifest.GetProperty("version").GetString(), "exact_package_version_mismatch");
        Require(sha == manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString(), "loaded_asset_hash_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "loaded_asset_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3ResetProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + expected).GetProperty("type").GetString() == "package", "package_reference_required");
        Require(!deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)), "official_dependency_rejected");
        Report["package_version"] = expected;
        Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = sha;
        Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
        Report["reference_kind"] = "exact_PackageReference";
    }

    private static void Configure(string repo)
    {
        string raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var builder = new DmConnectionStringBuilder(raw);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        builder.Schema = TestUser;
        builder.ConnPooling = false;
        builder.StmtPooling = false;
        builder.PreparePooling = false;
        builder.PersistSecurityInfo = false;
        builder.LogLevel = LogLevel.OFF;
        builder.ConnectTimeout = TimeSpan.FromSeconds(15);
        builder.CommandTimeout = 15;
        builder.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        if (Mode == "tls")
        {
            Require(builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_tls_target_required");
            string certs = Path.Combine(repo, ".local", "t07", "certs", "client_ssl", TestUser);
            builder.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem");
            builder.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
            builder.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem");
            builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Settings = builder.ConnectionString;
        Report["explicit_transport"] = builder.TransportSecurity.ToString();
        Report["pooling_enabled"] = false;
        Report["persist_security_info"] = false;
    }

    private static async Task RunRealAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        string table = "T15_T_" + suffix, temporary = "T15_G_" + suffix;
        Report["owned_objects"] = new[] { table, temporary };
        Report["final_database_state"] = "unverified";
        bool tableAttempted = false, tempAttempted = false, tempCreated = false;
        Exception? workFailure = null, cleanupFailure = null;
        try
        {
            Stage = "create_owned_objects";
            await using (var setup = await OpenVerifiedAsync().ConfigureAwait(false))
            {
                Require(await ObjectCountAsync(setup, table).ConfigureAwait(false) == 0 &&
                    await ObjectCountAsync(setup, temporary).ConfigureAwait(false) == 0, "unique_object_names_already_exist");
                tableAttempted = true;
                await ExecAsync(setup, null, $"CREATE TABLE {table} (ID INT PRIMARY KEY)").ConfigureAwait(false);
                // This is an own-schema capability attempt, never a privileged reset.
                // SQL rejection remains not_proven rather than an invented permission result.
                try
                {
                    tempAttempted = true;
                    await ExecAsync(setup, null, $"CREATE GLOBAL TEMPORARY TABLE {temporary} (ID INT) ON COMMIT PRESERVE ROWS").ConfigureAwait(false);
                    tempCreated = true;
                }
                catch (DmException error) when (IsRecoverableServerRejection(error, setup))
                {
                    Ledger["temporary_objects"] = new { status = "not_proven", same_session_reset_proven = false,
                        reason = "own_schema_capability_attempt_rejected", error = SafeError(error) };
                }
            }
            Stage = "autocommit_fresh_visibility";
            await using (var writer = await OpenVerifiedAsync().ConfigureAwait(false))
                await ExecAsync(writer, null, $"INSERT INTO {table}(ID) VALUES (1)").ConfigureAwait(false);
            await using (var fresh = await OpenVerifiedAsync().ConfigureAwait(false))
                Require(await CountAsync(fresh, table, 1).ConfigureAwait(false) == 1, "autocommit_fresh_visibility_failed");
            Ledger["autocommit"] = new { status = "fresh_baseline_observed", same_session_reset_proven = false, committed_rows = 1 };

            Stage = "physical_discard_active_transaction";
            await using var old = await OpenVerifiedAsync().ConfigureAwait(false);
            await using DbTransaction tx = await old.BeginTransactionAsync(IsolationLevel.ReadCommitted).ConfigureAwait(false);
            Require(tx.IsolationLevel == IsolationLevel.ReadCommitted, "requested_isolation_not_observed");
            await ExecAsync(old, tx, $"INSERT INTO {table}(ID) VALUES (2)").ConfigureAwait(false);
            Require(await CountAsync(old, table, 2, tx).ConfigureAwait(false) == 1, "old_transaction_write_missing");
            await using (var fresh = await OpenVerifiedAsync().ConfigureAwait(false))
                Require(await CountAsync(fresh, table, 2).ConfigureAwait(false) == 0, "uncommitted_write_visible_before_discard");
            long closedBefore = Observe!.Counter("DisposedTcpSockets");
            await old.CloseAsync().ConfigureAwait(false);
            Require(old.State == ConnectionState.Closed && Observe.Counter("DisposedTcpSockets") - closedBefore == 1, "physical_socket_discard_not_observed");
            await using (var fresh = await OpenVerifiedAsync().ConfigureAwait(false))
            {
                Require(await CountAsync(fresh, table, 2).ConfigureAwait(false) == 0, "uncommitted_write_survived_discard");
                // The old primary-key lock must no longer prevent this bounded fresh write.
                await ExecAsync(fresh, null, $"INSERT INTO {table}(ID) VALUES (2)").ConfigureAwait(false);
                Require(await CountAsync(fresh, table, 2).ConfigureAwait(false) == 1, "transaction_lock_not_released");
                await ExecAsync(fresh, null, $"DELETE FROM {table} WHERE ID=2").ConfigureAwait(false);
            }
            Ledger["transaction"] = new { status = "discard_verified", same_session_reset_proven = false,
                old_uncommitted_rows = 1, fresh_rows_before_close = 0, fresh_rows_after_close = 0,
                disposed_tcp_sockets = 1, transaction_lock_released = true };
            Ledger["isolation"] = new { status = "fresh_baseline_observed", same_session_reset_proven = false, requested_isolation = "ReadCommitted" };

            if (tempCreated)
            {
                Stage = "temporary_session_rows_discard";
                await using var tempOld = await OpenVerifiedAsync().ConfigureAwait(false);
                await ExecAsync(tempOld, null, $"INSERT INTO {temporary}(ID) VALUES (3)").ConfigureAwait(false);
                Require(await CountAsync(tempOld, temporary, 3).ConfigureAwait(false) == 1, "old_temp_rows_missing");
                long disposedBefore = Observe.Counter("DisposedTcpSockets");
                await tempOld.CloseAsync().ConfigureAwait(false);
                Require(Observe.Counter("DisposedTcpSockets") - disposedBefore == 1, "temporary_physical_discard_not_observed");
                await using var fresh = await OpenVerifiedAsync().ConfigureAwait(false);
                Require(await CountAsync(fresh, temporary, 3).ConfigureAwait(false) == 0, "temporary_rows_leaked_to_fresh_session");
                Ledger["temporary_objects"] = new { status = "discard_verified", same_session_reset_proven = false,
                    object_kind = "global_temporary_table_preserve_rows", old_rows = 1, fresh_rows = 0,
                    definition_persists_until_owned_cleanup = true, disposed_tcp_sockets = 1 };
            }
            await VerifySessionSettingAsync("timezone", "SELECT SESSIONTIMEZONE FROM DUAL", "SET TIME ZONE '+9:00'", "+09:00").ConfigureAwait(false);
            await VerifySessionSettingAsync("nls_language", "SELECT CAST(DATE '2020-01-02' AS VARCHAR(32)) FROM DUAL",
                "ALTER SESSION SET NLS_DATE_FORMAT='YYYY/MM/DD'", "2020/01/02").ConfigureAwait(false);
            await VerifyStaleObjectsAsync().ConfigureAwait(false);
            Ledger["schema"] = new { status = "fresh_baseline_observed", same_session_reset_proven = false,
                expected = TestUser, cross_schema_mutation_attempted = false };
            Report["discard_decision"] = "full_reset_not_proven_discard_all_sessions";
            Report["measured_reused_sessions"] = 0;
        }
        catch (Exception error) { workFailure = error; Report["work_error"] = SafeError(error); Report["work_failed_stage"] = Stage; }
        finally
        {
            Stage = "fresh_owned_cleanup";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync().ConfigureAwait(false))
                {
                    // Observe existence after an attempted DDL even if its response was lost.
                    if (tempAttempted && await ObjectCountAsync(cleaner, temporary).ConfigureAwait(false) == 1)
                        await ExecAsync(cleaner, null, $"DROP TABLE {temporary}").ConfigureAwait(false);
                    if (tableAttempted && await ObjectCountAsync(cleaner, table).ConfigureAwait(false) == 1)
                        await ExecAsync(cleaner, null, $"DROP TABLE {table}").ConfigureAwait(false);
                }
                await using var check = await OpenVerifiedAsync().ConfigureAwait(false);
                Require(await ObjectCountAsync(check, table).ConfigureAwait(false) == 0 && await ObjectCountAsync(check, temporary).ConfigureAwait(false) == 0,
                    "owned_object_cleanup_absence_failed");
                Report["cleanup_verified"] = true;
                Report["final_verification_identity"] = TestUser;
                Report["final_verification_schema"] = TestUser;
                Report["final_database_state"] = "unique_owned_objects_absent";
            }
            catch (Exception error) { cleanupFailure = error; Report["cleanup_error"] = SafeError(error); }
        }
        Report["physical_connections_created"] = Observe!.Counter("CreatedTcpSockets");
        Report["physical_connections_disposed"] = Observe.Counter("DisposedTcpSockets");
        Report["negotiated_encrypt_modes"] = Observe.Modes;
        Require(Observe.Counter("CreatedTcpSockets") == Observe.Counter("DisposedTcpSockets"), "physical_socket_leak");
        if (workFailure != null) throw new ProbeFailure("mandatory_probe_failed");
        if (cleanupFailure != null) throw new ProbeFailure("cleanup_failed");
    }

    private static async Task VerifySessionSettingAsync(string capability, string readSql, string mutateSql, string expected)
    {
        Stage = capability + "_session_setting";
        await using var old = await OpenVerifiedAsync().ConfigureAwait(false);
        string baseline, changed;
        try
        {
            baseline = await ReadSettingAsync(old, readSql).ConfigureAwait(false);
            await ExecAsync(old, null, mutateSql).ConfigureAwait(false);
            changed = await ReadSettingAsync(old, readSql).ConfigureAwait(false);
        }
        catch (DmException error) when (IsRecoverableServerRejection(error, old))
        {
            Ledger[capability] = new { status = "not_proven", same_session_reset_proven = false,
                reason = "documented_session_capability_attempt_rejected", error = SafeError(error) };
            return;
        }
        if (changed != expected || changed == baseline)
        {
            Ledger[capability] = new { status = "not_proven", same_session_reset_proven = false,
                reason = "mutation_effect_not_observed", documented_set_executed = true };
            return;
        }
        long before = Observe!.Counter("DisposedTcpSockets");
        await old.CloseAsync().ConfigureAwait(false);
        Require(Observe.Counter("DisposedTcpSockets") - before == 1, "session_setting_discard_not_observed");
        await using var fresh = await OpenVerifiedAsync().ConfigureAwait(false);
        Require(await ReadSettingAsync(fresh, readSql).ConfigureAwait(false) == baseline, "session_setting_leaked_to_fresh_session");
        if (capability == "nls_language")
            Ledger[capability] = new { status = "not_proven", same_session_reset_proven = false, reason = "language_not_probed",
                date_format_subset = new { status = "discard_verified", mutation_effect_observed = true,
                    fresh_matches_baseline = true, disposed_tcp_sockets = 1 } };
        else
            Ledger[capability] = new { status = "discard_verified", same_session_reset_proven = false,
                mutation_effect_observed = true, fresh_matches_baseline = true, disposed_tcp_sockets = 1 };
    }
    private static async Task<string> ReadSettingAsync(DmConnection c, string sql)
    {
        await using var command = Command(c, null, sql);
        object? value = await command.ExecuteScalarAsync().ConfigureAwait(false);
        Require(value is string, "session_setting_string_required");
        return ((string)value!).Trim();
    }
    private static bool IsRecoverableServerRejection(DmException error, DmConnection c) =>
        error.FailureInfo is { ErrorKind: DmErrorKind.Server, OperationOutcome: DmOperationOutcome.ServerReported,
            ConnectionReusable: true, ServerErrorNumber: not null } && c.State == ConnectionState.Open;

    private static async Task VerifyStaleObjectsAsync()
    {
        Stage = "closed_prepared_command_rejection";
        await using (var old = await OpenVerifiedAsync().ConfigureAwait(false))
        {
            await using var prepared = Command(old, null, "SELECT 1 FROM DUAL");
            await prepared.PrepareAsync(CancellationToken.None).ConfigureAwait(false);
            Require(Convert.ToInt32(await prepared.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) == 1, "prepared_positive_control_failed");
            await old.CloseAsync().ConfigureAwait(false);
            long sends = Observe!.SendCount, sockets = Observe.Counter("CreatedTcpSockets");
            bool rejected = false;
            try { await prepared.ExecuteScalarAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && Observe.SendCount == sends && Observe.Counter("CreatedTcpSockets") == sockets && old.State == ConnectionState.Closed,
                "stale_prepared_command_not_rejected_without_io");
            Ledger["prepared_statement"] = new { status = "discard_verified", same_session_reset_proven = false,
                positive_control = true, old_object_rejected = true, post_close_new_sends = 0, post_close_new_sockets = 0 };
        }
        Stage = "closed_reader_rejection";
        await using (var old = await OpenVerifiedAsync().ConfigureAwait(false))
        {
            await using var command = Command(old, null, "SELECT 1 FROM DUAL UNION ALL SELECT 2 FROM DUAL");
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            Require(await reader.ReadAsync().ConfigureAwait(false), "reader_positive_control_failed");
            await old.CloseAsync().ConfigureAwait(false);
            long sends = Observe!.SendCount, sockets = Observe.Counter("CreatedTcpSockets");
            bool rejected = false;
            try { await reader.ReadAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && reader.IsClosed && Observe.SendCount == sends && Observe.Counter("CreatedTcpSockets") == sockets,
                "stale_reader_not_rejected_without_io");
            Ledger["open_reader"] = new { status = "discard_verified", same_session_reset_proven = false,
                positive_control = true, old_object_rejected = true, post_close_new_sends = 0, post_close_new_sockets = 0 };
        }
    }

    private static async Task<DmConnection> OpenVerifiedAsync()
    {
        var connection = new DmConnection(Settings);
        try
        {
            long sockets = Observe!.Counter("CreatedTcpSockets");
            int priorModes = Observe.Modes.Count;
            await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            Require(Observe.Counter("CreatedTcpSockets") - sockets == 1 && Observe.Modes.Count == priorModes + 1 && Observe.Modes[^1] == (Mode == "tls" ? 1 : 0),
                "new_physical_session_and_explicit_transport_required");
            await using var command = Command(connection, null, "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            Require(await reader.ReadAsync().ConfigureAwait(false) && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "test_identity_or_schema_mismatch");
            string version = connection.ServerVersion;
            Require(Regex.IsMatch(version, @"^\d+(\.\d+){1,5}$"), "safe_numeric_server_version_required");
            if (Report.TryGetValue("server_version", out object? previous)) Require((string?)previous == version, "server_profile_changed");
            Report["server_version"] = version;
            Report["server_identity"] = TestUser;
            Report["server_schema"] = TestUser;
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static DmCommand Command(DmConnection c, DbTransaction? tx, string sql)
    {
        var command = new DmCommand(sql, c) { CommandTimeout = 15 };
        ((DbCommand)command).Transaction = tx;
        return command;
    }
    private static async Task ExecAsync(DmConnection c, DbTransaction? tx, string sql)
    { await using var command = Command(c, tx, sql); await command.ExecuteNonQueryAsync().ConfigureAwait(false); }
    private static async Task<int> CountAsync(DmConnection c, string table, int id, DbTransaction? tx = null)
    {
        await using var command = Command(c, tx, $"SELECT COUNT(*) FROM {table} WHERE ID=:p0");
        command.Parameters.Add(new DmParameter("p0", DmDbType.Int32) { Value = id });
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
    private static async Task<int> ObjectCountAsync(DmConnection c, string table)
    {
        await using var command = Command(c, null, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0");
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = table });
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
    private static object SafeError(Exception error) => new
    {
        type = error.GetType().FullName,
        classification = error is ProbeFailure f ? f.Kind : error is OperationCanceledException ? "operation_canceled" :
            error is NotSupportedException ? "unsupported" : error is DmException ? "provider_error" : "unclassified_failure",
        server_number = error is DmException dm ? dm.FailureInfo?.ServerErrorNumber : null,
        failure_kind = error is DmException driver ? driver.FailureInfo?.ErrorKind.ToString() : null,
        connection_reusable = error is DmException provider ? (bool?)provider.FailureInfo?.ConnectionReusable : null
    };
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }

    private sealed class Observation : IDisposable
    {
        private readonly Type Transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")
            ?? throw new ProbeFailure("transport_counters_missing");
        private readonly Type Wire = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")
            ?? throw new ProbeFailure("wire_counters_missing");
        private readonly FieldInfo ModeField;
        private readonly long InitialCreated, InitialDisposed;
        internal readonly List<int> Modes = [];
        internal Observation()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            ModeField = Wire.GetField("AfterStartupNegotiatedEncryptMode", flags) ?? throw new ProbeFailure("startup_hook_missing");
            Require(ModeField.GetValue(null) == null, "isolated_hooks_required");
            InitialCreated = RawCounter("CreatedTcpSockets");
            InitialDisposed = RawCounter("DisposedTcpSockets");
            ModeField.SetValue(null, (Action<int>)(mode => Modes.Add(mode)));
        }
        private long RawCounter(string name) => (long)(Transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("transport_counter_missing"));
        internal long Counter(string name) => RawCounter(name) - (name == "CreatedTcpSockets" ? InitialCreated : InitialDisposed);
        internal long SendCount => (long)(Wire.GetProperty("SendCount", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("wire_counter_missing"));
        public void Dispose() => ModeField.SetValue(null, null);
    }
}
