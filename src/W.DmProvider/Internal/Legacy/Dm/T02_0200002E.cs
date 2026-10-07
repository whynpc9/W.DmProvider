using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

internal class DmConnInstance
{
	private DmTransaction m_Tran;

	private B m_Csi;
	private readonly D physicalTransport;
	private D syntheticPhysicalTransport;

	internal DmSession Session { get; }

	private DmConnection m_Conn;

	private List<global::W.Dm.Internal.Legacy.A.A> m_Stmts = new List<global::W.Dm.Internal.Legacy.A.A>();

	private b m_SendMsg = new b();

	private b m_RecvMsg = new b();

	private DmConnProperty m_ConnPro;

	private Queue stmt_queue = new Queue();

	private LRUCache<string, global::W.Dm.Internal.Legacy.A.A> pstmtCache;

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

	public List<global::W.Dm.Internal.Legacy.A.A> Stmts => m_Stmts;

	internal bool ReUsedStmt(global::W.Dm.Internal.Legacy.A.A stmt)
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
					stmt.__t02_method_0600088A(false);
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
			Session = conn.Session ?? throw new InvalidOperationException("Physical session is missing.");
			SetDmConnection(conn);
			m_ConnPro = conn.ConnProperty.Clone();
			if (m_ConnPro.PreparePooling)
			{
				pstmtCache = new LRUCache<string, global::W.Dm.Internal.Legacy.A.A>(m_ConnPro.PreparePoolSize);
			}
			m_Csi = new B(m_SendMsg, m_RecvMsg, this);
			physicalTransport = m_Csi.A();
			Session.AttachTransport(this);
			// Legacy process-wide alive checks are disabled in T04.
		}
		catch
		{
			try { AbortTransport(); } catch { }
			throw;
		}
	}

	internal B GetCsi()
	{
		return m_Csi;
	}

	internal void Open(DmDeadline deadline)
	{
		m_Csi.Open(deadline);
	}

	public bool GetAutoCommit()
	{
		return m_ConnPro.AutoCommit;
	}

	internal global::W.Dm.Internal.Legacy.A.A GetStmtFromPool(DmCommand cmd)
	{
		lock (stmt_queue)
		{
			global::W.Dm.Internal.Legacy.A.A a2 = null;
			if (m_ConnPro.PreparePooling && cmd != null && cmd.do_CommandText != null && !cmd.do_CommandText.Trim().Equals(""))
			{
				a2 = pstmtCache.FindValue(cmd.GetCommandText());
			}
			if (m_ConnPro.StmtPooling && a2 == null && stmt_queue.Count > 0)
			{
				a2 = stmt_queue.Dequeue() as global::W.Dm.Internal.Legacy.A.A;
			}
			if (a2 != null)
			{
				a2.__t02_method_0600089A(cmd);
				AddStmt(a2);
				return a2;
			}
			return new global::W.Dm.Internal.Legacy.A.A(this, cmd);
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
			Transaction.InvalidateForSessionLoss();
			Transaction = null;
		}
		foreach (global::W.Dm.Internal.Legacy.A.A stmt in m_Stmts)
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

	// No protocol cleanup is safe after an interrupted exchange. Close only the captured
	// physical transport; never invoke transaction Dispose or statement close here.
	internal void RunAfterPhysicalClosed(Action completion)
	{
		ArgumentNullException.ThrowIfNull(completion);
		// Keep the actual adapter even after AbortTransport takes m_Csi. The
		// fallback supports synthetic instances whose constructor was bypassed.
		D captured = physicalTransport ?? Volatile.Read(ref syntheticPhysicalTransport) ?? Volatile.Read(ref m_Csi)?.A();
		if (captured == null) completion();
		else captured.RunAfterPhysicalClosed(completion);
	}

	internal void AbortTransport()
	{
		if (physicalTransport == null)
		{
			D synthetic = Volatile.Read(ref m_Csi)?.A();
			if (synthetic != null) Interlocked.CompareExchange(ref syntheticPhysicalTransport, synthetic, null);
		}
		B captured = Interlocked.Exchange(ref m_Csi, null);
		if (captured == null) return;
		try { captured.E(forcePhysicalAbort: true); }
		finally
		{
			AliveCheck = false;
			Transaction?.InvalidateForSessionLoss();
			Transaction = null;
		}
	}

	public void Close(bool keep_tcp)
	{
		AbortTransport();
	}

	public void Close(bool keep_tcp, StringBuilder msg)
	{
		AbortTransport();
	}

	private bool SetTransactionIsolation(IsolationLevel level, out DmCommand isolationOwner)
	{
		isolationOwner = null;
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
			// This owner belongs only to the configuration exchange. Its explicit
			// SQL and handle must never become a user command's transaction statement.
			isolationOwner = Conn.CreateCommand(text);
			var controlStatement = GetStmtFromPool(isolationOwner);
			isolationOwner.Statement = controlStatement;
			controlStatement.B(text);
			m_Csi.A(controlStatement.__t02_field_04000925, controlStatement.__t02_field_04000926,
				controlStatement, text, true, 0);
			var invocation = DmInvocation.Current ??
				throw new InvalidOperationException("Isolation configuration has no invocation owner.");
			controlStatement.F().RequireTransactionIsolationReceipt((short)num, invocation.Identity);
		}
		// With no SET, the complete LOGIN/SET SESSION response already established
		// the default value. A transaction SET is confirmed by its own receipt above.
		return true;
	}

	public void SetTrxISO(IsolationLevel level)
	{
		ConnProperty.IsolationLevel = level;
	}

	public DmTransaction BeginTrx(IsolationLevel il, out DmCommand isolationOwner, out bool isolationConfirmed)
	{
		isolationOwner = null;
		isolationConfirmed = false;
		if (Transaction != null && Transaction.Valid)
		{
			if (ConnProperty.Enlist)
			{
				return Transaction;
			}
			throw new InvalidOperationException("不支持并行事务");
		}
		Transaction = new DmTransaction(this, il);
		isolationConfirmed = SetTransactionIsolation(il, out isolationOwner);
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
			m_Csi.__t02_method_06000A82(m_SendMsg, m_RecvMsg);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			ClearTrx();
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
			m_Csi.b(m_SendMsg, m_RecvMsg);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			ClearTrx();
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
		return Transaction?.do_Save(savepointName) ??
			throw new InvalidOperationException("Savepoint requires an active transaction.");
	}

	public void Rollback(string savepointName)
	{
		if (Transaction == null) throw new InvalidOperationException("Savepoint requires an active transaction.");
		Transaction.do_Rollback(savepointName);
	}

	public void Release(string savepointName)
	{
		if (Transaction == null) throw new InvalidOperationException("Savepoint requires an active transaction.");
		Transaction.do_Release(savepointName);
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

	public void AddStmt(global::W.Dm.Internal.Legacy.A.A stmt)
	{
		lock (m_Stmts)
		{
			m_Stmts.Add(stmt);
		}
	}

	public void RemoveStmt(global::W.Dm.Internal.Legacy.A.A stmt)
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
			foreach (global::W.Dm.Internal.Legacy.A.A stmt in m_Stmts)
			{
				stmt.__t02_method_060008BE(false, keep_tcp);
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
			foreach (global::W.Dm.Internal.Legacy.A.A stmt in m_Stmts)
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
			foreach (global::W.Dm.Internal.Legacy.A.A stmt in m_Stmts)
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
		throw new NotSupportedException("Legacy socket health checks are unsupported without an owned exchange.");
	}

	internal Task OpenAsync(DmDeadline deadline,CancellationToken cancellationToken = default) => m_Csi.OpenAsync(deadline,cancellationToken);
	internal async Task<global::W.Dm.Internal.Legacy.A.A> GetStmtFromPoolAsync(DmCommand cmd,CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		global::W.Dm.Internal.Legacy.A.A statement = null;
		lock(stmt_queue)
		{
			if(m_ConnPro.PreparePooling && cmd != null && !string.IsNullOrWhiteSpace(cmd.do_CommandText)) statement = pstmtCache.FindValue(cmd.GetCommandText());
			if(m_ConnPro.StmtPooling && statement == null && stmt_queue.Count > 0) statement = stmt_queue.Dequeue() as global::W.Dm.Internal.Legacy.A.A;
			if(statement != null) { statement.__t02_method_0600089A(cmd); AddStmt(statement); }
		}
		return statement ?? await global::W.Dm.Internal.Legacy.A.A.CreateAsync(this,cmd,cancellationToken).ConfigureAwait(false);
	}
	private async Task<bool> SetTransactionIsolationAsync(IsolationLevel level, DmCommand isolationOwner, CancellationToken cancellationToken)
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
			// This owner belongs only to the configuration exchange. Its explicit
			// SQL and handle must never become a user command's transaction statement.
			isolationOwner.CommandText = text;
			var controlStatement = await GetStmtFromPoolAsync(isolationOwner, cancellationToken).ConfigureAwait(false);
			isolationOwner.Statement = controlStatement;
			controlStatement.B(text);
			await m_Csi.AAsync(controlStatement.__t02_field_04000925, controlStatement.__t02_field_04000926,
				controlStatement, text, true, 0, cancellationToken).ConfigureAwait(false);
			var invocation = DmInvocation.Current ??
				throw new InvalidOperationException("Isolation configuration has no invocation owner.");
			controlStatement.F().RequireTransactionIsolationReceipt((short)num, invocation.Identity);
		}
		// With no SET, the complete LOGIN/SET SESSION response already established
		// the default value. A transaction SET is confirmed by its own receipt above.
		return true;
	}
