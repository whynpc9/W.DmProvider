using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using W.Dm;

// Test-host proof of the actual package DLL. No database access or secret reading here.
internal static class T12CandidateGuard
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void VerifyCandidate()
    {
        string expected = Environment.GetEnvironmentVariable("DAMENG_T12_DRIVER_DLL_SHA256")
            ?? throw new InvalidOperationException("T12 candidate identity is required.");
        string record = Environment.GetEnvironmentVariable("DAMENG_T12_DRIVER_LOAD_RECORD")
            ?? throw new InvalidOperationException("T12 load evidence path is required.");
        string path = typeof(DmConnection).Assembly.Location;
        string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("T12 loaded driver does not match the candidate package.");
        File.WriteAllText(record, JsonSerializer.Serialize(new
        {
            driver_dll_sha256 = actual,
            loaded_path = path,
            driver_assembly = typeof(DmConnection).Assembly.GetName().Name,
            ef_assembly_version = typeof(DbContext).Assembly.GetName().Version?.ToString(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            process_id = Environment.ProcessId
        }) + "\n");
    }
}
