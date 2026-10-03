using System.Data;
using System.Data.Common;
using System.Text.Json;
using W.Dm;
using W.Dm.filter;
using W.Dm.Internal.Legacy.A;
using W.Dm.util;
using Xunit;

namespace W.DmProvider.ConfigurationTests;

public sealed class ConfigurationTests
{
    private const string Canary = "T04_SECRET_7b88b146c12f";
    private const string Safe = "server=127.0.0.1;user=test_user;password=" + Canary;

    [Fact]
    public void SafeDefaultsAndStandardUnits()
    {
        var b = new DmConnectionStringBuilder();
        Assert.True(string.IsNullOrEmpty(b.Server), "Host must have no default.");
        Assert.True(string.IsNullOrEmpty(b.User), "User must have no default.");
        Assert.True(string.IsNullOrEmpty(b.Password), "Password must have no default.");
        Assert.Equal(5236, b.Port);
        Assert.Equal(5000, b.ConnectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), b.ConnectTimeout);
        Assert.Equal(5000, b.ConnPoolTimeout);
        Assert.Equal(0, b.SocketTimeout);
        Assert.Equal(30, b.CommandTimeout);
        Assert.Equal(DmTransportSecurity.RequireTls, b.TransportSecurity);
        Assert.False(b.PersistSecurityInfo);
        using var empty = new DmConnection();
        Assert.Equal(5, empty.ConnectionTimeout);
        Assert.Equal(30, empty.CreateCommand().CommandTimeout);
        Assert.Equal(30, new DmCommand().CommandTimeout);
        Assert.Equal(30, new DmCommand("SELECT 1").CommandTimeout);
        using var one = new DmConnection(Safe + ";connect_timeout=1;command_timeout=0");
        Assert.Equal(1, one.ConnectionTimeout);
        Assert.Equal(0, one.CreateCommand().CommandTimeout);
        using var ceil = new DmConnection(Safe + ";connect_timeout=1001");
        Assert.Equal(2, ceil.ConnectionTimeout);
        using var unlimited = new DmConnection(Safe + ";connect_timeout=0");
        Assert.Equal(0, unlimited.ConnectionTimeout);

