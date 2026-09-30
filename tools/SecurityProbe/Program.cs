using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Transport;

const string TestUser = "WDM_PROVIDER_TEST";
string mode = args.Length == 1 ? args[0] : "invalid";
bool negative = mode is "unknown-ca" or "unknown-ca-mode4" or "unknown-ca-plaintext-allowed" or
    "plaintext-reject" or "auth-only-reject";
var result = new Dictionary<string, object?>
{
    ["schema_version"] = 1,
    ["task"] = "T07",
    ["implementation"] = "W",
    ["probe"] = mode,
    ["integration"] = mode == "plaintext-reject" ? "existing_plaintext_test_schema" : "isolated_local_tls_test_schema",
    ["run_id"] = Guid.NewGuid().ToString("N")
};
result["server_configured_encrypt_mode"] = mode switch
{
    "auth-only-reject" => 2,
    "mode4-smoke" or "unknown-ca-mode4" => 4,
    "mode5-smoke" => 5,
    "plaintext-reject" => 0,
    _ => 1
};
string stage = "arguments";
bool accountVerified = false, queryVerified = false, closed = false, failureClosedOnReturn = false;
bool crudVerified = false, transactionVerified = false, blobReadbackVerified = false, cleanupVerified = false;
bool expectedFailureObserved = false;
int? negotiatedMode = null;
string? serverVersion = null, temporaryCa = null, settings = null, table = null;
long firstOpenTcpConnections = 0, firstOpenTlsUpgrades = 0;
Exception? workError = null;
DmConnection? connection = null;
var openingOpcodes = new List<short>();
DmTransportTestHooks.Reset();
DmWireTestHooks.Reset();
try
{
    if (mode is not ("smoke" or "real" or "mode4-smoke" or "mode5-smoke" or
                     "unknown-ca" or "unknown-ca-mode4" or "unknown-ca-plaintext-allowed" or
                     "plaintext-reject" or "auth-only-reject"))
        throw new ProbeFailure("usage");
    stage = "configuration";
    string variable = mode == "plaintext-reject" ? "DAMENG_TEST_CONNECTION_STRING" : "DAMENG_TLS_TEST_CONNECTION_STRING";
    string raw = Environment.GetEnvironmentVariable(variable) ?? throw new ProbeFailure("test_connection_missing");
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase))
        throw new ProbeFailure("test_account_invalid");
    if (mode != "plaintext-reject")
    {
        if (!string.Equals(builder.Server, "127.0.0.1", StringComparison.Ordinal) || builder.Port != 15236)
            throw new ProbeFailure("isolated_tls_target_invalid");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string certs = Path.Combine(root, ".local", "t07", "certs", "client_ssl", TestUser);
        builder.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem");
        builder.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
        builder.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem");
        builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
        if (!File.Exists(builder.TlsCaCertificatePath) || !File.Exists(builder.TlsClientCertificatePath) ||
            !File.Exists(builder.TlsClientPrivateKeyPath))
            throw new ProbeFailure("tls_test_certificate_files_missing");
        if (mode is "unknown-ca" or "unknown-ca-mode4" or "unknown-ca-plaintext-allowed")
        {
            temporaryCa = CreateUnknownAuthority();
            builder.TlsCaCertificatePath = temporaryCa;
        }
        result["revocation_policy"] = "NoCheck";
        result["revocation_reason"] = "isolated_ephemeral_ca_without_crl";
    }
    builder.TransportSecurity = mode == "unknown-ca-plaintext-allowed"
        ? DmTransportSecurity.PlaintextAllowed : DmTransportSecurity.RequireTls;
    builder.PersistSecurityInfo = false;
    builder.Schema = TestUser;
    result["explicit_transport"] = builder.TransportSecurity.ToString();
    settings = builder.ConnectionString;
    DmWireTestHooks.AfterHandshakeExchangeEntered = (_, opcode) => openingOpcodes.Add(opcode);
    DmWireTestHooks.AfterStartupNegotiatedEncryptMode = value => negotiatedMode = value;
    stage = "open";
    connection = new DmConnection(settings);
    try { connection.Open(); }
    catch (Exception ex) when (negative)
    {
        expectedFailureObserved = true;
        result["rejection_kind"] = ex.GetType().Name;
        result["rejection_inner_kind"] = ex.InnerException?.GetType().Name;
        failureClosedOnReturn = connection.State == ConnectionState.Closed;
    }
    if (connection.State == ConnectionState.Open)
        negotiatedMode ??= connection.GetConnInstance().ConnProperty.Encrypt;
    firstOpenTcpConnections = DmTransportTestHooks.SuccessfulTcpConnections;
    firstOpenTlsUpgrades = DmTransportTestHooks.SuccessfulTlsUpgrades;
    if (negative)
    {
        if (!expectedFailureObserved) throw new ProbeFailure("expected_rejection_missing");
    }
    else
    {
        stage = "identity";
        serverVersion = connection.ServerVersion;
        accountVerified = string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(),
            TestUser, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.Schema, TestUser, StringComparison.OrdinalIgnoreCase);
        if (!accountVerified) throw new ProbeFailure("tls_test_identity_invalid");
        queryVerified = Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL"), CultureInfo.InvariantCulture) == 1;
        if (!queryVerified) throw new ProbeFailure("tls_test_query_invalid");
        if (mode == "real")
        {
            table = "T07_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
            result["object_name"] = table;
            stage = "business_roundtrip";
            var proof = BusinessRoundtrip(connection, settings, table);
            crudVerified = proof.Crud;
            transactionVerified = proof.Transaction;
            blobReadbackVerified = proof.Blob;
            if (!crudVerified || !transactionVerified || !blobReadbackVerified)
                throw new ProbeFailure("tls_business_roundtrip_invalid");
        }
    }
}
catch (Exception ex)
{
    workError = ex;
    result["work_stage"] = stage;
    result["work_error_kind"] = ex is ProbeFailure failure ? failure.Kind : ex.GetType().Name;
    result["work_inner_kind"] = ex.InnerException?.GetType().Name;
}
finally
{
    try
    {
        connection?.Close();
        closed = connection == null || connection.State == ConnectionState.Closed;
        connection?.Dispose();
    }
    catch (Exception ex)
    {
        workError ??= ex;
        result["close_error_kind"] = ex.GetType().Name;
    }
    DmWireTestHooks.Reset();
    if (mode == "real" && settings != null && table != null)
    {
        try
        {
            using var cleaner = OpenVerified(settings);
            if (ObjectExists(cleaner, table)) Exec(cleaner, $"DROP TABLE {table}");
            using var readback = OpenVerified(settings);
            cleanupVerified = !ObjectExists(readback, table);
        }
        catch (Exception ex)
        {
            workError ??= ex;
            result["cleanup_error_kind"] = ex.GetType().Name;
        }
    }
    if (temporaryCa != null) File.Delete(temporaryCa);
}
result["negotiated_encrypt_mode"] = negotiatedMode;
result["server_version"] = serverVersion;
result["opening_opcodes"] = openingOpcodes.ToArray();
result["login_not_encoded"] = !openingOpcodes.Contains((short)1);
result["server_account_verified"] = accountVerified;
result["query_verified"] = queryVerified;
result["crud_verified"] = crudVerified;
result["transaction_verified"] = transactionVerified;
result["blob_96k_readback_verified"] = blobReadbackVerified;
result["cleanup_verified"] = cleanupVerified;
result["connection_closed_verified"] = closed;
result["failure_closed_before_cleanup"] = failureClosedOnReturn;
result["successful_tcp_connections"] = DmTransportTestHooks.SuccessfulTcpConnections;
result["first_open_successful_tcp_connections"] = firstOpenTcpConnections;
result["successful_tls_upgrades"] = DmTransportTestHooks.SuccessfulTlsUpgrades;
result["first_open_successful_tls_upgrades"] = firstOpenTlsUpgrades;
result["failed_tls_upgrades"] = DmTransportTestHooks.FailedTlsUpgrades;
result["negotiated_tls_protocol"] = DmTransportTestHooks.LastNegotiatedTlsProtocol.ToString();
result["created_tcp_sockets"] = DmTransportTestHooks.CreatedTcpSockets;
result["disposed_tcp_sockets"] = DmTransportTestHooks.DisposedTcpSockets;
result["final_database_state"] = mode == "real"
    ? cleanupVerified ? "random_object_absent" : "not_verified" : "no_object_created";
