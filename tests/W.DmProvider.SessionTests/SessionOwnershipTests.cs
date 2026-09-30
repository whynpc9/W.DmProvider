using System.Collections.Concurrent;
using System.Text.Json;
using W.Dm.Internal.Sessions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace W.DmProvider.SessionTests;

public sealed class SessionOwnershipTests
{
    private static DmSession Ready()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        return session;
    }

    private static JsonDocument Fixture() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenarios.json")));

    [Fact]
    public void OneExecutionAndOneChildInvocationOwnTheSession()
    {
        var session = Ready();
        using var lease = session.BeginExecution(DmOperationPurpose.Reader);
        Assert.Equal(DmPhysicalSessionState.Busy, session.State);
        Assert.Throws<InvalidOperationException>(() => session.BeginExecution(DmOperationPurpose.Query));
        Assert.Throws<InvalidOperationException>(() => session.BeginExecution(DmOperationPurpose.TransactionControl));
        using (var invocation = lease.BeginInvocation())
        {
            Assert.Throws<InvalidOperationException>(() => lease.BeginInvocation());
            using var exchange = session.BeginWireExchange();
            Assert.Throws<InvalidOperationException>(() => session.BeginWireExchange());
            exchange.Complete();
        }
        using (var next = lease.BeginInvocation())
        {
            using var exchange = session.BeginWireExchange();
            exchange.Complete();
        }
        Assert.Equal(DmPhysicalSessionState.Busy, session.State);
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(DmPhysicalSessionState.Ready, session.State);
        using var following = session.BeginExecution(DmOperationPurpose.Query);
        Assert.NotEqual(lease.Identity.ExecutionId, following.Identity.ExecutionId);
    }

    [Fact]
    public void FailedWireExchangeCannotReturnBrokenSessionToReady()
    {
        var session = Ready();
        using var lease = session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        using (session.BeginWireExchange()) { /* Failure before Complete. */ }
        Assert.Equal(DmPhysicalSessionState.Broken, session.State);
        Assert.Throws<InvalidOperationException>(() => session.BeginExecution(DmOperationPurpose.Query));
        Assert.Throws<InvalidOperationException>(() => session.BeginWireExchange());
        invocation.Dispose();
        lease.Dispose();
        Assert.Equal(DmPhysicalSessionState.Broken, session.State);
    }

    [Fact]
    public void AllFourStaleIdentityComponentsAreRejected()
    {
        using var fixture = Fixture();
        var scenario = fixture.RootElement.GetProperty("offline_scenarios").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == "stale_identity_abort_cannot_abort_new_operation");
        var components = scenario.GetProperty("stale_abort_vectors").EnumerateArray()
            .Select(v => v.GetProperty("stale_component").GetString()).ToArray();
        Assert.Equal(new[] { "session_id", "lease_generation", "execution_id", "invocation_id" }, components);
        var session = Ready();
        using var lease = session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var valid = invocation.Identity;
        var stale = components.Select(component => component switch
        {
            "session_id" => valid with { SessionId = valid.SessionId + 1 },
            "lease_generation" => valid with { LeaseGeneration = valid.LeaseGeneration + 1 },
            "execution_id" => valid with { ExecutionId = valid.ExecutionId + 1 },
            "invocation_id" => valid with { InvocationId = valid.InvocationId + 1 },
            _ => throw new InvalidDataException("Unknown identity vector")
        });
        foreach (var identity in stale)
        {
            Assert.Null(session.Detach(identity));
            Assert.Equal(DmPhysicalSessionState.Busy, session.State);
            session.RequireWireOwnership(invocation);
        }
        using var exchange = session.BeginWireExchange();
        exchange.Complete();
    }

    [Fact]
    public void OldAbortCannotBreakASeparateNewSession()
    {
        var old = Ready();
        var current = Ready();
        using var oldLease = old.BeginExecution(DmOperationPurpose.Query);
        using var oldInvocation = oldLease.BeginInvocation();
        using var currentLease = current.BeginExecution(DmOperationPurpose.Query);
        using var currentInvocation = currentLease.BeginInvocation();
        Assert.Null(current.Detach(oldInvocation.Identity));
        Assert.Equal(DmPhysicalSessionState.Busy, current.State);
        using var currentExchange = current.BeginWireExchange();
        currentExchange.Complete();
    }

    [Fact]
    public void IncompleteExchangeCannotBeHiddenByDisposingItsOwner()
    {
        var session = Ready();
        var oldLease = session.BeginExecution(DmOperationPurpose.Query);
        var oldInvocation = oldLease.BeginInvocation();
        var oldExchange = session.BeginWireExchange();
        oldInvocation.Dispose();
        oldLease.Dispose();
        Assert.Equal(DmPhysicalSessionState.Broken, session.State);
        oldExchange.Dispose();
        Assert.Equal(DmPhysicalSessionState.Broken, session.State);
        Assert.Throws<InvalidOperationException>(() => session.BeginExecution(DmOperationPurpose.Query));
    }

    [Fact]
    public void OldInvocationAbortCannotBreakNextExecutionOfSameSession()
    {
        var session = Ready();
        OperationIdentity oldIdentity;
        using (var oldLease = session.BeginExecution(DmOperationPurpose.Query))
        using (var oldInvocation = oldLease.BeginInvocation())
        {
            oldIdentity = oldInvocation.Identity;
            using var oldExchange = session.BeginWireExchange();
            oldExchange.Complete();
        }
        using var newLease = session.BeginExecution(DmOperationPurpose.Query);
        using var newInvocation = newLease.BeginInvocation();
        Assert.Null(session.Detach(oldIdentity));
        Assert.Equal(DmPhysicalSessionState.Busy, session.State);
        using var newExchange = session.BeginWireExchange();
        newExchange.Complete();
    }

    [Fact]
    public void WireRequiresBothCurrentInvocationAndCompleteExchangeScope()
    {
        var session = Ready();
        Assert.Throws<InvalidOperationException>(() => session.BeginWireExchange());
        Assert.Throws<InvalidOperationException>(() => session.RequireActiveWireExchange());
        using var lease = session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        Assert.Throws<InvalidOperationException>(() => session.RequireActiveWireExchange());
        using (var exchange = session.BeginWireExchange())
        {
            session.RequireActiveWireExchange();
            exchange.Complete();
        }
        Assert.Throws<InvalidOperationException>(() => session.RequireActiveWireExchange());
    }

    [Fact]
    public void CounterOverflowBreaksSessionWithoutWrappingIdentity()
    {
        using var fixture = Fixture();
        var scenario = fixture.RootElement.GetProperty("offline_scenarios").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == "checked_identity_overflow_fails_closed");
        long max = long.Parse(scenario.GetProperty("max_value_decimal").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(long.MaxValue, max);
        var execution = Ready();
        execution.SeedCountersForTests(max, 0, 1);
        Assert.Throws<InvalidOperationException>(() => execution.BeginExecution(DmOperationPurpose.Query));
        Assert.Equal(DmPhysicalSessionState.Broken, execution.State);

        var invocation = Ready();
        invocation.SeedCountersForTests(0, max, 1);
        using var lease = invocation.BeginExecution(DmOperationPurpose.Query);
        Assert.Throws<InvalidOperationException>(() => lease.BeginInvocation());
        Assert.Equal(DmPhysicalSessionState.Broken, invocation.State);
    }

    [Fact]
    public void SessionIdOverflowRejectsCreation()
    {
        long previous = new DmSession().SessionId;
        try
        {
            DmSession.SeedSessionCounterForTests(long.MaxValue);
            Assert.Throws<InvalidOperationException>(() => new DmSession());
        }
        finally { DmSession.SeedSessionCounterForTests(previous); }
    }

    [Fact]
    public async Task IndependentSessionsReachBarrierTogether()
    {
        var first = Ready();
        var second = Ready();
        using var bothEntered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var entered = new ConcurrentDictionary<long, byte>();
        DmSessionTestHooks.AfterExecutionAcquired = identity =>
        {
            if (entered.TryAdd(identity.SessionId, 0)) bothEntered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Independent-session barrier did not release.");
        };
        try
        {
            var a = Task.Run(() => { using var lease = first.BeginExecution(DmOperationPurpose.Query); return lease.Identity; });
            var b = Task.Run(() => { using var lease = second.BeginExecution(DmOperationPurpose.Query); return lease.Identity; });
            Assert.True(bothEntered.Wait(TimeSpan.FromSeconds(10)), "A process-wide lock serialized independent sessions.");
            release.Set();
            var identities = await Task.WhenAll(a, b);
            Assert.NotEqual(identities[0].SessionId, identities[1].SessionId);
            Assert.Equal(DmPhysicalSessionState.Ready, first.State);
            Assert.Equal(DmPhysicalSessionState.Ready, second.State);
        }
        finally
        {
            release.Set();
            DmSessionTestHooks.AfterExecutionAcquired = null;
        }
    }
}
