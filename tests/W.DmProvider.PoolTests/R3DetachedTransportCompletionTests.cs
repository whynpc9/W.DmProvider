using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using LegacyTransport = W.Dm.Internal.Legacy.A.D;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "R3DetachedTransportCompletion")]
public sealed class R3DetachedTransportCompletionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldWireCleanupDoesNotClaimAnotherAbortOfATerminalSession(bool markClosed)
    {
        using var fixture = new PhysicalFixture();
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(caller.Token);
        var wire = fixture.Session.BeginWireExchange();
        fixture.Channel.AllowSend = true;
        await ActualTransport(fixture.Instance.GetCsi().A()).SendAllAsync([1], 0, 1,
            invocation.Deadline, 0, invocation.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int abortHooks = 0;
        DmSessionTestHooks.BeforeTransportAbort = identity =>
        {
            if (identity != invocation.Identity) return;
            Assert.Equal(1, Interlocked.Increment(ref abortHooks));
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };
        Task cancellation = Task.Run(caller.Cancel);
        try
        {
            await entered.Task.WaitAsync(Timeout);
            Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
            if (markClosed) fixture.Session.MarkClosed();
            wire.Dispose();
            Assert.Equal(1, Volatile.Read(ref abortHooks));
            Assert.Equal(0, fixture.Channel.DisposeCalls);
            DmDetachedTransport captured = fixture.Session.Detach();
            Assert.Same(captured, fixture.Session.Detach());
            int completed = 0;
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            Assert.Equal(0, completed);

            using var replacement = new PhysicalFixture();
            using var replacementLease = replacement.Session.BeginExecution(DmOperationPurpose.Query);
            using var replacementInvocation = replacementLease.BeginInvocation();
            using var replacementWire = replacement.Session.BeginWireExchange();
            lease.Cancel();
            replacement.Session.TerminateInvocation(invocation, DmCancelSource.User, caller.Token);
            fixture.Session.TerminateInvocation(invocation, DmCancelSource.User, caller.Token);
            replacement.Channel.AllowSend = true;
            await ActualTransport(replacement.Instance.GetCsi().A()).SendAllAsync([2], 0, 1,
                replacementInvocation.Deadline, 0, replacementInvocation.CancellationToken);
            replacementWire.Complete();
            replacementInvocation.Complete();
            Assert.Equal(1, fixture.Channel.SendCalls);
            Assert.Equal(1, replacement.Channel.SendCalls);
            Assert.Equal(0, replacement.Channel.DisposeCalls);
            Assert.Equal(DmPhysicalSessionState.Busy, replacement.Session.State);
            Assert.Equal(0, completed);
            release.TrySetResult();
            await cancellation.WaitAsync(Timeout);
            Assert.Equal(1, Volatile.Read(ref abortHooks));
            Assert.Equal(1, fixture.Channel.DisposeCalls);
            Assert.Equal(1, completed);
            Assert.Equal(0, replacement.Channel.DisposeCalls);
        }
        finally
        {
            release.TrySetResult();
            try { await cancellation.WaitAsync(Timeout); }
            finally
            {
                DmSessionTestHooks.BeforeTransportAbort = null;
                wire.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EarlierDirectCloseMustFinishBeforeDetachedTransportCompletes(bool instanceOwned, bool closeAdapter)
    {
        using var fixture = new PhysicalFixture();
        var channel = instanceOwned ? fixture.Channel : new CloseChannel();
        channel.PauseClose = true;
        LegacyTransport adapter = instanceOwned ? fixture.Instance.GetCsi().A() : Adapter(channel);
        DmTransport transport = ActualTransport(adapter);
        var captured = instanceOwned ? fixture.Session.Detach() : new DmDetachedTransport(null, adapter);
        int completed = 0;
        captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
        Task firstClose = Task.Run(() => { if (closeAdapter) adapter.C(); else transport.Close(); });
        try
        {
            await channel.CloseEntered.Task.WaitAsync(Timeout);
            Assert.True(transport.IsClosed); // Logical close precedes physical disposal.
            await Task.Run(captured.AbortTransport).WaitAsync(Timeout);
            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.False(channel.IsClosed);
            Assert.False(firstClose.IsCompleted);
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            channel.ReleaseClose.TrySetResult();
            await firstClose.WaitAsync(Timeout);
            Assert.True(channel.IsClosed);
            Assert.Equal(2, completed);
            Assert.Equal(1, channel.DisposeCalls);
        }
        finally
        {
            channel.ReleaseClose.TrySetResult();
            await firstClose.WaitAsync(Timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyCapturedInstanceKeepsItsPhysicalCompletionReference(bool syntheticInstance)
    {
        using var fixture = new PhysicalFixture();
        fixture.Channel.PauseClose = true;
        DmConnInstance instance = fixture.Instance;
        if (syntheticInstance)
        {
            instance = (DmConnInstance)RuntimeHelpers.GetUninitializedObject(typeof(DmConnInstance));
            SetField(instance, "m_Csi", fixture.Instance.GetCsi());
            SetField(instance, "_aliveCheckLockObj", new object());
        }
        Task firstAbort = Task.Run(instance.AbortTransport);
        try
        {
            await fixture.Channel.CloseEntered.Task.WaitAsync(Timeout);
            Assert.Null(instance.GetCsi());
            var captured = new DmDetachedTransport(instance, null);
            int completed = 0;
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            await Task.Run(captured.AbortTransport).WaitAsync(Timeout);
            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.False(firstAbort.IsCompleted);
            fixture.Channel.ReleaseClose.TrySetResult();
            await firstAbort.WaitAsync(Timeout);
            Assert.Equal(1, completed);
            Assert.Equal(1, fixture.Channel.DisposeCalls);
        }
        finally
        {
            fixture.Channel.ReleaseClose.TrySetResult();
            await firstAbort.WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task AbortDuringHandshakeReallyClosesTransportDespiteProtocolClosedFlag()
    {
        using var fixture = new PhysicalFixture();
        SetField(fixture.Instance.GetCsi(), "__t02_field_04000ABD", true);
        fixture.Channel.PauseClose = true;
        DmDetachedTransport captured = fixture.Session.Detach();
        int completed = 0;
        captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
        Task close = Task.Run(captured.AbortTransport);
        try
        {
            await fixture.Channel.CloseEntered.Task.WaitAsync(Timeout);
            Assert.Equal(1, fixture.Channel.DisposeCalls);
            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.False(close.IsCompleted);
            fixture.Channel.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
            Assert.Equal(1, completed);
            Assert.True(fixture.Channel.IsClosed);
        }
        finally
        {
            fixture.Channel.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
        }
    }

    [Fact]
    public void TransportCancellationCallbackFailureStillClosesChannelAndAttemptsEveryCompletion()
    {
        var channel = new CloseChannel();
        var transport = new DmTransport(channel);
        var source = (CancellationTokenSource)typeof(DmTransport).GetField("closeSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transport)!;
        var cancellationFailure = new InvalidOperationException("synthetic cancellation callback failure");
        using var registration = source.Token.Register(() => throw cancellationFailure);
        int completed = 0;
        transport.RunAfterPhysicalClosed(() => { completed++; throw new InvalidOperationException("synthetic completion callback failure"); });
        transport.RunAfterPhysicalClosed(() => completed++);
        AggregateException failure = Assert.Throws<AggregateException>(transport.Close);
        Assert.Same(cancellationFailure, Assert.Single(failure.InnerExceptions));
        Assert.True(channel.IsClosed);
        Assert.Equal(1, channel.DisposeCalls);
        Assert.Equal(2, completed);
        transport.RunAfterPhysicalClosed(() => completed++);
        transport.Close();
        Assert.Equal(3, completed);
    }

    [Fact]
    public async Task IdleReaderCancelAndRepeatedDetachShareUnfinishedPhysicalClose()
    {
        using var fixture = new PhysicalFixture();
        fixture.Channel.PauseClose = true;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        lease.SendAttempted = true; // The reader is between invocations after a sent request.
        Task cancel = Task.Run(lease.Cancel);
        try
        {
            await fixture.Channel.CloseEntered.Task.WaitAsync(Timeout);
            Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
            DmDetachedTransport captured = fixture.Session.Detach();
            int completed = 0;
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            await Task.Run(() =>
            {
                Assert.Same(captured, fixture.Session.Detach());
                captured.AbortTransport();
                fixture.Session.MarkClosed();
                Assert.Same(captured, fixture.Session.Detach());
            }).WaitAsync(Timeout);
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.False(fixture.Channel.IsClosed);
            Assert.False(cancel.IsCompleted);
            fixture.Channel.ReleaseClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
            Assert.True(fixture.Channel.IsClosed);
            Assert.Equal(2, completed);
            captured.AbortTransport();
            Assert.Equal(1, fixture.Channel.DisposeCalls);
            Assert.Equal(2, completed);
        }
        finally
        {
            fixture.Channel.ReleaseClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionWaitsForBothPhysicalCloseAttempts(bool pauseInstance)
    {
        using var fixture = new PhysicalFixture();
        var pendingChannel = new CloseChannel { PauseClose = !pauseInstance };
        fixture.Channel.PauseClose = pauseInstance;
        var captured = new DmDetachedTransport(fixture.Instance, Adapter(pendingChannel));
        int completed = 0;
        captured.RunAfterClosed(() =>
        {
            Assert.True(fixture.Channel.IsClosed);
            Assert.True(pendingChannel.IsClosed);
            Interlocked.Increment(ref completed);
        });
        Task close = Task.Run(captured.AbortTransport);
        try
        {
            CloseChannel paused = pauseInstance ? fixture.Channel : pendingChannel;
            await paused.CloseEntered.Task.WaitAsync(Timeout);
            await Task.Run(captured.AbortTransport).WaitAsync(Timeout);
            Assert.Equal(0, Volatile.Read(ref completed));
            Assert.False(close.IsCompleted);
            Assert.Equal(pauseInstance ? 0 : 1, pendingChannel.DisposeCalls);
            paused.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
            Assert.Equal(1, completed);
            Assert.Equal(1, fixture.Channel.DisposeCalls);
            Assert.Equal(1, pendingChannel.DisposeCalls);
        }
        finally
        {
            fixture.Channel.ReleaseClose.TrySetResult();
            pendingChannel.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task ChannelDisposalMayReenterAbortAndRegisterCompletionWithoutDeadlock()
    {
        var channel = new CloseChannel();
        var captured = new DmDetachedTransport(null, Adapter(channel));
        int completed = 0;
        channel.OnDispose = () =>
        {
            captured.AbortTransport();
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            Assert.Equal(0, completed);
        };
        captured.RunAfterClosed(() =>
        {
            Assert.True(channel.IsClosed);
            captured.AbortTransport();
            captured.RunAfterClosed(() => Interlocked.Increment(ref completed));
            Interlocked.Increment(ref completed);
        });
        await Task.Run(captured.AbortTransport).WaitAsync(Timeout);
        Assert.Equal(3, completed);
        Assert.Equal(1, channel.DisposeCalls);
    }

    [Fact]
    public async Task RegistrationsDuringPhysicalCloseAndAfterCompletionRunExactlyOnce()
    {
        var channel = new CloseChannel { PauseClose = true };
        var captured = new DmDetachedTransport(null, Adapter(channel));
        var counts = new int[32];
        for (int index = 0; index < 8; index++) Register(index);
        Task close = Task.Run(captured.AbortTransport);
        try
        {
            await channel.CloseEntered.Task.WaitAsync(Timeout);
            await Task.WhenAll(Enumerable.Range(8, 16).Select(index => Task.Run(() => Register(index)))).WaitAsync(Timeout);
            Assert.All(counts, count => Assert.Equal(0, count));
            channel.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
            for (int index = 24; index < counts.Length; index++) Register(index);
            captured.AbortTransport();
            Assert.All(counts, count => Assert.Equal(1, count));
            Assert.Equal(1, channel.DisposeCalls);
        }
        finally
        {
            channel.ReleaseClose.TrySetResult();
            await close.WaitAsync(Timeout);
        }
        void Register(int index) => captured.RunAfterClosed(() => Interlocked.Increment(ref counts[index]));
    }

    [Fact]
    public void PhysicalFailureTakesPriorityAndEveryCompletionIsAttempted()
    {
        using var fixture = new PhysicalFixture();
        var instanceFailure = new InvalidOperationException("synthetic instance close failure");
        var pendingFailure = new InvalidOperationException("synthetic pending close failure");
        fixture.Channel.Failure = instanceFailure;
        var pendingChannel = new CloseChannel { Failure = pendingFailure };
        var captured = new DmDetachedTransport(fixture.Instance, Adapter(pendingChannel));
        int completed = 0;
        captured.RunAfterClosed(() => { completed++; throw new InvalidOperationException("synthetic callback failure"); });
        captured.RunAfterClosed(() => completed++);
        Assert.Same(pendingFailure, Assert.Throws<InvalidOperationException>(captured.AbortTransport));
        Assert.Equal(1, fixture.Channel.DisposeCalls);
        Assert.Equal(1, pendingChannel.DisposeCalls);
        Assert.Equal(2, completed);
        captured.RunAfterClosed(() => completed++);
        captured.AbortTransport();
        Assert.Equal(3, completed);
    }

    [Fact]
    public void CallbackFailurePropagatesAfterAllCallbacksAndClosedRegistrationRunsSynchronously()
    {
        var captured = new DmDetachedTransport(null, null);
        var callbackFailure = new InvalidOperationException("synthetic callback failure");
        int completed = 0;
        captured.RunAfterClosed(() => { completed++; throw callbackFailure; });
        captured.RunAfterClosed(() => completed++);
        Assert.Same(callbackFailure, Assert.Throws<InvalidOperationException>(captured.AbortTransport));
        Assert.Equal(2, completed);
        Assert.Same(callbackFailure, Assert.Throws<InvalidOperationException>(() => captured.RunAfterClosed(() => throw callbackFailure)));
        captured.AbortTransport();
        Assert.Equal(2, completed);
    }

    [Fact]
    public void EmptyTransportCompletesNormallyAndCannotAcquireNewPhysicalResources()
    {
        var session = new DmSession();
        DmDetachedTransport captured = session.Detach();
        int completed = 0;
        captured.RunAfterClosed(() => completed++);
        Assert.Equal(0, completed);
        captured.AbortTransport();
        Assert.Equal(1, completed);
        captured.RunAfterClosed(() => completed++);
        session.MarkClosed();
        Assert.Same(captured, session.Detach());
        var rejected = new CloseChannel();
        Assert.Throws<InvalidOperationException>(() => session.RegisterPendingTransport(Adapter(rejected)));
        Assert.True(rejected.IsClosed);
        using var fixture = new PhysicalFixture();
        Assert.Throws<InvalidOperationException>(() => session.AttachTransport(fixture.Instance));
        Assert.Equal(2, completed);
    }

    [Fact]
    public void ExpectedIdentityIsValidatedBeforeReturningRetainedClosedHandle()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        using var lease = session.BeginExecution(DmOperationPurpose.Query);
        DmDetachedTransport captured = session.Detach(lease.Identity);
        captured.AbortTransport();
        session.MarkClosed();
        Assert.Null(session.Detach(lease.Identity));
        Assert.Same(captured, session.Detach());
    }

    private static LegacyTransport Adapter(CloseChannel channel)
    {
        var adapter = (LegacyTransport)RuntimeHelpers.GetUninitializedObject(typeof(LegacyTransport));
        SetField(adapter, "transport", new DmTransport(channel));
        return adapter;
    }

    private static DmTransport ActualTransport(LegacyTransport adapter) =>
        (DmTransport)typeof(LegacyTransport).GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(adapter)!;

    private static void SetField(object owner, string name, object? value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);

    private sealed class PhysicalFixture : IDisposable
    {
        private readonly DmConnection connection = new(PoolApiSettings.Text);
        internal DmSession Session { get; } = new();
        internal CloseChannel Channel { get; } = new();
        internal DmConnInstance Instance { get; }

        internal PhysicalFixture()
        {
            SetField(connection, "session", Session);
            Session.BeginConnecting();
            using var lease = Session.BeginExecution(DmOperationPurpose.Handshake);
            using var invocation = lease.BeginInvocation();
            Instance = new DmConnInstance(connection);
            var wire = Instance.GetCsi().A();
            ActualTransport(wire).Dispose();
            SetField(wire, "transport", new DmTransport(Channel));
            Session.CompleteHandshake();
        }

        public void Dispose()
        {
            Channel.ReleaseClose.TrySetResult();
            try { Session.Detach()?.AbortTransport(); }
            finally { connection.Dispose(); }
        }
    }

    private sealed class CloseChannel : IDmByteChannel
    {
        private int disposed;
        private int disposeCalls;
        private int sendCalls;
        internal bool PauseClose;
        internal bool AllowSend;
        internal Action? OnDispose;
        internal Exception? Failure;
        internal TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);
        internal int SendCalls => Volatile.Read(ref sendCalls);
        public bool IsClosed => Volatile.Read(ref disposed) != 0;
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            if (!AllowSend) throw new InvalidOperationException("Unexpected synthetic I/O.");
            Interlocked.Increment(ref sendCalls);
            return count;
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException("Unexpected synthetic I/O.");
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Send(buffer, offset, count, 0));
        }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token) => ValueTask.FromException<int>(new InvalidOperationException("Unexpected synthetic I/O."));
        public void Dispose()
        {
            Interlocked.Increment(ref disposeCalls);
            CloseEntered.TrySetResult();
            OnDispose?.Invoke();
            if (PauseClose) ReleaseClose.Task.GetAwaiter().GetResult();
            Volatile.Write(ref disposed, 1);
            if (Failure != null) throw Failure;
        }
    }
}
