using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class FixtureVectorTests
{
    [Fact]
    public void SyntheticManifestHasUniqueCasesAndNoCredentialMaterial()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json"));
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(27, cases.Length);
        Assert.Equal(cases.Length, cases.Select(c => c.GetProperty("case_id").GetString()).Distinct().Count());
        Assert.All(cases, c => Assert.Equal("not_run", c.GetProperty("execution_status").GetString()));
        Assert.False(source.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal));
        Assert.False(source.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal));
        Assert.False(source.Contains("\"pfx_base64\"", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FixtureNegotiatedModesExecuteAgainstPolicyGuard()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "scenarios.json")));
        var modes = document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("input").TryGetProperty("negotiated_wire_encrypt", out var wire) &&
                        wire.ValueKind == JsonValueKind.Number).ToArray();
        Assert.NotEmpty(modes);
        foreach (var scenario in modes)
        {
            var input = scenario.GetProperty("input");
            int mode = input.GetProperty("negotiated_wire_encrypt").GetInt32();
            var policy = input.TryGetProperty("transport_security", out var configured) &&
                configured.GetString() == "PlaintextAllowed"
                ? DmTransportSecurity.PlaintextAllowed : DmTransportSecurity.RequireTls;
            if (mode == 0 && policy == DmTransportSecurity.PlaintextAllowed)
                Assert.False(DmHandshakeSecurityGuard.RequiresFullTls(mode, policy));
            else if (mode is 1 or 4)
                Assert.True(DmHandshakeSecurityGuard.RequiresFullTls(mode, policy));
            else
                Assert.Throws<NotSupportedException>(() => DmHandshakeSecurityGuard.RequiresFullTls(mode, policy));
        }
        var configuredFive = modes.Where(c => c.GetProperty("input")
            .TryGetProperty("server_enable_encrypt", out var configured) &&
            configured.ValueKind == JsonValueKind.Number && configured.GetInt32() == 5).ToArray();
        Assert.NotEmpty(configuredFive);
        Assert.All(configuredFive, c => Assert.Equal(4,
            c.GetProperty("input").GetProperty("negotiated_wire_encrypt").GetInt32()));
        var wireFive = modes.Single(c => c.GetProperty("case_id").GetString() == "tls_wire5_unsupported");
        Assert.Equal(5, wireFive.GetProperty("input").GetProperty("negotiated_wire_encrypt").GetInt32());
        Assert.False(wireFive.GetProperty("expected").GetProperty("accepted").GetBoolean());
    }
}
