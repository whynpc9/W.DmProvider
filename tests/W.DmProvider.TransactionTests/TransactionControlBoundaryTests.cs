using System.IO;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.TransactionTests;

public sealed class TransactionControlBoundaryTests
{
    [Fact]
    public void UnknownCommitExceptionIsNonTransient()
    {
        var exception = new DmCommitOutcomeUnknownException(new IOException("synthetic_lost_response"));
        Assert.False(exception.IsTransient);
        Assert.IsType<IOException>(exception.InnerException);
    }

    [Fact]
    public void LegacyPublicFlagsCannotForgeCanonicalSessionOutcome()
    {
        var (session, transaction) = ActiveTransaction();
        Assert.Throws<NotSupportedException>(() => transaction.Valid = false);
        Assert.Throws<NotSupportedException>(() => transaction.Clear());
        transaction._disposeStatus = DmTransaction.DisposeStatus.Disposed;
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        Assert.Equal(DmLocalTransactionState.Active, session.TransactionState);
    }

    [Fact]
    public void ControlInvocationsShareOneAbsoluteDeadline()
    {
        var time = new ManualTimeProvider();
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl,
            DmDeadline.Start(TimeSpan.FromSeconds(30), time));
        using (var first = lease.BeginInvocation())
        {
            Assert.Equal(30000, first.Deadline.RemainingMilliseconds);
            time.AdvanceMilliseconds(29000);
            Assert.Equal(1000, first.Deadline.RemainingMilliseconds);
        }
        using (var second = lease.BeginInvocation())
        {
            Assert.Equal(1000, second.Deadline.RemainingMilliseconds);
            time.AdvanceMilliseconds(1000);
            Assert.Throws<TimeoutException>(() => second.Deadline.ThrowIfExpired());
        }
        Assert.Throws<TimeoutException>(() => lease.Deadline.ThrowIfExpired());
    }

    [Fact]
    public void InfiniteControlBudgetDoesNotMakeDisposeCleanupInfinite()
    {
        var time = new ManualTimeProvider();
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl,
            DmDeadline.Start(TimeSpan.Zero, time));
        Assert.True(lease.Deadline.IsInfinite);
        using var cleanup = lease.BeginCleanupInvocation(TimeSpan.FromMilliseconds(500));
        Assert.Equal(500, cleanup.Deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(500);
        Assert.Throws<TimeoutException>(() => cleanup.Deadline.ThrowIfExpired());
        Assert.True(lease.Deadline.IsInfinite);
    }

    [Fact]
    public void ReaderCleanupAndRollbackShareTheOriginalFiniteCleanupBudget()
    {
        var time = new ManualTimeProvider();
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        var cleanupDeadline = DmDeadline.Start(TimeSpan.FromMilliseconds(500), time);
        using (var readerLease = session.BeginExecution(DmOperationPurpose.Reader,
            DmDeadline.Start(TimeSpan.Zero, time)))
        using (var readerCleanup = readerLease.BeginCleanupInvocation(cleanupDeadline))
        {
            time.AdvanceMilliseconds(400);
            Assert.Equal(100, readerCleanup.Deadline.RemainingMilliseconds);
        }
        using var rollbackLease = session.BeginExecution(DmOperationPurpose.TransactionControl, cleanupDeadline);
        using var rollback = rollbackLease.BeginInvocation();
        Assert.Equal(100, rollback.Deadline.RemainingMilliseconds);
        time.AdvanceMilliseconds(100);
        Assert.Throws<TimeoutException>(() => rollback.Deadline.ThrowIfExpired());
    }

    [Fact]
    public void FailureBeforeAnySendAttemptPreservesActiveTransaction()
    {
        var (session, transaction) = ActiveTransaction();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        DmTransportTestHooks.BeforeControlSendAttempt = (_, kind) =>
        {
            Assert.Equal(DmTransactionControlKind.Commit, kind);
            throw new InvalidOperationException("synthetic_pre_send_fault");
        };
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            Assert.Throws<InvalidOperationException>(() =>
                transport.SendAll([1, 2, 3], 0, 3, DmDeadline.Infinite, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
            Assert.Equal(DmLocalTransactionState.Active, session.TransactionState);
            Assert.Equal(0, channel.SendCalls);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void AttemptMarkedBeforeZeroByteWriteMakesOutcomeUnknown()
    {
        var (session, transaction) = ActiveTransaction();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        DmTransportTestHooks.AfterControlAttemptMarked = (_, kind) =>
        {
            Assert.Equal(DmTransactionControlKind.Commit, kind);
            throw new IOException("synthetic_zero_byte_attempt");
        };
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            Assert.Throws<IOException>(() =>
                transport.SendAll([1, 2, 3], 0, 3, DmDeadline.Infinite, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
            Assert.Equal(DmLocalTransactionState.OutcomeUnknown, session.TransactionState);
            Assert.Equal(0, channel.SendCalls);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void PartialSendThenFailureNeverClaimsCommitOrRetries()
    {
        var (session, transaction) = ActiveTransaction();
        var channel = new FakeChannel(FakeChannelMode.OneByteThenThrow);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            Assert.Throws<IOException>(() =>
                transport.SendAll([1, 2, 3], 0, 3, DmDeadline.Infinite, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
            Assert.Equal(2, channel.SendCalls);
            Assert.Equal(1, channel.SentBytes);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void CompleteSendWithLostResponseRemainsUnknown()
    {
        var (session, transaction) = ActiveTransaction();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            transport.SendAll([1, 2, 3], 0, 3, DmDeadline.Infinite, 0);
            Assert.Throws<EndOfStreamException>(() =>
                transport.ReadExactly(new byte[64], 0, 64, DmDeadline.Infinite, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
            Assert.Equal(1, channel.SendCalls);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void ExpiryBeforeControlSendAttemptLeavesTransactionActiveWithZeroBytes()
    {
        var (session, transaction) = ActiveTransaction();
        var time = new ManualTimeProvider();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl,
                DmDeadline.Start(TimeSpan.FromSeconds(30), time));
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            time.AdvanceMilliseconds(30000);
            Assert.Throws<TimeoutException>(() =>
                transport.SendAll([1, 2, 3], 0, 3, invocation.Deadline, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
            Assert.Equal(0, channel.SendCalls);
            Assert.Equal(0, channel.SentBytes);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void ExpiryAfterControlAttemptMakesOutcomeUnknownWithoutRetry()
    {
        var (session, transaction) = ActiveTransaction();
        var time = new ManualTimeProvider();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        DmTransportTestHooks.AfterControlAttemptMarked = (_, kind) =>
        {
            Assert.Equal(DmTransactionControlKind.Commit, kind);
            time.AdvanceMilliseconds(30000);
        };
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl,
                DmDeadline.Start(TimeSpan.FromSeconds(30), time));
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            Assert.Throws<TimeoutException>(() =>
                transport.SendAll([1, 2, 3], 0, 3, invocation.Deadline, 0));
            Assert.False(session.EndTransactionControl(invocation));
            Assert.Equal(DmTransactionOutcome.OutcomeUnknown, transaction.Outcome);
            Assert.Equal(1, channel.SendCalls);
            Assert.Equal(3, channel.SentBytes);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    [Fact]
    public void ValidatedAckKeepsCommittedOutcomeAfterLateClose()
    {
        var (session, transaction) = ActiveTransaction();
        var channel = new FakeChannel(FakeChannelMode.Full);
        using var transport = new DmTransport(channel);
        DmTransportTestHooks.Reset();
        try
        {
            using var lease = session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            session.BeginTransactionControl(transaction, invocation.Identity, DmTransactionControlKind.Commit);
            transport.SendAll([1, 2, 3], 0, 3, DmDeadline.Infinite, 0);
            session.ConfirmTransactionAck(invocation, DmTransactionControlKind.Commit);
            Assert.True(session.EndTransactionControl(invocation));
            session.MarkClosed();
            Assert.Equal(DmTransactionOutcome.Committed, transaction.Outcome);
            Assert.Equal(1, channel.SendCalls);
        }
        finally { DmTransportTestHooks.Reset(); }
    }

    private static (DmSession Session, DmTransaction Transaction) ActiveTransaction()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        session.SetTransactionState(DmLocalTransactionState.Starting);
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        using (var lease = session.BeginExecution(DmOperationPurpose.TransactionControl))
        using (var invocation = lease.BeginInvocation())
            session.ActivateTransaction(transaction, invocation.Identity);
        Assert.Equal(DmTransactionOutcome.Active, transaction.Outcome);
        return (session, transaction);
    }

    private enum FakeChannelMode { Full, OneByteThenThrow }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void AdvanceMilliseconds(long milliseconds) => timestamp += milliseconds;
    }

    private sealed class FakeChannel(FakeChannelMode mode) : IDmByteChannel
    {
        public int SendCalls { get; private set; }
        public int SentBytes { get; private set; }
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            SendCalls++;
            if (mode == FakeChannelMode.OneByteThenThrow && SendCalls == 2)
                throw new IOException("synthetic_partial_send_failure");
            int sent = mode == FakeChannelMode.OneByteThenThrow ? 1 : count;
            SentBytes += sent;
            return sent;
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => 0;
        public void Dispose() => IsClosed = true;
    }
}
