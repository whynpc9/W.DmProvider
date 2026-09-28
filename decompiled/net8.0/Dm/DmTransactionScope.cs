using System;
using System.Threading;

namespace Dm;

internal class DmTransactionScope
{
	public DmConnection Connection;

	public readonly DmTransaction Transaction;

	public int RollbackThreadId;

	public DmTransactionScope(DmConnection connection)
	{
		Connection = connection;
		Transaction = Connection.m_ConnInst.Transaction;
	}

	public void Rollback()
	{
		RollbackThreadId = Thread.CurrentThread.ManagedThreadId;
		Transaction.do_Rollback();
		RollbackThreadId = 0;
		ClearTransactionAndConnection();
	}

	public void SinglePhaseCommit()
	{
		Transaction.do_Commit();
		ClearTransactionAndConnection();
	}

	public void ClearTransactionAndConnection()
	{
		DisposeTransaction();
		ClearConnection();
	}

	public void DisposeTransaction()
	{
		if (Transaction._disposeStatus == DmTransaction.DisposeStatus.NeedToDispose)
		{
			Transaction.Dispose();
		}
	}

	public void ClearConnection()
	{
		Connection.m_ConnInst.CurrentDmPromotableTransaction = null;
		switch (Connection.StatusInTransactionScope)
		{
		case DmConnection.ConnectionStatusInTransactionScope.Closed:
			Connection.Close();
			break;
		case DmConnection.ConnectionStatusInTransactionScope.Disposed:
			Connection.Dispose();
			break;
		default:
			throw new ArgumentOutOfRangeException();
		case DmConnection.ConnectionStatusInTransactionScope.Open:
			break;
		}
	}
}
