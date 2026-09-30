using System.Text.Json;
using W.Dm;

if (args.Length != 1 || args[0] != "release-environment")
{
    Console.WriteLine(JsonSerializer.Serialize(new { task = "R1", status = "rejected", reason = "usage" }));
    return 64;
}
string? raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(raw))
{
    Console.WriteLine(JsonSerializer.Serialize(new { task = "R1", status = "rejected",
        reason = "integration_environment_missing", missing = new[] { "DAMENG_TEST_CONNECTION_STRING" }, release_accepted = false }));
    return 66;
}
try
{
    // Use the real provider's aliases, quoting and duplicate-key rules. Password
    // contents cannot impersonate an actual User Id key. This performs no login.
    var builder = new DmConnectionStringBuilder(raw);
    if (!string.Equals(builder.User, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { task = "R1", status = "rejected",
            reason = "configured_test_identity_invalid", release_accepted = false }));
        return 67;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { task = "R1", status = "release_environment_preflight_verified",
        configured_test_identity_verified = true, server_identity_verified = false,
        integration = "not_executed", release_accepted = false }));
    return 0;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { task = "R1", status = "rejected",
        reason = "integration_configuration_invalid", exception_kind = error.GetType().FullName, release_accepted = false }));
    return 68;
}
