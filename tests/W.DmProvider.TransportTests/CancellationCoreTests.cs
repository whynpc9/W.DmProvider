using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using LegacyTransport = W.Dm.Internal.Legacy.A.D;
using Xunit;

namespace W.DmProvider.TransportTests;

public sealed class CancellationCoreTests
{
    [Fact]
    public async Task CancelAtPreSendBarrierPreservesSessionAndActiveTransaction()
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(source.Token);
        DmTransportTestHooks.BeforeSendAttempt = _ => source.Cancel();
        try
        {
            using (fixture.Session.BeginWireExchange())
            {
                var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() =>
                    fixture.Transport.SendAllAsync([1, 2], 0, 2, DmDeadline.Infinite, 0, invocation.CancellationToken).AsTask());
                Assert.Equal(source.Token, error.CancellationToken);
                Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
                Assert.True(error.FailureInfo.ConnectionReusable);
                Assert.False(invocation.SendAttempted);
            }
            Assert.Equal(0, fixture.Channel.SendCalls);
            Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
            Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
            invocation.Dispose();
            lease.Dispose();
            Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
            Assert.False(fixture.Transport.IsClosed);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void CommandBeforeInvocationWinsOverLaterPreCanceledUserToken()
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var source = new CancellationTokenSource();
        // This is the lease-published / invocation-not-yet-created barrier.
        lease.Cancel();
        source.Cancel();
        var error = Assert.Throws<DmOperationCanceledException>(() => lease.BeginInvocation(source.Token));
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        Assert.NotEqual(source.Token, error.CancellationToken);
        Assert.True(error.CancellationToken.IsCancellationRequested);
        Assert.Equal(DmOperationOutcome.NotSent, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.Equal(DmFailurePhase.Prepare, error.FailureInfo.Phase);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(0, fixture.Channel.SendCalls);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        fixture.Session.MarkClosed();
        var closed = Assert.Throws<DmOperationCanceledException>(() => lease.BeginInvocation());
        Assert.False(closed.FailureInfo.ConnectionReusable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAfterPartialSendOrResponseAbortsCapturedTransport(bool receive)
    {
        using var fixture = new CancellationFixture();
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        using var source = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(source.Token);
        using var wire = fixture.Session.BeginWireExchange();
        fixture.Channel.BlockSendAfter = receive ? int.MaxValue : 1;
        fixture.Channel.BlockReceiveAfter = 1;
        Task operation;
        if (receive)
        {
            await fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
            operation = fixture.Transport.ReadExactlyAsync(new byte[3], 0, 3, invocation.Deadline, 0, invocation.CancellationToken).AsTask();
        }
        else operation = fixture.Transport.SendAllAsync([1, 2, 3], 0, 3, invocation.Deadline, 0, invocation.CancellationToken).AsTask();
        await fixture.Channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(operation.IsCompleted);
        source.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Transport.IsClosed);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
        Assert.Equal(receive ? 1 : 2, fixture.Channel.SendCalls);
        Assert.Equal(receive ? 2 : 0, fixture.Channel.ReceiveCalls);
        source.Cancel();
        lease.Cancel();
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task OldExecuteReaderTokenAndOldInvocationCannotTerminateNextRead()
    {
        using var fixture = new CancellationFixture();
        using var oldToken = new CancellationTokenSource();
        using var reader = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        DmInvocation oldInvocation;
        using (oldInvocation = reader.BeginInvocation(oldToken.Token))
        {
            using var wire = fixture.Session.BeginWireExchange();
            await fixture.Transport.SendAllAsync([1], 0, 1, oldInvocation.Deadline, 0, oldInvocation.CancellationToken);
            wire.Complete();
            oldInvocation.Complete();
        }
        using var nextToken = new CancellationTokenSource();
        using var next = reader.BeginInvocation(nextToken.Token);
        using var nextWire = fixture.Session.BeginWireExchange();
        oldToken.Cancel();
        fixture.Session.TerminateInvocation(oldInvocation, DmCancelSource.User, oldToken.Token);
        Assert.False(next.IsTerminated);
        await fixture.Transport.SendAllAsync([2], 0, 1, next.Deadline, 0, next.CancellationToken);
        nextWire.Complete();
        next.Complete();
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Equal(0, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task CommandCancelDuringIdleReaderPersistsAndDoesNotRetry()
    {
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using (var invocation = lease.BeginInvocation())
        {
            using var wire = fixture.Session.BeginWireExchange();
            await fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
            wire.Complete();
            invocation.Complete();
        }
        lease.Cancel();
        var error = Assert.Throws<DmOperationCanceledException>(() => lease.BeginInvocation());
        Assert.Equal(DmCancelSource.Command, error.FailureInfo.CancelSource);
        Assert.True(error.CancellationToken.IsCancellationRequested);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.SendCalls);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAndCloseInEitherOrderReleaseOnlyOnce(bool closeFirst)
    {
        using var fixture = new CancellationFixture();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        await fixture.Transport.SendAllAsync([1], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
        void Close() { fixture.Session.Detach()?.AbortTransport(); fixture.Session.MarkClosed(); }
        if (closeFirst) { Close(); lease.Cancel(); }
        else { lease.Cancel(); Close(); }
        lease.Cancel(); Close();
        Assert.Equal(DmPhysicalSessionState.Closed, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Fact]
    public async Task CapturedAbortPausedAtBarrierCannotCloseAReplacementPhysicalSession()
    {
        using var oldFixture = new CancellationFixture();
        using var oldToken = new CancellationTokenSource();
        using var oldLease = oldFixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var oldInvocation = oldLease.BeginInvocation(oldToken.Token);
        var oldWire = oldFixture.Session.BeginWireExchange();
        await oldFixture.Transport.SendAllAsync([1], 0, 1, oldInvocation.Deadline, 0, oldInvocation.CancellationToken);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        DmSessionTestHooks.BeforeTransportAbort = identity =>
        {
            if (identity != oldInvocation.Identity) return;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Abort barrier did not release.");
        };
        Task? cancellation = null;
        try
        {
            cancellation = Task.Run(oldToken.Cancel);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.Equal(DmPhysicalSessionState.Broken, oldFixture.Session.State);
            oldFixture.Session.MarkClosed();
            oldWire.Dispose();
            using var replacement = new CancellationFixture();
            using var lease = replacement.Session.BeginExecution(DmOperationPurpose.Query);
            using var invocation = lease.BeginInvocation();
            using var wire = replacement.Session.BeginWireExchange();
            oldLease.Cancel();
            replacement.Session.TerminateInvocation(oldInvocation, DmCancelSource.User, oldToken.Token);
            await replacement.Transport.SendAllAsync([2], 0, 1, invocation.Deadline, 0, invocation.CancellationToken);
            wire.Complete(); invocation.Complete();
            release.Set();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, oldFixture.Channel.DisposeCalls);
            Assert.Equal(0, replacement.Channel.DisposeCalls);
            Assert.Equal(DmPhysicalSessionState.Busy, replacement.Session.State);
        }
        finally
        {
            release.Set();
            if (cancellation != null) await cancellation.WaitAsync(TimeSpan.FromSeconds(3));
            DmSessionTestHooks.BeforeTransportAbort = null;
            oldWire.Dispose();
        }
    }

    [Fact]
    public async Task CompletedExecutionAndStaleLeaseCannotCancelFollowingExecution()
    {
        using var fixture = new CancellationFixture();
        using var source = new CancellationTokenSource();
        var oldLease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        DmInvocation old;
        using (old = oldLease.BeginInvocation(source.Token))
        {
            using var wire = fixture.Session.BeginWireExchange();
            await fixture.Transport.SendAllAsync([1], 0, 1, old.Deadline, 0, old.CancellationToken);
            wire.Complete(); old.Complete();
            source.Cancel(); oldLease.Cancel();
            Assert.False(old.IsTerminated);
        }
        oldLease.Dispose();
        using var currentLease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var current = currentLease.BeginInvocation();
        using var currentWire = fixture.Session.BeginWireExchange();
        oldLease.Cancel();
        // Direct stale send/completion calls must validate owner identity before
        // observing the old canceled token or the old elapsed deadline.
        source.Cancel();
        Assert.Throws<InvalidOperationException>(() => fixture.Session.TryBeginSendAttempt(old));
        Assert.Throws<InvalidOperationException>(() => old.Complete());
        await fixture.Transport.SendAllAsync([2], 0, 1, current.Deadline, 0, current.CancellationToken);
        currentWire.Complete(); current.Complete();
        Assert.Equal(0, fixture.Channel.DisposeCalls);
    }
}

internal sealed class CancellationFixture : IDisposable
{
    internal DmSession Session { get; } = new();
    internal ControlledChannel Channel { get; } = new();
    internal DmTransport Transport { get; }
    internal CancellationFixture()
    {
        Transport = new DmTransport(Channel);
        var adapter = (LegacyTransport)RuntimeHelpers.GetUninitializedObject(typeof(LegacyTransport));
        typeof(LegacyTransport).GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(adapter, Transport);
        Session.RegisterPendingTransport(adapter);
        Session.CompleteHandshakeForTests();
    }
    public void Dispose() => Transport.Dispose();
}

internal sealed class ControlledChannel : IDmByteChannel
{
    internal int BlockSendAfter = int.MaxValue;
    internal int BlockReceiveAfter = int.MaxValue;
    internal Action? OnProgress;
    internal int SendCalls;
    internal int ReceiveCalls;
    internal int DisposeCalls;
    internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsClosed => DisposeCalls != 0;
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("sync forbidden");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("sync forbidden");
    public async ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        if (++SendCalls > BlockSendAfter) { Blocked.TrySetResult(); await release.Task.WaitAsync(token).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested();
        OnProgress?.Invoke();
        return Math.Min(1, count);
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        if (++ReceiveCalls > BlockReceiveAfter) { Blocked.TrySetResult(); await release.Task.WaitAsync(token).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested();
        buffer[offset] = 1;
        OnProgress?.Invoke();
        return Math.Min(1, count);
    }
    public void Dispose() => Interlocked.Increment(ref DisposeCalls);
}
