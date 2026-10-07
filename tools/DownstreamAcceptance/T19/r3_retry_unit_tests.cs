using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using W.Dm;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

// Candidate exception and real EF strategy; synthetic injection, not driver->EF network-fault E2E.
[Trait("Category", "R3Unit")]
[Trait("Feature", "T19SyntheticDownstreamUnit")]
public sealed class R3ExecutionStrategyTests
{
    [Fact]
    public void CandidateUnknownCommitIsPropagatedUnchangedAfterOneSynchronousStrategyAttempt()
    {
        using var context = Context();
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.True(strategy.RetriesOnFailure);
        DmCommitOutcomeUnknownException unknown = Unknown();
        Assert.False(unknown.IsTransient);
        int attempts = 0;
        var actual = Assert.Throws<DmCommitOutcomeUnknownException>(() => strategy.Execute(state: 42,
            operation: (_, state) => { attempts++; if (attempts == 1) throw unknown; return state; }, verifySucceeded: null));
        Assert.Same(unknown, actual); Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task CandidateUnknownCommitIsPropagatedUnchangedAfterOneAsynchronousStrategyAttempt()
    {
        await using var context = Context();
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.True(strategy.RetriesOnFailure);
        DmCommitOutcomeUnknownException unknown = Unknown();
        Assert.False(unknown.IsTransient);
        int attempts = 0;
        var actual = await Assert.ThrowsAsync<DmCommitOutcomeUnknownException>(() => strategy.ExecuteAsync(state: 42,
            operation: (_, state, _) => { attempts++; return attempts == 1 ? Task.FromException<int>(unknown) : Task.FromResult(state); }, verifySucceeded: null));
        Assert.Same(unknown, actual); Assert.Equal(1, attempts);
    }

    [Fact]
    public void AdditionalErrorNumbersCannotOverrideTheCandidateNoReplayBoundary()
    {
        Assert.Throws<NotSupportedException>(() => Context([0]));
        Assert.Throws<NotSupportedException>(() => Context([-77777]));
    }

    private static TestContext Context(IEnumerable<int>? numbers = null) => new(new DbContextOptionsBuilder<TestContext>()
        .UseDameng("Server=localhost;Port=5236;User Id=SYNTHETIC_T19;PWD=SYNTHETIC_ONLY",
            settings => settings.EnableRetryOnFailure(2, TimeSpan.Zero, numbers ?? []))
        .Options);
    private static DmCommitOutcomeUnknownException Unknown()
    {
        var constructor = typeof(DmCommitOutcomeUnknownException).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Exception)], null) ?? throw new InvalidOperationException("T19_UNIT_UNKNOWN_CONSTRUCTOR_REQUIRED");
        return (DmCommitOutcomeUnknownException)constructor.Invoke([new TimeoutException("T19_SYNTHETIC_CAUSE")]);
    }
    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
}
