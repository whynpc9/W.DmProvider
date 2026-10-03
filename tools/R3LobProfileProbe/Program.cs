using System.Data;
using System.Data.Common;
using System.Globalization;
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
    private const string Version = "0.1.0-r3.t16.20261002143244";
    private const string PackageHash = "a15d5009659e1f78f00a49efe7c1caf0cd43974b69169e405dfc794ef5275a44";
    private const string DllHash = "f70627e5d957f3de86ec33070474ab1543c162c5f6321f93312ad87c4535290f";
    private const string Mvid = "654b6ddd-b0ff-4408-8e1b-b93bd075c1da";
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T17-profile", ["phase"] = "capability_characterization",
        ["implementation"] = "W-package", ["accepted"] = false, ["status"] = "started", ["exit_code"] = -1,
        ["streaming_api_acceptance_claimed"] = false, ["execution_mode"] = "async_raw_single_frame_protocol",
        ["runtime"] = RuntimeInformation.FrameworkDescription, ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier,
        ["other_profile_charset_support_claimed"] = false
    };
    private static readonly Dictionary<string, object?> Cases = new();
    private static readonly List<Dictionary<string, object?>> Requests = [];
    private static string Mode = "", Settings = "", StageValue = "arguments";
    private static string? Output;
    private static Vector Input = null!;
    private static Observation? Observe;
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
            Mode = args[0]; Output = args[3]; Report["mode"] = Mode; Report["cases"] = Cases; Report["requests"] = Requests;
            Stage = "accepted_package_identity"; VerifyPackage(args[2]);
            Stage = "independent_vector_identity"; Input = LoadVector(Path.GetFullPath(args[1]));
            Require(typeof(AbstractLob).GetMethod("BeginPublicOperation", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                "reader_lob_invocation_surface_missing");
            Type protocol = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.B") ?? throw new ProbeFailure("protocol_surface_missing");
            Require(protocol.GetMethod("ReadLobAsync", BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(AbstractLob), typeof(long), typeof(int), typeof(CancellationToken)], null)?.ReturnType == typeof(Task<Data>),
                "raw_single_frame_async_surface_missing");
            Cases["offline_contract"] = new { category = "Contract", feature = "LobProfile", status = "passed", exact_accepted_package = true,
                independent_vector_checked = true, raw_single_frame_async_surface_checked = true, network_invoked = false };
            if (Mode == "offline") { Report["status"] = "offline_verified"; Report["integration"] = "integration_pending"; }
            else
            {
                Stage = "configure"; Configure(Path.GetFullPath(args[1]));
                using (Observe = new Observation()) await RunRealAsync();
                Report["status"] = "profile_characterized"; Report["integration"] = "real_test_schema";
            }
            code = 0; Report["accepted"] = true;
        }
        catch (Exception error)
        {
            bool pending = error is ProbeFailure { Kind: "integration_pending" };
            Report["status"] = pending ? "integration_pending" : "rejected";
            Report["integration"] = pending ? "integration_pending" : "not_accepted";
            Report["failed_stage"] = Report.TryGetValue("primary_failed_stage", out object? prior) ? prior : Stage;
            Report["error"] = SafeError(Unwrap(error)); code = pending ? 3 : 1;
        }
        Report["exit_code"] = code;
        try { Checkpoint(); Console.WriteLine(JsonSerializer.Serialize(Report)); }
        catch { Console.WriteLine("{\"task\":\"T17-profile\",\"status\":\"rejected\",\"classification\":\"report_write_failed\",\"exit_code\":1}"); return 1; }
        return code;
    }

    private static void VerifyPackage(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath)); var manifest = document.RootElement;
        Assembly assembly = typeof(DmConnection).Assembly;
        string requested = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "R3PackageVersion").Value ?? "";
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string sha = Hash(File.ReadAllBytes(assembly.Location));
        Require(requested == Version && actual == Version && manifest.GetProperty("version").GetString() == Version &&
            manifest.GetProperty("package_sha256").GetString() == PackageHash && sha == DllHash &&
            manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString() == DllHash &&
            assembly.ManifestModule.ModuleVersionId.ToString("D") == Mvid, "accepted_t16_package_identity_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "loaded_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3LobProfileProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + Version).GetProperty("type").GetString() == "package",
            "exact_package_reference_required");
        Require(!deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)),
            "official_dependency_rejected");
        Report["package_version"] = Version; Report["package_sha256"] = PackageHash; Report["loaded_assembly_sha256"] = sha;
        Report["loaded_assembly_mvid"] = Mvid; Report["reference_kind"] = "exact_PackageReference";
    }

    private static Vector LoadVector(string repo)
    {
        string path = Path.Combine(repo, "tests", "fixtures", "r3-lob", "vectors.json");
        byte[] bytes = File.ReadAllBytes(path); Require(bytes.Length <= 4 * 1024 * 1024, "fixture_size_bound");
        using var document = JsonDocument.Parse(bytes);
        JsonElement item = document.RootElement.GetProperty("out_row_profile_large");
        string value = item.GetProperty("value").GetString() ?? "";
        Require(value.Length <= 2 * 1024 * 1024 && new UTF8Encoding(false, true).GetByteCount(value) >= 128 * 1024, "large_profile_vector_required");
        int runes = RuneCount(value);
        string utf8Hash = Hash(new UTF8Encoding(false, true).GetBytes(value));
        Require(item.GetProperty("utf16_count").GetInt32() == value.Length && item.GetProperty("rune_count").GetInt32() == runes &&
            item.GetProperty("utf8_sha256").GetString()!.Equals(utf8Hash, StringComparison.OrdinalIgnoreCase) &&
            item.GetProperty("utf8_byte_count").GetInt32() == new UTF8Encoding(false, true).GetByteCount(value), "independent_vector_reference_mismatch");
        var markers = item.GetProperty("markers").EnumerateArray().Select(marker => new Marker(
            marker.GetProperty("utf16_offset").GetInt32(), marker.GetProperty("scalar_offset").GetInt32(), marker.GetProperty("unique_marker").GetString()!)).ToArray();
        Require(markers.Length >= 3 && markers.Count(m => m.Utf16 > m.Scalar) >= 3 && markers.All(m => m.Utf16 >= m.Scalar && m.Utf16 >= 0 &&
            value.AsSpan(m.Utf16).StartsWith(m.Text, StringComparison.Ordinal) && RuneCount(value[..m.Utf16]) == m.Scalar) &&
            markers.Select(m => m.Text).Distinct(StringComparer.Ordinal).Count() == markers.Length, "independent_marker_reference_mismatch");
        Require(value.Contains('\0') && value.EnumerateRunes().Any(r => r.Value > 0xffff) && value.Any(c => c is >= '\u0300' and <= '\u036f'),
            "unicode_nul_supplementary_combining_controls_required");
        Report["fixture_sha256"] = Hash(bytes); Report["expected_utf16_code_units"] = value.Length;
        Report["accepted_source_manifest_sha256"] = Hash(File.ReadAllBytes(Path.Combine(repo, "docs", "implementation", "evidence", "T16", "accepted-v3", "accepted-source-manifest.json")));
        Report["expected_rune_count"] = runes; Report["expected_utf8_sha256"] = utf8Hash;
        Report["expected_utf8_byte_count"] = new UTF8Encoding(false, true).GetByteCount(value);
        Report["independent_marker_count"] = markers.Length;
        return new Vector(value, runes, utf8Hash, markers);
    }

    private static void Configure(string repo)
    {
        string raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var builder = new DmConnectionStringBuilder(raw);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        builder.Schema = TestUser; builder.Pooling = false; builder.StmtPooling = false; builder.PreparePooling = false;
        builder.PersistSecurityInfo = false; builder.LogLevel = LogLevel.OFF; builder.ConnectTimeout = TimeSpan.FromSeconds(15); builder.CommandTimeout = 30;
        Require(builder.LobMode != 2, "automatic_full_lob_fetch_disallowed");
        builder.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        if (Mode == "tls")
        {
            Require(builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_tls_target_required");
            string certificates = Path.Combine(repo, ".local", "t07", "certs", "client_ssl", TestUser);
            builder.TlsCaCertificatePath = Path.Combine(certificates, "ca-cert.pem"); builder.TlsClientCertificatePath = Path.Combine(certificates, "client-cert.pem");
            builder.TlsClientPrivateKeyPath = Path.Combine(certificates, "client-key.pem"); builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Settings = builder.ConnectionString; Report["explicit_transport"] = builder.TransportSecurity.ToString();
        Report["pooling_enabled"] = false; Report["persist_security_info"] = false;
    }

    private static async Task RunRealAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        string clob = "T17C_" + suffix, nclob = "T17N_" + suffix;
        Report["owned_objects"] = new[] { clob, nclob }; Report["final_database_state"] = "unverified";
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        Exception? workFailure = null, cleanupFailure = null;
        try
        {
            Stage = "create_and_upload_clob"; await CreateAndUploadAsync(clob, "CLOB", attempted);
            Stage = "clob_raw_protocol_characterization";
            await CharacterizeAsync(clob, "CLOB");
            Stage = "optional_nclob_declaration";
            bool available = true;
            try { await CreateAndUploadAsync(nclob, "NCLOB", attempted); }
            catch (DmException error) when (error.FailureInfo is { ErrorKind: DmErrorKind.Server, OperationOutcome: DmOperationOutcome.ServerReported,
                ServerErrorNumber: not null })
            {
                // Only a declaration rejection is an unsupported profile capability. Upload failures are never reclassified.
                if (Stage != "declare_NCLOB") throw;
                available = false; Cases["NCLOB"] = new { category = "state_evidence", status = "unsupported_by_profile", declaration = "NCLOB", error = SafeError(error) };
            }
            if (available)
            {
                Stage = "nclob_raw_protocol_characterization"; await CharacterizeAsync(nclob, "NCLOB");
                Report["nclob_support_scope"] = "this_exact_server_profile_and_observed_codec_only_no_public_national_type_inference";
            }
        }
        catch (Exception error) { workFailure = Unwrap(error); Report["work_error"] = SafeError(workFailure); Report["primary_failed_stage"] = Stage; }
        finally
        {
            Stage = "fresh_owned_cleanup";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync())
                    foreach (string name in attempted)
                        if (await ObjectCountAsync(cleaner, name) == 1) await ExecAsync(cleaner, "DROP TABLE " + name);
                await using (var fresh = await OpenVerifiedAsync())
                    Require(await ObjectCountAsync(fresh, clob) == 0 && await ObjectCountAsync(fresh, nclob) == 0, "owned_object_absence_failed");
                Report["cleanup_verified"] = true; Report["final_database_state"] = "unique_owned_objects_absent";
                Report["final_verification_identity"] = TestUser; Report["final_verification_schema"] = TestUser;
            }
            catch (Exception error) { cleanupFailure = Unwrap(error); Report["cleanup_error"] = SafeError(cleanupFailure); }
        }
        Report["physical_connections_created"] = Observe!.Created; Report["physical_connections_disposed"] = Observe.Disposed;
        Report["negotiated_encrypt_modes"] = Observe.Modes; Report["network_io_counts"] = Observe.Counts;
        Exception? accountingFailure = null;
        try
        {
            Require(Observe.Created == Observe.Disposed && Observe.Created == Observe.Modes.Count && Observe.Created > 0, "physical_connection_accounting_failed");
            Require(Observe.Modes.All(m => m == (Mode == "tls" ? 1 : 0)), "explicit_transport_mode_mismatch");
            Require(Observe.AsyncOnly(Mode == "tls"), "async_raw_probe_used_synchronous_network");
        }
        catch (Exception error) { accountingFailure = error; Report["secondary_error"] = SafeError(error); }
        if (workFailure != null) throw workFailure;
        if (cleanupFailure != null) { Report["primary_failed_stage"] = "fresh_owned_cleanup"; throw cleanupFailure; }
        if (accountingFailure != null) throw accountingFailure;
    }

    private static async Task CreateAndUploadAsync(string name, string declaration, HashSet<string> attempted)
    {
        await using var connection = await OpenVerifiedAsync();
        Require(await ObjectCountAsync(connection, name) == 0, "unique_object_already_exists");
        Stage = "declare_" + declaration; attempted.Add(name);
        await ExecAsync(connection, $"CREATE TABLE {name}(ID INT PRIMARY KEY,V {declaration})");
        Stage = "upload_" + declaration + "_existing_string_parameter";
        await using var insert = new DmCommand($"INSERT INTO {name}(ID,V) VALUES(1,:p0)", connection) { CommandTimeout = 30 };
        insert.Parameters.Add(new DmParameter("p0", DmDbType.Clob) { Value = Input.Value });
        Require(await insert.ExecuteNonQueryAsync() == 1, "existing_string_parameter_upload_failed");
    }

    private static async Task CharacterizeAsync(string table, string declaration)
    {
        var result = new Dictionary<string, object?> { ["category"] = "state_evidence", ["status"] = "started", ["declared_type"] = declaration,
            ["upload_path"] = "existing_string_Clob_parameter", ["protocol_path"] = "ReadLobAsync_AbstractLob_single_frame" };
        Cases[declaration] = result; Checkpoint();
        await using (var context = await LobContext.OpenAsync(table))
        {
            result["column_metadata"] = context.Metadata(); result["get_lob_len"] = await context.LengthAsync();
            Require(!context.Lob.local && context.Lob.storageType != AbstractLob.STORAGE_IN_ROW, "out_row_locator_required");
        }
        var candidates = new List<string>();
        foreach (string candidate in new[] { "unicode_scalar", "utf16_code_unit", "encoded_byte" })
        {
            bool matches = true;
            int discriminatingMarkers = 0;
            foreach (Marker marker in Input.Markers)
            {
                await using var context = await LobContext.OpenAsync(table);
                long offset = candidate switch { "unicode_scalar" => marker.Scalar, "utf16_code_unit" => marker.Utf16,
                    _ => context.Encoding.GetByteCount(Input.Value.AsSpan(0, marker.Utf16)) };
                long reportedLength = await context.LengthAsync();
                if (offset >= reportedLength)
                {
                    Requests.Add(new Dictionary<string, object?> { ["declared_type"] = declaration, ["purpose"] = "marker_unit_probe_" + candidate,
                        ["request_offset"] = offset, ["status"] = "beyond_reported_length_not_used_as_unit_evidence", ["get_lob_len"] = reportedLength });
                    continue;
                }
                int length = candidate == "encoded_byte" ? context.Encoding.GetByteCount(marker.Text) : marker.Text.Length;
                Data frame = await context.ReadAsync(offset, length);
                string? decoded = null;
                try { decoded = context.Encoding.GetString(frame.value ?? []); }
                catch (DecoderFallbackException) { }
                bool exactPrefix = decoded != null && decoded.Length >= marker.Text.Length && decoded.StartsWith(marker.Text, StringComparison.Ordinal) &&
                    Input.Value.AsSpan(marker.Utf16).StartsWith(decoded, StringComparison.Ordinal);
                RecordFrame(context, declaration, "marker_unit_probe_" + candidate, offset, length, frame, decoded, exactPrefix);
                matches &= exactPrefix;
                if (exactPrefix && marker.Utf16 > marker.Scalar) discriminatingMarkers++;
            }
            matches &= discriminatingMarkers >= 3;
            result[candidate + "_marker_probes_match"] = matches;
            result[candidate + "_discriminating_markers"] = discriminatingMarkers;
            if (matches) candidates.Add(candidate);
        }
        result["independently_matched_offset_units"] = candidates;
        Checkpoint();
        await using var scan = await LobContext.OpenAsync(table);
        long total = await scan.LengthAsync();
        Require(total >= 0, "negative_get_lob_length");
        long offsetNow = 0; int utf16Now = 0, runeNow = 0;
        bool anyMissingLength = false, missingNonterminalLength = false, allMetadataLength = true, reachedEnd = false;
        bool terminalWireLengthMissing = false;
        int blocks = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int requestLength = Math.Min(4093, scan.MaximumRequest);
        Require(requestLength > 0, "negotiated_chunk_limit_invalid");
        while (blocks++ < 2048)
        {
            Data frame = await scan.ReadAsync(offsetNow, requestLength);
            Require(frame.len >= -1, "invalid_wire_length_metadata");
            string decoded;
            try { decoded = scan.Encoding.GetString(frame.value ?? []); }
            catch (DecoderFallbackException)
            {
                RecordFrame(scan, declaration, "full_exact_content_scan", offsetNow, requestLength, frame, null, false);
                result["status"] = "not_proven"; result["streaming_support_decision"] = "frame_decode_or_existing_input_requires_followup";
                throw;
            }
            bool matches = utf16Now <= Input.Value.Length && Input.Value.AsSpan(utf16Now).StartsWith(decoded, StringComparison.Ordinal);
            RecordFrame(scan, declaration, "full_exact_content_scan", offsetNow, requestLength, frame, decoded, matches);
            if (!matches) { result["status"] = "not_proven"; result["streaming_support_decision"] = "existing_input_or_offset_semantics_requires_followup"; }
            Require(matches, "existing_upload_or_raw_read_unicode_content_mismatch");
            int scalarCount = RuneCount(decoded);
            digest.AppendData(new UTF8Encoding(false, true).GetBytes(decoded));
            utf16Now += decoded.Length; runeNow += scalarCount;
            long advance;
            if (frame.len >= 0) advance = frame.len;
            else
            {
                anyMissingLength = true; allMetadataLength = false;
                if (scan.Lob.readOver)
                {
                    // EOF needs no next request offset. Preserve that the terminal advance was not supplied;
                    // never replace the missing wire fact with a decoded character or byte count.
                    terminalWireLengthMissing = true;
                    advance = 0;
                }
                else
                {
                    missingNonterminalLength = true;
                    Require(candidates.Count == 1, "missing_wire_length_offset_unit_not_proven");
                    advance = candidates[0] switch { "unicode_scalar" => scalarCount, "utf16_code_unit" => decoded.Length,
                        _ => frame.value?.Length ?? 0 };
                }
            }
            Require(advance >= 0 && (scan.Lob.readOver || advance > 0), "nonterminal_read_did_not_advance");
            offsetNow = checked(offsetNow + advance);
            if (scan.Lob.readOver) { reachedEnd = true; break; }
            Require(frame.value is { Length: > 0 } && decoded.Length > 0, "nonterminal_empty_chunk_not_eof");
        }
        string actualHash = Convert.ToHexStringLower(digest.GetHashAndReset());
        result["scan_utf16_code_units"] = utf16Now; result["scan_rune_count"] = runeNow; result["scan_utf8_sha256"] = actualHash;
        result["final_wire_offset"] = terminalWireLengthMissing ? null : (object)offsetNow;
        result["known_wire_advance_sum"] = offsetNow; result["terminal_wire_length_missing"] = terminalWireLengthMissing;
        result["terminal_read_over"] = reachedEnd;
        result["wire_length_metadata_always_present"] = allMetadataLength; result["wire_length_metadata_missing"] = anyMissingLength;
        result["progress_strategy"] = missingNonterminalLength ? "same_profile_marker_proven_" + candidates.Single() : "nonnegative_Data_len";
        result["offset_unit"] = missingNonterminalLength ? candidates.Single() : "opaque_server_units";
        result["progress_requires_updated_locator"] = true;
        result["absolute_seek_proven"] = false;
        result["get_lob_len_matches_scalar_count"] = total == Input.Runes;
        result["get_lob_len_matches_utf16_count"] = total == Input.Value.Length;
        result["get_lob_len_matches_final_wire_offset"] = terminalWireLengthMissing ? null : (object)(total == offsetNow);
        result["get_lob_len_matches_encoded_byte_count"] = total == scan.Encoding.GetByteCount(Input.Value);
        result["wire_length_ledger_complete"] = allMetadataLength;
        bool completeInput = reachedEnd && utf16Now == Input.Value.Length && runeNow == Input.Runes && actualHash == Input.Utf8Hash;
        result["complete_input_matches"] = completeInput;
        Require(completeInput, "complete_unicode_roundtrip_or_wire_length_not_proven");
        Require(allMetadataLength && total == offsetNow, "get_lob_length_wire_advance_ledger_not_proven");
        result["status"] = "characterized";
        result["streaming_support_decision"] = "same_profile_progress_and_codec_proven_requires_new_stream_implementation";
        Checkpoint();
    }

    private static void RecordFrame(LobContext context, string declaration, string purpose, long offset, int requested, Data frame, string? decoded, bool matches)
    {
        Require(Requests.Count < 4096, "observation_count_bound");
        Requests.Add(new Dictionary<string, object?> { ["declared_type"] = declaration, ["purpose"] = purpose,
            ["request_offset"] = offset, ["request_length"] = requested, ["reply_byte_count"] = frame.value?.Length ?? 0,
            ["data_len"] = frame.len, ["read_over"] = context.Lob.readOver, ["decoded_utf16_count"] = decoded?.Length,
            ["decoded_rune_count"] = decoded != null ? RuneCount(decoded) : null, ["strict_decode"] = decoded != null, ["independent_content_matches"] = matches });
        Checkpoint();
    }

    private sealed class LobContext : IAsyncDisposable
    {
        private readonly DmConnection connection;
        private readonly DmCommand command;
        private readonly DmDataReader reader;
        private readonly object protocol, properties;
        internal readonly DmClob Lob;
        internal readonly Encoding Encoding;
        internal int MaximumRequest => Convert.ToInt32(Property(properties, "MaxLobDataLenPerMsg"), CultureInfo.InvariantCulture);
        private LobContext(DmConnection connection, DmCommand command, DmDataReader reader)
        {
            this.connection = connection; this.command = command; this.reader = reader;
            Lob = reader.GetClob(0);
            object instance = Field(Lob, typeof(AbstractLob), "ConnInstance");
            protocol = Invoke(instance, "GetCsi", []); properties = Property(instance, "ConnProperty");
            Encoding = System.Text.Encoding.GetEncoding(Lob.serverEncoding, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        internal static async Task<LobContext> OpenAsync(string table)
        {
            var connection = await OpenVerifiedAsync();
            var command = new DmCommand($"SELECT V FROM {table} WHERE ID=1", connection) { CommandTimeout = 30 };
            DmDataReader? reader = null;
            try
            {
                reader = (DmDataReader)await command.ExecuteReaderAsync(); Require(await reader.ReadAsync(), "uploaded_lob_row_missing");
                return new LobContext(connection, command, reader);
            }
            catch { if (reader != null) await reader.DisposeAsync(); await command.DisposeAsync(); await connection.DisposeAsync(); throw; }
        }
        internal object Metadata()
        {
            var columns = (Array)Field(reader, typeof(DmDataReader), "m_ColInfo"); object column = columns.GetValue(0)!;
            string typeName = (string)Invoke(column, "GetTypeName", []);
            return new { type_name = SafeType(typeName), public_type_name = SafeType(reader.GetDataTypeName(0)),
                c_type = Convert.ToInt32(Invoke(column, "GetCType", []), CultureInfo.InvariantCulture),
                scale = Convert.ToInt32(Invoke(column, "GetScale", []), CultureInfo.InvariantCulture),
                is_lob = (bool)Invoke(column, "GetIsLob", []), type_flag = Convert.ToInt32(Invoke(column, "GetTypeFlag", []), CultureInfo.InvariantCulture),
                server_encoding = Encoding.WebName, encoding_code_page = Encoding.CodePage, msg_version = Convert.ToInt32(Field(properties, properties.GetType(), "msgVersion"), CultureInfo.InvariantCulture),
                long_lob_flag = (bool)Property(properties, "LongLobFlag"), new_lob_flag = (bool)Property(properties, "NewLobFlag"),
                storage_type = Lob.storageType, local = Lob.local, lob_flag = Lob.lobFlag, negotiated_max_lob_message_length = MaximumRequest };
        }
        internal async Task<long> LengthAsync() => (long)await RawAsync("GetLobLengthAsync", [Lob, CancellationToken.None],
            [typeof(AbstractLob), typeof(CancellationToken)]);
        internal async Task<Data> ReadAsync(long offset, int length) => (Data)await RawAsync("ReadLobAsync", [Lob, offset, length, CancellationToken.None],
            [typeof(AbstractLob), typeof(long), typeof(int), typeof(CancellationToken)]);
        private async Task<object> RawAsync(string name, object[] arguments, Type[] signature)
        {
            // Begin is synchronous in this caller's flow so AsyncLocal ownership survives through protocol await.
            using var invocation = (IDisposable)typeof(AbstractLob).GetMethod("BeginPublicOperation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Lob, null)!;
            var method = protocol.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, signature, null)
                ?? throw new ProbeFailure("raw_protocol_method_missing");
            Task task = (Task)method.Invoke(protocol, arguments)!;
            await task.ConfigureAwait(false);
            Invoke(invocation, "Complete", []);
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        public async ValueTask DisposeAsync()
        {
            try { await reader.DisposeAsync(); }
            finally { try { await command.DisposeAsync(); } finally { await connection.DisposeAsync(); } }
        }
    }

    private static async Task<DmConnection> OpenVerifiedAsync()
    {
        var connection = new DmConnection(Settings);
        try
        {
            await connection.OpenAsync();
            Require(Observe != null && Observe.Modes.Count > 0 && Observe.Modes[^1] == (Mode == "tls" ? 1 : 0), "observed_startup_transport_mode_required");
            await using var command = new DmCommand("SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL", connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync();
            Require(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "test_identity_or_schema_mismatch");
            Require(Regex.IsMatch(connection.ServerVersion, @"^\d+(\.\d+){1,5}$"), "safe_numeric_server_version_required");
            if (Report.TryGetValue("server_version", out object? previous)) Require((string?)previous == connection.ServerVersion, "server_profile_changed");
            Report["server_identity"] = TestUser; Report["server_schema"] = TestUser; Report["server_version"] = connection.ServerVersion;
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task ExecAsync(DmConnection connection, string sql)
    { await using var command = new DmCommand(sql, connection) { CommandTimeout = 30 }; await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ObjectCountAsync(DmConnection connection, string name)
    {
        await using var command = new DmCommand("SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0", connection) { CommandTimeout = 30 };
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = name });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
    private static object Invoke(object target, string name, object[] args) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, args)!;
    private static object Property(object target, string name) => target.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object Field(object target, Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static string SafeType(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9_ ()]{0,64}$") ? value : "unrecognized_type_name";
    private static int RuneCount(string value) { int count = 0; foreach (Rune _ in value.EnumerateRunes()) count++; return count; }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Exception Unwrap(Exception error) => error is TargetInvocationException { InnerException: not null } wrapper ? Unwrap(wrapper.InnerException) : error;
    private static object SafeError(Exception error) => new { type = error.GetType().FullName,
        classification = error is ProbeFailure failure ? failure.Kind : error is DecoderFallbackException ? "strict_decode_rejected" :
            error is DmException ? "provider_error" : error is OperationCanceledException ? "operation_canceled" : "unclassified_failure",
        failure_kind = (error as DmException)?.FailureInfo?.ErrorKind.ToString(), operation_outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString(),
        failure_phase = (error as DmException)?.FailureInfo?.Phase.ToString(), driver_code = (error as DmException)?.DriverErrorCode,
        number = error is DmException dm ? (int?)dm.Number : null, server_number = (error as DmException)?.FailureInfo?.ServerErrorNumber,
        reusable = (error as DmException)?.FailureInfo?.ConnectionReusable };
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
    private sealed record Marker(int Utf16, int Scalar, string Text);
    private sealed record Vector(string Value, int Runes, string Utf8Hash, Marker[] Markers);

    private sealed class Observation : IDisposable
    {
        private readonly Type transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!;
        private readonly FieldInfo modeHook, ioHook;
        private readonly object gate = new();
        private readonly Dictionary<string, long> counts = new();
        private readonly long initialCreated, initialDisposed;
        private bool unknownOperation;
        internal readonly List<int> Modes = [];
        internal Observation()
        {
            Type wire = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!;
            modeHook = wire.GetField("AfterStartupNegotiatedEncryptMode", BindingFlags.Static | BindingFlags.NonPublic)!;
            ioHook = transport.GetField("BeforeNetworkIo", BindingFlags.Static | BindingFlags.NonPublic)!;
            Require(modeHook != null && ioHook != null && modeHook.GetValue(null) == null && ioHook.GetValue(null) == null, "isolated_observation_hooks_required");
            foreach (string operation in new[] { "connect", "tls", "send", "receive" })
            { counts["async_" + operation] = 0; counts["sync_" + operation] = 0; }
            initialCreated = Counter("CreatedTcpSockets"); initialDisposed = Counter("DisposedTcpSockets");
            modeHook.SetValue(null, (Action<int>)(mode => Modes.Add(mode)));
            ioHook.SetValue(null, (Action<bool, string>)((asynchronous, operation) =>
            {
                lock (gate)
                {
                    string key = (asynchronous ? "async_" : "sync_") + operation;
                    if (counts.ContainsKey(key)) counts[key]++; else unknownOperation = true;
                }
            }));
        }
        private long Counter(string name) => (long)transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        internal long Created => Counter("CreatedTcpSockets") - initialCreated;
        internal long Disposed => Counter("DisposedTcpSockets") - initialDisposed;
        internal Dictionary<string, long> Counts { get { lock (gate) return new(counts); } }
        internal bool AsyncOnly(bool tls)
        {
            lock (gate) return !unknownOperation && counts.Where(p => p.Key.StartsWith("sync_", StringComparison.Ordinal)).All(p => p.Value == 0) &&
                counts["async_connect"] > 0 && counts["async_send"] > 0 && counts["async_receive"] > 0 &&
                (tls ? counts["async_tls"] > 0 : counts["async_tls"] == 0);
        }
        public void Dispose() { modeHook.SetValue(null, null); ioHook.SetValue(null, null); }
    }
}
