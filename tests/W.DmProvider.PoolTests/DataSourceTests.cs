using System.Data;
using System.Data.Common;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class DataSourceTests
{
    [Fact]
    public void SafeDisplayNeverSuppliesCredentialsAndDataSourcesHaveIndependentOwners()
    {
        using var first = new DmDataSource(PoolApiSettings.Text);
        using var second = new DmDataSource(PoolApiSettings.Text);
        Assert.NotSame(first.Owner, second.Owner);
        Assert.DoesNotContain("CANARY_POOL", first.ConnectionString);
        using var connection = first.CreateConnection();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal("CANARY_POOL", connection.Settings.Password);
        Assert.DoesNotContain("CANARY_POOL", connection.ConnectionString);
        Assert.Throws<InvalidOperationException>(() => connection.ConnectionString = PoolApiSettings.Text.Replace("CANARY_POOL", "DIFFERENT"));
        Assert.Throws<InvalidOperationException>(() => connection.Schema = "OTHER");
        using var clone = connection.Clone();
        Assert.Same(first.Owner, clone.PoolOwner);
        using var factory = DmClientFactory.Instance.CreateDataSource(PoolApiSettings.Text);
        Assert.IsType<DmDataSource>(factory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DataSourceDisposeStopsNewAdmissionWhileExistingLeaseCanFinish(bool pooling)
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text.Replace("Pooling=true", "Pooling=" + pooling));
        using var first = await source.OpenConnectionAsync();
        using var closed = source.CreateConnection();
        await source.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => source.CreateConnection());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => closed.OpenAsync());
        Assert.Equal(ConnectionState.Open, first.State);
        Assert.False(hook.Channels[0].IsClosed);
        await first.CloseAsync();
        Assert.True(hook.Channels[0].IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task DisposeRejectsCreatingConnectionBeforePublishAndAbortsCandidate()
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        DmPendingOpenTestHooks.BeforePublish = _ => source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync());
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.True(hook.Channels.Single().IsClosed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task DisposeCancelsWaiterButDoesNotAbortPublishedLease()
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text);
        using var first = await source.OpenConnectionAsync();
        using var second = source.CreateConnection();
        Task pending = second.OpenAsync();
        Assert.Equal(1, source.Snapshot.Waiting);
        source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(ConnectionState.Open, first.State);
        Assert.Equal(1, source.Snapshot.Leased);
        await first.CloseAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutReaderOwnsConnectionUntilDisposeThenAllowsRepeatWithSameParameters()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var fake = new ShortcutCommand();
        await using var command = new DmDataSourceCommand(source, "SELECT :p FROM DUAL", fake);
        var parameter = new DmParameter("p", DmDbType.Int32) { Value = 8 };
        command.Parameters.Add(parameter);
        var reader = await command.ExecuteReaderAsync();
        Assert.Equal(1, source.Snapshot.Leased);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(8, await reader.GetFieldValueAsync<int>(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteScalarAsync());
        await reader.DisposeAsync();
        await reader.DisposeAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Same(parameter, command.Parameters[0]);
        parameter.Value = 9;
        Assert.Equal(9, await command.ExecuteScalarAsync());
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(0, fake.SyncCalls);
        Assert.Equal(2, fake.AsyncCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShortcutNonReaderPrepareAndErrorsReleaseEveryLease(bool asynchronous)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var fake = new ShortcutCommand();
        await using var command = new DmDataSourceCommand(source, "synthetic", fake);
        command.Parameters.Add(new DmParameter("p", DmDbType.Int32) { Value = 10 });
        if (asynchronous)
        {
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await command.PrepareAsync();
        }
        else { Assert.Equal(1, command.ExecuteNonQuery()); command.Prepare(); }
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        fake.Failure = new InvalidOperationException("synthetic execution failure");
        if (asynchronous) await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteScalarAsync());
        else Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(asynchronous ? 0 : 3, fake.SyncCalls);
        Assert.Equal(asynchronous ? 3 : 0, fake.AsyncCalls);
        Assert.All(hook.Channels, channel => Assert.True(channel.IsClosed));
    }

    [Fact]
    public async Task ShortcutCleanupCallbackFailureCannotReplaceTypedPrimaryFailure()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var primary = new DmTimeoutException(new DmFailureInfo(DmErrorKind.Timeout, DmFailurePhase.Receive,
            "WDM_TIMEOUT", DmOperationOutcome.Unknown, null, false, DmCancelSource.TotalDeadline));
        var fake = new ShortcutCommand { Failure = primary };
        await using var command = new DmDataSourceCommand(source, "synthetic", fake);
        DmPendingOpenTestHooks.AfterCapacityAcquired = connection => connection.StateChange += (_, change) =>
        { if (change.CurrentState == ConnectionState.Closed) throw new InvalidOperationException("synthetic cleanup failure"); };
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => command.ExecuteNonQueryAsync());
        Assert.Same(primary, error);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutDisposeBeforeAuthenticationPreventsNewHandshakeAndReturnsPermit()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        DmPendingOpenTestHooks.AfterCapacityAcquired = _ => command.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteScalarAsync());
        Assert.Empty(hook.Channels);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutDisposeBeforeReaderPublicationClosesUnpublishedReaderAndConnection()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var fake = new ShortcutCommand();
        using var command = new DmDataSourceCommand(source, "synthetic", fake);
        fake.BeforeReaderReturn = () => command.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => command.ExecuteReaderAsync());
        Assert.NotNull(fake.LastReader);
        Assert.True(fake.LastReader!.IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutPreCanceledInvocationDoesNotAcquireConnectionOrMutateParameters()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("synthetic");
        var parameter = new DmParameter("p", DmDbType.Int32) { Value = 42 };
        command.Parameters.Add(parameter);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteNonQueryAsync(caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Same(parameter, command.Parameters[0]);
        Assert.Empty(hook.Channels);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ActualShortcutPrepareUsesAsyncOnlyChannelAndCanceledFailureReturnsPermit()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        using var caller = new CancellationTokenSource();
        var channel = new AsyncOnlyShortcutChannel();
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) => { hook.Install(candidate, channel); return ValueTask.CompletedTask; };
        DmTransportTestHooks.BeforeNetworkIo = (asynchronous, _) => Assert.True(asynchronous);
        try
        {
            Task call = command.PrepareAsync(caller.Token);
            await channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel();
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
            Assert.Equal(0, channel.SyncCalls);
            Assert.True(channel.AsyncSends > 0);
            Assert.True(channel.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
        }
        finally { DmTransportTestHooks.BeforeNetworkIo = null; }
    }

    private sealed class AsyncOnlyShortcutChannel : IDmByteChannel
    {
        internal readonly TaskCompletionSource ReceiveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SyncCalls, AsyncSends;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync send forbidden."); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync receive forbidden."); }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); AsyncSends++; return ValueTask.FromResult(count); }
        public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { ReceiveEntered.TrySetResult(); await aborted.Task.WaitAsync(token); return 0; }
        public void Dispose() { IsClosed = true; aborted.TrySetResult(); }
    }

    private sealed class ShortcutCommand : DmCommand
    {
        internal int SyncCalls, AsyncCalls;
        internal Exception? Failure;
        internal Action? BeforeReaderReturn;
        internal DbDataReader? LastReader;
        private object Value => Parameters.Count == 0 ? 1 : Parameters[0].Value;
        private void Check() { if (Failure != null) throw Failure; }
        public override int ExecuteNonQuery() { SyncCalls++; Check(); return 1; }
        public override object ExecuteScalar() { SyncCalls++; Check(); return Value; }
        public override void Prepare() { SyncCalls++; Check(); }
        public override Task<int> ExecuteNonQueryAsync(CancellationToken token) { AsyncCalls++; Check(); return Task.FromResult(1); }
        public override Task<object> ExecuteScalarAsync(CancellationToken token) { AsyncCalls++; Check(); return Task.FromResult(Value); }
        public override Task PrepareAsync(CancellationToken token = default) { AsyncCalls++; Check(); return Task.CompletedTask; }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        { SyncCalls++; Check(); return Reader(); }
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        { AsyncCalls++; Check(); return Task.FromResult(Reader()); }
        private DbDataReader Reader()
        {
            var table = new DataTable();
            table.Columns.Add("VALUE", typeof(int));
            table.Rows.Add(Value);
            LastReader = table.CreateDataReader();
            BeforeReaderReturn?.Invoke();
            return LastReader;
        }
    }
}
