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
            Exec(connection, null, $"CREATE TABLE {table} (ID INT PRIMARY KEY, INTVAL INT, TXT CLOB)");
            Exec(connection, null, $"INSERT INTO {table}(ID,INTVAL,TXT) VALUES (1,NULL,:p0)", (ClobText, DmDbType.Clob));
            Exec(connection, null, $"INSERT INTO {table}(ID,INTVAL,TXT) VALUES (2,0,:p0)", ("", DmDbType.Clob));

            stage = "review_cases";
            Case(cases, "null_int32", () => NullInt32(connection, table));
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
        Exec(connection, transaction, "SAVEPOINT sp");
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
    private static object SafeError(Exception error) => new
    { type = error.GetType().FullName, kind = error is ProbeFailure failure ? failure.Kind : null, number = error is DmException dm ? (int?)dm.Number : null };
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed record CaseResult(bool Pass, string Status, object? Error, object? Details = null);
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
}
