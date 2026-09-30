using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
#if OFFICIAL
using DriverConnection = Dm.DmConnection;
#else
using DriverConnection = W.Dm.DmConnection;
#endif

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const string TargetServerVersion = "8.1.5.60";
    private static string? observedServerVersion;
#if OFFICIAL
    private const string Lane = "O";
    private const string PackageVersion = "8.3.1.47463";
    private const string PackageHash = "62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75";
    private const string DllHash = "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b";
    private const string Mvid = "d909b5af-965f-4cf9-a80a-a6181124e361";
    private const string AssetPath = "lib/net9.0/DM.DmProvider.dll";
    private const string TemporalClass = "Dm.DmDateTime";
#else
    private const string Lane = "W-frozen-R1";
    private const string PackageVersion = "0.1.0-r1.20260930051717";
    private const string PackageHash = "23b4b6a88f14bd58bfe98d6703e81181b89fd4c43390a2b22b1092d17c616df9";
    private const string DllHash = "ddd64493db5208efaabd2c7edd72350bc48b9f8ab1e5edac277a26c123621eb1";
    private const string Mvid = "ebec302b-ca05-4037-8e44-4bab084b226c";
    private const string AssetPath = "lib/net10.0/W.DmProvider.dll";
    private const string TemporalClass = "W.Dm.DmDateTime";
