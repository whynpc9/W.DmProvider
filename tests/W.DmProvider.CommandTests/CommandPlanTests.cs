using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class CommandPlanTests
{
    [Fact]
    public async Task CapturedPlanRejectsConcurrentMutationAndCopiesBinaryParameter()
    {
        using var command = new DmCommand("SELECT :p0 FROM DUAL");
        byte[] supplied = [1, 2, 3, 4];
        var parameter = new DmParameter("p0", supplied);
        command.Parameters.Add(parameter);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        DmCommand.AfterPlanCaptured = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("plan_barrier");
        };
        try
        {
            Task<Exception?> executing = Task.Run(() =>
            {
                try { command.ExecuteNonQuery(); return null; }
                catch (Exception error) { return error; }
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.Throws<InvalidOperationException>(() => command.CommandText = "SELECT 2 FROM DUAL");
            Assert.Throws<InvalidOperationException>(() => parameter.Value = new byte[] { 9 });
            Assert.Throws<InvalidOperationException>(() => command.Parameters.Add(new DmParameter("p1", 9)));
            Assert.Throws<InvalidOperationException>(() =>
                ((DmParameterCollection)command.Parameters).ChangeName(parameter, "p0", "blocked"));
            supplied[0] = 99; // caller still owns its original buffer
            var plan = (DmCommandPlan?)typeof(DmCommand).GetField("activePlan",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(command);
            Assert.NotNull(plan);
            Assert.Equal("SELECT :p0 FROM DUAL", plan.Sql);
            Assert.True(plan.Parameters[0].Value is byte[] snapshot && snapshot[0] == 1);
            release.Set();
            Assert.NotNull(await executing.WaitAsync(TimeSpan.FromSeconds(3))); // closed connection, no network
            Assert.Equal("SELECT :p0 FROM DUAL", command.CommandText);
        }
        finally
        {
            release.Set();
            DmCommand.AfterPlanCaptured = null;
        }
    }

    [Fact]
    public void UnsupportedSchemaOnlyKeyInfoCombinationRejectsBeforeConnectionUse()
    {
        using var command = new DmCommand("INSERT INTO any_table VALUES (1)");
        Assert.Throws<NotSupportedException>(() => command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo));
        Assert.Equal("INSERT INTO any_table VALUES (1)", command.CommandText);
    }

    [Fact]
    public void PublicChangeNameRejectsForgedOldNameWithoutCorruptingIndex()
    {
        using var command = new DmCommand("SELECT :p0, :p1 FROM DUAL");
        var first = new DmParameter("p0", 1);
        var second = new DmParameter("p1", 2);
        var parameters = (DmParameterCollection)command.Parameters;
        parameters.Add(first);
        parameters.Add(second);
        Assert.Throws<ArgumentException>(() => parameters.ChangeName(first, "p1", "forged"));
        Assert.Equal("p0", first.ParameterName);
        Assert.Equal("p1", second.ParameterName);
        Assert.True(parameters.Contains("p0"));
        Assert.True(parameters.Contains("p1"));
        Assert.False(parameters.Contains("forged"));
        parameters.ChangeName(first, "p0", "renamed");
        Assert.Equal("renamed", first.ParameterName);
        Assert.False(parameters.Contains("p0"));
        Assert.True(parameters.Contains("renamed"));
        Assert.True(parameters.Contains("p1"));
    }

    [Fact]
    public async Task DisposeWhilePlanIsCapturedRejectsExecutionAndReleasesGateWithoutWire()
    {
        using var command = new DmCommand("SELECT 1 FROM DUAL");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        long sends = DmWireTestHooks.SendCount;
        DmCommand.AfterPlanCaptured = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("dispose_barrier");
        };
        try
        {
            Task<Exception?> executing = Task.Run(() =>
            {
                try { command.ExecuteNonQuery(); return null; }
                catch (Exception error) { return error; }
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            command.Dispose();
            release.Set();
            Exception? failure = await executing.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(failure is ObjectDisposedException or InvalidOperationException);
            var active = typeof(DmCommand).GetField("activePlan",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(command);
            var gate = (DmCommandPlanGate?)typeof(DmCommand).GetField("commandPlanGate",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(command);
            Assert.Null(active);
            Assert.NotNull(gate);
            Assert.False(gate.IsExecuting);
            Assert.Equal(sends, DmWireTestHooks.SendCount);
        }
        finally
        {
            release.Set();
            DmCommand.AfterPlanCaptured = null;
        }
    }
}
