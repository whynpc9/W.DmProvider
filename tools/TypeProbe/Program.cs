using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if CANDIDATE
using W.Dm.Internal.Legacy.A;
#endif
#if OFFICIAL
using DriverConnection = Dm.DmConnection;
using DriverNumeric = Dm.DmdbNumeric;
using DriverXDec = Dm.DmXDec;
using DriverParameter = Dm.DmParameter;
using DriverDbType = Dm.DmDbType;
#else
using DriverConnection = W.Dm.DmConnection;
using DriverNumeric = W.Dm.DmdbNumeric;
using DriverXDec = W.Dm.DmXDec;
using DriverParameter = W.Dm.DmParameter;
using DriverDbType = W.Dm.DmDbType;
#endif

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const string Unicode = "T09-中文-Ω-😀-e\u0301";
    private const string ValidGuid = "00112233-4455-6677-8899-aabbccddeeff";
    private const string InvalidGuid = "00112233-4455-6677-8899-aabbccddeefg";
    private const string PreciseDecimal = "123456789012345678.12345678901234567890";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private enum SyntheticSigned : int { Negative = -10, Positive = 7, Large = 1000 }
    [Flags] private enum SyntheticFlags : uint { Read = 1, Write = 2, Audit = 4 }
    private enum SyntheticByte : byte { Max = byte.MaxValue }
    private enum SyntheticUnsignedLong : ulong { Max = ulong.MaxValue }

    public static int Main(string[] args)
    {
        string implementation =
#if OFFICIAL
            "O";
#elif FROZEN
            "W-T08-frozen";
#else
            "W-T09-candidate";
#endif
        var assembly = typeof(DriverConnection).Assembly;
        var report = new Dictionary<string, object?>
        {
            ["schema_version"] = 1,
            ["task"] = "T09",
            ["implementation"] = implementation,
            ["evidence_kind"] = "observed_profile",
            ["assembly_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            ["assembly_mvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            ["runtime"] = Environment.Version.ToString(),
            ["run_id"] = Guid.NewGuid().ToString("N"),
            ["fixture_execution_status"] = "not_run",
            ["declared_charset_profile"] = "unverified",
            ["cases"] = new Dictionary<string, object?>(),
        };
        if (args.Length == 1 && args[0] == "codec")
        {
            report["evidence_kind"] = "offline_legacy_codec_characterization";
            report["legacy_wire_vectors"] = new Dictionary<string, string>
            {
                ["0.1"] = Convert.ToHexString(DriverNumeric.valueOf(0.1m, 0, -1).encode(false)),
                ["-0.1"] = Convert.ToHexString(DriverNumeric.valueOf(-0.1m, 0, -1).encode(false)),
                ["0.01"] = Convert.ToHexString(DriverNumeric.valueOf(0.01m, 0, -1).encode(false)),
                ["-0.01"] = Convert.ToHexString(DriverNumeric.valueOf(-0.01m, 0, -1).encode(false)),
                ["1.23"] = Convert.ToHexString(DriverNumeric.valueOf(1.23m, 0, -1).encode(false)),
                ["-1.23"] = Convert.ToHexString(DriverNumeric.valueOf(-1.23m, 0, -1).encode(false)),
            };
            var xdec = new DriverXDec();
            var xdecInputs = new[] { "0.1", "-0.1", "0.01", "-0.01", "1.23", "-1.23" };
            report["xdec_wire_vectors"] = xdecInputs.ToDictionary(value => value,
                value => Convert.ToHexString(xdec.StrToDec(value, 0, 0, dmxdec_direct: true)));
            report["xdec_parse_text"] = xdecInputs.ToDictionary(value => value,
                value => new DriverXDec().Parse(value).ToString());
            return Finish(report, "observed", null, 0);
        }
        if (args.Length != 1 || args[0] != "profile")
            return Finish(report, "rejected", "usage", 2);
        var raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(raw))
            return Finish(report, "rejected", "test_connection_missing", 2);
        if (!raw.Contains("User Id=" + TestUser + ";", StringComparison.OrdinalIgnoreCase))
            return Finish(report, "rejected", "configured_test_identity_missing", 2);

        var table = "T09_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var cases = (Dictionary<string, object?>)report["cases"]!;
        bool serverIdentity = false, created = false, cleanup = false;
        string? failureStage = null;
        object? failure = null;
        try
        {
            using (var connection = OpenVerified(raw))
            {
                serverIdentity = true;
                report["server_version"] = connection.ServerVersion;
                report["negotiated_charset"] = NegotiatedCharset(connection);
                // A successful Unicode row establishes only this negotiated server profile.
                report["server_account_verified"] = true;
                Exec(connection, $"CREATE TABLE {table} (ID INT PRIMARY KEY, NUMVAL DECIMAL(38,20), TEXTVAL VARCHAR(200), INTVAL INT, GUIDTXT VARCHAR(50), GUIDBIN VARBINARY(16), GUIDSHORT VARBINARY(15), PAYLOAD BLOB)");
                created = true;
            }

            cases["TYP-01"] = new Dictionary<string, object?>
            {
                ["enum_negative"] = Probe(() => ScalarParameter(raw, SyntheticSigned.Negative)),
                ["enum_positive"] = Probe(() => ScalarParameter(raw, SyntheticSigned.Positive)),
                ["enum_large"] = Probe(() => ScalarParameter(raw, SyntheticSigned.Large)),
                ["flags"] = Probe(() => ScalarParameter(raw, SyntheticFlags.Read | SyntheticFlags.Audit)),
                ["byte_underlying"] = Probe(() => ScalarParameter(raw, SyntheticByte.Max)),
                ["ulong_underlying"] = Probe(() => ScalarParameter(raw, SyntheticUnsignedLong.Max)),
                ["ulong_enum_explicit_string"] = Probe(() => ScalarParameter(raw, SyntheticUnsignedLong.Max, DbType.String)),
                ["ulong_enum_explicit_string_cast"] = Probe(() => ScalarParameter(raw, SyntheticUnsignedLong.Max, DbType.String,
                    "SELECT CAST(:p0 AS VARCHAR(30)) FROM DUAL")),
                ["ulong_enum_bare_metadata"] = Probe(() => UnsignedBindingCase(raw, SyntheticUnsignedLong.Max, castDecimal: false)),
                ["ulong_enum_cast_decimal_metadata"] = Probe(() => UnsignedBindingCase(raw, SyntheticUnsignedLong.Max, castDecimal: true)),
            };
            cases["TYP-02"] = new Dictionary<string, object?>
            {
                ["sbyte_min"] = Probe(() => ScalarParameter(raw, sbyte.MinValue)),
                ["byte_max"] = Probe(() => ScalarParameter(raw, byte.MaxValue)),
                ["int16_min"] = Probe(() => ScalarParameter(raw, short.MinValue)),
                ["uint16_max"] = Probe(() => ScalarParameter(raw, ushort.MaxValue)),
                ["int32_min"] = Probe(() => ScalarParameter(raw, int.MinValue)),
                ["int32_max"] = Probe(() => ScalarParameter(raw, int.MaxValue)),
                ["int64_min"] = Probe(() => ScalarParameter(raw, long.MinValue)),
                ["int64_max"] = Probe(() => ScalarParameter(raw, long.MaxValue)),
                ["uint32_max"] = Probe(() => ScalarParameter(raw, uint.MaxValue)),
                ["uint64_max"] = Probe(() => ScalarParameter(raw, ulong.MaxValue)),
                ["ulong_explicit_string"] = Probe(() => ScalarParameter(raw, ulong.MaxValue, DbType.String)),
                ["ulong_explicit_string_cast"] = Probe(() => ScalarParameter(raw, ulong.MaxValue, DbType.String,
                    "SELECT CAST(:p0 AS VARCHAR(30)) FROM DUAL")),
                ["double_explicit_single"] = Probe(() => ScalarParameter(raw, 1.5d, DbType.Single)),
                ["double_explicit_single_cast"] = Probe(() => ScalarParameter(raw, 1.5d, DbType.Single,
                    "SELECT CAST(:p0 AS REAL) FROM DUAL")),
                ["ulong_bare_metadata"] = Probe(() => UnsignedBindingCase(raw, ulong.MaxValue, castDecimal: false)),
                ["ulong_cast_decimal_metadata"] = Probe(() => UnsignedBindingCase(raw, ulong.MaxValue, castDecimal: true)),
                ["explicit_byte_256_rejected"] = Probe(() => PreSendFailure(raw, 256, DbType.Byte)),
                ["explicit_uint16_negative_rejected"] = Probe(() => PreSendFailure(raw, -1, DbType.UInt16)),
                ["explicit_uint16_65536_rejected"] = Probe(() => PreSendFailure(raw, 65536, DbType.UInt16)),
                ["explicit_uint32_negative_rejected"] = Probe(() => PreSendFailure(raw, -1, DbType.UInt32)),
                ["explicit_uint64_negative_rejected"] = Probe(() => PreSendFailure(raw, -1, DbType.UInt64)),
                ["explicit_byte_max"] = Probe(() => ScalarParameter(raw, byte.MaxValue, DbType.Byte)),
                ["explicit_uint16_max"] = Probe(() => ScalarParameter(raw, ushort.MaxValue, DbType.UInt16)),
                ["explicit_uint32_max"] = Probe(() => ScalarParameter(raw, uint.MaxValue, DbType.UInt32)),
                ["explicit_uint64_max"] = Probe(() => ScalarParameter(raw, ulong.MaxValue, DbType.UInt64)),
                ["explicit_int32_overflow"] = Probe(() => PreSendFailure(raw, ulong.MaxValue, DbType.Int32)),
                ["nonintegral_getint32"] = Probe(() => ReadGetter(raw, "SELECT CAST(1.5 AS DOUBLE) FROM DUAL",
                    r => Probe(() => r.GetInt32(0)))),
                ["double_getbyte"] = Probe(() => ReadGetter(raw, "SELECT CAST(7 AS DOUBLE) FROM DUAL", r => r.GetByte(0))),
                ["nan_input"] = Probe(() => PreSendFailure(raw, double.NaN, DbType.Double)),
                ["infinity_input"] = Probe(() => PreSendFailure(raw, double.PositiveInfinity, DbType.Double)),
            };
            cases["TYP-03"] = new Dictionary<string, object?>
            {
                ["decimal_input"] = Probe(() => InsertDecimal(raw, table, 1, 123456789012345678.1234567890m)),
                ["ado_decimal_positive_tenth"] = Probe(() => InsertDecimal(raw, table, 3, 0.1m)),
                ["ado_decimal_negative_tenth"] = Probe(() => InsertDecimal(raw, table, 4, -0.1m)),
                ["ado_decimal_positive_hundredth"] = Probe(() => InsertDecimal(raw, table, 5, 0.01m)),
                ["ado_decimal_negative_hundredth"] = Probe(() => InsertDecimal(raw, table, 6, -0.01m)),
                ["ado_decimal_positive_1_23"] = Probe(() => InsertDecimal(raw, table, 7, 1.23m)),
                ["ado_decimal_negative_1_23"] = Probe(() => InsertDecimal(raw, table, 8, -1.23m)),
                ["decimal38_20"] = Probe(() => ReadDecimal(raw, PreciseDecimal)),
                ["small_negative"] = Probe(() => ReadDecimal(raw, "-0.00000000000000000001")),
                ["outside_clr_decimal"] = Probe(() => ReadDecimal(raw, "999999999999999999.99999999999999999999")),
                ["float_profile_separate_from_decimal"] = Probe(() =>
                    ReadGetter(raw, "SELECT CAST(1.25 AS FLOAT) FROM DUAL", reader => Capture(reader))),
            };
            cases["TYP-04"] = new Dictionary<string, object?>
            {
                ["guid_shaped_text"] = Probe(() => ReadGuidText(raw, ValidGuid)),
                ["invalid_getguid"] = Probe(() => ReadGuidText(raw, InvalidGuid)),
                ["nullable_guid_columns"] = Probe(() => GuidColumnCase(raw, table, 9, null, null)),
                ["varchar50_guid_d_format"] = Probe(() => GuidColumnCase(raw, table, 10, ValidGuid, null)),
                ["binary16_guid"] = Probe(() => GuidColumnCase(raw, table, 11, null, Guid.Parse(ValidGuid).ToByteArray())),
                ["invalid_binary15_guid"] = Probe(() => GuidColumnCase(raw, table, 12, null, new byte[15])),
                ["clr_guid_to_varchar50"] = Probe(() => GuidBindingCase(raw, table, 10, "GUIDTXT", DbType.String)),
                ["clr_guid_to_binary16"] = Probe(() => GuidBindingCase(raw, table, 11, "GUIDBIN", DbType.Binary)),
                ["clr_guid_to_binary15_rejected"] = Probe(() => GuidBindingCase(raw, table, 11, "GUIDSHORT", DbType.Binary)),
            };
            cases["TYP-05"] = new Dictionary<string, object?>
            {
                ["unicode_roundtrip"] = Probe(() => InsertText(raw, table, 2, Unicode)),
                ["unpaired_surrogate"] = Probe(() => PreSendFailure(raw, "\uD800", DbType.String)),
            };
            cases["TYP-06"] = new Dictionary<string, object?>
            {
                ["timestamp_profile"] = Probe(() => ReadTemporal(raw,
                    "SELECT CAST('2024-02-29 12:34:56.123456' AS TIMESTAMP(6)) FROM DUAL")),
                ["interval_profile"] = Probe(() => ReadTemporal(raw,
                    "SELECT CAST('2 03:04:05.123456' AS INTERVAL DAY(9) TO SECOND(6)) FROM DUAL")),
                ["dateonly_parameter"] = Probe(() => ScalarParameter(raw, new DateOnly(2024, 2, 29))),
                ["timeonly_parameter"] = Probe(() => ScalarParameter(raw, new TimeOnly(12, 34, 56, 123).Add(TimeSpan.FromTicks(4567)))),
                ["datetimeoffset_parameter"] = Probe(() => ScalarParameter(raw,
                    new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567))),
                ["dateonly_explicit"] = Probe(() => TemporalExplicitCase(raw, new DateOnly(2024, 2, 29),
                    DriverDbType.Date, 0)),
                ["timeonly_six_digits"] = Probe(() => TemporalExplicitCase(raw,
                    new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234560)), DriverDbType.Time, 6)),
                ["timeonly_seventh_digit_rejected"] = Probe(() => TemporalExplicitCase(raw,
                    new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234567)), DriverDbType.Time, 6)),
                ["dto_positive_six_digits"] = Probe(() => TemporalExplicitCase(raw,
                    new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234560),
                    DriverDbType.DateTimeOffset, 6)),
                ["dto_negative_six_digits"] = Probe(() => TemporalExplicitCase(raw,
                    new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5)).AddTicks(1234560),
                    DriverDbType.DateTimeOffset, 6)),
                ["dto_seventh_digit_rejected"] = Probe(() => TemporalExplicitCase(raw,
                    new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567),
                    DriverDbType.DateTimeOffset, 6)),
                ["negative_multiday_timespan"] = Probe(() => ScalarParameter(raw,
                    -(TimeSpan.FromDays(2) + TimeSpan.FromHours(3) + TimeSpan.FromTicks(1234560)))),
                ["interval_positive_one_microsecond"] = Probe(() => IntervalParameter(raw, TimeSpan.FromTicks(10))),
                ["interval_explicit_scale6_bare"] = Probe(() => IntervalParameter(raw, TimeSpan.FromTicks(10), 6)),
                ["interval_negative_one_microsecond"] = Probe(() => IntervalParameter(raw, TimeSpan.FromTicks(-10))),
                ["interval_negative_multiday_fraction"] = Probe(() => IntervalParameter(raw,
                    -(TimeSpan.FromDays(2) + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(4) +
                      TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(1234560)))),
                ["interval_scale4_small_fraction"] = Probe(() => IntervalScaleCase(raw, "0.0001", 4, 1000)),
                ["interval_scale3_small_fraction"] = Probe(() => IntervalScaleCase(raw, "0.001", 3, 10000)),
                ["interval_scale4_trailing_zero"] = Probe(() => IntervalScaleCase(raw, "0.1000", 4, 1000000)),
                ["interval_timespan_minvalue"] = Probe(() => IntervalParameter(raw, TimeSpan.MinValue)),
                ["interval_bare_prepare_metadata"] = Probe(() => IntervalMetadataCase(raw, castInterval: false)),
                ["interval_cast_prepare_metadata"] = Probe(() => IntervalMetadataCase(raw, castInterval: true)),
            };
            cases["TYP-07"] = new Dictionary<string, object?>
            {
                ["typed_null"] = Probe(() => ScalarParameter(raw, DBNull.Value, DbType.Int32)),
                ["untyped_null"] = Probe(() => PreSendFailure(raw, DBNull.Value, null)),
                ["null_untyped_select_metadata"] = Probe(() => NullBindingCase(raw, table,
                    "SELECT :p0 FROM DUAL", explicitInt32: false, insert: false, ordinal: 0)),
                ["null_int32_select_metadata"] = Probe(() => NullBindingCase(raw, table,
                    "SELECT :p0 FROM DUAL", explicitInt32: true, insert: false, ordinal: 0)),
                ["null_cast_int_select_metadata"] = Probe(() => NullBindingCase(raw, table,
                    "SELECT CAST(:p0 AS INT) FROM DUAL", explicitInt32: false, insert: false, ordinal: 0)),
                ["null_int_column_insert_metadata"] = Probe(() => NullBindingCase(raw, table,
                    $"INSERT INTO {table}(ID,INTVAL) VALUES (:p0,:p1)", explicitInt32: false, insert: true, ordinal: 1)),
                ["empty_text"] = Probe(() => ScalarParameter(raw, "")),
                ["empty_binary"] = Probe(() => ScalarParameter(raw, Array.Empty<byte>())),
                ["binary_overflow"] = Probe(() => BinaryOverflowCase(raw, table, DriverDbType.Binary)),
                ["varbinary_overflow"] = Probe(() => BinaryOverflowCase(raw, table, DriverDbType.VarBinary)),
                ["blob_overflow"] = Probe(() => BinaryOverflowCase(raw, table, DriverDbType.Blob)),
            };
            cases["TYP-08"] = new Dictionary<string, object?>
            {
                ["duplicate_names"] = Probe(DuplicateNames),
                ["named_marker_ignores_literal_comment"] = Probe(() => MarkerCase(raw)),
                ["mixed_named_positional"] = Probe(() => MixedMarkers(raw)),
                ["prepare_value_change"] = Probe(() => PreparedValues(raw, changeType: false, changeSize: false)),
                ["prepare_type_change"] = Probe(() => PreparedValues(raw, changeType: true, changeSize: false)),
                ["prepare_size_change"] = Probe(() => PreparedValues(raw, changeType: false, changeSize: true)),
                ["reset_dbtype"] = Probe(ResetDbType),
            };
            cases["TYP-09"] = new Dictionary<string, object?>
            {
                ["sync_async_text"] = Probe(() => SyncAsyncParity(raw, "SELECT CAST('T09_PARITY' AS VARCHAR(32)) FROM DUAL")),
                ["sync_async_timestamp"] = Probe(() => SyncAsyncParity(raw,
                    "SELECT CAST('2024-02-29 12:34:56.123456' AS TIMESTAMP(6)) FROM DUAL")),
                ["typed_null_string"] = Probe(() => TypedNumericParity<string>(raw,
                    "SELECT CAST(NULL AS VARCHAR(50)) FROM DUAL")),
                ["typed_null_object"] = Probe(() => TypedNumericParity<object>(raw,
                    "SELECT CAST(NULL AS VARCHAR(50)) FROM DUAL")),
                ["typed_nonintegral_int32"] = Probe(() => TypedNumericParity<int>(raw,
                    "SELECT CAST(1.5 AS DECIMAL(2,1)) FROM DUAL")),
                ["typed_overflow_int32"] = Probe(() => TypedNumericParity<int>(raw,
                    "SELECT CAST(2147483648 AS DECIMAL(10,0)) FROM DUAL")),
                ["typed_uint64_max"] = Probe(() => TypedNumericParity<ulong>(raw,
                    "SELECT CAST(18446744073709551615 AS DECIMAL(20,0)) FROM DUAL")),
                ["typed_highprecision_nonintegral_int32"] = Probe(() => TypedNumericParity<int>(raw,
                    "SELECT CAST(123456789012345678.12345678901234567890 AS DECIMAL(38,20)) FROM DUAL")),
                ["sync_async_invalid_guid"] = Probe(() => SyncAsyncInvalidGuid(raw)),
                ["lob_text_profile"] = Probe(() => ReadTemporal(raw, "SELECT CAST('T09_LOB' AS CLOB) FROM DUAL")),
                ["complex_array_rejected"] = Probe(() => PreSendFailure(raw, new[] { 1, 2, 3 }, DbType.Object)),
            };

            using var final = OpenVerified(raw);
            report["final_database_state_before_cleanup"] = new
            {
                row_count = Convert.ToInt32(Scalar(final, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture),
                object_present = ObjectExists(final, table)
            };
        }
        catch (Exception ex)
        {
            failureStage = serverIdentity ? "profile_or_final_state" : "server_identity";
            failure = Error(ex);
        }
        finally
        {
            if (serverIdentity)
            {
                try
                {
                    using var final = OpenVerified(raw);
                    if (created && ObjectExists(final, table)) Exec(final, $"DROP TABLE {table}");
                    cleanup = !ObjectExists(final, table);
                }
                catch (Exception ex)
                {
                    report["cleanup_error"] = Error(ex);
                }
            }
            report["server_account_verified"] = serverIdentity;
            report["cleanup_verified"] = cleanup;
            report["final_database_state"] = cleanup ? "unique_object_absent" : "cleanup_unverified";
        }
        if (failure is not null)
        {
            report["failure_stage"] = failureStage;
            report["failure"] = failure;
        }
        return Finish(report, failure is null && cleanup ? "observed" : "rejected",
            failure is null && cleanup ? null : "profile_or_cleanup_failed", failure is null && cleanup ? 0 : 1);
    }

    private static int Finish(Dictionary<string, object?> report, string status, string? reason, int code)
    {
        report["status"] = status;
        if (reason is not null) report["reason"] = reason;
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return code;
    }

    private static object Probe(Func<object?> action)
    {
        try { return new { outcome = "returned", observation = action() }; }
        catch (Exception ex) { return new { outcome = "error", error = Error(ex) }; }
    }

    private static object Error(Exception ex)
    {
        // Never serialize exception messages, SQL, parameters, or raw connection metadata.
        int? number = null;
        try
        {
            if (ex.GetType().GetProperty("Number", BindingFlags.Public | BindingFlags.Instance)?.GetValue(ex) is int value)
                number = value;
        }
        catch { /* Diagnostic extraction must not change the observed outcome. */ }
        return new { kind = ex.GetType().FullName, number };
    }

    private static DriverConnection OpenVerified(string raw)
    {
#if OFFICIAL
        var connection = new DriverConnection(raw);
#else
        var builder = new W.Dm.DmConnectionStringBuilder(raw)
        {
            TransportSecurity = W.Dm.DmTransportSecurity.PlaintextAllowed,
            PersistSecurityInfo = false,
            Schema = TestUser
        };
        var connection = new DriverConnection(builder.ConnectionString);
#endif
        try
        {
            connection.Open();
            if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("test_identity_invalid");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static string? NegotiatedCharset(DriverConnection connection)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var property = connection.GetType().GetProperty("ConnProperty", flags)?.GetValue(connection);
            return property?.GetType().GetProperty("ServerEncoding", flags)?.GetValue(property) as string;
        }
        catch { return null; }
    }

    private static object? Scalar(DbConnection connection, string sql, params object?[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++) Add(command, "p" + i, values[i]);
        return command.ExecuteScalar();
    }

    private static void Exec(DbConnection connection, string sql, params object?[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++) Add(command, "p" + i, values[i]);
        command.ExecuteNonQuery();
    }

    private static DbParameter Add(DbCommand command, string name, object? value, DbType? type = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        if (type is not null) parameter.DbType = type.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static bool ObjectExists(DbConnection connection, string name)
        => Convert.ToInt32(Scalar(connection,
            "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", name), CultureInfo.InvariantCulture) != 0;

    private static object ScalarParameter(string raw, object value, DbType? type = null,
        string sql = "SELECT :p0 FROM DUAL")
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Add(command, "p0", value, type);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Capture(reader) : new { rows = 0 };
    }

    private static object PreSendFailure(string raw, object value, DbType? type)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT :p0 FROM DUAL";
        var before = WireSendCount();
