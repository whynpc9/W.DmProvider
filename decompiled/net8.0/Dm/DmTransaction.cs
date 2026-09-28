using System;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Transactions;
using A;
using Dm.Config;
using Dm.filter;

namespace Dm;

public sealed class DmTransaction : DbTransaction, IFilterInfo
{
	public enum DisposeStatus
	{
		Valid = 1,
		NeedToDispose,
		Disposed
	}

	internal long id = -1L;

	internal static long idGenerator;

	private global::A.A stmt;

	private readonly DmConnInstance connInst;

	public DisposeStatus _disposeStatus = DisposeStatus.Valid;

	private bool m_StmtSerial;

	private System.Data.IsolationLevel m_il;

	private bool m_Valid = true;

	public long ID
	{
		get
		{
			if (id < 0)
			{
				id = Interlocked.Increment(ref idGenerator);
			}
			return id;
		}
	}

	public BaseFilter filterHead { get; set; }

	public LogInfo LogInfo { get; set; }

	public RWInfo RWInfo { get; set; }

	public RecoverInfo RecoverInfo { get; set; }

	internal System.Data.IsolationLevel do_IsolationLevel => m_il;

	internal DmConnection do_DbConnection
	{
		get
		{
			if (!Valid)
			{
				return null;
			}
			return connInst.Conn;
		}
	}

	public override System.Data.IsolationLevel IsolationLevel
	{
		get
		{
			if (filterHead == null)
			{
				return do_IsolationLevel;
			}
			return filterHead.getIsolationLevel(this);
		}
	}

