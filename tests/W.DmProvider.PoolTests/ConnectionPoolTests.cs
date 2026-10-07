using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[CollectionDefinition("Pool API hooks", DisableParallelization = true)]
public sealed class PoolApiCollection { }

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class ConnectionPoolTests
{
    [Fact]
    public async Task DirectEquivalentSettingsAndClearAllKeepOneCapacityDomain()
    {
        using var hook = new SyntheticPoolHandshake();
        string raw = PoolApiSettings.Text.Replace("POOL_SYNTH", "SYNTH_" + Guid.NewGuid().ToString("N"));
        using var first = new DmConnection(raw);
        using var second = new DmConnection(new DmConnectionStringBuilder(raw).ToSettings().ToConnectionString(true));
        await first.OpenAsync();
        Task waiting = second.OpenAsync();
        Assert.Equal(1, first.PoolOwner.Snapshot.Waiting);
        DmConnection.ClearPool(first);
        DmConnection.ClearAllPools();
        Assert.Equal(1, first.PoolOwner.Snapshot.PhysicalCount);
        Assert.False(waiting.IsCompleted);
        await first.CloseAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first.PoolOwner, second.PoolOwner);
        await second.CloseAsync();
        Assert.Equal(0, second.PoolOwner.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task MaxOneWaitCancellationPreservesOriginalTokenAndReturnsCapacity()
    {
        using var hook = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var first = await source.OpenConnectionAsync();
        await using var second = source.CreateConnection();
        using var caller = new CancellationTokenSource();
        Task waiting = second.OpenAsync(caller.Token);
        Assert.Equal(1, source.Snapshot.Waiting);
        caller.Cancel();
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => waiting);
        Assert.Equal(caller.Token, failure.CancellationToken);
        Assert.Equal(DmFailurePhase.PoolWait, failure.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
        Assert.Equal(ConnectionState.Closed, second.State);
        Assert.Equal(1, source.Snapshot.Leased);
        await first.CloseAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        await second.OpenAsync();
        await second.CloseAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(2, hook.Channels.Count);
        Assert.All(hook.Channels, c => Assert.True(c.IsClosed));
    }

    [Fact]
    public async Task CloseWhileWaitingCancelsOnlyOldPendingAndImmediateReopenIsSafe()
    {
        using var hook = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var held = await source.OpenConnectionAsync();
        await using var connection = source.CreateConnection();
        Task old = connection.OpenAsync();
        await connection.CloseAsync();
        Task replacement = connection.OpenAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => old);
        Assert.Equal(ConnectionState.Connecting, connection.State);
        await held.CloseAsync();
        await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ConnectionState.Open, connection.State);
        await connection.CloseAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task OldHandshakeCompletionCannotOverwriteAReopenedConnection()
    {
        using var hook = new SyntheticPoolHandshake();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = async (candidate, _, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            { entered.TrySetResult(); await release.Task; }
            hook.Install(candidate);
        };
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var connection = source.CreateConnection();
        Task old = connection.OpenAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DmSession oldSession = connection.Session;
        await connection.CloseAsync();
        Task replacement = connection.OpenAsync();
        Assert.Equal(1, source.Snapshot.Creating);
        Assert.Equal(1, source.Snapshot.Waiting);
        release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => old);
        await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotSame(oldSession, connection.Session);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, source.Snapshot.Leased);
    }

    [Fact]
    public async Task CloseAbortBarrierKeepsPermitUntilTransportIsClosed()
    {
        using var hook = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var first = await source.OpenConnectionAsync();
        await using var next = source.CreateConnection();
        Task waiting = next.OpenAsync();
        bool seen = false;
        DmSessionTestHooks.BeforeConnectionTransportAbort = _ =>
        {
            seen = true;
            Assert.Equal(1, source.Snapshot.Closing);
            Assert.False(waiting.IsCompleted);
            Assert.False(hook.Channels[0].IsClosed);
        };
        try { await first.CloseAsync(); }
        finally { DmSessionTestHooks.BeforeConnectionTransportAbort = null; }
        Assert.True(seen);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(hook.Channels[0].IsClosed);
        Assert.Equal(1, source.Snapshot.Leased);
    }

    [Fact]
    public async Task OpenNotificationCanCloseAndReopenWithoutOldStateWrites()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        int opens = 0;
        connection.StateChange += (_, change) =>
        {
            if (change.CurrentState == ConnectionState.Open && ++opens == 1)
            { connection.Close(); source.ClearPool(); connection.Open(); }
        };
        await connection.OpenAsync();
        Assert.Equal(2, opens);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.True(hook.Channels[0].IsClosed);
        Assert.Equal(1, source.Snapshot.Leased);
    }

    [Fact]
    public async Task ThrowingOpenCallbackAbortsAndReturnsExactlyOnePermit()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        connection.StateChange += (_, change) =>
        { if (change.CurrentState == ConnectionState.Open) throw new InvalidOperationException("synthetic callback"); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.True(hook.Channels.Single().IsClosed);
    }

    [Fact]
    public async Task PendingBrokenNotificationWaitsUntilCleanupSoCallbackCanSynchronouslyReopen()
    {
        using var hook = new SyntheticPoolHandshake();
        int calls = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate);
            if (++calls == 1)
            {
                candidate.Session.Detach(DmInvocation.Current.Identity)?.AbortTransport();
                throw new InvalidOperationException("synthetic broken handshake");
            }
            return ValueTask.CompletedTask;
        };
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        bool reopened = false;
        connection.StateChange += (_, change) =>
        {
            Assert.NotEqual(ConnectionState.Broken, change.CurrentState);
            if (change.CurrentState == ConnectionState.Closed && !reopened)
            { reopened = true; connection.Open(); }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());
        Assert.True(reopened);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(1, source.Snapshot.Leased);
    }

    [Fact]
    public async Task CapacityGrantGapPreservesAbsolutePoolDeadlineBeforeHandshake()
    {
        using var hook = new SyntheticPoolHandshake();
        var clock = new PoolClock();
        using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=1000");
        using var connection = source.CreateConnection();
        connection.OperationClock = clock;
        DmPendingOpenTestHooks.AfterCapacityAcquired = _ => clock.Advance(TimeSpan.FromSeconds(1));
        var failure = await Assert.ThrowsAsync<DmTimeoutException>(() => connection.OpenAsync());
        Assert.Equal(DmFailurePhase.PoolWait, failure.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
        Assert.Empty(hook.Channels);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task CandidateUsesOriginalClockAndTimeoutFirstCauseSurvivesLateUserCancellation()
    {
        using var hook = new SyntheticPoolHandshake();
        var clock = new PoolClock();
        using var source = new DmDataSource(PoolApiSettings.Text + ";connect_timeout=1000");
        using var connection = source.CreateConnection();
        using var caller = new CancellationTokenSource();
        connection.OperationClock = clock;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            Assert.Same(clock, candidate.OperationClock);
            hook.Install(candidate);
            clock.Advance(TimeSpan.FromSeconds(1));
            caller.Cancel();
            throw new OperationCanceledException(caller.Token);
        };
        var failure = await Assert.ThrowsAsync<DmTimeoutException>(() => connection.OpenAsync(caller.Token));
        Assert.Equal(DmCancelSource.TotalDeadline, failure.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandshakeUserCancellationKeepsActualSendOutcome(bool sent)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        using var caller = new CancellationTokenSource();
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate);
            DmInvocation.Current.SendAttempted = sent;
            caller.Cancel();
            throw new OperationCanceledException(caller.Token);
        };
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => connection.OpenAsync(caller.Token));
        Assert.Equal(caller.Token, failure.CancellationToken);
        Assert.Equal(sent ? DmOperationOutcome.Unknown : DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.True(hook.Channels.Single().IsClosed);
    }

    [Fact]
    public async Task LateCancellationAfterHandshakeAcknowledgmentDoesNotClaimUnknownOrNotSent()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        using var caller = new CancellationTokenSource();
        DmPendingOpenTestHooks.BeforePublish = _ => caller.Cancel();
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => connection.OpenAsync(caller.Token));
        Assert.Equal(caller.Token, failure.CancellationToken);
        Assert.Equal(DmOperationOutcome.ServerReported, failure.FailureInfo.OperationOutcome);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public void GenerationExhaustionRejectsOpenButNeverPreventsClose()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.OpenConnection();
        typeof(DmConnection).GetField("openGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, long.MaxValue);
        connection.Close();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Throws<OverflowException>(() => connection.Open());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }
}

