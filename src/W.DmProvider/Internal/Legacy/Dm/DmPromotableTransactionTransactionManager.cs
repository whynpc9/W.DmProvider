using System.Collections;
using System.Transactions;

namespace W.Dm;

internal class DmPromotableTransactionTransactionManager
{
	public static readonly Hashtable DmPromotableTransactionInUse = new Hashtable();

	public static DmPromotableTransaction GetDmPromotableTransactionInTransaction(Transaction transaction)
	{
		lock (DmPromotableTransactionInUse.SyncRoot)
		{
			return (DmPromotableTransaction)DmPromotableTransactionInUse[transaction.GetHashCode()];
		}
	}

	public static void SetDmPromotableTransactionInTransaction(DmPromotableTransaction dmPromotableTransaction)
	{
		lock (DmPromotableTransactionInUse.SyncRoot)
		{
			DmPromotableTransactionInUse[dmPromotableTransaction.BaseTransaction.GetHashCode()] = dmPromotableTransaction;
		}
	}

	public static void RemoveDmPromotableTransactionInTransaction(Transaction transaction)
	{
		lock (DmPromotableTransactionInUse.SyncRoot)
		{
			DmPromotableTransactionInUse.Remove(transaction.GetHashCode());
		}
	}
}
