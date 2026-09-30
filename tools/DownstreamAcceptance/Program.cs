using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
#if LANE_W
using DriverConnection = W.Dm.DmConnection;
#else
using DriverConnection = Dm.DmConnection;
#endif

// Deliberately outside both production repositories. No logger receives SQL or values.
internal static class Program
{
    internal const int TimeoutSeconds = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[1] is not ("public" or "profile")) return 64;
        var output = new Dictionary<string, object?>
        {
            ["schema"] = 1, ["task"] = "T11", ["ef_commit"] = "113014cc74dd1f751ef97226a78d2ec855b32c8c",
            ["entry"] = args[1], ["utc"] = DateTimeOffset.UtcNow,
            ["category"] = IsCandidate ? "CandidateCorrectness" : "OfficialCharacterization",
            ["lane"] = IsCandidate ? "W" : "O", ["acceptance_scope"] = "ef_savechanges_matrix_only",
            ["full_ef_suite"] = "not_run", ["r1_release_gate"] = "pending"
        };
        var cases = new List<Dictionary<string, object?>>();
        output["cases"] = cases;
        bool accepted = false;
        try
        {
            string actualHash = HashFile(typeof(DriverConnection).Assembly.Location);
            output["loaded_driver_sha256"] = actualHash;
            output["loaded_driver_assembly"] = typeof(DriverConnection).Assembly.GetName().Name;
            output["ef_assembly_version"] = typeof(DbContext).Assembly.GetName().Version?.ToString();
            output["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            if (!string.Equals(actualHash, args[2], StringComparison.OrdinalIgnoreCase))
                throw new ProbeFailure("loaded_driver_hash_mismatch");
            if (!IsCandidate && args[1] != "public") throw new ProbeFailure("official_profile_forbidden");
            await using (var identity = await OpenVerified())
            {
                output["identity_verified"] = true;
                output["server_version"] = identity.ServerVersion;
            }
            foreach (var isolation in new[] { IsolationLevel.ReadCommitted, IsolationLevel.ReadUncommitted, IsolationLevel.Serializable })
            foreach (bool savepoints in new[] { true, false })
            foreach (string operation in new[] { "update", "insert" })
            foreach (string end in new[] { "commit", "rollback" })
                cases.Add(await RunCase(isolation, savepoints, operation, end, args[1]));
            accepted = cases.Count == 24 && cases.All(c => c["accepted"] is true);
        }
        catch (Exception error)
        {
            output["fatal"] = SafeError(error);
        }
        output["accepted"] = accepted;
        output["meaning"] = IsCandidate ? "candidate_matrix_correctness" : "official_observations_captured";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(output, JsonOptions) + "\n");
        Console.WriteLine("T11 EF " + (IsCandidate ? "W" : "O") + " cases=" + cases.Count + " accepted=" + accepted);
        return accepted ? 0 : 1;
    }

    private static bool IsCandidate
    {
        get
        {
#if LANE_W
            return true;
#else
            return false;
#endif
        }
    }

    private static async Task<Dictionary<string, object?>> RunCase(IsolationLevel isolation, bool savepoints,
        string operation, string end, string entry)
    {
        string table = "T11EF_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..16].ToUpperInvariant();
        var trace = new CommandTrace();
        var item = new Dictionary<string, object?>
        {
            ["isolation"] = isolation.ToString(), ["auto_savepoints"] = savepoints,
            ["operation"] = operation, ["end"] = end, ["entry"] = entry,
            ["commands"] = trace.Events, ["accepted"] = false,
            ["table"] = table, ["success"] = false, ["committed"] = false
        };
        bool createAttempted = false, workSucceeded = false, committed = false, cleanupConfirmed = false;
        bool finalMatches = false, knownCommandTextFailure = false, transactionEnded = false;
        DbTransaction? transaction = null;
        DriverConnection? connection = null;
        ProbeContext? context = null;
        string stage = "create";
        try
        {
            await using (var owner = await OpenVerified())
            {
                createAttempted = true;
                await Exec(owner, $"CREATE TABLE \"{table}\" (\"ID\" BIGINT IDENTITY(1,1) NOT NULL, \"NAME\" NVARCHAR2(200) NOT NULL, \"VERSION\" INT NOT NULL, CONSTRAINT \"PK_{table}\" NOT CLUSTER PRIMARY KEY (\"ID\"))");
                await Exec(owner, $"INSERT INTO \"{table}\" (\"NAME\", \"VERSION\") VALUES ('seed', 1)");
            }
            stage = "begin";
            connection = await OpenVerified();
            var options = new DbContextOptionsBuilder<ProbeContext>().UseDameng(connection)
                .ReplaceService<IModelCacheKeyFactory, ProbeModelCacheKeyFactory>()
                .AddInterceptors(trace).Options;
            context = new ProbeContext(options, table);
            context.Database.SetCommandTimeout(TimeoutSeconds);
            context.Database.AutoSavepointsEnabled = savepoints;
            transaction = Begin(connection, isolation, entry);
            item["transaction_type"] = transaction.GetType().FullName;
            item["transaction_before"] = TransactionSnapshot(transaction);
            var attached = context.Database.UseTransaction(transaction) ?? throw new ProbeFailure("ef_transaction_not_attached");
            item["ef_supports_savepoints"] = attached.SupportsSavepoints;
            if (savepoints && !attached.SupportsSavepoints) throw new ProbeFailure("ef_savepoint_path_unavailable");
            stage = "tracked_change";
            long seedId = 0;
            if (operation == "update")
            {
                var row = await context.Rows.SingleAsync();
                seedId = row.Id;
                row.Name = "changed";
                row.Version = 2;
            }
            else
            {
                context.Rows.Add(new ProbeRow { Name = "added", Version = 1 });
            }
            stage = "savechanges";
            int affected = await context.SaveChangesAsync();
            item["savechanges_count"] = affected;
            item["generated_key_valid"] = operation == "update" ? seedId > 0
                : context.ChangeTracker.Entries<ProbeRow>().Single().Entity.Id > 0;
            if (affected != 1 || item["generated_key_valid"] is not true)
                throw new ProbeFailure("savechanges_result_mismatch");
            workSucceeded = true;
            stage = end;
            if (end == "commit") { await transaction.CommitAsync(); committed = true; }
            else await transaction.RollbackAsync();
            transactionEnded = true;
            item["transaction_after"] = TransactionSnapshot(transaction);
            item["success"] = true;
        }
        catch (Exception error)
        {
            knownCommandTextFailure = Chain(error).Any(e => e is InvalidOperationException && e.Message.Contains("CommandText has no value", StringComparison.Ordinal));
            item["error"] = SafeError(error);
            item["failed_stage"] = stage;
            item["official_commandtext_signature"] = knownCommandTextFailure;
            if (transaction is not null) item["transaction_on_failure"] = TransactionSnapshot(transaction);
        }
        finally
        {
            if (transaction is not null && !transactionEnded)
            {
                try { await transaction.RollbackAsync(); transactionEnded = true; item["cleanup_rollback_acknowledged"] = true; }
                catch (Exception error) { item["cleanup_rollback_error"] = SafeError(error); }
            }
            if (context is not null)
            {
                try { await context.DisposeAsync(); }
                catch (Exception error) { item["context_dispose_error"] = SafeError(error); }
            }
            if (transaction is not null)
            {
                try { await transaction.DisposeAsync(); }
                catch (Exception error) { item["transaction_dispose_error"] = SafeError(error); }
            }
            if (connection is not null)
            {
                try { await connection.DisposeAsync(); }
                catch (Exception error) { item["connection_dispose_error"] = SafeError(error); }
            }
            if (createAttempted)
            {
                try
                {
                    await using var verify = await OpenVerified();
                    bool exists = await TableExists(verify, table);
                    item["table_existed_before_cleanup"] = exists;
                    if (exists)
                    {
                        // A fresh connection, after all transaction/context disposal. No auxiliary SELECT in a save chain.
                        var rows = new List<(long Id, string Name, int Version)>();
                        using (var command = verify.CreateCommand())
                        {
                            command.CommandTimeout = TimeoutSeconds;
                            command.CommandText = $"SELECT \"ID\", \"NAME\", \"VERSION\" FROM \"{table}\" ORDER BY \"ID\"";
                            await using var reader = await command.ExecuteReaderAsync();
                            while (await reader.ReadAsync()) rows.Add((Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetInt32(2)));
                        }
                        finalMatches = operation == "update"
                            ? rows.Count == 1 && rows[0].Id > 0 && rows[0].Name == (committed ? "changed" : "seed") && rows[0].Version == (committed ? 2 : 1)
                            : rows.Count == (committed ? 2 : 1) && rows.Count(r => r.Name == "seed" && r.Version == 1) == 1
                                && rows.Count(r => r.Name == "added" && r.Version == 1 && r.Id > 0) == (committed ? 1 : 0);
                        item["final_row_count"] = rows.Count;
                        await Exec(verify, $"DROP TABLE \"{table}\"");
                    }
                    cleanupConfirmed = !await TableExists(verify, table);
                }
                catch (Exception error) { item["verification_or_cleanup_error"] = SafeError(error); }
            }
        }
        item["committed"] = committed;
        item["final_state_matches"] = finalMatches;
        item["cleanup_confirmed"] = cleanupConfirmed;
        item["transaction_end_confirmed"] = transactionEnded;
        bool expectedObservation = workSucceeded && transactionEnded && item["success"] is true;
        if (!IsCandidate && isolation != IsolationLevel.ReadCommitted)
            expectedObservation |= knownCommandTextFailure && transactionEnded;
        bool cleanupErrors = item.Keys.Any(k => k.EndsWith("_error", StringComparison.Ordinal) && k != "error");
        item["accepted"] = expectedObservation && finalMatches && cleanupConfirmed && !cleanupErrors;
        return item;
    }

    private static DbTransaction Begin(DriverConnection connection, IsolationLevel isolation, string entry)
    {
        if (entry == "public") return connection.BeginTransaction(isolation);
        var method = connection.GetType().GetMethod("BeginProfileProbeTransaction", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(IsolationLevel)], null) ?? throw new ProbeFailure("profile_probe_seam_missing");
        return (DbTransaction)(method.Invoke(connection, [isolation]) ?? throw new ProbeFailure("profile_probe_returned_null"));
    }

    internal static async Task<DriverConnection> OpenVerified()
    {
        string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
            ?? throw new ProbeFailure("integration_pending");
#if LANE_W
        var builder = new W.Dm.DmConnectionStringBuilder(raw)
        {
            TransportSecurity = W.Dm.DmTransportSecurity.PlaintextAllowed,
            ConnectTimeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
        var connection = new DriverConnection(builder.ConnectionString);
#else
        var connection = new DriverConnection(raw);
#endif
        try
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds;
            command.CommandText = "SELECT CURRENT_USER FROM DUAL";
            string? user = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (!string.Equals(user, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
                throw new ProbeFailure("test_identity_mismatch");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExists(DbConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = TimeoutSeconds;
        command.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :name";
        var parameter = command.CreateParameter(); parameter.ParameterName = "name"; parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string HashFile(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException) yield return e;
    }

    internal static object SafeError(Exception error) => Chain(error).Select(e => new
    {
        type = e.GetType().FullName,
        probe_kind = e is ProbeFailure failure ? failure.Kind : null,
        number = Member(e, "Number") is int number ? (int?)number : null,
        methods = new StackTrace(e, false).GetFrames()?.Take(24).Select(f =>
            f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name).ToArray()
    }).ToArray();

    internal static object? Member(object? instance, string name)
    {
        if (instance is null) return null;
        try
        {
            for (Type? type = instance.GetType(); type is not null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var property = type.GetProperty(name, flags);
                if (property is not null && property.GetIndexParameters().Length == 0) return property.GetValue(instance);
                var field = type.GetField(name, flags);
                if (field is not null) return field.GetValue(instance);
            }
        }
        catch { /* Unavailable markers remain null, never surface exception text. */ }
        return null;
    }

    internal static object TransactionSnapshot(DbTransaction transaction) => new
    {
        outcome = Member(transaction, "Outcome")?.ToString(),
        valid = Member(transaction, "Valid") as bool?,
        supports_savepoints = transaction.SupportsSavepoints,
        connection_state = (Member(transaction, "Connection") as DbConnection)?.State.ToString()
    };
}

internal sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
internal sealed class ProbeRow { public long Id { get; set; } public string Name { get; set; } = ""; public int Version { get; set; } }
internal sealed class ProbeContext(DbContextOptions<ProbeContext> options, string table) : DbContext(options)
{
    internal string Table { get; } = table;
    internal DbSet<ProbeRow> Rows => Set<ProbeRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeRow>(entity =>
        {
            entity.ToTable(Table);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("ID").ValueGeneratedOnAdd();
            entity.Property(row => row.Name).HasColumnName("NAME").HasMaxLength(200).IsRequired();
            entity.Property(row => row.Version).HasColumnName("VERSION").IsConcurrencyToken();
        });
    }
}
internal sealed class ProbeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ProbeContext)context).Table, designTime);
}

