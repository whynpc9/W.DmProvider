using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class R2ReviewBeginTests
{
    [Fact]
    public async Task CleanupDeadlineAfterCompletedConfigurationKeepsTimeoutDespiteLateCallerCancellation()
    {
        var clock = new CancellationTestClock();
        await using var fixture = await BeginReviewFixture.CreateAsync(clock, cleanupMilliseconds: 500);
        using var caller = new CancellationTokenSource();
        fixture.Channel.BlockCleanup = true;
        Task call = fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable, caller.Token).AsTask();
        await fixture.Channel.CleanupReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.AssertConfigurationEnded();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        caller.Cancel();
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => call);
        AssertFailure(error.FailureInfo, DmErrorKind.Timeout, DmFailurePhase.Cleanup,
            DmCancelSource.TotalDeadline, DmOperationOutcome.Unknown);
        Assert.Same(fixture.Channel.ReceiveCancellation, error.InnerException);
        fixture.AssertAborted(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawCleanupTimeoutIsTranslatedInItsOwnPhaseInBothFacades(bool synchronous)
    {
        var clock = new CancellationTestClock();
        await using var fixture = await BeginReviewFixture.CreateAsync(clock);
        using var caller = new CancellationTokenSource();
        var original = new TimeoutException("synthetic cleanup timeout");
        DmTransportTestHooks.BeforeSendAttempt = _ =>
        {
            if (DmInvocation.Current?.Phase != DmFailurePhase.Cleanup) return;
            fixture.AssertConfigurationEnded();
            caller.Cancel();
            throw original;
        };
        try
        {
            DmTimeoutException error;
            if (synchronous)
                error = Assert.Throws<DmTimeoutException>(() => fixture.Connection.BeginTransaction(IsolationLevel.Serializable));
            else
                error = await Assert.ThrowsAsync<DmTimeoutException>(() => fixture.Connection
                    .BeginTransactionAsync(IsolationLevel.Serializable, caller.Token).AsTask());
            Assert.Same(original, error.InnerException);
            AssertFailure(error.FailureInfo, DmErrorKind.Timeout, DmFailurePhase.Cleanup,
                DmCancelSource.TotalDeadline, DmOperationOutcome.NotSent);
            fixture.AssertAborted(2);
        }
        finally { DmTransportTestHooks.BeforeSendAttempt = null; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActivationFactoryFailureUsesItsOwnCauseAfterConfigurationAndCleanupEnd(bool cancellation)
    {
        var clock = new CancellationTestClock();
        await using var fixture = await BeginReviewFixture.CreateAsync(clock);
        using var caller = new CancellationTokenSource();
        DmInvocation? cleanup = null;
        DmResultProtocolTrace.AfterFrame = (opcode, _, _, _) =>
        {
            if (opcode != 4) return;
            fixture.AssertConfigurationEnded();
            cleanup = DmInvocation.Current;
            // Cleanup does not inherit the caller token. The next child's
            // factory observes this exact pre-canceled token / expired budget.
            if (cancellation) caller.Cancel();
            clock.Advance(TimeSpan.FromSeconds(1));
        };
        try
        {
            Task call = fixture.Connection.BeginTransactionAsync(IsolationLevel.Serializable, caller.Token).AsTask();
            if (cancellation)
            {
                var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
                Assert.Equal(caller.Token, error.CancellationToken);
                var original = Assert.IsType<OperationCanceledException>(error.InnerException);
                Assert.Equal(caller.Token, original.CancellationToken);
                AssertFailure(error.FailureInfo, DmErrorKind.Canceled, DmFailurePhase.Prepare,
                    DmCancelSource.User, DmOperationOutcome.NotSent);
            }
            else
            {
                var error = await Assert.ThrowsAsync<DmTimeoutException>(() => call);
                AssertFailure(error.FailureInfo, DmErrorKind.Timeout, DmFailurePhase.Prepare,
                    DmCancelSource.TotalDeadline, DmOperationOutcome.NotSent);
            }
            Assert.NotNull(cleanup);
            Assert.True(cleanup.IsDisposed);
            fixture.AssertAborted(3);
        }
        finally { DmResultProtocolTrace.AfterFrame = null; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TypedCleanupFirstCauseAndOriginalExceptionSurviveAnOppositeLateCause(bool timeout)
    {
        var clock = new CancellationTestClock();
        await using var fixture = await BeginReviewFixture.CreateAsync(clock);
        using var caller = new CancellationTokenSource();
        var original = timeout ? (Exception)new TimeoutException("synthetic first deadline") :
            new OperationCanceledException("synthetic first cancellation", caller.Token);
        var info = new DmFailureInfo(timeout ? DmErrorKind.Timeout : DmErrorKind.Canceled,
            DmFailurePhase.Cleanup, timeout ? "WDM_TIMEOUT" : "WDM_CANCELED", DmOperationOutcome.NotSent,
            DmTransactionOutcome.Starting, true, timeout ? DmCancelSource.TotalDeadline : DmCancelSource.User);
        Exception typed = timeout ? new DmTimeoutException(info, original) :
            new DmOperationCanceledException(info, caller.Token, original);
        DmTransportTestHooks.BeforeSendAttempt = _ =>
        {
            if (DmInvocation.Current?.Phase != DmFailurePhase.Cleanup) return;
            fixture.AssertConfigurationEnded();
            if (timeout) caller.Cancel();
            else
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(TimeSpan.Zero, DmInvocation.Current.Lease.Deadline.RemainingTime);
            }
            throw typed;
        };
        try
        {
            var error = await Record.ExceptionAsync(() => fixture.Connection
                .BeginTransactionAsync(IsolationLevel.Serializable, caller.Token).AsTask());
            Assert.Same(typed, error);
            Assert.Same(original, error!.InnerException);
            var finalInfo = timeout ? ((DmTimeoutException)error).FailureInfo :
                ((DmOperationCanceledException)error).FailureInfo;
            AssertFailure(finalInfo, timeout ? DmErrorKind.Timeout : DmErrorKind.Canceled, DmFailurePhase.Cleanup,
                timeout ? DmCancelSource.TotalDeadline : DmCancelSource.User, DmOperationOutcome.NotSent);
            if (!timeout) Assert.Equal(caller.Token, ((DmOperationCanceledException)error).CancellationToken);
            fixture.AssertAborted(2);
        }
        finally { DmTransportTestHooks.BeforeSendAttempt = null; }
    }

    [Fact]
    public async Task TypedCleanupServerMetadataKeepsItsOriginalServerNumber()
    {
        var clock = new CancellationTestClock();
        await using var fixture = await BeginReviewFixture.CreateAsync(clock);
        var original = new DmException("synthetic server failure");
        original.SetFailureInfo(new DmFailureInfo(DmErrorKind.Server, DmFailurePhase.Cleanup,
            "WDM_SERVER", DmOperationOutcome.ServerReported, DmTransactionOutcome.Starting,
            true, serverErrorNumber: 12345));
        DmTransportTestHooks.BeforeSendAttempt = _ =>
        {
            if (DmInvocation.Current?.Phase != DmFailurePhase.Cleanup) return;
            fixture.AssertConfigurationEnded();
            throw original;
        };
        try
        {
            var error = await Assert.ThrowsAsync<DmException>(() => fixture.Connection
                .BeginTransactionAsync(IsolationLevel.Serializable).AsTask());
            Assert.Same(original, error);
            Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
            Assert.Equal(DmFailurePhase.Cleanup, error.FailureInfo.Phase);
            Assert.Equal(12345, error.FailureInfo.ServerErrorNumber);
            Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
            Assert.False(error.FailureInfo.ConnectionReusable);
            fixture.AssertAborted(2);
        }
        finally { DmTransportTestHooks.BeforeSendAttempt = null; }
    }

    private static void AssertFailure(DmFailureInfo info, DmErrorKind kind, DmFailurePhase phase,
        DmCancelSource cause, DmOperationOutcome outcome)
    {
        Assert.Equal(kind, info.ErrorKind);
        Assert.Equal(phase, info.Phase);
        Assert.Equal(cause, info.CancelSource);
        Assert.Equal(outcome, info.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, info.TransactionOutcome);
        Assert.False(info.ConnectionReusable);
        Assert.Null(info.ServerErrorNumber);
        Assert.Equal(kind == DmErrorKind.Timeout ? "WDM_TIMEOUT" : "WDM_CANCELED", info.ErrorCode);
    }
}

internal sealed class BeginReviewFixture(TransactionAsyncFixture database) : IAsyncDisposable
{
    internal DmConnection Connection => database.Connection;
    internal BeginReviewChannel Channel { get; } = new();
    internal DmInvocation? Configuration;
    private DmTransaction? failedTransaction;

    internal static async Task<BeginReviewFixture> CreateAsync(CancellationTestClock clock, int cleanupMilliseconds = 5000)
    {
        var database = new TransactionAsyncFixture(clock, commandTimeout: 1,
            cleanupTimeout: TimeSpan.FromMilliseconds(cleanupMilliseconds));
        database.Channel.ReleaseResponse.TrySetResult(true);
        await database.Transaction.RollbackAsync();
        var fixture = new BeginReviewFixture(database);
        fixture.Channel.ObserveConfiguration = invocation => fixture.Configuration = invocation;
        fixture.Channel.ObserveCleanup = () =>
        {
            fixture.AssertConfigurationEnded();
            fixture.failedTransaction = fixture.Connection.m_ConnInst.Transaction;
        };
        var wire = database.Connection.m_ConnInst.GetCsi().A();
        var field = wire.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((DmTransport)field.GetValue(wire)!).Dispose();
        field.SetValue(wire, new DmTransport(fixture.Channel));
        return fixture;
    }

    internal void AssertConfigurationEnded()
    {
        Assert.NotNull(Configuration);
        Assert.True(Configuration.Completed);
        Assert.True(Configuration.IsDisposed);
        failedTransaction ??= Connection.m_ConnInst.Transaction;
    }
    internal void AssertAborted(int sends)
    {
        AssertConfigurationEnded();
        Assert.Equal(sends, Channel.Sends);
        Assert.True(Channel.IsClosed);
        Assert.Equal(ConnectionState.Closed, Connection.State);
        Assert.Equal(DmPhysicalSessionState.Closed, database.Session.State);
        Assert.Null(Connection.Session);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, failedTransaction!.Outcome);
    }
    public ValueTask DisposeAsync() => database.DisposeAsync();
}

// Valid minimal allocation, SET TRANSACTION receipt and close replies. Only
// cleanup can wait at the controlled barrier; no socket, database or sleep.
internal sealed class BeginReviewChannel : IDmByteChannel
{
    private byte[] response = [];
    private int responseOffset;
    private short opcode;
    private readonly TaskCompletionSource<bool> cleanupResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends;
    internal bool BlockCleanup;
    internal Action<DmInvocation>? ObserveConfiguration;
    internal Action? ObserveCleanup;
    internal OperationCanceledException? ReceiveCancellation;
    internal TaskCompletionSource<bool> CleanupReceiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsClosed { get; private set; }

    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        Sends++;
        opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4, 2));
        if (opcode == 3) ObserveConfiguration!(DmInvocation.Current);
        if (opcode == 4) ObserveCleanup!();
        response = new byte[opcode == 5 ? 67 : 64];
        responseOffset = 0;
        if (opcode == 3) BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(0, 4), 41);
        if (opcode == 5)
        {
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(6, 4), 3);
            BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(20, 2), 150);
            BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(64, 2), 3);
        }
        for (int index = 0; index < 19; index++) response[19] ^= response[index];
        return count;
    }
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        int copied = Math.Min(count, response.Length - responseOffset);
        Array.Copy(response, responseOffset, buffer, offset, copied);
        responseOffset += copied;
        return copied;
    }
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Send(buffer, offset, count, 0));
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (opcode == 4 && BlockCleanup)
        {
            CleanupReceiveEntered.TrySetResult(true);
            try { await cleanupResponse.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException error) { ReceiveCancellation = error; throw; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Receive(buffer, offset, count, 0);
    }
    public void Dispose() => IsClosed = true;
}
