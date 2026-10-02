using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using W.Dm;

internal static partial class Program
{
    private static async Task RunSecurityAsync(string reportPath)
    {
        using var handshake = new HandshakeCapture();
        string? certificateDirectory = null;
        bool verifyAfter = false;
        try
        {
            Stage = "security_before_identity";
            Report["before"] = await SecurityIdentityAsync(handshake).ConfigureAwait(false);
            var builder = new DmConnectionStringBuilder(Settings) { TransportSecurity = DmTransportSecurity.RequireTls };
            if (Mode == "tls")
            {
                Stage = "security_unrelated_public_ca";
                certificateDirectory = Path.Combine(Path.GetDirectoryName(reportPath)!, "security-public-ca-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(certificateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string certificatePath = Path.Combine(certificateDirectory, "unrelated-ca.pem");
                using var key = RSA.Create(2048);
                var request = new CertificateRequest("CN=WDM-T13-Unrelated-Public-CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
                // Export only the public certificate. The generated private key stays in memory.
                byte[] pem = Encoding.ASCII.GetBytes(certificate.ExportCertificatePem());
                await using (var file = new FileStream(certificatePath, new FileStreamOptions { Mode = FileMode.CreateNew,
                    Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
                    await file.WriteAsync(pem, Token).ConfigureAwait(false);
                Require(File.GetUnixFileMode(certificatePath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite) &&
                    File.GetUnixFileMode(certificateDirectory) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute),
                    "security_public_ca_permissions_invalid");
                builder.TlsCaCertificatePath = certificatePath;
                Report["unrelated_ca"] = new { public_certificate_only = true, certificate_mode = "0600", directory_mode = "0700" };
            }

            Stage = "security_negative_open_async";
            Observation!.Stage = Stage;
            handshake.Opcodes.Clear();
            int modesBefore = Observation.EncryptModes.Count;
            int framesBefore = Observation.Frames.Count;
            var countersBefore = SecurityCounters();
            Exception? failure = null;
            verifyAfter = true;
            await using (var rejected = new DmConnection(builder.ConnectionString))
            {
                try { await rejected.OpenAsync(Token).ConfigureAwait(false); }
                catch (Exception error) { failure = error; }
                var countersAfter = SecurityCounters();
                long created = countersAfter.Created - countersBefore.Created;
                long disposed = countersAfter.Disposed - countersBefore.Disposed;
                long tlsSuccess = countersAfter.TlsSuccess - countersBefore.TlsSuccess;
                long tlsFailure = countersAfter.TlsFailure - countersBefore.TlsFailure;
                int[] modes = Observation.EncryptModes.Skip(modesBefore).ToArray();
                Require(failure?.GetType() == (Mode == "tls" ? typeof(AuthenticationException) : typeof(NotSupportedException)),
                    "security_exact_rejection_required");
                Require(rejected.State == ConnectionState.Closed, "security_failed_connection_not_closed");
                Require(handshake.Opcodes.SequenceEqual(new short[] { 200 }) && modes.SequenceEqual(new[] { Mode == "tls" ? 1 : 0 }),
                    "security_login_or_downgrade_observed");
                Require(created == 1 && disposed == 1 && tlsSuccess == 0 && tlsFailure == (Mode == "tls" ? 1 : 0),
                    "security_transport_cleanup_invalid");
                Report["security_case"] = new { classification = Mode == "tls" ? "unknown_ca_rejected_before_login" : "require_tls_rejected_plaintext_before_login",
                    exception_type = failure!.GetType().FullName, connection_state = rejected.State.ToString(),
                    handshake_opcodes = handshake.Opcodes.ToArray(), login_encoded_count = handshake.Opcodes.Count(op => op == 1),
                    negotiated_encrypt_modes = modes, created_tcp_sockets = created, disposed_tcp_sockets = disposed,
                    successful_tls_upgrades = tlsSuccess, failed_tls_upgrades = tlsFailure,
                    explicit_transport = builder.TransportSecurity.ToString() };
                Report["negative_numeric_frame_observations"] = Observation.Frames.Skip(framesBefore).ToArray();
            }
            Report["final_database_state"] = "no_objects_created";
        }
        finally
        {
            try
            {
                if (verifyAfter)
                {
                    string priorStage = Stage;
                    Stage = "security_after_identity";
                    Report["after"] = await SecurityIdentityAsync(handshake).ConfigureAwait(false);
                    Stage = priorStage;
                }
            }
            finally
            {
                if (certificateDirectory != null)
                {
                    string certificatePath = Path.Combine(certificateDirectory, "unrelated-ca.pem");
                    if (File.Exists(certificatePath)) File.Delete(certificatePath);
                    if (Directory.Exists(certificateDirectory)) Directory.Delete(certificateDirectory);
                    Report["temporary_public_ca_removed"] = true;
                }
            }
        }
    }

    private static async Task<object> SecurityIdentityAsync(HandshakeCapture handshake)
    {
        handshake.Opcodes.Clear();
        int modesBefore = Observation!.EncryptModes.Count;
        await using var connection = await OpenVerifiedAsync().ConfigureAwait(false);
        Require(handshake.Opcodes.Count(op => op == 200) == 1 && handshake.Opcodes.Count(op => op == 1) == 1,
            "security_handshake_hook_positive_control_failed");
        int[] modes = Observation.EncryptModes.Skip(modesBefore).ToArray();
        Require(modes.SequenceEqual(new[] { Mode == "tls" ? 1 : 0 }), "security_identity_transport_mode_mismatch");
        return new { identity = TestUser, schema = TestUser, negotiated_encrypt_mode = modes[0],
            login_encoded_count = 1, server_version = connection.ServerVersion };
    }

    private static (long Created, long Disposed, long TlsSuccess, long TlsFailure) SecurityCounters()
    {
        var type = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")
            ?? throw new ProbeFailure("security_transport_counters_missing");
        long Read(string name) => (long)(type.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("security_transport_counter_missing"));
        return (Read("CreatedTcpSockets"), Read("DisposedTcpSockets"), Read("SuccessfulTlsUpgrades"), Read("FailedTlsUpgrades"));
    }

    private sealed class HandshakeCapture : IDisposable
    {
        private readonly FieldInfo Field;
        private readonly object? Previous;
        internal List<short> Opcodes { get; } = [];
        internal HandshakeCapture()
        {
            Field = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")?
                .GetField("AfterHandshakeExchangeEntered", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("security_handshake_hook_missing");
            Previous = Field.GetValue(null);
            Require(Previous == null, "isolated_security_handshake_hook_required");
            var arguments = Field.FieldType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
            Require(arguments.Length == 2 && arguments[1].Type == typeof(short), "security_handshake_hook_signature_invalid");
            var add = Expression.Call(Expression.Constant(Opcodes), typeof(List<short>).GetMethod(nameof(List<short>.Add))!, arguments[1]);
            Field.SetValue(null, Expression.Lambda(Field.FieldType, add, arguments).Compile());
            Report["handshake_hook"] = "DmWireTestHooks.AfterHandshakeExchangeEntered_via_HandshakeFrameEncoded";
        }
        public void Dispose() => Field.SetValue(null, Previous);
    }
}