internal sealed class CommandTrace : DbCommandInterceptor
{
    internal List<object> Events { get; } = [];
    private void Capture(DbCommand command, string phase, Exception? error = null)
    {
        object? statement = Program.Member(command, "m_Stmt");
        object? transactionStatement = Program.Member(command.Transaction, "Stmt");
        object? owner = statement is null ? null : FindCommandOwner(statement);
        string? ownerSql = (owner as DbCommand)?.CommandText;
        object? session = Program.Member(command.Connection, "session");
        Events.Add(new
        {
            phase, sql_length = command.CommandText.Length, sql_sha256 = Program.Hash(command.CommandText),
            parameter_count = command.Parameters.Count,
            has_statement = statement is not null,
            inherited_transaction_statement = statement is not null && ReferenceEquals(statement, transactionStatement),
            statement_owner_is_current = owner is null ? (bool?)null : ReferenceEquals(owner, command),
            owner_sql_length = ownerSql?.Length, owner_sql_sha256 = ownerSql is null ? null : Program.Hash(ownerSql),
            statement_prepared_marker = Program.Member(statement, "__t02_field_0400092C") as bool?,
            session_id = Program.Member(session, "SessionId") as long?,
            session_generation = Program.Member(session, "leaseGeneration") as long?,
            transaction_bound = command.Transaction is not null,
            connection_state = command.Connection?.State.ToString(), error = error is null ? null : Program.SafeError(error)
        });
    }

    private static object? FindCommandOwner(object statement)
    {
        for (Type? type = statement.GetType(); type is not null; type = type.BaseType)
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            if (typeof(DbCommand).IsAssignableFrom(field.FieldType)) return field.GetValue(statement);
        return null;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Capture(command, "reader_before"); return ValueTask.FromResult(result); }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { Capture(command, "reader_after"); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Capture(command, "nonquery_before"); return ValueTask.FromResult(result); }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { Capture(command, "nonquery_after"); return ValueTask.FromResult(result); }
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { Capture(command, "failed", eventData.Exception); return Task.CompletedTask; }
}
