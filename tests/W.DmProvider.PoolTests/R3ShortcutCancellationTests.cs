using System.Buffers.Binary;
using System.Data.Common;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "R3ShortcutCancellation")]
public sealed class R3ShortcutCancellationTests
{
    public static IEnumerable<object[]> Methods()
    {
        foreach (bool asynchronous in new[] { false, true })
        foreach (string method in new[] { "nonquery", "scalar", "reader", "prepare" })
            yield return new object[] { asynchronous, method };
    }
    public static IEnumerable<object[]> Gaps()
    {
        foreach (object[] method in Methods())
        foreach (string gap in new[] { "open", "plan", "lease" })
            yield return new object[] { method[0], method[1], gap };
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task CancelInterruptsUnlimitedPoolWaitWithoutStartingAnotherHandshake(bool asynchronous, string method)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var held = await source.OpenConnectionAsync();
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        Task call = Start(command, asynchronous, method);
        try
        {
            await WaitFor(() => source.Snapshot.Waiting == 1);
            Assert.False(call.IsCompleted);
            Assert.Single(handshake.Channels);
            command.Cancel();
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            AssertCanceled(failure, DmFailurePhase.PoolWait, DmOperationOutcome.NotSent);
            Assert.Equal(0, source.Snapshot.Waiting);
            Assert.Equal(1, source.Snapshot.Leased);
            Assert.Single(handshake.Channels);
            Assert.False(handshake.Channels[0].IsClosed);
        }
        finally
        {
            command.Cancel();
            await Drain(call);
        }
        await held.CloseAsync();
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task FailedOpenAfterSuppressedBrokenNotifiesClosedFromLastPublishedState()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var connection = new DmConnection(PoolApiSettings.Text + ";conn_pool_timeout=0");
        var events = new List<(System.Data.ConnectionState Original, System.Data.ConnectionState Current)>();
        connection.StateChange += (_, change) => { lock (events) events.Add((change.OriginalState, change.CurrentState)); };
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            handshake.Install(candidate);
            var invocation = DmInvocation.Current;
            invocation.SendAttempted = true;
            // A wire failure after a sent handshake breaks the pending session; its
            // Broken event is deliberately deferred until the workflow ends, so the
            // close notification must chain from the last published state instead.
            candidate.Session.TerminateInvocation(invocation, DmCancelSource.Command);
            throw new IOException("Synthetic wire failure after a sent handshake.");
        };
        await Assert.ThrowsAsync<DmOperationCanceledException>(() => connection.OpenAsync());
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        (System.Data.ConnectionState Original, System.Data.ConnectionState Current)[] snapshot;
        lock (events) snapshot = events.ToArray();
        Assert.Equal(new[]
        {
            (System.Data.ConnectionState.Closed, System.Data.ConnectionState.Connecting),
            (System.Data.ConnectionState.Connecting, System.Data.ConnectionState.Closed)
        }, snapshot);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task CancelInterruptsHandshakeAndKeepsCapacityUntilPhysicalAbort(bool asynchronous, string method)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        var channel = new ShortcutWireChannel(asynchronous) { PausePhysicalClose = true };
        var entered = NewSignal();
        int handshakes = 0;
        DmPendingOpenTestHooks.Handshake = async (candidate, asyncOpen, token) =>
        {
            if (Interlocked.Increment(ref handshakes) != 1)
            { handshake.Install(candidate); return; }
            Assert.Equal(asynchronous, asyncOpen);
            handshake.Install(candidate, channel);
            DmInvocation.Current.Phase = DmFailurePhase.Connect;
            DmInvocation.Current.SendAttempted = true;
            entered.TrySetResult();
            if (asyncOpen) await Task.Delay(System.Threading.Timeout.Infinite, token);
            else { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
        };
        Task call = Start(command, asynchronous, method);
        Task? cancel = null, waiting = null;
        await using var next = source.CreateConnection();
        try
        {
            await WaitForSignal(entered.Task, call);
            cancel = Task.Run(command.Cancel);
            await WaitForSignal(channel.CloseEntered.Task, cancel, allowCompletedObserver: true);
            waiting = next.OpenAsync();
            await WaitFor(() => source.Snapshot.Waiting == 1);
            Assert.False(channel.IsClosed);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, handshakes);
            channel.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            AssertCanceled(failure, DmFailurePhase.Connect, DmOperationOutcome.Unknown);
            await waiting.WaitAsync(Timeout);
            Assert.True(channel.IsClosed);
            Assert.Equal(2, handshakes);
            Assert.Equal(1, source.Snapshot.Leased);
        }
        finally
        {
            channel.ReleasePhysicalClose.TrySetResult();
            command.Cancel();
            if (cancel != null) await Drain(cancel);
            await Drain(call);
            if (waiting != null) { await next.CloseAsync(); await Drain(waiting); }
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [MemberData(nameof(Gaps))]
    public async Task CancelAtOpenPlanOrLeaseGapIsLatchedBeforeAnyExecuteFrame(bool asynchronous, string method, string gap)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        var channel = new ShortcutWireChannel(asynchronous);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, channel); return ValueTask.CompletedTask; };
        if (gap == "open") DmDataSourceCommand.AfterOpen = command.Cancel;
        else if (gap == "plan") DmCommand.AfterPlanCaptured = command.Cancel;
        else DmSessionTestHooks.AfterExecutionAcquired = _ => { if (handshake.Channels.Count != 0) command.Cancel(); };
        try
        {
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => Start(command, asynchronous, method).WaitAsync(Timeout));
            Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
            Assert.Empty(channel.Opcodes);
            Assert.True(channel.IsClosed);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally { ResetHooks(); }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task OrdinaryCommandCancellationKeepsCommandClassificationDuringRealWireIo(bool asynchronous, string method)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new ShortcutWireChannel(asynchronous) { HoldQueryReply = true };
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, channel); return ValueTask.CompletedTask; };
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", connection);
        Task call = Start(command, asynchronous, method);
        try
        {
            await WaitForSignal(channel.ReceiveEntered.Task, call);
            command.Cancel();
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, failure.FailureInfo.OperationOutcome);
            Assert.True(channel.IsClosed);
            short queryOpcode = !asynchronous && method != "prepare" ? (short)91 : (short)5;
            Assert.Equal(new short[] { 3, queryOpcode }, channel.Opcodes);
            channel.AssertIoPath();
        }
        finally { channel.ReleaseReply.TrySetResult(); command.Cancel(); await Drain(call); }
        await connection.CloseAsync();
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData("pool")]
    [InlineData("handshake")]
    [InlineData("command")]
    public async Task OriginalCallerTokenAndUserSourceSurviveEveryShortcutStage(string stage)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        using var caller = new CancellationTokenSource();
        var entered = NewSignal();
        var channel = new ShortcutWireChannel(true) { HoldQueryReply = stage == "command" };
        DmConnection? held = stage == "pool" ? await source.OpenConnectionAsync() : null;
        if (stage != "pool")
            DmPendingOpenTestHooks.Handshake = async (candidate, _, token) =>
            {
                handshake.Install(candidate, channel);
                if (stage != "handshake") return;
                DmInvocation.Current.Phase = DmFailurePhase.Connect;
                DmInvocation.Current.SendAttempted = true;
                entered.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, token);
            };
        Task call = command.PrepareAsync(caller.Token);
        try
        {
            if (stage == "pool") await WaitFor(() => source.Snapshot.Waiting == 1);
            else await WaitForSignal(stage == "handshake" ? entered.Task : channel.ReceiveEntered.Task, call);
            caller.Cancel();
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.Equal(DmCancelSource.User, failure.FailureInfo.CancelSource);
            if (stage == "handshake")
            {
                Assert.Equal(DmFailurePhase.Connect, failure.FailureInfo.Phase);
                Assert.Contains(failure.FailureInfo.ErrorCode, new[] { "WDM_CONNECT_CANCELED", "WDM_CANCELED" });
                Assert.IsType<DmOperationCanceledException>(failure.InnerException);
            }
            Assert.Equal(stage == "pool" ? DmOperationOutcome.NotSent : DmOperationOutcome.Unknown,
                failure.FailureInfo.OperationOutcome);
            if (stage == "pool") Assert.Single(handshake.Channels);
            else Assert.True(channel.IsClosed);
        }
        finally { caller.Cancel(); channel.ReleaseReply.TrySetResult(); await Drain(call); if (held != null) await held.DisposeAsync(); }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandshakeCancellationCallbackLagKeepsOriginalPhaseSourceAndSendOutcome(bool sent)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        using var caller = new CancellationTokenSource();
        var channel = new ShortcutWireChannel(true);
        var entered = NewSignal(); var blockerEntered = NewSignal(); var releaseBlocker = NewSignal();
        CancellationTokenRegistration blocker = default;
        DmInvocation? initial = null;
        DmPendingOpenTestHooks.Handshake = async (candidate, _, token) =>
        {
            handshake.Install(candidate, channel);
            initial = DmInvocation.Current;
            if (sent)
            {
                using var wire = candidate.Session.BeginWireExchange();
                candidate.Session.TryBeginSendAttempt(initial);
                wire.Complete();
            }
            blocker = token.Register(() =>
            {
                blockerEntered.TrySetResult();
                if (!releaseBlocker.Task.Wait(TimeSpan.FromSeconds(15)))
                    throw new InvalidOperationException("R3_CANCELLATION_BLOCKER_RELEASE_MISSING");
            });
            Task canceledDelay = Task.Delay(System.Threading.Timeout.Infinite, token);
            entered.TrySetResult(); // Delay registered last, ahead of the blocker and pending callbacks.
            await canceledDelay;
        };
        Task call = command.PrepareAsync(caller.Token);
        Task? cancel = null;
        try
        {
            await WaitForSignal(entered.Task, call);
            cancel = Task.Run(caller.Cancel);
            await WaitForSignal(blockerEntered.Task, cancel, allowCompletedObserver: true);
            await WaitForSignal(channel.CloseEntered.Task, cancel, allowCompletedObserver: true);
            await WaitForSignal(channel.PhysicalClosed.Task, cancel, allowCompletedObserver: true);
            Assert.False(cancel.IsCompleted);
            Assert.NotNull(initial);
            Assert.Equal(DmCancelSource.None, initial!.TerminalCause);
            Assert.Equal(sent, initial.SendAttempted);
            Assert.True(channel.IsClosed);
            releaseBlocker.TrySetResult();
            await cancel.WaitAsync(Timeout);
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.Equal(DmCancelSource.User, failure.FailureInfo.CancelSource);
            Assert.Equal(DmFailurePhase.Connect, failure.FailureInfo.Phase);
            Assert.Equal("WDM_CONNECT_CANCELED", failure.FailureInfo.ErrorCode);
            Assert.Equal(sent ? DmOperationOutcome.Unknown : DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
            var connectionFailure = Assert.IsType<DmOperationCanceledException>(failure.InnerException);
            Assert.IsType<TaskCanceledException>(connectionFailure.InnerException);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            releaseBlocker.TrySetResult();
            blocker.Dispose();
            if (cancel != null) await Drain(cancel);
            await Drain(call);
        }
    }

    [Fact]
    public async Task SuccessfulSecondaryCancelObserverStillWaitsForThePhysicalCloseSignal()
    {
        var signal = NewSignal();
        Task waiting = WaitForSignal(signal.Task, Task.CompletedTask, allowCompletedObserver: true);
        Assert.False(waiting.IsCompleted);
        signal.TrySetResult();
        await waiting.WaitAsync(Timeout);
    }

    [Fact]
    public async Task SuccessfulExecutionBeforeItsExpectedSignalIsRejected()
    {
        var signal = NewSignal();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => WaitForSignal(signal.Task, Task.CompletedTask));
        Assert.Equal("R3_EXECUTION_COMPLETED_BEFORE_BARRIER", failure.Message);
        Assert.False(signal.Task.IsCompleted);
    }

    [Fact]
    public async Task CloseWinnerSurvivesUserObservationBeforeItsLinkedLifetimeCallbackRuns()
    {
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var candidate = source.CreateConnection();
        using var caller = new CancellationTokenSource();
        using var pending = new DmPendingOpen(1, candidate.Settings, new DmSession(), candidate,
            caller.Token, DmDeadline.Infinite);
        var closeSource = (CancellationTokenSource)typeof(DmPendingOpen)
            .GetField("close", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
        var entered = NewSignal(); var release = NewSignal();
        CancellationTokenRegistration blocker = closeSource.Token.Register(() =>
        {
            entered.TrySetResult();
            if (!release.Task.Wait(TimeSpan.FromSeconds(15)))
                throw new InvalidOperationException("R3_CLOSE_BLOCKER_RELEASE_MISSING");
        });
        Task close = Task.Run(pending.CancelClose);
        try
        {
            await WaitForSignal(entered.Task, close, allowCompletedObserver: true);
            Assert.True(pending.CloseCancellationWon);
            Assert.False(pending.UserCancellationWon);
            Assert.False(pending.LifetimeToken.IsCancellationRequested);
            caller.Cancel();
            pending.ObserveUserCancellation();
            Assert.True(pending.CloseCancellationWon);
            Assert.False(pending.UserCancellationWon);
            release.TrySetResult();
            await close.WaitAsync(Timeout);
            Assert.True(pending.LifetimeToken.IsCancellationRequested);
        }
        finally { release.TrySetResult(); blocker.Dispose(); await Drain(close); }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task CompletedPhysicalSignalAndSuccessfulSecondaryObserverCanBeObservedTogether()
    {
        var signal = NewSignal(); signal.TrySetResult();
        await WaitForSignal(signal.Task, Task.CompletedTask, allowCompletedObserver: true);
    }

    [Fact]
    public async Task SecondaryCancelObserverFaultIsPropagatedBeforeThePhysicalSignal()
    {
        var signal = NewSignal();
        var primary = new InvalidOperationException("R3_SECONDARY_CANCEL_FAULT");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WaitForSignal(signal.Task, Task.FromException(primary), allowCompletedObserver: true));
        Assert.Same(primary, failure);
        Assert.False(signal.Task.IsCompleted);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("initial", true)]
    [InlineData("borrowed", true)]
    public async Task RawHandshakeCancellationRetainsCompletedInvocationAndRootSendEvidence(string sendOwner, bool sent)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        using var caller = new CancellationTokenSource();
        var entered = NewSignal(); var releaseWorkflow = NewSignal();
        DmInvocation? initial = null;
        DmExecutionLease? root = null;
        DmPendingOpenTestHooks.Handshake = async (candidate, _, token) =>
        {
            handshake.Install(candidate);
            initial = DmInvocation.Current;
            root = initial.Lease;
            if (sendOwner == "initial")
            {
                using var wire = candidate.Session.BeginWireExchange();
                candidate.Session.TryBeginSendAttempt(initial);
                wire.Complete();
            }
            // Match the handshake child handoff: the finished invocation has
            // unregistered its caller callback before the next child begins.
            initial.Complete();
            initial.Dispose();
            if (sendOwner == "borrowed")
            {
                using var borrowed = candidate.BeginInternalExecution(DmOperationPurpose.Query);
                using var child = borrowed.BeginInvocation();
                using var wire = candidate.Session.BeginWireExchange();
                candidate.Session.TryBeginSendAttempt(child);
                wire.Complete();
                child.Complete();
            }
            entered.TrySetResult();
            await releaseWorkflow.Task;
            token.ThrowIfCancellationRequested();
        };
        Task call = command.PrepareAsync(caller.Token);
        try
        {
            await WaitForSignal(entered.Task, call);
            Assert.NotNull(initial); Assert.NotNull(root);
            Assert.True(initial!.IsDisposed);
            Assert.Equal(DmCancelSource.None, initial.TerminalCause);
            Assert.Equal(sendOwner == "initial", initial.SendAttempted);
            Assert.Equal(sent, root!.SendAttempted);
            caller.Cancel(); // Complete every pending-token callback before releasing the raw failure.
            Assert.Equal(DmCancelSource.None, initial.TerminalCause);
            releaseWorkflow.TrySetResult();
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Timeout));
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.Equal(DmCancelSource.User, failure.FailureInfo.CancelSource);
            Assert.Equal(DmFailurePhase.Connect, failure.FailureInfo.Phase);
            Assert.Equal(sent ? DmOperationOutcome.Unknown : DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
            var connectionFailure = Assert.IsType<DmOperationCanceledException>(failure.InnerException);
            Assert.IsType<OperationCanceledException>(connectionFailure.InnerException);
            Assert.True(source.Snapshot.IsQuiescent);
            Assert.True(Assert.Single(handshake.Channels).IsClosed);
        }
        finally { caller.Cancel(); releaseWorkflow.TrySetResult(); await Drain(call); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedOldCancelCanResumeAfterReuseWithoutTouchingNewCall(bool asynchronous)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        var first = new ShortcutWireChannel(asynchronous) { HoldQueryReply = true };
        var second = new ShortcutWireChannel(asynchronous) { HoldQueryReply = true };
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, Interlocked.Increment(ref attempts) == 1 ? first : second); return ValueTask.CompletedTask; };
        Task call = Start(command, asynchronous, "prepare");
        var captured = NewSignal(); var resume = NewSignal();
        Task? oldCancel = null, replacement = null;
        try
        {
            await WaitForSignal(first.ReceiveEntered.Task, call);
            DmDataSourceCommand.AfterCancelCaptured = () =>
            { captured.TrySetResult(); resume.Task.GetAwaiter().GetResult(); };
            oldCancel = Task.Run(command.Cancel);
            await captured.Task.WaitAsync(Timeout);
            first.ReleaseReply.TrySetResult();
            await call.WaitAsync(Timeout);
            Assert.True(first.IsClosed);
            replacement = Start(command, asynchronous, "prepare");
            await WaitForSignal(second.ReceiveEntered.Task, replacement);
            resume.TrySetResult();
            await oldCancel.WaitAsync(Timeout);
            Assert.False(replacement.IsCompleted);
            Assert.False(second.IsClosed);
            second.ReleaseReply.TrySetResult();
            await replacement.WaitAsync(Timeout);
            first.AssertIoPath(); second.AssertIoPath();
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            ResetHooks(); resume.TrySetResult(); first.ReleaseReply.TrySetResult(); second.ReleaseReply.TrySetResult();
            if (oldCancel != null) await Drain(oldCancel);
            await Drain(call); if (replacement != null) await Drain(replacement);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderCancellationAndConcurrentDisposeKeepCapacityAndPermitSafeCommandReuse(bool asynchronous)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand("SELECT VALUE FROM SYNTHETIC_TABLE");
        var first = new ShortcutWireChannel(asynchronous);
        var second = new ShortcutWireChannel(asynchronous);
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, Interlocked.Increment(ref attempts) == 1 ? first : second); return ValueTask.CompletedTask; };
        DbDataReader reader = asynchronous ? await command.ExecuteReaderAsync() : command.ExecuteReader();
        first.PausePhysicalClose = true;
        first.BeforeDispose = reader.Dispose;
        Task cancel = Task.Run(command.Cancel);
        Task? dispose = null;
        try
        {
            await WaitForSignal(first.CloseEntered.Task, cancel, allowCompletedObserver: true);
            dispose = Task.Run(async () =>
            { if (asynchronous) await reader.DisposeAsync(); else reader.Dispose(); });
            // A correct close remains Closing at the abort barrier. An early
            // release completes Dispose and makes the following capacity check fail.
            await WaitFor(() => source.Snapshot.Closing == 1 || dispose.IsCompleted);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.False(first.IsClosed);
            // Cleanup owns the context until the captured physical abort completes.
            await Assert.ThrowsAsync<InvalidOperationException>(() => command.PrepareAsync());
            first.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
            await dispose.WaitAsync(Timeout);
            Assert.True(first.IsClosed);
            Assert.True(source.Snapshot.IsQuiescent);
            await Start(command, asynchronous, "prepare").WaitAsync(Timeout);
            Assert.Equal(2, attempts);
            Assert.True(second.IsClosed);
            first.AssertIoPath(); second.AssertIoPath();
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            first.ReleasePhysicalClose.TrySetResult();
            await Drain(cancel); if (dispose != null) await Drain(dispose);
            await reader.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false, "signal")]
    [InlineData(true, "signal")]
    [InlineData(false, "channel")]
    [InlineData(true, "channel")]
    public async Task ReentrantConnectionDisposeCannotReleaseCapacityDuringAnIdleReaderAbort(bool asynchronous, string boundary)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        var channel = new ShortcutWireChannel(asynchronous);
        int handshakes = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            if (Interlocked.Increment(ref handshakes) == 1) handshake.Install(candidate, channel);
            else handshake.Install(candidate);
            return ValueTask.CompletedTask;
        };
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", connection);
        DmExecutionLease? lease = null;
        DmCommand.AfterExecutionCaptured = captured => lease = captured;
        DbDataReader reader;
        try { reader = asynchronous ? await command.ExecuteReaderAsync() : command.ExecuteReader(); }
        finally { DmCommand.AfterExecutionCaptured = null; }
        channel.PausePhysicalClose = true;
        Assert.NotNull(lease);
        int callbacks = 0;
        void DisposeConnection() { Interlocked.Increment(ref callbacks); connection.Dispose(); }
        using var signal = boundary == "signal" ? lease!.CommandCancellationToken.Register(DisposeConnection) : default;
        if (boundary == "channel") channel.BeforeDispose = DisposeConnection;
        await using var next = source.CreateConnection();
        Task cancel = Task.Run(command.Cancel);
        Task? waiting = null;
        try
        {
            await WaitForSignal(channel.CloseEntered.Task, cancel, allowCompletedObserver: true);
            waiting = next.OpenAsync();
            Assert.Equal(1, callbacks);
            Assert.Equal(1, source.Snapshot.Closing);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.Equal(1, source.Snapshot.Waiting);
            Assert.Equal(1, handshakes);
            Assert.False(channel.IsClosed);
            Assert.False(waiting.IsCompleted);
            channel.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
            await waiting.WaitAsync(Timeout);
            Assert.Equal(2, handshakes);
            Assert.Equal(1, channel.DisposeCalls);
            Assert.True(channel.IsClosed);
        }
        finally
        {
            channel.ReleasePhysicalClose.TrySetResult(); await Drain(cancel);
            await reader.DisposeAsync(); await next.CloseAsync(); if (waiting != null) await Drain(waiting);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedNotificationFromReentrantAbortCanSynchronouslyReopenAfterCapacityReturns(bool duplicateClose)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        var first = new ShortcutWireChannel(true);
        int handshakes = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, Interlocked.Increment(ref handshakes) == 1 ? first : new ShortcutWireChannel(true)); return ValueTask.CompletedTask; };
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", connection);
        await using var reader = await command.ExecuteReaderAsync();
        first.PausePhysicalClose = true;
        int closed = 0;
        var reopened = NewSignal();
        connection.StateChange += (_, change) =>
        {
            if (change.CurrentState != System.Data.ConnectionState.Closed || Interlocked.Increment(ref closed) != 1) return;
            Assert.True(first.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            connection.Open();
            reopened.TrySetResult();
        };
        first.BeforeDispose = () =>
        {
            connection.Close();
            if (duplicateClose) connection.Close();
        };
        Task cancel = Task.Run(command.Cancel);
        try
        {
            await WaitForSignal(first.CloseEntered.Task, cancel, allowCompletedObserver: true);
            Assert.Equal(0, closed);
            Assert.Equal(1, source.Snapshot.Closing);
            Assert.False(cancel.IsCompleted);
            first.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Timeout);
            await reopened.Task.WaitAsync(Timeout);
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            Assert.Equal(2, handshakes);
            Assert.Equal(1, closed);
            await reader.DisposeAsync();
            await using var healthy = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", connection);
            await healthy.PrepareAsync();
        }
        finally { first.ReleasePhysicalClose.TrySetResult(); await Drain(cancel); await connection.CloseAsync(); }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task DeferredOldClosedNotificationCannotReportClosedAfterReplacementOpenHasStarted()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        var first = new ShortcutWireChannel(true);
        int handshakes = 0, closed = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, Interlocked.Increment(ref handshakes) == 1 ? first : new ShortcutWireChannel(true)); return ValueTask.CompletedTask; };
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", connection);
        await using var reader = await command.ExecuteReaderAsync();
        first.PausePhysicalClose = true;
        connection.StateChange += (_, change) => { if (change.CurrentState == System.Data.ConnectionState.Closed) closed++; };
        first.BeforeDispose = connection.Close;
        Task cancel = Task.Run(command.Cancel);
        Task? replacement = null;
        try
        {
            await WaitForSignal(first.CloseEntered.Task, cancel, allowCompletedObserver: true);
            replacement = connection.OpenAsync();
            Assert.Equal(System.Data.ConnectionState.Connecting, connection.State);
            Assert.Equal(1, source.Snapshot.Waiting);
            first.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Timeout); await replacement.WaitAsync(Timeout);
            Assert.Equal(0, closed);
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            Assert.Equal(2, handshakes);
        }
        finally
        {
            first.ReleasePhysicalClose.TrySetResult(); await Drain(cancel);
            if (replacement != null) await Drain(replacement); await connection.CloseAsync();
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingCloseWaitsForOriginalWorkflowBeforeReturningCapacityAndNotifyingClosed(bool duplicateClose)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        var first = new ShortcutWireChannel(true);
        var entered = NewSignal(); var releaseWorkflow = NewSignal(); var reopened = NewSignal();
        int handshakes = 0, closed = 0;
        bool workflowStopped = false;
        DmPendingOpenTestHooks.Handshake = async (candidate, _, _) =>
        {
            if (Interlocked.Increment(ref handshakes) != 1)
            { handshake.Install(candidate, new ShortcutWireChannel(true)); return; }
            handshake.Install(candidate, first);
            entered.TrySetResult();
            await releaseWorkflow.Task;
            workflowStopped = true;
        };
        await using var connection = source.CreateConnection();
        connection.StateChange += (_, change) =>
        {
            if (change.CurrentState != System.Data.ConnectionState.Closed || Interlocked.Increment(ref closed) != 1) return;
            Assert.True(workflowStopped);
            Assert.True(first.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            connection.Open();
            reopened.TrySetResult();
        };
        Task opening = connection.OpenAsync();
        try
        {
            await WaitForSignal(entered.Task, opening);
            connection.Close();
            if (duplicateClose) connection.Close();
            Assert.True(first.IsClosed);
            Assert.False(workflowStopped);
            Assert.False(opening.IsCompleted);
            Assert.Equal(1, source.Snapshot.Creating);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.Equal(0, closed);
            Assert.Equal(1, handshakes);
            releaseWorkflow.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => opening.WaitAsync(Timeout));
            await reopened.Task.WaitAsync(Timeout);
            Assert.Equal(1, closed);
            Assert.Equal(2, handshakes);
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            await using var healthy = new DmCommand("SELECT 1 FROM DUAL", connection);
            await healthy.PrepareAsync();
        }
        finally { releaseWorkflow.TrySetResult(); await Drain(opening); await connection.CloseAsync(); }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task WaitFor(Func<bool> condition) => Task.Run(() => Assert.True(SpinWait.SpinUntil(condition, Timeout)));
    private static async Task WaitForSignal(Task signal, Task execution, bool allowCompletedObserver = false)
    {
        Task winner = await Task.WhenAny(signal, execution).WaitAsync(Timeout);
        if (winner == execution)
        {
            await execution; // Preserve the actual fixture/execution failure.
            if (!allowCompletedObserver && !signal.IsCompleted)
                throw new InvalidOperationException("R3_EXECUTION_COMPLETED_BEFORE_BARRIER");
        }
        await signal.WaitAsync(Timeout);
        if (execution.IsFaulted || execution.IsCanceled) await execution;
    }
    private static async Task Drain(Task call) { try { await call.WaitAsync(Timeout); } catch { } }
    private static void AssertCanceled(DmOperationCanceledException failure, DmFailurePhase phase, DmOperationOutcome outcome)
    {
        Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
        Assert.Equal(phase, failure.FailureInfo.Phase);
        Assert.Equal(outcome, failure.FailureInfo.OperationOutcome);
    }
    private static void ResetHooks()
    {
        DmDataSourceCommand.AfterOpen = null; DmDataSourceCommand.AfterCancelCaptured = null;
        DmCommand.AfterPlanCaptured = null; DmCommand.AfterExecutionCaptured = null;
        DmSessionTestHooks.AfterExecutionAcquired = null;
    }
    private static Task Start(DbCommand command, bool asynchronous, string method, CancellationToken token = default)
    {
        if (asynchronous)
            return method switch
            {
                "nonquery" => command.ExecuteNonQueryAsync(token), "scalar" => command.ExecuteScalarAsync(token),
                "reader" => ReadAndCloseAsync(command, token), _ => command.PrepareAsync(token)
            };
        return Task.Run(() =>
        {
            switch (method)
            {
                case "nonquery": command.ExecuteNonQuery(); break;
                case "scalar": command.ExecuteScalar(); break;
                case "reader": using (command.ExecuteReader()) { } break;
                default: command.Prepare(); break;
            }
        });
    }
    private static async Task ReadAndCloseAsync(DbCommand command, CancellationToken token)
    { await using var reader = await command.ExecuteReaderAsync(token); }

    // Fixed allocation, one-column SELECT, and close replies traverse the real
    // statement/protocol paths. I/O and physical-close barriers are independently released.
    private sealed class ShortcutWireChannel(bool asynchronous) : IDmByteChannel
    {
        private byte[] reply = [];
        private int position;
        private short opcode;
        internal readonly List<short> Opcodes = [];
        internal readonly TaskCompletionSource ReceiveEntered = NewSignal();
        internal readonly TaskCompletionSource ReleaseReply = NewSignal();
        internal readonly TaskCompletionSource CloseEntered = NewSignal();
        internal readonly TaskCompletionSource PhysicalClosed = NewSignal();
        internal readonly TaskCompletionSource ReleasePhysicalClose = NewSignal();
        internal bool HoldQueryReply, PausePhysicalClose;
        internal int SyncCalls, AsyncCalls, DisposeCalls;
        internal Action? BeforeDispose;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout)
        { SyncCalls++; Assert.False(asynchronous); return SendCore(buffer, offset, count); }
        public int Receive(byte[] buffer, int offset, int count, int timeout)
        {
            SyncCalls++; Assert.False(asynchronous);
            if (HoldQueryReply && (opcode is 5 or 91))
            { ReceiveEntered.TrySetResult(); Assert.True(ReleaseReply.Task.Wait(TimeSpan.FromSeconds(15))); }
            return ReceiveCore(buffer, offset, count);
        }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { AsyncCalls++; Assert.True(asynchronous); token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, offset, count)); }
        public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            AsyncCalls++; Assert.True(asynchronous); token.ThrowIfCancellationRequested();
            if (HoldQueryReply && (opcode is 5 or 91))
            { ReceiveEntered.TrySetResult(); await ReleaseReply.Task.WaitAsync(token); }
            return ReceiveCore(buffer, offset, count);
        }
        private int SendCore(byte[] buffer, int offset, int count)
        {
            opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Opcodes.Add(opcode);
            Assert.Contains(opcode, new short[] { 3, 5, 91, 4 });
            // Opcode 5 carries both prepare and execute. Its byte-21 flag
            // distinguishes their different reply header layouts.
            bool query = opcode == 91 || opcode == 5 && buffer[offset + 21] == 1;
            reply = new byte[query ? 64 + 32 + 5 : 64];
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(4), opcode);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(6), reply.Length - 64);
            if (opcode == 3) BinaryPrimitives.WriteInt32LittleEndian(reply, 41);
            if (opcode is 5 or 91) BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(20), 160);
            if (query)
            {
                BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(22), 1);
                // Independent 32-byte plain column descriptor: INT32(7),
                // precision 4, nullable, no LOB flag, and five-byte ASCII name.
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(64), 7);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(68), 4);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(76), 1);
                BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(88), 5);
                new byte[] { 86, 65, 76, 85, 69 }.CopyTo(reply, 96);
            }
            for (int index = 0; index < 19; index++) reply[19] ^= reply[index];
            position = 0; return count;
        }
        private int ReceiveCore(byte[] buffer, int offset, int count)
        {
            if (IsClosed) return 0;
            int read = Math.Min(count, reply.Length - position);
            reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read; return read;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCalls);
            BeforeDispose?.Invoke();
            CloseEntered.TrySetResult();
            if (PausePhysicalClose) Assert.True(ReleasePhysicalClose.Task.Wait(TimeSpan.FromSeconds(15)));
            IsClosed = true;
            ReleaseReply.TrySetResult();
            PhysicalClosed.TrySetResult();
        }
        internal void AssertIoPath()
        {
            Assert.True(asynchronous ? AsyncCalls > 0 : SyncCalls > 0);
            Assert.Equal(0, asynchronous ? SyncCalls : AsyncCalls);
        }
    }
}
