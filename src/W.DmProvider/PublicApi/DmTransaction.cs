using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Transactions;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using W.Dm.util;

namespace W.Dm;

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

	private global::W.Dm.Internal.Legacy.A.A stmt;

	private readonly DmConnInstance connInst;
	private readonly DmSession boundSession;
	private readonly OperationIdentity boundIdentity;
	private readonly int commandTimeoutSeconds;
	private readonly TimeSpan cleanupTimeout;
	private readonly TimeProvider transactionClock;
	private int outcome = (int)DmTransactionOutcome.Starting;
	private string failureInfo;
	private const int MaxSavepoints = 128;
	private const int MaxUserSavepointNameLength = 128;
	private readonly object savepointGate = new();
	private readonly List<SavepointEntry> savepoints = new();
	private int savepointSequence;

	private sealed class SavepointEntry
	{
		internal readonly string UserName;
		internal readonly string ServerName;
		internal SavepointEntry(string userName, string serverName)
		{ UserName = userName; ServerName = serverName; }
	}

	public DisposeStatus _disposeStatus = DisposeStatus.Valid;

	private bool m_StmtSerial;

	private System.Data.IsolationLevel m_il;

	private int disposeStarted;

	public DmTransactionOutcome Outcome => (DmTransactionOutcome)Volatile.Read(ref outcome);
	public override bool SupportsSavepoints =>
		string.Equals(connInst.ConnProperty.ServerVersion, "8.1.5.60", StringComparison.Ordinal);

	/// <summary>A credential-free cleanup or protocol failure category, when one occurred.</summary>
	public string FailureInfo => Volatile.Read(ref failureInfo);

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

	public BaseFilter filterHead
	{
		get => null;
		set
		{
			if (value != null)
				throw new NotSupportedException("Legacy filter injection is unsupported.");
		}
	}

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

	internal global::W.Dm.Internal.Legacy.A.A Stmt
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
		get => Outcome == DmTransactionOutcome.Active;
		set
		{
			if (value != Valid)
				throw new NotSupportedException("Transaction validity is controlled by its session outcome.");
		}
	}

	internal DmTransaction(DmConnInstance connInst)
	{
		BaseFilter.CreateFilterChain(this, connInst.ConnProperty);
		this.connInst = connInst;
		boundSession = connInst.Session;
		boundIdentity = DmInvocation.Current?.Identity ?? throw new InvalidOperationException("Transaction start has no session invocation.");
		commandTimeoutSeconds = connInst.Conn.Settings.CommandTimeout;
		cleanupTimeout = connInst.Conn.Settings.CleanupTimeout;
		transactionClock = connInst.Conn.TransactionClock;
		m_il = System.Data.IsolationLevel.ReadCommitted;
	}

	internal DmTransaction(DmConnInstance connInst, System.Data.IsolationLevel il)
		: this(connInst)
	{
		m_il = il;
	}

	internal void do_Commit()
	{
		DmDeadline deadline = CreateControlDeadline();
		CheckBoundSession();
		using var lease = BeginControlExecution(deadline);
		using var invocation = lease.BeginInvocation();
		CheckBoundSession();
		DmPromotableTransaction currentDmPromotableTransaction = connInst.CurrentDmPromotableTransaction;
		if (currentDmPromotableTransaction != null)
			throw new NotSupportedException("Ambient transaction enlistment is unavailable.");
		boundSession.BeginTransactionControl(this, invocation.Identity, DmTransactionControlKind.Commit);
		try
		{
			connInst.Commit();
			if (Outcome != DmTransactionOutcome.Committed)
				throw new InvalidOperationException("Commit did not receive a confirmed response.");
		}
		catch (Exception ex)
		{
			if (Outcome == DmTransactionOutcome.Committed)
			{
				RecordFailure("post_ack_cleanup_failed");
				AbortUnknownOutcome();
				return;
			}
			boundSession.EndTransactionControl(invocation);
			if (Outcome == DmTransactionOutcome.Active) throw;
			RecordFailure("commit_response_unconfirmed");
			AbortUnknownOutcome();
			throw new DmCommitOutcomeUnknownException(ex);
		}
		finally
		{
			boundSession.EndTransactionControl(invocation);
			if (Outcome != DmTransactionOutcome.Active) Clear();
		}
	}

	internal void do_Rollback()
		=> RollbackCore(CreateControlDeadline(), cleanup: false);

	private void RollbackCore(DmDeadline deadline, bool cleanup)
	{
		CheckBoundSession();
		using var lease = BeginControlExecution(deadline, cleanup);
		using var invocation = lease.BeginInvocation();
		CheckBoundSession();
		DmPromotableTransaction currentDmPromotableTransaction = connInst.CurrentDmPromotableTransaction;
		if (currentDmPromotableTransaction != null)
			throw new NotSupportedException("Ambient transaction enlistment is unavailable.");
		boundSession.BeginTransactionControl(this, invocation.Identity, DmTransactionControlKind.Rollback);
		try
		{
			connInst.Rollback();
			if (Outcome != DmTransactionOutcome.RolledBack)
				throw new InvalidOperationException("Rollback did not receive a confirmed response.");
		}
		catch
		{
			if (Outcome == DmTransactionOutcome.RolledBack)
			{
				RecordFailure("post_ack_cleanup_failed");
				AbortUnknownOutcome();
				return;
			}
			boundSession.EndTransactionControl(invocation);
			if (Outcome == DmTransactionOutcome.Active) throw;
			RecordFailure("rollback_response_unconfirmed");
			AbortUnknownOutcome();
			throw;
		}
		finally
		{
			boundSession.EndTransactionControl(invocation);
			if (Outcome != DmTransactionOutcome.Active) Clear();
		}
	}

	internal void do_Dispose(bool disposing)
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) != 0) return;
		_disposeStatus = DisposeStatus.Disposed;
		GC.SuppressFinalize(this);
		base.Dispose(disposing);
		CleanupOnDispose();
	}

	internal void do_Dispose(bool disposing, StringBuilder msg)
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
		{
			msg.Append("->{_disposeStatus == DisposeStatus.Disposed}");
			return;
		}
		msg.Append("->{_disposeStatus = DisposeStatus.Disposed;GC.SuppressFinalize(this);}");
		_disposeStatus = DisposeStatus.Disposed;
		GC.SuppressFinalize(this);
		msg.Append("->{base.Dispose(disposing);}");
		base.Dispose(disposing);
		msg.Append("->{CleanupOnDispose()}");
		CleanupOnDispose();
	}

	private void CleanupOnDispose()
	{
		DmDeadline cleanupDeadline = DmDeadline.Start(cleanupTimeout, transactionClock);
		try
		{
			if (Outcome is DmTransactionOutcome.Committed or DmTransactionOutcome.RolledBack or
				DmTransactionOutcome.CompletedExternally) return;
			if (Outcome != DmTransactionOutcome.Active)
			{
				AbortBoundSession();
				return;
			}
			if (boundSession.TryGetActiveReader(out DmDataReader reader))
			{
				try { reader.CloseForTransactionCleanup(cleanupDeadline); }
				catch { RecordFailure("reader_cleanup_failed"); }
			}
			TimeSpan timeout = this.cleanupTimeout;
			if (timeout <= TimeSpan.Zero || boundSession.State != DmPhysicalSessionState.Ready)
			{
				RecordFailure("reader_or_session_not_synchronized");
				AbortBoundSession();
				return;
			}
			try { RollbackCore(cleanupDeadline, cleanup: true); }
			catch
			{
				RecordFailure("dispose_rollback_unconfirmed");
				AbortBoundSession();
			}
		}
		catch
		{
			RecordFailure("dispose_cleanup_failed");
			try { AbortBoundSession(); } catch { }
		}
		finally { Clear(); }
	}

	internal DmSavePoint do_Save(string savepointName)
	{
		DmDeadline deadline = CreateControlDeadline();
		RequireSavepoints();
		ValidateSavepointName(savepointName);
		CheckBoundSession();
		using var lease = BeginControlExecution(deadline);
		CheckBoundSession();
		if (connInst.GetAutoCommit())
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		string serverName;
		lock (savepointGate)
		{
			if (savepointSequence >= MaxSavepoints)
				throw new InvalidOperationException("Transaction savepoint limit reached.");
			serverName = "WSP_" + (++savepointSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		ExecuteSavepointSql(lease, "SAVEPOINT \"" + serverName + "\"");
		lock (savepointGate)
		{
			savepoints.RemoveAll(entry => string.Equals(entry.UserName, savepointName, StringComparison.Ordinal));
			savepoints.Add(new SavepointEntry(savepointName, serverName));
		}
		return DmSavePoint.CreateHandle(connInst.Conn, this, savepointName);
	}

	internal void do_Rollback(string savepointName)
	{
		DmDeadline deadline = CreateControlDeadline();
		RequireSavepoints();
		ValidateSavepointName(savepointName);
		CheckBoundSession();
		using var lease = BeginControlExecution(deadline);
		CheckBoundSession();
		if (connInst.GetAutoCommit() && !connInst.ConnProperty.AlwaysAllowAutoCommit)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_ROLLBACK_TO_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		SavepointEntry target = FindSavepoint(savepointName);
		ExecuteSavepointSql(lease, "ROLLBACK TO SAVEPOINT \"" + target.ServerName + "\"");
		lock (savepointGate)
		{
			int index = savepoints.IndexOf(target);
			if (index >= 0) savepoints.RemoveRange(index + 1, savepoints.Count - index - 1);
		}
	}

	internal void do_Release(string savepointName)
	{
		DmDeadline deadline = CreateControlDeadline();
		RequireSavepoints();
		ValidateSavepointName(savepointName);
		CheckBoundSession();
		using var lease = BeginControlExecution(deadline);
		CheckBoundSession();
		if (connInst.GetAutoCommit() && !connInst.ConnProperty.AlwaysAllowAutoCommit)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_RELEASE_SAVEPOINT_IN_AUTOCOMMIT_MODE);
		SavepointEntry target = FindSavepoint(savepointName);
		ExecuteSavepointSql(lease, "RELEASE SAVEPOINT \"" + target.ServerName + "\"");
		lock (savepointGate)
		{
			int index = savepoints.IndexOf(target);
			if (index >= 0) savepoints.RemoveRange(index, savepoints.Count - index);
		}
	}

	private void RequireSavepoints()
	{
		if (!SupportsSavepoints)
			throw new NotSupportedException("Savepoints are not verified for this server profile.");
	}

	private static void ValidateSavepointName(string name)
	{
		if (string.IsNullOrEmpty(name) || name.Length > MaxUserSavepointNameLength || name.IndexOf('\0') >= 0)
			throw new ArgumentException("Savepoint name must be nonempty, at most 128 characters, and contain no NUL.", nameof(name));
	}

	private SavepointEntry FindSavepoint(string userName)
	{
		lock (savepointGate)
		{
			for (int index = savepoints.Count - 1; index >= 0; index--)
				if (string.Equals(savepoints[index].UserName, userName, StringComparison.Ordinal))
					return savepoints[index];
		}
		throw new InvalidOperationException("Savepoint is not active in this transaction.");
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

	internal void CheckBoundSession()
	{
		CheckValid();
		if (boundSession == null || !ReferenceEquals(connInst.Session, boundSession) ||
			!boundSession.IsCurrent(boundIdentity) ||
			!ReferenceEquals(connInst.Conn?.Session, boundSession) ||
			!ReferenceEquals(connInst.Conn?.m_ConnInst, connInst) ||
			!ReferenceEquals(connInst.Transaction, this) ||
			boundSession.State is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
			throw new InvalidOperationException("Transaction belongs to a closed or replaced session.");
	}

	// Run both before and after a user command acquires its root execution lease.
	// The second check closes the race with Commit/Dispose between preflight and
	// lease acquisition. Internal transaction-control commands use their borrowed
	// lease and do not pass through this user-command guard.
	internal static void ValidateCommandBinding(DmConnection connection, DmTransaction selected)
	{
		if (connection == null || connection.Session == null || connection.do_State != ConnectionState.Open)
			throw new InvalidOperationException("Command connection is not open.");
		DmConnInstance current = connection.GetConnInstance();
		if (current == null || !ReferenceEquals(current.Session, connection.Session))
			throw new InvalidOperationException("Command connection changed physical session.");
		DmTransaction active = connection.Session.ActiveTransaction;
		if (active != null && !ReferenceEquals(current.Transaction, active))
			throw new InvalidOperationException("Connection transaction and session owner disagree.");
		if (active != null && !ReferenceEquals(active, selected))
			throw new InvalidOperationException("Command must use the active local transaction.");
		if (selected == null) return;
		if (!ReferenceEquals(selected.connInst.Conn, connection) || !ReferenceEquals(selected.connInst, current))
			throw new InvalidOperationException("Command transaction belongs to another connection.");
		selected.CheckBoundSession();
		if (selected.Outcome != DmTransactionOutcome.Active)
			throw new InvalidOperationException("Command transaction is not active.");
	}

	// The lexical scanner retains the user's complete SQL unchanged. This narrow
	// gate blocks transaction-control escape hatches whose effects would bypass
	// the bound transaction's outcome state; broader DDL policy is profile-gated.
	internal static void ValidateCommandSql(DmConnection connection, DmTransaction selected, string sql,
		bool hasBoundParameters = false)
	{
		if (connection?.Session?.ActiveTransaction == null ||
			!ReferenceEquals(connection.Session.ActiveTransaction, selected)) return;
		var statements = DmParameterBinding.ScanTopLevelStatements(sql);
		foreach (DmSqlStatementHead statement in statements)
		{
			bool forbidden = statement.First switch
			{
				"COMMIT" => true,
				"ROLLBACK" => !DmParameterBinding.IsStrictRollbackToSavepoint(sql),
				"SET" => statement.Second is "TRANSACTION" or "AUTOCOMMIT",
				"CALL" or "EXEC" or "EXECUTE" or "BEGIN" or "DECLARE" => true,
				"CREATE" or "ALTER" or "DROP" or "TRUNCATE" =>
					statement.Second != "TABLE" || !selected.SupportsVerifiedDdlCompletion ||
					statements.Count != 1 || hasBoundParameters,
				"GRANT" or "REVOKE" or "AUDIT" or "COMMENT" or "RENAME" => true,
				"RELEASE" => statement.Second != "SAVEPOINT",
				_ => false
			};
			if (forbidden)
				throw new NotSupportedException("Direct transaction or dynamic control SQL is unavailable inside a local transaction.");
		}
	}

	private bool SupportsVerifiedDdlCompletion =>
		string.Equals(connInst.ConnProperty.ServerVersion, "8.1.5.60", StringComparison.Ordinal);

	internal void SetOutcomeFromSession(DmTransactionOutcome next)
	{
		while (true)
		{
			DmTransactionOutcome current = Outcome;
			if (current is DmTransactionOutcome.Committed or DmTransactionOutcome.RolledBack or
				DmTransactionOutcome.CompletedExternally) return;
			if (Interlocked.CompareExchange(ref outcome, (int)next, (int)current) == (int)current) return;
		}
	}

	internal void InvalidateForSessionLoss()
	{
		SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
		Stmt = null;
	}

	internal void RecordFailure(string category) => Volatile.Write(ref failureInfo, category);

	internal void ExecuteSavepointSql(DmExecutionLease lease, string sql)
	{
		if (lease == null || !ReferenceEquals(lease.Session, boundSession))
			throw new InvalidOperationException("Savepoint has no bound session owner.");
		CheckBoundSession();
		using var command = connInst.Conn.CreateCommand(sql);
		command.ExecuteInternalNonQuery(lease);
	}

	private DmDeadline CreateControlDeadline() =>
		DmDeadline.FromSeconds(commandTimeoutSeconds, transactionClock);

	private DmExecutionLease BeginControlExecution(DmDeadline deadline, bool cleanup = false)
	{
		return boundSession.BeginExecution(DmOperationPurpose.TransactionControl,
			deadline, cleanup ? 0 : commandTimeoutSeconds);
	}

	private void AbortUnknownOutcome()
	{
		// Preserve the original protocol error. A failed close must still invalidate
		// only this transaction's physical session, never a later Open's session.
		try { AbortBoundSession(); }
		catch { }
	}

	private void AbortBoundSession()
	{
		try { connInst.Conn?.CloseExpectedSession(boundSession); }
		finally
		{
			if (boundSession.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
				boundSession.Detach()?.AbortTransport();
		}
	}

	public void Clear()
	{
		if (Outcome is DmTransactionOutcome.Starting or DmTransactionOutcome.Active or
			DmTransactionOutcome.Committing or DmTransactionOutcome.RollingBack)
			throw new NotSupportedException("An active transaction cannot be cleared without a confirmed outcome.");
		Stmt = null;
	}
}
