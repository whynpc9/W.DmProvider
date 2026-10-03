using System;
using System.Collections.Generic;
using System.Data.Common;
using W.Dm.Config;

namespace W.Dm;

/// <summary>A per-connection snapshot. No mutable configuration objects escape it.</summary>
internal sealed class DmConnectionSettings
{
    internal const int DefaultMaxMessageSize = 64 * 1024 * 1024;
    internal const int DefaultMaxMaterializedLobSize = 64 * 1024 * 1024;
    internal const int DefaultLobChunkSize = 32 * 1024;

    internal string Host { get; }
    internal int Port { get; }
    internal string User { get; }
    internal string Password { get; }
    internal string Schema { get; }
    internal int Language { get; }
    internal TimeSpan ConnectTimeout { get; }
    internal int CommandTimeout { get; }
    internal TimeSpan PoolAcquireTimeout { get; }
    internal bool Pooling { get; }
    internal int MaxPoolSize { get; }
    internal int MaxPoolWaiters { get; }
    internal TimeSpan ReadIdleTimeout { get; }
    internal TimeSpan CleanupTimeout { get; }
    internal DmTransportSecurity TransportSecurity { get; }
    internal string TlsCaCertificatePath { get; }
    internal string TlsClientCertificatePath { get; }
    internal string TlsClientPrivateKeyPath { get; }
    internal string TlsClientCertificatePassword { get; }
    internal DmTlsRevocationMode TlsRevocationMode { get; }
    internal bool PersistSecurityInfo { get; }
    internal int MaxMessageSize => DefaultMaxMessageSize;
    internal int MaxMaterializedLobSize => DefaultMaxMaterializedLobSize;
    internal int LobChunkSize => DefaultLobChunkSize;

    internal DmConnectionSettings(DmConnectionStringBuilder builder)
    {
        Host = builder.Server;
        Port = builder.Port;
        User = builder.User;
        Password = builder.Password;
        Schema = builder.Schema;
        Language = builder.Language;
        ConnectTimeout = builder.ConnectTimeout;
        CommandTimeout = builder.CommandTimeout;
        PoolAcquireTimeout = builder.PoolAcquireTimeout;
        Pooling = builder.Pooling;
        MaxPoolSize = builder.MaxPoolSize;
        MaxPoolWaiters = builder.MaxPoolWaiters;
        ReadIdleTimeout = builder.ReadIdleTimeout;
        CleanupTimeout = builder.CleanupTimeout;
        TransportSecurity = builder.TransportSecurity;
        TlsCaCertificatePath = builder.TlsCaCertificatePath;
        TlsClientCertificatePath = builder.TlsClientCertificatePath;
        TlsClientPrivateKeyPath = builder.TlsClientPrivateKeyPath;
        TlsClientCertificatePassword = builder.TlsClientCertificatePassword;
        TlsRevocationMode = builder.TlsRevocationMode;
        PersistSecurityInfo = builder.PersistSecurityInfo;
        ValidateTlsConfiguration();
    }

    private DmConnectionSettings(DmConnectionSettings source, string schema)
    {
        Host = source.Host;
        Port = source.Port;
        User = source.User;
        Password = source.Password;
        Schema = schema;
        Language = source.Language;
        ConnectTimeout = source.ConnectTimeout;
        CommandTimeout = source.CommandTimeout;
        PoolAcquireTimeout = source.PoolAcquireTimeout;
        Pooling = source.Pooling;
        MaxPoolSize = source.MaxPoolSize;
        MaxPoolWaiters = source.MaxPoolWaiters;
        ReadIdleTimeout = source.ReadIdleTimeout;
        CleanupTimeout = source.CleanupTimeout;
        TransportSecurity = source.TransportSecurity;
        TlsCaCertificatePath = source.TlsCaCertificatePath;
        TlsClientCertificatePath = source.TlsClientCertificatePath;
        TlsClientPrivateKeyPath = source.TlsClientPrivateKeyPath;
        TlsClientCertificatePassword = source.TlsClientCertificatePassword;
        TlsRevocationMode = source.TlsRevocationMode;
        PersistSecurityInfo = source.PersistSecurityInfo;
    }

