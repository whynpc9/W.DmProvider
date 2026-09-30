using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using W.Dm;

internal static class Program
{
    private const string MaintenanceVariable = "WDM_PROVIDER_MAINTENANCE_CONNECTION_STRING";
    private const string InspectionVariable = "WDM_PROVIDER_TEST_INSPECTION_CONNECTION_STRING";
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const string GrantSql = "GRANT SOI TO \"WDM_PROVIDER_TEST\"";
    private const string RoleSql = "SELECT ADMIN_OPTION FROM DBA_ROLE_PRIVS " +
        "WHERE GRANTEE = 'WDM_PROVIDER_TEST' AND GRANTED_ROLE = 'SOI'";
    private const int CommandSeconds = 20;

    private sealed class Proof
    {
        public bool AdminConfiguredSa { get; set; }
        public bool AdminIdentitySa { get; set; }
        public bool RoleReadBefore { get; set; }
        public bool RolePresentBefore { get; set; }
        public bool GrantAttempted { get; set; }
        public bool GrantAcknowledged { get; set; }
        public bool RoleReadAfter { get; set; }
        public bool RolePresentAfter { get; set; }
        public bool RoleWithoutAdminOption { get; set; }
        public bool AdminClosed { get; set; }
    }

    private sealed class Outcome
    {
        public string TargetRole { get; } = "SOI";
        public string Code { get; set; } = "arguments_rejected";
        public string? ErrorType { get; set; }
        public int? ProviderCode { get; set; }
        public bool Success { get; set; }
        public Proof Proof { get; } = new();
    }

    public static int Main(string[] arguments)
        => arguments.Length == 1 && arguments[0] == "inspect-soi-as-test"
            ? InspectAsTest() : GrantAsAdmin(arguments);

