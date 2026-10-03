using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Net.Sockets;
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
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const long BlobBytes = 64L * 1024 * 1024 + 1024;
    private const int BlobBufferBytes = 32 * 1024, TextCycles = 10, TextBufferChars = 4093;
    private const int CommandSeconds = 300, ConnectSeconds = 15, ProcessSeconds = 1800;
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["task"] = "T17", ["implementation"] = "W-package", ["accepted"] = false,
        ["status"] = "started", ["exit_code"] = -1, ["measured_reused_sessions"] = 0, ["session_reset_verified"] = false,
        ["reuse_policy"] = "discard", ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["runtime_identifier"] = RuntimeInformation.RuntimeIdentifier
    };
    private static readonly Dictionary<string, object?> Cases = new();
    private static string Mode = "", Settings = "", StageValue = "arguments", Table = "";
    private static string? Output;
    private static TextReference Text = null!;
    private static Observation Observe = null!;
    private static DmDataSource Source = null!;
    private static string ExpectedBlobHash = "";
    private static string Stage
    {
        get => StageValue;
        set { StageValue = value; Report["checkpoint_stage"] = value; Checkpoint(); }
    }
    private static void Checkpoint()
    {
        if (Output == null) return;
        File.WriteAllText(Output + ".tmp", JsonSerializer.Serialize(Report)); File.Move(Output + ".tmp", Output, true);
    }
    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        try
        {
            Require(args.Length == 4 && args[0] is "offline" or "shared" or "tls", "usage");
            Mode = args[0]; Output = args[3]; Report["mode"] = Mode; Report["cases"] = Cases;
            Stage = "exact_package_identity"; VerifyPackage(args[2]);
            Stage = "fixed_input_manifest"; LoadInputs(Path.GetFullPath(args[1]));
            Require(typeof(DmDataReader).GetMethod("GetStream", [typeof(int)])?.DeclaringType == typeof(DmDataReader) &&
                typeof(DmDataReader).GetMethod("GetTextReader", [typeof(int)])?.DeclaringType == typeof(DmDataReader), "public_stream_overrides_required");
            Pass("offline_public_contract", "Contract", "LOB-01,LOB-02", new { public_stream_overrides_verified = true,
                independent_generators_defined = true, network_invoked = false, database_streaming_claimed = false });
            if (Mode == "offline")
            { Report["status"] = "offline_verified"; Report["integration"] = "integration_pending"; }
            else
            {
                Stage = "configure"; Configure(Path.GetFullPath(args[1]));
                using (Observe = new Observation())
                await using (Source = new DmDataSource(Settings)) await RunRealAsync();
                Report["status"] = "streaming_verified"; Report["integration"] = "real_test_schema";
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
        catch { Console.WriteLine("{\"task\":\"T17\",\"status\":\"rejected\",\"classification\":\"report_write_failed\",\"exit_code\":1}"); return 1; }
        return code;
    }

    private static void VerifyPackage(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var manifest = document.RootElement;
        Assembly assembly = typeof(DmConnection).Assembly;
        string expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "R3PackageVersion").Value ?? "";
        string actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string hash = Hash(File.ReadAllBytes(assembly.Location));
        Require(expected == actual && expected == manifest.GetProperty("version").GetString() &&
            hash == manifest.GetProperty("assets").GetProperty("lib/net10.0/W.DmProvider.dll").GetString(), "exact_package_identity_mismatch");
        Require(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "W.DmProvider.dll")), "loaded_location_mismatch");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R3LobProbe.deps.json")));
        Require(deps.RootElement.GetProperty("libraries").GetProperty("W.DmProvider/" + expected).GetProperty("type").GetString() == "package" &&
            !deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)), "package_reference_required");
        Report["package_version"] = expected; Report["package_sha256"] = manifest.GetProperty("package_sha256").GetString();
        Report["loaded_assembly_sha256"] = hash; Report["loaded_assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D");
        Report["reference_kind"] = "exact_PackageReference";
    }
    private static void LoadInputs(string repo)
    {
        byte[] fixture = File.ReadAllBytes(Path.Combine(repo, "tests", "fixtures", "r3-lob", "vectors.json"));
        using var document = JsonDocument.Parse(fixture); var large = document.RootElement.GetProperty("out_row_profile_large");
        string value = large.GetProperty("value").GetString()!;
        Require(value.Length == large.GetProperty("utf16_count").GetInt32() &&
            value.EnumerateRunes().Count() == large.GetProperty("rune_count").GetInt32() &&
            Hash(new UTF8Encoding(false, true).GetBytes(value)) == large.GetProperty("utf8_sha256").GetString(), "independent_fixture_mismatch");
        var binary = document.RootElement.GetProperty("large_logical_stream_descriptor").GetProperty("generator");
        Require(binary.GetProperty("algorithm").GetString() == "byte_cycle_v1" && binary.GetProperty("seed").GetInt32() == 17 &&
            binary.GetProperty("stride").GetInt32() == 131, "independent_blob_pattern_mismatch");
        Text = new TextReference(value, large.GetProperty("rune_count").GetInt32(), TextCycles);
        Require(Text.Length > 1024 * 1024, "large_text_required");
        ExpectedBlobHash = BlobReferenceHash();
        Report["fixture_sha256"] = Hash(fixture);
        Report["input_manifest"] = new { blob_bytes = BlobBytes, blob_seed = 17, blob_stride = 131, blob_buffer_bytes = BlobBufferBytes,
            materialized_blob_limit_bytes = 64 * 1024 * 1024, expected_blob_sha256 = ExpectedBlobHash,
            clob_utf16_chars = Text.Length, nclob_utf16_chars = Text.Length, text_cycles = TextCycles,
            expected_text_runes = Text.Runes, expected_text_utf8_sha256 = Text.Hash, text_read_buffer_chars = TextBufferChars,
            command_total_seconds = CommandSeconds, connect_seconds = ConnectSeconds, process_total_seconds = ProcessSeconds,
            inputs_unknown_length = true, inputs_nonseekable = true, caller_owns_inputs = true,
            one_gib_database_claimed = false, one_gib_scope = "independent_offline_LobTests",
            malformed_reply_and_truncation_scope = "independent_offline_LobTests_not_injected_database_packets" };
    }
    private static void Configure(string repo)
    {
        string raw = Environment.GetEnvironmentVariable(Mode == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var builder = new DmConnectionStringBuilder(raw);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        builder.Schema = TestUser; builder.Pooling = true; builder.MaxPoolSize = 1; builder.MaxPoolWaiters = 4;
        builder.PoolAcquireTimeout = TimeSpan.FromSeconds(15); builder.ConnectTimeout = TimeSpan.FromSeconds(ConnectSeconds);
        builder.CommandTimeout = CommandSeconds; builder.PersistSecurityInfo = false; builder.StmtPooling = false; builder.PreparePooling = false;
        builder.LogLevel = LogLevel.OFF; builder.TransportSecurity = Mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        if (Mode == "tls")
        {
            Require(builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_tls_target_required");
            string certificates = Path.Combine(repo, ".local", "t07", "certs", "client_ssl", TestUser);
            builder.TlsCaCertificatePath = Path.Combine(certificates, "ca-cert.pem"); builder.TlsClientCertificatePath = Path.Combine(certificates, "client-cert.pem");
            builder.TlsClientPrivateKeyPath = Path.Combine(certificates, "client-key.pem"); builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_ephemeral_CA_without_CRL";
        }
        Settings = builder.ConnectionString; Report["explicit_transport"] = builder.TransportSecurity.ToString();
    }

    private static async Task RunRealAsync()
    {
        Table = "T17S_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        Report["owned_objects"] = new[] { Table }; Report["final_database_state"] = "unverified";
        bool attempted = false; Exception? workFailure = null, cleanupFailure = null;
        string activeWorkCase = "create_unique_table";
        void WorkStage(string value) { activeWorkCase = value; Report["active_work_case"] = value; Stage = value; }
        try
        {
            WorkStage("create_unique_table");
            await using (var setup = await OpenVerifiedAsync())
            {
                Require(await ObjectCountAsync(setup) == 0, "unique_object_already_exists"); attempted = true;
                await ExecAsync(setup, $"CREATE TABLE {Table}(ID INT PRIMARY KEY,B BLOB,C CLOB,N NCLOB)");
            }
            WorkStage("LOB05_blob_input_and_repeat"); await UploadBlobAsync();
            WorkStage("LOB05_clob_and_nclob_inputs"); await UploadTextAsync("C"); await UploadTextAsync("N");
            WorkStage("LOB05_small_empty_exact_short_inputs"); await SmallInputsAsync();
            WorkStage("LOB01_oversize_full_api_guard"); await MaterializationGuardAsync();
            WorkStage("LOB01_blob_public_stream"); await ReadBlobAsync();
            WorkStage("LOB02_clob_public_text_reader"); await ReadTextAsync("C");
            WorkStage("LOB02_nclob_public_text_reader"); await ReadTextAsync("N");
            WorkStage("LOB03_ranges_and_zero_length"); await RangesAsync();
            WorkStage("legacy_40k_array_string_regression"); await LegacyRegressionAsync();
            WorkStage("LOB04_parent_lifecycle"); await LifecycleAsync();
            WorkStage("LOB07_explicit_input_type"); await RejectWrongTypeAsync();
            WorkStage("LOB06_input_exception_no_replay"); await InputExceptionAsync();
            WorkStage("LOB06_upload_cancel_no_replay"); await UploadCancellationAsync();
            WorkStage("LOB06_read_cancel_no_replay"); await ReadCancellationAsync();
        }
        catch (Exception error)
        {
            workFailure = Unwrap(error);
            Report["work_error"] = SafeError(workFailure);
            Report["primary_failed_stage"] = activeWorkCase;
            Report["work_failed_case"] = activeWorkCase;
            Report["work_failed_checkpoint_stage"] = Stage;
            // Immutable numeric snapshot before cleanup performs any new protocol work.
            Report["work_failure_protocol_observation"] = Observe.FailureSnapshot();
        }
        finally
        {
            Stage = "fresh_owned_cleanup";
            try
            {
                await using (var cleaner = await OpenVerifiedAsync())
                    if (attempted && await ObjectCountAsync(cleaner) == 1) await ExecAsync(cleaner, "DROP TABLE " + Table);
                await using (var fresh = await OpenVerifiedAsync()) Require(await ObjectCountAsync(fresh) == 0, "owned_object_absence_failed");
                Report["cleanup_verified"] = true; Report["final_database_state"] = "unique_owned_objects_absent";
                Report["final_verification_identity"] = TestUser; Report["final_verification_schema"] = TestUser;
            }
            catch (Exception error) { cleanupFailure = Unwrap(error); Report["cleanup_error"] = SafeError(cleanupFailure); }
        }
        Report["physical_connections_created"] = Observe.Created; Report["physical_connections_disposed"] = Observe.Disposed;
        Report["negotiated_encrypt_modes"] = Observe.Modes; Report["network_io_counts"] = Observe.AsyncCounts;
        Report["explicit_sync_api_network_io_counts"] = Observe.ExplicitSyncCounts;
        Report["lob_opcode_counts"] = Observe.Opcodes; Report["measured_buffer_stats"] = Observe.BufferStats;
        Report["pool_final_snapshot"] = PoolSnapshot();
        Exception? auditFailure = null;
        try
        {
            Require(Observe.Created == Observe.Disposed && Observe.Created == Observe.Modes.Count && Observe.Created > 0, "physical_connection_leak_or_mode_gap");
            Require(Observe.Modes.All(mode => mode == (Mode == "tls" ? 1 : 0)) && Observe.AsyncOnly(Mode == "tls"), "async_network_or_transport_policy_failed");
            Require(PoolQuiescent(), "pool_state_not_zero"); Require(Observe.MeasuredInputChunks > 0 && Observe.MeasuredOutputChunks > 0, "measured_buffer_observation_missing");
        }
        catch (Exception error) { auditFailure = error; Report["secondary_error"] = SafeError(error); }
        if (workFailure != null) throw workFailure;
        if (cleanupFailure != null) { Report["primary_failed_stage"] = "fresh_owned_cleanup"; throw cleanupFailure; }
        if (auditFailure != null) throw auditFailure;
    }

    private static async Task UploadBlobAsync()
    {
        await using var connection = await OpenVerifiedAsync();
        using var input = new PatternStream(BlobBytes);
        await using var command = Command(connection, $"INSERT INTO {Table}(ID,B) VALUES(:id,:value)");
        var id = new DmParameter("id", DmDbType.Int32) { Value = 1 };
        command.Parameters.Add(id); command.Parameters.Add(new DmParameter("value", DmDbType.Blob) { Value = input });
        Require(await command.ExecuteNonQueryAsync() == 1, "blob_stream_upload_failed");
        Require(input.Consumed == BlobBytes && !input.Disposed && input.ForbiddenCalls == 0, "blob_input_ownership_or_hidden_access_failed");
        long consumed = input.Consumed; id.Value = 3;
        Require(await command.ExecuteNonQueryAsync() == 1 && input.Consumed == consumed && !input.Disposed, "repeated_input_rewound_or_disposed");
        await using (var empty = Command(connection, $"SELECT B FROM {Table} WHERE ID=3"))
        await using (var reader = await empty.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync(), "repeated_input_row_missing"); await using var stream = reader.GetStream(0);
            Require(await stream.ReadAsync(new byte[17]) == 0, "repeated_input_did_not_read_current_eof");
        }
        Pass("LOB05_blob_input_repeat", "Contract", "LOB-05,LOB-07", new { uploaded_bytes = input.Consumed, unknown_length = true,
            forbidden_accesses = input.ForbiddenCalls, caller_owned = true, repeated_execution_empty_current_position = true });
    }
    private static async Task UploadTextAsync(string column)
    {
        await using var connection = await OpenVerifiedAsync(); using var input = new GeneratedTextReader(Text);
        await using var command = Command(connection, $"UPDATE {Table} SET {column}=:value WHERE ID=1");
        command.Parameters.Add(new DmParameter("value", DmDbType.Clob) { Value = input });
        Require(await command.ExecuteNonQueryAsync() == 1, "text_reader_upload_failed");
        Require(input.Consumed == Text.Length && !input.Disposed && input.SyncCalls == 0 && input.SplitSurrogateReads > 0,
            "text_input_ownership_async_or_surrogate_boundary_failed");
        Pass("LOB05_" + column + "_input", "Contract", "LOB-05,LOB-02", new { declared_type = column == "N" ? "NCLOB" : "CLOB",
            typed_parameter = "Clob", uploaded_utf16_chars = input.Consumed, unknown_length = true, caller_owned = true,
            sync_reads = input.SyncCalls, split_surrogate_reads = input.SplitSurrogateReads });
    }
    private static async Task MaterializationGuardAsync()
    {
        await using var context = await ReaderContext.OpenAsync("B", 1);
        long gets = Observe.LobGets; bool rejected = false;
        using (Observe.ExplicitSync())
            try { _ = context.Reader.GetValue(0); }
            catch (NotSupportedException error) when (error.Message == "LOB materialization exceeds the fixed 64 MiB returned-payload limit.") { rejected = true; }
        Require(rejected && Observe.LobGets == gets && !context.Reader.IsClosed && context.Connection.State == ConnectionState.Open,
            "oversize_value_not_rejected_before_lob_data");
        await using var followup = context.Reader.GetStream(0);
        byte[] prefix = new byte[17]; int count = await followup.ReadAsync(prefix);
        Require(count > 0, "materialization_guard_made_same_reader_stream_unusable"); VerifyPattern(prefix.AsSpan(0, count), 0);
        Pass("LOB01_materialization_guard", "Contract", "LOB-01", new { field_bytes = BlobBytes, rejected_before_get_lob_data = true,
            exact_cap_exception_matched = true, same_reader_stream_positive = true,
            execution_mode = "explicit_sync_public_api", stream_followup_case = "LOB01_blob_roundtrip" });
    }
    private static async Task ReadBlobAsync()
    {
        await using var context = await ReaderContext.OpenAsync("B", 1);
        long gets = Observe.LobGets, lengths = Observe.LobLengths;
        await using var stream = context.Reader.GetStream(0);
        Require(Observe.LobGets == gets && Observe.LobLengths == lengths && stream.CanRead && !stream.CanSeek && !stream.CanWrite, "blob_stream_not_lazy_or_forward_only");
        bool secondRefused = false; try { context.Reader.GetStream(0).Dispose(); } catch (InvalidOperationException) { secondRefused = true; }
        Require(secondRefused && Observe.LobGets == gets, "second_active_stream_not_refused_without_io");
        byte[] buffer = new byte[BlobBufferBytes]; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long firstGets = Observe.LobGets, firstReplyBytes = Observe.GetReplyBodyBytes;
        int first = await stream.ReadAsync(buffer.AsMemory(0, 1031));
        Observe.SampleOutput(stream);
        long firstRequestCount = Observe.LobGets - firstGets, firstReplyBody = Observe.GetReplyBodyBytes - firstReplyBytes;
        Require(first > 0 && first < BlobBytes && firstRequestCount is > 0 and <= 2 && firstReplyBody is > 0 and < 1048576,
            "blob_first_read_eager_or_empty");
        VerifyPattern(buffer.AsSpan(0, first), 0); hash.AppendData(buffer.AsSpan(0, first)); long total = first;
        using (Observe.ExplicitSync())
        {
            Require(context.Reader.GetBytes(0, 0, null!, 0, 0) == BlobBytes, "blob_length_metadata_failed");
            gets = Observe.LobGets; lengths = Observe.LobLengths;
            Require(context.Reader.GetBytes(0, total, buffer, buffer.Length, 0) == 0 && Observe.LobGets == gets && Observe.LobLengths == lengths,
                "zero_length_blob_read_performed_io");
        }
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
        { Observe.SampleOutput(stream); VerifyPattern(buffer.AsSpan(0, count), total); hash.AppendData(buffer.AsSpan(0, count)); total += count; }
        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        Require(total == BlobBytes && actual == ExpectedBlobHash, "blob_stream_count_or_hash_failed");
        Pass("LOB01_blob_roundtrip", "Contract", "LOB-01,LOB-03", new { bytes = total, sha256 = actual, lazy_get_requests = 0,
            first_read_bytes = first, caller_buffer_bytes = buffer.Length, second_stream_refused = true,
            first_get_request_count = firstRequestCount, first_get_reply_body_bytes = firstReplyBody,
            length_query_preserved_stream_position = true, zero_length_no_io = true, full_array_allocated = false });
    }
    private static async Task ReadTextAsync(string column)
    {
        await using var context = await ReaderContext.OpenAsync(column, 1);
        long gets = Observe.LobGets, lengths = Observe.LobLengths;
        using var reader = context.Reader.GetTextReader(0);
        Require(Observe.LobGets == gets && Observe.LobLengths == lengths, "text_reader_not_lazy");
        bool secondRefused = false; try { context.Reader.GetTextReader(0).Dispose(); } catch (InvalidOperationException) { secondRefused = true; }
        Require(secondRefused, "second_active_text_reader_not_refused");
        char[] buffer = new char[TextBufferChars]; using var digest = new TextDigest();
        long firstGets = Observe.LobGets, firstReplyBytes = Observe.GetReplyBodyBytes;
        int first = await reader.ReadAsync(buffer.AsMemory(0, 17));
        long firstRequestCount = Observe.LobGets - firstGets, firstReplyBody = Observe.GetReplyBodyBytes - firstReplyBytes;
        Require(first > 0 && first < Text.Length && firstRequestCount is > 0 and <= 2 && firstReplyBody is > 0 and < 1048576,
            "text_first_read_eager_or_empty");
        Observe.SampleOutput(reader);
        Text.Check(buffer.AsSpan(0, first), 0); digest.Append(buffer.AsSpan(0, first)); long total = first;
        using (Observe.ExplicitSync())
        {
            Require(context.Reader.GetChars(0, 0, null!, 0, 0) == Text.Length, "text_length_not_clr_utf16_scan");
            gets = Observe.LobGets; lengths = Observe.LobLengths;
            Require(context.Reader.GetChars(0, total, buffer, buffer.Length, 0) == 0 && Observe.LobGets == gets && Observe.LobLengths == lengths,
                "zero_length_text_read_performed_io");
        }
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        { Observe.SampleOutput(reader); Text.Check(buffer.AsSpan(0, count), total); digest.Append(buffer.AsSpan(0, count)); total += count; }
        string actual = digest.Finish();
        Require(total == Text.Length && digest.Runes == Text.Runes && actual == Text.Hash, "text_stream_unicode_count_or_hash_failed");
        Pass("LOB02_" + column + "_roundtrip", "Contract", "LOB-02,LOB-03", new { declared_type = column == "N" ? "NCLOB" : "CLOB",
            utf16_chars = total, runes = digest.Runes, utf8_sha256 = actual, lazy_get_requests = 0, first_read_chars = first,
            caller_buffer_chars = buffer.Length, second_stream_refused = true, length_query_preserved_stream_position = true,
            first_get_request_count = firstRequestCount, first_get_reply_body_bytes = firstReplyBody,
            zero_length_no_io = true, whole_value_string_materialized = false, national_scope = "this_verified_profile_only" });
    }

    private static async Task SmallInputsAsync()
    {
        await using var connection = await OpenVerifiedAsync();
        int chunk = EffectiveChunk(connection);
        var facts = new List<object>();
        foreach ((int id, int length) in new[] { (20, 0), (21, chunk), (22, chunk + 17) })
        {
            using var input = new PatternStream(length, shortReads: true);
            var before = Observe.InputChunks.ToArray(); long sent = Observe.PutFrames, acks = Observe.PutAcks;
            await using var command = Command(connection, $"INSERT INTO {Table}(ID,B) VALUES(:id,:value)");
            command.Parameters.Add(new DmParameter("id", DmDbType.Int32) { Value = id });
            command.Parameters.Add(new DmParameter("value", DmDbType.Blob) { Value = input });
            Require(await command.ExecuteNonQueryAsync() == 1 && !input.Disposed && input.ForbiddenCalls == 0 && input.Consumed == length,
                "small_binary_input_failed");
            int[] observed = Observe.InputChunks.Skip(before.Length).ToArray();
            int[] expected = length == 0 ? [0] : length == chunk ? [chunk, 0] : [chunk, 17];
            Require(observed.SequenceEqual(expected) && Observe.PutFrames - sent == expected.Length && Observe.PutAcks - acks == expected.Length,
                "small_binary_exact_tail_or_ack_failed");
            facts.Add(new { kind = "Blob", length, effective_chunk = chunk, observed_chunk_sizes = observed, caller_owned = true });
            await using var check = Command(connection, $"SELECT B FROM {Table} WHERE ID={id}"); await using var result = await check.ExecuteReaderAsync();
            Require(await result.ReadAsync(), "small_binary_row_missing"); await using var output = result.GetStream(0);
            byte[] buffer = new byte[8191]; long total = 0; int count;
            while ((count = await output.ReadAsync(buffer)) != 0) { VerifyPattern(buffer.AsSpan(0, count), total); total += count; }
            Require(total == length, "small_binary_stored_length_failed");
        }
        foreach ((int id, int length) in new[] { (23, 0), (24, chunk), (25, chunk + 17) })
        {
            using var input = new SmallTextReader(new string('A', length));
            int first = Observe.InputChunks.Count; long sent = Observe.PutFrames, acks = Observe.PutAcks;
            await using var command = Command(connection, $"INSERT INTO {Table}(ID,C) VALUES(:id,:value)");
            command.Parameters.Add(new DmParameter("id", DmDbType.Int32) { Value = id });
            command.Parameters.Add(new DmParameter("value", DmDbType.Clob) { Value = input });
            Require(await command.ExecuteNonQueryAsync() == 1 && !input.Disposed && input.SyncCalls == 0, "small_text_input_failed");
            int[] observed = Observe.InputChunks.Skip(first).ToArray();
            int[] expected = length == 0 ? [0] : length == chunk ? [chunk, 0] : [chunk, 17];
            Require(observed.SequenceEqual(expected) && Observe.PutFrames - sent == expected.Length && Observe.PutAcks - acks == expected.Length,
                "small_text_exact_tail_or_ack_failed");
            facts.Add(new { kind = "Clob_ASCII", length, effective_chunk = chunk, observed_chunk_sizes = observed, caller_owned = true });
            await using var check = Command(connection, $"SELECT C FROM {Table} WHERE ID={id}"); await using var result = await check.ExecuteReaderAsync();
            Require(await result.ReadAsync(), "small_text_row_missing"); using var output = result.GetTextReader(0);
            char[] buffer = new char[257]; long total = 0; int count;
            while ((count = await output.ReadAsync(buffer)) != 0)
            { Require(buffer.AsSpan(0, count).IndexOfAnyExcept('A') < 0, "small_text_stored_content_failed"); total += count; }
            Require(total == length, "small_text_stored_length_failed");
        }
        Pass("LOB05_empty_exact_short", "Contract", "LOB-05,LOB-07", new { inputs = facts,
            positive_caller_short_reads_filled_to_capacity = true, actual_ack_opcode = 261, actual_ack_status = 0, actual_ack_body_length = 21 });
    }
    private static async Task RangesAsync()
    {
        await using (var context = await ReaderContext.OpenAsync("B", 1, CommandBehavior.SequentialAccess))
        {
            byte[] buffer = new byte[257];
            using (Observe.ExplicitSync())
            {
                Require(context.Reader.GetBytes(0, 123, buffer, 0, buffer.Length) == buffer.Length, "binary_forward_range_short"); VerifyPattern(buffer, 123);
                Require(context.Reader.GetBytes(0, 4096, buffer, 0, buffer.Length) == buffer.Length, "binary_second_forward_range_short"); VerifyPattern(buffer, 4096);
                long frames = Observe.Frames;
                bool refused = false; try { context.Reader.GetBytes(0, 123, buffer, 0, 1); } catch (DmException error) when (error.Number == 6097) { refused = true; }
                Require(refused && Observe.Frames == frames, "binary_backward_range_not_refused_before_io");
            }
        }
        await using (var context = await ReaderContext.OpenAsync("C", 1, CommandBehavior.SequentialAccess))
        {
            char[] buffer = new char[257];
            using (Observe.ExplicitSync())
            {
                Require(context.Reader.GetChars(0, 123, buffer, 0, buffer.Length) == buffer.Length, "text_forward_range_short"); Text.Check(buffer, 123);
                Require(context.Reader.GetChars(0, 4096, buffer, 0, buffer.Length) == buffer.Length, "text_second_forward_range_short"); Text.Check(buffer, 4096);
                long frames = Observe.Frames;
                bool refused = false; try { context.Reader.GetChars(0, 123, buffer, 0, 1); } catch (DmException error) when (error.Number == 6097) { refused = true; }
                Require(refused && Observe.Frames == frames, "text_backward_range_not_refused_before_io");
            }
        }
        Pass("LOB03_forward_ranges", "Contract", "LOB-03", new { execution_mode = "explicit_sync_public_api", byte_offsets_verified = true,
            utf16_offsets_verified = true, backward_before_io = true, buffers_bounded = true });
    }
    private static async Task LegacyRegressionAsync()
    {
        byte[] bytes = new byte[40 * 1024]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((17 + i * 131) & 255);
        string text = new string('X', 40 * 1024);
        await using var connection = await OpenVerifiedAsync();
        await using (var insert = Command(connection, $"INSERT INTO {Table}(ID,B,C) VALUES(2,:b,:c)"))
        {
            insert.Parameters.Add(new DmParameter("b", DmDbType.Blob) { Value = bytes }); insert.Parameters.Add(new DmParameter("c", DmDbType.Clob) { Value = text });
            Require(await insert.ExecuteNonQueryAsync() == 1, "legacy_40k_upload_failed");
        }
        await using var command = Command(connection, $"SELECT B,C FROM {Table} WHERE ID=2"); await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "legacy_40k_row_missing");
        using (Observe.ExplicitSync())
            Require(((byte[])reader.GetValue(0)).AsSpan().SequenceEqual(bytes) && reader.GetString(1) == text, "legacy_40k_roundtrip_failed");
        Pass("legacy_40k_array_string", "Contract", "LOB-01,LOB-02", new { blob_bytes = bytes.Length, text_utf16_chars = text.Length,
            actual_legacy_array_string_roundtrip = true, replaces_large_stream_gate = false });
    }
    private static async Task LifecycleAsync()
    {
        await using (var connection = await OpenVerifiedAsync())
        await using (var command = Command(connection, $"SELECT B FROM {Table} WHERE ID IN(1,2) ORDER BY ID"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync(), "lifetime_first_row_missing"); await using var old = reader.GetStream(0);
            Require(await reader.ReadAsync(), "lifetime_next_row_missing"); await InvalidStreamAsync(old);
        }
        await using (var context = await ReaderContext.OpenAsync("B", 2))
        {
            await using var old = context.Reader.GetStream(0); Require(!await context.Reader.NextResultAsync(), "unexpected_additional_result"); await InvalidStreamAsync(old);
        }
        await using (var context = await ReaderContext.OpenAsync("B", 2))
        {
            await using var old = context.Reader.GetStream(0); await context.Reader.CloseAsync(); await InvalidStreamAsync(old);
        }
        await using (var context = await ReaderContext.OpenAsync("C", 1))
        {
            using var old = context.Reader.GetTextReader(0); await context.Connection.CloseAsync(); await context.Connection.OpenAsync();
            await VerifyIdentityAsync(context.Connection); long frames = Observe.Frames;
            await ExpectInvalidAsync(old.ReadAsync(new char[17]).AsTask());
            Require(Observe.Frames == frames && context.Connection.State == ConnectionState.Open, "old_text_flow_touched_reopened_connection");
            await VerifyIdentityAsync(context.Connection);
        }
        Pass("LOB04_parent_lifetime", "Contract", "LOB-04", new { next_row_invalidates = true, next_result_invalidates = true, reader_close_invalidates = true,
            old_flow_cannot_touch_new_physical_lease = true, invalid_reads_before_io = true });
    }
    private static async Task InvalidStreamAsync(Stream stream)
    { long frames = Observe.Frames; await ExpectInvalidAsync(stream.ReadAsync(new byte[17]).AsTask()); Require(Observe.Frames == frames, "invalid_stream_performed_io"); }
    private static async Task ExpectInvalidAsync(Task operation)
    {
        try { await operation; } catch (InvalidOperationException) { return; }
        throw new ProbeFailure("stale_parent_read_not_refused");
    }
    private static async Task RejectWrongTypeAsync()
    {
        await using var connection = await OpenVerifiedAsync(); using var input = new PatternStream(100);
        await using var command = Command(connection, $"INSERT INTO {Table}(ID,B) VALUES(30,:p0)");
        command.Parameters.Add(new DmParameter("p0", DmDbType.Int32) { Value = input });
        long frames = Observe.Frames; bool refused = false;
        try { await command.ExecuteNonQueryAsync(); } catch (NotSupportedException) { refused = true; } catch (ArgumentException) { refused = true; }
        Require(refused && Observe.Frames == frames && input.Consumed == 0 && input.ForbiddenCalls == 0 && !input.Disposed, "wrong_stream_type_not_rejected_before_io");
        Pass("LOB07_explicit_type", "Contract", "LOB-07", new { before_network = true, before_input_read = true, caller_owned = true });
    }
    private static async Task InputExceptionAsync()
    {
        await using var connection = await OpenVerifiedAsync(); using var input = new PatternStream(BlobBytes, throwAfter: 2 * EffectiveChunk(connection) + 17);
        await using var command = Command(connection, $"INSERT INTO {Table}(ID,B) VALUES(31,:p0)");
        command.Parameters.Add(new DmParameter("p0", DmDbType.Blob) { Value = input });
        long frames = Observe.PutFrames, sockets = Observe.Created; Exception? failure = null;
        try { await command.ExecuteNonQueryAsync(); } catch (Exception error) { failure = error; }
        Require(failure != null && input.ThrowCount == 1 && !input.Disposed && Observe.PutFrames > frames && Observe.Created == sockets &&
            connection.State is ConnectionState.Broken or ConnectionState.Closed, "sent_input_exception_replayed_or_reusable");
        await connection.CloseAsync(); Require(await FreshRowCountAsync(31) == 0, "failed_input_created_business_row");
        Pass("LOB06_input_exception", "Contract", "LOB-06", new { after_upload_send = true, source_throw_count = input.ThrowCount,
            caller_owned = true, connection_broken_or_closed = true, socket_replay_count = 0, fresh_rows = 0, error = SafeError(failure!) });
    }
    private static async Task UploadCancellationAsync()
    {
        await using var connection = await OpenVerifiedAsync(); using var input = new PatternStream(BlobBytes); using var cancellation = new CancellationTokenSource();
        await using var command = Command(connection, $"INSERT INTO {Table}(ID,B) VALUES(32,:p0)");
        command.Parameters.Add(new DmParameter("p0", DmDbType.Blob) { Value = input });
        long frames = Observe.PutFrames, sockets = Observe.Created;
        using (Observe.CancelAfterOpcode(26, cancellation))
        {
            var error = await ExpectCanceledAsync(command.ExecuteNonQueryAsync(cancellation.Token), cancellation.Token);
            Require(Observe.PutFrames - frames == 1 && Observe.Created == sockets && !input.Disposed && input.Consumed < BlobBytes &&
                connection.State is ConnectionState.Broken or ConnectionState.Closed, "sent_upload_cancel_replayed_or_reusable");
            await connection.CloseAsync(); Require(await FreshRowCountAsync(32) == 0, "canceled_upload_created_business_row");
            Pass("LOB06_upload_cancel", "Contract", "LOB-06", new { canceled_after_opcode26 = true, sent_frames = 1, no_replay = true,
                caller_owned = true, source_bytes_consumed = input.Consumed, fresh_rows = 0, error = SafeError(error) });
        }
    }
    private static async Task ReadCancellationAsync()
    {
        await using var context = await ReaderContext.OpenAsync("B", 1); await using var stream = context.Reader.GetStream(0);
        using var cancellation = new CancellationTokenSource(); long frames = Observe.LobGets, sockets = Observe.Created;
        using (Observe.CancelAfterOpcode(32, cancellation))
        {
            var error = await ExpectCanceledAsync(stream.ReadAsync(new byte[17], cancellation.Token).AsTask(), cancellation.Token);
            Require(Observe.LobGets - frames == 1 && Observe.Created == sockets && context.Connection.State is ConnectionState.Broken or ConnectionState.Closed,
                "sent_read_cancel_replayed_or_reusable");
            Pass("LOB06_read_cancel", "Contract", "LOB-06", new { canceled_after_opcode32 = true, sent_frames = 1, no_replay = true, error = SafeError(error) });
        }
    }
    private static async Task<DmOperationCanceledException> ExpectCanceledAsync(Task operation, CancellationToken original)
    {
        try { await operation; } catch (DmOperationCanceledException error)
        {
            Require(error.CancellationToken == original && error.FailureInfo.ErrorKind == DmErrorKind.Canceled &&
                error.FailureInfo.OperationOutcome == DmOperationOutcome.Unknown && !error.FailureInfo.ConnectionReusable,
                "sent_cancel_typed_identity_or_outcome_failed"); return error;
        }
        throw new ProbeFailure("expected_typed_cancellation_missing");
    }

    private static async Task<DmConnection> OpenVerifiedAsync()
    {
        var connection = Source.CreateConnection();
        try
        {
            long created = Observe.Created; await connection.OpenAsync();
            Require(Observe.Created == created + 1, "physical_reuse_or_extra_open_observed"); await VerifyIdentityAsync(connection); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task VerifyIdentityAsync(DmConnection connection)
    {
        await using var command = Command(connection, "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "test_identity_or_schema_mismatch");
        string expected = Mode == "tls" ? "8.1.4.6" : "8.1.5.60";
        Require(connection.ServerVersion == expected && Regex.IsMatch(connection.ServerVersion, @"^\d+(\.\d+){1,5}$"), "characterized_server_profile_required");
        object properties = ConnectionProperties(connection);
        string encoding = (string)Property(properties, "ServerEncoding"); int messageVersion = (int)Field(properties, "msgVersion");
        string canonical = Encoding.GetEncoding(encoding).WebName;
        Require(canonical == (Mode == "tls" ? "gb18030" : "utf-8") && messageVersion == (Mode == "tls" ? 11 : 21), "characterized_charset_and_message_version_required");
        Report["server_identity"] = TestUser; Report["server_schema"] = TestUser; Report["server_version"] = connection.ServerVersion;
        Report["server_encoding"] = canonical; Report["message_version"] = messageVersion;
    }
    private static DmCommand Command(DmConnection connection, string sql) => new(sql, connection) { CommandTimeout = CommandSeconds };
    private static async Task ExecAsync(DmConnection connection, string sql)
    { await using var command = Command(connection, sql); await command.ExecuteNonQueryAsync(); }
    private static async Task<int> ObjectCountAsync(DmConnection connection)
    {
        await using var command = Command(connection, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0");
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = Table }); return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
    private static async Task<int> FreshRowCountAsync(int id)
    { await using var connection = await OpenVerifiedAsync(); await using var command = Command(connection, $"SELECT COUNT(*) FROM {Table} WHERE ID={id}"); return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture); }
    private static object ConnectionProperties(DmConnection connection) => Property(Field(connection, "m_ConnInst"), "ConnProperty");
    private static int EffectiveChunk(DmConnection connection) => Math.Min(BlobBufferBytes, (int)Property(ConnectionProperties(connection), "MaxLobDataLenPerMsg"));
    private static object Property(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object PoolSnapshot()
    {
        object snapshot = Property(Source, "Snapshot");
        return new { creating = (int)Property(snapshot, "Creating"), leased = (int)Property(snapshot, "Leased"), closing = (int)Property(snapshot, "Closing"),
            waiting = (int)Property(snapshot, "Waiting"), idle = (int)Property(snapshot, "Idle"), resetting = (int)Property(snapshot, "Resetting") };
    }
    private static bool PoolQuiescent()
    { object snapshot = Property(Source, "Snapshot"); return new[] { "Creating", "Leased", "Closing", "Waiting", "Idle", "Resetting" }.All(n => (int)Property(snapshot, n) == 0); }
    private static void Pass(string id, string category, string contracts, object evidence)
    { Cases[id] = new { status = "passed", category, feature = "LobStreaming", contracts, evidence }; Checkpoint(); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static Exception Unwrap(Exception error) => error is TargetInvocationException { InnerException: not null } wrapped ? Unwrap(wrapped.InnerException) : error;
    private static object SafeError(Exception error)
    {
        DmFailureInfo? info = (error as DmException)?.FailureInfo ?? (error as DmOperationCanceledException)?.FailureInfo;
        var innerErrors = new List<object>();
        Exception? inner = error.InnerException;
        for (int depth = 1; inner != null && depth <= 3; depth++, inner = inner.InnerException)
        {
            innerErrors.Add(new { depth, type = inner.GetType().FullName, hresult = inner.HResult,
                classification = inner is DmException ? "provider_error" : inner is OperationCanceledException ? "operation_canceled" :
                    inner is SocketException ? "socket_exception" : "unclassified_failure",
                number = inner is DmException innerDm ? (int?)innerDm.Number : null,
                socket_error_code = inner is SocketException innerSocket ? (int?)innerSocket.SocketErrorCode : null,
                native_error_code = inner is SocketException nativeSocket ? (int?)nativeSocket.NativeErrorCode : null });
        }
        return new { type = error.GetType().FullName, classification = error is ProbeFailure failure ? failure.Kind :
            error is DmException ? "provider_error" : error is OperationCanceledException ? "operation_canceled" : "unclassified_failure",
            hresult = error.HResult,
            socket_error_code = error is SocketException socket ? (int?)socket.SocketErrorCode : null,
            native_error_code = error is SocketException native ? (int?)native.NativeErrorCode : null,
            inner_errors = innerErrors, inner_errors_truncated = inner != null,
            failure_kind = info?.ErrorKind.ToString(), failure_phase = info?.Phase.ToString(), operation_outcome = info?.OperationOutcome.ToString(),
            driver_code = info?.ErrorCode, number = error is DmException dm ? (int?)dm.Number : null, server_number = info?.ServerErrorNumber,
            connection_reusable = info?.ConnectionReusable };
    }
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
    private sealed class ReaderContext(DmConnection connection, DmCommand command, DmDataReader reader) : IAsyncDisposable
    {
        internal DmConnection Connection => connection;
        internal DmDataReader Reader => reader;
        internal static async Task<ReaderContext> OpenAsync(string column, int id, CommandBehavior behavior = CommandBehavior.Default)
        {
            var connection = await OpenVerifiedAsync(); var command = Command(connection, $"SELECT {column} FROM {Table} WHERE ID={id}"); DmDataReader? reader = null;
            try { reader = (DmDataReader)await command.ExecuteReaderAsync(behavior); Require(await reader.ReadAsync(), "stream_row_missing"); return new(connection, command, reader); }
            catch { if (reader != null) await reader.DisposeAsync(); await command.DisposeAsync(); await connection.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        { try { await reader.DisposeAsync(); } finally { try { await command.DisposeAsync(); } finally { await connection.DisposeAsync(); } } }
    }
    private static void VerifyPattern(ReadOnlySpan<byte> bytes, long start)
    { for (int i = 0; i < bytes.Length; i++) Require(bytes[i] == (byte)((17 + 131 * ((start + i) & 255)) & 255), "binary_pattern_mismatch"); }
    private static string BlobReferenceHash()
    {
        byte[] buffer = new byte[BlobBufferBytes]; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long offset = 0; offset < BlobBytes;)
        {
            int count = (int)Math.Min(buffer.Length, BlobBytes - offset);
            for (int i = 0; i < count; i++) buffer[i] = (byte)((17 + 131 * ((offset + i) % 256)) % 256);
            hash.AppendData(buffer.AsSpan(0, count)); offset += count;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed class PatternStream : Stream
    {
        private readonly long length, throwAfter;
        private readonly bool shortReads;
        private readonly byte[] cycle = Enumerable.Range(0, 256).Select(i => (byte)((17 + 131 * i) & 255)).ToArray();
        private long position;
        private int calls;
        internal long Consumed => position;
        internal int ForbiddenCalls, ThrowCount;
        internal bool Disposed;
        internal PatternStream(long length, bool shortReads = false, long throwAfter = long.MaxValue)
        { this.length = length; this.shortReads = shortReads; this.throwAfter = throwAfter; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek { get { ForbiddenCalls++; throw new NotSupportedException(); } }
        public override long Length { get { ForbiddenCalls++; throw new NotSupportedException(); } }
        public override long Position { get { ForbiddenCalls++; throw new NotSupportedException(); } set { ForbiddenCalls++; throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count) { ForbiddenCalls++; throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer) { ForbiddenCalls++; throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= throwAfter) { ThrowCount++; throw new IOException("Synthetic input failure."); }
            int count = (int)Math.Min(Math.Min(buffer.Length, BlobBufferBytes), Math.Min(length - position, throwAfter - position));
            if (shortReads) count = Math.Min(count, (++calls % 3) switch { 0 => 17, 1 => 31, _ => 257 });
            for (int i = 0; i < count; i++) buffer.Span[i] = cycle[(int)((position + i) & 255)];
            position += count; return ValueTask.FromResult(count);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) { ForbiddenCalls++; throw new NotSupportedException(); }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class TextReference
    {
        private readonly string value;
        private readonly string[] prefixes;
        private readonly int stride;
        internal readonly long Length, Runes;
        internal readonly string Hash;
        internal TextReference(string value, int runes, int cycles)
        {
            this.value = value; prefixes = Enumerable.Range(0, cycles).Select(i => $"[CYCLE:{i:D4}]\n").ToArray();
            stride = value.Length + prefixes[0].Length; Length = (long)stride * cycles; Runes = (long)(runes + prefixes[0].Length) * cycles;
            byte[] bytes = new byte[BlobBufferBytes]; var encoding = new UTF8Encoding(false, true); using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string prefix in prefixes)
            foreach (string piece in new[] { prefix, value })
            {
                int offset = 0;
                while (offset < piece.Length)
                {
                    int count = Math.Min(8191, piece.Length - offset);
                    if (char.IsHighSurrogate(piece[offset + count - 1])) count--;
                    int encoded = encoding.GetBytes(piece.AsSpan(offset, count), bytes); digest.AppendData(bytes.AsSpan(0, encoded)); offset += count;
                }
            }
            Hash = Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        internal char At(long index)
        { int cycle = (int)(index / stride), within = (int)(index % stride); return within < prefixes[cycle].Length ? prefixes[cycle][within] : value[within - prefixes[cycle].Length]; }
        internal void Check(ReadOnlySpan<char> chars, long start)
        { Require(start + chars.Length <= Length, "text_read_exceeded_reference"); for (int i = 0; i < chars.Length; i++) Require(chars[i] == At(start + i), "unicode_text_reference_mismatch"); }
        internal int Fill(Memory<char> buffer, long start, int limit)
        { int count = (int)Math.Min(Math.Min(buffer.Length, limit), Length - start); for (int i = 0; i < count; i++) buffer.Span[i] = At(start + i); return count; }
    }
    private sealed class GeneratedTextReader(TextReference text) : TextReader
    {
        private long position;
        private int calls;
        internal long Consumed => position;
        internal int SyncCalls, SplitSurrogateReads;
        internal bool Disposed;
        public override int Read(char[] buffer, int index, int count) { SyncCalls++; throw new NotSupportedException(); }
        public override int Read(Span<char> buffer) { SyncCalls++; throw new NotSupportedException(); }
        public override int Read() { SyncCalls++; throw new NotSupportedException(); }
        public override int Peek() { SyncCalls++; throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); int limit = (++calls % 5) switch { 0 => 17, 1 => 31, 2 => 8191, 3 => 257, _ => 4093 };
            int count = text.Fill(buffer, position, limit); if (count > 0 && char.IsHighSurrogate(buffer.Span[count - 1])) SplitSurrogateReads++;
            position += count; return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class SmallTextReader(string value) : TextReader
    {
        private int position;
        internal int SyncCalls;
        internal bool Disposed;
        public override int Read(char[] buffer, int index, int count) { SyncCalls++; throw new NotSupportedException(); }
        public override int Read(Span<char> buffer) { SyncCalls++; throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); int count = Math.Min(31, Math.Min(buffer.Length, value.Length - position)); value.AsSpan(position, count).CopyTo(buffer.Span); position += count; return ValueTask.FromResult(count); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class TextDigest : IDisposable
    {
        private readonly Encoder encoder = new UTF8Encoding(false, true).GetEncoder();
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] bytes = new byte[BlobBufferBytes];
        private bool high;
        internal long Runes;
        internal void Append(ReadOnlySpan<char> chars)
        {
            foreach (char value in chars)
            {
                if (high) { Require(char.IsLowSurrogate(value), "output_unpaired_high_surrogate"); high = false; Runes++; }
                else if (char.IsHighSurrogate(value)) high = true;
                else { Require(!char.IsLowSurrogate(value), "output_unpaired_low_surrogate"); Runes++; }
            }
            do
            {
                encoder.Convert(chars, bytes, false, out int usedChars, out int usedBytes, out _);
                hash.AppendData(bytes.AsSpan(0, usedBytes)); chars = chars[usedChars..];
            } while (!chars.IsEmpty);
        }
        internal string Finish()
        {
            Require(!high, "output_terminal_surrogate_missing"); encoder.Convert(ReadOnlySpan<char>.Empty, bytes, true, out _, out int count, out bool complete);
            Require(complete, "output_utf8_flush_incomplete"); hash.AppendData(bytes.AsSpan(0, count)); return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        public void Dispose() => hash.Dispose();
    }

    private sealed class Observation : IDisposable
    {
        private readonly Type transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!;
        private readonly FieldInfo frameHook, headerHook, modeHook, ioHook, inputHook;
        private readonly object gate = new();
        private readonly Dictionary<string, long> io = new(), sync = new();
        private readonly Dictionary<short, long> opcodes = new();
        private readonly long initialCreated, initialDisposed;
        private int explicitSyncDepth;
        private bool unknownOperation, invalidAck;
        private short cancelOpcode;
        private CancellationTokenSource? cancel;
        private bool canceled;
        private long frames, acks, inputObserved, inputPeak, outputObserved, outputPeak, replyBodyPeak, replyBodyTotal;
        private short lastSentOpcode, lastHeaderRequestOpcode, lastHeaderResponseOpcode;
        private bool headerObserved;
        private int lastHeaderStatus, lastHeaderBodyBytes;
        private long lastInputConsumedBytes, lastInputConsumedCharacters;
        internal readonly List<int> Modes = [];
        internal readonly List<int> InputChunks = [];
        internal Observation()
        {
            Type wire = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!;
            Type trace = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!;
            Type input = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Lobs.DmLobInputTestHooks") ?? throw new ProbeFailure("input_buffer_hook_missing");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            frameHook = wire.GetField("AfterFrameSent", flags)!; headerHook = trace.GetField("AfterFrame", flags)!;
            modeHook = wire.GetField("AfterStartupNegotiatedEncryptMode", flags)!; ioHook = transport.GetField("BeforeNetworkIo", flags)!;
            inputHook = input.GetField("AfterChunk", flags)!;
            foreach (FieldInfo field in new[] { frameHook, headerHook, modeHook, ioHook, inputHook }) Require(field != null && field.GetValue(null) == null, "isolated_hooks_required");
            foreach (string operation in new[] { "connect", "tls", "send", "receive" })
            { io["async_" + operation] = 0; io["sync_" + operation] = 0; sync[operation] = 0; }
            initialCreated = Counter("CreatedTcpSockets"); initialDisposed = Counter("DisposedTcpSockets");
            frameHook.SetValue(null, Bridge(frameHook, values =>
            {
                short opcode = (short)values[1]; CancellationTokenSource? signal = null;
                lock (gate)
                {
                    frames++; lastSentOpcode = opcode; opcodes[opcode] = opcodes.GetValueOrDefault(opcode) + 1;
                    if (cancel != null && !canceled && opcode == cancelOpcode) { canceled = true; signal = cancel; }
                }
                signal?.Cancel(); // Signal only outside the metrics gate; never Close/Dispose or blocking I/O.
            }));
            headerHook.SetValue(null, (Action<short, short, int, int>)((request, response, status, bodyLength) =>
            {
                lock (gate)
                {
                    headerObserved = true; lastHeaderRequestOpcode = request; lastHeaderResponseOpcode = response;
                    lastHeaderStatus = status; lastHeaderBodyBytes = bodyLength;
                    if (request == 26) { acks++; invalidAck |= response != 261 || status != 0 || bodyLength != 21; }
                    if (request == 32) { replyBodyPeak = Math.Max(replyBodyPeak, bodyLength); replyBodyTotal = checked(replyBodyTotal + bodyLength); }
                }
            }));
            modeHook.SetValue(null, (Action<int>)(mode => { lock (gate) Modes.Add(mode); }));
            ioHook.SetValue(null, (Action<bool, string>)((asynchronous, operation) =>
            {
                lock (gate)
                {
                    if (!sync.ContainsKey(operation)) { unknownOperation = true; return; }
                    if (!asynchronous && explicitSyncDepth > 0) sync[operation]++;
                    else io[(asynchronous ? "async_" : "sync_") + operation]++;
                }
            }));
            inputHook.SetValue(null, (Action<long, long, int, int>)((consumedBytes, consumedCharacters, chunkBytes, peakBytes) =>
            {
                lock (gate)
                {
                    Require(InputChunks.Count < 32768 && chunkBytes >= 0 && peakBytes > 0, "input_numeric_observation_invalid");
                    InputChunks.Add(chunkBytes); inputObserved++; inputPeak = Math.Max(inputPeak, peakBytes);
                    lastInputConsumedBytes = consumedBytes; lastInputConsumedCharacters = consumedCharacters;
                }
            }));
        }
        private static Delegate Bridge(FieldInfo field, Action<object[]> action)
        {
            var parameters = field.FieldType.GenericTypeArguments.Select(t => Expression.Parameter(t)).ToArray();
            return Expression.Lambda(field.FieldType, Expression.Invoke(Expression.Constant(action),
                Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object))))), parameters).Compile();
        }
        internal void SampleOutput(object stream)
        {
            object cursor = stream.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(f => f.FieldType.FullName == "W.Dm.Internal.Lobs.DmLobReadCursor").GetValue(stream)!;
            int retained = (int)Property(cursor, "BufferedBytes");
            Require(retained >= 0, "output_retained_buffer_observation_invalid");
            lock (gate) { outputObserved++; outputPeak = Math.Max(outputPeak, retained); }
        }
        internal IDisposable ExplicitSync()
        { lock (gate) explicitSyncDepth++; return new Scope(() => { lock (gate) explicitSyncDepth--; }); }
        internal IDisposable CancelAfterOpcode(short opcode, CancellationTokenSource signal)
        {
            lock (gate) { Require(cancel == null, "cancel_observer_already_installed"); cancelOpcode = opcode; cancel = signal; canceled = false; }
            return new Scope(() => { lock (gate) { cancel = null; cancelOpcode = 0; } });
        }
        private long Counter(string name) => (long)transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        internal long Created => Counter("CreatedTcpSockets") - initialCreated;
        internal long Disposed => Counter("DisposedTcpSockets") - initialDisposed;
        internal long Frames { get { lock (gate) return frames; } }
        internal long LobGets { get { lock (gate) return opcodes.GetValueOrDefault((short)32); } }
        internal long LobLengths { get { lock (gate) return opcodes.GetValueOrDefault((short)29); } }
        internal long PutFrames { get { lock (gate) return opcodes.GetValueOrDefault((short)26); } }
        internal long PutAcks { get { lock (gate) return acks; } }
        internal long GetReplyBodyBytes { get { lock (gate) return replyBodyTotal; } }
        internal long MeasuredInputChunks { get { lock (gate) return inputObserved; } }
        internal long MeasuredOutputChunks { get { lock (gate) return outputObserved; } }
        internal Dictionary<string, long> AsyncCounts { get { lock (gate) return new(io); } }
        internal Dictionary<string, long> ExplicitSyncCounts { get { lock (gate) return new(sync); } }
        internal object Opcodes { get { lock (gate) return new { get_lob_data = opcodes.GetValueOrDefault((short)32), get_lob_length = opcodes.GetValueOrDefault((short)29),
            put_data2_sent = opcodes.GetValueOrDefault((short)26), put_data2_ack = acks, ack_opcode = 261, ack_status = 0, ack_body_bytes = 21, invalid_ack_seen = invalidAck }; } }
        internal object FailureSnapshot()
        {
            lock (gate) return new
            {
                observation_scope = "existing_numeric_hooks_before_cleanup",
                completed_frame_hook_scope = true, put_data2_send_attempts = "unavailable_completed_frame_hook_not_attempt_callback",
                frames_sent = frames, last_sent_opcode = frames > 0 ? (short?)lastSentOpcode : null,
                last_header_request_opcode = !headerObserved ? (short?)null : lastHeaderRequestOpcode,
                last_header_response_opcode = !headerObserved ? (short?)null : lastHeaderResponseOpcode,
                last_header_status = !headerObserved ? (int?)null : lastHeaderStatus,
                last_header_body_bytes = !headerObserved ? (int?)null : lastHeaderBodyBytes,
                put_data2_sent = opcodes.GetValueOrDefault((short)26), put_data2_ack_frame_observations = acks,
                invalid_ack_header_seen = invalidAck,
                accepted_put_data2_acknowledgments = "unavailable_header_hook_does_not_prove_body_acceptance",
                input_completed_chunk_observations = inputObserved,
                input_consumed_bytes_at_last_completed_chunk_hook = inputObserved > 0 ? (long?)lastInputConsumedBytes : null,
                input_consumed_utf16_chars_at_last_completed_chunk_hook = inputObserved > 0 ? (long?)lastInputConsumedCharacters : null,
                exact_current_input_consumption = "unavailable_between_completed_chunk_hooks"
            };
        }
        internal object BufferStats { get { lock (gate) return new { input_chunk_observations = inputObserved, input_owned_cursor_peak_bytes = inputPeak,
            output_public_read_samples = outputObserved, output_sampled_retained_cursor_peak_bytes = outputPeak,
            output_max_get_lob_reply_body_bytes = replyBodyPeak, scope = "cursor_owned_and_sampled_retained_buffers_plus_observed_reply_body_not_process_RSS" }; } }
        internal bool AsyncOnly(bool tls)
        {
            lock (gate) return !unknownOperation && !invalidAck && io.Where(p => p.Key.StartsWith("sync_", StringComparison.Ordinal)).All(p => p.Value == 0) &&
                io["async_connect"] > 0 && io["async_send"] > 0 && io["async_receive"] > 0 && (tls ? io["async_tls"] > 0 : io["async_tls"] == 0);
        }
        public void Dispose() { foreach (FieldInfo field in new[] { frameHook, headerHook, modeHook, ioHook, inputHook }) field.SetValue(null, null); }
        private sealed class Scope(Action complete) : IDisposable
        { private Action? action = complete; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
    }
}