#if CANDIDATE
        var responseOpcodes = new List<short>();
        DmResultProtocolTrace.AfterFrame = (requestOpcode, _, _, _) => responseOpcodes.Add(requestOpcode);
#endif
        try
        {
            Add(command, "p0", value, type);
            var observed = Safe(command.ExecuteScalar());
            var after = WireSendCount();
            return new { outcome = "returned", observed, sent_delta = Delta(before, after),
                execution_frame_seen = ExecutionFrameSeen(), business_frame_seen = BusinessFrameSeen() };
        }
        catch (Exception ex)
        {
            var after = WireSendCount();
            return new { outcome = "error", error = Error(ex), sent_delta = Delta(before, after),
                execution_frame_seen = ExecutionFrameSeen(), business_frame_seen = BusinessFrameSeen() };
        }
        finally
        {
#if CANDIDATE
            DmResultProtocolTrace.AfterFrame = null;
#endif
        }

        bool? BusinessFrameSeen()
        {
#if CANDIDATE
            return responseOpcodes.Any(opcode => opcode is 13 or 44);
#else
            return null;
#endif
        }

        bool? ExecutionFrameSeen()
        {
#if CANDIDATE
            // This fixed parameterized SELECT may describe/prepare with opcodes 3/5.
            // Opcode 13 executes it; opcode 44 only fetches MORE_RESULT afterwards.
            return responseOpcodes.Any(opcode => opcode is 13 or 44);
#else
            return null;
#endif
        }
    }

    private static long? Delta(long? before, long? after)
        => before.HasValue && after.HasValue ? after.Value - before.Value : null;

    private static long? WireSendCount()
    {
#if OFFICIAL
        return null;
#else
        var hooks = typeof(DriverConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks");
        return (long?)hooks?.GetProperty("SendCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null);
#endif
    }

    private static object Capture(DbDataReader reader) => new
    {
        field_type = Probe(() => reader.GetFieldType(0).FullName),
        provider_type = Probe(() => reader.GetProviderSpecificFieldType(0).FullName),
        database_type = Probe(() => reader.GetDataTypeName(0)),
        is_dbnull = Probe(() => reader.IsDBNull(0)),
        value = Probe(() => new { type = reader.GetValue(0)?.GetType().FullName, value = Safe(reader.GetValue(0)) }),
        provider_value = Probe(() => new { type = reader.GetProviderSpecificValue(0)?.GetType().FullName,
            value = Safe(reader.GetProviderSpecificValue(0)) })
    };

    private static object Safe(object? value)
    {
        if (value is null or DBNull) return "null";
        if (value is byte[] bytes) return new { bytes = bytes.Length, sha256 = Hash(bytes) };
        if (value is DateTime dateTime)
            return new { ticks = dateTime.Ticks, kind = dateTime.Kind.ToString(),
                invariant = dateTime.ToString("O", CultureInfo.InvariantCulture) };
        if (value is DateTimeOffset dateTimeOffset)
            return new { ticks = dateTimeOffset.Ticks, offset_ticks = dateTimeOffset.Offset.Ticks,
                invariant = dateTimeOffset.ToString("O", CultureInfo.InvariantCulture) };
        if (value is TimeSpan timeSpan)
            return new { ticks = timeSpan.Ticks, invariant = timeSpan.ToString("c", CultureInfo.InvariantCulture) };
        if (value is DateOnly dateOnly) return new { day_number = dateOnly.DayNumber };
        if (value is TimeOnly timeOnly) return new { ticks = timeOnly.Ticks };
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        // All server data should be synthetic; still bound output length and avoid raw arbitrary text.
        return new { chars = text.Length, sha256 = Hash(Encoding.UTF8.GetBytes(text)),
            known_value = IsKnownSynthetic(text) ? text : null };
    }

    private static bool IsKnownSynthetic(string text)
        => text is PreciseDecimal or ValidGuid or InvalidGuid or Unicode or "T09_PARITY" or "T09_LOB"
            or "1.5" or "-10" or "7" or "17" or "1000" or "5" or "255" or "18446744073709551615"
            or "-128" or "-32768" or "65535" or "-2147483648" or "2147483647"
            or "-9223372036854775808" or "9223372036854775807" or "4294967295"
            or "-0.00000000000000000001" or "999999999999999999.99999999999999999999";

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static object ReadGetter(string raw, string sql, Func<DbDataReader, object> getter)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new { rows = 0 };
        return new { metadata = Capture(reader), getter = getter(reader) };
    }

    private static object InsertDecimal(string raw, string table, int id, decimal value)
    {
        using (var connection = OpenVerified(raw))
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = $"INSERT INTO {table}(ID,NUMVAL) VALUES (:p0,:p1)";
            Add(insert, "p0", id, DbType.Int32);
            Add(insert, "p1", value, DbType.Decimal);
            insert.ExecuteNonQuery();
        }
        using var independent = OpenVerified(raw);
        using var command = independent.CreateCommand();
        command.CommandText = $"SELECT NUMVAL FROM {table} WHERE ID = :p0";
        Add(command, "p0", id);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new { rows = 1, metadata = Capture(reader),
                exact_getdecimal = Probe(() => reader.GetDecimal(0) == value),
                expected = value.ToString(CultureInfo.InvariantCulture) }
            : new { rows = 0, metadata = (object?)null,
                exact_getdecimal = (object?)null, expected = value.ToString(CultureInfo.InvariantCulture) };
    }

    private static object ReadDecimal(string raw, string literal)
        => ReadGetter(raw, $"SELECT CAST('{literal}' AS DECIMAL(38,20)) FROM DUAL",
            reader => new { decimal_getter = Probe(() => Safe(reader.GetDecimal(0))),
                provider_specific = Probe(() => Safe(reader.GetProviderSpecificValue(0))) });

    private static object ReadGuidText(string raw, string text)
        => ReadGetter(raw, $"SELECT CAST('{text}' AS VARCHAR(36)) FROM DUAL",
            reader => new { guid_getter = Probe(() => Safe(reader.GetGuid(0))),
                value = Safe(reader.GetValue(0)) });

    private static object GuidColumnCase(string raw, string table, int id, string? text, byte[]? bytes)
    {
        using (var connection = OpenVerified(raw))
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = $"INSERT INTO {table}(ID,GUIDTXT,GUIDBIN) VALUES (:p0,:p1,:p2)";
            Add(insert, "p0", id, DbType.Int32);
            Add(insert, "p1", text ?? (object)DBNull.Value, DbType.String);
            Add(insert, "p2", bytes ?? (object)DBNull.Value, DbType.Binary);
            insert.ExecuteNonQuery();
        }
        using var independent = OpenVerified(raw);
        using var command = independent.CreateCommand();
        command.CommandText = $"SELECT GUIDTXT,GUIDBIN FROM {table} WHERE ID = :p0";
        Add(command, "p0", id, DbType.Int32);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new { rows = 0 };
        return new
        {
            rows = 1,
            text_null = reader.IsDBNull(0),
            binary_null = reader.IsDBNull(1),
            text_field_type = Probe(() => reader.GetFieldType(0).FullName),
            text_value = Probe(() => Safe(reader.GetValue(0))),
            text_getguid = Probe(() => Safe(reader.GetGuid(0))),
            binary_value = Probe(() => Safe(reader.GetValue(1))),
            binary_getguid = Probe(() => Safe(reader.GetGuid(1)))
        };
    }

    private static object GuidBindingCase(string raw, string table, int id, string column, DbType type)
    {
        object ReadBack()
        {
            using var fresh = OpenVerified(raw);
            using var command = fresh.CreateCommand();
            command.CommandText = $"SELECT {column} FROM {table} WHERE ID = :p0";
            Add(command, "p0", id, DbType.Int32);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return new { rows = 0 };
            return new { rows = 1, is_null = reader.IsDBNull(0), value = Safe(reader.GetValue(0)),
                field_type = Probe(() => reader.GetFieldType(0).FullName),
                get_guid = Probe(() => Safe(reader.GetGuid(0))) };
        }
        object before = ReadBack();
        bool accepted = false;
        object? error = null;
        using (var connection = OpenVerified(raw))
        using (var update = connection.CreateCommand())
        {
            update.CommandText = $"UPDATE {table} SET {column} = :p0 WHERE ID = :p1";
            try
            {
                Add(update, "p0", Guid.Parse(ValidGuid), type);
                Add(update, "p1", id, DbType.Int32);
                accepted = update.ExecuteNonQuery() == 1;
            }
            catch (Exception ex) { error = Error(ex); }
        }
        object after = ReadBack();
        return new { accepted, error, before, after,
            unchanged = JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after),
            target_capacity = column == "GUIDSHORT" ? 15 : column == "GUIDBIN" ? 16 : 50,
            guid_bytes = 16 };
    }

    private static object BinaryOverflowCase(string raw, string table, DriverDbType type)
    {
        string column = type == DriverDbType.Blob ? "PAYLOAD" : "GUIDBIN";
        object? ReadOriginal()
        {
            using var fresh = OpenVerified(raw);
            return type == DriverDbType.Blob
                ? Scalar(fresh, $"SELECT COUNT(*) FROM {table} WHERE ID = 11 AND PAYLOAD IS NULL")
                : Safe(Scalar(fresh, $"SELECT GUIDBIN FROM {table} WHERE ID = 11"));
        }
        object? before = ReadOriginal();
        object? error = null;
        bool accepted = false;
        using (var connection = OpenVerified(raw))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"UPDATE {table} SET {column} = :p0 WHERE ID = 11";
            try
            {
                var parameter = (DriverParameter)command.CreateParameter();
                parameter.ParameterName = "p0";
                parameter.DmSqlType = type;
                parameter.Size = 16;
                parameter.Value = Enumerable.Range(0, 17).Select(index => (byte)index).ToArray();
                command.Parameters.Add(parameter);
                command.ExecuteNonQuery();
                accepted = true;
            }
            catch (Exception ex) { error = Error(ex); }
        }
        object? after = ReadOriginal();
        return new { accepted, error, unchanged = JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after),
            before, after, declared_size = 16, input_bytes = 17, provider_type = type.ToString() };
    }

    private static object NullBindingCase(string raw, string table, string sql,
        bool explicitInt32, bool insert, int ordinal)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (insert) Add(command, "p0", 14, DbType.Int32);
        var target = (DriverParameter)command.CreateParameter();
        target.ParameterName = "p" + ordinal.ToString(CultureInfo.InvariantCulture);
        if (explicitInt32) target.DmSqlType = DriverDbType.Int32;
        target.Value = DBNull.Value;
        command.Parameters.Add(target);
        using var planCapture = new PlanCaptureScope(command, ordinal);
        bool prepared = false, executed = false;
        object? prepareError = null, executeError = null, result = null;
        try { command.Prepare(); prepared = true; }
        catch (Exception ex) { prepareError = Error(ex); }
        object? preparedParameter = prepared ? PreparedParameterObject(command, ordinal) : null;
        object? metadata = PreparedParameterMetadata(preparedParameter, ordinal);
        object? typeSource = ParameterTypeSnapshot(target, preparedParameter);
        long? before = WireSendCount();
