using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using W.Dm;
using W.Dm.Config;

internal static partial class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const int PageRows = 2048;
    private static readonly CancellationToken Token = CancellationToken.None;
    private static readonly byte[] Blob = Enumerable.Range(0, 256 * 1024).Select(i => (byte)(i % 251)).ToArray();
    private static readonly string Clob = string.Concat(Enumerable.Repeat("A中e\u0301文", 32768));
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T13", ["implementation"] = "W-package",
        ["status"] = "started", ["accepted"] = false,
        ["streaming_lob_claimed"] = false, ["cancellation_acceptance"] = "T14_pending"
    };
    private static string Stage = "arguments";
    private static string Mode = "";
    private static string Repo = "";
    private static string Settings = "";
    private static Trace? Observation;

    public static async Task<int> Main(string[] args)
    {
        int exitCode = 1;
        string? reportPath = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
        try
        {
            Require(args.Length == 4 && args[0] is "offline" or "shared" or "tls" or "security-shared" or "security-tls", "usage");
            bool security = args[0] is "security-shared" or "security-tls";
            Mode = args[0] switch { "security-shared" => "shared", "security-tls" => "tls", _ => args[0] };
            Repo = Path.GetFullPath(args[1]);
            Report["mode"] = args[0];
            Stage = "package_identity";
            VerifyPackage(args[2]);
            Stage = "api_override_audit";
            AuditOverrides();
            if (Mode == "offline")
            {
                Report["status"] = "offline_verified";
                Report["integration"] = "integration_pending";
                Report["accepted"] = true;
            }
            else
            {
                Stage = "configuration";
                Configure();
                using (Observation = new Trace())
                    if (security) await RunSecurityAsync(reportPath!).ConfigureAwait(false);
                    else await RunRealAsync().ConfigureAwait(false);
                Report["status"] = security ? "security_rejection_verified" : "package_async_verified";
                Report["accepted"] = true;
                Report["integration"] = security ? "real_test_security" : "real_test_schema";
            }
            exitCode = 0;
        }
        catch (Exception error)
        {
            Report["status"] = error is ProbeFailure { Kind: "integration_pending" } ? "integration_pending" : "rejected";
            Report["stage"] = Stage;
            Report["error"] = SafeError(error);
            exitCode = error is ProbeFailure { Kind: "integration_pending" } ? 3 : 1;
        }
        Report["exit_code"] = exitCode;
        var json = JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true });
        if (reportPath != null)
        {
            try { await File.WriteAllTextAsync(reportPath, json).ConfigureAwait(false); }
            catch (Exception error)
            {
                // The report itself is safe; the I/O exception text can include paths.
                Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", stage = "report_write", error = SafeError(error), exit_code = 1 }));
                return 1;
            }
        }
        Console.WriteLine(json);
        return exitCode;
    }

    private static void VerifyPackage(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = doc.RootElement;
        var assembly = typeof(DmConnection).Assembly;
        string? expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "R2PackageVersion").Value;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        string sha = Sha(File.ReadAllBytes(assembly.Location));
        Require(assembly.GetName().Name == "W.DmProvider" && expected != null && version == expected &&
            manifest.GetProperty("version").GetString() == expected, "package_version_mismatch");
        Require(manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString() == sha,
            "loaded_package_asset_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")),
            "loaded_asset_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R2AsyncProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + expected).GetProperty("type").GetString() == "package",
            "package_consumer_required");
        Require(!assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, "DM.DmProvider", StringComparison.OrdinalIgnoreCase)) &&
            !deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)),
            "official_provider_dependency_rejected");
        Report["package_version"] = version;
        Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = sha;
        Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
        Report["reference_kind"] = "exact_PackageReference";
    }

    private static void AuditOverrides()
    {
        var checkedApis = new List<string>();
        Check(typeof(DmConnection), "OpenAsync", typeof(CancellationToken));
        Check(typeof(DmConnection), "CloseAsync");
        Check(typeof(DmConnection), "DisposeAsync");
        Check(typeof(DmConnection), "BeginDbTransactionAsync", typeof(IsolationLevel), typeof(CancellationToken));
        Check(typeof(DmConnection), "GetSchemaAsync", typeof(CancellationToken));
        Check(typeof(DmConnection), "GetSchemaAsync", typeof(string), typeof(CancellationToken));
        Check(typeof(DmConnection), "GetSchemaAsync", typeof(string), typeof(string[]), typeof(CancellationToken));
        Check(typeof(DmConnection), "ChangeDatabaseAsync", typeof(string), typeof(CancellationToken));
        Check(typeof(DmCommand), "PrepareAsync", typeof(CancellationToken));
        Check(typeof(DmCommand), "ExecuteNonQueryAsync", typeof(CancellationToken));
        Check(typeof(DmCommand), "ExecuteScalarAsync", typeof(CancellationToken));
        Check(typeof(DmCommand), "ExecuteDbDataReaderAsync", typeof(CommandBehavior), typeof(CancellationToken));
        Check(typeof(DmCommand), "DisposeAsync");
        Check(typeof(DmDataReader), "ReadAsync", typeof(CancellationToken));
        Check(typeof(DmDataReader), "NextResultAsync", typeof(CancellationToken));
        Check(typeof(DmDataReader), "CloseAsync");
        Check(typeof(DmDataReader), "DisposeAsync");
        Check(typeof(DmDataReader), "IsDBNullAsync", typeof(int), typeof(CancellationToken));
        Check(typeof(DmDataReader), "GetSchemaTableAsync", typeof(CancellationToken));
        const string fieldValueApi = "DmDataReader.GetFieldValueAsync(Int32,CancellationToken)";
        Report["api_override_current"] = fieldValueApi;
        // DbDataReader also exposes GetFieldValueAsync<T>(int); choose the
        // provider override of the token overload, never the inherited facade.
        var generic = typeof(DmDataReader).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.DeclaringType == typeof(DmDataReader) && x.Name == "GetFieldValueAsync" &&
                x.IsGenericMethodDefinition && x.GetGenericArguments().Length == 1 &&
                x.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int), typeof(CancellationToken) }))
            .ToArray();
        Require(generic.Length == 1, "async_api_signature_not_unique:" + fieldValueApi);
        Verify(generic[0], typeof(DmDataReader), fieldValueApi);
        foreach (var name in new[] { "CommitAsync", "RollbackAsync" })
            Check(typeof(DmTransaction), name, typeof(CancellationToken));
        foreach (var name in new[] { "SaveAsync", "ReleaseAsync", "RollbackAsync" })
            Check(typeof(DmTransaction), name, typeof(string), typeof(CancellationToken));
        Check(typeof(DmTransaction), "DisposeAsync");
        Report["api_override_audit"] = checkedApis;
        Report.Remove("api_override_current");
        Report["api_override_audit_limit"] = "Provider overrides exclude ADO.NET base fallback; offline barrier tests and reviewed call chains establish nonblocking network I/O.";

        void Check(Type type, string name, params Type[] arguments)
        {
            // Labels contain only this audit's known type/method/signature
            // constants, rather than package exception text or user data.
            string label = type.Name + "." + name + "(" + string.Join(",", arguments.Select(t => t.Name)) + ")";
            Report["api_override_current"] = label;
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
            Require(method != null, "async_api_missing:" + label);
            Verify(method!, type, label);
        }
        void Verify(MethodInfo method, Type type, string label)
        {
            Require(method.DeclaringType == type && method.IsVirtual && method.GetBaseDefinition().DeclaringType != type,
                "ado_net_sync_fallback_detected:" + label);
            checkedApis.Add(label);
        }
    }

    private static void Configure()
    {
        string variable = Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING";
        string raw = Environment.GetEnvironmentVariable(variable) ?? throw new ProbeFailure("integration_pending");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        Report["configuration_step"] = "parse_test_settings";
        var builder = new DmConnectionStringBuilder(raw);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_user_required_before_authentication");
        ConfigStep("TransportSecurity", () => builder.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed);
        ConfigStep("Schema", () => builder.Schema = TestUser);
        ConfigStep("PersistSecurityInfo", () => builder.PersistSecurityInfo = false);
        ConfigStep("ConnPooling", () => builder.ConnPooling = false);
        ConfigStep("StmtPooling", () => builder.StmtPooling = false);
        ConfigStep("PreparePooling", () => builder.PreparePooling = false);
        ConfigStep("LogLevel", () => builder.LogLevel = LogLevel.OFF);
        ConfigStep("ConnectTimeout", () => builder.ConnectTimeout = TimeSpan.FromSeconds(20));
        ConfigStep("CommandTimeout", () => builder.CommandTimeout = 30);
        // Leave BufPrefetch at its supported default; LOGIN negotiates the server value.
        if (Mode == "tls")
        {
            Require(builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_tls_target_required");
            string certs = Path.Combine(Repo, ".local", "t07", "certs", "client_ssl", TestUser);
            ConfigStep("TlsCaCertificatePath", () => builder.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem"));
            ConfigStep("TlsClientCertificatePath", () => builder.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem"));
            ConfigStep("TlsClientPrivateKeyPath", () => builder.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem"));
            ConfigStep("TlsRevocationMode", () => builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck);
            Require(File.Exists(builder.TlsCaCertificatePath) && File.Exists(builder.TlsClientCertificatePath) && File.Exists(builder.TlsClientPrivateKeyPath),
                "isolated_tls_certificates_missing");
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Report["configuration_step"] = "serialize_settings";
        Settings = builder.ConnectionString;
        Report["explicit_transport"] = builder.TransportSecurity.ToString();
        Report["persist_security_info"] = false;
        Report["buf_prefetch"] = builder.BufPrefetch;
        Report["configuration_step"] = "complete";

        void ConfigStep(string name, Action apply)
        {
            // Only fixed setting names are reported, never values or exception text.
            Report["configuration_step"] = name;
            apply();
        }
    }

    private static async Task RunRealAsync()
    {
        string table = "T13_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        Report["owned_table"] = table;
        Report["final_database_state"] = "unverified";
        Exception? workError = null;
        Exception? cleanupError = null;
        var cases = new Dictionary<string, object?>();
        Report["cases"] = cases;
        try
        {
            Stage = "open_async";
            await using var connection = await OpenVerifiedAsync().ConfigureAwait(false);
            Report["server_identity"] = TestUser;
            Report["server_schema"] = TestUser;
            Report["server_version"] = connection.ServerVersion;
            cases["open_async"] = true;
            Stage = "owned_table_create_async";
            await ExecAsync(connection, null, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(2048), B BLOB, C CLOB)").ConfigureAwait(false);
            Stage = "prepare_execute_nonquery_async";
            await using (var insert = Command(connection, null, $"INSERT INTO {table}(ID,VAL) VALUES (:p0,:p1)",
                (1, DmDbType.Int32), ("prepared", DmDbType.VarChar)))
            {
                await insert.PrepareAsync(Token).ConfigureAwait(false);
                Require(await insert.ExecuteNonQueryAsync(Token).ConfigureAwait(false) == 1, "prepared_insert_count_invalid");
            }
            cases["prepare_execute_nonquery_async"] = true;
            Stage = "execute_scalar_async";
            Require(await ScalarAsync(connection, null, $"SELECT VAL FROM {table} WHERE ID=1").ConfigureAwait(false) is string value && value == "prepared",
                "scalar_value_invalid");
            cases["execute_scalar_async"] = true;
            await ExecAsync(connection, null, $"DELETE FROM {table} WHERE ID=1").ConfigureAwait(false);
            Stage = "fetch_fixture_insert_async";
            Require(await ExecAsync(connection, null,
                $"INSERT INTO {table}(ID,VAL) SELECT LEVEL, RPAD('x',1024,'x') FROM DUAL CONNECT BY LEVEL <= {PageRows}").ConfigureAwait(false) == PageRows,
                "fetch_fixture_row_count_invalid");
            Stage = "read_async_cross_page_fetch";
            int rows = 0;
            int fetchedAfterRows = -1;
            int fetchStart = Observation!.CountOpcode(7);
            await using (var command = Command(connection, null, $"SELECT ID,VAL FROM {table} ORDER BY ID"))
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, Token).ConfigureAwait(false))
            {
                int fetchBeforeRead = Observation.CountOpcode(7);
                Observation.Stage = "read_async_cross_page_fetch";
                Require((await reader.GetSchemaTableAsync(Token).ConfigureAwait(false))?.Rows.Count == 2, "cached_schema_invalid");
                while (await reader.ReadAsync(Token).ConfigureAwait(false))
                {
                    if (fetchedAfterRows < 0 && Observation.CountOpcode(7) > fetchBeforeRead) fetchedAfterRows = rows;
                    rows++;
                    Require(await reader.GetFieldValueAsync<int>(0, Token).ConfigureAwait(false) == rows &&
                        !await reader.IsDBNullAsync(1, Token).ConfigureAwait(false) &&
                        (await reader.GetFieldValueAsync<string>(1, Token).ConfigureAwait(false)).Length == 1024, "fetched_row_invalid");
                }
                Require(rows == PageRows && Observation.CountOpcode(7) > fetchBeforeRead && fetchedAfterRows >= 0,
                    "first_actual_fetch_not_observed");
                await reader.CloseAsync().ConfigureAwait(false);
                Require(reader.IsClosed, "reader_close_async_failed");
            }
            cases["read_async_cross_page_fetch"] = new { rows, fetch_frames = Observation.CountOpcode(7) - fetchStart, rows_before_first_actual_fetch = fetchedAfterRows };
            await ExecAsync(connection, null, $"DELETE FROM {table}").ConfigureAwait(false);
            Stage = "next_result_async";
            int moreStart = Observation.CountOpcode(44);
            await using (var command = Command(connection, null, "SELECT 1 AS FIRST_VALUE FROM DUAL; SELECT 2 AS SECOND_VALUE FROM DUAL"))
            await using (var reader = await command.ExecuteReaderAsync(Token).ConfigureAwait(false))
            {
                Require(await reader.ReadAsync(Token).ConfigureAwait(false) && await reader.GetFieldValueAsync<int>(0, Token).ConfigureAwait(false) == 1,
                    "first_rowset_invalid");
                Observation.Stage = "next_result_async";
                Require(await reader.NextResultAsync(Token).ConfigureAwait(false) &&
                    await reader.ReadAsync(Token).ConfigureAwait(false) && await reader.GetFieldValueAsync<int>(0, Token).ConfigureAwait(false) == 2 &&
                    !await reader.NextResultAsync(Token).ConfigureAwait(false), "next_rowset_invalid");
            }
            Require(Observation.CountOpcode(44) > moreStart, "next_result_wire_not_observed");
            cases["next_result_async"] = new { rowsets = 2, more_result_frames = Observation.CountOpcode(44) - moreStart, source = "T08_two_selects_target_evidence" };
            await TransactionsAsync(connection, table, cases).ConfigureAwait(false);
            await LobsAsync(connection, table, cases).ConfigureAwait(false);
            Stage = "fresh_connection_final_rows";
            await using (var observer = await OpenVerifiedAsync().ConfigureAwait(false))
            {
                int count = await CountRowsAsync(observer, table).ConfigureAwait(false);
                Require(count == 0, "unexpected_final_rows");
                Report["server_final_rows_before_cleanup"] = count;
            }
            Stage = "connection_close_async";
            await connection.CloseAsync().ConfigureAwait(false);
            Require(connection.State == ConnectionState.Closed, "connection_close_async_failed");
            cases["connection_close_dispose_async"] = true;
        }
        catch (Exception error) { workError = error; Report["work_failure_stage"] = Stage; }
        finally
        {
            Stage = "exact_cleanup_async";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync().ConfigureAwait(false))
                    if (await TableCountAsync(cleaner, table).ConfigureAwait(false) != 0)
                        await ExecAsync(cleaner, null, $"DROP TABLE {table}").ConfigureAwait(false);
                await using var verify = await OpenVerifiedAsync().ConfigureAwait(false);
                Require(await TableCountAsync(verify, table).ConfigureAwait(false) == 0, "owned_object_cleanup_unverified");
                Report["cleanup_verified"] = true;
                Report["final_database_state"] = "random_object_absent";
                Report["final_verification_identity"] = TestUser;
                Report["final_verification_schema"] = TestUser;
            }
            catch (Exception error) { cleanupError = error; Report["cleanup_error"] = SafeError(error); }
            Report["numeric_frame_observations"] = Observation!.Frames;
            Report["negotiated_encrypt_modes"] = Observation.EncryptModes;
        }
        if (workError != null) Report["work_error"] = SafeError(workError);
        if (cleanupError != null) throw new ProbeFailure("cleanup_failed");
        if (workError != null) { Stage = Convert.ToString(Report["work_failure_stage"], CultureInfo.InvariantCulture)!; throw workError; }
    }

    private static async Task TransactionsAsync(DmConnection c, string table, Dictionary<string, object?> cases)
    {
        Stage = "transaction_begin_commit_async";
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, Token).ConfigureAwait(false))
        {
            await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (101,'commit')").ConfigureAwait(false);
            await tx.CommitAsync(Token).ConfigureAwait(false);
        }
        await using (var observer = await OpenVerifiedAsync().ConfigureAwait(false))
            Require(await CountIdAsync(observer, table, 101).ConfigureAwait(false) == 1, "async_commit_not_visible");
        cases["transaction_begin_commit_async"] = new { fresh_committed_rows = 1 };
        Stage = "transaction_rollback_async";
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, Token).ConfigureAwait(false))
        {
            await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (102,'rollback')").ConfigureAwait(false);
            await tx.RollbackAsync(Token).ConfigureAwait(false);
        }
        await using (var observer = await OpenVerifiedAsync().ConfigureAwait(false))
            Require(await CountIdAsync(observer, table, 102).ConfigureAwait(false) == 0, "async_rollback_visible");
        cases["transaction_rollback_async"] = new { fresh_rolled_back_rows = 0 };
        Stage = "transaction_savepoints_async";
        string serverVersion = c.ServerVersion;
        bool savepointsSupported = serverVersion == "8.1.5.60";
        Require(savepointsSupported || Mode == "tls" && serverVersion == "8.1.4.6", "savepoint_server_profile_unverified");
        var rejections = new Dictionary<string, object?>();
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, Token).ConfigureAwait(false))
        {
            Require(tx.SupportsSavepoints == savepointsSupported, "savepoint_capability_profile_mismatch");
            await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (103,'before')").ConfigureAwait(false);
            if (savepointsSupported)
            {
                await tx.SaveAsync("T13 checkpoint", Token).ConfigureAwait(false);
                await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (104,'after')").ConfigureAwait(false);
                await tx.RollbackAsync("T13 checkpoint", Token).ConfigureAwait(false);
                Require(Convert.ToInt32(await ScalarAsync(c, tx, $"SELECT COUNT(*) FROM {table} WHERE ID=104").ConfigureAwait(false), CultureInfo.InvariantCulture) == 0,
                    "savepoint_rollback_failed");
                await tx.ReleaseAsync("T13 checkpoint", Token).ConfigureAwait(false);
                await tx.CommitAsync(Token).ConfigureAwait(false);
            }
            else
            {
                Require(tx is DmTransaction, "provider_transaction_required");
                var dmTx = (DmTransaction)tx;
                await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (104,'unsupported-profile')").ConfigureAwait(false);
                rejections["SaveAsync"] = await RejectSavepointAsync(() => tx.SaveAsync("T13 checkpoint", Token), dmTx, c).ConfigureAwait(false);
                rejections["RollbackAsync"] = await RejectSavepointAsync(() => tx.RollbackAsync("T13 checkpoint", Token), dmTx, c).ConfigureAwait(false);
                rejections["ReleaseAsync"] = await RejectSavepointAsync(() => tx.ReleaseAsync("T13 checkpoint", Token), dmTx, c).ConfigureAwait(false);
                await tx.RollbackAsync(Token).ConfigureAwait(false);
                Require(dmTx.Outcome == DmTransactionOutcome.RolledBack, "unsupported_savepoints_transaction_rollback_failed");
            }
        }
        await using (var observer = await OpenVerifiedAsync().ConfigureAwait(false))
            Require(await CountIdAsync(observer, table, 103).ConfigureAwait(false) == (savepointsSupported ? 1 : 0) &&
                await CountIdAsync(observer, table, 104).ConfigureAwait(false) == 0, "savepoint_fresh_rows_invalid");
        if (savepointsSupported)
            cases["transaction_savepoints_async"] = new { status = "supported_for_server_profile", server_version = serverVersion,
                supports_savepoints = true, fresh_retained_rows = 1, fresh_rolled_back_rows = 0 };
        else
            cases["transaction_savepoints_async"] = new { status = "unsupported_for_server_profile", server_version = serverVersion,
                supports_savepoints = false, rejections, post_rollback_outcome = "RolledBack", fresh_candidate_rows = 0, fresh_rolled_back_rows = 0 };
        Stage = "transaction_dispose_async_rollback";
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, Token).ConfigureAwait(false))
            await ExecAsync(c, tx, $"INSERT INTO {table}(ID,VAL) VALUES (105,'dispose')").ConfigureAwait(false);
        await using (var observer = await OpenVerifiedAsync().ConfigureAwait(false))
            Require(await CountIdAsync(observer, table, 105).ConfigureAwait(false) == 0, "transaction_dispose_async_left_row");
        cases["transaction_dispose_async_rollback"] = new { fresh_rolled_back_rows = 0 };
        await ExecAsync(c, null, $"DELETE FROM {table}").ConfigureAwait(false);
    }

    private static async Task<object> RejectSavepointAsync(Func<Task> operation, DmTransaction tx, DmConnection c)
    {
        int before = Observation!.Frames.Count;
        Exception? rejected = null;
        try { await operation().ConfigureAwait(false); }
        catch (Exception error) { rejected = error; }
        int newFrames = Observation.Frames.Count - before;
        Require(rejected?.GetType() == typeof(NotSupportedException), "savepoint_profile_exact_rejection_required");
        Require(newFrames == 0, "unsupported_savepoint_network_io_observed");
        Require(tx.Outcome == DmTransactionOutcome.Active && c.State == ConnectionState.Open,
            "unsupported_savepoint_changed_transaction_or_connection");
        return new { exception_type = rejected!.GetType().FullName, new_frames = newFrames,
            transaction_outcome = tx.Outcome.ToString(), connection_state = c.State.ToString() };
    }

    private static async Task LobsAsync(DmConnection c, string table, Dictionary<string, object?> cases)
    {
        Stage = "lob_parameter_upload_async";
        Require(await ExecAsync(c, null, $"INSERT INTO {table}(ID,B,C) VALUES (:p0,:p1,:p2)",
            (201, DmDbType.Int32), (Blob, DmDbType.Blob), (Clob, DmDbType.Clob)).ConfigureAwait(false) == 1, "lob_insert_count_invalid");
        cases["lob_parameter_upload_async"] = new { blob_bytes = Blob.Length, clob_utf16_units = Clob.Length };
        Stage = "lob_get_field_value_async";
        int lobStart = Observation!.CountOpcode(32);
        await using (var command = Command(c, null, $"SELECT B,C,VAL FROM {table} WHERE ID=201"))
        await using (var reader = await command.ExecuteReaderAsync(Token).ConfigureAwait(false))
        {
            Require(await reader.ReadAsync(Token).ConfigureAwait(false), "lob_row_missing");
            Observation.Stage = "lob_get_field_value_async";
            byte[] bytes = await reader.GetFieldValueAsync<byte[]>(0, Token).ConfigureAwait(false);
            string chars = await reader.GetFieldValueAsync<string>(1, Token).ConfigureAwait(false);
            Require(bytes.AsSpan().SequenceEqual(Blob) && chars == Clob && await reader.IsDBNullAsync(2, Token).ConfigureAwait(false),
                "async_lob_value_mismatch");
        }
        Require(Observation.CountOpcode(32) > lobStart, "network_lob_chunk_not_observed");
        cases["lob_get_field_value_async"] = new { blob_sha256 = Sha(Blob), clob_utf8_sha256 = Sha(Encoding.UTF8.GetBytes(Clob)),
            get_lob_data_frames = Observation.CountOpcode(32) - lobStart };
        Stage = "lob_execute_scalar_async";
        int scalarStart = Observation.CountOpcode(32);
        Require(await ScalarAsync(c, null, $"SELECT B FROM {table} WHERE ID=201").ConfigureAwait(false) is byte[] scalarBlob &&
            scalarBlob.AsSpan().SequenceEqual(Blob), "scalar_lob_value_invalid");
        Require(await ScalarAsync(c, null, $"SELECT C FROM {table} WHERE ID=201").ConfigureAwait(false) is string scalarClob && scalarClob == Clob,
            "scalar_clob_value_invalid");
        Require(Observation.CountOpcode(32) > scalarStart, "scalar_network_lob_chunk_not_observed");
        cases["lob_execute_scalar_async"] = new { get_lob_data_frames = Observation.CountOpcode(32) - scalarStart };
        await ExecAsync(c, null, $"DELETE FROM {table} WHERE ID=201").ConfigureAwait(false);
    }

    private static async Task<DmConnection> OpenVerifiedAsync()
    {
        var c = new DmConnection(Settings);
        try
        {
            int negotiated = Observation!.EncryptModes.Count;
            Observation.Stage = "open_async";
            await c.OpenAsync(Token).ConfigureAwait(false);
            Require(Observation.EncryptModes.Count > negotiated, "startup_transport_mode_not_observed");
            int mode = Observation.EncryptModes[^1];
            Require(Mode == "tls" ? mode == 1 : mode == 0, "target_transport_mode_mismatch");
            await using var command = Command(c, null, "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
            await using var reader = await command.ExecuteReaderAsync(Token).ConfigureAwait(false);
            Require(await reader.ReadAsync(Token).ConfigureAwait(false) &&
                await reader.GetFieldValueAsync<string>(0, Token).ConfigureAwait(false) == TestUser &&
                await reader.GetFieldValueAsync<string>(1, Token).ConfigureAwait(false) == TestUser,
                "test_identity_or_schema_mismatch");
            return c;
        }
        catch { await c.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static DmCommand Command(DmConnection c, DbTransaction? tx, string sql, params (object Value, DmDbType Type)[] values)
    {
        var command = new DmCommand(sql, c) { CommandTimeout = 30 };
        ((DbCommand)command).Transaction = tx;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.Add(new DmParameter("p" + index.ToString(CultureInfo.InvariantCulture), values[index].Type) { Value = values[index].Value });
        if (Observation != null) Observation.Stage = Stage;
        return command;
    }
    private static async Task<int> ExecAsync(DmConnection c, DbTransaction? tx, string sql, params (object Value, DmDbType Type)[] values)
    { await using var cmd = Command(c, tx, sql, values); return await cmd.ExecuteNonQueryAsync(Token).ConfigureAwait(false); }
    private static async Task<object?> ScalarAsync(DmConnection c, DbTransaction? tx, string sql, params (object Value, DmDbType Type)[] values)
    { await using var cmd = Command(c, tx, sql, values); return await cmd.ExecuteScalarAsync(Token).ConfigureAwait(false); }
    private static async Task<int> TableCountAsync(DmConnection c, string table) =>
        Convert.ToInt32(await ScalarAsync(c, null, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0", (table, DmDbType.VarChar)).ConfigureAwait(false), CultureInfo.InvariantCulture);
    private static async Task<int> CountRowsAsync(DmConnection c, string table) =>
        Convert.ToInt32(await ScalarAsync(c, null, $"SELECT COUNT(*) FROM {table}").ConfigureAwait(false), CultureInfo.InvariantCulture);
    private static async Task<int> CountIdAsync(DmConnection c, string table, int id) =>
        Convert.ToInt32(await ScalarAsync(c, null, $"SELECT COUNT(*) FROM {table} WHERE ID=:p0", (id, DmDbType.Int32)).ConfigureAwait(false), CultureInfo.InvariantCulture);
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static object SafeError(Exception error)
    {
        if (error is TargetInvocationException { InnerException: { } inner }) error = inner;
        return new { type = error.GetType().FullName, classification = error is ProbeFailure f ? f.Kind :
            error is OperationCanceledException ? "operation_canceled" : error is NotSupportedException ? "unsupported" :
            error is DmException ? "provider_error" : "unclassified_failure", server_number = error is DmException dm ? (int?)dm.Number : null };
    }
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }

    private sealed class Trace : IDisposable
    {
        private readonly FieldInfo FrameField;
        private readonly FieldInfo ModeField;
        private readonly object? PreviousFrame;
        private readonly object? PreviousMode;
        internal string Stage { get; set; } = "none";
        internal List<Frame> Frames { get; } = [];
        internal List<int> EncryptModes { get; } = [];
        internal Trace()
        {
            var assembly = typeof(DmConnection).Assembly;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            FrameField = assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")?.GetField("AfterFrame", flags)
                ?? throw new ProbeFailure("numeric_frame_hook_missing");
            ModeField = assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")?.GetField("AfterStartupNegotiatedEncryptMode", flags)
                ?? throw new ProbeFailure("numeric_transport_hook_missing");
            PreviousFrame = FrameField.GetValue(null);
            PreviousMode = ModeField.GetValue(null);
            Require(PreviousFrame == null && PreviousMode == null, "isolated_probe_hooks_required");
            FrameField.SetValue(null, (Action<short, short, int, int>)((request, response, sqlCode, length) =>
                Frames.Add(new Frame(Stage, request, response, sqlCode, length))));
            ModeField.SetValue(null, (Action<int>)(mode => EncryptModes.Add(mode)));
        }
        internal int CountOpcode(short opcode) => Frames.Count(f => f.RequestOpcode == opcode);
        public void Dispose() { FrameField.SetValue(null, PreviousFrame); ModeField.SetValue(null, PreviousMode); }
    }
    private sealed record Frame(string Stage, short RequestOpcode, short ResponseOpcode, int SqlCode, int BodyLength);
}
