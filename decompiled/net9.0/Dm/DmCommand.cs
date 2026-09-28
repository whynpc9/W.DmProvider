using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Xml;
using A;
using Dm.Config;
using Dm.filter;
using Dm.util;

namespace Dm;

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

	private global::A.A m_Stmt;

	private ArrayList m_refCursorStmt_arr = new ArrayList();

	private int m_refCursorStmtArr_cur;

	private global::A.A m_RetRefCursorStmt;

	private DmSetValue m_SetValue;

	private DmParameterCollection m_Paras;

	private UpdateRowSource m_UpdateRowSource = UpdateRowSource.Both;

	private bool m_AlreadyDisposed;

	private bool m_DesignTimeVisible;

	private DmDataReader rd;

	private bool m_StmtSerial;

	private long executeId = -1L;

	private int batchErrorsCount;

	private bool running;

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

	public BaseFilter filterHead { get; set; }

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
			return DmOptionHelper.commandTimeoutDef;
		}
		set
		{
			if ((long)value >= 0L && value <= int.MaxValue)
			{
				m_CommandTimeout = value;
			}
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
			if (value != null)
			{
				m_Conn = value;
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
			m_Trx = value;
			if (value != null && value.Stmt != null)
			{
				Statement = value.Stmt;
			}
			if (m_Trx != null)
			{
				m_StmtSerial = m_Trx.GetStmtSerial();
			}
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

	internal global::A.A Statement
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

	internal global::A.A RetRefCursorStmt
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
		if (running || (rd != null && !rd.IsClosed && rd.FetchedAll))
		{
			m_Conn.Reconnect();
		}
	}

	internal virtual int do_ExecuteNonQuery()
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
		lock (this)
		{
			if (StatementInvalid())
			{
				try
				{
					m_Stmt = connInstance.GetStmtFromPool(this);
				}
				catch (DmException ex)
				{
					throw ex;
				}
				catch (Exception)
				{
				}
			}
		}
		m_Stmt.b = m_StmtSerial;
		int rowCount = 0;
		try
		{
			if (m_Paras.do_Count == 0)
			{
				rowCount = m_Stmt.c(GetCommandText());
			}
			else
			{
				PrepareInternal(checkCommandText: true);
				if (BindParameters(ref rowCount, null, CommandBehavior.Default))
				{
					rowCount = ExecutePreparedUpdate();
				}
			}
			RetCmdType = m_Stmt.m().GetRetStmtType();
		}
		finally
		{
			executeId = m_Stmt.H().Execid;
			m_Stmt.p();
			m_Stmt = null;
		}
		AfterExecute();
		return rowCount;
	}

	internal virtual object do_ExecuteScalar()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "ExecuteScalar()");
		object result = null;
		DmDataReader dmDataReader = do_ExecuteDbDataReader(CommandBehavior.Default);
		if (dmDataReader.do_FieldCount > 0 && dmDataReader.do_Read())
		{
			result = dmDataReader.do_GetValue(0);
		}
		dmDataReader.do_Close();
		return result;
	}

	internal void do_Prepare()
	{
		PrepareInternal(checkCommandText: false);
	}

	internal DmParameter do_CreateDbParameter()
	{
		return new DmParameter();
	}

	internal virtual DmDataReader do_ExecuteDbDataReader(CommandBehavior behavior)
	{
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
		lock (this)
		{
			if (StatementInvalid())
			{
				m_Stmt = connInstance.GetStmtFromPool(this);
			}
		}
		m_Stmt.b = m_StmtSerial;
		if (connInstance.ConnProperty.EnRsCache)
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
					m_Stmt.A(dmResultSetCache);
					m_Stmt.A(behavior);
					rd = m_Stmt.A;
					RetCmdType = m_Stmt.m().GetRetStmtType();
					CurResultSetCache = m_Stmt.l();
					m_Stmt = null;
					return rd;
				}
			}
		}
		try
		{
			if ((Convert.ToByte(behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SchemaOnly))
			{
				m_Stmt.D(GetCommandText());
				m_Stmt.A(behavior);
				rd = m_Stmt.A;
			}
			else if (m_Paras.do_Count == 0)
			{
				rd = m_Stmt.A(GetCommandText(), behavior);
			}
			else
			{
				PrepareInternal(checkCommandText: true);
				int rowCount = 0;
				if (BindParameters(ref rowCount, rd, behavior))
				{
					rd = ExecutePreparedQuery(behavior);
				}
			}
		}
		catch (DmException ex)
		{
			if ((Convert.ToByte(behavior) & 0x3F) == Convert.ToByte(CommandBehavior.CloseConnection))
			{
				m_Conn.do_Close();
			}
			else
			{
				m_Stmt.p();
			}
			throw ex;
		}
		RetCmdType = m_Stmt.m().GetRetStmtType();
		CurResultSetCache = m_Stmt.l();
		executeId = m_Stmt.H().Execid;
		m_Stmt = null;
		AfterExecute();
		return rd;
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
		m_CommandText = cmd.GetCommandText();
		m_CommandType = cmd.m_CommandType;
		m_CommandTimeout = cmd.m_CommandTimeout;
		m_Paras = cmd.do_DbParameterCollection;
	}

	~DmCommand()
	{
		ReleaseUnmanagedResource();
	}

	public void Close()
	{
		ReleaseUnmanagedResource();
	}

	private void CheckDisposed()
	{
		_ = m_AlreadyDisposed;
	}

	private void ReleaseManagedResource()
	{
	}

	private void ReleaseUnmanagedResource()
	{
		if (!StatementInvalid())
		{
			try
			{
			}
			finally
			{
				m_Stmt = null;
			}
		}
	}

	protected override void Dispose(bool disposing)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Dispose(" + disposing + ")");
		if (m_AlreadyDisposed)
		{
			return;
		}
		try
		{
			if (disposing)
			{
				ReleaseManagedResource();
			}
			ReleaseUnmanagedResource();
		}
		finally
		{
			m_AlreadyDisposed = true;
			GC.SuppressFinalize(this);
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
		if (behavior != CommandBehavior.Default && (Convert.ToByte(behavior) & 0x3F) == 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_ENUM_VALUE);
		}
	}

	public XmlReader ExecuteXmlReader()
	{
		XmlReader xmlReader = null;
		DmDataReader dmDataReader = do_ExecuteDbDataReader(CommandBehavior.Default);
		if (dmDataReader.do_FieldCount != 1)
		{
			throw new InvalidOperationException("the command must return xml!");
		}
		try
		{
			global::A.A stmtFromPool = m_Conn.GetConnInstance().GetStmtFromPool((DmCommand)m_Conn.CreateCommand());
			string text = "declare xx clob; cursor c1 for " + GetCommandText() + "; begin open c1; IF c1%rowcount >= 1 THEN begin fetch c1 into xx; select sf_xmlquery(xx, '/'); end; end if; close c1; end;";
			stmtFromPool.A(text, CommandBehavior.Default).do_Close();
			stmtFromPool.p();
			dmDataReader.do_Read();
			xmlReader = XmlReader.Create(dmDataReader.GetClob(0).GetStream());
			dmDataReader.do_Close();
			return xmlReader;
		}
		catch (Exception)
		{
			throw new InvalidOperationException("the command must return xml!");
		}
	}

	private bool StatementInvalid()
	{
		if (m_Stmt == null || m_Stmt.P())
		{
			return true;
		}
		return false;
	}

	public void PrepareInternal(bool checkCommandText)
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
			m_Stmt = connInstance.GetStmtFromPool(this);
		}
		m_Stmt.b = m_StmtSerial;
		if (do_CommandType == CommandType.StoredProcedure && m_Paras.do_Count > 0)
		{
			string text = do_CommandText + "(";
			for (int i = 0; i < m_Paras.do_Count; i++)
			{
				if (((DmParameter)m_Paras[i]).do_Direction != ParameterDirection.ReturnValue)
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
			string text2;
			try
			{
				text2 = GetCommandText();
			}
			catch (Exception)
			{
				text2 = "";
			}
			m_Stmt.D(text2);
		}
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
		bool flag = ArrayUtil.IsAllMatch(m_Paras, ParamsInfo);
		for (int i = 0; i < parameterCount; i++)
		{
			DmParameterInternal dmParameterInternal = ParamsInfo[i];
			if (dmParameterInternal.GetInOutType() != 1 && dmParameterInternal.GetCType() != 120)
			{
				DmParameter dmParameter = (flag ? ((DmParameter)m_Paras[dmParameterInternal.GetName()]) : ((DmParameter)m_Paras[i]));
				if (dmParameter == null)
				{
					continue;
				}
				object obj = DmSysTypeConvertion.TypeConvertion(dmParameter);
				if (m_Conn.compatibleOracle() && dmParameterInternal.GetInOutType() == 0 && m_Paras[i].Direction == ParameterDirection.Output)
				{
					obj = null;
				}
				try
				{
					dmParameterInternal.recommendType(m_Conn, dmParameterInternal, DmSqlType.DmDbTypeToDmSqlType(dmParameter.DmSqlType), obj == null || obj is DBNull);
					m_SetValue.SetObject(dmParameterInternal.GetParamValue()[0], obj, m_Conn, dmParameter.DmSqlTypeName, dmParameterInternal.GetCType(), dmParameterInternal);
				}
				catch (Exception ex)
				{
					if (obj is Array)
					{
						if (dmParameterInternal.GetCType() == 119)
						{
							throw new Exception("非Array类型入参无法使用Array进行绑定");
						}
						ex = null;
						Array array = (Array)obj;
						rowCount = 0;
						for (int j = 0; j < array.Length; j++)
						{
							if (m_Conn.GetConnInstance().ConnProperty.BatchType == 2 || (m_Conn.GetConnInstance().ConnProperty.BatchNotOnCall && m_Stmt.F().GetRetStmtType() == 162))
							{
								try
								{
									m_SetValue.SetObject(dmParameterInternal.GetParamValue()[0], array.GetValue(j), m_Conn, dmParameter.DmSqlTypeName, dmParameterInternal.GetCType(), dmParameterInternal);
									if (rd != null)
									{
										rd = ExecutePreparedQuery(behavior);
									}
									else
									{
										rowCount += ExecutePreparedUpdate();
									}
								}
								catch (Exception ex2)
								{
									ex = ex2;
									if (m_Conn.GetConnInstance().ConnProperty.BatchContinueOnError)
									{
										batchErrorsCount++;
										if (m_Conn.GetConnInstance().ConnProperty.BatchAllowMaxErrors + 1 == batchErrorsCount)
										{
											break;
										}
										continue;
									}
									break;
								}
							}
							else
							{
								if (j > 0)
								{
									dmParameterInternal.GetParamValue().Add(new DmParamValue());
								}
								m_SetValue.SetObject(dmParameterInternal.GetParamValue()[j], array.GetValue(j), m_Conn, dmParameter.DmSqlTypeName, dmParameterInternal.GetCType(), dmParameterInternal);
							}
						}
					}
					if (ex != null)
					{
						throw ex;
					}
					if (m_Conn.GetConnInstance().ConnProperty.BatchType == 2 || (m_Conn.GetConnInstance().ConnProperty.BatchNotOnCall && m_Stmt.F().GetRetStmtType() == 162))
					{
						return false;
					}
				}
			}
			else if (dmParameterInternal.GetCType() == 120)
			{
				DmParameter dmParameter = (flag ? ((DmParameter)m_Paras[dmParameterInternal.GetName()]) : ((DmParameter)m_Paras[i]));
				short inOutType = dmParameterInternal.GetInOutType();
				if ((uint)(inOutType - 1) <= 1u && dmParameter.do_Direction != ParameterDirection.Output && dmParameter.do_Direction != ParameterDirection.InputOutput && dmParameter.do_Direction != ParameterDirection.ReturnValue)
				{
					throw new DmException(new DmError(DmErrorDefinition.EC_PARAM_IO_TYPE_MISMATCH));
				}
				if (dmParameter.do_Direction == ParameterDirection.ReturnValue)
				{
					m_RetRefCursorStmt = m_Conn.GetConnInstance().GetStmtFromPool(this);
					m_RetRefCursorStmt.b(do_CommandTimeout);
					object x = RetRefCursorStmt.g();
					m_SetValue.SetObject(dmParameterInternal.GetParamValue()[0], x, m_Conn, dmParameter.DmSqlTypeName, 7, dmParameterInternal);
				}
				else
				{
					global::A.A stmtFromPool = m_Conn.GetConnInstance().GetStmtFromPool(this);
					stmtFromPool.b(do_CommandTimeout);
					object x = stmtFromPool.g();
					m_SetValue.SetObject(dmParameterInternal.GetParamValue()[0], x, m_Conn, dmParameter.DmSqlTypeName, 7, dmParameterInternal);
					dmParameter.refCursorStmt = stmtFromPool;
					m_refCursorStmt_arr.Add(stmtFromPool);
				}
			}
		}
		return true;
	}

	internal string GetCommandText()
	{
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
			m_Stmt = connInstance.GetStmtFromPool(this);
		}
		m_Stmt.b = m_StmtSerial;
		m_Stmt.d(name);
	}

	public void ChangeCursorType(byte cursorType)
	{
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
			m_Stmt = connInstance.GetStmtFromPool(this);
		}
		m_Stmt.b = m_StmtSerial;
		if (m_Stmt.l() != null)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_RN_INVALID_CURSOR_STATE);
		}
		m_Stmt.A(cursorType);
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
}
