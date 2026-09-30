using System;
using W.Dm;

namespace W.Dm.Internal.Execution;

/// <summary>Tracks only counts proven to be DML, independent of rowset row counts.</summary>
internal sealed class DmResultCursor
{
    private long affected;
    private bool hasKnownDmlCount;
    private bool hasUnknownCount;

    internal DmInfo Current { get; private set; }
    internal bool IsTerminal { get; private set; }
    internal bool HasReadableRowset => !IsTerminal && Current?.GetHasResultSet() == true;

    internal void Observe(DmInfo info)
    {
        if (info == null) throw new ArgumentNullException(nameof(info));
        if (IsTerminal) throw new InvalidOperationException("Result cursor has reached its terminal response.");
        Current = info;
        if (info.IsTerminal)
        {
            IsTerminal = true;
            return;
        }
        if (info.GetHasResultSet())
        {
            if (info.GetRetStmtType() is DmConst.RET_DML_CALL or DmConst.RET_DML_INSERT
                or DmConst.RET_DML_DELETE or DmConst.RET_DML_UPDATE)
                hasUnknownCount = true; // A rowset may hide a DML step or its count.
            return;
        }

        int resultType = info.GetRetStmtType();
        if (resultType is DmConst.RET_DML_INSERT or DmConst.RET_DML_DELETE or DmConst.RET_DML_UPDATE)
        {
            long count = info.GetRecordsAffected();
            if (count < 0) { hasUnknownCount = true; return; }
            try { affected = checked(affected + count); }
            catch (OverflowException ex) { throw new OverflowException("Affected row count overflow.", ex); }
            hasKnownDmlCount = true;
        }
		else
		{
			// A count-only reply of another type has no proven DML cardinality.
			hasUnknownCount = true;
		}
    }

    internal int RecordsAffected
    {
        get
        {
            if (!hasKnownDmlCount || hasUnknownCount) return -1;
            if (affected > int.MaxValue) throw new OverflowException("Affected row count exceeds Int32.");
            return (int)affected;
        }
    }
}