#if CANDIDATE
        var responseOpcodes = new List<short>();
        DmResultProtocolTrace.AfterFrame = (requestOpcode, _, _, _) => responseOpcodes.Add(requestOpcode);
#endif
        if (prepared)
        {
            try
            {
                if (insert)
                {
                    int affected = command.ExecuteNonQuery();
                    result = new { affected };
                }
                else
                {
                    using var reader = command.ExecuteReader();
                    result = reader.Read()
                        ? new { rows = 1, field_type = reader.GetFieldType(0).FullName,
                            database_type = reader.GetDataTypeName(0), is_dbnull = reader.IsDBNull(0) }
                        : new { rows = 0, field_type = (string?)null,
                            database_type = (string?)null, is_dbnull = false };
                }
                executed = true;
            }
            catch (Exception ex) { executeError = Error(ex); }
        }
        long? after = WireSendCount();
#if CANDIDATE
        DmResultProtocolTrace.AfterFrame = null;
        bool? executionFrameSeen = responseOpcodes.Contains(44);
#else
        bool? executionFrameSeen = null;
#endif
        planCapture.Stop();
        object? final = null;
        if (insert)
        {
            using var fresh = OpenVerified(raw);
            int rows = Convert.ToInt32(Scalar(fresh, $"SELECT COUNT(*) FROM {table} WHERE ID = 14"),
                CultureInfo.InvariantCulture);
            object? isNull = rows == 1 ? Scalar(fresh,
                $"SELECT CASE WHEN INTVAL IS NULL THEN 1 ELSE 0 END FROM {table} WHERE ID = 14") : null;
            final = new { rows, int_column_null = isNull is null ? (bool?)null :
                Convert.ToInt32(isNull, CultureInfo.InvariantCulture) == 1 };
        }
        return new { ordinal, explicit_int32 = explicitInt32, prepare_succeeded = prepared,
            prepare_error = prepareError, parameter_metadata = metadata,
            parameter_metadata_after = PreparedParameterMetadata(preparedParameter, ordinal),
            parameter_type_source = typeSource,
            parameter_type_source_after = ParameterTypeSnapshot(target, preparedParameter),
            plan_type_snapshots = planCapture.Snapshots,
            execute_attempted = prepared, execute_succeeded = executed,
            execute_error = executeError, execute_send_delta = Delta(before, after),
            execution_frame_seen = executionFrameSeen,
            result, final_state = final };
    }

    private static object? PreparedParameterObject(DbCommand command, int ordinal)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            object? statement = command.GetType().GetField("m_Stmt", flags)?.GetValue(command);
            object? info = statement?.GetType().GetMethod("H", flags, null, Type.EmptyTypes, null)?.Invoke(statement, null);
            object? array = info?.GetType().GetMethod("GetParamsInfo", flags, null, Type.EmptyTypes, null)
                ?.Invoke(info, null);
            if (array is not Array parameters || ordinal < 0 || ordinal >= parameters.Length)
                return null;
            return parameters.GetValue(ordinal);
        }
        catch { return null; }
    }

    private static object PreparedParameterMetadata(object? parameter, int ordinal)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (parameter is null) return new { ordinal, available = false };
        try
        {
            object? Call(string method) => parameter.GetType().GetMethod(method, flags, null, Type.EmptyTypes, null)
                ?.Invoke(parameter, null);
            int? encodedLength = null;
            try
            {
                encodedLength = (CallWithIndex("GetInValue", 0) as byte[])?.Length;
            }
            catch { }
            object? CallWithIndex(string method, int index) => parameter.GetType()
                .GetMethod(method, flags, null, [typeof(int)], null)?.Invoke(parameter, [index]);
            return new { ordinal, available = true,
                c_type = Call("GetCType"), sql_type = Call("GetSqlType"),
                precision = Call("GetPrecision"), scale = Call("GetScale"),
                type_flag = Call("GetTypeFlag"), mask = parameter.GetType().GetField("mask", flags)?.GetValue(parameter),
                encoded_length = encodedLength };
        }
        catch { return new { ordinal, available = false }; }
    }

    private static object ParameterTypeSnapshot(DriverParameter parameter, object? described)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string? source = parameter.GetType().GetProperty("TypeSource", flags)?.GetValue(parameter)?.ToString();
        string? resolvedType = null, resolvedSource = null;
        try
        {
            var method = parameter.GetType().GetMethod("ResolveType", flags);
            object? result = method?.Invoke(parameter, [described]);
            resolvedType = result?.GetType().GetField("Item1")?.GetValue(result)?.ToString();
            resolvedSource = result?.GetType().GetField("Item2")?.GetValue(result)?.ToString();
        }
        catch { }
        return new { source, db_type = parameter.DbType.ToString(), dm_sql_type = parameter.DmSqlType.ToString(),
            resolved_type = resolvedType, resolved_source = resolvedSource };
    }

    private static object UnsignedBindingCase(string raw, object value, bool castDecimal)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = castDecimal
            ? "SELECT CAST(:p0 AS DECIMAL(38,0)) FROM DUAL" : "SELECT :p0 FROM DUAL";
        var parameter = (DriverParameter)command.CreateParameter();
        parameter.ParameterName = "p0";
        parameter.Value = value;
        command.Parameters.Add(parameter);
        using var planCapture = new PlanCaptureScope(command, 0);
        bool prepared = false, executed = false;
        object? prepareError = null, executeError = null, result = null;
        try { command.Prepare(); prepared = true; }
        catch (Exception ex) { prepareError = Error(ex); }
        object? described = prepared ? PreparedParameterObject(command, 0) : null;
        object? before = PreparedParameterMetadata(described, 0);
        object? source = ParameterTypeSnapshot(parameter, described);
        long? wireBefore = WireSendCount();
        if (prepared)
        {
            try
            {
                using var reader = command.ExecuteReader();
                result = reader.Read() ? Capture(reader) : new { rows = 0 };
                executed = true;
            }
            catch (Exception ex) { executeError = Error(ex); }
        }
        return new { cast_decimal = castDecimal, prepare_succeeded = prepared, prepare_error = prepareError,
            parameter_type_source = source, parameter_metadata_before = before,
            parameter_metadata_after = PreparedParameterMetadata(described, 0),
            parameter_type_source_after = ParameterTypeSnapshot(parameter, described),
            plan_type_snapshots = planCapture.Snapshots,
            execute_succeeded = executed, execute_error = executeError,
            execute_send_delta = Delta(wireBefore, WireSendCount()), result };
    }

    private static object IntervalMetadataCase(string raw, bool castInterval)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = castInterval
            ? "SELECT CAST(:p0 AS INTERVAL DAY(9) TO SECOND(6)) FROM DUAL" : "SELECT :p0 FROM DUAL";
        var parameter = (DriverParameter)command.CreateParameter();
        parameter.ParameterName = "p0";
        parameter.DmSqlType = DriverDbType.IntervalDayToSecond;
        if (castInterval) { parameter.Precision = 9; parameter.Scale = 6; }
        parameter.Value = TimeSpan.FromTicks(10);
        command.Parameters.Add(parameter);
        using var planCapture = new PlanCaptureScope(command, 0);
        bool prepared = false, executed = false;
        object? prepareError = null, executeError = null, result = null;
        try { command.Prepare(); prepared = true; }
        catch (Exception ex) { prepareError = Error(ex); }
        object? described = prepared ? PreparedParameterObject(command, 0) : null;
        object? before = PreparedParameterMetadata(described, 0);
        object? source = ParameterTypeSnapshot(parameter, described);
        long? wireBefore = WireSendCount();
        if (prepared)
        {
            try
            {
                using var reader = command.ExecuteReader();
                result = reader.Read() ? new { metadata = Capture(reader),
                    typed = Probe(() => Safe(reader.GetFieldValue<TimeSpan>(0))) } : new { rows = 0 };
                executed = true;
            }
            catch (Exception ex) { executeError = Error(ex); }
        }
        return new { cast_interval = castInterval, parameter_precision = parameter.Precision,
            parameter_scale = parameter.Scale, prepare_succeeded = prepared, prepare_error = prepareError,
            type_source = source, metadata_before = before,
            metadata_after = PreparedParameterMetadata(described, 0),
            type_source_after = ParameterTypeSnapshot(parameter, described),
            plan_type_snapshots = planCapture.Snapshots,
            execute_succeeded = executed, execute_error = executeError,
            execute_send_delta = Delta(wireBefore, WireSendCount()), result };
    }

    private sealed class PlanCaptureScope : IDisposable
    {
        private readonly DbCommand command;
        private readonly int ordinal;
        private bool stopped;
        internal List<object> Snapshots { get; } = [];

        internal PlanCaptureScope(DbCommand command, int ordinal)
        {
            this.command = command;
            this.ordinal = ordinal;
#if CANDIDATE
            W.Dm.DmCommand.AfterPlanCaptured = Capture;
#endif
        }

        private void Capture()
        {
#if CANDIDATE
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                object? plan = command.GetType().GetField("activePlan", flags)?.GetValue(command);
                object? collection = plan?.GetType().GetProperty("Parameters", flags)?.GetValue(plan);
                if (collection is not DbParameterCollection parameters || ordinal >= parameters.Count)
                {
                    Snapshots.Add(new { available = false });
                    return;
                }
                var parameter = (DriverParameter)parameters[ordinal];
                Snapshots.Add(new { available = true, ordinal,
                    type_source = parameter.GetType().GetProperty("TypeSource", flags)?.GetValue(parameter)?.ToString(),
                    db_type = parameter.DbType.ToString(), dm_sql_type = parameter.DmSqlType.ToString() });
            }
            catch { Snapshots.Add(new { available = false }); }
#endif
        }

        internal void Stop()
        {
            if (stopped) return;
            stopped = true;
#if CANDIDATE
            W.Dm.DmCommand.AfterPlanCaptured = null;
#endif
        }

        public void Dispose() => Stop();
    }

    private static object InsertText(string raw, string table, int id, string value)
    {
        using (var connection = OpenVerified(raw))
            Exec(connection, $"INSERT INTO {table}(ID,TEXTVAL) VALUES (:p0,:p1)", id, value);
        using var independent = OpenVerified(raw);
        var readback = Scalar(independent, $"SELECT TEXTVAL FROM {table} WHERE ID = :p0", id);
        return new { exact = string.Equals(readback?.ToString(), value, StringComparison.Ordinal),
            input_utf16 = value.Length, input_utf8 = Encoding.UTF8.GetByteCount(value), readback = Safe(readback) };
    }

    private static object ReadTemporal(string raw, string sql)
        => ReadGetter(raw, sql, reader => new
        {
            get_string = Probe(() =>
            {
                var value = reader.GetString(0);
                return new { text = Safe(value),
                    ef_datetimeoffset_parse = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out _),
                    framework_timespan_parse = TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _),
                    ef_interval_shape_parse = TryParseEfInterval(value, out var ticks),
                    ef_interval_ticks = ticks };
            }),
            get_datetime = Probe(() => Safe(reader.GetDateTime(0))),
            get_timespan = Probe(() => Safe(reader.GetFieldValue<TimeSpan>(0))),
            get_datetimeoffset = Probe(() => Safe(reader.GetFieldValue<DateTimeOffset>(0)))
        });

    private static object IntervalParameter(string raw, TimeSpan value, byte? fractionalScale = null)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT :p0 FROM DUAL";
        var parameter = (DriverParameter)command.CreateParameter();
        parameter.ParameterName = "p0";
        parameter.DmSqlType = DriverDbType.IntervalDayToSecond;
        if (fractionalScale is { } scale) parameter.Scale = scale;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new { rows = 0 };
        return new { rows = 1, expected_ticks = value.Ticks, metadata = Capture(reader),
            typed = Probe(() => Safe(reader.GetFieldValue<TimeSpan>(0))),
            get_string = Probe(() =>
            {
                var text = reader.GetString(0);
                return new { text = Safe(text), ef_interval_shape_parse = TryParseEfInterval(text, out long ticks),
                    ef_interval_ticks = ticks };
            }) };
    }

    private static object TemporalExplicitCase<T>(string raw, T value, DriverDbType type, byte scale)
        where T : struct
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT :p0 FROM DUAL";
        var parameter = (DriverParameter)command.CreateParameter();
        parameter.ParameterName = "p0";
        parameter.DmSqlType = type;
        parameter.Scale = scale;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new { rows = 0 };
        return new
        {
            rows = 1,
            expected = Safe(value),
            metadata = Capture(reader),
            typed = Probe(() => Safe(reader.GetFieldValue<T>(0))),
            get_string = Probe(() =>
            {
                var text = reader.GetString(0);
                return new { text = Safe(text),
                    invariant_dto_parse = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var parsed),
                    parsed_dto = Safe(parsed) };
            })
        };
    }

    private static object DuplicateNames()
    {
        using var connection = new DriverConnection();
        using var command = connection.CreateCommand();
        Add(command, ":value", 1);
        try
        {
            Add(command, "VALUE", 2);
            return new { first_added = true, duplicate_rejected = false, count = command.Parameters.Count,
                error = (object?)null };
        }
        catch (Exception ex)
        {
            return new { first_added = true, duplicate_rejected = true, count = command.Parameters.Count,
                error = Error(ex) };
        }
    }

    private static object MarkerCase(string raw)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ':not_a_parameter', :p0, :P0 FROM DUAL /* :comment */";
        Add(command, "p0", 17);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new { field_count = reader.FieldCount,
            text_literal = Safe(reader.GetValue(0)), first_parameter = Safe(reader.GetValue(1)),
            repeated_parameter = Safe(reader.GetValue(2)) } : new { rows = 0 };
    }

    private static object MixedMarkers(string raw)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT :p0, ? FROM DUAL";
        Add(command, "p0", 1);
        Add(command, "p1", 2);
        try { return new { rejected = false, result = Safe(command.ExecuteScalar()), error = (object?)null }; }
        catch (Exception ex) { return new { rejected = true, result = (object?)null, error = Error(ex) }; }
    }

    private static object PreparedValues(string raw, bool changeType, bool changeSize)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT :p0 FROM DUAL";
        var parameter = Add(command, "p0", 1, DbType.Int32);
        if (changeSize) parameter.Size = 4;
        command.Prepare();
        var first = Safe(command.ExecuteScalar());
        parameter.Value = changeType ? "T09_PARITY" : 2;
        if (changeType) parameter.DbType = DbType.String;
        if (changeSize) parameter.Size = 8;
        return new { first, second = Safe(command.ExecuteScalar()), changed_type = changeType,
            changed_size = changeSize };
    }

    private static object ResetDbType()
    {
        using var connection = new DriverConnection();
        using var command = connection.CreateCommand();
        var parameter = Add(command, "p0", 12L, DbType.Int32);
        var before = parameter.DbType.ToString();
        parameter.ResetDbType();
        return new { before, after = parameter.DbType.ToString() };
    }

    private static object SyncAsyncParity(string raw, string sql)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var sync = command.ExecuteReader();
        if (!sync.Read()) return new { rows = 0 };
        var syncValue = Capture(sync);
        sync.Close();
        using var asyncReader = command.ExecuteReaderAsync().GetAwaiter().GetResult();
        if (!asyncReader.ReadAsync().GetAwaiter().GetResult()) return new { rows = 0, sync_value = syncValue };
        var asyncValue = Capture(asyncReader);
        return new { same_serialized_observation = JsonSerializer.Serialize(syncValue) == JsonSerializer.Serialize(asyncValue),
            sync_value = syncValue, async_value = asyncValue };
    }

    // Mirrors the quoted interval shape consumed by the reviewed EF mapping;
    // T12 still requires an actual fixed-commit EF package consumer.
    private static bool TryParseEfInterval(string value, out long ticks)
    {
        ticks = 0;
        int first = value.IndexOf('\'');
        int last = value.LastIndexOf('\'');
        if (first < 0 || last <= first) return false;
        string payload = value[(first + 1)..last].Trim();
        bool negative = payload.StartsWith('-');
        if (negative || payload.StartsWith('+')) payload = payload[1..].TrimStart();
        string[] parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long days))
            return false;
        string[] clock = parts[1].Split(':');
        if (clock.Length != 3 ||
            !int.TryParse(clock[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
            !int.TryParse(clock[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
            return false;
        string[] seconds = clock[2].Split('.');
        if (seconds.Length is < 1 or > 2 ||
            !int.TryParse(seconds[0], NumberStyles.None, CultureInfo.InvariantCulture, out int wholeSeconds))
            return false;
        string fraction = seconds.Length == 2 ? seconds[1] : string.Empty;
        if (fraction.Length > 7 || fraction.Any(c => c is < '0' or > '9')) return false;
        if (!long.TryParse(fraction.PadRight(7, '0'), NumberStyles.None, CultureInfo.InvariantCulture,
                out long fractionTicks)) return false;
        try
        {
            ticks = checked(days * TimeSpan.TicksPerDay + hours * TimeSpan.TicksPerHour +
                minutes * TimeSpan.TicksPerMinute + wholeSeconds * TimeSpan.TicksPerSecond + fractionTicks);
            if (negative) ticks = checked(-ticks);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static object TypedNumericParity<T>(string raw, string sql)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var sync = command.ExecuteReader();
        if (!sync.Read()) return new { rows = 0 };
        var syncValue = Probe(() => Safe(sync.GetFieldValue<T>(0)));
        bool? syncDbNull = typeof(T) == typeof(object) ? sync.GetFieldValue<object>(0) is DBNull : null;
        sync.Close();
        using var asyncReader = command.ExecuteReaderAsync().GetAwaiter().GetResult();
        if (!asyncReader.ReadAsync().GetAwaiter().GetResult()) return new { rows = 0 };
        var asyncValue = Probe(() => Safe(asyncReader.GetFieldValueAsync<T>(0).GetAwaiter().GetResult()));
        bool? asyncDbNull = typeof(T) == typeof(object)
            ? asyncReader.GetFieldValueAsync<object>(0).GetAwaiter().GetResult() is DBNull : null;
        return new { rows = 1, sync = syncValue, async_result = asyncValue,
            sync_object_is_dbnull = syncDbNull, async_object_is_dbnull = asyncDbNull,
            same_serialized_observation = JsonSerializer.Serialize(syncValue) == JsonSerializer.Serialize(asyncValue) };
    }

    private static object IntervalScaleCase(string raw, string seconds, int scale, long expectedTicks)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CAST('0 00:00:0{seconds}' AS INTERVAL DAY(9) TO SECOND({scale})) FROM DUAL";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new { rows = 0 };
        string text = reader.GetString(0);
        return new { rows = 1, scale, expected_ticks = expectedTicks,
            typed = Probe(() => Safe(reader.GetFieldValue<TimeSpan>(0))),
            text = Safe(text), ef_interval_shape_parse = TryParseEfInterval(text, out long ticks),
            ef_interval_ticks = ticks };
    }

    private static object SyncAsyncInvalidGuid(string raw)
    {
        using var connection = OpenVerified(raw);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT CAST('{InvalidGuid}' AS VARCHAR(36)) FROM DUAL";
        using var sync = command.ExecuteReader();
        if (!sync.Read()) return new { rows = 0 };
        var syncError = Probe(() => Safe(sync.GetGuid(0)));
        sync.Close();
        using var asyncReader = command.ExecuteReaderAsync().GetAwaiter().GetResult();
        if (!asyncReader.ReadAsync().GetAwaiter().GetResult()) return new { rows = 0, sync_error = syncError };
        var asyncError = Probe(() => Safe(asyncReader.GetFieldValueAsync<Guid>(0).GetAwaiter().GetResult()));
        return new { sync_error = syncError, async_error = asyncError };
    }
}
