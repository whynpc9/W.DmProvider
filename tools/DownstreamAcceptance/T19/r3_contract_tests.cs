using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using W.Dm;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

// Separate R3 lane: caller-owned DataSource connections, no alteration of historical full-functional tests.
[Trait("Feature", "T19DownstreamR3")]
public sealed class R3DriverContractTests
{
    private const string Owner = "WDM_PROVIDER_TEST";
    private const int Seconds = 20;

    [Fact, Trait("Category", "R3Shared")]
    public Task SharedPooledAsyncCrudGeneratedIdentityConcurrencyAndFortyKiBLobs()
        => WithTable("shared", "shared_crud_lob_concurrency", Crud);

    [Fact, Trait("Category", "R3Tls")]
    public Task TlsPooledAsyncCrudAndFortyKiBLobsUseSupportedSubset()
        => WithTable("tls", "tls_crud_lob_concurrency_supported_subset", Crud);

    [Fact, Trait("Category", "R3Shared")]
    public Task SharedExplicitEfTransactionUsesAutomaticAndExplicitSavepoints()
        => WithTable("shared", "shared_transaction_savepoint", async store =>
        {
            store.Stage = "explicit_transaction";
            long id;
            await using (var connection = await store.Open())
            await using (var context = store.Context(connection))
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                Assert.True(context.Database.AutoSavepointsEnabled);
                Assert.True(transaction.SupportsSavepoints);
                var row = NewRow("before_savepoint"); context.Rows.Add(row);
                Assert.Equal(1, await context.SaveChangesAsync()); id = row.Id; Assert.True(id > 0);
                await transaction.CreateSavepointAsync("T19_POINT");
                row.Name = "rolled_back_update"; row.Version = 2;
                Assert.Equal(1, await context.SaveChangesAsync());
                await transaction.RollbackToSavepointAsync("T19_POINT");
                context.ChangeTracker.Clear();
                var restored = await context.Rows.SingleAsync(item => item.Id == id);
                Assert.True(restored.Name == "before_savepoint"); Assert.Equal(1, restored.Version);
                await transaction.ReleaseSavepointAsync("T19_POINT");
                await transaction.CommitAsync();
            }
            store.Stage = "fresh_committed_state";
            await using (var fresh = await store.Open())
            await using (var context = store.Context(fresh)) Assert.Equal(1, await context.Rows.CountAsync(item => item.Id == id));
            store.Stage = "explicit_rollback";
            await using (var connection = await store.Open())
            await using (var context = store.Context(connection))
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.Rows.Add(NewRow("rolled_back_insert"));
                Assert.Equal(1, await context.SaveChangesAsync());
                await transaction.RollbackAsync();
            }
            await using (var fresh = await store.Open())
            await using (var context = store.Context(fresh)) Assert.Equal(1, await context.Rows.CountAsync());
            store.AssertAsyncEfCommands();
        });

    [Fact, Trait("Category", "R3Tls")]
    public Task TlsSavepointCapabilityRefusesBeforeChangingTheTransaction()
        => WithTable("tls", "tls_savepoint_explicitly_unsupported", async store =>
        {
            store.Stage = "unsupported_savepoint";
            await using (var connection = await store.Open())
            await using (var transaction = (DmTransaction)await connection.BeginTransactionAsync())
            {
                Assert.False(transaction.SupportsSavepoints);
                using (var sends = new SendAttemptObservation())
                {
                    int before = sends.Count;
                    await Assert.ThrowsAsync<NotSupportedException>(() => transaction.SaveAsync("T19_UNSUPPORTED"));
                    Assert.Equal(before, sends.Count);
                    store.Results["unsupported_savepoint_send_attempts"] = sends.Count - before;
                }
                Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
                await transaction.RollbackAsync();
                Assert.Equal(DmTransactionOutcome.RolledBack, transaction.Outcome);
            }
            await using (var fresh = await store.Open())
            await using (var context = store.Context(fresh)) Assert.Equal(0, await context.Rows.CountAsync());
            store.AssertAsyncEfCommands();
        });

    [Fact, Trait("Category", "R3Shared")]
    public Task IndependentDataSourcesKeepOwnershipAndDoNotCloseTheirActiveConnectionOnDispose()
        => WithTable("shared", "shared_datasource_ownership", async store =>
        {
            store.Stage = "held_source_capacity";
            await using var first = await store.Open();
            using var waiterCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Seconds));
            Task<DmConnection> waiting = store.Source.OpenConnectionAsync(waiterCancellation.Token).AsTask();
            Assert.False(waiting.IsCompleted);
            await using (var independent = new DmDataSource(store.Settings))
            await using (var second = await store.Open(independent))
            {
                Assert.Equal(ConnectionState.Open, second.State);
                Assert.False(waiting.IsCompleted);
            }
            store.Source.ClearPool();
            Assert.False(waiting.IsCompleted);
            store.Source.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(Seconds)));
            Assert.Equal(ConnectionState.Open, first.State);
            await using (var command = new DmCommand("SELECT COUNT(*) FROM " + store.QualifiedTable, first) { CommandTimeout = Seconds })
                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
            await first.CloseAsync();
            Assert.Equal(ConnectionState.Closed, first.State);
            Assert.Throws<ObjectDisposedException>(() => store.Source.CreateConnection());
            store.Results["independent_owners_verified"] = true;
            store.Results["disposed_source_active_lease_closed_by_caller"] = true;
        });

    private static async Task Crud(Store store)
    {
        long id; byte[] binary = BinaryPayload(); string text = TextPayload();
        store.Stage = "crud_insert_identity";
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection))
        {
            store.Stage = "crud_insert_before_auto_savepoint_check";
            Assert.True(context.Database.AutoSavepointsEnabled);
            var row = NewRow("initial"); row.Binary = binary; row.Text = text;
            store.Stage = "crud_insert_before_add";
            context.Rows.Add(row);
            store.ObserveKey(context, row, "before_save");
            store.Stage = "crud_insert_before_save";
            int affected = await context.SaveChangesAsync();
            store.Stage = "crud_insert_after_save";
            store.Results["insert_affected_count"] = affected;
            store.ObserveKey(context, row, "after_save");
            Assert.Equal(1, affected);
            id = row.Id; store.Results["generated_id_positive"] = id > 0;
            Assert.True(id > 0);
            store.Stage = "crud_insert_after_identity_check";
        }
        store.Stage = "crud_read_40k_lobs";
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection))
        {
            var row = await context.Rows.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.True(row.Name == "initial"); Assert.Equal(1, row.Version);
            Assert.Equal(Digest(binary), Digest(row.Binary)); Assert.Equal(DigestText(text), DigestText(row.Text));
            Assert.Equal(40 * 1024, row.Binary.Length); Assert.True(row.Text.Length >= 40 * 1024);
        }
        store.Stage = "crud_update_one_concurrency";
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection))
        {
            var row = await context.Rows.SingleAsync(item => item.Id == id);
            row.Name = "updated"; row.Version = 2; Assert.Equal(1, await context.SaveChangesAsync());
        }
        store.Stage = "crud_zero_concurrency";
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection))
        {
            var stale = NewRow("stale"); stale.Id = id;
            context.Rows.Attach(stale);
            context.Entry(stale).Property(item => item.Name).IsModified = true;
            context.Entry(stale).Property(item => item.Version).OriginalValue = 1;
            context.Entry(stale).Property(item => item.Version).CurrentValue = 3;
            context.Entry(stale).Property(item => item.Version).IsModified = true;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        }
        store.Stage = "fresh_state_after_concurrency";
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection))
        {
            var row = await context.Rows.SingleAsync(item => item.Id == id);
            Assert.True(row.Name == "updated"); Assert.Equal(2, row.Version);
            Assert.Equal(Digest(binary), Digest(row.Binary)); Assert.Equal(DigestText(text), DigestText(row.Text));
            context.Rows.Remove(row); Assert.Equal(1, await context.SaveChangesAsync());
        }
        await using (var connection = await store.Open())
        await using (var context = store.Context(connection)) Assert.Equal(0, await context.Rows.CountAsync());
        store.Results["generated_id_positive"] = true;
        store.Results["concurrency_one_then_zero_verified"] = true;
        store.Results["blob_bytes"] = binary.Length; store.Results["clob_utf16_units"] = text.Length;
        store.AssertAsyncEfCommands();
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string DigestText(string text) => Digest(new UTF8Encoding(false, true).GetBytes(text));
    private static Entity NewRow(string name) => new() { Name = name, Version = 1, Binary = [17], Text = "T19" };
    private static byte[] BinaryPayload()
    {
        byte[] bytes = new byte[40 * 1024];
        for (int index = 0; index < bytes.Length; index++) bytes[index] = (byte)((index * 131 + 17) & 255);
        return bytes;
    }
    private static string TextPayload() => "A中🙂\0e\u0301Z" + new string('x', 40 * 1024);

    private static async Task WithTable(string profile, string caseName, Func<Store, Task> work)
    {
        Store store;
        try
        {
            Need(global::T19ConnectionSettings.Profile == profile, "PROFILE_REQUIRED");
            string raw = DamengTestEnvironment.ConnectionString ?? throw new R3Failure("TEST_CONNECTION_REQUIRED");
            var builder = new DmConnectionStringBuilder(global::T19ConnectionSettings.Configure(raw))
            { Pooling = true, MaxPoolSize = 1, MaxPoolWaiters = 8, PoolAcquireTimeout = TimeSpan.FromSeconds(Seconds), CommandTimeout = Seconds };
            Need(builder.User == Owner && builder.Schema == Owner, "TEST_SCOPE_REQUIRED");
            store = new Store(builder.ConnectionString, profile, caseName);
        }
        catch (R3Failure) { throw; }
        catch { throw new R3Failure("CONFIGURATION_FAILED"); }
        Exception? primary = null, cleanup = null;
        bool evidenceWritten = false;
        try { await store.Create(); await work(store); store.Results["work_verified"] = true; }
        catch (Exception failure)
        {
            primary = failure; store.Results["failed_stage"] = store.Stage; store.Results["work_failure"] = SafeFailure(failure);
            store.Results["work_failure_command_counts"] = store.CommandSnapshot();
            store.Results["work_failure_last_verified_client"] = store.ClientSnapshot();
        }
        finally
        {
            try { await store.Cleanup(); }
            catch (Exception failure) { cleanup = failure; store.Results["cleanup_failure"] = SafeFailure(failure); }
            try { await store.DisposeAsync(); }
            catch (Exception failure) { cleanup ??= failure; store.Results["source_close_failure"] = SafeFailure(failure); }
            store.Results["accepted"] = primary == null && cleanup == null;
            try { store.WriteEvidence(); evidenceWritten = true; }
            catch { store.Results["accepted"] = false; store.Results["evidence_write_failed"] = true; }
        }
        if (primary != null) throw new R3Failure("WORK_FAILED");
        if (cleanup != null) throw new R3Failure("CLEANUP_UNCONFIRMED");
        if (!evidenceWritten) throw new R3Failure("EVIDENCE_WRITE_FAILED");
    }

    private sealed class Store : IAsyncDisposable
    {
        internal readonly string Settings, Table, Profile, CaseName;
        internal string QualifiedTable => "\"" + Owner + "\".\"" + Table + "\"";
        internal readonly DmDataSource Source;
        internal readonly CommandCounts Counts = new();
        internal readonly Dictionary<string, object?> Results = new();
        internal string Stage = "create_unique_table";
        private bool attempted;
        private int identities;
        private DmConnection? lastVerifiedConnection;
        private object? lastVerifiedSession;
        private readonly string evidenceDirectory;
        internal Store(string settings, string profile, string caseName)
        {
            Settings = settings; Profile = profile; CaseName = caseName; Table = "T19R3_" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
            evidenceDirectory = Environment.GetEnvironmentVariable("DAMENG_T19_R3_EVIDENCE_DIR") ?? throw new R3Failure("EVIDENCE_DIRECTORY_REQUIRED");
            Need(Directory.Exists(evidenceDirectory), "EVIDENCE_DIRECTORY_REQUIRED");
            if (!OperatingSystem.IsWindows())
                Need(new DirectoryInfo(evidenceDirectory).LinkTarget == null &&
                    File.GetUnixFileMode(evidenceDirectory) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute),
                    "PRIVATE_EVIDENCE_DIRECTORY_REQUIRED");
            Source = new DmDataSource(settings);
            Results["schema_version"] = 1; Results["case"] = caseName; Results["profile"] = profile;
            Results["owned_table"] = Table; Results["pooling"] = true; Results["owner_kind"] = "explicit_datasource";
            Results["automatic_savepoints_disabled"] = false; Results["final_database_state"] = "unverified";
            Results["savepoint_scope"] = profile == "shared" ? "shared_supported" : "tls_supported_subset_without_savepoint_claim";
        }
        internal async Task<DmConnection> Open(DmDataSource? source = null)
        {
            var connection = await (source ?? Source).OpenConnectionAsync();
            try
            {
                Need(connection.ServerVersion == (Profile == "shared" ? "8.1.5.60" : "8.1.4.6"), "SERVER_PROFILE_REQUIRED");
                await using var command = new DmCommand("SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL", connection) { CommandTimeout = Seconds };
                await using var reader = await command.ExecuteReaderAsync();
                Need(await reader.ReadAsync() && reader.GetString(0) == Owner && reader.GetString(1) == Owner && !await reader.ReadAsync(), "ACTUAL_TEST_IDENTITY_REQUIRED");
                identities++;
                lastVerifiedConnection = connection;
                try { lastVerifiedSession = typeof(DmConnection).GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(connection); }
                catch { lastVerifiedSession = null; }
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }
        internal async Task Create()
        {
            await using var connection = await Open();
            Need(await ObjectCount(connection) == 0, "UNIQUE_OBJECT_REQUIRED");
            WritePendingOwnership();
            attempted = true;
            await Execute(connection, $"CREATE TABLE {QualifiedTable} (\"ID\" BIGINT IDENTITY(1,1) NOT NULL,\"NAME\" NVARCHAR2(200) NOT NULL,\"VERSION\" INT NOT NULL,\"B\" BLOB NULL,\"C\" CLOB NULL,PRIMARY KEY(\"ID\"))");
            Results["create_verified"] = true;
        }
        internal R3Context Context(DmConnection connection) => new(connection, Table, Counts);
        internal void AssertAsyncEfCommands()
        {
            Assert.True(Counts.Async > 0); Assert.Equal(0, Counts.Sync); Assert.True(Counts.AllCandidateCommands);
            Results["ef_async_commands"] = Counts.Async; Results["ef_sync_commands"] = Counts.Sync;
            Results["ef_command_categories"] = Counts.Categories; Results["ef_candidate_command_types_only"] = true;
        }
        internal void ObserveKey(R3Context context, Entity entity, string checkpoint)
        {
            try
            {
                var key = context.Entry(entity).Property(item => item.Id);
                var property = key.Metadata;
                Results["key_" + checkpoint] = new
                {
                    value_generated = (int)property.ValueGenerated, is_temporary = key.IsTemporary,
                    generation_strategy = (int)property.GetDamengValueGenerationStrategy(StoreObjectIdentifier.Table(Table, Owner)),
                    before_save_behavior = (int)property.GetBeforeSaveBehavior(), after_save_behavior = (int)property.GetAfterSaveBehavior(),
                    readwrite_save_behavior = property.GetBeforeSaveBehavior() == PropertySaveBehavior.Save &&
                        property.GetAfterSaveBehavior() == PropertySaveBehavior.Save,
                    id_positive = entity.Id > 0
                };
            }
            catch { Results["key_" + checkpoint] = new { availability = "unavailable" }; }
        }
        internal object CommandSnapshot() => new
        {
            asynchronous = Counts.Async, synchronous = Counts.Sync, candidate_types_only = Counts.AllCandidateCommands,
            categories = new Dictionary<int, int>(Counts.Categories)
        };
        internal object ClientSnapshot()
        {
            try
            {
                object? session = lastVerifiedSession;
                if (session == null || session.GetType().Assembly != typeof(DmConnection).Assembly)
                    return new { availability = "unavailable" };
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                object? state = session.GetType().GetProperty("State", flags)?.GetValue(session);
                object? transactionState = session.GetType().GetProperty("TransactionState", flags)?.GetValue(session);
                object? active = session.GetType().GetProperty("ActiveTransaction", flags)?.GetValue(session);
                return new
                {
                    scope = "last_verified_client_after_work_unwind_not_server_transaction_proof",
                    connection_state = lastVerifiedConnection == null ? (int?)null : (int)lastVerifiedConnection.State,
                    session_state = PackageEnumNumber(state, "W.Dm.Internal.Sessions.DmPhysicalSessionState"),
                    transaction_state = PackageEnumNumber(transactionState, "W.Dm.Internal.Sessions.DmLocalTransactionState"),
                    active_transaction_outcome = active is DmTransaction transaction ? (int?)transaction.Outcome : null
                };
            }
            catch { return new { availability = "unavailable" }; }
        }
        internal async Task Cleanup()
        {
            // Fresh independent TEST source; even a test which disposed its source must be cleaned safely.
            await using var freshSource = new DmDataSource(Settings);
            await using (var connection = await Open(freshSource))
            {
                int count = await ObjectCount(connection); Need(count is 0 or 1, "OWNED_OBJECT_SHAPE_REQUIRED");
                if (attempted && count == 1) await Execute(connection, "DROP TABLE " + QualifiedTable);
            }
            await using (var final = await Open(freshSource)) Need(await ObjectCount(final) == 0, "OWNED_OBJECT_ABSENCE_REQUIRED");
            Results["cleanup_verified"] = true; Results["final_database_state"] = "exact_owned_table_absent";
            Results["fresh_test_identity_checks"] = identities;
        }
        private async Task<int> ObjectCount(DmConnection connection)
        {
            await using var command = new DmCommand("SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME=:name AND OBJECT_TYPE='TABLE'", connection) { CommandTimeout = Seconds };
            command.Parameters.Add(new DmParameter("name", Table));
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        private static async Task Execute(DmConnection connection, string sql)
        { await using var command = new DmCommand(sql, connection) { CommandTimeout = Seconds }; await command.ExecuteNonQueryAsync(); }
        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            GC.SuppressFinalize(this);
        }
        private void WritePendingOwnership()
        {
            string path = Path.Combine(evidenceDirectory, CaseName + "." + Table + ".owned.json");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(file, new
            {
                schema_version = 1, kind = "pending_exact_test_object_ownership", owner_schema = Owner,
                owned_table = Table, object_type = "TABLE", profile = Profile, @case = CaseName,
                fresh_test_identity_verified = true, observed_fresh_object_count = 0,
                planned_operation = "create_exact_test_table", created_confirmed = false,
                accepted = false, status = "pending_create", utc = DateTimeOffset.UtcNow
            });
            file.Flush(flushToDisk: true);
            Results["pending_ownership_ledger_written_before_create"] = true;
        }
        internal void WriteEvidence()
        {
            string path = Path.Combine(evidenceDirectory, CaseName + "." + Table + ".json");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(file, Results);
        }
    }

    private sealed class Entity
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Version { get; set; }
        public byte[] Binary { get; set; } = [];
        public string Text { get; set; } = "";
    }
    private sealed class R3Context : DbContext
    {
        internal readonly string Table;
        internal DbSet<Entity> Rows => Set<Entity>();
        internal R3Context(DmConnection connection, string table, CommandCounts counters)
            : base(new DbContextOptionsBuilder<R3Context>().UseDameng(connection, contextOwnsConnection: false)
                .ReplaceService<IModelCacheKeyFactory, R3ModelCacheKey>().AddInterceptors(counters).Options) => Table = table;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var entity = model.Entity<Entity>(); entity.ToTable(Table, Owner); entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("ID").ValueGeneratedOnAdd();
            entity.Property(item => item.Name).HasColumnName("NAME").HasMaxLength(200).IsRequired();
            entity.Property(item => item.Version).HasColumnName("VERSION").IsConcurrencyToken();
            entity.Property(item => item.Binary).HasColumnName("B").HasColumnType("BLOB");
            entity.Property(item => item.Text).HasColumnName("C").HasColumnType("CLOB");
        }
    }
    public sealed class R3ModelCacheKey : IModelCacheKeyFactory
    { public object Create(DbContext context, bool designTime) => context is R3Context typed ? (context.GetType(), typed.Table, designTime) : (object)(context.GetType(), designTime); }
    private sealed class CommandCounts : DbCommandInterceptor
    {
        internal int Async, Sync; internal bool AllCandidateCommands = true;
        internal readonly Dictionary<int, int> Categories = new();
        private void Record(DbCommand command, CommandEventData data, bool asynchronous)
        { if (asynchronous) Async++; else Sync++; AllCandidateCommands &= command is DmCommand; int category = (int)data.CommandSource; Categories[category] = Categories.GetValueOrDefault(category) + 1; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Record(command, data, false); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Record(command, data, true); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Record(command, data, false); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Record(command, data, true); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
        { Record(command, data, false); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
        { Record(command, data, true); return ValueTask.FromResult(result); }
    }
    private sealed class SendAttemptObservation : IDisposable
    {
        private readonly FieldInfo field;
        private int count;
        internal int Count => Volatile.Read(ref count);
        internal SendAttemptObservation()
        {
            Type hooks = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")
                ?? throw new R3Failure("SEND_COUNTER_HOOK_REQUIRED");
            field = hooks.GetField("BeforeSendAttempt", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new R3Failure("SEND_COUNTER_HOOK_REQUIRED");
            Need(field.GetValue(null) == null, "ISOLATED_SEND_COUNTER_REQUIRED");
            var arguments = field.FieldType.GenericTypeArguments.Select(type => Expression.Parameter(type)).ToArray();
            Action observe = () => Interlocked.Increment(ref count);
            var callback = Expression.Lambda(field.FieldType, Expression.Invoke(Expression.Constant(observe)), arguments).Compile();
            field.SetValue(null, callback);
        }
        public void Dispose() => field.SetValue(null, null);
    }

    private static object SafeFailure(Exception failure)
    {
        var causes = new List<object>(); var frames = new List<object>();
        int? number = null; int depth = 0; bool framesTruncated = false;
        Exception? current = failure;
        for (; current != null && depth < 4; current = current.InnerException, depth++)
        {
            string knownType = KnownExceptionType(current);
            DmFailureInfo? info = knownType == "Unknown" ? null :
                (current as DmException)?.FailureInfo ?? (current as DmOperationCanceledException)?.FailureInfo;
            int? causeNumber = knownType != "Unknown" && current is DmException dm ? dm.Number : null;
            number ??= causeNumber;
            causes.Add(new
            {
                depth, type = knownType, hresult = current.HResult, number = causeNumber,
                failure_kind = info == null ? (int?)null : (int)info.ErrorKind,
                failure_phase = info == null ? (int?)null : (int)info.Phase,
                operation_outcome = info == null ? (int?)null : (int)info.OperationOutcome,
                transaction_outcome = info?.TransactionOutcome is { } outcome ? (int?)outcome : null,
                cancel_source = info == null ? (int?)null : (int)info.CancelSource,
                connection_reusable = info?.ConnectionReusable, server_number = info?.ServerErrorNumber,
                driver_code = info == null ? "Unavailable" : KnownFailureCode(info.ErrorCode)
            });
            int causeFrames = 0;
            foreach (var frame in new StackTrace(current, false).GetFrames().Take(64))
            {
                MethodBase? method = frame.GetMethod(); Type? type = method?.DeclaringType;
                if (type == null || type.Assembly.IsDynamic) continue;
                string? assembly = TrustedAssemblyCode(type.Assembly);
                if (assembly == null) continue;
                if (causeFrames >= 4) { framesTruncated = true; break; }
                if (type.IsConstructedGenericType) type = type.GetGenericTypeDefinition();
                // Names originate only in pinned static package/framework metadata, never signatures or file paths.
                frames.Add(new { cause_depth = depth, assembly, declaring_type = type.FullName ?? type.Name, method = method!.Name });
                causeFrames++;
            }
        }
        return new
        {
            classification = failure is DbUpdateConcurrencyException ? "concurrency" : failure is OperationCanceledException ? "canceled" : "failure",
            hresult = failure.HResult, server_number = number, causes, causes_truncated = current != null,
            trusted_frames = frames, trusted_frames_truncated = framesTruncated
        };
    }
    private static string KnownExceptionType(Exception error)
    {
        Type type = error.GetType();
        if (type == typeof(Exception)) return "System.Exception";
        if (type == typeof(InvalidOperationException)) return "System.InvalidOperationException";
        if (type == typeof(NotSupportedException)) return "System.NotSupportedException";
        if (type == typeof(ArgumentException)) return "System.ArgumentException";
        if (type == typeof(ArgumentNullException)) return "System.ArgumentNullException";
        if (type == typeof(ArgumentOutOfRangeException)) return "System.ArgumentOutOfRangeException";
        if (type == typeof(TimeoutException)) return "System.TimeoutException";
        if (type == typeof(OperationCanceledException)) return "System.OperationCanceledException";
        if (type == typeof(TargetInvocationException)) return "System.Reflection.TargetInvocationException";
        if (type == typeof(DbUpdateException)) return "Microsoft.EntityFrameworkCore.DbUpdateException";
        if (type == typeof(DbUpdateConcurrencyException)) return "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";
        if (type == typeof(DmException)) return "W.Dm.DmException";
        if (type == typeof(DmTimeoutException)) return "W.Dm.DmTimeoutException";
        if (type == typeof(DmOperationCanceledException)) return "W.Dm.DmOperationCanceledException";
        if (type == typeof(DmCommitOutcomeUnknownException)) return "W.Dm.DmCommitOutcomeUnknownException";
        if (type == typeof(R3Failure)) return "T19.FixedCodeFailure";
        if (type.Assembly == typeof(Assert).Assembly && type.FullName is
            "Xunit.Sdk.TrueException" or "Xunit.Sdk.FalseException" or "Xunit.Sdk.EqualException" or
            "Xunit.Sdk.ThrowsException" or "Xunit.Sdk.SingleException" or "Xunit.Sdk.EmptyException" or "Xunit.Sdk.IsTypeException")
            return type.FullName!;
        return "Unknown";
    }
    private static string KnownFailureCode(string code) => code switch
    {
        "WDM_TIMEOUT" => "WDM_TIMEOUT", "WDM_CANCELED" => "WDM_CANCELED", "WDM_TRANSPORT" => "WDM_TRANSPORT",
        "WDM_SERVER" => "WDM_SERVER", "WDM_COMMIT_UNKNOWN" => "WDM_COMMIT_UNKNOWN",
        "WDM_POOL_WAIT_CANCELED" => "WDM_POOL_WAIT_CANCELED", "WDM_POOL_WAIT_TIMEOUT" => "WDM_POOL_WAIT_TIMEOUT",
        "WDM_POOL_WAIT_QUEUE_FULL" => "WDM_POOL_WAIT_QUEUE_FULL", _ => "Unknown"
    };
    private static string? TrustedAssemblyCode(Assembly assembly)
    {
        if (assembly == typeof(DmConnection).Assembly) return "candidate_driver";
        if (assembly == typeof(DbContext).Assembly) return "ef_core";
        if (assembly == typeof(RelationalDatabaseFacadeExtensions).Assembly) return "ef_relational";
        if (assembly == typeof(DamengDbContextOptionsBuilder).Assembly) return "pinned_ef_provider";
        if (assembly == typeof(Assert).Assembly) return "xunit_assert_framework";
        if (assembly == typeof(Exception).Assembly) return "runtime_framework";
        if (assembly == typeof(DbCommand).Assembly) return "data_framework";
        return null;
    }
    private static int? PackageEnumNumber(object? value, string expectedType)
        => value != null && value.GetType().Assembly == typeof(DmConnection).Assembly &&
            value.GetType().IsEnum && value.GetType().FullName == expectedType
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;

    private static void Need(bool condition, string code) { if (!condition) throw new R3Failure(code); }
    private sealed class R3Failure(string code) : InvalidOperationException("T19_R3_" + code);
}
