using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

const string manifestRelative = "upstream/DM.DmProvider/8.3.1.47463/manifest.json";
const string assemblyRelative = "packages/extracted/lib/net9.0/DM.DmProvider.dll";
string[] roots = ["packages", "decompiled"];
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
json.Converters.Add(new JsonStringEnumConverter());

try
{
    if (args.Length is < 1 or > 3 || args[0] is not ("capture" or "verify"))
        throw new SnapshotFailure("usage", "Expected capture|verify [repository-root] [manifest-path].");
    var command = args[0];
    var root = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
    var manifestPath = Path.GetFullPath(args.Length > 2 ? args[2] : Path.Combine(root, manifestRelative));
    if (command == "capture")
    {
        var snapshot = Capture(root);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(snapshot, json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(new { status = "captured", file_count = snapshot.Files.Count }, json));
    }
    else
    {
        if (!File.Exists(manifestPath)) throw new SnapshotFailure("manifest_missing", "The snapshot manifest is missing.");
        var expected = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(manifestPath), json)
            ?? throw new SnapshotFailure("manifest_invalid", "The snapshot manifest is invalid.");
        var actual = Capture(root);
        var errors = Compare(expected, actual).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { status = errors.Length == 0 ? "verified" : "rejected",
            errors, current_runtime = Environment.Version.ToString(), current_sdk_version = SdkVersion() }, json));
        if (errors.Length != 0) Environment.ExitCode = 1;
    }
}
catch (SnapshotFailure ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", error_kind = ex.Kind }, json));
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", error_kind = ex.GetType().Name }, json));
    Environment.ExitCode = 1;
}

Snapshot Capture(string root)
{
    var files = new List<SnapshotFile>();
    foreach (var relativeRoot in roots)
    {
        var source = Path.Combine(root, relativeRoot);
        if (!Directory.Exists(source)) throw new SnapshotFailure("source_missing", "A source tree is missing.");
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            files.Add(new(relative, new FileInfo(path).Length, Hash(path)));
        }
    }
    const string packageRelative = "packages/dm.dmprovider.8.3.1.47463.nupkg";
    var package = Path.Combine(root, packageRelative);
    var assembly = Path.Combine(root, assemblyRelative);
    if (!File.Exists(package) || !File.Exists(assembly)) throw new SnapshotFailure("source_missing", "A source asset is missing.");
    files.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
    using var stream = File.OpenRead(assembly);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata) throw new SnapshotFailure("assembly_metadata_missing", "Assembly metadata is missing.");
    var md = pe.GetMetadataReader();
    var asm = md.GetAssemblyDefinition();
    string? tfm = null;
    foreach (var handle in asm.GetCustomAttributes())
    {
        var attribute = md.GetCustomAttribute(handle);
        var name = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent).Name),
            HandleKind.MethodDefinition => md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()).Name),
            _ => null
        };
        if (name != "TargetFrameworkAttribute") continue;
        var blob = md.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() == 1) tfm = blob.ReadSerializedString();
    }
    var satellite = files.Where(f => f.Path.StartsWith("packages/extracted/lib/net9.0/", StringComparison.Ordinal)
        && f.Path.EndsWith("/DM.DmProvider.resources.dll", StringComparison.Ordinal)).Select(f => f.Path).ToArray();
    var sdkVersion = SdkVersion();
    return new Snapshot(1, "DM.DmProvider", "8.3.1.47463", packageRelative, assemblyRelative,
        md.GetGuid(md.GetModuleDefinition().Mvid).ToString(), asm.Version.ToString(), tfm,
        tfm is null ? "TargetFrameworkAttribute absent" : null, satellite,
        "860988296cb95deeb600328cb7bcd2160237a94f", null,
        "Historical ilspycmd version was not recorded in source evidence.",
        ["ilspycmd -p -o decompiled/net8.0 packages/extracted/lib/net8.0/DM.DmProvider.dll",
         "ilspycmd -p -o decompiled/net9.0 packages/extracted/lib/net9.0/DM.DmProvider.dll"],
        "docs/reverse/README.md:137-138", "UpstreamSnapshot PEReader/System.Reflection.Metadata",
        Environment.Version.ToString(), sdkVersion,
        sdkVersion is null ? "Selected dotnet SDK version could not be read." : null, files);
}

IEnumerable<object> Compare(Snapshot expected, Snapshot actual)
{
    if (expected.SchemaVersion != 1 || expected.PackageId != actual.PackageId || expected.PackageVersion != actual.PackageVersion
        || expected.PackagePath != actual.PackagePath || expected.AssemblyPath != actual.AssemblyPath
        || expected.SourceCommit != actual.SourceCommit || expected.CaptureTool != actual.CaptureTool
        || expected.HistoricalDecompilerVersion != actual.HistoricalDecompilerVersion
        || expected.HistoricalDecompilerMissingReason != actual.HistoricalDecompilerMissingReason
        || expected.HistoricalDecompilerCommandSource != actual.HistoricalDecompilerCommandSource
        || !expected.HistoricalDecompilerCommands.SequenceEqual(actual.HistoricalDecompilerCommands))
        yield return new { kind = "manifest_metadata_mismatch", path = "" };
    if (!System.Text.RegularExpressions.Regex.IsMatch(expected.CaptureRuntime ?? "", @"^\d+\.\d+\.\d+$")
        || (expected.CaptureSdkVersion is null
            ? string.IsNullOrWhiteSpace(expected.CaptureSdkMissingReason)
            : !System.Text.RegularExpressions.Regex.IsMatch(expected.CaptureSdkVersion, @"^\d+\.\d+\.\d+$")
                || expected.CaptureSdkMissingReason is not null))
        yield return new { kind = "capture_environment_invalid", path = "" };
    foreach (var item in expected.Files.ToDictionary(f => f.Path, StringComparer.Ordinal))
    {
        var found = actual.Files.FirstOrDefault(f => f.Path == item.Key);
        if (found is null) yield return new { kind = "file_missing", path = item.Key };
        else if (found != item.Value) yield return new { kind = "file_changed", path = item.Key };
    }
    var known = expected.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
    foreach (var file in actual.Files.Where(f => !known.Contains(f.Path)))
        yield return new { kind = "file_added", path = file.Path };
    if (expected.AssemblyMvid != actual.AssemblyMvid || expected.AssemblyVersion != actual.AssemblyVersion
        || expected.TargetFramework != actual.TargetFramework || expected.TargetFrameworkMissingReason != actual.TargetFrameworkMissingReason
        || !expected.SatelliteResources.SequenceEqual(actual.SatelliteResources))
        yield return new { kind = "assembly_metadata_mismatch", path = assemblyRelative };
}

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

static string? SdkVersion()
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        if (process is null) return null;
        var version = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") ? version : null;
    }
    catch { return null; }
}

sealed record SnapshotFile(string Path, long Length, string Sha256);
sealed record Snapshot(int SchemaVersion, string PackageId, string PackageVersion, string PackagePath,
    string AssemblyPath, string AssemblyMvid, string AssemblyVersion, string? TargetFramework,
    string? TargetFrameworkMissingReason, string[] SatelliteResources, string SourceCommit,
    string? HistoricalDecompilerVersion, string HistoricalDecompilerMissingReason, string[] HistoricalDecompilerCommands,
    string HistoricalDecompilerCommandSource, string CaptureTool,
    string CaptureRuntime, string? CaptureSdkVersion, string? CaptureSdkMissingReason, List<SnapshotFile> Files);
sealed class SnapshotFailure(string kind, string message) : Exception(message) { public string Kind { get; } = kind; }
