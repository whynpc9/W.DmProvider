using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Net.Sockets;
using System.Text;
using A;
using Dm.Config;
using Dm.filter;
using Dm.util;

namespace Dm;

internal class DmConnInstance
{
	private DmTransaction m_Tran;

	private B m_Csi;

	private DmConnection m_Conn;

	private List<global::A.A> m_Stmts = new List<global::A.A>();

	private b m_SendMsg = new b();

	private b m_RecvMsg = new b();

	private DmConnProperty m_ConnPro;

	private Queue stmt_queue = new Queue();

	private LRUCache<string, global::A.A> pstmtCache;

	public DmPromotableTransaction CurrentDmPromotableTransaction;

	public int trxStatus;

	private bool trxFinish = true;

	private object _aliveCheckLockObj = new object();

	private bool _aliveCheck = true;

	internal DateTime connPoolPutTime;

	internal RWInfo RWInfo { get; set; }

	internal RecoverInfo RecoverInfo { get; set; }

	internal bool AliveCheck
	{
		get
		{
			lock (_aliveCheckLockObj)
			{
				return _aliveCheck;
			}
		}
		set
		{
			lock (_aliveCheckLockObj)
			{
				_aliveCheck = value;
			}
		}
	}

	public DmConnProperty ConnProperty => m_ConnPro;

	public DmTransaction Transaction
	{
		get
		{
			return m_Tran;
		}
		set
		{
			m_Tran = value;
		}
	}

	public DmConnection Conn
	{
		get
		{
			return m_Conn;
		}
		set
		{
			m_Conn = value;
		}
	}

	public List<global::A.A> Stmts => m_Stmts;

	internal bool ReUsedStmt(global::A.A stmt)
	{
		lock (stmt_queue)
		{
			bool result = true;
			if (m_ConnPro.PreparePooling && stmt.b())
			{
				stmt = pstmtCache.Add(stmt.C(), stmt);
				result = false;
			}
			if (stmt != null && stmt_queue.Count < m_ConnPro.StmtPoolSize)
			{
				if (!stmt_queue.Contains(stmt))
				{
					stmt.A(false);
					stmt_queue.Enqueue(stmt);
				}
				result = false;
			}
			return result;
		}
	}

	public DmConnInstance(DmConnection conn)
	{
		try
		{
			SetDmConnection(conn);
			m_ConnPro = conn.ConnProperty.Clone();
			if (m_ConnPro.PreparePooling)
			{
				pstmtCache = new LRUCache<string, global::A.A>(m_ConnPro.PreparePoolSize);
			}
			m_Csi = new B(m_SendMsg, m_RecvMsg, this);
			DBAliveCheckThread.CheckThread.AddConnInstance(this);
		}
		catch (Exception ex)
		{
			conn.do_State = ConnectionState.Closed;
			throw ex;
		}
	}

	internal B GetCsi()
	{
		return m_Csi;
	}

	public bool GetAutoCommit()
	{
		return m_ConnPro.AutoCommit;
	}

	internal global::A.A GetStmtFromPool(DmCommand cmd)
	{
		lock (stmt_queue)
		{
			global::A.A a2 = null;
			if (m_ConnPro.PreparePooling && cmd != null && cmd.do_CommandText != null && !cmd.do_CommandText.Trim().Equals(""))
			{
				a2 = pstmtCache.FindValue(cmd.GetCommandText());
			}
			if (m_ConnPro.StmtPooling && a2 == null && stmt_queue.Count > 0)
			{
				a2 = stmt_queue.Dequeue() as global::A.A;
			}
			if (a2 != null)
			{
				a2.A(cmd);
				AddStmt(a2);
				return a2;
			}
			return new global::A.A(this, cmd);
		}
	}

	public void SetAutoCommit(bool autoCommit)
	{
		ConnProperty.AutoCommit = autoCommit;
	}