internal static class PoolApiSettings
{
    internal const string Text = "server=synthetic.invalid;user=POOL_SYNTH;password=CANARY_POOL;schema=POOL_SYNTH;Pooling=true;MaxPoolSize=1;MaxPoolWaiters=4;transport_security=PlaintextAllowed";
}

internal sealed class SyntheticPoolHandshake : IDisposable
{
    internal readonly List<IDmByteChannel> Channels = [];
    internal SyntheticPoolHandshake()
    {
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) => { Install(candidate); return ValueTask.CompletedTask; };
    }
    internal void Install(DmConnection candidate, IDmByteChannel? channel = null)
    {
        var instance = new DmConnInstance(candidate);
        candidate.m_ConnInst = instance;
        instance.ConnProperty.ServerVersion = "8.1.5.60";
        var protocol = instance.GetCsi();
        var wire = protocol.A();
        ((DmTransport)GetField(wire, "transport")!).Dispose();
        channel ??= new SyntheticPoolChannel();
        Channels.Add(channel);
        SetField(wire, "transport", new DmTransport(channel));
        SetField(wire, "__t02_field_04000AAD", false);
        SetField(protocol, "__t02_field_04000ABD", false);
        candidate.Session.BeginAuthenticating();
        candidate.do_State = ConnectionState.Open;
    }
    private static object? GetField(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);
    private static void SetField(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
    public void Dispose()
    {
        DmPendingOpenTestHooks.Handshake = null;
        DmPendingOpenTestHooks.AfterCapacityAcquired = null;
        DmPendingOpenTestHooks.BeforePublish = null;
        DmPendingOpenTestHooks.AfterInstalled = null;
        DmSessionTestHooks.BeforeConnectionTransportAbort = null;
    }
}
internal sealed class SyntheticPoolChannel : IDmByteChannel
{
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("Unexpected synthetic network I/O.");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("Unexpected synthetic network I/O.");
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token) => ValueTask.FromException<int>(new InvalidOperationException("Unexpected synthetic network I/O."));
    public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token) => ValueTask.FromException<int>(new InvalidOperationException("Unexpected synthetic network I/O."));
    public void Dispose() => IsClosed = true;
}
