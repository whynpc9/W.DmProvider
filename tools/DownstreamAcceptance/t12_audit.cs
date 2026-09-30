using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using W.Dm;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || args[0] is not ("before" or "after" or "permissions")) return 64;
        var result = new Dictionary<string, object?> { ["task"] = "T12", ["mode"] = args[0], ["accepted"] = false };
        string stage = "driver_hash";
        try
        {
            string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(DmConnection).Assembly.Location)));
            result["driver_dll_sha256"] = hash;
            if (!string.Equals(hash, args[3], StringComparison.OrdinalIgnoreCase)) throw new AuditFailure("driver_hash_mismatch");
            stage = "identity";
            await using var connection = await OpenVerified();
            result["identity_verified"] = true;
            result["current_schema_verified"] = true;
            result["server_version"] = connection.ServerVersion;
            if (args[0] == "permissions")
            {
                string history = "T12PF_" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
                var options = new DbContextOptionsBuilder<AuditContext>()
                    .UseDameng(connection, options => options.MigrationsHistoryTable(history)).Options;
                await using var context = new AuditContext(options);
                context.Database.SetCommandTimeout(20);
                var repository = context.GetService<IHistoryRepository>();
                stage = "history_exists_readonly";
                bool exists = await repository.ExistsAsync();
                result["owned_history_absent"] = !exists;
                if (exists) throw new AuditFailure("random_history_collision");
                result["history_catalog_readable"] = true;
                stage = "original_migration_lock_acquire";
                // Uses the unchanged provider's fixed lock ID 20260723, never custom unlock SQL.
                await using (var ownedLock = await repository.AcquireDatabaseLockAsync())
                {
                    result["original_lock_acquired"] = true;
                    stage = "original_migration_lock_release";
                }
                result["original_lock_released"] = true;
            }
            else
            {
                stage = "owned_schema_inventory";
                var inventory = await Inventory(connection);
                result["inventory_count"] = inventory.Count;
                result["inventory_sha256"] = Hash(JsonSerializer.Serialize(inventory));
                if (args[0] == "before")
                    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(inventory) + "\n");
                else
                {
                    var baseline = JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(args[2]))!;
                    bool equal = baseline.SequenceEqual(inventory);
                    result["independent_final_inventory_matches"] = equal;
                    result["new_object_hash_count"] = inventory.Except(baseline).Count();
                    result["removed_object_hash_count"] = baseline.Except(inventory).Count();
                    if (!equal) throw new AuditFailure("owned_schema_inventory_changed");
                }
            }
            result["accepted"] = true;
        }
        catch (Exception error)
        {
            result["failed_stage"] = stage;
            result["error"] = SafeError(error);
        }
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("T12 audit " + args[0] + " accepted=" + result["accepted"]);
        return result["accepted"] is true ? 0 : 1;
    }

    private static async Task<DmConnection> OpenVerified()
    {
        string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? throw new AuditFailure("integration_pending");
        var builder = new DmConnectionStringBuilder(raw)
        {
            TransportSecurity = DmTransportSecurity.PlaintextAllowed,
            PersistSecurityInfo = false,
            Schema = "WDM_PROVIDER_TEST",
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };
        if (!string.Equals(builder.User, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
            throw new AuditFailure("test_user_required_before_authentication");
        var connection = new DmConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandTimeout = 20;
            command.CommandText = "SELECT USER, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.GetString(0) != "WDM_PROVIDER_TEST" || reader.GetString(1) != "WDM_PROVIDER_TEST")
                throw new AuditFailure("test_identity_or_schema_mismatch");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<List<string>> Inventory(DbConnection connection)
    {
        var hashes = new List<string>();
        foreach (var pair in new[] { ("USER_TABLES", "TABLE_NAME"), ("USER_VIEWS", "VIEW_NAME"), ("USER_SEQUENCES", "SEQUENCE_NAME") })
        {
            using var command = connection.CreateCommand();
            command.CommandTimeout = 20;
            command.CommandText = "SELECT " + pair.Item2 + " FROM " + pair.Item1;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) hashes.Add(Hash(pair.Item1 + ":" + reader.GetString(0)));
        }
        hashes.Sort(StringComparer.Ordinal);
        return hashes;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static object SafeError(Exception error)
    {
        var list = new List<object>();
        for (Exception? e = error; e is not null; e = e.InnerException)
            list.Add(new { type = e.GetType().FullName, probe_kind = e is AuditFailure failure ? failure.Kind : null,
                number = e is DmException dm ? (int?)dm.Number : null,
                methods = new StackTrace(e, false).GetFrames()?.Take(20).Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name).ToArray() });
        return list;
    }
}
internal sealed class AuditContext(DbContextOptions<AuditContext> options) : DbContext(options);
internal sealed class AuditFailure(string kind) : Exception { internal string Kind { get; } = kind; }