bool socketClosed = DmTransportTestHooks.CreatedTcpSockets > 0 &&
                    DmTransportTestHooks.CreatedTcpSockets == DmTransportTestHooks.DisposedTcpSockets;
bool passed = workError == null && closed && socketClosed && firstOpenTcpConnections == 1 &&
    (mode switch
    {
        "smoke" or "real" or "mode4-smoke" or "mode5-smoke" =>
                   accountVerified && queryVerified && negotiatedMode == (mode is "mode4-smoke" or "mode5-smoke" ? 4 : 1) &&
                   !string.IsNullOrWhiteSpace(serverVersion) &&
                   firstOpenTlsUpgrades == 1 &&
                   DmTransportTestHooks.FailedTlsUpgrades == 0 &&
                   DmTransportTestHooks.LastNegotiatedTlsProtocol is SslProtocols.Tls12 or SslProtocols.Tls13 &&
                   (mode != "real" || crudVerified && transactionVerified && blobReadbackVerified && cleanupVerified),
        "unknown-ca" => expectedFailureObserved && failureClosedOnReturn && negotiatedMode == 1 &&
                        !openingOpcodes.Contains((short)1) &&
                        DmTransportTestHooks.SuccessfulTlsUpgrades == 0 && DmTransportTestHooks.FailedTlsUpgrades == 1,
        "unknown-ca-mode4" => expectedFailureObserved && failureClosedOnReturn && negotiatedMode == 4 &&
                        !openingOpcodes.Contains((short)1) &&
                        DmTransportTestHooks.SuccessfulTlsUpgrades == 0 && DmTransportTestHooks.FailedTlsUpgrades == 1,
        "unknown-ca-plaintext-allowed" => expectedFailureObserved && failureClosedOnReturn && negotiatedMode == 1 &&
                        !openingOpcodes.Contains((short)1) &&
                        DmTransportTestHooks.SuccessfulTlsUpgrades == 0 && DmTransportTestHooks.FailedTlsUpgrades == 1,
        "plaintext-reject" => expectedFailureObserved && failureClosedOnReturn && negotiatedMode == 0 &&
                              !openingOpcodes.Contains((short)1) &&
                              DmTransportTestHooks.SuccessfulTlsUpgrades == 0,
        "auth-only-reject" => expectedFailureObserved && failureClosedOnReturn && negotiatedMode == 2 &&
                              !openingOpcodes.Contains((short)1) &&
                              DmTransportTestHooks.SuccessfulTlsUpgrades == 0,
        _ => false
    });
