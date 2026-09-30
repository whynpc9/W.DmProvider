using System.Reflection;
using W.Dm;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengExecutionStrategyTests
{
    [Fact]
    public void EnableRetryOnFailurePreservesDefaultSettingsWithoutClassifyingErrorsAsReplaySafe()
    {
        using var context = CreateContext(options => options.EnableRetryOnFailure());
        var strategy = Assert.IsType<DamengRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        Assert.Equal(6, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(30), strategy.MaxRetryDelay);
        Assert.True(strategy.RetriesOnFailure);
    }

    [Fact]
    public void EmptyAdditionalNumbersPreserveCustomSettingsAndAreCopied()
    {
        var numbers = new List<int>();
        using var context = CreateContext(options => options.EnableRetryOnFailure(3, TimeSpan.FromSeconds(4), numbers));
        numbers.Add(-77777);
        var strategy = Assert.IsType<DamengRetryingExecutionStrategy>(context.Database.CreateExecutionStrategy());
        Assert.Equal(3, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(4), strategy.MaxRetryDelay);
        Assert.Empty(strategy.AdditionalErrorNumbers!);
    }

    [Theory]
    [InlineData(-3003)] [InlineData(-3404)] [InlineData(-6003)] [InlineData(-6004)] [InlineData(-6010)]
    [InlineData(6001)] [InlineData(6027)] [InlineData(6060)] [InlineData(6089)] [InlineData(6123)]
    [InlineData(-2007)] [InlineData(-1040)] [InlineData(-1210)] [InlineData(-3002)] [InlineData(-6011)] [InlineData(0)]
    public void UnclassifiedDriverErrorsAreNeverReplayed(int number)
    {
        using var context = CreateContext();
        var strategy = new DamengRetryingExecutionStrategy(context, 2, TimeSpan.Zero, null);
        var error = CreateError(number);
        int attempts = 0;
        Assert.Throws<DmException>(() => strategy.Execute(state: 42, (_, state) =>
        {
            attempts++;
            if (attempts == 1) throw error;
            return state;
        }, verifySucceeded: null));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void CommitOutcomeUnknownIsNeverReplayed()
    {
        using var context = CreateContext();
        var strategy = new DamengRetryingExecutionStrategy(context, 2, TimeSpan.Zero, []);
        var ctor = typeof(DmCommitOutcomeUnknownException).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Exception)], null)!;
        var error = (DmCommitOutcomeUnknownException)ctor.Invoke([new TimeoutException("synthetic timeout")]);
        int attempts = 0;
        Assert.Throws<DmCommitOutcomeUnknownException>(() => strategy.Execute(state: 42, (_, state) =>
        {
            attempts++;
            if (attempts == 1) throw error;
            return state;
        }, verifySucceeded: null));
        Assert.False(error.IsTransient);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(0)] [InlineData(-77777)] [InlineData(6001)] [InlineData(6089)]
    public void NonemptyNumberOverridesAreExplicitlyRejected(int number)
    {
        using var context = CreateContext();
        Assert.Throws<NotSupportedException>(() => new DamengRetryingExecutionStrategy(context, 2, TimeSpan.Zero, [number]));
        Assert.Throws<NotSupportedException>(() => CreateContext(options => options.EnableRetryOnFailure([number])));
        Assert.Throws<NotSupportedException>(() => CreateContext(options => options.EnableRetryOnFailure(2, TimeSpan.Zero, [number])));
    }

    [Fact]
    public void CollectionOverloadRejectsNull()
    {
        var builder = new DbContextOptionsBuilder<TestContext>();
        Assert.Throws<ArgumentNullException>(() => builder.UseDameng("Server=localhost;Port=5236;User Id=test;PWD=test",
            options => options.EnableRetryOnFailure((ICollection<int>)null!)));
    }

    private static TestContext CreateContext(Action<Microsoft.EntityFrameworkCore.Infrastructure.DamengDbContextOptionsBuilder>? configure = null)
        => new(new DbContextOptionsBuilder<TestContext>().UseDameng("Server=localhost;Port=5236;User Id=test;PWD=test", configure).Options);
    private static DmException CreateError(int number)
    {
        var error = typeof(DmError).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(int), typeof(string)], null)!
            .Invoke([number, "synthetic error"]);
        return (DmException)typeof(DmException).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(DmError)], null)!.Invoke([error]);
    }
    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
}