#endif
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Sample(string Name, DateTimeOffset Value);
    // Fixed EF 113014 AddTicks(1) temporal vector and the requested existing offset set.
    private static readonly Sample[] Samples =
    [
        new("utc", new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.Zero).AddTicks(1)),
        new("plus08", new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromHours(8)).AddTicks(1)),
        new("minus05", new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromHours(-5)).AddTicks(1)),
        new("plus0530", new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(330)).AddTicks(1))
    ];

    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] is "seed" or "read" or "cleanup")
            return RunHandoff(args);
        var report = new Dictionary<string, object?>
        {
            ["schema_version"] = 1, ["task"] = "T12", ["lane"] = Lane,
            ["package_version"] = PackageVersion, ["runtime"] = Environment.Version.ToString(),
            ["status"] = "not_run", ["public_W_seven_digit_acceptance"] = false,
            ["server_identity_verified"] = false, ["cleanup_verified"] = false
        };
        var issues = new List<string>();
        string stage = "identity";
        bool ownsTable = false;
        string table = "TP7_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        string? raw = null;
        try
        {
            if (args.Length != 3 || args[0] is not ("profile" or "codec"))
                throw new InvalidOperationException("usage_rejected");
            CheckPackage(args[1], report);
            try { report["pure_encoders"] = PureEncoders(); }
            catch (Exception error)
            {
                report["pure_encoder_failure"] = Error(error);
                if (args[0] == "codec") throw;
            }
            if (args[0] == "codec")
            {
                report["status"] = "offline_encoder_characterization";
                return Finish(args[2], report, 0);
            }
            stage = "test_connection";
            raw = ConfiguredTestConnection();
            var builder = new DbConnectionStringBuilder { ConnectionString = raw };
            if (!builder.Keys.Cast<string>().Any(key => key.Replace(" ", "", StringComparison.Ordinal).Equals("userid", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(builder[key], CultureInfo.InvariantCulture), TestUser, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("configured_test_identity_invalid");
            stage = "create_owned_table";
            using (var connection = OpenVerified(raw))
            {
                report["server_identity_verified"] = true;
                report["server_version"] = observedServerVersion;
                if (Exists(connection, table)) throw new InvalidOperationException("owned_name_collision");
                report["owned_table"] = table;
                File.WriteAllText(args[2] + ".ownership.json", JsonSerializer.Serialize(new
                {
                    lane = Lane, owned_table = table, server_identity_verified = true,
                    server_version = observedServerVersion, package_sha256 = PackageHash,
                    status = "owned_name_absent_before_create"
                }, JsonOptions));
                ownsTable = true; // The exact absent name is reserved for this run, even if CREATE acknowledgement fails.
                Exec(connection, $"CREATE TABLE \"{table}\" (\"ID\" INT PRIMARY KEY, \"DT\" TIMESTAMP(7), \"DTO\" DATETIME(7) WITH TIME ZONE)");
            }
            var parameters = new List<object>();
            var reads = new List<object>();
            report["typed_parameter_cases"] = parameters;
            report["readback_cases"] = reads;
            for (int index = 0; index < Samples.Length; index++)
            {
                var sample = Samples[index];
                stage = "typed_parameter_" + sample.Name;
#if OFFICIAL
                int parameterId = index + 1;
#else
                int parameterId = index + 101;
#endif
                var parameterCase = WriteParameters(raw, table, parameterId, sample, out bool written, out bool metadataValid);
                parameters.Add(parameterCase);
                if (!metadataValid) issues.Add("prepare_metadata_" + sample.Name);
#if OFFICIAL
                if (!written) issues.Add("official_public_parameter_write_" + sample.Name);
                if (written) reads.Add(ReadFresh(raw, table, parameterId, sample, issues));
#else
                // W public failure remains characterization; no gate bypass is used for this call.
                if (written) issues.Add("frozen_W_public_write_unexpectedly_accepted_" + sample.Name);
                using (var fresh = OpenVerified(raw))
                    if (CountId(fresh, table, parameterId) != 0) issues.Add("frozen_W_failed_write_changed_state_" + sample.Name);
                stage = "literal_write_" + sample.Name;
                using (var connection = OpenVerified(raw))
                {
                    string local = sample.Value.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                    string offset = sample.Value.ToString("zzz", CultureInfo.InvariantCulture);
                    Exec(connection, $"INSERT INTO \"{table}\" (\"ID\",\"DT\",\"DTO\") VALUES ({index + 1}, CAST('{local}' AS TIMESTAMP(7)), CAST('{local} {offset}' AS DATETIME(7) WITH TIME ZONE))");
                }
                stage = "literal_read_" + sample.Name;
                reads.Add(ReadFresh(raw, table, index + 1, sample, issues));
#endif
            }
            using (var final = OpenVerified(raw)) report["rows_before_cleanup"] = Count(final, table);
            report["evidence_kind"] =
#if OFFICIAL
                "official_public_write_and_server_oracle";
#else
                "frozen_W_literal_decode_and_public_write_rejection_characterization";
#endif
        }
        catch (Exception error)
        {
            report["failure_stage"] = stage;
            report["server_version"] = observedServerVersion;
            report["failure"] = Error(error);
            issues.Add("profile_stage_failed");
        }
        finally
        {
            if (ownsTable && raw is not null)
            {
                try
                {
                    using (var connection = OpenVerified(raw))
                        if (Exists(connection, table)) Exec(connection, $"DROP TABLE \"{table}\"");
                    using var fresh = OpenVerified(raw);
                    bool absent = !Exists(fresh, table);
                    report["cleanup_verified"] = absent;
                    report["final_database_state"] = absent ? "unique_owned_table_absent" : "owned_table_remaining";
                    if (!absent) issues.Add("cleanup_state_failed");
                }
                catch (Exception error)
                {
                    report["cleanup_failure"] = Error(error);
                    issues.Add("cleanup_failed");
                }
            }
        }
        if (!(report["cleanup_verified"] is true)) issues.Add("cleanup_unverified");
        report["issues"] = issues;
        report["status"] = issues.Count == 0 ? "diagnostic_complete" : "diagnostic_failed";
        return Finish(args.Length == 3 ? args[2] : null, report, issues.Count == 0 ? 0 : 1);
    }

    private sealed record Ownership(int SchemaVersion, string Nonce, string Table, string State,
        bool AbsentBeforeCreate, bool IdentityVerified, string ServerVersion, string OwnerPackageHash,
        string OwnerDllHash, string OwnerMvid);

    private static int RunHandoff(string[] args)
    {
        string mode = args[0];
        var report = new Dictionary<string, object?>
        {
            ["schema_version"] = 1, ["task"] = "T12", ["mode"] = mode, ["lane"] = Lane,
            ["package_version"] = PackageVersion, ["runtime"] = Environment.Version.ToString(),
            ["public_W_seven_digit_acceptance"] = false, ["server_identity_verified"] = false,
            ["cleanup_verified"] = false, ["status"] = "not_run"
        };
        var issues = new List<string>();
        string stage = "frozen_package";
        string? raw = null;
        Ownership? ownership = null;
        bool seedOwnsName = false, seedReady = false;
        try
        {
            CheckPackage(args[1], report);
            if ((mode is "seed" or "cleanup") && Lane != "O") throw new InvalidOperationException("owner_lane_required");
            if (mode == "read" && Lane == "O") throw new InvalidOperationException("W_decode_lane_required");
            raw = ConfiguredTestConnection();
            stage = "handoff_identity";
            using (var connection = OpenVerified(raw))
            {
                report["server_identity_verified"] = true;
                report["server_version"] = observedServerVersion;
                report["processing"] = ProcessingMetadata(connection);
                if (mode == "seed")
                {
                    string nonce = Guid.NewGuid().ToString("N").ToUpperInvariant();
                    string table = "TP7H_" + nonce[..24];
                    if (Exists(connection, table)) throw new InvalidOperationException("owned_name_collision");
                    ownership = new(1, nonce, table, "seed_started", true, true, TargetServerVersion,
                        "62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75",
                        "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b",
                        "d909b5af-965f-4cf9-a80a-a6181124e361");
                    // Refuse reuse. The manifest commits the exact absent name before any DDL.
                    using (var manifest = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        JsonSerializer.Serialize(manifest, ownership, JsonOptions);
                    seedOwnsName = true;
                    stage = "seed_create";
                    Exec(connection, $"CREATE TABLE \"{table}\" (\"ID\" INT PRIMARY KEY, \"DT\" TIMESTAMP(7), \"DTO\" DATETIME(7) WITH TIME ZONE)");
                }
                else ownership = LoadOwnership(args[2], mode);
            }
            report["ownership_manifest_sha256"] = Hash(File.ReadAllBytes(args[2]));
            report["owned_table"] = ownership!.Table;
            if (mode == "seed")
            {
                var parameters = new List<object>();
                var typedReads = new List<object>();
                var literals = new List<object>();
                report["typed_parameter_cases"] = parameters;
                report["typed_readback_cases"] = typedReads;
                report["literal_characterization"] = literals;
                for (int index = 0; index < Samples.Length; index++)
                {
                    var sample = Samples[index];
                    stage = "seed_typed_" + sample.Name;
                    parameters.Add(WriteParameters(raw, ownership.Table, index + 1, sample, out bool written, out bool metadataValid));
                    if (!written || !metadataValid) issues.Add("official_public_seed_" + sample.Name);
                    if (written) typedReads.Add(ReadFresh(raw, ownership.Table, index + 1, sample, issues));
                    stage = "seed_literal_" + sample.Name;
                    var characterizationIssues = new List<string>();
                    object? insertion = null, read = null, literalError = null;
                    try
                    {
                        insertion = InsertLiteral(raw, ownership.Table, index + 101, sample);
                        read = ReadFresh(raw, ownership.Table, index + 101, sample, characterizationIssues);
                    }
                    catch (Exception error) { literalError = Error(error); }
                    // Literal conversion differences are recorded, never substituted for typed oracle success.
                    literals.Add(new { sample = sample.Name, evidence = "literal_characterization_not_decoder_gate",
                        insertion, read, differences = characterizationIssues, error = literalError });
                }
                using (var final = OpenVerified(raw)) report["rows_before_handoff"] = Count(final, ownership.Table);
                if (issues.Count == 0)
                {
                    ownership = ownership with { State = "ready_for_W_read" };
                    SaveOwnership(args[2], ownership);
                    seedReady = true;
                    report["status"] = "seed_ready";
                    report["handoff_ready"] = true;
                    report["cleanup_required"] = true;
                }
            }
            else if (mode == "read")
            {
                stage = "cross_read";
                var reads = new List<object>();
                report["cross_typed_readback_cases"] = reads;
                for (int index = 0; index < Samples.Length; index++)
                    reads.Add(ReadFresh(raw, ownership.Table, index + 1, Samples[index], issues));
                using (var final = OpenVerified(raw)) report["rows_after_read"] = Count(final, ownership.Table);
                report["status"] = issues.Count == 0 ? "cross_decode_complete" : "cross_decode_failed";
                report["cleanup_required"] = true;
            }
            else
            {
                stage = "handoff_cleanup";
                CleanupExact(raw, ownership.Table);
                ownership = ownership with { State = "cleanup_verified" };
                SaveOwnership(args[2], ownership);
                report["cleanup_verified"] = true;
                report["final_database_state"] = "unique_owned_table_absent";
                report["status"] = "cleanup_complete";
            }
            try { report["pure_encoders"] = PureEncoders(); }
            catch (Exception error) { report["pure_encoder_failure"] = Error(error); }
        }
        catch (Exception error)
        {
            report["failure_stage"] = stage;
            report["failure"] = Error(error);
            report["server_version"] = observedServerVersion;
            issues.Add("handoff_stage_failed");
        }
        finally
        {
            // A successful seed intentionally hands the exact object to Low's finally cleanup.
            // Any failed seed cleans its own object before returning, including CREATE acknowledgement failure.
            if (mode == "seed" && seedOwnsName && !seedReady && raw is not null && ownership is not null)
            {
                try
                {
                    CleanupExact(raw, ownership.Table);
                    ownership = ownership with { State = "cleanup_verified" };
                    SaveOwnership(args[2], ownership);
                    report["cleanup_verified"] = true;
                    report["final_database_state"] = "unique_owned_table_absent";
                }
                catch (Exception error)
                {
                    report["cleanup_failure"] = Error(error);
                    issues.Add("failed_seed_cleanup_unverified");
                }
            }
        }
        if (issues.Count > 0) report["status"] = "handoff_failed";
        report["issues"] = issues;
        if (File.Exists(args[2])) report["ownership_manifest_sha256"] = Hash(File.ReadAllBytes(args[2]));
        return Finish(args[3], report, issues.Count == 0 ? 0 : 1);
    }

    private static Ownership LoadOwnership(string path, string mode)
    {
        var owner = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("ownership_missing");
        bool validNonce = owner.Nonce.Length == 32 && owner.Nonce.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
        if (owner.SchemaVersion != 1 || !validNonce || owner.Table != "TP7H_" + owner.Nonce[..24]
            || !owner.AbsentBeforeCreate || !owner.IdentityVerified || owner.ServerVersion != TargetServerVersion
            || owner.OwnerPackageHash != "62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75"
            || owner.OwnerDllHash != "8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b"
            || owner.OwnerMvid != "d909b5af-965f-4cf9-a80a-a6181124e361")
            throw new InvalidOperationException("ownership_binding_invalid");
        if (mode == "read" && owner.State != "ready_for_W_read")
            throw new InvalidOperationException("seed_not_ready_for_cross_read");
        if (mode == "cleanup" && owner.State is not ("seed_started" or "ready_for_W_read" or "cleanup_verified"))
            throw new InvalidOperationException("cleanup_ownership_state_invalid");
        return owner;
    }
    private static void SaveOwnership(string path, Ownership ownership)
        => File.WriteAllText(path, JsonSerializer.Serialize(ownership, JsonOptions) + Environment.NewLine);
    private static void CleanupExact(string raw, string table)
    {
        using (var connection = OpenVerified(raw))
            if (Exists(connection, table)) Exec(connection, $"DROP TABLE \"{table}\"");
        using var fresh = OpenVerified(raw);
        if (Exists(fresh, table)) throw new InvalidOperationException("owned_table_still_present");
    }
    private static object InsertLiteral(string raw, string table, int id, Sample sample)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand(); command.CommandTimeout = 30;
        string local = sample.Value.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        string offset = sample.Value.ToString("zzz", CultureInfo.InvariantCulture);
        command.CommandText = $"INSERT INTO \"{table}\" (\"ID\",\"DT\",\"DTO\") VALUES ({id}, CAST('{local}' AS TIMESTAMP(7)), CAST('{local} {offset}' AS DATETIME(7) WITH TIME ZONE))";
        command.ExecuteNonQuery();
        return new { processing = ProcessingMetadata(connection), sql_hashes = SqlHashes(command),
            literal_fraction_digits = 7, literal_fraction_ticks = sample.Value.Ticks % TimeSpan.TicksPerSecond };
    }

    private static void CheckPackage(string packagePath, Dictionary<string, object?> report)
    {
        var assembly = typeof(DriverConnection).Assembly;
        string packageHash = Hash(File.ReadAllBytes(packagePath));
        string dllHash = Hash(File.ReadAllBytes(assembly.Location));
        string mvid = assembly.ManifestModule.ModuleVersionId.ToString("D");
        using var archive = ZipFile.OpenRead(packagePath);
        using var stream = (archive.GetEntry(AssetPath) ?? throw new InvalidOperationException("asset_missing")).Open();
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        string assetHash = Hash(bytes.ToArray());
        report["package_sha256"] = packageHash; report["assembly_sha256"] = dllHash;
        report["assembly_mvid"] = mvid; report["asset"] = AssetPath;
        if (packageHash != PackageHash || dllHash != DllHash || assetHash != DllHash || mvid != Mvid)
            throw new InvalidOperationException("frozen_package_identity_mismatch");
        report["package_identity_verified"] = true;
    }

    private static DriverConnection OpenVerified(string raw)
    {
#if OFFICIAL
        var connection = new DriverConnection(raw);
#else
        var settings = new W.Dm.DmConnectionStringBuilder(raw)
        {
            TransportSecurity = W.Dm.DmTransportSecurity.PlaintextAllowed,
            PersistSecurityInfo = false, Schema = TestUser
        };
        var connection = new DriverConnection(settings.ConnectionString);
#endif
        try
        {
            connection.Open();
            string version = connection.ServerVersion.Trim();
            observedServerVersion = version.Length <= 32 && version.All(character => char.IsAsciiDigit(character) || character == '.')
                ? version : "unrecognized";
            if (version != TargetServerVersion) throw new InvalidOperationException("target_server_version_mismatch");
            string? user = Scalar(connection, "SELECT USER FROM DUAL") as string;
            string? schema = Scalar(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL") as string;
            if (user?.Trim() != TestUser || schema?.Trim() != TestUser)
                throw new InvalidOperationException("server_test_identity_mismatch");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static string ConfiguredTestConnection()
    {
        string? raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("test_connection_missing");
        var builder = new DbConnectionStringBuilder { ConnectionString = raw };
        if (!builder.Keys.Cast<string>().Any(key => key.Replace(" ", "", StringComparison.Ordinal).Equals("userid", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Convert.ToString(builder[key], CultureInfo.InvariantCulture), TestUser, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("configured_test_identity_invalid");
        return raw;
    }

    private static object WriteParameters(string raw, string table, int id, Sample sample, out bool written, out bool metadataValid)
    {
        written = false; metadataValid = false;
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = $"INSERT INTO \"{table}\" (\"ID\",\"DT\",\"DTO\") VALUES ({id}, :dt, :dto)";
        foreach (var entry in new[] { (Name: "dt", Type: DbType.DateTime2, Value: (object)sample.Value.DateTime), (Name: "dto", Type: DbType.DateTimeOffset, Value: (object)sample.Value) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = entry.Name; parameter.DbType = entry.Type; parameter.Precision = 7; parameter.Value = entry.Value;
            command.Parameters.Add(parameter);
        }
        object? error = null; bool prepared = false, executeAttempted = false;
        Metadata[] metadata = [];
        try
        {
            command.Prepare(); prepared = true;
            metadata = CaptureParameters(command);
            metadataValid = metadata.Length == 2 && metadata[0].CType == 26 && metadata[1].CType == 27
                && metadata.All(item => item.Scale == 7 && item.TypeFlag == 1);
            executeAttempted = true;
            written = command.ExecuteNonQuery() == 1;
        }
        catch (Exception failure) { error = Error(failure); }
        return new { sample = sample.Name, expected_ticks = sample.Value.Ticks, expected_offset_minutes = (int)sample.Value.Offset.TotalMinutes,
            requested_db_types = new[] { "DateTime2", "DateTimeOffset" }, requested_precision = 7,
            prepared, metadata, execute_attempted = executeAttempted, written, error,
            processing = ProcessingMetadata(connection), sql_hashes = SqlHashes(command),
            evidence = Lane == "O" ? "official_public_parameter_path" : "frozen_W_public_characterization" };
    }

    private sealed record Metadata(bool Available, int? CType, int? TypeFlag, int? Precision, int? Scale, int? Mask);
    private static Metadata[] CaptureParameters(DbCommand command)
    {
        try
        {
            object? statement = command.GetType().GetField("m_Stmt", Flags)?.GetValue(command);
            object? info = statement?.GetType().GetMethod("H", Flags, null, Type.EmptyTypes, null)?.Invoke(statement, null);
            object? values = info?.GetType().GetMethod("GetParamsInfo", Flags, null, Type.EmptyTypes, null)?.Invoke(info, null);
            return values is Array parameters ? parameters.Cast<object>().Select(CaptureMetadata).ToArray() : [];
        }
        catch { return []; }
    }
    private static Metadata CaptureMetadata(object value)
    {
        int? Call(string method) => value.GetType().GetMethod(method, Flags, null, Type.EmptyTypes, null)?.Invoke(value, null) as int?;
        try
        {
            object? flag = value.GetType().GetMethod("GetTypeFlag", Flags, null, Type.EmptyTypes, null)?.Invoke(value, null);
            return new(true, Call("GetCType"), flag is byte b ? b : flag as int?, Call("GetPrecision"), Call("GetScale"), value.GetType().GetField("mask", Flags)?.GetValue(value) as int?);
        }
        catch { return new(false, null, null, null, null, null); }
    }

    private static object ReadFresh(string raw, string table, int id, Sample sample, List<string> issues)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = $"SELECT \"DT\",\"DTO\", TO_CHAR(\"DT\",'YYYY-MM-DD HH24:MI:SS.FF7'), TO_CHAR(\"DTO\",'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM') FROM \"{table}\" WHERE \"ID\"={id}";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("row_missing");
        string serverDate = reader.GetString(2), serverOffset = reader.GetString(3);
        bool dateTextExact = serverDate.Trim() == sample.Value.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        bool offsetTextExact = serverOffset.Trim() == sample.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture);
        if (!dateTextExact || !offsetTextExact) issues.Add("server_seven_digit_or_offset_oracle_" + sample.Name);
        var date = Observe(() => reader.GetDateTime(0));
        var genericDate = Observe(() => reader.GetFieldValue<DateTime>(0));
        var offset = Observe(() => reader.GetType().GetMethod("GetDateTimeOffset", Flags, null, [typeof(int)], null)?.Invoke(reader, [1])
            ?? throw new NotSupportedException("typed_offset_getter_missing"));
        var genericOffset = Observe(() => reader.GetFieldValue<DateTimeOffset>(1));
        var rawOffset = Observe(() => reader.GetValue(1));
        string? clientText = null;
        object? clientTextError = null;
        try { clientText = reader.GetString(1); }
        catch (Exception error) { clientTextError = Error(error); }
        bool parseExact = clientText is not null && DateTimeOffset.TryParse(clientText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            && parsed.Ticks == sample.Value.Ticks && parsed.Offset == sample.Value.Offset;
#if !OFFICIAL
        if (date.Ticks != sample.Value.Ticks || date.Kind != "Unspecified" || genericDate.Ticks != sample.Value.Ticks || genericDate.Kind != "Unspecified" || offset.Ticks != sample.Value.Ticks
            || offset.OffsetMinutes != (int)sample.Value.Offset.TotalMinutes || genericOffset.Ticks != sample.Value.Ticks
            || genericOffset.OffsetMinutes != (int)sample.Value.Offset.TotalMinutes || !parseExact)
            issues.Add("W_extended_decode_" + sample.Name);
#endif
        object? columns = reader.GetType().GetField("m_ColInfo", Flags)?.GetValue(reader);
        Metadata[] metadata = columns is Array cols ? cols.Cast<object>().Take(2).Select(CaptureMetadata).ToArray() : [];
        if (metadata.Length != 2 || metadata[0].CType != 26 || metadata[1].CType != 27) issues.Add("read_column_wire_metadata_" + sample.Name);
        return new { sample = sample.Name, expected_ticks = sample.Value.Ticks, expected_offset_minutes = (int)sample.Value.Offset.TotalMinutes,
            metadata, server_date_exact = dateTextExact, server_offset_exact = offsetTextExact,
            server_date_hash = Hash(System.Text.Encoding.UTF8.GetBytes(serverDate)), server_offset_hash = Hash(System.Text.Encoding.UTF8.GetBytes(serverOffset)),
            typed_date = date, generic_date = genericDate, typed_offset = offset, generic_offset = genericOffset, raw_offset = rawOffset,
            processing = ProcessingMetadata(connection), sql_hashes = SqlHashes(command),
            client_text_parse_exact = parseExact, client_text_error = clientTextError,
            client_text_hash = clientText is null ? null : Hash(System.Text.Encoding.UTF8.GetBytes(clientText)) };
    }

    private sealed record GetterObservation(string Outcome, string? Type, long? Ticks, int? OffsetMinutes, string? Kind, object? Error);
    private static GetterObservation Observe(Func<object?> getter)
    {
        try
        {
            object? value = getter();
            return value switch
            {
                DateTime date => new("returned", typeof(DateTime).FullName, date.Ticks, null, date.Kind.ToString(), null),
                DateTimeOffset offset => new("returned", typeof(DateTimeOffset).FullName, offset.Ticks, (int)offset.Offset.TotalMinutes, null, null),
                _ => new("returned", value?.GetType().FullName, null, null, null, null)
            };
        }
        catch (Exception error) { return new("error", null, null, null, null, Error(error)); }
    }
    private static object[] PureEncoders()
    {
        var type = typeof(DriverConnection).Assembly.GetType(TemporalClass) ?? throw new InvalidOperationException("temporal_helper_missing");
        return Samples.Select(sample =>
        {
            object?[] dateArgs = [null, sample.Value.DateTime];
            object?[] offsetArgs = [null, sample.Value.DateTime, (short)sample.Value.Offset.TotalMinutes];
            type.GetMethod("DmdtEncodeFast2", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, dateArgs);
            type.GetMethod("Dmdt2TzEncodeFast2", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, offsetArgs);
            if (dateArgs[0] is not byte[] date || offsetArgs[0] is not byte[] offset || date.Length != 9 || offset.Length != 11)
                throw new InvalidOperationException("pure_extended_encoder_unavailable");
            return (object)new { sample = sample.Name, expected_ticks = sample.Value.Ticks, expected_offset_minutes = (int)sample.Value.Offset.TotalMinutes,
                date_length = date.Length, offset_length = offset.Length, date_hex = Convert.ToHexString(date), offset_hex = Convert.ToHexString(offset),
                date_nanoseconds = WireNanoseconds(date), offset_nanoseconds = WireNanoseconds(offset),
                date_hash = Hash(date), offset_hash = Hash(offset), evidence = "synthetic_helper_only_not_public_write_acceptance" };
        }).ToArray();
    }
    private static object Error(Exception error)
    {
        if (error is TargetInvocationException { InnerException: { } inner }) error = inner;
        object? number = null;
        try { number = error.GetType().GetProperty("Number", Flags)?.GetValue(error) is int n ? n : null; } catch { }
        return new { kind = error.GetType().FullName, number };
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static int WireNanoseconds(byte[] wire) => ((wire[5] >> 1) & 0x7F) + (wire[6] << 7) + (wire[7] << 15) + ((wire[8] & 0x7F) << 23);

    private static object ProcessingMetadata(DriverConnection connection)
    {
        try
        {
            object? properties = connection.GetType().GetProperty("ConnProperty", Flags)?.GetValue(connection);
            object? c2p = properties?.GetType().GetProperty("C2p", Flags)?.GetValue(properties);
            object? escape = properties?.GetType().GetProperty("EscapeProcess", Flags)?.GetValue(properties);
            return new { c2p = c2p is byte number ? (int?)number : c2p as int?, escape_process = escape as bool? };
        }
        catch { return new { c2p = (int?)null, escape_process = (bool?)null }; }
    }

    private static object SqlHashes(DbCommand command)
    {
        string? actual = null;
        try
        {
            object? statement = command.GetType().GetField("m_Stmt", Flags)?.GetValue(command);
            actual = statement?.GetType().GetMethod("C", Flags, null, Type.EmptyTypes, null)?.Invoke(statement, null) as string;
        }
        catch { }
        return new { requested_sha256 = Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText)),
            statement_sha256 = actual is null ? null : Hash(System.Text.Encoding.UTF8.GetBytes(actual)) };
    }
    private static object? Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30;
        return command.ExecuteScalar();
    }
    private static void Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30;
        command.ExecuteNonQuery();
    }
    private static bool Exists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand(); command.CommandTimeout = 30;
        command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.DbType = DbType.String; parameter.Value = table;
        command.Parameters.Add(parameter); return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }
    private static long Count(DbConnection connection, string table) => Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM \"{table}\""), CultureInfo.InvariantCulture);
    private static long CountId(DbConnection connection, string table, int id) => Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM \"{table}\" WHERE \"ID\"={id}"), CultureInfo.InvariantCulture);
    private static int Finish(string? path, Dictionary<string, object?> report, int exitCode)
    {
        string json = JsonSerializer.Serialize(report, JsonOptions);
        try { if (path is not null) File.WriteAllText(path, json + Environment.NewLine); else Console.WriteLine(json); }
        catch { Console.WriteLine("{\"status\":\"safe_report_write_failed\"}"); return 2; }
        return exitCode;
    }
}
