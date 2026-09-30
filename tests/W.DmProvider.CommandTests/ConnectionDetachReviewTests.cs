using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.CommandTests;

public sealed class ConnectionDetachReviewTests
{
    [Fact]
    public void PublicNullAssignmentDetachesAndEveryExecutionEntryRejectsWithoutWire()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("SELECT :p0 FROM DUAL", connection);
        var parameter = new DmParameter("p0", 17);
        command.Parameters.Add(parameter);
        command.SetStmtSerial(3);
        long sends = DmWireTestHooks.SendCount;

        ((DbCommand)command).Connection = null;

        Assert.Null(command.Connection);
        Assert.False(command.GetStmtSerial());
        Assert.Same(parameter, command.Parameters[0]);
        Assert.Equal("SELECT :p0 FROM DUAL", command.CommandText);
        Assert.Null(command.filterHead);
        Assert.Null(command.Parameters.filterHead);
        Assert.Throws<InvalidOperationException>(() => command.Prepare());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Fact]
    public void RebindPreservesCommandAndParametersButResetsConnectionScopedState()
    {
        using var first = new DmConnection();
        using var second = new DmConnection();
        using var command = new DmCommand("SELECT :p0 FROM DUAL", first);
        command.CommandTimeout = 7;
        var parameter = new DmParameter("p0", 17);
        command.Parameters.Add(parameter);
        command.SetStmtSerial(3);
        // A closed statement fixture models stale state after a connection closes.
        // Server PREPARE / STMT_CLOSE and fresh-handle behavior require the real-db probe.
        var closed = BareStatement(command);
        closed.o();
        command.Statement = closed;
        command.CurResultSetCache = new DmResultSetCache(closed, 1, 2);
        command.RetCmdType = 129;
        SetField(command, "preparedMetadata", new[] { DmParameterMetadata.From(parameter) });
        SetField(command, "statementConnection", first);
        SetField(command, "statementSession", new DmSession());
        SetField(command, "executeId", 42L);

        command.Connection = second;

        Assert.Same(second, command.Connection);
        Assert.Null(command.Statement);
        Assert.Null(GetField(command, "preparedMetadata"));
        Assert.Null(GetField(command, "statementConnection"));
        Assert.Null(GetField(command, "statementSession"));
        Assert.Null(command.CurResultSetCache);
        Assert.Equal(0, command.RetCmdType);
        Assert.Equal(-1L, command.GetExecuteId());
        Assert.False(command.GetStmtSerial());
        Assert.Equal(7, command.CommandTimeout);
        Assert.Equal("SELECT :p0 FROM DUAL", command.CommandText);
        Assert.Same(parameter, command.Parameters[0]);
        Assert.Null(command.filterHead);
        Assert.Null(command.Parameters.filterHead);
        command.Connection = null;
        Assert.Null(command.Connection);
    }

    [Fact]
    public async Task CapturedExecutionRejectsDetachAndRebindUntilItsPlanIsReleased()
    {
        using var original = new DmConnection();
        using var replacement = new DmConnection();
        using var command = new DmCommand("SELECT 1 FROM DUAL", original);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        DmCommand.AfterPlanCaptured = () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("detach_plan_barrier");
        };
        Task<Exception?>? executing = null;
        try
        {
            executing = Task.Run(() => Record.Exception(() => command.ExecuteNonQuery()));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.Throws<InvalidOperationException>(() => command.Connection = null);
            Assert.Throws<InvalidOperationException>(() => command.Connection = replacement);
            Assert.Same(original, command.Connection);
            release.Set();
            Assert.IsType<InvalidOperationException>(await executing.WaitAsync(TimeSpan.FromSeconds(3)));
            DmCommand.AfterPlanCaptured = null;
            command.Connection = null;
            Assert.Null(command.Connection);
        }
        finally
        {
            release.Set();
            DmCommand.AfterPlanCaptured = null;
            if (executing != null) await executing.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void ActiveTransactionRejectsConnectionChangeAndExplicitClearResetsSerial()
    {
        using var original = new DmConnection();
        using var replacement = new DmConnection();
        using var command = new DmCommand("SELECT 1 FROM DUAL", original);
        using var control = new DmCommand();
        var controlStatement = BareStatement(control);
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        SetField(transaction, "outcome", (int)DmTransactionOutcome.Active);
        transaction.SetStmtSerial(3);
        transaction.Stmt = controlStatement;
        command.Transaction = transaction;

        Assert.True(command.GetStmtSerial());
        Assert.Throws<InvalidOperationException>(() => command.Connection = null);
        Assert.Throws<InvalidOperationException>(() => command.Connection = replacement);
        Assert.Same(original, command.Connection);
        Assert.Same(transaction, command.Transaction);
        command.Transaction = null;
        Assert.False(command.GetStmtSerial());
        command.Connection = null;
        Assert.Null(command.Connection);
        Assert.True(transaction.Valid);
        Assert.Same(controlStatement, transaction.Stmt);
        Assert.Same(control, controlStatement.f());
        Assert.False(controlStatement.P());
    }

    [Fact]
    public void DetachClearsCompletedTransactionWithoutReleasingItsControlStatement()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("SELECT 1 FROM DUAL", connection);
        using var control = new DmCommand();
        var controlStatement = BareStatement(control);
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        SetField(transaction, "outcome", (int)DmTransactionOutcome.Committed);
        transaction.Stmt = controlStatement;
        command.Transaction = transaction;

        command.Connection = null;

        Assert.Null(command.Transaction);
        Assert.Same(controlStatement, transaction.Stmt);
        Assert.False(controlStatement.P());
    }

    [Fact]
    public void ForeignLiveStatementCannotBeReleasedByConnectionAssignment()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("SELECT 1 FROM DUAL", connection);
        using var control = new DmCommand();
        var foreign = BareStatement(control);
        command.Statement = foreign;
        try
        {
            Assert.Throws<InvalidOperationException>(() => command.Connection = null);
            Assert.Same(connection, command.Connection);
            Assert.Same(foreign, command.Statement);
            Assert.Same(control, foreign.f());
            Assert.False(foreign.P());
        }
        finally { command.Statement = null!; }
    }

    [Fact]
    public void BusyOriginalSessionRejectsDetachWithoutClearingStatementOrBreakingOtherOwner()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("SELECT 1 FROM DUAL", connection);
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        var instance = (DmConnInstance)RuntimeHelpers.GetUninitializedObject(typeof(DmConnInstance));
        SetField(instance, "<Session>k__BackingField", session);
        var statement = BareStatement(command);
        SetField(statement, "__t02_field_04000923", instance);
        command.Statement = statement;
        SetField(command, "statementSession", session);
        SetField(command, "statementConnection", connection);
        // A real session lease supplies the exclusion boundary without opening a socket.
        // The integration probe separately verifies a live reader remains readable.
        using var otherOwner = session.BeginExecution(DmOperationPurpose.Query, DmDeadline.Infinite);
        try
        {
            Assert.Throws<InvalidOperationException>(() => command.Connection = null);
            Assert.Same(connection, command.Connection);
            Assert.Same(statement, command.Statement);
            Assert.Equal(DmPhysicalSessionState.Busy, session.State);
            Assert.False(statement.P());
            using var invocation = otherOwner.BeginInvocation();
            Assert.Same(session, DmInvocation.Current!.Lease.Session);
        }
        finally { command.Statement = null!; }
    }

    private static Statement BareStatement(DmCommand owner)
    {
        var statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
        statement.__t02_method_0600089A(owner);
        return statement;
    }

    private static object? GetField(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