    internal DmConnectionSettings WithSchema(string schema)
    {
        return new DmConnectionSettings(this, DmSchemaValidator.Normalize(schema));
    }

    internal static DmConnectionSettings Parse(string connectionString) =>
        new DmConnectionStringBuilder(connectionString).ToSettings();

    private void ValidateTlsConfiguration()
    {
        if (TlsClientPrivateKeyPath.Length != 0 && TlsClientCertificatePath.Length == 0)
            throw new ArgumentException("A TLS private key requires a client certificate.");
        if (TlsClientCertificatePassword.Length != 0 && TlsClientCertificatePath.Length == 0)
            throw new ArgumentException("A TLS certificate password requires a client certificate.");
    }

    internal string ToConnectionString(bool includeSecrets)
    {
        var output = new DbConnectionStringBuilder
        {
            ["server"] = Host,
            ["port"] = Port,
            ["user"] = User,
            ["schema"] = Schema,
            ["language"] = Language,
            ["connect_timeout"] = checked((long)ConnectTimeout.TotalMilliseconds),
            ["command_timeout"] = CommandTimeout,
            ["conn_pool_timeout"] = checked((long)PoolAcquireTimeout.TotalMilliseconds),
            ["conn_pooling"] = Pooling,
            ["conn_pool_size"] = MaxPoolSize,
            ["max_pool_waiters"] = MaxPoolWaiters,
            ["socketTimeout"] = checked((long)ReadIdleTimeout.TotalMilliseconds),
            ["cleanup_timeout"] = checked((long)CleanupTimeout.TotalMilliseconds),
            ["transport_security"] = TransportSecurity.ToString(),
            ["persist_security_info"] = PersistSecurityInfo
        };
        if (includeSecrets)
            output["password"] = Password;
        if (TlsCaCertificatePath.Length != 0) output["tls_ca_certificate_path"] = TlsCaCertificatePath;
        if (TlsClientCertificatePath.Length != 0) output["tls_client_certificate_path"] = TlsClientCertificatePath;
        if (TlsClientPrivateKeyPath.Length != 0) output["tls_client_private_key_path"] = TlsClientPrivateKeyPath;
        if (includeSecrets && TlsClientCertificatePassword.Length != 0)
            output["tls_client_certificate_password"] = TlsClientCertificatePassword;
        if (TlsRevocationMode != DmTlsRevocationMode.Online)
            output["tls_revocation_mode"] = TlsRevocationMode.ToString();
        return output.ConnectionString;
    }

    internal Dictionary<string, object> ToLegacyProperties()
    {
        var result = new Dictionary<string, object>(new DmConnectionStringBuilder().property, StringComparer.OrdinalIgnoreCase)
        {
            [DmConst.PROP_KEY_SERVER] = Host,
            [DmConst.PROP_KEY_PORT] = Port,
            [DmConst.PROP_KEY_USER] = User,
            [DmConst.PROP_KEY_PASSWORD] = Password,
            [DmConst.PROP_KEY_SCHEMA] = Schema,
            [DmConst.PROP_KEY_LANGUAGE] = (SupportedLanguage)Language,
            [DmConst.PROP_KEY_CONNECTION_TIMEOUT] = checked((int)ConnectTimeout.TotalMilliseconds),
            [DmConst.PROP_KEY_COMMAND_TIMEOUT] = CommandTimeout,
            [DmConst.PROP_KEY_CONN_POOL_TIMEOUT] = checked((int)PoolAcquireTimeout.TotalMilliseconds),
            // The owned scheduler never activates the unverified legacy pool.
            [DmConst.PROP_KEY_CONN_POOLING] = false,
            [DmConst.PROP_KEY_CONN_POOL_SIZE] = MaxPoolSize,
            [DmConst.PROP_KEY_SOCKET_TIMEOUT] = checked((int)ReadIdleTimeout.TotalMilliseconds)
        };
		result[DmConst.PROP_KEY_DM_SVC_PATH] = string.Empty;
		result[DmConst.PROP_KEY_LOG_DIR] = string.Empty;
        return result;
    }

    public override string ToString() => ToConnectionString(includeSecrets: false);
}
