using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Transactions;

namespace Dm;

internal sealed class DmPromotableTransaction : IPromotableSinglePhaseNotification, ITransactionPromoter
{
	public readonly Transaction BaseTransaction;

	public Queue<DmTransactionScope> ScopeQueue;

	public bool InCommitOrRollback;

	public readonly System.Data.IsolationLevel IsolationLevel;

	public bool InRollback
	{
		get
		{
			if (ScopeQueue.Count <= 0)
			{
				return false;
			}
			return ScopeQueue.Peek().RollbackThreadId == Thread.CurrentThread.ManagedThreadId;
		}
	}

	public DmPromotableTransaction(Transaction transaction, StringBuilder msg)
	{
		BaseTransaction = transaction;
		string name = Enum.GetName(typeof(System.Transactions.IsolationLevel), BaseTransaction.IsolationLevel);
		if (name != null)
		{
			msg.Append("->{isolationLevelName{" + name + "} != null");
			IsolationLevel = (System.Data.IsolationLevel)Enum.Parse(typeof(System.Data.IsolationLevel), name);
		}
		else
		{
			msg.Append("->{isolationLevel == null}");
		}
		ScopeQueue = new Queue<DmTransactionScope>();
	}

	void IPromotableSinglePhaseNotification.Initialize()
	{
	}

	public void Enqueue(DmConnection connection, StringBuilder msg)
	{
		msg.Append("->{connection.do_BeginDbTransaction(" + IsolationLevel.ToString() + ")}");
		connection.do_BeginDbTransaction(IsolationLevel);
		msg.Append("->{ScopeQueue.Enqueue(new DmTransactionScope(connection))}");
		ScopeQueue.Enqueue(new DmTransactionScope(connection));
	}

	byte[] ITransactionPromoter.Promote()
	{
		throw new NotSupportedException();
	}

	void IPromotableSinglePhaseNotification.Rollback(SinglePhaseEnlistment singlePhaseEnlistment)
	{
		try
		{
			InCommitOrRollback = true;
			while (ScopeQueue.Count > 0)
			{
				ScopeQueue.Peek().Rollback();
				ScopeQueue.Dequeue();
			}
			singlePhaseEnlistment.Aborted();
			DmPromotableTransactionTransactionManager.RemoveDmPromotableTransactionInTransaction(BaseTransaction);
		}
		catch (Exception e)
		{
			singlePhaseEnlistment.Aborted(e);
		}
	}

	void IPromotableSinglePhaseNotification.SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment)
	{
		InCommitOrRollback = true;
		while (ScopeQueue.Count > 0)
		{
			ScopeQueue.Peek().SinglePhaseCommit();
			ScopeQueue.Dequeue();
		}
		singlePhaseEnlistment.Committed();
		DmPromotableTransactionTransactionManager.RemoveDmPromotableTransactionInTransaction(BaseTransaction);
	}
}