result["status"] = passed ? negative ? "tls_rejection_verified" : mode == "real" ? "tls_real_verified" : "tls_smoke_verified" : "rejected";
Console.WriteLine(JsonSerializer.Serialize(result));
return passed ? 0 : 1;

static (bool Crud, bool Transaction, bool Blob) BusinessRoundtrip(DmConnection owner, string settings, string table)
{
    const string initial = "TLS-汉字-Ω";
    const string updated = "TLS-更新-β";
    byte[] payload = Enumerable.Range(0, 96 * 1024).Select(index => (byte)(index % 251)).ToArray();
    Exec(owner, $"CREATE TABLE {table} (ID INT PRIMARY KEY, TXT VARCHAR(100), PAYLOAD BLOB)");
    using (var transaction = owner.BeginTransaction())
    {
        if (Exec(owner, $"INSERT INTO {table} (ID, TXT, PAYLOAD) VALUES (:p0, :p1, :p2)", transaction,
                1, initial, payload) != 1)
            throw new ProbeFailure("tls_insert_invalid");
        transaction.Commit();
    }
    bool blobValid;
    using (var independent = OpenVerified(settings))
    using (var command = Command(independent, $"SELECT TXT, PAYLOAD FROM {table} WHERE ID = :p0", null, 1))
    using (var reader = command.ExecuteReader())
    {
        if (!reader.Read() || reader.GetString(0) != initial) throw new ProbeFailure("tls_unicode_readback_invalid");
        var observed = new byte[payload.Length];
        long offset = 0;
        while (offset < observed.Length)
        {
            long copied = reader.GetBytes(1, offset, observed, (int)offset,
                Math.Min(4096, observed.Length - (int)offset));
            if (copied <= 0) throw new ProbeFailure("tls_blob_short_read");
            offset += copied;
        }
        blobValid = observed.SequenceEqual(payload) && !reader.Read();
        if (!blobValid) throw new ProbeFailure("tls_blob_readback_invalid");
    }
    using (var transaction = owner.BeginTransaction())
    {
        if (Exec(owner, $"UPDATE {table} SET TXT = :p0 WHERE ID = :p1", transaction, updated, 1) != 1)
            throw new ProbeFailure("tls_update_invalid");
        transaction.Commit();
    }
    using (var independent = OpenVerified(settings))
        if (!string.Equals(Scalar(independent, $"SELECT TXT FROM {table} WHERE ID = :p0", 1)?.ToString(),
                updated, StringComparison.Ordinal))
            throw new ProbeFailure("tls_update_readback_invalid");
    using (var transaction = owner.BeginTransaction())
    {
        Exec(owner, $"INSERT INTO {table} (ID, TXT) VALUES (:p0, :p1)", transaction, 2, "rollback");
        transaction.Rollback();
    }
    using (var independent = OpenVerified(settings))
        if (Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture) != 1)
            throw new ProbeFailure("tls_rollback_invalid");
    if (Exec(owner, $"DELETE FROM {table} WHERE ID = :p0", null, 1) != 1)
        throw new ProbeFailure("tls_delete_invalid");
    using (var independent = OpenVerified(settings))
        if (Convert.ToInt32(Scalar(independent, $"SELECT COUNT(*) FROM {table}"), CultureInfo.InvariantCulture) != 0)
            throw new ProbeFailure("tls_delete_readback_invalid");
    return (true, true, blobValid);
}

