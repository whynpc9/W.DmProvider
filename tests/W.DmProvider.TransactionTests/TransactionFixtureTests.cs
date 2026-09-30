using System.Text.Json;
using Xunit;

namespace W.DmProvider.TransactionTests;

public sealed class TransactionFixtureTests
{
    [Fact]
    public void LogicalVectorsRemainDistinctFromUnrunRealDatabaseProcedures()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json"));
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var real = root.GetProperty("real_database_probes").EnumerateArray().ToArray();
        Assert.Equal(37, cases.Length);
        Assert.Equal(cases.Length, cases.Select(c => c.GetProperty("case_id").GetString()).Distinct().Count());
        Assert.All(cases, c => Assert.Equal("not_run", c.GetProperty("execution_status").GetString()));
        Assert.Equal(5, real.Length);
        Assert.All(real, c => Assert.Equal("planned_not_run", c.GetProperty("verification_status").GetString()));
        Assert.False(source.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownCommitAndDdlVectorsNeverClaimUnverifiedRollback()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "scenarios.json")));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray();
        var unknown = cases.Single(c => c.GetProperty("case_id").GetString() == "tx_commit_response_lost_unknown");
        Assert.Equal("OutcomeUnknown", unknown.GetProperty("expected").GetProperty("transaction_state").GetString());
        Assert.False(unknown.GetProperty("expected").GetProperty("is_transient").GetBoolean());
        Assert.False(unknown.GetProperty("expected").GetProperty("automatic_reconnect_or_replay").GetBoolean());
        var ddl = cases.Single(c => c.GetProperty("case_id").GetString() == "tx_ddl_implicit_commit_state_unverified");
        Assert.False(ddl.GetProperty("expected").GetProperty("rollback_claimed").GetBoolean());
        Assert.True(ddl.GetProperty("expected").GetProperty("CompletedExternally_only_if_verified").GetBoolean());
    }
}
