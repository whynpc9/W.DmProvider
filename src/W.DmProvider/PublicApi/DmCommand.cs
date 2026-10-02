using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Types;

namespace W.Dm;

public class DmCommand : DbCommand, ICloneable, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmCommand";

	private string m_CommandText = "";

	private CommandType m_CommandType;

	private DmConnection m_Conn;

	private int m_CommandTimeout;

	private DmTransaction m_Trx;

	private global::W.Dm.Internal.Legacy.A.A m_Stmt;
	private DmSession statementSession;
	private DmConnection statementConnection;
	private DmParameterMetadata[] preparedMetadata;

	private ArrayList m_refCursorStmt_arr = new ArrayList();

	private int m_refCursorStmtArr_cur;

	private global::W.Dm.Internal.Legacy.A.A m_RetRefCursorStmt;

	private DmSetValue m_SetValue;

	private DmParameterCollection m_Paras;

	private UpdateRowSource m_UpdateRowSource = UpdateRowSource.Both;

	private bool m_AlreadyDisposed;
	private int disposeStarted;

	private bool m_DesignTimeVisible;

	private DmDataReader rd;

	private bool m_StmtSerial;

	private long executeId = -1L;

	private bool running;
	private readonly DmCommandPlanGate commandPlanGate = new();
	private DmCommandPlan activePlan;
	internal static Action<DmExecutionLease> AfterExecutionCaptured;
	internal static Action AfterPlanCaptured;
	private DmParameterCollection ExecutionParameters => activePlan?.Parameters ?? m_Paras;

	private IDisposable BeginMutation()
	{
		CheckDisposed();
		return commandPlanGate.BeginMutation();
	}

	private DmCommandPlan EnterPlan()
	{
		CheckDisposed();
		DmCommandPlan plan = commandPlanGate.Enter(() =>
		{
			CheckDisposed();
			ValidateSupportedCommand();
			if (ComplexTypeToBytes || ArrayBindCount != 0)
				throw new NotSupportedException("Complex type conversion and array binding are not supported by this provider version.");
			return DmCommandPlan.Capture(m_CommandText, m_CommandType, do_CommandTimeout,
				m_Trx, m_Conn, m_Paras);
		});
		Volatile.Write(ref activePlan, plan);
		try
		{
			Volatile.Read(ref AfterPlanCaptured)?.Invoke();
			if (commandPlanGate.IsDisposed) throw new ObjectDisposedException(nameof(DmCommand));
			return plan;
		}
		catch
		{
			ReleasePlan(plan);
			throw;
		}
	}

	private void ReleasePlan(DmCommandPlan plan)
	{
		if (plan == null) return;
		Interlocked.CompareExchange(ref activePlan, null, plan);
		plan.Dispose();
		if (m_AlreadyDisposed && m_Stmt != null) ReleaseUnmanagedResource();
	}

	private void ValidateSupportedCommand()
	{
		if (m_CommandType != CommandType.Text)
			throw new NotSupportedException("Only CommandType.Text is supported by this provider version.");
		if (string.IsNullOrWhiteSpace(m_CommandText))
			throw new InvalidOperationException("CommandText is required.");
		foreach (DmParameter parameter in m_Paras)
			if (parameter.Direction != ParameterDirection.Input || parameter.DmSqlType is DmDbType.Cursor or DmDbType.RefCursor)
				throw new NotSupportedException("Output and cursor parameters are not supported by this provider version.");
	}

	public bool ComplexTypeToBytes;

	public int ArrayBindCount;

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

	public new DmParameterCollection Parameters => m_Paras;

	internal bool do_DesignTimeVisible
	{
		get
		{
			return m_DesignTimeVisible;
		}
		set
		{
			using var mutation = BeginMutation();
			m_DesignTimeVisible = value;
		}
	}

	internal CommandType do_CommandType
	{
		get
		{
			return m_CommandType;
		}
		set
		{
			using var mutation = BeginMutation();
			if (value != CommandType.StoredProcedure && value != CommandType.TableDirect && value != CommandType.Text)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_ENUM_VALUE);
			}
			m_CommandType = value;
		}
	}

	internal int do_CommandTimeout
	{
		get
		{
			if (m_CommandTimeout != -1)
			{
				return m_CommandTimeout;
			}
			if (m_Conn != null)
			{
				return m_Conn.ConnProperty.CommandTimeout;
			}
			return 30;
		}
		set
		{
			using var mutation = BeginMutation();
			if (value < 0)
				throw new ArgumentOutOfRangeException(nameof(value));
			m_CommandTimeout = value;
		}
	}

	internal string do_CommandText
	{
		get
		{
			return m_CommandText;
		}
		set
		{
			using var mutation = BeginMutation();
			m_CommandText = value;
		}
	}

	internal UpdateRowSource do_UpdatedRowSource
	{
		get
		{
			return m_UpdateRowSource;
		}
		set
		{
			using var mutation = BeginMutation();
			if (value != UpdateRowSource.Both && value != UpdateRowSource.FirstReturnedRecord && value != UpdateRowSource.None && value != UpdateRowSource.OutputParameters)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_ENUM_VALUE);
			}
			m_UpdateRowSource = value;
		}
	}

	internal DmConnection do_DbConnection
	{
		get
		{
			return m_Conn;
		}
		set
		{
			using var mutation = BeginMutation();
			if (ReferenceEquals(value, m_Conn)) return;
			if (m_Trx?.Valid == true)
				throw new InvalidOperationException("Clear the active command transaction before changing its connection.");
			ReleaseStatementForConnectionChange();
			m_Conn = value;
			m_Trx = null;
			m_StmtSerial = false;
			m_SetValue = null;
			rd = null;
			RetCmdType = 0;
			CurResultSetCache = null;
			m_refCursorStmt_arr.Clear();
			m_refCursorStmtArr_cur = 0;
			m_RetRefCursorStmt = null;
			executeId = -1L;
			if (m_Conn != null)
			{
				BaseFilter.CreateFilterChain(this, m_Conn.ConnProperty);
				BaseFilter.CreateFilterChain(m_Paras, m_Conn.ConnProperty);
			}
		}
	}

	internal DmParameterCollection do_DbParameterCollection => m_Paras;

	internal DmTransaction do_DbTransaction
	{
		get
		{
			return m_Trx;
		}
		set
		{
			using var mutation = BeginMutation();
			m_Trx = value;
			m_StmtSerial = m_Trx?.GetStmtSerial() ?? false;
		}
	}

	public override bool DesignTimeVisible
	{
		get
		{
			if (filterHead == null)
			{
				return do_DesignTimeVisible;
			}
			return filterHead.getDesignTimeVisible(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_DesignTimeVisible = value;
			}
			else
			{
				filterHead.setDesignTimeVisible(this, value);
			}
		}
	}

	public override CommandType CommandType
	{
		get
		{
			if (filterHead == null)
			{
				return do_CommandType;
			}
			return filterHead.getCommandType(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_CommandType = value;
			}
			else
			{
				filterHead.setCommandType(this, value);
			}
		}
	}

	public override int CommandTimeout
	{
		get
		{
			if (filterHead == null)
			{
				return do_CommandTimeout;
			}
			return filterHead.getCommandTimeout(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_CommandTimeout = value;
			}
			else
			{
				filterHead.setCommandTimeout(this, value);
			}
		}
	}

	public override string CommandText
	{
		get
		{
			if (filterHead == null)
			{
				return do_CommandText;
			}
			return filterHead.getCommandText(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_CommandText = value;
			}
			else
			{
				filterHead.setCommandText(this, value);
			}
		}
	}

	public override UpdateRowSource UpdatedRowSource
	{
		get
		{
			if (filterHead == null)
			{
				return do_UpdatedRowSource;
			}
			return filterHead.getUpdatedRowSource(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_UpdatedRowSource = value;
			}
			else
			{
				filterHead.setUpdatedRowSource(this, value);
			}
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
		set
		{
			if (filterHead == null)
			{
				do_DbConnection = (DmConnection)value;
			}
			else
			{
				filterHead.setDbConnection(this, (DmConnection)value);
			}
		}
	}

	protected override DbParameterCollection DbParameterCollection
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbParameterCollection;
			}
			return filterHead.getDbParameterCollection(this);
		}
	}

	protected override DbTransaction DbTransaction
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbTransaction;
			}
			return filterHead.getDbTransaction(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_DbTransaction = (DmTransaction)value;
			}
			else
			{
				filterHead.setDbTransaction(this, (DmTransaction)value);
			}
		}
	}

	internal int RetCmdType { get; set; }

	internal DmResultSetCache CurResultSetCache { get; set; }

	internal global::W.Dm.Internal.Legacy.A.A Statement
	{
		get
		{
			return m_Stmt;
		}
		set
		{
			m_Stmt = value;
		}
	}

	internal ArrayList RefCursorStmtArr
	{
		get
		{
			return m_refCursorStmt_arr;
		}
		set
		{
			m_refCursorStmt_arr = value;
		}
	}

	internal int RefCursorStmtArr_cur
	{
		get
		{
			return m_refCursorStmtArr_cur;
		}
		set
		{
			m_refCursorStmtArr_cur = value;
		}
	}

	internal global::W.Dm.Internal.Legacy.A.A RetRefCursorStmt
	{
		get
		{
			return m_RetRefCursorStmt;
		}
		set
		{
			m_RetRefCursorStmt = value;
		}
	}

	public DmCommand()
	{
		m_Paras = new DmParameterCollection();
		m_Paras.Command = this;
		m_Paras.AttachPlanGate(commandPlanGate);
		m_CommandType = CommandType.Text;
		m_CommandTimeout = -1;
	}

	public DmCommand(string cmdText)
		: this()
	{
		m_CommandText = cmdText;
	}

	public DmCommand(string cmdText, DmConnection connection)
		: this(cmdText)
	{
		base.Connection = connection;
	}

	public DmCommand(string cmdText, DmConnection connection, DmTransaction tran)
		: this(cmdText, connection)
	{
		base.Transaction = tran;
	}

	internal void do_Cancel()
	{
		// Capture one plan, including a cancellation request made before its lease exists.
		Volatile.Read(ref activePlan)?.RequestCancellation();
	}

	internal virtual int do_ExecuteNonQuery()
	{
		DmCommandPlan plan = EnterPlan();
		try
		{
			using var lease = BeginValidatedUserExecution(plan, DmOperationPurpose.Query);
			return ExecuteNonQueryComplete(lease, plan);
		}
		finally { ReleasePlan(plan); }
	}

	private int ExecuteNonQueryComplete(DmExecutionLease lease, DmCommandPlan plan)
	{
		DmDataReader reader = null;
		bool completed = false;
		bool verifiedServerError = false;
		try
		{
			using (var invocation = lease.BeginInvocation())
			{
				try
				{
					reader = ExecuteReaderOwned(CommandBehavior.Default, userExecution: true);
					if (reader == null) throw new InvalidOperationException("Execution did not return a reader.");
					reader.AttachExecutionLease(lease, ownsLease: false);
					while (reader.NextResultForCommandOwned()) { }
					int affected = reader.do_RecordsAffected;
					invocation.Complete();
					completed = true;
					return affected;
				}
				catch (DmException error) when (reader == null && IsOwnedVerifiedServerError(error, lease))
				{
					verifiedServerError = true;
					throw;
				}
				catch (Exception error) { throw invocation.TranslateFailure(error); }
			}
		}
		catch (DmException) when (verifiedServerError)
		{
			CleanupAfterVerifiedServerError(lease, plan);
			throw;
		}
		finally
		{
			if (reader != null)
			{
				if (completed) reader.Close();
				else try { reader.Close(); } catch { /* Preserve the execution failure. */ }
			}
		}
	}

	internal int ExecuteInternalNonQuery(DmExecutionLease borrowed)
	{
		if (borrowed == null || m_Conn == null || !ReferenceEquals(borrowed.Session, m_Conn.Session))
			throw new InvalidOperationException("Internal execution lease does not belong to this command.");
		DmCommandPlan plan = EnterPlan();
		bool succeeded = false;
		try
		{
			using (var invocation = borrowed.BeginInvocation())
			{
				int result = ExecuteNonQueryOwned();
				invocation.Complete();
				succeeded = true;
				return result;
			}
		}
		finally
		{
			try
			{
				if (m_Stmt != null)
				{
					using var cleanup = borrowed.BeginCleanupInvocation(plan.CleanupTimeout);
					CleanupCurrentStatement(suppressFailure: !succeeded);
				}
			}
			catch when (!succeeded) { /* Preserve the execution failure. */ }
			catch
			{
				m_Conn?.CloseExpectedSession(borrowed.Session);
				throw;
			}
			finally { ReleasePlan(plan); }
		}
	}

	private int ExecuteNonQueryOwned()
	{
		BeforeExecute();
		if (m_Conn == null || m_Conn.do_State == ConnectionState.Closed)
		{
			throw new InvalidOperationException();
		}
		DmConnInstance connInstance = m_Conn.GetConnInstance();
		if (connInstance == null)
		{
			throw new InvalidOperationException();
		}
		if (StatementInvalid())
		{
			CleanupCurrentStatement(suppressFailure: false);
			m_Stmt = connInstance.GetStmtFromPool(this);
			statementSession = m_Conn.Session;
			statementConnection = m_Conn;
		}
		m_Stmt.__t02_field_04000931 = m_StmtSerial;
		int rowCount = 0;
		try
		{
			if (ExecutionParameters.do_Count == 0)
			{
				rowCount = m_Stmt.c(GetCommandText());
			}
			else
			{
				PrepareInternalCore(checkCommandText: true);
				if (BindParameters(ref rowCount, null, CommandBehavior.Default))
				{
					rowCount = ExecutePreparedUpdate();
				}
			}
			RetCmdType = m_Stmt.m().GetRetStmtType();
			return rowCount;
		}
		finally { AfterExecute(); }
	}

	internal virtual object do_ExecuteScalar()
	{
		DmCommandPlan plan = EnterPlan();
		try
		{
			using var lease = BeginValidatedUserExecution(plan, DmOperationPurpose.Query);
			DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "ExecuteScalar()");
			DmDataReader reader = null;
			bool completed = false;
			bool verifiedServerError = false;
			try
			{
				using (var invocation = lease.BeginInvocation())
				{
					try { reader = ExecuteReaderOwned(CommandBehavior.Default, userExecution: true); }
					catch (DmException error) when (IsOwnedVerifiedServerError(error, lease))
					{
						verifiedServerError = true;
						throw;
					}
					catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
					if (reader == null)
					{
						AbortStatementAfterFailedExecution();
						throw new InvalidOperationException("Execution did not return a reader.");
					}
					reader.AttachExecutionLease(lease, ownsLease: false);
					object scalar = reader.do_FieldCount > 0 && reader.ReadOwned() ? reader.GetValueOwned(0) : null;
					invocation.Complete();
					completed = true;
					return scalar;
				}
			}
			catch (DmException) when (verifiedServerError)
			{
				CleanupAfterVerifiedServerError(lease, plan);
				throw;
			}
			finally
			{
				if (reader != null)
				{
					if (completed) reader.Close();
					else try { reader.Close(); } catch { /* Preserve the read/decode failure. */ }
				}
			}
		}
		finally { ReleasePlan(plan); }
	}

	internal void do_Prepare()
	{
		DmCommandPlan plan = EnterPlan();
		try
		{
			using var lease = BeginValidatedUserExecution(plan, DmOperationPurpose.Query);
			using var invocation = lease.BeginInvocation();
			try { PrepareInternalCore(checkCommandText: false); invocation.Complete(); }
			catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
		}
		finally { ReleasePlan(plan); }
	}

	internal DmParameter do_CreateDbParameter()
	{
		return new DmParameter();
	}

	internal virtual DmDataReader do_ExecuteDbDataReader(CommandBehavior behavior)
	{
		CheckCommandBehavior(behavior);
		DmCommandPlan plan = EnterPlan();
		DmExecutionLease lease = null;
		DmDataReader reader = null;
		bool transferred = false;
		bool verifiedServerError = false;
		try
		{
			lease = BeginValidatedUserExecution(plan, DmOperationPurpose.Reader);
			using (var invocation = lease.BeginInvocation())
			{
				try { reader = ExecuteReaderOwned(behavior, userExecution: true); }
				catch (DmException error) when (IsOwnedVerifiedServerError(error, lease))
				{
					verifiedServerError = true;
					throw;
				}
				catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
				if (reader == null)
				{
					AbortStatementAfterFailedExecution();
					throw new InvalidOperationException("Execution did not return a reader.");
				}
				invocation.Complete();
			}
			reader.AttachExecutionLease(lease);
			reader.AttachCommandPlan(plan, ReleasePlan);
			transferred = true;
			return reader;
		}
		catch
		{
			if (verifiedServerError) CleanupAfterVerifiedServerError(lease, plan);
			if (reader != null && !transferred)
			{
				try { m_Conn?.CloseExpectedSession(lease?.Session); } catch { }
				try { reader.Close(); } catch { }
			}
			lease?.Dispose();
			if (!transferred) ReleasePlan(plan);
			throw;
		}
	}

	internal DmDataReader ExecuteInternalReader(DmExecutionLease borrowed, CommandBehavior behavior)
	{
		if (borrowed == null || m_Conn == null || !ReferenceEquals(borrowed.Session, m_Conn.Session))
			throw new InvalidOperationException("Internal reader lease does not belong to this command.");
		CheckCommandBehavior(behavior);
		DmCommandPlan plan = EnterPlan();
		DmDataReader reader = null;
		try
		{
			using var invocation = borrowed.BeginInvocation();
			try { reader = ExecuteReaderOwned(behavior); }
			catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
			if (reader == null)
			{
				AbortStatementAfterFailedExecution();
				throw new InvalidOperationException("Execution did not return a reader.");
			}
			reader.AttachExecutionLease(borrowed, ownsLease: false);
			reader.AttachCommandPlan(plan, ReleasePlan);
			invocation.Complete();
			return reader;
		}
		catch
		{
			if (reader != null)
			{
				try { m_Conn?.CloseExpectedSession(borrowed.Session); } catch { }
				try { reader.Close(); } catch { }
			}
			ReleasePlan(plan);
			throw;
		}
	}

	private DmDataReader ExecuteReaderOwned(CommandBehavior behavior, bool userExecution = false)
	{
		DmCommandPlan plan = userExecution ? Volatile.Read(ref activePlan) : null;
		DmSqlSavepointControl? rawSavepoint = null;
		if (plan?.Transaction != null &&
			ReferenceEquals(plan.Connection?.Session?.ActiveTransaction, plan.Transaction) &&
			DmParameterBinding.TryParseSavepointControl(plan.Sql, out var control)) rawSavepoint = control;
		BeforeExecute();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "ExecuteReader(CommandBehavior behavior)");
		if (rd != null && !rd.do_IsClosed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATAREADER_ALREADY_OPENED);
		}
		CheckCommandBehavior(behavior);
		if (m_Conn == null || m_Conn.do_State == ConnectionState.Closed)
		{
			throw new InvalidOperationException("connIsNull:" + (m_Conn == null) + " state:" + (m_Conn?.do_State).ToString());
		}
		DmConnInstance connInstance = m_Conn.GetConnInstance();
		if (connInstance == null)
		{
			throw new InvalidOperationException("connInstanceIsNull:" + (connInstance == null));
		}
		if (StatementInvalid())
		{
			CleanupCurrentStatement(suppressFailure: false);
			m_Stmt = connInstance.GetStmtFromPool(this);
			statementSession = m_Conn.Session;
			statementConnection = m_Conn;
		}
		m_Stmt.__t02_field_04000931 = m_StmtSerial;
		if (connInstance.ConnProperty.EnRsCache && !rawSavepoint.HasValue)
		{
			bool flag = false;
			RsKey key = new RsKey(connInstance.ConnProperty.Guid, connInstance.ConnProperty.CurrentSchema, GetCommandText(), do_DbParameterCollection.Count, do_DbParameterCollection);
			DmResultSetCache dmResultSetCache = DmConnection.rsLRUCache.Find(key);
			if (dmResultSetCache != null)
			{
				if (connInstance.ConnProperty.RsRefreshFreq != 0 && (DateTime.Now - dmResultSetCache.lastCheckDt).TotalMilliseconds >= (double)connInstance.ConnProperty.RsRefreshFreq)
				{
					long[] array = m_Stmt.h().A(dmResultSetCache);
					dmResultSetCache.lastCheckDt = DateTime.Now;
					int num = ((array != null) ? array.Length : 0);
					if (num != dmResultSetCache.tss.Length)
					{
						flag = true;
					}
					else
					{
						for (int i = 0; i < num; i++)
						{
							if (dmResultSetCache.tss[i] != array[i])
							{
								flag = true;
							}
						}
					}
				}
				if (!flag)
				{
					m_Stmt.__t02_method_060008B0(dmResultSetCache);
					m_Stmt.__t02_method_060008AE(behavior);
					rd = m_Stmt.__t02_field_04000929;
					RetCmdType = m_Stmt.m().GetRetStmtType();
					CurResultSetCache = m_Stmt.l();
					m_Stmt = null;
					return rd;
				}
			}
		}
		try
		{
			if (ExecutionParameters.do_Count == 0)
			{
				rd = m_Stmt.__t02_method_060008B1(GetCommandText(), behavior);
			}
			else
			{
				PrepareInternalCore(checkCommandText: true);
				int rowCount = 0;
				if (BindParameters(ref rowCount, rd, behavior))
				{
					rd = ExecutePreparedQuery(behavior);
				}
			}
			if (rawSavepoint.HasValue)
			{
				// The executing call has returned a verified response. Record its
				// effects before result navigation/cleanup can fail after execution.
				if (!ReferenceEquals(plan, Volatile.Read(ref activePlan)) || rd == null ||
					m_Stmt.m() == null || m_Stmt.m().IsTerminal || m_Stmt.m().GetHasResultSet())
					throw new InvalidOperationException("Savepoint execution did not return a current control response.");
				plan.Transaction.ConfirmUserSavepointControl(plan.Connection, rawSavepoint.Value,
					DmInvocation.Current?.Lease);
			}
		}
		catch (DmException error) when (IsOwnedVerifiedServerError(error, DmInvocation.Current?.Lease))
		{
			AfterExecute();
			throw;
		}
		catch (Exception error)
		{
			AbortStatementAfterFailedExecution(error);
			throw TranslateCurrentFailure(error);
		}
		try { rd?.SeekFirstReadableResultOwned(); }
		catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
		RetCmdType = m_Stmt.m().GetRetStmtType();
		CurResultSetCache = m_Stmt.l();
		executeId = m_Stmt.H().Execid;
		m_Stmt = null;
		AfterExecute();
		return rd;
	}

	public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
		=> (int)await ExecuteCompleteAsync(false, cancellationToken).ConfigureAwait(false);

	public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
		=> ExecuteCompleteAsync(true, cancellationToken);

	private async Task<object> ExecuteCompleteAsync(bool scalar, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DmCommandPlan plan = EnterPlan();
		DmDataReader reader = null;
		DmExecutionLease lease = null;
		bool completed = false;
		bool verifiedServerError = false;
		try
		{
			lease = BeginValidatedUserExecutionAsync(plan, DmOperationPurpose.Query, cancellationToken);
			try
			{
				using var invocation = lease.BeginInvocation(cancellationToken);
				try
				{
					try { reader = await ExecuteReaderOwnedAsync(CommandBehavior.Default, true, cancellationToken).ConfigureAwait(false); }
					catch (DmException error) when (IsOwnedVerifiedServerError(error, lease))
					{ verifiedServerError = true; throw; }
					reader.AttachExecutionLease(lease, ownsLease: false);
					object result;
					if (scalar)
						result = reader.do_FieldCount > 0 && await reader.ReadOwnedAsync(cancellationToken).ConfigureAwait(false)
							? await reader.GetValueOwnedAsync(0, cancellationToken).ConfigureAwait(false) : null;
					else
					{
						while (await reader.NextResultForCommandOwnedAsync(cancellationToken).ConfigureAwait(false)) { }
						result = reader.do_RecordsAffected;
					}
					invocation.Complete();
					completed = true;
					return result;
				}
				catch (Exception error) { throw invocation.TranslateFailure(error); }
			}
			catch (DmException) when (verifiedServerError)
			{
				await CleanupAfterVerifiedServerErrorAsync(lease, plan).ConfigureAwait(false);
				throw;
			}
		}
		finally
		{
			try
			{
				if (reader != null)
				{
					if (completed) await reader.CloseAsync().ConfigureAwait(false);
					else try { await reader.CloseAsync().ConfigureAwait(false); } catch { }
				}
			}
			finally
			{
				lease?.Dispose();
				await ReleasePlanAsync(plan).ConfigureAwait(false);
			}
		}
	}

	protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		CheckCommandBehavior(behavior);
		DmCommandPlan plan = EnterPlan();
		DmExecutionLease lease = null;
		DmDataReader reader = null;
		bool transferred = false;
		bool verifiedServerError = false;
		try
		{
			lease = BeginValidatedUserExecutionAsync(plan, DmOperationPurpose.Reader, cancellationToken);
			using (var invocation = lease.BeginInvocation(cancellationToken))
			{
				try { reader = await ExecuteReaderOwnedAsync(behavior, true, cancellationToken).ConfigureAwait(false); }
				catch (DmException error) when (IsOwnedVerifiedServerError(error, lease))
				{ verifiedServerError = true; throw; }
				invocation.Complete();
			}
			reader.AttachExecutionLease(lease);
			reader.AttachCommandPlan(plan, ReleasePlan);
			transferred = true;
			return reader;
		}
		catch
		{
			if (verifiedServerError) await CleanupAfterVerifiedServerErrorAsync(lease, plan).ConfigureAwait(false);
			if (reader != null && !transferred)
			{
				lease?.Session.Detach(lease.Identity)?.AbortTransport();
				try { await reader.CloseAsync().ConfigureAwait(false); } catch { }
			}
			throw;
		}
		finally
		{
			if (!transferred)
			{
				lease?.Dispose();
				await ReleasePlanAsync(plan).ConfigureAwait(false);
			}
		}
	}

	public override async Task PrepareAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DmCommandPlan plan = EnterPlan();
		DmExecutionLease lease = null;
		bool verifiedServerError = false;
		try
		{
			lease = BeginValidatedUserExecutionAsync(plan, DmOperationPurpose.Query, cancellationToken);
			try
			{
				using var invocation = lease.BeginInvocation(cancellationToken);
				try { await PrepareInternalCoreAsync(false, cancellationToken).ConfigureAwait(false); invocation.Complete(); }
				catch (DmException error) when (IsOwnedVerifiedServerError(error, lease))
				{ verifiedServerError = true; throw; }
				catch (Exception error) { AbortStatementAfterFailedExecutionAsync(error); throw TranslateCurrentFailure(error); }
			}
			catch (DmException) when (verifiedServerError)
			{
				await CleanupAfterVerifiedServerErrorAsync(lease, plan).ConfigureAwait(false);
				throw;
			}
		}
		finally { lease?.Dispose(); await ReleasePlanAsync(plan).ConfigureAwait(false); }
	}

	internal async Task<int> ExecuteInternalNonQueryAsync(DmExecutionLease borrowed, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (borrowed == null || m_Conn == null || !ReferenceEquals(borrowed.Session, m_Conn.Session))
			throw new InvalidOperationException("Internal execution lease does not belong to this command.");
		DmCommandPlan plan = EnterPlan();
		DmDataReader reader = null;
		bool succeeded = false;
		try
		{
			using var invocation = borrowed.BeginInvocation(cancellationToken);
			try
			{
				reader = await ExecuteReaderOwnedAsync(CommandBehavior.Default, false, cancellationToken).ConfigureAwait(false);
				reader.AttachExecutionLease(borrowed, ownsLease: false);
				while (await reader.NextResultForCommandOwnedAsync(cancellationToken).ConfigureAwait(false)) { }
				int result = reader.do_RecordsAffected;
				invocation.Complete();
				succeeded = true;
				return result;
			}
			catch (Exception error) { throw invocation.TranslateFailure(error); }
		}
		finally
		{
			try
			{
				if (reader != null) await reader.CloseAsync().ConfigureAwait(false);
				else if (m_Stmt != null)
				{
					using var cleanup = borrowed.BeginCleanupInvocation(plan.CleanupTimeout);
					await CleanupCurrentStatementAsync(!succeeded, CancellationToken.None).ConfigureAwait(false);
				}
			}
			catch when (!succeeded) { }
			finally { await ReleasePlanAsync(plan).ConfigureAwait(false); }
		}
	}

	private async Task<DmDataReader> ExecuteReaderOwnedAsync(CommandBehavior behavior, bool userExecution, CancellationToken cancellationToken)
	{
		// Existing locator objects can read their source connection during legacy binding.
		// Reject that path before allocating or preparing any server statement.
		foreach (DmParameter parameter in ExecutionParameters)
			if (parameter.do_Value is AbstractLob)
				throw new NotSupportedException("Binding an existing LOB object asynchronously is not supported; use a byte array or string value.");
		DmCommandPlan plan = userExecution ? Volatile.Read(ref activePlan) : null;
		DmSqlSavepointControl? rawSavepoint = null;
		if (plan?.Transaction != null && ReferenceEquals(plan.Connection?.Session?.ActiveTransaction, plan.Transaction)
			&& DmParameterBinding.TryParseSavepointControl(plan.Sql, out var control)) rawSavepoint = control;
		BeforeExecute();
		try
		{
			if (rd != null && !rd.do_IsClosed)
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATAREADER_ALREADY_OPENED);
			CheckCommandBehavior(behavior);
			await EnsureStatementAsync(cancellationToken).ConfigureAwait(false);
			if (ExecutionParameters.do_Count == 0)
				rd = await m_Stmt.ExecuteReaderAsync(GetCommandText(), behavior, cancellationToken).ConfigureAwait(false);
			else
			{
				await PrepareInternalCoreAsync(true, cancellationToken).ConfigureAwait(false);
				int rowCount = 0;
				if (!BindParameters(ref rowCount, rd, behavior)) throw new InvalidOperationException("Parameter binding failed.");
				rd = await m_Stmt.ExecutePreparedReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
			}
			if (rd == null) throw new InvalidOperationException("Execution did not return a reader.");
			if (rawSavepoint.HasValue)
			{
				if (!ReferenceEquals(plan, Volatile.Read(ref activePlan)) || m_Stmt.m() == null ||
					m_Stmt.m().IsTerminal || m_Stmt.m().GetHasResultSet())
					throw new InvalidOperationException("Savepoint execution did not return a current control response.");
				plan.Transaction.ConfirmUserSavepointControl(plan.Connection, rawSavepoint.Value, DmInvocation.Current?.Lease);
			}
			await rd.SeekFirstReadableResultOwnedAsync(cancellationToken).ConfigureAwait(false);
			RetCmdType = m_Stmt.m().GetRetStmtType();
			CurResultSetCache = m_Stmt.l();
			executeId = m_Stmt.H().Execid;
			m_Stmt = null;
			return rd;
		}
		catch (DmException error) when (IsOwnedVerifiedServerError(error, DmInvocation.Current?.Lease)) { throw; }
		catch (Exception error) { AbortStatementAfterFailedExecutionAsync(error); throw TranslateCurrentFailure(error); }
		finally { AfterExecute(); }
	}

	private async Task EnsureStatementAsync(CancellationToken cancellationToken)
	{
		if (m_Conn == null || m_Conn.do_State == ConnectionState.Closed)
			throw new InvalidOperationException("Command requires an open connection.");
		DmConnInstance instance = m_Conn.GetConnInstance() ?? throw new InvalidOperationException("Connection has no physical instance.");
		if (StatementInvalid())
		{
			await CleanupCurrentStatementAsync(false, cancellationToken).ConfigureAwait(false);
			m_Stmt = await global::W.Dm.Internal.Legacy.A.A.CreateAsync(instance, this, cancellationToken).ConfigureAwait(false);
			statementSession = m_Conn.Session;
			statementConnection = m_Conn;
		}
		m_Stmt.__t02_field_04000931 = m_StmtSerial;
	}

	private async Task PrepareInternalCoreAsync(bool checkCommandText, CancellationToken cancellationToken)
	{
		await EnsureStatementAsync(cancellationToken).ConfigureAwait(false);
		if (!checkCommandText || !m_Stmt.b())
			await m_Stmt.PrepareAsync(GetCommandText(), cancellationToken).ConfigureAwait(false);
		preparedMetadata = Volatile.Read(ref activePlan)?.ParameterMetadata;
	}

	private DmExecutionLease BeginValidatedUserExecutionAsync(DmCommandPlan plan, DmOperationPurpose purpose, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		DmTransaction.ValidateCommandBinding(plan.Connection, plan.Transaction);
		DmTransaction.ValidateCommandSql(plan.Connection, plan.Transaction, plan.Sql, plan.Binding.MarkerCount != 0);
		if (commandPlanGate.IsDisposed) throw new ObjectDisposedException(nameof(DmCommand));
		if (plan.Connection == null) throw new InvalidOperationException("Command has no connection.");
		DmExecutionLease lease = plan.Connection.BeginExecution(purpose, DmDeadline.FromSeconds(plan.TimeoutSeconds, plan.Connection.OperationClock), plan.TimeoutSeconds);
		try
		{
			DmTransaction.ValidateCommandBinding(plan.Connection, plan.Transaction);
			DmTransaction.ValidateCommandSql(plan.Connection, plan.Transaction, plan.Sql, plan.Binding.MarkerCount != 0);
			CaptureExecution(plan, lease);
			return lease;
		}
		catch { lease.Dispose(); throw; }
	}

	private async Task CleanupCurrentStatementAsync(bool suppressFailure, CancellationToken token)
	{
		var statement = m_Stmt;
		m_Stmt = null;
		preparedMetadata = null;
		var owner = statementSession;
		statementSession = null;
		statementConnection = null;
		if (statement == null || statement.P()) return;
		try
		{
			if (owner != null && ReferenceEquals(DmInvocation.Current?.Lease.Session, owner))
			{
				executeId = statement.H()?.Execid ?? -1L;
				await statement.CloseAsync(token).ConfigureAwait(false);
			}
			else statement.o();
		}
		catch
		{
			var lease = DmInvocation.Current?.Lease;
			if (ReferenceEquals(lease?.Session, owner)) owner.Detach(lease.Identity)?.AbortTransport();
			statement.o();
			if (!suppressFailure) throw;
		}
	}

	private void AbortStatementAfterFailedExecutionAsync(Exception error = null)
	{
		if (error != null && DmInvocation.Current?.ShouldAbortAfterFailure(error) == false)
		{ AfterExecute(); return; }
		var lease = DmInvocation.Current?.Lease;
		if (lease != null && (error == null || DmInvocation.Current.ShouldAbortAfterFailure(error)))
			lease.Session.Detach(lease.Identity)?.AbortTransport();
		var statement = m_Stmt;
		m_Stmt = null;
		preparedMetadata = null;
		statementSession = null;
		statementConnection = null;
		try { statement?.o(); } catch { }
		AfterExecute();
	}

	private async Task CleanupAfterVerifiedServerErrorAsync(DmExecutionLease lease, DmCommandPlan plan)
	{
		try
		{
			if (lease == null || m_Stmt == null || m_Stmt.P() || !ReferenceEquals(statementSession, lease.Session))
				throw new InvalidOperationException("Verified server error has no current statement to close.");
			using var cleanup = lease.BeginCleanupInvocation(plan.CleanupTimeout);
			await CleanupCurrentStatementAsync(false, CancellationToken.None).ConfigureAwait(false);
		}
		catch { lease?.Session.Detach(lease.Identity)?.AbortTransport(); }
	}

	private async Task ReleasePlanAsync(DmCommandPlan plan)
	{
		if (plan == null) return;
		Interlocked.CompareExchange(ref activePlan, null, plan);
		plan.Dispose();
		if (m_AlreadyDisposed && m_Stmt != null) await ReleaseUnmanagedResourceAsync().ConfigureAwait(false);
	}

	private async Task ReleaseUnmanagedResourceAsync()
	{
		if (rd != null && !rd.do_IsClosed) return;
		var statement = Interlocked.Exchange(ref m_Stmt, null);
		preparedMetadata = null;
		var owner = statementSession;
		statementSession = null;
		var connection = statementConnection;
		statementConnection = null;
		if (statement == null || statement.P()) return;
		if (owner == null) { statement.o(); return; }
		DmExecutionLease lease = null;
		try
		{
			lease = owner.BeginExecution(DmOperationPurpose.Query,
				DmDeadline.Start(connection?.Settings?.CleanupTimeout ?? TimeSpan.FromSeconds(5), connection?.OperationClock));
			using var invocation = lease.BeginInvocation();
			await statement.CloseAsync(CancellationToken.None).ConfigureAwait(false);
		}
		catch
		{
			if (lease != null) owner.Detach(lease.Identity)?.AbortTransport();
			else connection?.CloseExpectedSession(owner);
			statement.o();
		}
		finally { lease?.Dispose(); }
	}

	public override async ValueTask DisposeAsync()
	{
		commandPlanGate.MarkDisposed();
		if (Interlocked.Exchange(ref disposeStarted, 1) != 0) return;
		m_AlreadyDisposed = true;
		if (!commandPlanGate.IsExecuting) await ReleaseUnmanagedResourceAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	public override void Cancel()
	{
		if (filterHead == null)
		{
			do_Cancel();
		}
		else
		{
			filterHead.Cancel(this);
		}
	}

	public override int ExecuteNonQuery()
	{
		if (filterHead == null)
		{
			return do_ExecuteNonQuery();
		}
		return filterHead.ExecuteNonQuery(this);
	}

	public override object ExecuteScalar()
	{
		if (filterHead == null)
		{
			return do_ExecuteScalar();
		}
		return filterHead.ExecuteScalar(this);
	}

	public override void Prepare()
	{
		if (filterHead == null)
		{
			do_Prepare();
		}
		else
		{
			filterHead.Prepare(this);
		}
	}

	protected override DbParameter CreateDbParameter()
	{
		if (filterHead == null)
		{
			return do_CreateDbParameter();
		}
		return filterHead.CreateDbParameter(this);
	}

	protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
	{
		if (filterHead == null)
		{
			return do_ExecuteDbDataReader(behavior);
		}
		return filterHead.ExecuteDbDataReader(this, behavior);
	}

	internal void ResetSqlAndParameters(DmCommand cmd)
	{
		using var mutation = BeginMutation();
		DmParameterCollection detached = cmd.do_DbParameterCollection.CloneDetached();
		detached.Command = this;
		detached.AttachPlanGate(commandPlanGate);
		m_CommandText = cmd.GetCommandText();
		m_CommandType = cmd.m_CommandType;
		m_CommandTimeout = cmd.m_CommandTimeout;
		m_Paras = detached;
	}

	public void Close()
	{
		ReleaseUnmanagedResource();
	}

	private void CheckDisposed()
	{
		if (m_AlreadyDisposed) throw new ObjectDisposedException(nameof(DmCommand));
	}

	private void ReleaseManagedResource()
	{
	}

	private void CleanupCurrentStatement(bool suppressFailure)
	{
		var statement = m_Stmt;
		m_Stmt = null;
		preparedMetadata = null;
		var owner = statementSession;
		statementSession = null;
		var ownerConnection = statementConnection;
		statementConnection = null;
		if (statement == null || statement.P()) return;
		try
		{
			if (owner != null && ReferenceEquals(DmInvocation.Current?.Lease.Session, owner))
			{
				executeId = statement.H()?.Execid ?? -1L;
				statement.p();
			}
			else statement.o();
		}
		catch
		{
			ownerConnection?.CloseExpectedSession(owner);
			statement.o();
			if (!suppressFailure) throw;
		}
	}

	private void AbortStatementAfterFailedExecution(Exception error = null)
	{
		if (error != null && DmInvocation.Current?.ShouldAbortAfterFailure(error) == false)
		{ AfterExecute(); return; }
		var statement = m_Stmt;
		m_Stmt = null;
		preparedMetadata = null;
		var owner = statementSession;
		statementSession = null;
		var connection = statementConnection;
		statementConnection = null;
		try
		{
			if (owner != null && (error == null || DmInvocation.Current?.ShouldAbortAfterFailure(error) != false))
			{
				var capturedLease = DmInvocation.Current?.Lease;
				if (ReferenceEquals(capturedLease?.Session, owner))
					owner.Detach(capturedLease.Identity)?.AbortTransport();
				else if (owner.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
					connection?.CloseExpectedSession(owner);
			}
		}
		catch { /* Preserve the execution error while closing the captured session. */ }
		finally
		{
			try { statement?.o(); } catch { }
			AfterExecute();
		}
	}

	private void ReleaseUnmanagedResource()
	{
		// A reader owns its statement and command plan until Reader.Close.
		if (rd != null && !rd.do_IsClosed) return;
		var statement = Interlocked.Exchange(ref m_Stmt, null);
		preparedMetadata = null;
		var owner = statementSession;
		statementSession = null;
		var ownerConnection = statementConnection;
		statementConnection = null;
		if (statement == null || statement.P()) return;
		if (owner == null) { statement.o(); return; }
		try
		{
			TimeSpan cleanupTimeout = ownerConnection?.Settings?.CleanupTimeout ?? TimeSpan.FromSeconds(5);
			using var lease = owner.BeginExecution(DmOperationPurpose.Query, DmDeadline.Start(cleanupTimeout, ownerConnection?.OperationClock));
			using var invocation = lease.BeginInvocation();
			statement.p();
		}
		catch
		{
			// A prepared server handle cannot safely wait behind another reader.
			// Close only the session that created it; a reopened connection is untouched.
			ownerConnection?.CloseExpectedSession(owner);
			statement.o();
		}
	}

	private void ReleaseStatementForConnectionChange()
	{
		if (rd != null && !rd.do_IsClosed)
			throw new InvalidOperationException("Close the command reader before changing its connection.");
		ValidateStatementOwnership();
		var owner = statementSession;
		if (m_Stmt == null || m_Stmt.P() || owner == null ||
			owner.State is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed)
		{
			CleanupCurrentStatement(suppressFailure: false);
			return;
		}
		// Acquire before clearing the handle. Another command's reader must keep its
		// session and statement when this connection change cannot obtain ownership.
		TimeSpan cleanupTimeout = statementConnection?.Settings?.CleanupTimeout ?? TimeSpan.FromSeconds(5);
		using var lease = owner.BeginExecution(DmOperationPurpose.Query, DmDeadline.Start(cleanupTimeout, statementConnection?.OperationClock));
		using var invocation = lease.BeginInvocation();
		CleanupCurrentStatement(suppressFailure: false);
	}

	protected override void Dispose(bool disposing)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Dispose(" + disposing + ")");
		commandPlanGate.MarkDisposed();
		if (Interlocked.Exchange(ref disposeStarted, 1) != 0) return;
		m_AlreadyDisposed = true;
		try
		{
			if (disposing)
			{
				ReleaseManagedResource();
			}
			if (!commandPlanGate.IsExecuting) ReleaseUnmanagedResource();
		}
		finally
		{
			base.Dispose(disposing);
		}
	}

	public new void Dispose()
	{
		Dispose(disposing: true);
	}

	internal void IncRefCur()
	{
		m_refCursorStmtArr_cur++;
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

	private void CheckCommandBehavior(CommandBehavior behavior)
	{
		if ((behavior & (CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo)) != 0)
			throw new NotSupportedException("SchemaOnly and KeyInfo require a separately verified metadata path.");
		const CommandBehavior supported = CommandBehavior.SingleResult | CommandBehavior.SingleRow |
			CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection;
		if ((behavior & ~supported) != 0)
			throw new ArgumentOutOfRangeException(nameof(behavior));
	}

	public XmlReader ExecuteXmlReader()
	{
		throw new NotSupportedException("ExecuteXmlReader is unavailable until its nested statement has session ownership.");
	}

	private bool StatementInvalid()
	{
		if (m_Stmt == null || m_Stmt.P())
		{
			return true;
		}
		ValidateStatementOwnership();
		if (!ReferenceEquals(m_Stmt.G()?.Session, m_Conn?.Session)) return true;
		if (!m_Stmt.b() || preparedMetadata == null) return false;
		DmParameterMetadata[] current = Volatile.Read(ref activePlan)?.ParameterMetadata;
		if (current == null || current.Length != preparedMetadata.Length) return true;
		for (int index = 0; index < current.Length; index++)
			if (!current[index].Equals(preparedMetadata[index])) return true;
		return false;
	}

	internal void ValidateStatementOwnership()
	{
		var statement = m_Stmt;
		if (statement == null || statement.P()) return;
		if (!ReferenceEquals(statement.f(), this))
			throw new InvalidOperationException("Statement owner does not match this command.");
		if (statementSession == null || statementConnection == null ||
			!ReferenceEquals(statement.G()?.Session, statementSession))
			throw new InvalidOperationException("Statement does not match its captured physical session.");
	}

	public void PrepareInternal(bool checkCommandText)
	{
		DmCommandPlan plan = EnterPlan();
		try
		{
			using var lease = BeginValidatedUserExecution(plan, DmOperationPurpose.Query);
			using var invocation = lease.BeginInvocation();
			try { PrepareInternalCore(checkCommandText); invocation.Complete(); }
			catch (Exception error) { AbortStatementAfterFailedExecution(error); throw TranslateCurrentFailure(error); }
		}
		finally { ReleasePlan(plan); }
	}

	private void PrepareInternalCore(bool checkCommandText)
	{
		bool flag = false;
		bool flag2 = false;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "PrepareInternal()");
		if (m_Conn == null || m_Conn.do_State == ConnectionState.Closed)
		{
			throw new InvalidOperationException("connIsNull:" + (m_Conn == null) + " state:" + (m_Conn?.do_State).ToString());
		}
		DmConnInstance connInstance = m_Conn.GetConnInstance();
		if (connInstance == null)
		{
			throw new InvalidOperationException("connInstanceIsNull:" + (connInstance == null));
		}
		if (StatementInvalid())
		{
			CleanupCurrentStatement(suppressFailure: false);
			m_Stmt = connInstance.GetStmtFromPool(this);
			statementSession = m_Conn.Session;
			statementConnection = m_Conn;
		}
		m_Stmt.__t02_field_04000931 = m_StmtSerial;
		if (do_CommandType == CommandType.StoredProcedure && ExecutionParameters.do_Count > 0)
		{
			string text = do_CommandText + "(";
			for (int i = 0; i < ExecutionParameters.do_Count; i++)
			{
				if (((DmParameter)ExecutionParameters[i]).do_Direction != ParameterDirection.ReturnValue)
				{
					text += "?,";
					flag = true;
				}
				else if (!flag2)
				{
					flag2 = true;
					text = "? = " + text;
				}
			}
			if (flag)
			{
				text = text.Remove(text.Length - 1, 1);
			}
			text += ")";
			if (!checkCommandText || !m_Stmt.b())
			{
				m_Stmt.D(text);
			}
		}
		else if (!checkCommandText || !m_Stmt.b())
		{
			m_Stmt.D(GetCommandText());
		}
		preparedMetadata = Volatile.Read(ref activePlan)?.ParameterMetadata;
	}

	private DmDataReader ExecutePreparedQuery(CommandBehavior behavior)
	{
		if (m_Conn == null || StatementInvalid())
		{
			throw new InvalidOperationException();
		}
		return m_Stmt.a(behavior);
	}

	private int ExecutePreparedUpdate()
	{
		if (m_Conn == null || StatementInvalid())
		{
			throw new InvalidOperationException();
		}
		return m_Stmt.M();
	}

	private bool BindParameters(ref int rowCount, DmDataReader rd, CommandBehavior behavior)
	{
		DmParameterInternal[] ParamsInfo = null;
		if (m_Conn == null || StatementInvalid())
		{
			return false;
		}
		if (m_SetValue == null)
		{
			m_SetValue = new DmSetValue(m_Conn.GetConnInstance().ConnProperty.ServerEncoding, m_Stmt);
		}
		else
		{
			m_SetValue.ChangeSetValue(m_Conn.GetConnInstance().ConnProperty.ServerEncoding, m_Stmt);
		}
		m_Stmt.H().GetParamsInfo(out ParamsInfo);
		int parameterCount = m_Stmt.H().GetParameterCount();
		DmCommandPlan plan = Volatile.Read(ref activePlan) ??
			throw new InvalidOperationException("Parameter binding requires an active command plan.");
		plan.Binding.ValidateServerCount(parameterCount);
		for (int i = 0; i < parameterCount; i++)
		{
			DmParameterInternal dmParameterInternal = ParamsInfo[i];
			if (dmParameterInternal.GetInOutType() != 0 || dmParameterInternal.GetCType() == 120)
				throw new NotSupportedException("Output and cursor parameters are not supported by this provider version.");
			{
				DmParameter dmParameter = plan.Binding.ResolveServerParameter(i, dmParameterInternal.GetName(), parameterCount);
				var resolved = dmParameter.ResolveType(dmParameterInternal);
				object obj = DmSysTypeConvertion.TypeConvertion(dmParameter);
				int describedCType = dmParameterInternal.GetCType();
				int describedScale = dmParameterInternal.GetScale();
				byte describedFlag = dmParameterInternal.GetTypeFlag();
				SelectBoundCType(dmParameterInternal, resolved.Type, resolved.Source);
				int? effectiveScale = dmParameter.m_SetScaleFlag ? dmParameter.do_Scale : null;
				if (dmParameterInternal.GetCType() == 21)
				{
					int packed = ResolveIntervalDayToSecondScale(describedCType, describedScale, describedFlag,
						dmParameterInternal.mask, effectiveScale);
					dmParameterInternal.SetScale(packed);
					if (describedFlag == 2) dmParameterInternal.SetPrecision(24);
					effectiveScale = packed;
				}
				int? effectivePrecision = dmParameter.m_SetPrecFlag ? dmParameter.do_Precision : null;
				if (describedFlag == 2 && dmParameterInternal.GetCType() == 17 &&
					(!dmParameter.m_SetSizeFlag || dmParameter.do_Size <= 0))
					throw new InvalidOperationException("An explicit fixed BINARY parameter requires a positive Size.");
				if (dmParameterInternal.GetTypeFlag() != 1 && dmParameter.m_SetSizeFlag &&
					dmParameterInternal.GetCType() is 17 or 18)
					effectivePrecision = dmParameterInternal.GetCType() == 18 && dmParameter.do_Size == 0
						? Math.Max(1, obj is byte[] bytes ? bytes.Length : 0) : dmParameter.do_Size;
				m_SetValue.SetResolvedObject(dmParameterInternal.GetParamValue()[0], obj, m_Conn,
					dmParameter.DmSqlTypeName, dmParameterInternal.GetCType(), dmParameterInternal,
					effectivePrecision,
					effectiveScale);
			}
		}
		return true;
	}

	private static void SelectBoundCType(DmParameterInternal serverParameter, DmDbType resolvedType,
		DmParameterTypeSource source)
	{
		int selected = DmSqlType.DmDbTypeToDmSqlType(resolvedType);
		int server = serverParameter.GetCType();
		if (serverParameter.GetTypeFlag() == 1)
		{
			bool explicitSource = source is DmParameterTypeSource.ExplicitDbType or DmParameterTypeSource.ExplicitDmSqlType;
			if (explicitSource && !CompatibleFixedServerType(selected, server))
				throw new InvalidOperationException("Explicit parameter type conflicts with fixed server metadata.");
			return;
		}
		if (serverParameter.GetTypeFlag() != 2)
			throw new NotSupportedException("Unrecognized server parameter binding mode.");
		if (serverParameter.mask != 0 && selected != server)
			throw new NotSupportedException("Decorated server parameter metadata cannot be rebound safely.");
		if (serverParameter.mask == 0 && selected != server)
			serverParameter.resetType(selected); // Advisory describe cannot override explicit or CLR type.
		if (serverParameter.GetCType() != selected)
			throw new InvalidOperationException("Selected parameter wire type was not applied.");
	}

	private static bool CompatibleFixedServerType(int selected, int server)
	{
		if (selected == server) return true;
		bool exactNumeric = selected is 3 or 5 or 6 or 7 or 8 or 9 or 24;
		bool targetNumeric = server is 3 or 5 or 6 or 7 or 8 or 9 or 24;
		if (exactNumeric && targetNumeric) return true; // SetObject checks range and scale exactly.
		if ((selected is 0 or 1 or 2 or 19) && (server is 0 or 1 or 2 or 19)) return true;
		if ((selected is 12 or 17 or 18) && (server is 12 or 17 or 18)) return true;
		if ((selected is 16 or 26) && (server is 16 or 26)) return true;
		if ((selected is 23 or 27) && (server is 23 or 27)) return true;
		return false;
	}

	internal static int ResolveIntervalDayToSecondScale(int serverCType, int serverScale, byte typeFlag,
		int mask, int? explicitFractionalScale)
	{
		int subtype = (serverScale >> 8) & 0xF;
		int leading = (serverScale >> 4) & 0xF;
		int fractional = serverScale & 0xF;
		if (serverCType != 21 || subtype != 6 || leading is < 1 or > 9 || fractional is < 0 or > 6)
		{
			if (typeFlag != 2 || mask != 0)
				throw new NotSupportedException("INTERVAL DAY TO SECOND metadata is not established for this parameter.");
			// DAY(9) TO SECOND(6), 24-byte encoding, passed the target-server profile.
			// Advisory metadata for a bare expression cannot determine the interval layout.
			subtype = 6;
			leading = 9;
			fractional = 6;
		}
		if (explicitFractionalScale is { } requested)
		{
			if (requested is < 0 or > 6)
				throw new ArgumentOutOfRangeException(nameof(explicitFractionalScale));
			if (typeFlag == 1 && requested != fractional)
				throw new InvalidOperationException("Explicit interval scale conflicts with fixed server metadata.");
			fractional = requested;
		}
		return (subtype << 8) | (leading << 4) | fractional;
	}

	internal string GetCommandText()
	{
		DmCommandPlan plan = Volatile.Read(ref activePlan);
		if (plan != null) return plan.Sql;
		if (m_CommandText == null || m_CommandText.Trim().Equals(""))
		{
			throw new InvalidOperationException(new DmError(DmErrorDefinition.ECNET_NO_COMMAND_TEXT).ToStringOnlyInfo());
		}
		if (m_CommandType == CommandType.TableDirect)
		{
			return "SELECT * FROM " + DmStringUtil.GetEscObjName(m_CommandText);
		}
		return m_CommandText;
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	public DmCommand Clone()
	{
		DmCommand dmCommand = new DmCommand(do_CommandText, do_DbConnection, do_DbTransaction);
		dmCommand.do_CommandTimeout = do_CommandTimeout;
		dmCommand.do_CommandType = do_CommandType;
		foreach (DmParameter item in do_DbParameterCollection)
		{
			dmCommand.do_DbParameterCollection.do_Add(item.Clone());
		}
		return dmCommand;
	}

	public string GetExplain()
	{
		return m_Stmt.L();
	}

	public string GetCursorName()
	{
		return m_Stmt.q();
	}

	public void SetCursorName(string name)
	{
		throw new NotSupportedException("Named cursor mutation is not supported.");
	}

	public void ChangeCursorType(byte cursorType)
	{
		throw new NotSupportedException("Cursor type mutation is not supported.");
	}

	public long GetExecuteId()
	{
		if (filterHead == null)
		{
			return do_GetExecuteId();
		}
		return filterHead.getExecuteId(this);
	}

	internal long do_GetExecuteId()
	{
		return executeId;
	}

	internal void BeforeExecute()
	{
		running = true;
	}

	internal void AfterExecute()
	{
		running = false;
	}

	private void CaptureExecution(DmCommandPlan plan, DmExecutionLease lease)
	{
		plan.AttachExecution(lease);
		AfterExecutionCaptured?.Invoke(lease);
	}

	private static Exception TranslateCurrentFailure(Exception error)
		=> DmInvocation.Current?.TranslateFailure(error) ?? error;

	private DmExecutionLease BeginCommandExecution(DmOperationPurpose purpose)
	{
		if (commandPlanGate.IsDisposed) throw new ObjectDisposedException(nameof(DmCommand));
		if (m_Conn == null) throw new InvalidOperationException("Command has no connection.");
		if (m_Stmt != null && statementSession != null && !ReferenceEquals(statementSession, m_Conn.Session))
			ReleaseUnmanagedResource();
		int timeoutSeconds = Volatile.Read(ref activePlan)?.TimeoutSeconds ?? do_CommandTimeout;
		return m_Conn.BeginExecution(purpose, DmDeadline.FromSeconds(timeoutSeconds, m_Conn.OperationClock), timeoutSeconds);
	}

	private DmExecutionLease BeginValidatedUserExecution(DmCommandPlan plan, DmOperationPurpose purpose)
	{
		DmTransaction.ValidateCommandBinding(plan.Connection, plan.Transaction);
		DmTransaction.ValidateCommandSql(plan.Connection, plan.Transaction, plan.Sql, plan.Binding.MarkerCount != 0);
		DmExecutionLease lease = BeginCommandExecution(purpose);
		try
		{
			DmTransaction.ValidateCommandBinding(plan.Connection, plan.Transaction);
			DmTransaction.ValidateCommandSql(plan.Connection, plan.Transaction, plan.Sql, plan.Binding.MarkerCount != 0);
			CaptureExecution(plan, lease);
			return lease;
		}
		catch
		{
			lease.Dispose();
			throw;
		}
	}

	private bool IsOwnedVerifiedServerError(DmException error, DmExecutionLease lease)
	{
		DmInvocation invocation = DmInvocation.Current;
		if (error == null || lease == null || invocation == null || !error.HasVerifiedServerResponse ||
			error.VerifiedResponseIdentity != invocation.Identity ||
			error.VerifiedResponseIdentity.SessionId != lease.Identity.SessionId ||
			error.VerifiedResponseIdentity.LeaseGeneration != lease.Identity.LeaseGeneration ||
			error.VerifiedResponseIdentity.ExecutionId != lease.Identity.ExecutionId)
			return false;
		return ReferenceEquals(lease.Session, m_Conn?.Session) &&
			lease.Session.State == DmPhysicalSessionState.Busy &&
			lease.Session.TransactionState == DmLocalTransactionState.Active;
	}

	private void CleanupAfterVerifiedServerError(DmExecutionLease lease, DmCommandPlan plan)
	{
		try
		{
			if (lease == null || m_Stmt == null || m_Stmt.P() || !ReferenceEquals(statementSession, lease.Session))
				throw new InvalidOperationException("Verified server error has no current statement to close.");
			using var cleanup = lease.BeginCleanupInvocation(plan.CleanupTimeout);
			CleanupCurrentStatement(suppressFailure: false); // STMT_CLOSE response must be fully verified.
		}
		catch
		{
			// The original verified server error remains the caller-visible failure.
			try { m_Conn?.CloseExpectedSession(lease?.Session); } catch { }
		}
	}
}