        var fixedLimits = new DmConnectionStringBuilder(
            "max_message_size=67108864;max_materialized_lob_size=67108864;lob_chunk_size=32768");
        Assert.True(fixedLimits.Remove("max_message_size"));
        Assert.True(fixedLimits.Remove("max_materialized_lob_size"));
        Assert.True(fixedLimits.Remove("lob_chunk_size"));
        Assert.Equal(64 * 1024 * 1024, fixedLimits.MaxMessageSize);
        Assert.Equal(64 * 1024 * 1024, fixedLimits.MaxMaterializedLobSize);
        Assert.Equal(32 * 1024, fixedLimits.LobChunkSize);
        var afterRemove = fixedLimits.ToSettings();
        var roundTrip = new DmConnectionStringBuilder(fixedLimits.ConnectionString).ToSettings();
        Assert.Equal(afterRemove.MaxMessageSize, roundTrip.MaxMessageSize);
        Assert.Equal(afterRemove.MaxMaterializedLobSize, roundTrip.MaxMaterializedLobSize);
        Assert.Equal(afterRemove.LobChunkSize, roundTrip.LobChunkSize);
    }

    [Fact]
    public void IncompleteAuthenticationRejectsBeforeNetwork()
    {
        foreach (var raw in new[] { "", "user=u;password=p", "server=127.0.0.1;password=p", "server=127.0.0.1;user=u" })
        {
            using var c = new DmConnection(raw);
            Assert.Throws<InvalidOperationException>(() => c.Open());
            Assert.Equal(ConnectionState.Closed, c.State);
        }
        // T07 now opens TCP to inspect STARTUP negotiation. RequireTls against
        // a plaintext server is covered by the T07 no-LOGIN transport probe.
    }

    [Fact]
    public void ConnectingEventFailureRestoresClosedState()
    {
        using var c = new DmConnection(Safe + ";port=1;transport_security=PlaintextAllowed");
        var entered = false;
        c.StateChange += (_, change) =>
        {
            if (change.CurrentState != ConnectionState.Connecting) return;
            entered = true;
            Assert.Throws<InvalidOperationException>(() => c.ConnectionString = Safe);
            Assert.Throws<InvalidOperationException>(() => c.Schema = "other");
            throw new InvalidOperationException("Synthetic connecting event failure.");
        };
        Assert.Throws<InvalidOperationException>(() => c.Open());
        Assert.True(entered, "Connecting callback was not reached.");
        Assert.Equal(ConnectionState.Closed, c.State);
    }

    [Fact]
    public void LegacyMillisecondsAndTypedTimeSpanAgree()
    {
        var b = new DmConnectionStringBuilder("connect_timeout=1201;conn_pool_timeout=4000;socketTimeout=3000;command_timeout=0");
        Assert.Equal(TimeSpan.FromMilliseconds(1201), b.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), b.PoolAcquireTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), b.ReadIdleTimeout);
        Assert.Equal(0, b.CommandTimeout);
        b.ConnectTimeout = TimeSpan.FromMilliseconds(2501);
        b.PoolAcquireTimeout = TimeSpan.FromMilliseconds(1200);
        b.ReadIdleTimeout = TimeSpan.Zero;
        b.CleanupTimeout = TimeSpan.FromSeconds(6);
        Assert.Equal(2501, b.ConnectionTimeout);
        Assert.Equal(1200, b.ConnPoolTimeout);
        Assert.Equal(0, b.SocketTimeout);
        Assert.Equal(TimeSpan.FromSeconds(6), b.CleanupTimeout);
        using var c = new DmConnection(b.ConnectionString);
        Assert.Equal(3, c.ConnectionTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ConnectTimeout = TimeSpan.FromTicks(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ConnectTimeout = TimeSpan.FromTicks(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ConnectTimeout = TimeSpan.FromMilliseconds((long)int.MaxValue + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.CleanupTimeout = TimeSpan.Zero);
    }

    [Fact]
    public void StrictAliasAssignmentAndRecovery()
    {
        var b = new DmConnectionStringBuilder("server=localhost;host=localhost;uid=u;user id=u;pwd=p;password=p");
        Assert.Equal("localhost", b.Server);
        Assert.Equal("u", b.User);
        Assert.Equal("p", b.Password);
        Assert.Throws<ArgumentException>(() => new DmConnectionStringBuilder("server=one;host=two"));
        Assert.Throws<ArgumentException>(() => b["host"] = "other");
        Assert.Throws<InvalidOperationException>(() => b.ToRedactedString());
        b.Clear();
        b["server"] = "one";
        b.Remove("server");
        b["host"] = "two";
        Assert.Equal("two", b.Server);
        b.Server = "three"; // Typed properties support replacing a value.
        Assert.Equal("three", b.Server);
        b.ConnectionString = Safe;
        Assert.True(b.ToRedactedString().Length > 0);
    }

    [Fact]
    public void DerivedSetterRollbackAndBaseSetterFailureFamilies()
    {
        var b = new DmConnectionStringBuilder(Safe);
        Assert.Throws<ArgumentException>(() => b.ConnectionString = "server=one;host=two");
        Assert.Equal("127.0.0.1", b.Server);
        Assert.True(b.ConnectionString.Contains("test_user", StringComparison.Ordinal));
        DbConnectionStringBuilder baseView = b;
        Assert.Throws<ArgumentException>(() => baseView.ConnectionString = "server=one;host=two");
        Assert.True(b.ToRedactedString().Length > 0); // .NET 10 rolls back this family.
        Assert.Equal("127.0.0.1", b.Server);
        Assert.Throws<ArgumentOutOfRangeException>(() => baseView.ConnectionString = "port=0");
        Assert.True(b.ToRedactedString().Length > 0);
        Assert.Throws<NotSupportedException>(() => baseView.ConnectionString = "unknown_option=1");
        Assert.Throws<InvalidOperationException>(() => b.ToRedactedString());
        Assert.Throws<InvalidOperationException>(() => _ = b.ConnectionString);
        // A base setter may leave partial state; only validated consumption is prohibited.
        b.ConnectionString = Safe;
        Assert.True(b.ToRedactedString().Length > 0);
        Assert.Throws<NotSupportedException>(() => baseView.ConnectionString = "stmt_pooling=true");
        Assert.Throws<InvalidOperationException>(() => b.ToRedactedString());
        b.Clear();
        Assert.True(b.ToRedactedString().Length > 0);
    }

    [Fact]
    public void SecretAndSnapshotsRemainLocal()
    {
        var b = new DmConnectionStringBuilder(Safe + ";schema=alpha;language=1");
        Assert.True(b.Password == Canary);
        Assert.False(b.ToRedactedString().Contains(Canary, StringComparison.Ordinal));
        using var a = new DmConnection(b.ConnectionString);
        using var other = new DmConnection(Safe.Replace("test_user", "other_user", StringComparison.Ordinal) + ";schema=beta;language=2");
        b.Password = "changed";
        b.Schema = "changed";
        Assert.True(a.Password == Canary);
        Assert.Equal("alpha", a.Schema);
        Assert.Equal("test_user", a.User);
        Assert.Equal("beta", other.Schema);
        Assert.Equal("other_user", other.User);
        Assert.Equal(1, new DmConnectionStringBuilder(a.ConnectionString).Language);
        Assert.Equal(2, new DmConnectionStringBuilder(other.ConnectionString).Language);
        var clone = a.Clone();
        using (clone)
        {
            Assert.True(clone.Password == Canary);
            Assert.Equal("alpha", clone.Schema);
        }
        a.Schema = "gamma";
        Assert.Equal("gamma", a.Schema);
        Assert.Equal("beta", other.Schema);
    }

    [Fact]
    public void UnsupportedAndAdvancedEntrypointsReject()
    {
        Assert.True(new DmConnectionStringBuilder("conn_pooling=true").Pooling);
        Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder("enlist=true"));
        Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder("initial catalog=other"));
        Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder("unknown_t04_option=1"));
        using var c = new DmConnection(Safe);
        Assert.Throws<NotSupportedException>(() => c.EnlistTransaction(null!));
        Assert.Throws<NotSupportedException>(() => c.SetDatabase("other"));
        Assert.Throws<NotSupportedException>(() => c.fldrStatement(null!));
        Assert.Throws<NotSupportedException>(() => new DmBulkCopy(c));
        Assert.Throws<NotSupportedException>(() => new DmBulkCopy2(c));
        Assert.Throws<NotSupportedException>(() => new DmFldrExport("127.0.0.1", 1, "u", "p", DM_CHARSET.UTF8));
        Assert.Throws<NotSupportedException>(() => c.getConnPoolKey());
        Assert.Throws<NotSupportedException>(() => c.filterHead = new BaseFilter());
        Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder().filterHead = new BaseFilter());
        Assert.Throws<NotSupportedException>(() => new DmCommand().filterHead = new BaseFilter());
        Assert.Throws<NotSupportedException>(() => DmTrace.Level = TraceLevel.Debug);
        Assert.Equal(TraceLevel.None, DmTrace.Level);
    }

    [Fact]
    public void NegotiatedSecurityFlagsAreRejectedBeforeLegacyCipherUse()
    {
        var baseline = new DmConnProperty();
        DmHandshakeSecurityGuard.Validate(baseline);
        foreach (var mode in new[] { 1, 2, 4 })
        {
            var property = new DmConnProperty { Encrypt = mode };
            Assert.Throws<NotSupportedException>(() => DmHandshakeSecurityGuard.Validate(property));
        }
        var passwordEncryption = new DmConnProperty { Encrypt = 0, encryptPwd = true };
        Assert.Throws<NotSupportedException>(() => DmHandshakeSecurityGuard.Validate(passwordEncryption));
        var messageEncryption = new DmConnProperty { Encrypt = 0, encryptMsg = true };
        Assert.Throws<NotSupportedException>(() => DmHandshakeSecurityGuard.Validate(messageEncryption));
    }

    [Fact]
    public void SchemaEntryPointsShareValidationAndQuoteOneIdentifier()
    {
        var b = new DmConnectionStringBuilder(Safe);
        using var c = new DmConnection(Safe);
        var boundary = new string('A', 128);
        b.Schema = boundary;
        c.Schema = boundary;
        Assert.Equal(boundary, b.Schema);
        Assert.Equal(boundary, c.Schema);
        Assert.Equal("set schema \"" + boundary + "\"", DriverUtil.FormatSchemaStatement(boundary));

        foreach (var invalid in new[] { new string('A', 129), "a\0b", "a\rb", "a\nb", "a\r\nb" })
        {
            Assert.Throws<ArgumentException>(() => b.Schema = invalid);
            Assert.Throws<ArgumentException>(() => c.Schema = invalid);
            Assert.Throws<ArgumentException>(() => new DmConnectionStringBuilder(Safe + ";schema=" + invalid));
            Assert.Throws<ArgumentException>(() => new DmConnection(Safe + ";schema=" + invalid));
            Assert.Throws<ArgumentException>(() => DriverUtil.FormatSchemaStatement(invalid));
            Assert.Equal(boundary, b.Schema);
            Assert.Equal(boundary, c.Schema);
        }

        const string quoted = "Ab\";X";
        b.Schema = quoted;
        c.Schema = quoted;
        Assert.Equal(quoted, b.Schema);
        Assert.Equal(quoted, c.Schema);
        Assert.Equal("set schema \"Ab\"\";X\"", DriverUtil.FormatSchemaStatement(quoted));
        Assert.Equal(quoted, new DmConnectionStringBuilder(b.ConnectionString).Schema);
        Assert.Equal(quoted, new DmConnectionStringBuilder(c.ConnectionString).Schema);

        b.Schema = null!;
        c.Schema = null!;
        Assert.Equal(string.Empty, b.Schema);
        Assert.Equal(string.Empty, c.Schema);
        Assert.Null(DriverUtil.FormatSchemaStatement(null!));
        Assert.Null(DriverUtil.FormatSchemaStatement(string.Empty));
        Assert.Equal(string.Empty, new DmConnectionStringBuilder(Safe + ";schema=").Schema);
        Assert.Equal(string.Empty, new DmConnection(Safe + ";schema=").Schema);
    }

    [Fact]
    public void FixtureCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "cases.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        foreach (var item in root.GetProperty("parse_success").EnumerateArray())
        {
            var b = new DmConnectionStringBuilder(item.GetProperty("connection_string").GetString()!);
            var expected = item.GetProperty("expected");
            CheckString(expected, "server", b.Server);
            CheckString(expected, "user", b.User);
            CheckString(expected, "password", b.Password);
            CheckString(expected, "schema", b.Schema);
            CheckInt(expected, "connect_timeout_ms", b.ConnectionTimeout);
            CheckInt(expected, "pool_timeout_ms", b.ConnPoolTimeout);
            CheckInt(expected, "socket_timeout_ms", b.SocketTimeout);
            CheckInt(expected, "command_timeout_seconds", b.CommandTimeout);
            CheckInt(expected, "max_pool_size", b.MaxPoolSize);
            if (expected.TryGetProperty("pooling", out var pooling)) Assert.Equal(pooling.GetBoolean(), b.Pooling);
            CheckString(expected, "transport_security", b.TransportSecurity.ToString());
        }
        foreach (var item in root.GetProperty("parse_rejected").EnumerateArray())
        {
            var raw = item.GetProperty("connection_string").GetString()!;
            var kind = item.GetProperty("error_kind").GetString();
            Exception? failure = Record.Exception(() => new DmConnectionStringBuilder(raw));
            var id = item.GetProperty("id").GetString();
            Assert.True(failure != null, "Fixture rejection unexpectedly accepted: " + id);
            Assert.True(failure!.GetType().Name == kind, "Fixture rejection had wrong exception class: " + id);
            if (item.TryGetProperty("canary", out var value))
            {
                var secret = value.GetString()!;
                Assert.False(failure.ToString().Contains(secret, StringComparison.Ordinal), "Exception leaked a canary.");
            }
            if (id == "unknown_option_value_canary_is_not_echoed")
                Assert.False(failure.ToString().Contains("unknown_option", StringComparison.Ordinal), "Exception echoed an unknown key.");
        }
        foreach (var item in root.GetProperty("unsupported_nondefault").EnumerateArray())
        {
            var raw = item.GetProperty("connection_string").GetString()!;
            Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder(raw));
        }
        foreach (var item in root.GetProperty("timeout_typed").EnumerateArray())
        {
            var b = new DmConnectionStringBuilder();
            var span = TimeSpan.FromTicks(item.GetProperty("ticks").GetInt64());
            var name = item.GetProperty("property").GetString();
            Action assign = name switch
            {
                "ConnectTimeout" => () => b.ConnectTimeout = span,
                "PoolAcquireTimeout" => () => b.PoolAcquireTimeout = span,
                "ReadIdleTimeout" => () => b.ReadIdleTimeout = span,
                "CleanupTimeout" => () => b.CleanupTimeout = span,
                _ => throw new InvalidDataException("Unknown fixture property")
            };
            if (item.TryGetProperty("expected_legacy_milliseconds", out var expected))
            {
                assign();
                var actual = name switch
                {
                    "ConnectTimeout" => b.ConnectionTimeout,
                    "PoolAcquireTimeout" => b.ConnPoolTimeout,
                    "ReadIdleTimeout" => b.SocketTimeout,
                    "CleanupTimeout" => (int)b.CleanupTimeout.TotalMilliseconds,
                    _ => -1
                };
                Assert.True(actual == expected.GetInt32(), "Typed timeout conversion differs from fixture.");
            }
            else Assert.Throws<ArgumentOutOfRangeException>(assign);
        }
    }

    private static void CheckString(JsonElement expected, string key, string actual)
    {
        if (expected.TryGetProperty(key, out var value))
            Assert.True(string.Equals(value.GetString(), actual, StringComparison.Ordinal), "Fixture string differs.");
    }
    private static void CheckInt(JsonElement expected, string key, int actual)
    {
        if (expected.TryGetProperty(key, out var value))
            Assert.True(value.GetInt32() == actual, "Fixture integer differs.");
    }
}
