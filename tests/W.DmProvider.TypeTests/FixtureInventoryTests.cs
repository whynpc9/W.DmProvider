using System.Text.Json;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class FixtureInventoryTests
{
    [Fact]
    public void SyntheticCasesRemainInputsRatherThanExecutionEvidence()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "scenarios.json"));
        using var document = JsonDocument.Parse(stream);
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(26, cases.Length);
        Assert.Equal(26, cases.Select(item => item.GetProperty("case_id").GetString()).Distinct().Count());
        Assert.All(cases, item => Assert.Equal("not_run", item.GetProperty("execution_status").GetString()));
        Assert.Equal(
            Enumerable.Range(1, 9).Select(number => $"TYP-{number:00}").ToArray(),
            cases.Select(item => item.GetProperty("contract").GetString()).Distinct().Order().ToArray());
        var real = document.RootElement.GetProperty("real_database_probes").EnumerateArray().ToArray();
        Assert.Equal(4, real.Length);
        Assert.All(real, item => Assert.Equal("planned_not_run", item.GetProperty("verification_status").GetString()));
    }
}
