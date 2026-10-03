using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using W.Dm;

// Installed in each isolated test/CLI host; verifies the actual package, never reads DB credentials.
internal static class T19CandidateGuard
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void VerifyCandidate()
    {
        try
        {
            string expectedVersion = Required("DAMENG_T19_DRIVER_VERSION");
            string expectedHash = Required("DAMENG_T19_DRIVER_DLL_SHA256");
            string expectedMvid = Required("DAMENG_T19_DRIVER_DLL_MVID");
            string recordPath = Required("DAMENG_T19_DRIVER_LOAD_RECORD");
            string assetRoot = Required("DAMENG_T19_DRIVER_ASSET_ROOT");
            Need(Regex.IsMatch(expectedHash, "^[A-Fa-f0-9]{64}$"), "T19_GUARD_HASH_FORMAT");
            Need(Guid.TryParse(expectedMvid, out Guid mvid), "T19_GUARD_MVID_FORMAT");
            Assembly driver = typeof(DmConnection).Assembly;
            Assembly host = typeof(T19CandidateGuard).Assembly;
            string version = driver.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
            Need(new FileInfo(driver.Location).LinkTarget == null, "T19_GUARD_LOADED_SOURCE_SYMLINK");
            using var loadedFile = File.OpenRead(driver.Location);
            string hash = Convert.ToHexStringLower(SHA256.HashData(loadedFile));
            Need(version == expectedVersion && string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase) &&
                driver.ManifestModule.ModuleVersionId == mvid, "T19_GUARD_DRIVER_IDENTITY");
            string hostDirectory = Path.GetDirectoryName(Path.GetFullPath(host.Location))!;
            Need(Path.GetFullPath(driver.Location) == Path.Combine(hostDirectory, "W.DmProvider.dll"), "T19_GUARD_LOADED_DIRECTORY");
            using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(hostDirectory, host.GetName().Name + ".deps.json")));
            var libraries = deps.RootElement.GetProperty("libraries");
            Need(libraries.GetProperty("W.DmProvider/" + expectedVersion).GetProperty("type").GetString() == "package" &&
                !libraries.EnumerateObject().Any(item => item.Name.StartsWith("DM.DmProvider/", StringComparison.OrdinalIgnoreCase)),
                "T19_GUARD_PACKAGE_REFERENCE");
            var retained = T19LoadedDriverAssetRetention.Retain(loadedFile, assetRoot, "loaded-" + Guid.NewGuid().ToString("N") + ".dll",
                hash, driver.ManifestModule.ModuleVersionId);
            using var evidence = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(recordPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(evidence, new
            {
                schema_version = 2, driver_id = "W.DmProvider", driver_version = version,
                driver_dll_sha256 = hash, driver_dll_mvid = driver.ManifestModule.ModuleVersionId.ToString("D"),
                package_library_type = "package", loaded_in_host_directory = true,
                host_assembly = host.GetName().Name, ef_assembly_version = typeof(DbContext).Assembly.GetName().Version?.ToString(),
                process_id = Environment.ProcessId,
                retained_driver_asset = retained.RelativeName, retained_driver_sha256 = retained.Sha256,
                retained_driver_mvid = retained.LoadedMvid.ToString("D"), retained_driver_source = "loaded_driver_location"
            });
        }
        catch (GuardFailure) { throw; }
        catch { throw new GuardFailure("T19_GUARD_METADATA_OR_RECORD_IO"); }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new GuardFailure("T19_GUARD_INPUT_REQUIRED");
    private static void Need(bool condition, string code) { if (!condition) throw new GuardFailure(code); }
    private sealed class GuardFailure(string code) : InvalidOperationException(code);
}

// Pure filesystem contract shared with isolated offline tests. Production supplies only its opened loadedFile.
internal static class T19LoadedDriverAssetRetention
{
    internal sealed record Asset(string RelativeName, string Sha256, Guid LoadedMvid);
    internal static Asset Retain(FileStream loadedFile, string assetRoot, string relativeName, string expectedHash, Guid loadedMvid)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetRoot));
        if (!Path.IsPathFullyQualified(assetRoot) || !Directory.Exists(root)) Fail("ROOT_REQUIRED");
        for (DirectoryInfo? current = new(root); current != null; current = current.Parent)
            if (current.LinkTarget != null) Fail("ROOT_SYMLINK");
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(root) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) Fail("ROOT_MODE");
        if (!Regex.IsMatch(relativeName, "^loaded-[0-9a-f]{32}[.]dll$")) Fail("RELATIVE_ASSET_NAME");
        string target = Path.GetFullPath(Path.Combine(root, relativeName));
        if (Path.GetDirectoryName(target) != root || File.Exists(target) || new FileInfo(target).LinkTarget != null) Fail("TARGET_EXISTS_OR_ESCAPE");
        if (!loadedFile.CanRead || !loadedFile.CanSeek) Fail("LOADED_STREAM_REQUIRED");
        loadedFile.Position = 0;
        bool created = false;
        try
        {
            string copiedHash;
            using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[32768]; int count;
                while ((count = loadedFile.Read(buffer, 0, buffer.Length)) != 0)
                { destination.Write(buffer, 0, count); digest.AppendData(buffer, 0, count); }
                copiedHash = Convert.ToHexStringLower(digest.GetHashAndReset());
                destination.Flush(flushToDisk: true);
            }
            if (!string.Equals(copiedHash, expectedHash, StringComparison.OrdinalIgnoreCase)) Fail("COPY_HASH_MISMATCH");
            using (var persisted = File.OpenRead(target))
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(persisted)), copiedHash, StringComparison.Ordinal)) Fail("PERSISTED_HASH_MISMATCH");
            return new(relativeName, copiedHash, loadedMvid);
        }
        catch
        {
            if (created) { try { File.Delete(target); } catch { /* Never publish a proof for an incomplete retained asset. */ } }
            throw;
        }
    }
    private static void Fail(string code) => throw new InvalidOperationException("T19_ASSET_" + code);
}