    private static int GrantAsAdmin(string[] arguments)
    {
        var result = new Outcome();
        DmConnection? connection = null;
        try
        {
            if (arguments.Length != 1 || arguments[0] != "grant-soi-to-test")
                throw new InvalidOperationException();
            result.Code = "configuration_rejected";
            string raw = Environment.GetEnvironmentVariable(MaintenanceVariable) ?? string.Empty;
            Environment.SetEnvironmentVariable(MaintenanceVariable, null);
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException();
            var settings = new DmConnectionStringBuilder(raw);
            if (!string.Equals(settings.User, "SA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            result.Proof.AdminConfiguredSa = true;
            settings.ConnectTimeout = TimeSpan.FromSeconds(CommandSeconds);
            settings.CommandTimeout = CommandSeconds;
            settings.ReadIdleTimeout = TimeSpan.FromSeconds(CommandSeconds);
            settings.CleanupTimeout = TimeSpan.FromSeconds(5);
            settings.ConnPooling = false;
            settings.StmtPooling = false;
            settings.PreparePooling = false;
            settings.Enlist = false;
            // This maintenance scope targets the existing shared plaintext development instance.
            // No TLS certificate validation setting is relaxed.
            settings.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
            connection = new DmConnection(settings.ConnectionString);
            result.Code = "connect_failed";
            connection.Open();

            result.Code = "admin_identity_rejected";
            using (var identity = connection.CreateCommand("SELECT USER FROM DUAL"))
            {
                identity.CommandTimeout = CommandSeconds;
                object? value = identity.ExecuteScalar();
                if (value is not string name || !string.Equals(name.Trim(), "SA", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException();
            }
            result.Proof.AdminIdentitySa = true;

            result.Code = "role_read_before_failed";
            RoleState before = ReadRole(connection);
            result.Proof.RoleReadBefore = true;
            result.Proof.RolePresentBefore = before.Present;
            if (before.Present && !before.WithoutAdminOption)
            {
                result.Code = "existing_admin_option_rejected";
                throw new InvalidOperationException();
            }
            if (!before.Present)
            {
                result.Code = "grant_failed";
                using var grant = connection.CreateCommand(GrantSql);
                grant.CommandTimeout = CommandSeconds;
                result.Proof.GrantAttempted = true;
                grant.ExecuteNonQuery();
                result.Proof.GrantAcknowledged = true;
            }

            result.Code = "role_read_after_failed";
            RoleState after = ReadRole(connection);
            result.Proof.RoleReadAfter = true;
            result.Proof.RolePresentAfter = after.Present;
            result.Proof.RoleWithoutAdminOption = after.WithoutAdminOption;
            if (!after.Present || !after.WithoutAdminOption)
            {
                result.Code = "role_postcondition_rejected";
                throw new InvalidOperationException();
            }
            result.Code = "completed";
            result.Success = true;
        }
        catch (Exception error)
        {
            RecordFailure(result, error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MaintenanceVariable, null);
            if (connection != null)
            {
                try
                {
                    connection.Close();
                    connection.Dispose();
                    result.Proof.AdminClosed = connection.State == ConnectionState.Closed;
                    if (!result.Proof.AdminClosed) throw new InvalidOperationException();
                }
                catch (Exception error)
                {
                    result.Code = "admin_close_failed";
                    RecordFailure(result, error);
                }
            }
            else result.Proof.AdminClosed = true;
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    private readonly record struct RoleState(bool Present, bool WithoutAdminOption);

    private sealed class InspectionProof
    {
        public bool TestConfigured { get; set; }
        public bool TestIdentity { get; set; }
        public bool TestSchema { get; set; }
        public bool RoleRowsRead { get; set; }
        public bool TestClosed { get; set; }
    }

    private sealed class FlagObservation
    {
        public string ValueClrType { get; init; } = string.Empty;
        public object? Value { get; init; }
        public string? Sha256 { get; init; }
    }

    private sealed class InspectionOutcome
    {
        public string TargetRole { get; } = "SOI";
        public string Mode { get; } = "test_readonly_inspect";
        public string Code { get; set; } = "test_configuration_rejected";
        public string? ErrorType { get; set; }
        public int? ProviderCode { get; set; }
        public bool Success { get; set; }
        public InspectionProof Proof { get; } = new();
        public string? FieldClrType { get; set; }
        public int RowsObserved { get; set; }
        public bool RowCountComplete { get; set; }
        public List<FlagObservation> Flags { get; } = new();
    }

    // A separate path: only the caller's TEST variable and fixed read-only SELECTs.
    // It neither reads the maintenance variable nor normalizes flags to an authorization result.
    private static int InspectAsTest()
    {
        var result = new InspectionOutcome();
        DmConnection? connection = null;
        try
        {
            string raw = Environment.GetEnvironmentVariable(InspectionVariable) ?? string.Empty;
            Environment.SetEnvironmentVariable(InspectionVariable, null);
            if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException();
            var settings = new DmConnectionStringBuilder(raw);
            if (!string.Equals(settings.User, TestUser, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            result.Proof.TestConfigured = true;
            settings.ConnectTimeout = TimeSpan.FromSeconds(CommandSeconds);
            settings.CommandTimeout = CommandSeconds;
            settings.ReadIdleTimeout = TimeSpan.FromSeconds(CommandSeconds);
            settings.CleanupTimeout = TimeSpan.FromSeconds(5);
            settings.ConnPooling = false;
            settings.StmtPooling = false;
            settings.PreparePooling = false;
            settings.Enlist = false;
            settings.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
            connection = new DmConnection(settings.ConnectionString);
            result.Code = "test_connect_failed";
            connection.Open();
            result.Code = "test_identity_rejected";
            if (!ReadTestIdentity(connection, "SELECT USER FROM DUAL")) throw new InvalidOperationException();
            result.Proof.TestIdentity = true;
            result.Code = "test_schema_rejected";
            if (!ReadTestIdentity(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"))
                throw new InvalidOperationException();
            result.Proof.TestSchema = true;

            result.Code = "role_inspection_failed";
            using (var command = connection.CreateCommand(
                "SELECT ADMIN_OPTION FROM USER_ROLE_PRIVS WHERE GRANTED_ROLE = 'SOI'"))
            {
                command.CommandTimeout = CommandSeconds;
                using var reader = command.ExecuteReader();
                result.FieldClrType = reader.GetFieldType(0).FullName;
                while (reader.Read())
                {
                    result.RowsObserved++;
                    if (result.RowsObserved > 16)
                    {
                        result.Code = "role_row_limit_rejected";
                        throw new InvalidDataException();
                    }
                    result.Flags.Add(ObserveFlag(reader.GetValue(0)));
                }
                result.RowCountComplete = true;
                result.Proof.RoleRowsRead = true;
            }
            result.Code = "inspection_completed";
            result.Success = true;
        }
        catch (Exception error)
        {
            result.ErrorType = error.GetType().FullName;
            result.ProviderCode = error is DmException provider ? provider.Number : null;
        }
        finally
        {
            Environment.SetEnvironmentVariable(InspectionVariable, null);
            if (connection != null)
            {
                try
                {
                    connection.Close();
                    connection.Dispose();
                    result.Proof.TestClosed = connection.State == ConnectionState.Closed;
                    if (!result.Proof.TestClosed) throw new InvalidOperationException();
                }
                catch (Exception error)
                {
                    result.Success = false;
                    result.Code = "test_close_failed";
                    result.ErrorType = error.GetType().FullName;
                    result.ProviderCode = error is DmException provider ? provider.Number : null;
                }
            }
            else result.Proof.TestClosed = true;
        }
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    private static bool ReadTestIdentity(DmConnection connection, string fixedSql)
    {
        using var command = connection.CreateCommand(fixedSql);
        command.CommandTimeout = CommandSeconds;
        return command.ExecuteScalar() is string name &&
            string.Equals(name.Trim(), TestUser, StringComparison.OrdinalIgnoreCase);
    }

    private static FlagObservation ObserveFlag(object value)
    {
        string type = value.GetType().FullName ?? value.GetType().Name;
        if (value is bool boolean) return new FlagObservation { ValueClrType = type, Value = boolean };
        if (value is sbyte or byte or short or ushort or int)
            return new FlagObservation { ValueClrType = type, Value = Convert.ToInt32(value, CultureInfo.InvariantCulture) };
        if (value is string text && text.Length <= 8 && text is "NO" or "YES" or "N" or "Y" or "0" or "1" or "是" or "否")
            return new FlagObservation { ValueClrType = type, Value = text };
        byte[] bytes = value switch
        {
            byte[] binary => binary,
            string unknown => Encoding.UTF8.GetBytes(unknown),
            IFormattable scalar => Encoding.UTF8.GetBytes(scalar.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty),
            DBNull => Encoding.UTF8.GetBytes("DBNull"),
            _ => Encoding.UTF8.GetBytes(type)
        };
        return new FlagObservation { ValueClrType = type, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }

    private static RoleState ReadRole(DmConnection connection)
    {
        using var command = connection.CreateCommand(RoleSql);
        command.CommandTimeout = CommandSeconds;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new RoleState(false, false);
        object adminOption = reader.GetValue(0);
        // TEST readback confirmed N on this database; NO remains recognized.
        // Affirmative and unknown representations remain rejected.
        bool withoutAdminOption = adminOption is string text &&
            (string.Equals(text.Trim(), "NO", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(text.Trim(), "N", StringComparison.OrdinalIgnoreCase));
        if (reader.Read()) throw new InvalidDataException();
        return new RoleState(true, withoutAdminOption);
    }

    private static void RecordFailure(Outcome result, Exception error)
    {
        result.Success = false;
        result.ErrorType = error.GetType().FullName;
        result.ProviderCode = error is DmException provider ? provider.Number : null;
    }
}
