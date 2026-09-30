using W.Dm;
using W.Dm.Internal.Execution;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class ResultCursorTests
{
    [Fact]
    public void ProvenDmlCountBecomesUnknownWhenCompoundRowsetCanHideAnotherDml()
    {
        var cursor = new DmResultCursor();
        var update = new DmInfo();
        update.SetRetStmtType(DmConst.RET_DML_UPDATE);
        update.SetRecordsAffected(1);
        update.SetHasResultSet(false);
        cursor.Observe(update);
        Assert.Equal(1, cursor.RecordsAffected);

        var compound = new DmInfo();
        compound.SetRetStmtType(DmConst.RET_DML_CALL);
        compound.SetRowCount(1);
        compound.SetRecordsAffected(1);
        compound.SetHasResultSet(true);
        cursor.Observe(compound);
        Assert.Equal(-1, cursor.RecordsAffected);
    }

    [Fact]
    public void PureSelectRowCountNeverBecomesAffectedRowCount()
    {
        var cursor = new DmResultCursor();
        var select = new DmInfo();
        select.SetRetStmtType(DmConst.RET_DML_CALL);
        select.SetRowCount(2);
        select.SetRecordsAffected(2);
        select.SetHasResultSet(true);
        cursor.Observe(select);
        Assert.Equal(-1, cursor.RecordsAffected);

        var terminal = new DmInfo();
        terminal.MarkTerminal();
        cursor.Observe(terminal);
        Assert.True(cursor.IsTerminal);
        Assert.Equal(-1, cursor.RecordsAffected);
        Assert.Throws<InvalidOperationException>(() => cursor.Observe(new DmInfo()));
    }

    [Fact]
    public void AffectedRowCountOverflowIsExplicit()
    {
        var cursor = new DmResultCursor();
        var beyondInt = new DmInfo();
        beyondInt.SetRetStmtType(DmConst.RET_DML_UPDATE);
        beyondInt.SetRecordsAffected((long)int.MaxValue + 1);
        cursor.Observe(beyondInt);
        Assert.Throws<OverflowException>(() => _ = cursor.RecordsAffected);

        var cumulative = new DmResultCursor();
        var first = new DmInfo();
        first.SetRetStmtType(DmConst.RET_DML_UPDATE);
        first.SetRecordsAffected(long.MaxValue);
        cumulative.Observe(first);
        var second = new DmInfo();
        second.SetRetStmtType(DmConst.RET_DML_UPDATE);
        second.SetRecordsAffected(1);
        Assert.Throws<OverflowException>(() => cumulative.Observe(second));
    }
}
