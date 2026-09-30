using System.Runtime.CompilerServices;
using System.Text.Json;
using W.Dm;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.IsolationTests;

public sealed class StatementOwnershipTests
{
    [Fact]
    public void TransactionAssignmentDoesNotAdoptItsForeignControlStatement()
    {
        using var control = new DmCommand();
        using var caller = new DmCommand("SELECT 1 FROM DUAL");
        var foreign = BareStatement(control);
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        transaction.Stmt = foreign;
        caller.Transaction = transaction;
        Assert.Null(caller.Statement);
        Assert.Same(foreign, transaction.Stmt);
        Assert.Same(control, foreign.f());
        Assert.Equal("SELECT 1 FROM DUAL", caller.CommandText);
    }

    [Fact]
    public void ChangingTransactionPreservesTheCallersOwnPreparedStatement()
    {
        using var caller = new DmCommand("SELECT 1 FROM DUAL");
        var owned = BareStatement(caller);
        owned.__t02_method_0600088A(true);
        caller.Statement = owned;
        try
        {
            var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
            caller.Transaction = transaction;
            Assert.Same(owned, caller.Statement);
            Assert.True(owned.b());
            Assert.Same(caller, owned.f());
        }
        finally { caller.Statement = null!; }
    }

    [Fact]
    public void ForeignLiveStatementIsRejectedBeforeAnySessionOrWireAccess()
    {
        using var control = new DmCommand();
        using var caller = new DmCommand("SELECT 1 FROM DUAL");
        var foreign = BareStatement(control);
        caller.Statement = foreign;
        try
        {
            Assert.Throws<InvalidOperationException>(caller.ValidateStatementOwnership);
            Assert.Same(foreign, caller.Statement);
            Assert.Same(control, foreign.f());
        }
        finally { caller.Statement = null!; }
    }

    [Fact]
    public void FailureDiagnosticDoesNotSerializeMessagesOrExceptionData()
    {
        var error = new InvalidOperationException("synthetic_password_not_for_output",
            new IOException("synthetic_connection_string_not_for_output"));
        error.Data["secret"] = "synthetic_password_not_for_output";
        string output = JsonSerializer.Serialize(ProbeSafety.Failure(error));
        Assert.DoesNotContain("synthetic_password_not_for_output", output, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_connection_string_not_for_output", output, StringComparison.Ordinal);
        Assert.Contains(typeof(IOException).FullName!, output, StringComparison.Ordinal);
    }

    private static Statement BareStatement(DmCommand owner)
    {
        // This fixture needs only owner and live/disposed state. It creates no socket or physical session.
        var statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
        statement.__t02_method_0600089A(owner);
        return statement;
    }
}