static DmConnection OpenVerified(string settings)
{
    var connection = new DmConnection(settings);
    try
    {
        connection.Open();
        if (!string.Equals(Scalar(connection, "SELECT USER FROM DUAL")?.ToString()?.Trim(), TestUser,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.Schema, TestUser, StringComparison.OrdinalIgnoreCase))
            throw new ProbeFailure("tls_test_identity_invalid");
        return connection;
    }
    catch { connection.Dispose(); throw; }
}

static bool ObjectExists(DmConnection connection, string table) =>
    Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :p0", table),
        CultureInfo.InvariantCulture) != 0;

static object? Scalar(DmConnection connection, string sql, params object[] values)
{
    using var command = Command(connection, sql, null, values);
    return command.ExecuteScalar();
}

static int Exec(DmConnection connection, string sql, DbTransaction? transaction = null, params object[] values)
{
    using var command = Command(connection, sql, transaction, values);
    return command.ExecuteNonQuery();
}

static DbCommand Command(DmConnection connection, string sql, DbTransaction? transaction, params object[] values)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Transaction = transaction;
    for (int index = 0; index < values.Length; index++)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "p" + index.ToString(CultureInfo.InvariantCulture);
        parameter.DbType = values[index] switch { int => DbType.Int32, byte[] => DbType.Binary, _ => DbType.String };
        parameter.Value = values[index];
        command.Parameters.Add(parameter);
    }
    return command;
}

static string CreateUnknownAuthority()
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=WDM-T07-Unknown-CA", key, HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddDays(1));
    string path = Path.Combine(Path.GetTempPath(), "wdm-t07-unknown-ca-" + Guid.NewGuid().ToString("N") + ".pem");
    File.WriteAllText(path, certificate.ExportCertificatePem());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    return path;
}

sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
