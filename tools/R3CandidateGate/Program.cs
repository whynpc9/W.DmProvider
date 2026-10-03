using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;

try
{
    if (args.Length != 2) throw new InvalidOperationException();
    Assembly driver = typeof(DmConnection).Assembly;
    string version = driver.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    if (version != args[0]) throw new InvalidOperationException();
    var api = driver.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type => new
    {
        type = type.FullName,
        members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Constructor or MemberTypes.Property or MemberTypes.Field or MemberTypes.Event)
            .Select(member => member.ToString()).OrderBy(value => value, StringComparer.Ordinal).ToArray()
    }).ToArray();
    string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(driver.Location)));
    var report = new
    {
        schema_version = 1, task = "T19", status = "candidate_identity_observed", version,
        driver_dll_sha256 = hash, driver_mvid = driver.ManifestModule.ModuleVersionId.ToString("D"),
        runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
        rid = RuntimeInformation.RuntimeIdentifier, process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        runtime_references = driver.GetReferencedAssemblies().Select(name => new { name = name.Name, version = name.Version?.ToString() })
            .OrderBy(name => name.name, StringComparer.Ordinal).ToArray(),
        public_api = api
    };
    File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("{\"task\":\"T19\",\"status\":\"candidate_identity_observed\"}");
    return 0;
}
catch
{
    Console.WriteLine("{\"task\":\"T19\",\"status\":\"rejected\",\"reason\":\"identity_observation_failed\"}");
    return 1;
}
