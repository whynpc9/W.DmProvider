using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    private sealed record HealthProof(string Lane, string RelativePath, string Sha256);
    private static readonly HealthProof[] ApprovedHealth = [
        new("old-before", ".local/verification/r2/t14/diagnostic-runs/20261001T173842Z-2669-529/probe.json", "a34f350d0f7d01613c1870fad1161fb6bb3bd4d513c2518338b69e5080fbbe10"),
        new("new", ".local/verification/r2/t14/diagnostic-runs/20261001T173931Z-2877-2597/probe.json", "ae6c3ccb0822224db0a989601f45197309d512f831a960823d48e9943481a99e"),
        new("old-after", ".local/verification/r2/t14/diagnostic-runs/20261001T174006Z-3047-13810/probe.json", "32039426eeadb8c61ae06ebc55703bf7cecff7c967da79a0f54f651a2798d5ec")
    ];
    private static void Gate(string repo, string gatePath)
    {
        byte[] gateBytes = File.ReadAllBytes(gatePath); using var gate = JsonDocument.Parse(gateBytes);
        var proofs = gate.RootElement.GetProperty("health_reports").EnumerateArray().ToArray();
        Need(proofs.Length == 3, "three_health_proofs_required");
        string[] names = ["unit_literal", "unit_int32_parameter", "catalog_literal", "catalog_varchar_parameter",
            "scalar_unit_literal", "scalar_catalog_literal", "scalar_catalog_varchar_parameter"];
        for (int index = 0; index < 3; index++)
        {
            var expected = ApprovedHealth[index]; var input = proofs[index];
            string path = Path.GetFullPath(input.GetProperty("path").GetString()!);
            Need(input.GetProperty("lane").GetString() == expected.Lane && input.GetProperty("sha256").GetString() == expected.Sha256 &&
                path == Path.GetFullPath(Path.Combine(repo, expected.RelativePath)), "health_proof_not_pinned");
            byte[] bytes = File.ReadAllBytes(path);
            Need(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expected.Sha256, "health_proof_hash_changed");
            using var document = JsonDocument.Parse(bytes); var p = document.RootElement; bool newer = index == 1;
            Need(p.GetProperty("schema_version").GetInt32() == 1 && p.GetProperty("task").GetString() == "T14-diagnostics" &&
                p.GetProperty("mode").GetString() == "health" && p.GetProperty("lane").GetString() == expected.Lane &&
                p.GetProperty("accepted").GetBoolean() && p.GetProperty("exit_code").GetInt32() == 0 &&
                p.GetProperty("status").GetString() == "diagnostic_health_verified" && p.GetProperty("read_only").GetBoolean() &&
                !p.GetProperty("ddl_performed").GetBoolean() && p.GetProperty("objects_created").GetInt32() == 0 &&
                p.GetProperty("connect_timeout_seconds").GetInt32() == 20 && p.GetProperty("command_timeout_seconds").GetInt32() == 15 &&
                p.GetProperty("explicit_transport").GetString() == "PlaintextAllowed" &&
                p.GetProperty("loaded_assembly_sha256").GetString() == (newer ? NewHash : OldHash) &&
                p.GetProperty("package_version").GetString() == (newer ? "0.1.0-r2.20261001164429" : "0.1.0-r2.20261001134645") &&
                p.GetProperty("package_sha256").GetString() == (newer ? "5b10a471c6becbfbc2d2d6474aa7f9e12fcf33fa7522d22a811b3cc0141c8122" : "b3133628fe8221efc6cace4616daaea370222b5381792b07eec9c9685a3d416c") &&
                p.GetProperty("loaded_assembly_mvid").GetString() == (newer ? "24e7baa3-bb4c-430e-85f1-9a3dbdc4ac60" : "f00bb05f-67ec-4498-9aba-1d5cf18aa42a"),
                "health_proof_contract_invalid");
            var cases = p.GetProperty("cases").EnumerateArray().ToArray(); Need(cases.Length == 7, "health_proof_cases_missing");
            for (int item = 0; item < 7; item++)
            {
                var c = cases[item]; Need(c.GetProperty("case").GetString() == names[item] && c.GetProperty("status").GetString() == "pass" &&
                    c.GetProperty("identity_verified").GetBoolean() && c.GetProperty("schema_verified").GetBoolean() &&
                    c.GetProperty("negotiated_encrypt_mode").GetInt32() == 0 && c.GetProperty("execute_returned").GetBoolean() &&
                    c.GetProperty("substage").GetString() == "Connection.Close" &&
                    c.GetProperty("observed_integer").GetInt32() == (names[item].Contains("unit_", StringComparison.Ordinal) ? 1 : 0) &&
                    !c.TryGetProperty("failure", out _) && !c.TryGetProperty("failure_substage", out _), "health_proof_case_invalid");
            }
        }
        Report["three_health_lanes_verified"] = true; Report["health_gate_sha256"] = Convert.ToHexStringLower(SHA256.HashData(gateBytes));
        Report["health_proof_sha256"] = ApprovedHealth.Select(p => p.Sha256).ToArray();
    }
}
