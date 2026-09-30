using System.IO;
using W.Dm.Internal.Sessions;

namespace W.Dm;

internal class DmInfo
{
	private DmColumn[] m_ColumnsInfo;

	private DmParameterInternal[] m_ParamsInfo;

	private long m_RowCount;

	private DmRowId m_Rowid;

	private bool m_HasResultSet;

	private bool m_Updatable;

	private string m_PrintMsg;

	private int m_RefHandle;

	private int m_RetStmtType = -1;
	private short? transactionIsolationResponseLevel;
	private short transactionIsolationRequestOpcode;
	private int transactionIsolationSqlCode;
	private OperationIdentity transactionIsolationResponseIdentity;

	internal void RecordTransactionIsolationReceipt(short level, short requestOpcode, int sqlCode,
		OperationIdentity identity)
	{
		transactionIsolationResponseLevel = level;
		transactionIsolationRequestOpcode = requestOpcode;
		transactionIsolationSqlCode = sqlCode;
		transactionIsolationResponseIdentity = identity;
	}

	internal void RequireTransactionIsolationReceipt(short expectedLevel, OperationIdentity identity)
	{
		if (expectedLevel is not (0 or 1 or 3) || m_RetStmtType != 150 ||
			transactionIsolationRequestOpcode != 5 || transactionIsolationSqlCode != 0 ||
			transactionIsolationResponseLevel != expectedLevel ||
			transactionIsolationResponseIdentity != identity ||
			identity.SessionId <= 0 || identity.LeaseGeneration <= 0 ||
			identity.ExecutionId <= 0 || identity.InvocationId <= 0)
			throw new InvalidDataException("Transaction isolation response was not confirmed for this operation.");
	}

	private int m_OutParamNum;

	private int m_Execid = -1;

	private long m_RecordsAffected;

	private bool m_IsTerminal;

	internal bool IsTerminal => m_IsTerminal;

	internal void MarkTerminal() => m_IsTerminal = true;

	private int m_ParamNum = 20;

	private int m_ParamCount = 20;

	private string m_ServerEncoding;

	private DmConnInstance conn;

	public bool rsBdta;

	internal DmConnInstance ConnInstance => conn;

	internal int Execid
	{
		get
		{
			return m_Execid;
		}
		set
		{
			m_Execid = value;
		}
	}

	public DmInfo()
	{
	}

	public DmInfo(DmConnInstance conn)
	{
		this.conn = conn;
		m_ServerEncoding = conn.ConnProperty.ServerEncoding;
	}

	internal void SetParaNum(int ParamNum)
	{
		m_ParamsInfo = new DmParameterInternal[ParamNum];
		for (int i = 0; i < ParamNum; i++)
		{
			m_ParamsInfo[i] = new DmParameterInternal(conn);
		}
		m_ParamCount = ParamNum;
		m_ParamNum = ParamNum;
	}

	internal void GetParamsInfo(out DmParameterInternal[] ParamsInfo)
	{
		ParamsInfo = m_ParamsInfo;
	}

	public void SetColumnsInfo(DmColumn[] columnsInfo)
	{
		m_ColumnsInfo = columnsInfo;
	}

	public DmColumn[] GetColumnsInfo()
	{
		return m_ColumnsInfo;
	}

	internal void SetParamsInfo(DmParameterInternal[] paramsInfo)
	{
		m_ParamsInfo = paramsInfo;
	}

	internal DmParameterInternal[] GetParamsInfo()
	{
		return m_ParamsInfo;
	}

	public int GetColumnCount()
	{
		if (m_ColumnsInfo == null)
		{
			return 0;
		}
		return m_ColumnsInfo.Length;
	}

	public int GetParameterCount()
	{
		if (m_ParamsInfo == null)
		{
			return 0;
		}
		return m_ParamNum;
	}

	internal int GetOutParamCount()
	{
		int num = 0;
		for (int i = 0; i < GetParameterCount(); i++)
		{
			if (m_ParamsInfo[i].GetInOutType() != 0)
			{
				num++;
			}
		}
		return num;
	}

	public void SetRowCount(long rowCount)
	{
		m_RowCount = rowCount;
	}

	public void SetRecordsAffected(long RecordsAffected)
	{
		m_RecordsAffected = RecordsAffected;
	}

	public long GetRecordsAffected()
	{
		return m_RecordsAffected;
	}

	public long GetRowCount()
	{
		return m_RowCount;
	}

	public bool GetHasResultSet()
	{
		return m_HasResultSet;
	}

	public void SetHasResultSet(bool hasResultSet)
	{
		m_HasResultSet = hasResultSet;
	}

	public void SetRowId(DmRowId rowid)
	{
		m_Rowid = rowid;
	}

	public DmRowId GetRowId()
	{
		return m_Rowid;
	}

	public void SetPrintMsg(string PrintMsg)
	{
		m_PrintMsg = PrintMsg;
	}

	public string GetPrintMsg()
	{
		return m_PrintMsg;
	}

	public void SetRefHandle(int refHandle)
	{
		m_RefHandle = refHandle;
	}

	public int GetRefHandle()
	{
		return m_RefHandle;
	}

	public bool GetUpdatable()
	{
		return m_Updatable;
	}

	public void SetUpdatable(bool val)
	{
		m_Updatable = val;
	}

	public int GetRetStmtType()
	{
		return m_RetStmtType;
	}

	public void SetRetStmtType(int stmtType)
	{
		m_RetStmtType = stmtType;
	}

	public int GetOutParamNum()
	{
		return m_OutParamNum;
	}

	public void SetOutParamNum(int outParamNum)
	{
		m_OutParamNum = outParamNum;
	}
}
