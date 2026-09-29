using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Transactions;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

public sealed class DmConnection : DbConnection, ICloneable, IFilterInfo
{
	public enum ConnectionStatusInTransactionScope
	{
		Open,
		Closed,
		Disposed
	}

	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmConnection";

	internal DmConnInstance m_ConnInst;

	private volatile bool m_AlreadyDisposed;

	private DmSchema m_Schema;

	private bool forEFCore;

	private ConnectionState connectionState;

	internal static RsLRUCache rsLRUCache;

	internal static object obj = new object();

	internal ConnectionStatusInTransactionScope StatusInTransactionScope;

	public static Assembly DmSkyWalkingAgentAssembly;

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

	internal string do_ServerVersion
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				throw new InvalidOperationException("connection is closed");
			}
			return GetConnInstance().ConnProperty.ServerVersion;
		}
	}

	internal string do_DataSource
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				return null;
			}
			return ConnProperty.Server;
		}
	}

	internal string do_Database
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				return "";
			}
			DmConnInstance connInstance = GetConnInstance();
			if (connInstance == null)
			{
				return ConnProperty.Database;
			}
			return connInstance.ConnProperty.Database;
		}
	}

	internal int do_ConnectionTimeout => ConnProperty.ConnectionTimeout;

	internal string do_ConnectionString
	{
		get
		{
			return ConnProperty.ConnectionString;
		}
		set
		{
			ConnProperty.ConnectionString = value;
			if (rsLRUCache == null && ConnProperty.EnRsCache)
			{
				lock (obj)
				{
					if (rsLRUCache == null)
					{
						rsLRUCache = new RsLRUCache(ConnProperty.RsCacheSize * 1024 * 1024);
					}
				}
			}
			BaseFilter.CreateFilterChain(this, ConnProperty);
		}
	}

	internal ConnectionState do_State
	{
		get
		{
			return connectionState;
		}
		set
		{
			ConnectionState originalState = connectionState;
			connectionState = value;
			OnStateChange(new StateChangeEventArgs(originalState, connectionState));
		}
	}

	internal DbProviderFactory do_DbProviderFactory => DmClientFactory.Instance;

	public override string ServerVersion
	{
		get
		{
			if (filterHead == null)
			{
				return do_ServerVersion;
			}
			return filterHead.getServerVersion(this);
		}
	}

	public override string DataSource
	{
		get
		{
			if (filterHead == null)
			{
				return do_DataSource;
			}
			return filterHead.getDataSource(this);
		}
	}

	public override string Database
	{
		get
		{
			if (filterHead == null)
			{
				return do_Database;
			}
			return filterHead.getDatabase(this);
		}
	}

	public override int ConnectionTimeout
	{
		get
		{
			if (filterHead == null)
			{
				return do_ConnectionTimeout;
			}
			return filterHead.getConnectionTimeout(this);
		}
	}

	public override string ConnectionString
	{
		get
		{
			if (filterHead == null)
			{
				return do_ConnectionString;
			}
			return filterHead.getConnectionString(this);
		}
		set
		{
			do_ConnectionString = value;
		}
	}

	protected override DbProviderFactory DbProviderFactory
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbProviderFactory;
			}
			return filterHead.getDbProviderFactory(this);
		}
	}

	public override ConnectionState State
	{
		get
		{
			if (filterHead == null)
			{
				return do_State;
			}
			return filterHead.getState(this);
		}
	}

	public DmMppType MppType
	{
		get
		{
			if (1 == ConnProperty.MppType)
			{
				return DmMppType.LOGIN_MPP_LOCAL;
			}
			return DmMppType.LOGIN_MPP_GLOBAL;
		}
		set
		{
			if (value == DmMppType.LOGIN_MPP_LOCAL)
			{
				ConnProperty.MppType = 1;
				return;
			}
			if (DmMppType.LOGIN_MPP_GLOBAL == value)
			{
				ConnProperty.MppType = 0;
				return;
			}
			throw new InvalidOperationException("invalid mpp status");
		}
	}

	public string User
	{
		get
		{
			if (ConnProperty == null)
			{
				return null;
			}
			return ConnProperty.User;
		}
	}

	public string Password
	{
		get
		{
			if (ConnProperty == null)
			{
				return null;
			}
			return ConnProperty.Pwd;
		}
	}

	internal DmConnProperty ConnProperty { get; set; }

	public string Schema
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				return "";
			}
			string schema = GetConnInstance().ConnProperty.Schema;
			if (schema.isEmpty())
			{
				return "SYSDBA";
			}
			return schema;
		}
		set
		{
			ConnProperty.Schema = value;
		}
	}

	public bool ForEFCore
	{
		get
		{
			return forEFCore;
		}
		set
		{
			forEFCore = value;
		}
	}

	public string getConnPoolKey()
	{
		return ConnProperty.ServName + "/" + ConnProperty.User + "/" + ConnProperty.PropertyHashCode;
	}

	public DmConnection()
	{
		ConnProperty = new DmConnProperty();
	}

	public DmConnection(bool forEF)
		: this()
	{
		ForEFCore = true;
	}

	public DmConnection(string connectionString)
		: this()
	{
		do_ConnectionString = connectionString;
	}

	public DmConnection(string connectionString, bool forEFCore)
		: this(forEFCore)
	{
		do_ConnectionString = connectionString;
	}

	internal DmTransaction do_BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
	{
		if (isolationLevel == System.Data.IsolationLevel.Unspecified)
		{
			isolationLevel = GetConnInstance().ConnProperty.IsolationLevel;
		}
		if (do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED, "do_BeginDbTransaction:" + do_State);
		}
		return m_ConnInst.BeginTrx(isolationLevel);
	}

	internal void do_ChangeDatabase(string databaseName)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_DO_NOT_SUPPORT_CATALOG);
	}

	internal void do_Close()
	{
		StringBuilder stringBuilder = new StringBuilder("{do_Close}");
		try
		{
			stringBuilder.Append("->{ReleaseUnmanagedResource(false)}");
			ReleaseUnmanagedResource(pooled: false, stringBuilder);
			stringBuilder.Append("->{do_State = ConnectionState.Closed;}");
			do_State = ConnectionState.Closed;
			stringBuilder.Append("->{end}");
		}
		catch (Exception ex)
		{
			throw new Exception(ex.Message + "\n" + ex.StackTrace + "\n[extraInfo]:" + stringBuilder.ToString());
		}
	}

	internal DmCommand do_CreateDbCommand()
	{
		DmCommand dmCommand = CreateDmTracingCommand(this);
		if (dmCommand != null)
		{
			return dmCommand;
		}
		return new DmCommand("", this);
	}

	internal void Reconnect()
	{
		do_Close();
		lock (this)
		{
			Connect();
		}
	}

	internal void do_EnlistTransaction(Transaction transaction)
	{
		StringBuilder stringBuilder = new StringBuilder("{do_EnlistTransaction(" + transaction?.ToString() + ")}");
		try
		{
			if (transaction == null)
			{
				stringBuilder.Append("->{transaction == null}");
				return;
			}
			if (m_ConnInst == null)
			{
				stringBuilder.Append("->{m_ConnInst == null}");
				throw new InvalidOperationException("connInstance is null");
			}
			if (m_ConnInst.CurrentDmPromotableTransaction != null)
			{
				if (m_ConnInst.CurrentDmPromotableTransaction.BaseTransaction == transaction)
				{
					stringBuilder.Append("->{m_ConnInst.CurrentDmPromotableTransaction.BaseTransaction == " + transaction?.ToString() + "}");
					return;
				}
				stringBuilder.Append("->{m_ConnInst.CurrentDmPromotableTransaction.BaseTransaction != " + transaction?.ToString() + "}");
				throw new InvalidOperationException("Already enlisted by a different transaction");
			}
			stringBuilder.Append("->{GetDmPromotableTransactionInTransaction(" + transaction?.ToString() + ")}");
			DmPromotableTransaction dmPromotableTransaction = DmPromotableTransactionTransactionManager.GetDmPromotableTransactionInTransaction(transaction);
			if (dmPromotableTransaction == null)
			{
				stringBuilder.Append("->{new DmPromotableTransaction(" + transaction?.ToString() + ")");
				dmPromotableTransaction = new DmPromotableTransaction(transaction, stringBuilder);
				stringBuilder.Append("->{SetDmPromotableTransactionInTransaction(" + dmPromotableTransaction?.ToString() + ")}");
				DmPromotableTransactionTransactionManager.SetDmPromotableTransactionInTransaction(dmPromotableTransaction);
				if (!transaction.EnlistPromotableSinglePhase(dmPromotableTransaction))
				{
					stringBuilder.Append("->{!transaction.EnlistPromotableSinglePhase(" + dmPromotableTransaction?.ToString() + ")}");
					throw new InvalidOperationException("transaction.EnlistPromotableSinglePhase failed");
				}
			}
			stringBuilder.Append("->{FindExistingEnlistedConnInstance(" + dmPromotableTransaction?.ToString() + ")}");
			if (!FindExistingEnlistedConnInstance(dmPromotableTransaction))
			{
				stringBuilder.Append("->{Enqueue(" + this?.ToString() + ")}");
				dmPromotableTransaction.Enqueue(this, stringBuilder);
				stringBuilder.Append("->{m_ConnInst.CurrentDmPromotableTransaction = dmPromotableTransaction}");
				m_ConnInst.CurrentDmPromotableTransaction = dmPromotableTransaction;
			}
			stringBuilder.Append("->{end}");
		}
		catch (Exception ex)
		{
			throw new Exception(ex.Message + "\n" + ex.StackTrace + "\n[extraInfo]:" + stringBuilder.ToString());
		}
	}

	internal bool FindExistingEnlistedConnInstance(DmPromotableTransaction dmPromotableTransaction)
	{
		foreach (DmTransactionScope item in dmPromotableTransaction.ScopeQueue)
		{
			DmConnection connection = item.Connection;
			if (connection.ConnectionString == ConnectionString)
			{
				Close();
				m_ConnInst = connection.m_ConnInst;
				m_ConnInst.SetDmConnection(this);
				do_State = ConnectionState.Open;
				item.Connection = this;
				return true;
			}
		}
		return false;
	}

	internal DataTable do_GetSchema()
	{
		return do_GetSchema(null);
	}

	internal DataTable do_GetSchema(string collectionName)
	{
		if (collectionName == null || collectionName.Equals(""))
		{
			collectionName = "METADATACOLLECTIONS";
		}
		return do_GetSchema(collectionName, null);
	}

	internal DataTable do_GetSchema(string collectionName, string[] restrictionValues)
	{
		if (m_Schema == null)
		{
			m_Schema = new DmSchema(this);
		}
		if (collectionName == null || collectionName.Equals(""))
		{
			collectionName = "METADATACOLLECTIONS";
		}
		return m_Schema.GetSchema(collectionName, restrictionValues);
	}

	internal void do_Open()
	{
		if (do_State == ConnectionState.Open)
		{
			return;
		}
		if (do_State == ConnectionState.Broken)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR);
		}
		ConnProperty.encryptPwd = false;
		ConnProperty.encryptMsg = false;
		ConnProperty.msgVersion = 21;
		CheckProperty();
		m_ConnInst = new DmConnInstance(this);
		try
		{
			do_State = ConnectionState.Open;
			DriverUtil.executeSetSchema(this);
		}
		catch (Exception)
		{
			do_State = ConnectionState.Closed;
			throw;
		}
	}

	internal DmStruct do_CreateStruct(string typeName, object[] attributes)
	{
		checkClosed();
		return new DmStruct(new ComplexTypeDesc(typeName, this), this, attributes);
	}

	internal DmArray do_CreateArray(string typeName, object[] elements)
	{
		checkClosed();
		return new DmArray(new ComplexTypeDesc(typeName, this), this, elements);
	}

	internal DmStruct do_CreateIndexTable(string typeName, Dictionary<string, object> dictionary)
	{
		checkClosed();
		return new DmStruct(new ComplexTypeDesc(typeName, this), this, dictionary);
	}

	protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
	{
		if (filterHead == null)
		{
			return do_BeginDbTransaction(isolationLevel);
		}
		return filterHead.BeginDbTransaction(this, isolationLevel);
	}

	public override void ChangeDatabase(string databaseName)
	{
		if (filterHead == null)
		{
			do_ChangeDatabase(databaseName);
		}
		else
		{
			filterHead.ChangeDatabase(this, databaseName);
		}
	}

	public override void Close()
	{
		if (ExistDmPromotableTransaction())
		{
			StatusInTransactionScope = ConnectionStatusInTransactionScope.Closed;
		}
		else if (m_ConnInst?.Conn == this)
		{
			if (filterHead == null)
			{
				do_Close();
			}
			else
			{
				filterHead.Close(this);
			}
		}
	}

	public void ForceClose()
	{
		if (ExistDmPromotableTransaction())
		{
			StatusInTransactionScope = ConnectionStatusInTransactionScope.Closed;
		}
		else if (m_ConnInst?.Conn == this)
		{
			if (filterHead == null)
			{
				do_Close();
			}
			else
			{
				filterHead.ForceClose(this);
			}
		}
	}

	protected override DbCommand CreateDbCommand()
	{
		if (filterHead == null)
		{
			return do_CreateDbCommand();
		}
		return filterHead.CreateDbCommand(this);
	}

	public DmCommand CreateCommand(string cmdText)
	{
		DmCommand dmCommand = do_CreateDbCommand();
		dmCommand.CommandText = cmdText;
		return dmCommand;
	}

	public override void EnlistTransaction(Transaction transaction)
	{
		if (filterHead == null)
		{
			do_EnlistTransaction(transaction);
		}
		else
		{
			filterHead.EnlistTransaction(this, transaction);
		}
	}

	public override DataTable GetSchema()
	{
		if (filterHead == null)
		{
			return do_GetSchema();
		}
		return filterHead.GetSchema(this);
	}

	public override DataTable GetSchema(string collectionName)
	{
		if (filterHead == null)
		{
			return do_GetSchema(collectionName);
		}
		return filterHead.GetSchema(this, collectionName);
	}

	public override DataTable GetSchema(string collectionName, string[] restrictionValues)
	{
		if (filterHead == null)
		{
			return do_GetSchema(collectionName, restrictionValues);
		}
		return filterHead.GetSchema(this, collectionName, restrictionValues);
	}

	public override void Open()
	{
		if (m_AlreadyDisposed)
		{
			throw new ObjectDisposedException("DmConnection");
		}
		if (ExistDmPromotableTransaction())
		{
			StatusInTransactionScope = ConnectionStatusInTransactionScope.Open;
			return;
		}
		if (filterHead == null)
		{
			Connect();
		}
		else
		{
			filterHead.Open(this);
		}
		if (ConnProperty.Enlist && Transaction.Current != null)
		{
			do_EnlistTransaction(Transaction.Current);
		}
	}

	internal void Connect()
	{
		if (do_State != ConnectionState.Open)
		{
			ConnProperty.EPGroup.connect(this);
		}
	}

	internal int getIndexOnDBGroup()
	{
		if (ConnProperty.EPGroup == null || ConnProperty.EPGroup.epList == null)
		{
			return -1;
		}
		EPGroup ePGroup = ConnProperty.EPGroup;
		for (int i = 0; i < ePGroup.epList.Count; i++)
		{
			EP eP = ePGroup.epList[i];
			if (ConnProperty.Server.Equals(eP.host, StringComparison.OrdinalIgnoreCase) && ConnProperty.Port == eP.port)
			{
				return i;
			}
		}
		return -1;
	}

	public bool getConnPooling()
	{
		return ConnProperty.ConnPooling;
	}

	~DmConnection()
	{
		try
		{
			try
			{
				ForceDispose();
			}
			catch (Exception)
			{
			}
		}
		finally
		{
			GC.SuppressFinalize(this);
		}
	}

	public void ClearAllPools(bool pooled)
	{
		ReleaseUnmanagedResource(pooled);
	}

	internal void ReleaseUnmanagedResource(bool pooled)
	{
		if (do_State != ConnectionState.Closed && m_ConnInst != null)
		{
			if (m_ConnInst.Transaction != null)
			{
				m_ConnInst.Transaction.Dispose();
				m_ConnInst.Transaction = null;
			}
			m_ConnInst.Close(pooled);
			m_ConnInst = null;
			do_State = ConnectionState.Closed;
		}
	}

	internal void ReleaseUnmanagedResource(bool pooled, StringBuilder msg)
	{
		if (do_State == ConnectionState.Closed)
		{
			msg.Append("->{do_State == ConnectionState.Closed}");
			return;
		}
		if (m_ConnInst == null)
		{
			msg.Append("->{m_ConnInst == null}");
			return;
		}
		if (m_ConnInst.Transaction != null)
		{
			msg.Append("->{m_ConnInst.Transaction.Dispose();}");
			m_ConnInst.Transaction.Dispose(msg);
			m_ConnInst.Transaction = null;
		}
		msg.Append("->{m_ConnInst.Close(" + pooled + ")}");
		m_ConnInst.Close(pooled, msg);
		msg.Append("->{m_ConnInst = null;do_State = ConnectionState.Closed;}");
		m_ConnInst = null;
		do_State = ConnectionState.Closed;
	}

	protected override void Dispose(bool disposing)
	{
		if (m_AlreadyDisposed)
		{
			return;
		}
		if (ExistDmPromotableTransaction())
		{
			StatusInTransactionScope = ConnectionStatusInTransactionScope.Disposed;
			return;
		}
		try
		{
			Close();
		}
		finally
		{
			m_AlreadyDisposed = true;
			base.Dispose(disposing);
		}
	}

	protected void ForceDispose()
	{
		if (m_AlreadyDisposed)
		{
			return;
		}
		if (ExistDmPromotableTransaction())
		{
			StatusInTransactionScope = ConnectionStatusInTransactionScope.Disposed;
			return;
		}
		try
		{
			ForceClose();
		}
		finally
		{
			m_AlreadyDisposed = true;
			base.Dispose(disposing: false);
		}
	}

	public void SetDatabase(string db)
	{
		ConnProperty.Database = db;
	}

	public DmTransaction BeginTransaction(System.Data.IsolationLevel il, bool for_ef)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "BeginTransaction(IsolationLevel il)");
		if (do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		return m_ConnInst.BeginTrx(il);
	}

	internal DmConnInstance GetConnInstance()
	{
		return m_ConnInst;
	}

	internal void CheckProperty()
	{
		if (ConnProperty.Server == null)
		{
			throw new InvalidOperationException("Cannot open a connection without specifying a data source or server.");
		}
		if (string.IsNullOrEmpty(do_ConnectionString))
		{
			throw new InvalidOperationException("There is no connectionString.");
		}
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	public DmConnection Clone()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Clone()");
		return new DmConnection(do_ConnectionString);
	}

	public bool compatibleOracle()
	{
		return (ConnProperty.CompatibleMode & CompatibleMode.ORACLE) != 0;
	}

	public bool compatibleMysql()
	{
		return ConnProperty.CompatibleMode == CompatibleMode.MYSQL;
	}

	public void checkClosed()
	{
		if (do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNECTION_CLOSED);
		}
	}

	public DmStruct CreateStruct(string typeName, object[] attributes)
	{
		if (filterHead == null)
		{
			return do_CreateStruct(typeName, attributes);
		}
		return filterHead.CreateStruct(this, typeName, attributes);
	}

	public DmArray CreateArray(string typeName, object[] elements)
	{
		if (filterHead == null)
		{
			return do_CreateArray(typeName, elements);
		}
		return filterHead.CreateArray(this, typeName, elements);
	}

	public DmStruct CreateIndexTable(string typeName, Dictionary<string, object> dictionary)
	{
		if (filterHead == null)
		{
			return do_CreateIndexTable(typeName, dictionary);
		}
		return filterHead.CreateIndexTable(this, typeName, dictionary);
	}

	public static DmCommand CreateDmTracingCommand(DmConnection conn)
	{
		if (!conn.ConnProperty.UseSkyWalking)
		{
			return null;
		}
		if (DmSkyWalkingAgentAssembly == null)
		{
			DmSkyWalkingAgentAssembly = Assembly.Load("DmSkyWalkingAgent");
		}
		return (DmCommand)DmSkyWalkingAgentAssembly.CreateInstance("DmSkyWalkingAgent.DmTracingCommand", ignoreCase: false, BindingFlags.Instance | BindingFlags.Public, null, new object[2] { "", conn }, null, null);
	}

	internal string getFormat(DmField column)
	{
		switch (column.GetCType())
		{
		case 14:
			return GetConnInstance().ConnProperty.oracleDateFormat;
		case 15:
			return GetConnInstance().ConnProperty.oracleTimeFormat;
		case 22:
			return GetConnInstance().ConnProperty.oracleTimeTZFormat;
		case 16:
		case 26:
			return GetConnInstance().ConnProperty.oracleTimestampFormat;
		case 23:
		case 27:
			return GetConnInstance().ConnProperty.oracleTimestampTZFormat;
		default:
			return "";
		}
	}

	public bool getClobAsString()
	{
		return ConnProperty.ClobAsString;
	}

	public bool isColumnNameUpperCase()
	{
		return ConnProperty.ColumnNameCase == ColumnNameCase.UPPER;
	}

	public bool isColumnNameLowerCase()
	{
		return ConnProperty.ColumnNameCase == ColumnNameCase.LOWER;
	}

	public bool lobFetchAll()
	{
		return ConnProperty.LobMode == 2;
	}

	internal Fldr getFldrInstance()
	{
		return new Fldr(m_ConnInst.GetCsi() ?? throw new Exception("请初始化Connection!"));
	}

	public FldrStatement fldrStatement(FldrConfig config)
	{
		if (filterHead == null)
		{
			return do_fldrStatement(config);
		}
		return filterHead.Connection_fldrStatement(this, config);
	}

	public FldrStatement do_fldrStatement(FldrConfig config)
	{
		checkClosed();
		if (config == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		return new FldrStatement(this, config);
	}

	public bool ExistDmPromotableTransaction()
	{
		return m_ConnInst?.CurrentDmPromotableTransaction != null;
	}
}