	public void CheckClosed()
	{
		if (m_Csi == null || m_Csi.D())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNECTION_CLOSED);
		}
	}

	public void Switched()
	{
		if (Transaction != null)
		{
			Transaction.Valid = false;
			Transaction = null;
		}
		foreach (global::A.A stmt in m_Stmts)
		{
			stmt.B(true);
		}
	}

	private void Cleanup(bool keep_tcp)
	{
		if (Transaction != null)
		{
			if (Transaction.Valid)
			{
				Transaction.Dispose();
			}
			Transaction = null;
		}
		if (m_Stmts != null)
		{
			CloseAllStmts(keep_tcp);
			if (!keep_tcp)
			{
				ClearAllStmts();
			}
		}
		if (!keep_tcp)
		{
			m_Csi = null;
			m_ConnPro = null;
		}
	}

	public void Close(bool keep_tcp)
	{
		if (m_Csi == null)
		{
			return;
		}
		try
		{
			if (!keep_tcp)
			{
				m_Csi.E();
				AliveCheck = false;
			}
		}
		finally
		{
			Cleanup(keep_tcp);
			if (Conn != null)
			{
				Conn.do_State = ConnectionState.Closed;
			}
		}
	}

	public void Close(bool keep_tcp, StringBuilder msg)
	{
		if (m_Csi == null)
		{
			msg.Append("->{m_Csi == null}");
			return;
		}
		try
		{
			if (keep_tcp)
			{
				msg.Append("->{keep_tcp = " + keep_tcp + "}");
				return;
			}
			msg.Append("->{m_Csi.Close()}");
			m_Csi.E();
			msg.Append("->{AliveCheck = false;}");
			AliveCheck = false;
		}
		finally
		{
			msg.Append("->{Cleanup(" + keep_tcp + ")}");
			Cleanup(keep_tcp);
			if (Conn != null)
			{
				msg.Append("->{Conn.do_State = ConnectionState.Closed;}");
				Conn.do_State = ConnectionState.Closed;
			}
		}
	}

	private void SetTransactionIsolation(DmTransaction dmTransaction, IsolationLevel level)
	{
		int num = 1;
		switch (level)
		{
		case IsolationLevel.Unspecified:
		case IsolationLevel.ReadCommitted:
			level = IsolationLevel.ReadCommitted;
			num = 1;
			break;
		case IsolationLevel.Serializable:
			num = 3;
			break;
		case IsolationLevel.ReadUncommitted:
			num = 0;
			break;
		case IsolationLevel.RepeatableRead:
			if (ConnProperty.CompatibleMode == CompatibleMode.MYSQL)
			{
				num = 1;
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TRAN_ISOLATION);
			}
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TRAN_ISOLATION);
			break;
		}
		if (level != m_ConnPro.IsolationLevel)
		{
			string text = "SET TRANSACTION ISOLATION LEVEL ";
			text = num switch
			{
				0 => text + "READ UNCOMMITTED;", 
				1 => text + "READ COMMITTED;", 
				_ => text + "SERIALIZABLE;", 
			};
			dmTransaction.Stmt = GetStmtFromPool((DmCommand)Conn.CreateCommand());
			m_Csi.A(dmTransaction.Stmt.A, dmTransaction.Stmt.a, dmTransaction.Stmt, text, true, 0);
		}
	}

	public void SetTrxISO(IsolationLevel level)
	{
		ConnProperty.IsolationLevel = level;
	}

	public DmTransaction BeginTrx(IsolationLevel il)
	{
		if (Transaction != null && Transaction.Valid)
		{
			if (ConnProperty.Enlist)
			{
				return Transaction;
			}
			throw new InvalidOperationException("不支持并行事务");
		}
		Transaction = new DmTransaction(this, il);
		SetTransactionIsolation(Transaction, il);
		SetAutoCommit(autoCommit: false);
		return Transaction;
	}

	public void Commit()
	{
		CheckClosed();
		if (GetAutoCommit())
		{
			if (!ConnProperty.AlwaysAllowAutoCommit)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMIT_IN_AUTOCOMMIT_MODE);
			}
		}
		else
		{
			ClearTrx();
			m_Csi.B(m_SendMsg, m_RecvMsg);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
		}
	}

	public void Rollback()
	{
		CheckClosed();
		if (GetAutoCommit())
		{
			if (!ConnProperty.AlwaysAllowAutoCommit)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMIT_IN_AUTOCOMMIT_MODE);
			}
		}
		else
		{
			ClearTrx();
			m_Csi.b(m_SendMsg, m_RecvMsg);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
		}
	}

	internal bool RollbackWithoutAutoCommitCheck()
	{
		try
		{
			CheckClosed();
			ClearTrx();
			m_Csi.b(m_SendMsg, m_RecvMsg);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public DmSavePoint Save(string savepointName)
	{
		CheckClosed();
		if (GetAutoCommit())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		}
		return new DmSavePoint(Conn, savepointName);
	}

	public void Rollback(string savepointName)
	{
		CheckClosed();
		if (GetAutoCommit() && !ConnProperty.AlwaysAllowAutoCommit)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_ROLLBACK_TO_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		}
		string sql = "ROLLBACK TO SAVEPOINT \"" + StringUtil.processDoubleQuoteOfName(savepointName) + "\"";
		DriverUtil.executeNonQuery(Conn, sql, null);
	}

	public void Release(string savepointName)
	{
		CheckClosed();
		if (GetAutoCommit() && !ConnProperty.AlwaysAllowAutoCommit)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_RELEASE_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		}
		lock (Conn)
		{
			string sql = "RELEASE_SAVEPOINT('" + StringUtil.processSingleQuoteOfName(savepointName) + "')";
			DriverUtil.executeNonQuery(Conn, sql, null);
		}
	}

	internal void ClearTrx()
	{
		if (Transaction != null)
		{
			if (Transaction.Valid)
			{
				Transaction.Clear();
			}
			Transaction = null;
		}
	}

	public void AddStmt(global::A.A stmt)
	{
		lock (m_Stmts)
		{
			m_Stmts.Add(stmt);
		}
	}

	public void RemoveStmt(global::A.A stmt)
	{
		lock (m_Stmts)
		{
			if (!ConnProperty.StmtPooling && m_Stmts.Contains(stmt))
			{
				m_Stmts.Remove(stmt);
			}
		}
	}

	public void CloseAllStmts(bool keep_tcp)
	{
		if (m_Stmts == null)
		{
			return;
		}
		lock (m_Stmts)
		{
			foreach (global::A.A stmt in m_Stmts)
			{
				stmt.A(false, keep_tcp);
			}
			m_Stmts.Clear();
		}
	}

	public void ClearAllStmts()
	{
		if (m_Stmts == null)
		{
			return;
		}
		lock (m_Stmts)
		{
			foreach (global::A.A stmt in m_Stmts)
			{
				stmt.o();
			}
			m_Stmts.Clear();
		}
	}

	public void RestAllStmt()
	{
		lock (m_Stmts)
		{
			foreach (global::A.A stmt in m_Stmts)
			{
				if (stmt.l() != null)
				{
					stmt.l().Reset();
				}
			}
		}
	}

	public void SetDmConnection(DmConnection connection)
	{
		Conn = connection;
	}

	public void CleanDmConnection()
	{
		Conn = null;
	}

	public void do_setTrxFinish(int status)
	{
		int num = status & 0xFFF;
		if (num == 0 || num == 32 || num == 64)
		{
			trxFinish = true;
		}
		else
		{
			trxFinish = false;
		}
	}

	public void do_setTrxFinish(bool trxFinish)
	{
		this.trxFinish = trxFinish;
	}

	public bool getTransFinish()
	{
		return trxFinish;
	}

	public bool IsSocketConnected()
	{
		Socket socket = m_Csi.A().A;
		if (socket.Poll(0, SelectMode.SelectRead))
		{
			byte[] buffer = new byte[1];
			if (socket.Receive(buffer, SocketFlags.Peek) == 0)
			{
				return false;
			}
		}
		return true;
	}
}
