using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const string ServerVersion = "8.1.5.60";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    // Luna's decomposed BMP source; supplementary mapping is tested offline only.
    private const string ClobText = "A中e\u0301文";
    private static readonly byte[] BlobBytes = [0x00, 0xAB, 0xFF, 0x42];

    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("baseline" or "fixed"))
        {
            Console.WriteLine("Usage: R1ReviewProbe baseline|fixed <safe-report.json>");
            return 2;
        }
        bool baseline = args[0] == "baseline";
        string reportPath = Path.GetFullPath(args[1]);
        string ownershipPath = reportPath + ".ownership.json";
        string nonce = Guid.NewGuid().ToString("N").ToUpperInvariant();
        string table = "R1RV_" + nonce[..24];
        var cases = new Dictionary<string, object>();
        var report = new Dictionary<string, object?>
        {
            ["mode"] = args[0], ["accepted"] = false, ["status"] = "started",
            ["cases"] = cases, ["table"] = table, ["nonce"] = nonce,
            ["transport"] = "PlaintextAllowed", ["persist_security_info"] = false,
            ["raw_guard_payload_attempted"] = false
        };
        bool ownsTable = false;
        bool finalAbsent = false;
        bool ranCases = false;
        string stage = "package_identity";
        try
        {
            var expected = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "R1ReviewPackageVersion").Value;
            Assembly loaded = typeof(DmConnection).Assembly;
            string loadedVersion = loaded.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
            Require(expected != null && loadedVersion == expected, "package_version_mismatch");
            report["package_version"] = loadedVersion;
            report["assembly_sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loaded.Location)));
            report["assembly_mvid"] = loaded.ManifestModule.ModuleVersionId;
            stage = "verified_connection";
            using var connection = OpenVerified();
            report["server_identity_verified"] = true;
            report["server_version"] = ServerVersion;
            stage = "ownership_registration";
            Require(TableCount(connection, table) == 0, "random_object_already_exists");
            using (var file = new FileStream(ownershipPath, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(file, new { nonce, table, user = TestUser, schema = TestUser,
                    server_version = ServerVersion, absent_before_create = true, state = "create_registered" }, JsonOptions);
            // Register before CREATE so a transport failure can still trigger exact-name cleanup.
            ownsTable = true;
            stage = "create_and_seed";
            Exec(connection, null, $"CREATE TABLE {table} (ID INT PRIMARY KEY, INTVAL INT, TXT CLOB, BIN BLOB)");
            Exec(connection, null, $"INSERT INTO {table}(ID,INTVAL,TXT,BIN) VALUES (1,NULL,:p0,:p1)", (ClobText, DmDbType.Clob), (BlobBytes, DmDbType.Blob));
            Exec(connection, null, $"INSERT INTO {table}(ID,INTVAL,TXT) VALUES (2,0,:p0)", ("", DmDbType.Clob));

            stage = "review_cases";
            Case(cases, "null_int32", () => NullInt32(connection, table));
            Case(cases, "typed_null_6081", () => TypedNull(connection, table));
            Case(cases, "sequential_generic_single_read", () => SequentialGeneric(connection, table));
            Case(cases, "small_lob_materialization", () => SmallLobs(connection, table));
            Case(cases, "lob_payload_boundaries", PackageLobBoundaries);
            Case(cases, "real_zero", () => Zero(connection, table));
            Case(cases, "clob_length_and_copy", () => Clob(connection, table, sequential: false));
            Case(cases, "sequential_clob_length_and_copy", () => Clob(connection, table, sequential: true));
            Case(cases, "sequential_clob_forward_gap", () => ClobForwardGap(connection, table));
            CharacterizeEmptyClob(cases, connection, table);
            int commentValue = Convert.ToInt32(Scalar(connection,
                "SELECT 11 /* /* */ + 31 -- */\n FROM DUAL"), CultureInfo.InvariantCulture);
            report["server_comment_value"] = commentValue;
            cases["server_comment_terminator"] = new CaseResult(commentValue is 42 or 11,
                commentValue == 42 ? "first_close_42" : commentValue == 11 ? "nested_11" : "unexpected_result", null);
            Case(cases, "prepared_detach_and_rebind", () => Detach(connection, table));
            Case(cases, "active_explicit_transaction_detach", () => TransactionDetach(connection, table));
            Case(cases, "own_active_reader_detach", () => OwnReaderDetach(connection));
            Case(cases, "other_active_reader_detach", () => OtherReaderDetach(connection));
            if (baseline)
                cases["transaction_guard"] = new { status = "baseline_dangerous_payload_not_sent", required = false };
            else
            {
                Case(cases, "api_savepoint_stack_and_raw_sql_rejection", () => SavepointStack(connection, table));
                Case(cases, "mixed_api_raw_savepoint_stack", () => MixedSavepointStack(connection, table));
                Case(cases, "unknown_savepoint_error_fail_closed", () => DiagnoseAbsentSavepoint(table,
                    cases["mixed_api_raw_savepoint_stack"] is CaseResult { Pass: true }));
                Require(commentValue == 42, "server_comment_profile_requires_design_review");
                report["raw_guard_payload_attempted"] = true;
                try { cases["transaction_guard"] = TransactionGuard(connection, table); }
                catch (Exception error) { cases["transaction_guard"] = new CaseResult(false, "error", SafeError(error)); }
            }
            ranCases = true;
        }
        catch (Exception error)
        {
            report["failed_stage"] = stage;
            report["error"] = SafeError(error);
            report["status"] = error is ProbeFailure { Kind: "integration_pending" } ? "integration_pending" : "failed";
        }
        finally
        {
            if (ownsTable)
            {
                try
                {
                    using (var cleanup = OpenVerified())
                        if (TableCount(cleanup, table) == 1) Exec(cleanup, null, $"DROP TABLE {table}");
                    using var observer = OpenVerified();
                    finalAbsent = TableCount(observer, table) == 0;
                    report["final_table_count"] = finalAbsent ? 0 : 1;
                    File.WriteAllText(ownershipPath, JsonSerializer.Serialize(new { nonce, table, user = TestUser,
                        schema = TestUser, server_version = ServerVersion, absent_before_create = true,
                        state = finalAbsent ? "cleanup_verified" : "cleanup_failed" }, JsonOptions));
                }
                catch (Exception error) { report["cleanup_error"] = SafeError(error); }
            }
        }
        bool allContracts = cases.Values.OfType<CaseResult>().All(result => result.Pass);
        report["all_required_contracts_pass"] = ranCases && allContracts;
        report["accepted"] = ranCases && finalAbsent && (baseline || allContracts);
        if (ranCases && finalAbsent)
            report["status"] = baseline ? "baseline_characterized" : allContracts ? "review_contracts_verified" : "contract_failure";
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions) + "\n");
        Console.WriteLine("R1 review " + args[0] + " status=" + report["status"] + " accepted=" + report["accepted"]);
        return report["accepted"] is true ? 0 : report["status"] is "integration_pending" ? 2 : 1;
    }

    private static bool NullInt32(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"SELECT INTVAL FROM {table} WHERE ID=1");
        using var reader = command.ExecuteReader();
        Require(reader.Read() && reader.IsDBNull(0) && reader.GetValue(0) is DBNull, "sql_null_identity_failed");
        var int64 = Capture(() => reader.GetInt64(0));
        var int32 = Capture(() => reader.GetInt32(0));
        var generic = Capture(() => reader.GetFieldValue<int>(0));
        return int64 is DmException first && int32 is DmException second && generic is DmException third
            && first.Number == second.Number && first.Number == third.Number;
    }

    private static bool TypedNull(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"SELECT INTVAL FROM {table} WHERE ID=1");
        using var reader = command.ExecuteReader();
        Require(reader.Read(), "typed_null_row_missing");
        Action[] getters = [() => reader.GetGuid(0), () => reader.GetString(0),
            () => reader.GetFieldValue<Guid>(0), () => reader.GetFieldValue<string>(0),
            () => reader.GetFieldValue<byte[]>(0), () => reader.GetFieldValue<DmDecimal>(0),
            () => reader.GetFieldValue<DateTime>(0), () => reader.GetFieldValue<TimeOnly>(0)];
        return getters.All(getter => Capture(getter) is DmException { Number: 6081 })
            && reader.GetValue(0) is DBNull && reader.GetFieldValue<object>(0) is DBNull
            && reader.GetFieldValue<DBNull>(0) is DBNull;
    }

    private static bool SequentialGeneric(DmConnection connection, string table)
    {
        using (var command = Command(connection, null, $"SELECT INTVAL,ID FROM {table} WHERE ID=2"))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            Require(reader.Read() && reader.GetFieldValue<int>(0) == 0 && reader.GetFieldValue<int>(1) == 2,
                "sequential_generic_non_null_failed");
            Require(Capture(() => reader.GetFieldValue<int>(0)) is DmException { Number: 6097 },
                "sequential_generic_backward_not_rejected");
        }
        using (var command = Command(connection, null, $"SELECT INTVAL,ID FROM {table} WHERE ID=1"))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            Require(reader.Read() && Capture(() => reader.GetFieldValue<int>(0)) is DmException { Number: 6081 }
                && reader.GetFieldValue<int>(1) == 1, "sequential_generic_null_then_next_failed");
        }
        using (var command = Command(connection, null, "SELECT CAST(NULL AS DECIMAL(10,2)),42 FROM DUAL"))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
        {
            Require(reader.Read() && Capture(() => reader.GetFieldValue<DmDecimal>(0)) is DmException { Number: 6081 }
                && reader.GetFieldValue<int>(1) == 42, "sequential_dmdecimal_null_then_next_failed");
        }
        using (var command = Command(connection, null, "SELECT CAST(123.45 AS DECIMAL(10,2)),42 FROM DUAL"))
        using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
            return reader.Read() && reader.GetFieldValue<DmDecimal>(0).ToDecimalExact() == 123.45m
                && reader.GetFieldValue<int>(1) == 42;
    }

    private static bool SmallLobs(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"SELECT TXT,BIN FROM {table} WHERE ID=1");
        using var reader = command.ExecuteReader();
        Require(reader.Read(), "lob_row_missing");
        Require(reader.GetString(0) == ClobText && Equals(reader.GetValue(0), ClobText)
            && reader.GetFieldValue<string>(0) == ClobText, "clob_materialization_failed");
        Require(reader.GetFieldValue<byte[]>(1).SequenceEqual(BlobBytes), "blob_value_failed");
        Require(reader.GetString(1) == Convert.ToHexString(BlobBytes), "blob_payload_hex_failed");
        var copy = new byte[BlobBytes.Length];
        return reader.GetBytes(1, 0, copy, 0, copy.Length) == copy.Length && copy.SequenceEqual(BlobBytes);
    }

    private static bool PackageLobBoundaries()
    {
        // Exercise the actual PackageReference assembly, with synthetic lengths only.
        Type guard = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Types.DmLobMaterialization")
            ?? throw new ProbeFailure("package_lob_guard_missing");
        const long limit = 64L * 1024 * 1024;
        foreach (var (name, exact) in new[] { ("Bytes", limit), ("Characters", limit / 2), ("HexInput", limit / 4) })
        {
            MethodInfo method = guard.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("package_lob_guard_method_missing");
            Require(Equals(method.Invoke(null, [exact]), checked((int)exact)), "lob_exact_boundary_rejected");
            foreach (long rejected in new[] { exact + 1, (long)int.MaxValue + 1, long.MaxValue })
                Require(Capture(() => method.Invoke(null, [rejected])) is TargetInvocationException
                    { InnerException: NotSupportedException }, "lob_over_boundary_not_rejected");
        }
        return true;
    }

    private static bool Zero(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"SELECT INTVAL FROM {table} WHERE ID=2");
        using var reader = command.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0) && reader.GetInt32(0) == 0 && reader.GetFieldValue<int>(0) == 0;
    }

    private static bool Clob(DmConnection connection, string table, bool sequential)
    {
        using var command = Command(connection, null, $"SELECT TXT,ID FROM {table} WHERE ID=1");
        using var reader = command.ExecuteReader(sequential ? CommandBehavior.SequentialAccess : CommandBehavior.Default);
        Require(reader.Read(), "clob_row_missing");
        // Null-buffer length is independent of offsets and requested copy length.
        Require(reader.GetChars(0, 0, null!, 0, 0) == ClobText.Length, "clob_full_utf16_length_failed");
        var prefix = new char[5];
        Require(reader.GetChars(0, 0, prefix, 1, 3) == 3 && new string(prefix, 1, 3) == ClobText[..3], "clob_partial_failed");
        Require(reader.GetChars(0, 0, null!, 19, 1) == ClobText.Length, "clob_length_after_copy_failed");
        Require(reader.GetChars(0, 3, prefix, prefix.Length, 0) == 0, "clob_zero_copy_failed");
        var remainder = new char[ClobText.Length - 3];
        Require(reader.GetChars(0, 3, remainder, 0, remainder.Length) == remainder.Length
            && new string(remainder) == ClobText[3..], "clob_remaining_copy_failed");
        Require(reader.GetInt32(1) == 1, "clob_following_column_failed");
        return !sequential || Capture(() => reader.GetChars(0, 0, new char[1], 0, 1)) is DmException;
    }

    private static void CharacterizeEmptyClob(Dictionary<string, object> cases, DmConnection connection, string table)
    {
        try
        {
            using var command = Command(connection, null, $"SELECT TXT FROM {table} WHERE ID=2");
            using var reader = command.ExecuteReader();
            Require(reader.Read(), "empty_clob_row_missing");
            if (reader.IsDBNull(0))
                cases["empty_clob"] = new { status = "server_maps_empty_to_sql_null", required = false };
            else
            {
                long length = reader.GetChars(0, 0, null!, 0, 0);
                cases["empty_clob"] = new CaseResult(length == 0, "non_null_empty_clob_observed", new { utf16_length = length });
            }
        }
        catch (Exception error) { cases["empty_clob"] = new CaseResult(false, "characterization_error", SafeError(error)); }
    }

    private static bool ClobForwardGap(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"SELECT TXT FROM {table} WHERE ID=1");
        using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
        Require(reader.Read(), "clob_forward_gap_row_missing");
        Require(reader.GetChars(0, 0, null!, 0, 0) == ClobText.Length, "clob_forward_gap_length_failed");
        var buffer = new char[1];
        Require(reader.GetChars(0, 2, buffer, 0, 1) == 1 && buffer[0] == ClobText[2], "clob_forward_gap_copy_failed");
        return Capture(() => reader.GetChars(0, 1, buffer, 0, 1)) is DmException;
    }

    private static bool Detach(DmConnection connection, string table)
    {
        using var command = Command(connection, null, $"INSERT INTO {table}(ID,INTVAL) VALUES (51,51)");
        command.Prepare();
        command.Connection = null;
        Require(command.Connection == null, "detached_getter_not_null");
        Require(Capture(() => command.ExecuteNonQuery()) is InvalidOperationException, "detached_nonquery_not_rejected");
        Require(Capture(() => command.ExecuteScalar()) is InvalidOperationException, "detached_scalar_not_rejected");
        Require(Capture(() => { using var reader = command.ExecuteReader(); }) is InvalidOperationException, "detached_reader_not_rejected");
        Require(Capture(() => command.Prepare()) is InvalidOperationException, "detached_prepare_not_rejected");
        using var second = OpenVerified();
        Require(RowCount(second, table, 51) == 0, "old_owner_executed_after_detach");
        command.Connection = second;
        command.CommandText = $"INSERT INTO {table}(ID,INTVAL) VALUES (52,52)";
        command.Prepare();
        Require(command.ExecuteNonQuery() == 1, "rebound_insert_failed");
        return RowCount(connection, table, 51) == 0 && RowCount(connection, table, 52) == 1;
    }

    private static bool TransactionDetach(DmConnection connection, string table)
    {
        using var transaction = (DmTransaction)connection.BeginTransaction();
        using var command = Command(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (61,61)");
        command.ExecuteNonQuery();
        bool rejected = Capture(() => command.Connection = null) is InvalidOperationException && command.Connection == connection;
        command.Transaction = null;
        command.Connection = null;
        bool detached = command.Connection == null && transaction.Outcome == DmTransactionOutcome.Active;
        transaction.Rollback();
        using var observer = OpenVerified();
        return rejected && detached && transaction.Outcome == DmTransactionOutcome.RolledBack && RowCount(observer, table, 61) == 0;
    }

    private static bool OwnReaderDetach(DmConnection connection)
    {
        using var command = Command(connection, null, "SELECT 71 FROM DUAL UNION ALL SELECT 72 FROM DUAL");
        using var reader = command.ExecuteReader();
        bool rejected = Capture(() => command.Connection = null) is InvalidOperationException && command.Connection == connection;
        return rejected && reader.Read() && reader.GetInt32(0) == 71 && reader.Read() && reader.GetInt32(0) == 72;
    }

    private static bool OtherReaderDetach(DmConnection connection)
    {
        using var prepared = Command(connection, null, "SELECT 73 FROM DUAL");
        prepared.Prepare();
        using var other = Command(connection, null, "SELECT 74 FROM DUAL UNION ALL SELECT 75 FROM DUAL");
        using var reader = other.ExecuteReader();
        bool rejected = Capture(() => prepared.Connection = null) is InvalidOperationException && prepared.Connection == connection;
        return rejected && reader.Read() && reader.GetInt32(0) == 74 && reader.Read() && reader.GetInt32(0) == 75;
    }

    private static CaseResult TransactionGuard(DmConnection connection, string table)
    {
        using var transaction = (DmTransaction)connection.BeginTransaction();
        transaction.Save("sp");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (81,81)");
        using var command = Command(connection, transaction, "ROLLBACK TO sp /* /* */ ; COMMIT -- */");
        Exception? rejected = Capture(() => command.ExecuteNonQuery());
        bool active = transaction.Outcome == DmTransactionOutcome.Active;
        int? rowsBeforeRollback = active ? Convert.ToInt32(Scalar(connection,
            $"SELECT COUNT(*) FROM {table} WHERE ID=81", transaction), CultureInfo.InvariantCulture) : null;
        transaction.Rollback();
        using var observer = OpenVerified();
        int finalRows = RowCount(observer, table, 81);
        bool pass = (rejected is InvalidDataException or NotSupportedException) && active && rowsBeforeRollback == 1
            && transaction.Outcome == DmTransactionOutcome.RolledBack && finalRows == 0;
        return new CaseResult(pass, pass ? "contract_verified" : "contract_failed", null, new
        {
            client_rejection = rejected == null ? null : SafeError(rejected), active_after_rejection = active,
            rows_before_rollback = rowsBeforeRollback, final_outcome = transaction.Outcome.ToString(),
            independent_connection_final_rows = finalRows
        });
    }

    private static bool SavepointStack(DmConnection connection, string table)
    {
        using var transaction = (DmTransaction)connection.BeginTransaction();
        transaction.Save("one");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (91,91)");
        transaction.Save("two");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (92,92)");
        foreach (string sql in new[] { "SAVEPOINT WSP_1", "SAVEPOINT \"WSP_2\"",
            "ROLLBACK TO WSP_1", "ROLLBACK TO SAVEPOINT \"WSP_2\"", "RELEASE SAVEPOINT WSP_2" })
        {
            Require(Capture(() => Exec(connection, transaction, sql)) is NotSupportedException,
                "raw_savepoint_sql_not_rejected");
            Require(transaction.Outcome == DmTransactionOutcome.Active && SavepointRows(connection, transaction, table) == 2,
                "raw_savepoint_rejection_changed_transaction");
        }
        transaction.Rollback("two");
        Require(SavepointRows(connection, transaction, table) == 1, "rollback_two_failed");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (93,93)");
        transaction.Rollback("two");
        Require(SavepointRows(connection, transaction, table) == 1, "rollback_target_not_retained");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (93,93)");
        transaction.Rollback("one");
        Require(SavepointRows(connection, transaction, table) == 0
            && Capture(() => transaction.Rollback("two")) is InvalidOperationException
            && transaction.Outcome == DmTransactionOutcome.Active,
            "rollback_one_did_not_invalidate_two");
        transaction.Save("three");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (94,94)");
        transaction.Release("three");
        Require(SavepointRows(connection, transaction, table) == 1
            && Capture(() => transaction.Rollback("three")) is InvalidOperationException
            && transaction.Outcome == DmTransactionOutcome.Active, "release_three_contract_failed");
        transaction.Rollback();
        using var observer = OpenVerified();
        return transaction.Outcome == DmTransactionOutcome.RolledBack && SavepointRows(observer, null, table) == 0;
    }

    private static CaseResult MixedSavepointStack(DmConnection connection, string table)
    {
        using var transaction = (DmTransaction)connection.BeginTransaction();
        transaction.Save("api_base");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (111,111)");
        Exec(connection, transaction, "SAVEPOINT \"__EFSavePoint\"");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (112,112)");
        Exec(connection, transaction, "ROLLBACK TO \"__EFSavePoint\"");
        Require(MixedRows(connection, transaction, table) == 1, "mixed_ef_raw_rollback_failed");
        transaction.Rollback("api_base");
        Require(MixedRows(connection, transaction, table) == 0, "mixed_raw_rollback_lost_earlier_api");

        // All three public execution entry points must register successful raw control.
        _ = Scalar(connection, "SAVEPOINT EF10_SAVEPOINT", transaction);
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (113,113)");
        Exec(connection, transaction, "RELEASE SAVEPOINT EF10_SAVEPOINT");
        Require(MixedRows(connection, transaction, table) == 1, "mixed_ef_raw_release_changed_rows");
        transaction.Rollback("api_base");
        Require(MixedRows(connection, transaction, table) == 0, "mixed_raw_release_lost_earlier_api");
        using (var command = Command(connection, transaction, "SAVEPOINT reader_point"))
        using (var reader = command.ExecuteReader()) { }
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (115,115)");
        Exec(connection, transaction, "ROLLBACK TO SAVEPOINT reader_point");
        Require(MixedRows(connection, transaction, table) == 0, "mixed_reader_raw_point_failed");
        transaction.Rollback("api_base");

        Exec(connection, transaction, "SAVEPOINT outside_early");
        transaction.Save("api_later");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (114,114)");
        Exec(connection, transaction, "ROLLBACK TO outside_early");
        Require(MixedRows(connection, transaction, table) == 0
            && Capture(() => transaction.Rollback("api_later")) is InvalidOperationException
            && transaction.Outcome == DmTransactionOutcome.Active, "mixed_raw_rollback_kept_later_api");
        transaction.Rollback("api_base");

        Exec(connection, transaction, "SAVEPOINT outside_release");
        transaction.Save("api_after_release");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (115,115)");
        Exec(connection, transaction, "RELEASE SAVEPOINT outside_release");
        Require(MixedRows(connection, transaction, table) == 1
            && Capture(() => transaction.Rollback("api_after_release")) is InvalidOperationException
            && transaction.Outcome == DmTransactionOutcome.Active, "mixed_raw_release_kept_later_api");
        transaction.Rollback("api_base");
        Require(MixedRows(connection, transaction, table) == 0, "mixed_early_release_lost_earlier_api");

        Exec(connection, transaction, "SAVEPOINT prepared_anchor");
        transaction.Save("api_after_prepare");
        Exec(connection, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (115,115)");
        using (var prepared = Command(connection, transaction, "ROLLBACK TO prepared_anchor"))
            prepared.Prepare();
        // Preparing a rollback must neither execute it nor invalidate the later API point.
        Require(MixedRows(connection, transaction, table) == 1, "mixed_prepare_executed_control");
        transaction.Rollback("api_after_prepare");
        Require(MixedRows(connection, transaction, table) == 0, "mixed_prepare_changed_api_stack");
        transaction.Rollback("api_base");

        transaction.Rollback();
        using (var observer = OpenVerified())
            Require(transaction.Outcome == DmTransactionOutcome.RolledBack && MixedRows(observer, null, table) == 0,
                "mixed_positive_final_rollback_failed");
        return new CaseResult(true, "contract_verified", null, new { mixed_positive_flows_pass = true });
    }

    private static CaseResult DiagnoseAbsentSavepoint(string table, bool mixedPositiveFlowsPass)
    {
        var details = new Dictionary<string, object?>
        {
            ["mixed_positive_flows_pass"] = mixedPositiveFlowsPass, ["isolated_negative_connection"] = true
        };
        DmConnection? isolated = null;
        DmTransaction? transaction = null;
        Exception? setupError = null;
        bool pass = false;
        string stage = "verified_connection";
        try
        {
            isolated = OpenVerified();
            stage = "transaction_and_seed";
            transaction = (DmTransaction)isolated.BeginTransaction();
            transaction.Save("api_base");
            Exec(isolated, transaction, $"INSERT INTO {table}(ID,INTVAL) VALUES (115,115)");
            stage = "absent_rollback_capture";
            Exception? rollbackError = Capture(() => Exec(isolated, transaction, "ROLLBACK TO absent_review_point"));
            ConnectionState stateAfterError = isolated.State;
            DmTransactionOutcome outcomeAfterError = transaction.Outcome;
            details["absent_rollback_capture"] = rollbackError == null ? null : SafeError(rollbackError);
            details["absent_rollback_is_transient"] = rollbackError is DmException dmError ? (bool?)dmError.IsTransient : null;
            details["connection_state_after_absent_rollback"] = stateAfterError.ToString();
            details["transaction_outcome_after_absent_rollback"] = outcomeAfterError.ToString();
            int? rowsAfterError = null;
            Exception? rowError = Capture(() => rowsAfterError = MixedRows(isolated, transaction, table));
            details["rows_query_after_absent_rollback_capture"] = rowError == null ? null : SafeError(rowError);
            details["rows_after_absent_rollback"] = rowsAfterError;
            details["connection_state_after_rows_query"] = isolated.State.ToString();
            details["transaction_outcome_after_rows_query"] = transaction.Outcome.ToString();
            bool rowsQueryFailedClosed = isolated.State == ConnectionState.Closed
                && transaction.Outcome == DmTransactionOutcome.OutcomeUnknown;
            Exception? apiError = Capture(() => transaction.Rollback("api_base"));
            details["api_rollback_capture"] = apiError == null ? null : SafeError(apiError);
            details["connection_state_after_api_rollback"] = isolated.State.ToString();
            details["transaction_outcome_after_api_rollback"] = transaction.Outcome.ToString();
            bool apiFailedClosed = isolated.State == ConnectionState.Closed
                && transaction.Outcome == DmTransactionOutcome.OutcomeUnknown;
            int? rowsAfterApi = null;
            Exception? apiRowError = Capture(() => rowsAfterApi = MixedRows(isolated, transaction, table));
            details["rows_query_after_api_rollback_capture"] = apiRowError == null ? null : SafeError(apiRowError);
            details["rows_after_api_rollback"] = rowsAfterApi;
            details["connection_state_after_api_rows_query"] = isolated.State.ToString();
            details["transaction_outcome_after_api_rows_query"] = transaction.Outcome.ToString();
            bool apiRowsFailedClosed = isolated.State == ConnectionState.Closed
                && transaction.Outcome == DmTransactionOutcome.OutcomeUnknown;
            Exception? finalRollbackError = Capture(() => transaction.Rollback());
            details["full_rollback_capture"] = finalRollbackError == null ? null : SafeError(finalRollbackError);
            details["connection_state_after_full_rollback"] = isolated.State.ToString();
            details["transaction_outcome_after_full_rollback"] = transaction.Outcome.ToString();
            // Existing T10 profileUnsupportedError contract: this verified 8.1.5.60
            // -2121 response is outside the narrowly accepted recoverable-error profile.
            // The physical connection closes; the outcome is unknown and subsequent controls are rejected.
            pass = mixedPositiveFlowsPass
                && rollbackError is DmException { Number: -2121, IsTransient: false }
                && stateAfterError == ConnectionState.Closed
                && outcomeAfterError == DmTransactionOutcome.OutcomeUnknown
                && rowError is InvalidOperationException && rowsAfterError == null && rowsQueryFailedClosed
                && apiError is InvalidOperationException && apiFailedClosed
                && apiRowError is InvalidOperationException && rowsAfterApi == null && apiRowsFailedClosed
                && finalRollbackError is InvalidOperationException && isolated.State == ConnectionState.Closed
                && transaction.Outcome == DmTransactionOutcome.OutcomeUnknown;
        }
        catch (Exception error)
        {
            setupError = error;
            details["failed_stage"] = stage;
        }
        finally
        {
            if (transaction != null)
            {
                Exception? disposeError = Capture(transaction.Dispose);
                details["transaction_dispose_capture"] = disposeError == null ? null : SafeError(disposeError);
                if (disposeError != null) pass = false;
            }
            if (isolated != null)
            {
                Exception? disposeError = Capture(isolated.Dispose);
                details["connection_dispose_capture"] = disposeError == null ? null : SafeError(disposeError);
                if (disposeError != null) pass = false;
            }
        }
        int? finalRows = null;
        Exception? observerError = Capture(() =>
        {
            using var observer = OpenVerified();
            finalRows = MixedRows(observer, null, table);
        });
        details["fresh_connection_rows_capture"] = observerError == null ? null : SafeError(observerError);
        details["fresh_connection_final_rows"] = finalRows;
        pass = pass && observerError == null && finalRows == 0;
        return new CaseResult(pass, pass ? "contract_verified" : "contract_failed",
            setupError == null ? null : SafeError(setupError), details);
    }

    private static int MixedRows(DmConnection connection, DbTransaction? transaction, string table) =>
        Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE ID BETWEEN 111 AND 115", transaction), CultureInfo.InvariantCulture);

    private static int SavepointRows(DmConnection connection, DbTransaction? transaction, string table) =>
        Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE ID BETWEEN 91 AND 94", transaction), CultureInfo.InvariantCulture);

    private static DmConnection OpenVerified()
    {
        string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? throw new ProbeFailure("integration_pending");
        var builder = new DmConnectionStringBuilder(raw)
        {
            TransportSecurity = DmTransportSecurity.PlaintextAllowed, PersistSecurityInfo = false,
            Schema = TestUser, ConnectTimeout = TimeSpan.FromSeconds(20), CommandTimeout = 20
        };
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_user_required_before_authentication");
        var connection = new DmConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            Require(connection.ServerVersion == ServerVersion, "target_server_version_mismatch");
            using var command = Command(connection, null, "SELECT USER, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL");
            using var reader = command.ExecuteReader();
            Require(reader.Read() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser,
                "test_identity_or_schema_mismatch");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static DmCommand Command(DmConnection connection, DbTransaction? transaction, string sql)
    {
        var command = new DmCommand(sql, connection) { CommandTimeout = 20 };
        ((DbCommand)command).Transaction = transaction;
        return command;
    }
    private static int Exec(DmConnection connection, DbTransaction? transaction, string sql, params (object Value, DmDbType Type)[] values)
    {
        using var command = Command(connection, transaction, sql);
        for (int index = 0; index < values.Length; index++)
            command.Parameters.Add(new DmParameter("p" + index.ToString(CultureInfo.InvariantCulture), values[index].Type) { Value = values[index].Value });
        return command.ExecuteNonQuery();
    }
    private static object? Scalar(DmConnection connection, string sql, DbTransaction? transaction = null)
    { using var command = Command(connection, transaction, sql); return command.ExecuteScalar(); }
    private static int RowCount(DmConnection connection, string table, int id) =>
        Convert.ToInt32(Scalar(connection, $"SELECT COUNT(*) FROM {table} WHERE ID={id}"), CultureInfo.InvariantCulture);
    private static int TableCount(DmConnection connection, string table)
    {
        using var command = Command(connection, null, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:p0");
        command.Parameters.Add(new DmParameter("p0", DmDbType.VarChar) { Value = table });
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
    private static Exception? Capture(Action action) { try { action(); return null; } catch (Exception error) { return error; } }
    private static void Case(Dictionary<string, object> cases, string name, Func<bool> action)
    {
        try { bool pass = action(); cases[name] = new CaseResult(pass, pass ? "contract_verified" : "contract_failed", null); }
        catch (Exception error) { cases[name] = new CaseResult(false, "error", SafeError(error)); }
    }
    private static void Case(Dictionary<string, object> cases, string name, Func<CaseResult> action)
    {
        try { cases[name] = action(); }
        catch (Exception error) { cases[name] = new CaseResult(false, "error", SafeError(error)); }
    }
    private static object SafeError(Exception error) => new
    { type = error.GetType().FullName, kind = error is ProbeFailure failure ? failure.Kind : null, number = error is DmException dm ? (int?)dm.Number : null };
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed record CaseResult(bool Pass, string Status, object? Error, object? Details = null);
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
}
