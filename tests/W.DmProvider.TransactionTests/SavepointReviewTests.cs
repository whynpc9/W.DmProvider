using System.Data;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TransactionTests;

public sealed class SavepointReviewTests
{
    [Theory]
    [InlineData("SAVEPOINT WSP_1")]
    [InlineData("SAVEPOINT \"WSP_1\"")]
    [InlineData("SAVEPOINT wSp_prediction")]
    [InlineData("/* prefix */ savepoint /* separator */ \"WSP_2\";")]
    [InlineData("ROLLBACK")]
    [InlineData("ROLLBACK WORK")]
    [InlineData("ROLLBACK TO WSP_1")]
    [InlineData("ROLLBACK TO SAVEPOINT \"WSP_1\"")]
    [InlineData("ROLLBACK TO /* target */ \"wsp_2\" -- tail comment")]
    [InlineData("RELEASE SAVEPOINT WSP_1")]
    [InlineData("RELEASE SAVEPOINT \"WSP_2\"")]
    [InlineData("RELEASE user_owned extra")]
    [InlineData("RELEASE user_owned")]
    [InlineData("RELEASE WORK")]
    [InlineData("SELECT 'SAVEPOINT WSP_1' FROM DUAL; SAVEPOINT WSP_1")]
    [InlineData("SELECT 1 FROM DUAL; /* separator */ ROLLBACK TO SAVEPOINT WSP_1")]
    [InlineData("SELECT 1 FROM DUAL; RELEASE SAVEPOINT WSP_1")]
    [InlineData("ROLLBACK TO WSP_1 /* /* */ ; COMMIT -- */")]
    public void EveryPublicExecutionEntryRejectsReservedOrNonStrictControlBeforeLeaseOrSend(string sql)
    {
        using var fixture = new ActiveTransactionFixture();
        using var command = new DmCommand(sql, fixture.Connection) { Transaction = fixture.Transaction };
        long sends = DmWireTestHooks.SendCount;
        long bytes = DmWireTestHooks.SentBytes;
        int acquiredLeases = 0;
        DmSessionTestHooks.AfterExecutionAcquired = _ => acquiredLeases++;
        try
        {
            Assert.Throws<NotSupportedException>(() => command.Prepare());
            Assert.Throws<NotSupportedException>(() => command.ExecuteNonQuery());
            Assert.Throws<NotSupportedException>(() => command.ExecuteScalar());
            Assert.Throws<NotSupportedException>(() => command.ExecuteReader());
        }
        finally { DmSessionTestHooks.AfterExecutionAcquired = null; }

        Assert.Equal(0, acquiredLeases);
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Equal(bytes, DmWireTestHooks.SentBytes);
        Assert.Null(command.Statement);
        Assert.Equal(sql, command.CommandText);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmLocalTransactionState.Active, fixture.Session.TransactionState);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
        Assert.Same(fixture.Transaction, fixture.Session.ActiveTransaction);
    }

    [Theory]
    [InlineData("SELECT 'SAVEPOINT WSP_1; ROLLBACK TO WSP_2' FROM DUAL")]
    [InlineData("SELECT \"SAVEPOINT\", \"ROLLBACK\", \"RELEASE\" FROM DUAL")]
    [InlineData("/* SAVEPOINT WSP_1 */ SELECT 1 FROM DUAL -- RELEASE SAVEPOINT WSP_2")]
    [InlineData("SELECT 1 FROM DUAL; /* ROLLBACK TO WSP_1 */ SELECT 2 FROM DUAL")]
    [InlineData("SELECT 1 FROM DUAL /* /* */ -- ROLLBACK TO WSP_1")]
    public void LiteralsIdentifiersCommentsAndReadOnlyCompoundSqlRemainAllowedByTheGate(string sql)
    {
        using var fixture = new ActiveTransactionFixture();
        DmTransaction.ValidateCommandBinding(fixture.Connection, fixture.Transaction);
        DmTransaction.ValidateCommandSql(fixture.Connection, fixture.Transaction, sql);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
    }

    [Theory]
    [InlineData("COMMIT")]
    [InlineData("SELECT 1 FROM DUAL; COMMIT")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE")]
    [InlineData("SET AUTOCOMMIT ON")]
    [InlineData("CALL control_proc()")]
    [InlineData("BEGIN SAVEPOINT WSP_1; END;")]
    public void ExistingTransactionAndDynamicControlFenceRemainsActive(string sql)
    {
        using var fixture = new ActiveTransactionFixture();
        Assert.Throws<NotSupportedException>(() =>
            DmTransaction.ValidateCommandSql(fixture.Connection, fixture.Transaction, sql));
    }

    [Theory]
    [InlineData("SAVEPOINT EF_save", (int)DmSqlSavepointKind.Save, "EF_save", false)]
    [InlineData("/* prefix */ SAVEPOINT \"__EFSavePoint\"; -- tail", (int)DmSqlSavepointKind.Save, "__EFSavePoint", true)]
    [InlineData("ROLLBACK TO EF_save", (int)DmSqlSavepointKind.Rollback, "EF_save", false)]
    [InlineData("ROLLBACK /* trivia */ TO SAVEPOINT \"__EFSavePoint\"", (int)DmSqlSavepointKind.Rollback, "__EFSavePoint", true)]
    [InlineData("RELEASE SAVEPOINT \"__EFSavePoint\"", (int)DmSqlSavepointKind.Release, "__EFSavePoint", true)]
    [InlineData("RELEASE SAVEPOINT EF_save;", (int)DmSqlSavepointKind.Release, "EF_save", false)]
    [InlineData("SAVEPOINT \"name\"\";/*literal*/\"", (int)DmSqlSavepointKind.Save, "name\";/*literal*/", true)]
    [InlineData("ROLLBACK TO EF_save /* /* */", (int)DmSqlSavepointKind.Rollback, "EF_save", false)]
    public void StrictUserOwnedSavepointSqlRemainsAllowedWithoutRewritingOrRecordingPreflight(
        string sql, int kind, string name, bool quoted)
    {
        using var fixture = new ActiveTransactionFixture();
        Assert.True(DmParameterBinding.TryParseSavepointControl(sql, out var control));
        Assert.Equal((DmSqlSavepointKind)kind, control.Kind);
        Assert.Equal(name, control.Name);
        Assert.Equal(quoted, control.IsQuoted);
        using var command = new DmCommand(sql, fixture.Connection) { Transaction = fixture.Transaction };
        DmTransaction.ValidateCommandSql(fixture.Connection, fixture.Transaction, command.CommandText);
        Assert.Equal(sql, command.CommandText);
        Assert.Empty(fixture.Entries);
    }

    [Theory]
    [InlineData("SAVEPOINT")]
    [InlineData("SAVEPOINT \"\"")]
    [InlineData("SAVEPOINT :p0")]
    [InlineData("SAVEPOINT [name]")]
    [InlineData("SAVEPOINT \"unterminated")]
    [InlineData("RELEASE SAVEPOINT")]
    [InlineData("RELEASE EF_save")]
    [InlineData("RELEASE WORK")]
    [InlineData("ROLLBACK")]
    [InlineData("ROLLBACK TO SAVEPOINT")]
    [InlineData("ROLLBACK TO point extra")]
    [InlineData("ROLLBACK TO point /* /* */ ; COMMIT -- */")]
    [InlineData("SAVEPOINT point; SELECT 1 FROM DUAL")]
    [InlineData("SAVEPOINT point;;")]
    [InlineData("SAVEPOINT point /* unterminated")]
    [InlineData("SAVEPOINT \"bad\0name\"")]
    public void MalformedOrCompoundControlCannotBecomeAnAllowedSingleStatement(string sql)
        => Assert.False(DmParameterBinding.TryParseSavepointControl(sql, out _));

    [Fact]
    public void ParameterBoundControlIsRejectedEvenWhenItsSqlHasOneLiteralIdentifier()
    {
        using var fixture = new ActiveTransactionFixture();
        Assert.Throws<NotSupportedException>(() => DmTransaction.ValidateCommandSql(
            fixture.Connection, fixture.Transaction, "SAVEPOINT EF_save", hasBoundParameters: true));
        Assert.Empty(fixture.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("8.1.5.600")]
    [InlineData("9.0.0.0")]
    public void UserOwnedRawSavepointsRequireTheSameVerifiedProfileAsDriverApis(string? version)
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.Connection.ConnProperty.ServerVersion = version!;
        foreach (string sql in new[] { "SAVEPOINT EF_save", "ROLLBACK TO EF_save", "RELEASE SAVEPOINT EF_save" })
            Assert.Throws<NotSupportedException>(() =>
                DmTransaction.ValidateCommandSql(fixture.Connection, fixture.Transaction, sql));
        Assert.Empty(fixture.Entries);
    }

    [Fact]
    public void LaterEfRawSaveRollbackReleasePreserveEarlierApiPoint()
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.SeedApiPoint("api_earlier", "WSP_1");
        fixture.Confirm("SAVEPOINT \"__EFSavePoint\"");
        fixture.Confirm("ROLLBACK TO SAVEPOINT \"__EFSavePoint\"");
        fixture.Confirm("RELEASE SAVEPOINT \"__EFSavePoint\"");
        Assert.Equal(new[] { "api_earlier" }, fixture.ApiNames());
        Assert.Empty(fixture.RawNames());
    }

    [Theory]
    [InlineData("ROLLBACK TO SAVEPOINT \"raw_early\"", true)]
    [InlineData("RELEASE SAVEPOINT \"raw_early\"", false)]
    public void EarlierRawDestructiveControlInvalidatesLaterApiAndRawPoints(string sql, bool retainsTarget)
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.Confirm("SAVEPOINT \"raw_early\"");
        fixture.SeedApiPoint("api_later", "WSP_1");
        fixture.Confirm("SAVEPOINT \"raw_later\"");
        fixture.Confirm(sql);
        Assert.Empty(fixture.ApiNames());
        Assert.Equal(retainsTarget ? new[] { "raw_early" } : Array.Empty<string>(), fixture.RawNames());
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Rollback("api_later"));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Fact]
    public void RawNameEqualToApiUserNameDoesNotExposeApiServerPoint()
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.SeedApiPoint("same_name", "WSP_1");
        fixture.Confirm("SAVEPOINT same_name");
        fixture.Confirm("RELEASE SAVEPOINT same_name");
        Assert.Equal(new[] { "same_name" }, fixture.ApiNames());
        Assert.Empty(fixture.RawNames());
        fixture.Confirm("SAVEPOINT raw_only");
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Rollback("raw_only"));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Theory]
    [InlineData("SAVEPOINT \"Alias\"", "ROLLBACK TO \"ALIAS\"")]
    [InlineData("SAVEPOINT Alias", "ROLLBACK TO \"Alias\"")]
    [InlineData("SAVEPOINT \"known\"", "RELEASE SAVEPOINT \"untracked\"")]
    public void SuccessfulUnknownOrAmbiguousTargetConservativelyInvalidatesApiPoints(string save, string control)
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.Confirm(save);
        fixture.SeedApiPoint("possibly_removed", "WSP_1");
        fixture.Confirm(control);
        Assert.Empty(fixture.Entries);
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Release("possibly_removed"));
    }

    [Fact]
    public void MultiplePlausibleCaseAliasesCannotRetainPotentiallyStaleApiPoints()
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.Confirm("SAVEPOINT \"Alias\"");
        fixture.SeedApiPoint("middle", "WSP_1");
        fixture.Confirm("SAVEPOINT \"ALIAS\"");
        fixture.Confirm("ROLLBACK TO \"Alias\"");
        Assert.Empty(fixture.Entries);
    }

    [Fact]
    public void PreflightNeverCreatesOrRemovesPointsWithoutAConfirmedExecution()
    {
        using var fixture = new ActiveTransactionFixture();
        fixture.SeedApiPoint("api", "WSP_1");
        fixture.Confirm("SAVEPOINT EF_save");
        foreach (string sql in new[] { "SAVEPOINT EF_save", "ROLLBACK TO EF_save", "RELEASE SAVEPOINT EF_save" })
            DmTransaction.ValidateCommandSql(fixture.Connection, fixture.Transaction, sql);
        Assert.Equal(new[] { "api" }, fixture.ApiNames());
        Assert.Equal(new[] { "EF_save" }, fixture.RawNames());
    }

    [Fact]
    public void StackCapacityIsCheckedWithoutSendingAndRepeatedRawNameConsumesNoLifetimeQuota()
    {
        using var fixture = new ActiveTransactionFixture();
        for (int index = 0; index < 128; index++) fixture.Confirm("SAVEPOINT raw_" + index);
        for (int repeat = 0; repeat < 130; repeat++) fixture.Confirm("SAVEPOINT raw_127");
        Assert.Equal(128, fixture.Entries.Count);
        Assert.Equal(0, (int)GetField(fixture.Transaction, "savepointSequence")!);
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<InvalidOperationException>(() => DmTransaction.ValidateCommandSql(
            fixture.Connection, fixture.Transaction, "SAVEPOINT overflow"));
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Save("api_overflow"));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Equal(128, fixture.Entries.Count);
    }

    [Fact]
    public void SavepointConfirmationRequiresCurrentUserInvocationAndMatchingSession()
    {
        using var fixture = new ActiveTransactionFixture();
        Assert.True(DmParameterBinding.TryParseSavepointControl("SAVEPOINT point", out var control));
        using (var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query))
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Transaction.ConfirmUserSavepointControl(fixture.Connection, control, lease));
        var foreign = new DmSession();
        foreign.CompleteHandshakeForTests();
        using (var lease = foreign.BeginExecution(DmOperationPurpose.Query))
        using (var invocation = lease.BeginInvocation())
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Transaction.ConfirmUserSavepointControl(fixture.Connection, control, lease));
        using (var lease = fixture.Session.BeginExecution(DmOperationPurpose.TransactionControl))
        using (var invocation = lease.BeginInvocation())
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Transaction.ConfirmUserSavepointControl(fixture.Connection, control, lease));
        Assert.Empty(fixture.Entries);
    }

    [Fact]
    public void DriverSavepointApisRejectInvalidOrAbsentNamesWithoutSendingOrChangingOutcome()
    {
        using var fixture = new ActiveTransactionFixture();
        long sends = DmWireTestHooks.SendCount;
        long bytes = DmWireTestHooks.SentBytes;

        Assert.True(fixture.Transaction.SupportsSavepoints);
        Assert.Throws<ArgumentException>(() => fixture.Transaction.Save(""));
        Assert.Throws<ArgumentException>(() => fixture.Transaction.Save("bad\0name"));
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Rollback("absent"));
        Assert.Throws<InvalidOperationException>(() => fixture.Transaction.Release("absent"));

        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Equal(bytes, DmWireTestHooks.SentBytes);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
    }

    [Fact]
    public void InternalSavepointControlCannotBorrowAnotherSessionsLease()
    {
        using var fixture = new ActiveTransactionFixture();
        var foreign = new DmSession();
        foreign.CompleteHandshakeForTests();
        using var lease = foreign.BeginExecution(DmOperationPurpose.TransactionControl);
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Transaction.ExecuteSavepointSql(lease, "SAVEPOINT \"WSP_1\""));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Equal(DmTransactionOutcome.Active, fixture.Transaction.Outcome);
    }

    private sealed class ActiveTransactionFixture : IDisposable
    {
        internal DmConnection Connection { get; } = new();
        internal DmSession Session { get; } = new();
        internal DmTransaction Transaction { get; }
        internal IList Entries => (IList)GetField(Transaction, "savepoints")!;

        internal ActiveTransactionFixture()
        {
            // Bind a real session/transaction state machine to a socket-free
            // instance. Unexpected execution cannot fall through to a live DB.
            Session.CompleteHandshakeForTests();
            SetField(Connection, "session", Session);
            Connection.do_State = ConnectionState.Open;
            var instance = (DmConnInstance)RuntimeHelpers.GetUninitializedObject(typeof(DmConnInstance));
            SetField(instance, "<Session>k__BackingField", Session);
            SetField(instance, "m_ConnPro", Connection.ConnProperty);
            instance.Conn = Connection;
            Connection.ConnProperty.ServerVersion = "8.1.5.60";
            Connection.ConnProperty.AutoCommit = false;
            Connection.m_ConnInst = instance;
            Session.SetTransactionState(DmLocalTransactionState.Starting);
            using var lease = Session.BeginExecution(DmOperationPurpose.TransactionControl);
            using var invocation = lease.BeginInvocation();
            Transaction = new DmTransaction(instance);
            instance.Transaction = Transaction;
            Session.ActivateTransaction(Transaction, invocation.Identity);
        }

        public void Dispose()
        {
            // There is no transport to roll back or close in this fixture.
            Connection.m_ConnInst = null!;
            SetField(Connection, "session", null);
            Connection.do_State = ConnectionState.Closed;
            Connection.Dispose();
        }

        internal void SeedApiPoint(string name, string serverName)
        {
            // Model a confirmed API SAVE without constructing a fake wire ACK.
            Type entryType = typeof(DmTransaction).GetNestedType("SavepointEntry", BindingFlags.NonPublic)!;
            Entries.Add(Activator.CreateInstance(entryType, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object[] { name, serverName }, culture: null));
        }

        internal void Confirm(string sql)
        {
            Assert.True(DmParameterBinding.TryParseSavepointControl(sql, out var control));
            DmTransaction.ValidateCommandSql(Connection, Transaction, sql);
            using var lease = Session.BeginExecution(DmOperationPurpose.Query);
            using var invocation = lease.BeginInvocation();
            // This fixture explicitly models a validated execution response;
            // live callbacks and server-side effects are checked by the probes.
            Transaction.ConfirmUserSavepointControl(Connection, control, lease);
        }

        internal string[] ApiNames() => Names(raw: false);
        internal string[] RawNames() => Names(raw: true);
        private string[] Names(bool raw) => Entries.Cast<object>()
            .Where(entry => (bool)GetField(entry, "IsRaw")! == raw)
            .Select(entry => (string)GetField(entry, "UserName")!).ToArray();
    }

    private static object? GetField(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static void SetField(object target, string name, object? value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
