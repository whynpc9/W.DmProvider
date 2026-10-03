using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
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
    private const string IdentitySql = "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T16", ["implementation"] = "W-package", ["accepted"] = false,
        ["status"] = "started", ["exit_code"] = -1, ["session_reset_verified"] = false, ["reuse_policy"] = "discard",
        ["handshake_optimization_claimed"] = false, ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier
    };
    private static readonly Dictionary<string, object?> Cases = new();
    private static readonly HashSet<object> Owners = new(ReferenceEqualityComparer.Instance);
    private static Observation Observe = null!;
    private static string Mode = "", Settings = "", StageValue = "arguments";
    private static string? Output;
    private static string Stage
    {
        get => StageValue;
        set { StageValue = value; Report["checkpoint_stage"] = value; Checkpoint(); }
    }
    private static void Checkpoint()
    {
        if (Output == null) return;
        File.WriteAllText(Output + ".tmp", JsonSerializer.Serialize(Report));
        File.Move(Output + ".tmp", Output, true);
    }
    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        try
        {
            Require(args.Length == 4 && args[0] is "offline" or "shared" or "tls", "usage");
            Mode = args[0]; Output = args[3]; Report["mode"] = Mode; Report["cases"] = Cases;
            Stage = "exact_package_identity";
            VerifyPackage(args[2]);
            using (Observe = new Observation())
            {
                Stage = "offline_public_contract";
                await VerifyOfflineAsync();
                if (Mode == "offline")
                { Report["status"] = "offline_verified"; Report["integration"] = "integration_pending"; }
                else
                {
                    Stage = "configure";
                    Configure(Path.GetFullPath(args[1]));
                    await RunRealAsync();
                    Report["status"] = "pool_discard_verified"; Report["integration"] = "real_test_schema";
                }
            }
            code = 0; Report["accepted"] = true;
        }
        catch (Exception error)
        {
            bool pending = error is ProbeFailure { Kind: "integration_pending" };
            Report["status"] = pending ? "integration_pending" : "rejected";
            Report["integration"] = pending ? "integration_pending" : "not_accepted";
            Report["failed_stage"] = Report.TryGetValue("primary_failed_stage", out object? primaryStage) ? primaryStage : Stage;
            Report["error"] = SafeError(error); code = pending ? 3 : 1;
        }
        Report["exit_code"] = code;
        try { Checkpoint(); Console.WriteLine(JsonSerializer.Serialize(Report)); }
        catch { Console.WriteLine("{\"task\":\"T16\",\"status\":\"rejected\",\"classification\":\"report_write_failed\",\"exit_code\":1}"); return 1; }
        return code;
    }

    private static void VerifyPackage(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;
        Assembly assembly = typeof(DmConnection).Assembly;
        string expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "R3PackageVersion").Value ?? throw new ProbeFailure("missing_package_version");
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        Require(expected == actual && expected == manifest.GetProperty("version").GetString(), "exact_package_version_mismatch");
        Require(sha == manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString(), "loaded_asset_hash_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "loaded_asset_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3PoolProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + expected).GetProperty("type").GetString() == "package", "package_reference_required");
        Require(!deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)), "official_dependency_rejected");
        Report["package_version"] = expected; Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = sha; Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
        Report["reference_kind"] = "exact_PackageReference";
    }

    private static async Task VerifyOfflineAsync()
    {
        Require(typeof(DbDataSource).IsAssignableFrom(typeof(DmDataSource)), "datasource_base_contract_missing");
        var builder = new DmConnectionStringBuilder
        {
            Server = "synthetic.invalid", User = "SYNTHETIC_USER", Password = "synthetic_secret_marker",
            Pooling = true, MaxPoolSize = 1, MaxPoolWaiters = 2, TlsCaCertificatePath = "/synthetic/ca.pem",
            TlsClientCertificatePath = "/synthetic/client.pfx", TlsClientCertificatePassword = "synthetic_certificate_secret_marker"
        };
        long created = Observe.Created;
        await using (var source = new DmDataSource(builder.ConnectionString))
        {
            Require(!source.ConnectionString.Contains("synthetic_secret_marker", StringComparison.Ordinal) &&
                !source.ConnectionString.Contains("synthetic_certificate_secret_marker", StringComparison.Ordinal), "datasource_secret_exposure");
            await using var connection = source.CreateConnection();
            Require(connection.State == ConnectionState.Closed, "create_connection_must_be_closed");
            await using var command = source.CreateCommand(IdentitySql);
            Require(command.CommandText == IdentitySql && Observe.Created == created, "shortcut_construction_must_not_open");
        }
        await using (var direct = new DmConnection(builder.ConnectionString))
        {
            bool rejected = false;
            try { await direct.OpenAsync(); } catch (NotSupportedException) { rejected = true; }
            Require(rejected && direct.State == ConnectionState.Closed && Observe.Created == created,
                "direct_explicit_tls_pooling_must_reject_before_socket");
        }
        Cases["offline_public_contract"] = new { category = "Improvement", feature = "Pooling", status = "passed", construction_sockets = 0, configuration_redacted = true };
        Cases["direct_explicit_certificate_rejected"] = new { category = "Contract", feature = "Pooling", status = "passed", sockets = 0 };
        Report["offline_core_scheduler_scope"] = "separate_PoolTests_not_fabricated_database_work";
    }

    private static void Configure(string repo)
    {
        string raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var builder = new DmConnectionStringBuilder(raw);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        builder.Schema = TestUser; builder.Pooling = true; builder.MaxPoolSize = 1; builder.MaxPoolWaiters = 4;
        builder.PoolAcquireTimeout = TimeSpan.FromSeconds(5); builder.ConnectTimeout = TimeSpan.FromSeconds(15);
        builder.CommandTimeout = 15; builder.StmtPooling = false; builder.PreparePooling = false;
        builder.PersistSecurityInfo = false; builder.LogLevel = LogLevel.OFF;
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
        Report["explicit_transport"] = builder.TransportSecurity.ToString(); Report["pooling_enabled"] = true;
        Report["maximum_physical_connections_per_owner"] = 1; Report["maximum_waiters_per_owner"] = 4;
        Report["pool_acquire_timeout_milliseconds"] = 5000; Report["persist_security_info"] = false;
    }

    private static async Task RunRealAsync()
    {
        string table = "T16_T_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        Report["owned_objects"] = new[] { table }; Report["final_database_state"] = "unverified";
        bool tableAttempted = false;
        Exception? workFailure = null, cleanupFailure = null;
        using var source = new DmDataSource(Settings);
        try
        {
            Stage = "create_owned_object";
            await using (var setup = await OpenVerifiedAsync(source.CreateConnection))
            {
                Require(await ObjectCountAsync(setup, table) == 0, "unique_object_already_exists");
                tableAttempted = true; await ExecAsync(setup, $"CREATE TABLE {table} (ID INT PRIMARY KEY)");
            }
            await CapacityAsync("datasource", source.CreateConnection, source.ClearPool);
            if (Mode == "shared") await CapacityAsync("direct_registry", () => new DmConnection(Settings), DmConnection.ClearAllPools);
            Stage = "shortcut_command_ownership";
            await ShortcutsAsync(source, table);
            Stage = "active_transaction_and_stale_objects_discard";
            await TransactionAndStaleAsync(source, table);
            Stage = "datasource_dispose_waiting";
            await DisposeWaitingAsync();
        }
        catch (Exception error) { workFailure = error; Report["work_error"] = SafeError(error); Report["work_failed_stage"] = Stage; }
        finally
        {
            Stage = "fresh_owned_cleanup";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync(source.CreateConnection))
                {
                    if (tableAttempted && await ObjectCountAsync(cleaner, table) == 1) await ExecAsync(cleaner, $"DROP TABLE {table}");
                }
                await using (var fresh = await OpenVerifiedAsync(source.CreateConnection))
                    Require(await ObjectCountAsync(fresh, table) == 0, "owned_object_absence_failed");
                Report["cleanup_verified"] = true; Report["final_database_state"] = "unique_owned_objects_absent";
                Report["final_verification_identity"] = TestUser; Report["final_verification_schema"] = TestUser;
            }
            catch (Exception error) { cleanupFailure = error; Report["cleanup_error"] = SafeError(error); }
        }
        Report["physical_connections_created"] = Observe.Created;
        Report["physical_connections_disposed"] = Observe.Disposed;
        Report["authenticated_successful_open_count"] = Observe.AuthenticatedSessionCount;
        Report["authenticated_open_count_basis"] = "unique_sessions_entering_user_wire_exchange_after_open";
        Report["negotiated_encrypt_modes"] = Observe.Modes.ToArray(); Report["measured_reused_sessions"] = 0;
        Report["owner_final_snapshots"] = Owners.Select(Snapshot).ToArray();
        Report["state_evidence_category"] = "state_evidence";
        Report["network_io_counts"] = Observe.NetworkCounts;
        Exception? accountingFailure = null;
        try
        {
            Require(Observe.Created == Observe.Disposed && Observe.Created == Observe.Modes.Count && Observe.Created == Observe.AuthenticatedSessionCount,
                "physical_socket_or_authentication_accounting_failed");
            Require(Observe.Modes.All(m => m == (Mode == "tls" ? 1 : 0)), "explicit_transport_mode_mismatch");
            Require(Owners.All(IsQuiescent), "pool_state_not_zero");
            Require(Observe.AsyncNetworkOnly(Mode == "tls"), "async_probe_used_synchronous_network_or_missing_observation");
        }
        catch (Exception error) { accountingFailure = error; Report["secondary_error"] = SafeError(error); }
        if (workFailure != null)
        { Report["primary_failed_stage"] = Report["work_failed_stage"]; throw workFailure; }
        if (cleanupFailure != null)
        { Report["primary_failed_stage"] = "fresh_owned_cleanup"; throw cleanupFailure; }
        if (accountingFailure != null) throw accountingFailure;
    }

    private static async Task CapacityAsync(string name, Func<DmConnection> factory, Action clear)
    {
        Stage = name + "_waiting_cancellation";
        await using var held = await OpenVerifiedAsync(factory);
        object owner = Owner(held);
        long created = Observe.Created;
        using (var cancellation = new CancellationTokenSource())
        await using (var waiting = factory())
        {
            Task opening = waiting.OpenAsync(cancellation.Token);
            Require(Count(owner, "Waiting") == 1 && Observe.Created == created, "waiting_open_created_socket");
            cancellation.Cancel();
            var error = await ExpectAsync<DmOperationCanceledException>(opening);
            Require(error.CancellationToken == cancellation.Token && error.FailureInfo.Phase == DmFailurePhase.PoolWait &&
                error.FailureInfo.OperationOutcome == DmOperationOutcome.NotSent, "pool_cancel_metadata_failed");
            Require(held.State == ConnectionState.Open && Count(owner, "Leased") == 1 && Count(owner, "Waiting") == 0 && Observe.Created == created,
                "waiting_cancellation_disturbed_business_lease");
        }
        Stage = name + "_waiting_deadline";
        await using (var timeout = factory())
        {
            var error = await ExpectAsync<DmTimeoutException>(timeout.OpenAsync());
            Require(error.FailureInfo.Phase == DmFailurePhase.PoolWait && error.FailureInfo.ErrorCode == "WDM_POOL_WAIT_TIMEOUT" &&
                error.FailureInfo.OperationOutcome == DmOperationOutcome.NotSent && Observe.Created == created,
                "pool_deadline_metadata_or_socket_failed");
        }
        Stage = name + "_clear_preserves_capacity";
        long epoch = Count(owner, "Epoch"); clear();
        Require(Count(owner, "Epoch") == epoch + 1, "clear_did_not_increment_epoch");
        await using var next = factory();
        Task nextOpen = next.OpenAsync();
        Require(Count(owner, "Waiting") == 1 && Observe.Created == created, "clear_split_capacity_domain");
        long disposed = Observe.Disposed;
        Observe.ExpectCloseBeforeNextConnect(disposed + 1);
        await held.CloseAsync();
        await nextOpen;
        Require(Observe.Disposed == disposed + 1 && Observe.Created == created + 1 && Observe.RequiredCloseObservedAtConnect,
            "return_did_not_discard_before_new_open");
        await VerifyIdentityAsync(next);
        Require(ReferenceEquals(owner, Owner(next)), "same_key_owner_changed_while_active");
        await next.CloseAsync(); await next.CloseAsync();
        Require(IsQuiescent(owner), "capacity_case_not_quiescent");
        Cases[name + "_capacity"] = new { category = "Contract", feature = "Pooling", status = "passed", waiting_cancel_not_sent = true, waiting_deadline_not_sent = true,
            clear_kept_capacity_domain = true, transport_closed_before_replacement_connect = true,
            physical_replacement_authenticated = true, reused_sessions = 0, final_state_zero = true };
    }

    private static async Task ShortcutsAsync(DmDataSource source, string table)
    {
        string identity = $"USER='{TestUser}' AND SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())='{TestUser}'";
        long before = Observe.Created, disposed = Observe.Disposed;
        await using (var nonreader = source.CreateCommand($"INSERT INTO {table} SELECT CASE WHEN {identity} THEN 9 ELSE NULL END FROM DUAL"))
            Require(await nonreader.ExecuteNonQueryAsync() == 1, "shortcut_nonreader_identity_or_result_failed");
        Require(Observe.Created == before + 1 && Observe.Disposed == disposed + 1, "shortcut_nonreader_not_returned");
        before = Observe.Created; disposed = Observe.Disposed;
        await using (var failure = source.CreateCommand($"INSERT INTO {table} SELECT CASE WHEN {identity} THEN 9 ELSE 10 END FROM DUAL"))
        {
            var error = await ExpectAsync<DmException>(failure.ExecuteNonQueryAsync());
            Report["shortcut_actual_failure"] = SafeError(error);
            Checkpoint();
            Require(error.FailureInfo is { ErrorKind: DmErrorKind.Server, OperationOutcome: DmOperationOutcome.ServerReported }, "shortcut_failure_not_server_reported");
        }
        Require(Observe.Created == before + 1 && Observe.Disposed == disposed + 1, "shortcut_failure_not_returned");
        before = Observe.Created; disposed = Observe.Disposed;
        await using (var scalar = source.CreateCommand("SELECT USER||':'||SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"))
            Require((string?)await scalar.ExecuteScalarAsync() == TestUser + ":" + TestUser, "shortcut_scalar_identity_failed");
        Require(Observe.Created == before + 1 && Observe.Disposed == disposed + 1, "shortcut_scalar_not_returned");
        before = Observe.Created;
        await using (var command = source.CreateCommand(IdentitySql))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "shortcut_reader_identity_failed");
            Require(Observe.Created == before + 1, "shortcut_reader_did_not_authenticate_fresh_session");
            using var cancellation = new CancellationTokenSource();
            await using var waiting = source.CreateConnection();
            long sockets = Observe.Created;
            Task opening = waiting.OpenAsync(cancellation.Token);
            Require(!opening.IsCompleted && Observe.Created == sockets, "shortcut_reader_released_before_close");
            cancellation.Cancel(); await ExpectAsync<DmOperationCanceledException>(opening);
            await reader.CloseAsync(); await reader.CloseAsync();
        }
        Require(IsQuiescent(SourceOwner(source)), "shortcut_reader_capacity_leaked");
        Cases["shortcut_command_ownership"] = new { category = "Contract", feature = "Pooling", status = "passed", nonreader_success_returned = true,
            nonreader_server_failure_returned = true, scalar_returned = true, reader_held_until_close = true,
            implicit_identity_checked_by_sql = true, final_state_zero = true };
    }

    private static async Task TransactionAndStaleAsync(DmDataSource source, string table)
    {
        await using var old = await OpenVerifiedAsync(source.CreateConnection);
        await using DbTransaction transaction = await old.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var insert = new DmCommand($"INSERT INTO {table}(ID) VALUES(2)", old))
        { ((DbCommand)insert).Transaction = transaction; await insert.ExecuteNonQueryAsync(); }
        await using (var positive = new DmCommand($"SELECT COUNT(*) FROM {table} WHERE ID=2", old))
        {
            ((DbCommand)positive).Transaction = transaction;
            Require(Convert.ToInt32(await positive.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1, "old_transaction_write_positive_control_failed");
        }
        await using (var observerSource = new DmDataSource(Settings))
        await using (var observer = await OpenVerifiedAsync(observerSource.CreateConnection))
        await using (var count = new DmCommand($"SELECT COUNT(*) FROM {table} WHERE ID=2", observer))
            Require(Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0, "uncommitted_row_visible_before_discard");
        await using var command = new DmCommand("SELECT 1 FROM DUAL UNION ALL SELECT 2 FROM DUAL", old);
        ((DbCommand)command).Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "stale_reader_positive_control_failed");
        await old.CloseAsync();
        await using var fresh = await OpenVerifiedAsync(source.CreateConnection);
        await using (var count = new DmCommand($"SELECT COUNT(*) FROM {table} WHERE ID=2", fresh))
            Require(Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0, "uncommitted_row_survived_discard");
        long sends = Observe.SendCount, sockets = Observe.Created;
        await ExpectAsync<InvalidOperationException>(reader.ReadAsync());
        try { await transaction.RollbackAsync(); } catch (InvalidOperationException) { }
        try { command.Cancel(); } catch (InvalidOperationException) { }
        await reader.DisposeAsync(); await transaction.DisposeAsync();
        Require(Observe.SendCount == sends && Observe.Created == sockets && fresh.State == ConnectionState.Open,
            "stale_object_callback_touched_new_session");
        await ExecAsync(fresh, $"INSERT INTO {table}(ID) VALUES(2)");
        await ExecAsync(fresh, $"DELETE FROM {table} WHERE ID=2");
        Cases["transaction_and_stale_objects"] = new { category = "Contract", feature = "Pooling", status = "passed", old_transaction_rows = 1, independent_rows_before_close = 0,
            fresh_uncommitted_rows = 0,
            old_reader_rejected_without_io = true, stale_cancel_and_transaction_did_not_touch_fresh = true, lock_released = true };
    }

    private static async Task DisposeWaitingAsync()
    {
        await using var source = new DmDataSource(Settings);
        await using var held = await OpenVerifiedAsync(source.CreateConnection);
        object owner = Owner(held);
        await using var waiting = source.CreateConnection();
        long sockets = Observe.Created;
        Task opening = waiting.OpenAsync();
        Require(Count(owner, "Waiting") == 1 && Observe.Created == sockets, "dispose_case_not_waiting");
        await source.DisposeAsync();
        await ExpectAsync<ObjectDisposedException>(opening);
        Require(Observe.Created == sockets && Count(owner, "Leased") == 1, "dispose_aborted_business_or_created_socket");
        await VerifyIdentityAsync(held);
        await held.CloseAsync();
        Require(IsQuiescent(owner), "disposed_datasource_return_leaked");
        bool refused = false; try { source.CreateConnection().Dispose(); } catch (ObjectDisposedException) { refused = true; }
        Require(refused, "disposed_datasource_admitted_new_connection");
        Cases["datasource_dispose_waiting"] = new { category = "Contract", feature = "Pooling", status = "passed", waiting_terminated_without_socket = true,
            existing_business_lease_completed = true, new_admissions_rejected = true, final_state_zero = true };
    }

    private static async Task<DmConnection> OpenVerifiedAsync(Func<DmConnection> factory)
    {
        var connection = factory();
        try
        {
            long before = Observe.Created;
            await connection.OpenAsync();
            Require(Observe.Created == before + 1, "physical_session_reuse_observed");
            await VerifyIdentityAsync(connection); Owner(connection); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task VerifyIdentityAsync(DmConnection connection)
    {
        await using var command = new DmCommand(IdentitySql, connection) { CommandTimeout = 15 };
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "server_identity_or_schema_mismatch");
        Require(Regex.IsMatch(connection.ServerVersion, @"^\d+(\.\d+){1,5}$"), "safe_numeric_server_version_required");
        if (Report.TryGetValue("server_version", out object? prior)) Require((string?)prior == connection.ServerVersion, "server_profile_changed");
        Report["server_identity"] = TestUser; Report["server_schema"] = TestUser; Report["server_version"] = connection.ServerVersion;
    }
    private static async Task ExecAsync(DmConnection connection, string sql)
    { await using var command = new DmCommand(sql, connection) { CommandTimeout = 15 }; await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ObjectCountAsync(DmConnection connection, string name)
    {
        await using var command = new DmCommand("SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0", connection) { CommandTimeout = 15 };
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = name });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
    private static object Owner(DmConnection connection)
    {
        object owner = connection.GetType().GetProperty("PoolOwner", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(connection)
            ?? throw new ProbeFailure("pool_owner_missing");
        Owners.Add(owner); return owner;
    }
    private static object SourceOwner(DmDataSource source) => typeof(DmDataSource).GetProperty("Owner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static object RawSnapshot(object owner) => owner.GetType().GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static long Count(object owner, string field) => Convert.ToInt64(RawSnapshot(owner).GetType().GetProperty(field,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(RawSnapshot(owner)), CultureInfo.InvariantCulture);
    private static bool IsQuiescent(object owner) => new[] { "Creating", "Leased", "Closing", "Waiting", "Idle", "Resetting" }.All(n => Count(owner, n) == 0);
    private static object Snapshot(object owner) => new { creating = Count(owner, "Creating"), leased = Count(owner, "Leased"),
        closing = Count(owner, "Closing"), waiting = Count(owner, "Waiting"), idle = Count(owner, "Idle"), resetting = Count(owner, "Resetting") };
    private static async Task<T> ExpectAsync<T>(Task operation) where T : Exception
    { try { await operation; } catch (T error) { return error; } throw new ProbeFailure("expected_failure_missing"); }
    private static object SafeError(Exception error) => new { type = error.GetType().FullName,
        classification = error is ProbeFailure failure ? failure.Kind : error is OperationCanceledException ? "operation_canceled" :
            error is DmException ? "provider_error" : error is NotSupportedException ? "unsupported" : "unclassified_failure",
        failure_phase = (error as DmException)?.FailureInfo?.Phase.ToString(), failure_kind = (error as DmException)?.FailureInfo?.ErrorKind.ToString(),
        operation_outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString(),
        driver_error_code = (error as DmException)?.DriverErrorCode,
        number = error is DmException dm ? (int?)dm.Number : null,
        server_number = (error as DmException)?.FailureInfo?.ServerErrorNumber,
        connection_reusable = (error as DmException)?.FailureInfo?.ConnectionReusable };
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }

    private sealed class Observation : IDisposable
    {
        private readonly Type transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!;
        private readonly Type wire = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!;
        private readonly FieldInfo modeField;
        private readonly FieldInfo ioField;
        private readonly FieldInfo exchangeField;
        private readonly object ioGate = new();
        private readonly Dictionary<string, long> ioCounts = new();
        private readonly HashSet<long> authenticatedSessions = new();
        private bool invalidIoOperation;
        private long requiredDisposedAtNextConnect = -1;
        private bool requiredCloseObserved;
        private readonly long initialCreated, initialDisposed;
        internal readonly List<int> Modes = [];
        internal Observation()
        {
            modeField = wire.GetField("AfterStartupNegotiatedEncryptMode", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("startup_mode_hook_missing");
            Require(modeField.GetValue(null) == null, "isolated_hooks_required");
            ioField = transport.GetField("BeforeNetworkIo", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("network_io_observer_hook_missing");
            Require(ioField.GetValue(null) == null, "isolated_io_hook_required");
            exchangeField = wire.GetField("AfterExchangeEntered", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("wire_exchange_observer_hook_missing");
            Require(exchangeField.GetValue(null) == null, "isolated_exchange_hook_required");
            foreach (string operation in new[] { "connect", "tls", "send", "receive" })
            { ioCounts["async_" + operation] = 0; ioCounts["sync_" + operation] = 0; }
            initialCreated = Counter("CreatedTcpSockets"); initialDisposed = Counter("DisposedTcpSockets");
            modeField.SetValue(null, (Action<int>)(mode => Modes.Add(mode)));
            ioField.SetValue(null, (Action<bool, string>)((asynchronous, operation) =>
            {
                lock (ioGate)
                {
                    string key = (asynchronous ? "async_" : "sync_") + operation;
                    if (ioCounts.ContainsKey(key)) ioCounts[key]++;
                    else invalidIoOperation = true;
                    if (operation == "connect" && requiredDisposedAtNextConnect >= 0)
                    {
                        requiredCloseObserved = Disposed >= requiredDisposedAtNextConnect;
                        requiredDisposedAtNextConnect = -1;
                    }
                }
            }));
            Type[] arguments = exchangeField.FieldType.GenericTypeArguments;
            ParameterExpression identity = Expression.Parameter(arguments[0]);
            ParameterExpression purpose = Expression.Parameter(arguments[1]);
            var method = typeof(Observation).GetMethod(nameof(ObserveUserExchange), BindingFlags.Instance | BindingFlags.NonPublic)!;
            exchangeField.SetValue(null, Expression.Lambda(exchangeField.FieldType,
                Expression.Call(Expression.Constant(this), method, Expression.Convert(identity, typeof(object)),
                    Expression.Convert(purpose, typeof(object))), identity, purpose).Compile());
        }
        private long Counter(string name) => (long)(transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("transport_counter_missing"));
        internal long Created => Counter("CreatedTcpSockets") - initialCreated;
        internal long Disposed => Counter("DisposedTcpSockets") - initialDisposed;
        internal long SendCount => (long)(wire.GetProperty("SendCount", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("wire_counter_missing"));
        internal Dictionary<string, long> NetworkCounts { get { lock (ioGate) return new(ioCounts); } }
        // A user wire lease requires an already-open connection. Keep session identity in memory only;
        // constructor, login failure and successful-case assertions cannot invent or lose this observation.
        private void ObserveUserExchange(object identity, object purpose)
        {
            if (purpose.ToString() is not ("Query" or "Reader" or "TransactionControl" or "Metadata" or "Lob")) return;
            long sessionId = (long)identity.GetType().GetProperty("SessionId")!.GetValue(identity)!;
            lock (ioGate) authenticatedSessions.Add(sessionId);
        }
        internal long AuthenticatedSessionCount { get { lock (ioGate) return authenticatedSessions.Count; } }
        internal void ExpectCloseBeforeNextConnect(long disposed)
        { lock (ioGate) { requiredDisposedAtNextConnect = disposed; requiredCloseObserved = false; } }
        internal bool RequiredCloseObservedAtConnect { get { lock (ioGate) return requiredCloseObserved; } }
        internal bool AsyncNetworkOnly(bool tls)
        {
            lock (ioGate) return !invalidIoOperation && ioCounts.Where(p => p.Key.StartsWith("sync_", StringComparison.Ordinal)).All(p => p.Value == 0) &&
                ioCounts["async_connect"] > 0 && ioCounts["async_send"] > 0 && ioCounts["async_receive"] > 0 &&
                (tls ? ioCounts["async_tls"] > 0 : ioCounts["async_tls"] == 0);
        }
        public void Dispose() { modeField.SetValue(null, null); ioField.SetValue(null, null); exchangeField.SetValue(null, null); }
    }
}
