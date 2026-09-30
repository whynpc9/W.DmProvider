using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Transport;

const string TestUser = "WDM_PROVIDER_TEST";
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T06",
    ["implementation"] = "W",
    ["integration"] = "real_test_schema",
    ["run_id"] = Guid.NewGuid().ToString("N")
};
string stage = "arguments";
string? table = null;
string? settings = null;
bool accountVerified = false, independentReadback = false, cleanupVerified = false;
bool singleTcpOpen = false;
var observedHandshakeOpcodes = new List<short>();
var faultResults = new Dictionary<string, (bool Closed, bool Released, bool Reopened)>();
int successfulTcpConnectionsOnOpen = 0;
int tcpConnectionAttemptsOnOpen = 0;
Exception? workError = null, cleanupError = null;

try
{
    if (args.Length != 1 || args[0] is not ("real" or "smoke")) throw new ProbeFailure("usage");
    stage = "configuration";
    string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new ProbeFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("configured_account_invalid");
    builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
    builder.PersistSecurityInfo = false;
    builder.Schema = TestUser;
    settings = builder.ConnectionString;
    result["explicit_transport"] = "PlaintextAllowed";
    result["explicit_test_schema"] = true;
    if (args[0] == "smoke")
    {
        stage = "server_identity";
        DmTransportTestHooks.Reset();
        DmWireTestHooks.AfterHandshakeExchangeEntered = (_, opcode) => observedHandshakeOpcodes.Add(opcode);
        using var smoke = OpenVerified(settings);
        DmWireTestHooks.Reset();
        result["server_account_verified"] = true;
        result["opening_opcodes"] = observedHandshakeOpcodes.ToArray();
        result["successful_tcp_connections_on_first_open"] = DmTransportTestHooks.SuccessfulTcpConnections;
        result["tcp_connection_attempts_on_first_open"] = DmTransportTestHooks.AttemptedTcpConnections;
        result["status"] = DmTransportTestHooks.SuccessfulTcpConnections == 1 ? "smoke_verified" : "rejected";
        Console.WriteLine(JsonSerializer.Serialize(result));
        return DmTransportTestHooks.SuccessfulTcpConnections == 1 ? 0 : 1;
    }
    table = "T06_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
    result["object_name"] = table;

    stage = "server_identity";
    DmTransportTestHooks.Reset();
    DmWireTestHooks.AfterHandshakeExchangeEntered = (_, opcode) => observedHandshakeOpcodes.Add(opcode);
    using (var owner = OpenVerified(settings))
    {
        DmWireTestHooks.Reset();
        result["opening_opcodes"] = observedHandshakeOpcodes.ToArray();
        successfulTcpConnectionsOnOpen = checked((int)DmTransportTestHooks.SuccessfulTcpConnections);
        tcpConnectionAttemptsOnOpen = checked((int)DmTransportTestHooks.AttemptedTcpConnections);
        singleTcpOpen = successfulTcpConnectionsOnOpen == 1;
        if (!singleTcpOpen) throw new ProbeFailure("single_tcp_open_invalid");
        accountVerified = true;

        stage = "create_table";
        Exec(owner, $"CREATE TABLE {table} (ID INT PRIMARY KEY, VAL VARCHAR(100))");
        stage = "insert";
        Exec(owner, $"INSERT INTO {table} (ID, VAL) VALUES (:p0, :p1)", 1, "t06");
        stage = "independent_readback";
        using var independent = OpenVerified(settings);
        independentReadback = Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table} WHERE ID = 1 AND VAL = 't06'"),
            CultureInfo.InvariantCulture) == 1;
        if (!independentReadback) throw new ProbeFailure("independent_readback_invalid");
    }
}
catch (Exception ex)
{
    DmWireTestHooks.Reset();
    workError = ex;
    result["work_stage"] = stage;
    result["first_failure_inner_kind"] = ex.InnerException?.GetType().Name;
    result["first_failure_chain"] = SafeChain(ex);
    result["first_failure_methods"] = new StackTrace(ex, true).GetFrames()?.Take(8)
        .Select(frame => new { type = frame.GetMethod()?.DeclaringType?.Name,
            method = frame.GetMethod()?.Name, line = frame.GetFileLineNumber() });
    result["first_failure_tcp_attempts"] = DmTransportTestHooks.AttemptedTcpConnections;
    result["first_failure_tcp_successes"] = DmTransportTestHooks.SuccessfulTcpConnections;
    result["first_failure_socket_creations"] = DmTransportTestHooks.CreatedTcpSockets;
    result["first_failure_socket_disposals"] = DmTransportTestHooks.DisposedTcpSockets;
    result["first_failure_sent_bytes"] = DmWireTestHooks.SentBytes;
}
finally
{
    if (settings != null && table != null)
    {
        stage = "cleanup";
        try
        {
            using var cleaner = OpenVerified(settings);
            if (ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            using var readback = OpenVerified(settings);
            cleanupVerified = !ObjectExists(readback, table);
        }
        catch (Exception ex) { cleanupError = ex; }
    }
}

if (workError == null && cleanupError == null && cleanupVerified && settings != null)
{
    try
    {
        short schemaOpcode = observedHandshakeOpcodes.FirstOrDefault(opcode => opcode is not (200 or 1));
        if (!observedHandshakeOpcodes.Contains((short)200) || !observedHandshakeOpcodes.Contains((short)1) ||
            schemaOpcode == 0)
            throw new ProbeFailure("opening_opcode_inventory_incomplete");
        result["schema_initialization_opcode"] = schemaOpcode;
        foreach (var (label, targetOpcode) in new[]
        {
            ("startup", (short)200), ("login", (short)1), ("schema_initialization", schemaOpcode)
        })
        {
            stage = label + "_fault_close";
            DmTransportTestHooks.Reset();
            DmWireTestHooks.Reset();
            bool opcodeObserved = false;
            DmWireTestHooks.AfterHandshakeExchangeEntered = (_, opcode) =>
            {
                if (opcode != targetOpcode) return;
                opcodeObserved = true;
                throw new ProbeFailure("injected_" + label + "_failure");
            };
            bool closed;
            using (var failed = new DmConnection(settings))
            {
                try { failed.Open(); throw new ProbeFailure(label + "_fault_not_triggered"); }
                catch (Exception) when (opcodeObserved) { }
                closed = opcodeObserved && failed.State == ConnectionState.Closed;
                if (label == "startup") closed &= DmWireTestHooks.SendCount == 0;
            }
            DmWireTestHooks.Reset();
            bool released = DmTransportTestHooks.CreatedTcpSockets == 1 &&
                            DmTransportTestHooks.DisposedTcpSockets == 1 &&
                            DmTransportTestHooks.SuccessfulTcpConnections == 1;
            if (!closed || !released)
                throw new ProbeFailure(label + "_fault_resource_invalid");
            stage = label + "_post_fault_reopen";
            bool reopened;
            using (var recovered = OpenVerified(settings))
                reopened = Convert.ToInt32(Scalar(recovered, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
            faultResults[label] = (closed, released, reopened);
            if (!reopened) throw new ProbeFailure(label + "_post_fault_reopen_invalid");
        }
    }
    catch (Exception ex) { workError = ex; result["work_stage"] = stage; }
    finally { DmWireTestHooks.Reset(); }
}

result["server_account_verified"] = accountVerified;
result["single_tcp_open_verified"] = singleTcpOpen;
result["successful_tcp_connections_on_first_open"] = successfulTcpConnectionsOnOpen;
result["tcp_connection_attempts_on_first_open"] = tcpConnectionAttemptsOnOpen;
result["independent_readback_verified"] = independentReadback;
foreach (string label in new[] { "startup", "login", "schema_initialization" })
{
    faultResults.TryGetValue(label, out var fault);
    result[label + "_fault_connection_closed_verified"] = fault.Closed;
    result[label + "_fault_socket_released_verified"] = fault.Released;
    result[label + "_post_fault_reopen_verified"] = fault.Reopened;
}
result["cleanup_verified"] = cleanupVerified;
result["final_database_state"] = cleanupVerified ? "random_object_absent" : "not_verified";
if (workError != null) result["work_error_kind"] = workError is ProbeFailure failure ? failure.Kind : workError.GetType().Name;
if (cleanupError != null) result["cleanup_error_kind"] = cleanupError.GetType().Name;
if (cleanupError != null) result["cleanup_error_chain"] = SafeChain(cleanupError);
bool passed = workError == null && cleanupError == null && accountVerified && singleTcpOpen &&
              independentReadback && cleanupVerified && faultResults.Count == 3 &&
              faultResults.Values.All(value => value.Closed && value.Released && value.Reopened);
result["status"] = passed ? "real_verified" : "rejected";
Console.WriteLine(JsonSerializer.Serialize(result));
return passed ? 0 : 1;

static DmConnection OpenVerified(string settings)
{
    var connection = new DmConnection(settings);
    try
    {
        connection.Open();
        if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.Schema, TestUser, StringComparison.OrdinalIgnoreCase))
            throw new ProbeFailure("server_identity_invalid");
        return connection;
    }
    catch { connection.Dispose(); throw; }
}

static bool ObjectExists(DmConnection connection, string table) =>
    Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table),
        CultureInfo.InvariantCulture) != 0;

static object? Scalar(DmConnection connection, string sql, params object[] values)
{
    using var command = Command(connection, sql, values);
    return command.ExecuteScalar();
}

static int Exec(DmConnection connection, string sql, params object[] values)
{
    using var command = Command(connection, sql, values);
    return command.ExecuteNonQuery();
}

static DmCommand Command(DmConnection connection, string sql, object[] values)
{
    var command = (DmCommand)connection.CreateCommand();
    command.CommandText = sql;
    for (int i = 0; i < values.Length; i++)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p" + i.ToString(CultureInfo.InvariantCulture);
        parameter.DbType = values[i] is int ? DbType.Int32 : DbType.String;
        parameter.Value = values[i];
        command.Parameters.Add(parameter);
    }
    return command;
}

static object[] SafeChain(Exception error)
{
    var chain = new List<object>();
    for (Exception? current = error; current != null && chain.Count < 5; current = current.InnerException)
    {
        int? number = null;
        try { if (current.GetType().GetProperty("Number")?.GetValue(current) is int value) number = value; }
        catch { }
        chain.Add(new { type = current.GetType().Name, number, origin = current.TargetSite?.DeclaringType?.Name,
            method = current.TargetSite?.Name });
    }
    return chain.ToArray();
}

sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