	protected override DbConnection DbConnection
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbConnection;
			}
			return filterHead.getDbConnection(this);
		}
	}

	internal global::A.A Stmt
	{
		get
		{
			return stmt;
		}
		set
		{
			stmt = value;
		}
	}

	public bool Valid
	{
		get
		{
			return m_Valid;
		}
		set
		{
			m_Valid = value;
		}
	}

	internal DmTransaction(DmConnInstance connInst)
	{
		BaseFilter.CreateFilterChain(this, connInst.ConnProperty);
		this.connInst = connInst;
		m_il = System.Data.IsolationLevel.ReadCommitted;
	}

	internal DmTransaction(DmConnInstance connInst, System.Data.IsolationLevel il)
		: this(connInst)
	{
		m_il = il;
	}

	internal void do_Commit()
	{
		DmPromotableTransaction currentDmPromotableTransaction = connInst.CurrentDmPromotableTransaction;
		if (currentDmPromotableTransaction != null && !currentDmPromotableTransaction.InCommitOrRollback)
		{
			return;
		}
		try
		{
			CheckValid();
			CheckTransactionStatus();
			connInst.Commit();
		}
		catch (DmException ex)
		{
			if (ex.ErrorCode != 6042)
			{
				throw;
			}
		}
		finally
		{
			Clear();
		}
	}

	internal void do_Rollback()
	{
		DmPromotableTransaction currentDmPromotableTransaction = connInst.CurrentDmPromotableTransaction;
		if (currentDmPromotableTransaction != null && !currentDmPromotableTransaction.InCommitOrRollback)
		{
			return;
		}
		CheckValid();
		CheckTransactionStatus();
		try
		{
			connInst.Rollback();
		}
		finally
		{
			Clear();
		}
	}

	internal void do_Dispose(bool disposing)
	{
		if (_disposeStatus == DisposeStatus.Disposed)
		{
			return;
		}
		if (connInst?.Conn != null && connInst.Conn.ExistDmPromotableTransaction())
		{
			_disposeStatus = DisposeStatus.NeedToDispose;
			return;
		}
		_disposeStatus = DisposeStatus.Disposed;
		GC.SuppressFinalize(this);
		base.Dispose(disposing);
		if (Valid)
		{
			if (connInst.ConnProperty.CompatibleMode == CompatibleMode.ORACLE)
			{
				do_Commit();
			}
			else
			{
				do_Rollback();
			}
		}
	}

	internal void do_Dispose(bool disposing, StringBuilder msg)
	{
		if (_disposeStatus == DisposeStatus.Disposed)
		{
			msg.Append("->{_disposeStatus == DisposeStatus.Disposed}");
			return;
		}
		msg.Append("->{connInst?.Conn != null && connInst.Conn.ExistDmPromotableTransaction()}");
		if (connInst?.Conn != null && connInst.Conn.ExistDmPromotableTransaction())
		{
			msg.Append("->{_disposeStatus = DisposeStatus.NeedToDispose;}");
			_disposeStatus = DisposeStatus.NeedToDispose;
			return;
		}
		msg.Append("->{_disposeStatus = DisposeStatus.Disposed;GC.SuppressFinalize(this);}");
		_disposeStatus = DisposeStatus.Disposed;
		GC.SuppressFinalize(this);
		msg.Append("->{base.Dispose(disposing);}");
		base.Dispose(disposing);
		if (!Valid)
		{
			msg.Append("->{!Valid}");
		}
		else if (connInst.ConnProperty.CompatibleMode == CompatibleMode.ORACLE)
		{
			msg.Append("->{do_Commit()}");
			do_Commit();
		}
		else
		{
			msg.Append("->{do_Rollback()}");
			do_Rollback();
		}
	}

	internal DmSavePoint do_Save(string savepointName)
	{
		try
		{
			CheckValid();
			CheckTransactionStatus();
			return connInst.Save(savepointName);
		}
		catch (DmException ex)
		{
			throw ex;
		}
		catch (Exception ex2)
		{
			throw ex2;
		}
	}

	internal void do_Rollback(string savepointName)
	{
		try
		{
			CheckValid();
			CheckTransactionStatus();
			connInst.Rollback(savepointName);
		}
		catch (DmException ex)
		{
			throw ex;
		}
		catch (Exception ex2)
		{
			throw ex2;
		}
	}

	internal void do_Release(string savepointName)
	{
		try
		{
			CheckValid();
			CheckTransactionStatus();
			connInst.Release(savepointName);
		}
		catch (DmException ex)
		{
			throw ex;
		}
		catch (Exception ex2)
		{
			throw ex2;
		}
	}

	public override void Commit()
	{
		if (filterHead == null)
		{
			do_Commit();
		}
		else
		{
			filterHead.Commit(this);
		}
	}

	public override void Rollback()
	{
		if (filterHead == null)
		{
			do_Rollback();
		}
		else
		{
			filterHead.Rollback(this);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (filterHead == null)
		{
			do_Dispose(disposing);
		}
		else
		{
			filterHead.Dispose(this, disposing);
		}
	}

	public override void Save(string savepointName)
	{
		if (filterHead == null)
		{
			do_Save(savepointName);
		}
		else
		{
			filterHead.Save(this, savepointName);
		}
	}

	public override void Rollback(string savepointName)
	{
		if (filterHead == null)
		{
			do_Rollback(savepointName);
		}
		else
		{
			filterHead.Rollback(this, savepointName);
		}
	}

	public override void Release(string savepointName)
	{
		if (filterHead == null)
		{
			do_Release(savepointName);
		}
		else
		{
			filterHead.Release(this, savepointName);
		}
	}

	public void Dispose(bool disposing, bool for_ef)
	{
		if (for_ef)
		{
			do_Dispose(disposing);
		}
	}

	public new void Dispose()
	{
		do_Dispose(disposing: true);
	}

	public void Dispose(StringBuilder msg)
	{
		do_Dispose(disposing: true, msg);
	}

	internal void SetStmtSerial(int level)
	{
		if (level == 3)
		{
			m_StmtSerial = true;
		}
	}

	public bool GetStmtSerial()
	{
		return m_StmtSerial;
	}

	private void CheckValid()
	{
		if (!Valid)
		{
			throw new InvalidOperationException("此Transaction已完成，不可再用");
		}
	}

	private void CheckTransactionStatus()
	{
		Transaction current = Transaction.Current;
		if (current == null)
		{
			return;
		}
		bool flag = false;
		if (connInst.CurrentDmPromotableTransaction != null)
		{
			flag = connInst.CurrentDmPromotableTransaction.InRollback;
		}
		if (!flag)
		{
			TransactionStatus transactionStatus = TransactionStatus.InDoubt;
			try
			{
				transactionStatus = current.TransactionInformation.Status;
			}
			catch (TransactionException)
			{
			}
			if (transactionStatus == TransactionStatus.Aborted)
			{
				throw new TransactionAbortedException();
			}
		}
	}

	public void Clear()
	{
		Valid = false;
		Stmt = null;
	}
}