internal async Task<(DmTransaction Transaction,DmCommand IsolationOwner,bool IsolationConfirmed)> BeginTrxAsync(IsolationLevel il,CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(Transaction != null && Transaction.Valid)
		{
			if(ConnProperty.Enlist) return (Transaction,null,true);
			throw new InvalidOperationException("不支持并行事务");
		}
		DmCommand isolationOwner = Conn.CreateCommand("");
		Transaction = new DmTransaction(this,il);
		try
		{
			bool confirmed = await SetTransactionIsolationAsync(il,isolationOwner,cancellationToken).ConfigureAwait(false);
			SetAutoCommit(false);
			if(isolationOwner.Statement == null) { await isolationOwner.DisposeAsync().ConfigureAwait(false); isolationOwner = null; }
			return (Transaction,isolationOwner,confirmed);
		}
		catch
		{
			var statement = isolationOwner.Statement;
			if(statement != null)
			{
				RemoveStmt(statement);
				statement.o();
			}
			await isolationOwner.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}
	public async Task CommitAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
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
			await m_Csi.__t02_method_06000A82Async(m_SendMsg, m_RecvMsg, cancellationToken).ConfigureAwait(false);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			ClearTrx();
		}
	}
	public async Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
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
			await m_Csi.bAsync(m_SendMsg, m_RecvMsg, cancellationToken).ConfigureAwait(false);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			ClearTrx();
		}
	}
	internal async Task<bool> RollbackWithoutAutoCommitCheckAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			CheckClosed();
			ClearTrx();
			await m_Csi.bAsync(m_SendMsg, m_RecvMsg, cancellationToken).ConfigureAwait(false);
			do_setTrxFinish(trxFinish: true);
			ConnProperty.ClearAutoCommit();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

}
