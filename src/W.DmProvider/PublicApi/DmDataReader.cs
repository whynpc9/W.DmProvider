using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Threading;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

public class DmDataReader : DbDataReader, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmDataReader";

	private DmConnInstance m_Conn;

	private DmResultSetCache m_RsCache;

	internal global::W.Dm.Internal.Legacy.A.A m_Statement;

	private DmInfo m_DbInfo;

	private DmColumn[] m_ColInfo;

	private DmGetValue m_GetVal;

	internal long m_RowCount;

	private CommandBehavior m_Behavior;

	private int m_is_single_row;

	internal long m_StartRow;

	protected long m_CurrentRow = -1L;

	private long m_RecordsAffected;

	protected bool m_IsClosed;

	private bool is_SequentialAccess;

	private int m_SequentialSeq = -1;

	private long m_StreamPos;

	private bool skipCol = true;

	private ArrayList m_Clobs = new ArrayList();

	public bool bdta;

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

	internal long RowCount => m_RowCount;

	internal bool FetchedAll => m_RowCount == long.MaxValue;

	internal int do_Depth => 0;

	internal bool do_HasRows
	{
		get
		{
			if (m_RowCount > 0)
			{
				return true;
			}
			return false;
		}
	}

	internal int do_VisibleFieldCount => base.VisibleFieldCount;

	internal int do_RecordsAffected
	{
		get
		{
			if (m_DbInfo != null && m_DbInfo.GetHasResultSet())
			{
				return -1;
			}
			return (int)m_RecordsAffected;
		}
	}

	internal bool do_IsClosed => m_IsClosed;

	internal int do_FieldCount => m_DbInfo.GetColumnCount();

	public override object this[int number]
	{
		get
		{
			if (filterHead == null)
			{
				return do_this(number);
			}
			return filterHead.getThis(this, number);
		}
	}

	public override object this[string name]
	{
		get
		{
			if (filterHead == null)
			{
				return do_this(name);
			}
			return filterHead.getThis(this, name);
		}
	}

	public override int Depth
	{
		get
		{
			if (filterHead == null)
			{
				return do_Depth;
			}
			return filterHead.getDepth(this);
		}
	}

	public override bool HasRows
	{
		get
		{
			if (filterHead == null)
			{
				return do_HasRows;
			}
			return filterHead.getHasRows(this);
		}
	}

	public override int VisibleFieldCount
	{
		get
		{
			if (filterHead == null)
			{
				return do_VisibleFieldCount;
			}
			return filterHead.getVisibleFieldCount(this);
		}
	}

	public override int RecordsAffected
	{
		get
		{
			if (filterHead == null)
			{
				return do_RecordsAffected;
			}
			return filterHead.getRecordsAffected(this);
		}
	}

	public override bool IsClosed
	{
		get
		{
			if (filterHead == null)
			{
				return do_IsClosed;
			}
			return filterHead.getIsClosed(this);
		}
	}

	public override int FieldCount
	{
		get
		{
			if (filterHead == null)
			{
				return do_FieldCount;
			}
			return filterHead.getFieldCount(this);
		}
	}

	internal DmDataReader(DmResultSetCache cache, DmInfo info, CommandBehavior behavior)
	{
		BaseFilter.CreateFilterChain(this);
		m_RsCache = cache;
		m_DbInfo = info;
		m_ColInfo = info.GetColumnsInfo();
		bdta = info.rsBdta;
		DmColumn[] colInfo = m_ColInfo;
		for (int i = 0; i < colInfo.Length; i++)
		{
			colInfo[i].isBdta = bdta;
		}
		m_RowCount = m_DbInfo.GetRowCount();
		m_RecordsAffected = m_DbInfo.GetRecordsAffected();
		m_Statement = m_RsCache.statement;
		m_Conn = m_Statement.G();
		string serverEncoding = m_Conn.ConnProperty.ServerEncoding;
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		bool newLobFlag = m_Conn.ConnProperty.NewLobFlag;
		DmField[] colInfo2 = m_ColInfo;
		m_GetVal = new DmGetValue(serverEncoding, statement, newLobFlag, colInfo2);
		m_Behavior = behavior;
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SequentialAccess))
		{
			is_SequentialAccess = true;
		}
	}

	internal DmDataReader(DmInfo info, CommandBehavior behavior, global::W.Dm.Internal.Legacy.A.A stmt)
	{
		BaseFilter.CreateFilterChain(this);
		m_RsCache = null;
		m_DbInfo = info;
		m_ColInfo = info.GetColumnsInfo();
		bdta = info.rsBdta;
		if (m_ColInfo != null)
		{
			DmColumn[] colInfo = m_ColInfo;
			for (int i = 0; i < colInfo.Length; i++)
			{
				colInfo[i].isBdta = bdta;
			}
		}
		m_RowCount = m_DbInfo.GetRowCount();
		m_RecordsAffected = m_DbInfo.GetRecordsAffected();
		m_Statement = stmt;
		m_Conn = m_Statement.G();
		string serverEncoding = m_Conn.ConnProperty.ServerEncoding;
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		bool newLobFlag = m_Conn.ConnProperty.NewLobFlag;
		DmField[] colInfo2 = m_ColInfo;
		m_GetVal = new DmGetValue(serverEncoding, statement, newLobFlag, colInfo2);
		m_Behavior = behavior;
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SequentialAccess))
		{
			is_SequentialAccess = true;
		}
	}

	internal object do_this(int number)
	{
		return do_GetValue(number);
	}

	internal object do_this(string name)
	{
		int number = do_GetOrdinal(name);
		return do_this(number);
	}

	internal void do_Close()
	{
		lock (this)
		{
			m_IsClosed = true;
			m_DbInfo = null;
			m_ColInfo = null;
			m_CurrentRow = -1L;
			if (m_RsCache != null)
			{
				m_RsCache = null;
			}
			if (m_Statement != null && !m_Statement.P())
			{
				m_Statement.p();
				m_Statement = null;
			}
			if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.CloseConnection) && m_Conn != null && m_Conn.Conn != null)
			{
				m_Conn.Conn.do_Close();
			}
		}
	}

	internal bool do_GetBoolean(int i)
	{
		object obj = do_GetValue(i);
		if (obj is bool)
		{
			return (bool)obj;
		}
		return Convert.ToBoolean(obj);
	}

	internal byte do_GetByte(int i)
	{
		byte[] value = null;
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetByte(i, value, cType, precision, scale);
	}

	internal long do_GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
	{
		checkClosed();
		skipCol = false;
		byte[] value = null;
		byte[] array = null;
		if (m_SequentialSeq == i && fieldOffset < m_StreamPos)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
		}
		else if (m_SequentialSeq < i)
		{
			m_StreamPos = 0L;
		}
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		array = m_GetVal.GetBytes(i, value, cType, precision, scale);
		if (buffer == null)
		{
			skipCol = true;
			m_SequentialSeq++;
			return array.Length;
		}
		if (bufferoffset >= buffer.Length || bufferoffset < 0)
		{
			throw new IndexOutOfRangeException("Buffer index must be a valid index in buffer");
		}
		if (buffer.Length < bufferoffset + length)
		{
			throw new ArgumentException("Buffer is not large enough to hold the requested data");
		}
		if (fieldOffset < 0)
		{
			throw new IndexOutOfRangeException("Field offset must be a valid index in the field");
		}
		long num = length;
		if (array.Length - fieldOffset < length)
		{
			num = array.Length - fieldOffset;
		}
		Array.Copy(array, fieldOffset, buffer, bufferoffset, num);
		if (is_SequentialAccess)
		{
			m_StreamPos += num;
			skipCol = true;
		}
		return num;
	}

	internal char do_GetChar(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetChar(int i)");
		CheckIndex(i);
		checkClosed();
		char[] array = new char[1];
		do_GetChars(i, 0L, array, 0, 1);
		return array[0];
	}

	internal long do_GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
	{
		string text = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetChars(int i,long fieldoffset,char[] buffer,int bufferoffset,int length)");
		checkClosed();
		skipCol = false;
		if (m_SequentialSeq == i && fieldoffset < m_StreamPos)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
		}
		else if (m_SequentialSeq < i)
		{
			m_StreamPos = 0L;
		}
		if (m_ColInfo[i].GetCType() == 19)
		{
			DmClob dmClob = (DmClob)m_Clobs[i];
			if (dmClob == null)
			{
				byte[] value = null;
				GetByteArrayValue(i, ref value);
				dmClob = new DmClob(value, m_Conn, m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
				m_Clobs[i] = dmClob;
			}
			text = dmClob.getSubString(fieldoffset, length);
			length = Math.Min(length, text.Length);
			Array.Copy(text.ToCharArray(), 0, buffer, bufferoffset, length);
			return length;
		}
		byte[] value2 = null;
		GetByteArrayValue(i, ref value2);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (value2 == null)
		{
			text = "";
		}
		text = m_GetVal.GetString(i, value2, cType, precision, scale);
		if (buffer == null)
		{
			skipCol = true;
			m_SequentialSeq++;
			return text.Length;
		}
		if (bufferoffset >= buffer.Length || bufferoffset < 0)
		{
			throw new IndexOutOfRangeException("Buffer index must be a valid index in buffer");
		}
		if (buffer.Length < bufferoffset + length)
		{
			throw new ArgumentException("Buffer is not large enough to hold the requested data");
		}
		if (fieldoffset < 0)
		{
			throw new IndexOutOfRangeException("Field offset must be a valid index in the field");
		}
		char[] array = text.ToCharArray();
		int num = length;
		if (buffer.Length - bufferoffset < length)
		{
			num = buffer.Length - bufferoffset;
		}
		else if (length > array.Length)
		{
			num = array.Length;
		}
		num = (int)Math.Min(num, array.Length - fieldoffset);
		Array.Copy(array, fieldoffset, buffer, bufferoffset, num);
		if (is_SequentialAccess)
		{
			m_StreamPos += num;
			skipCol = true;
		}
		return num;
	}

	internal string do_GetDataTypeName(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDataTypeName(int i)");
		checkClosed();
		return m_ColInfo[i].GetTypeName();
	}

	internal DateTime do_GetDateTime(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDateTime(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetTimestamp(i, value, cType, precision, scale);
	}

	internal decimal do_GetDecimal(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDecimal(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetBigDecimal(i, value, cType, precision, scale);
	}

	internal double do_GetDouble(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDouble(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetDouble(i, value, cType, precision, scale);
	}

	internal IEnumerator do_GetEnumerator()
	{
		if (m_Behavior == CommandBehavior.CloseConnection)
		{
			return new DbEnumerator(this, closeReader: true);
		}
		return new DbEnumerator(this, closeReader: false);
	}

	internal Type do_GetFieldType(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFieldType(int i)");
		CheckIndex(i);
		return DmSqlType.CTypeToSystemType(m_ColInfo[i].GetCType(), m_ColInfo[i].GetPrecision(), m_ColInfo[i].GetScale(), m_Conn.ConnProperty);
	}

	internal float do_GetFloat(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFloat(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetFloat(i, value, cType, precision, scale);
	}

	internal Guid do_GetGuid(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetGuid(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (value == null)
		{
			return new Guid("");
		}
		switch (cType)
		{
		case 0:
		case 1:
		case 2:
		case 54:
			return new Guid(m_GetVal.GetString(i, value, cType, precision, scale));
		case 17:
		case 18:
			if (value != null && value.Length == 16)
			{
				return new Guid(m_GetVal.GetBytes(i, value, cType, precision, scale));
			}
			break;
		}
		return new Guid("");
	}

	internal short do_GetInt16(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt16(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetShort(i, value, cType, precision, scale);
	}

	internal int do_GetInt32(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt32(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		if (value == null)
		{
			return 0;
		}
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetInt(i, value, cType, precision, scale);
	}

	internal long do_GetInt64(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt64(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetLong(i, value, cType, precision, scale);
	}

	internal string do_GetName(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetName(int i)");
		string name = m_ColInfo[i].GetName();
		if (m_Conn.ConnProperty.ColumnNameUpperCase)
		{
			return name.ToUpper();
		}
		if (m_Conn.ConnProperty.ColumnNameCase == ColumnNameCase.UPPER)
		{
			return name.ToUpper();
		}
		if (m_Conn.ConnProperty.ColumnNameCase == ColumnNameCase.LOWER)
		{
			return name.ToLower();
		}
		return name;
	}

	internal int do_GetOrdinal(string name)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetOrdinal(string name)");
		checkClosed();
		int i;
		for (i = 0; i < m_DbInfo.GetColumnCount() && !m_ColInfo[i].GetName().Equals(name); i++)
		{
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int j = 0; j < m_DbInfo.GetColumnCount(); j++)
			{
				if ((m_ColInfo[j].GetTable() + "." + m_ColInfo[j].GetName()).Equals(name))
				{
					i = j;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int k = 0; k < m_DbInfo.GetColumnCount(); k++)
			{
				if ((m_ColInfo[k].GetSchema() + "." + m_ColInfo[k].GetTable() + "." + m_ColInfo[k].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = k;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (i = 0; i < m_DbInfo.GetColumnCount() && !m_ColInfo[i].GetName().ToUpper().Equals(name.ToUpper()); i++)
			{
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int l = 0; l < m_DbInfo.GetColumnCount(); l++)
			{
				if ((m_ColInfo[l].GetTable() + "." + m_ColInfo[l].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = l;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int m = 0; m < m_DbInfo.GetColumnCount(); m++)
			{
				if ((m_ColInfo[m].GetSchema() + "." + m_ColInfo[m].GetTable() + "." + m_ColInfo[m].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = m;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			throw new IndexOutOfRangeException();
		}
		return i;
	}

	internal Type do_GetProviderSpecificFieldType(int ordinal)
	{
		return base.GetProviderSpecificFieldType(ordinal);
	}

	internal object do_GetProviderSpecificValue(int ordinal)
	{
		return base.GetProviderSpecificValue(ordinal);
	}

	internal int do_GetProviderSpecificValues(object[] values)
	{
		return base.GetProviderSpecificValues(values);
	}

	internal DataTable do_GetSchemaTable()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetSchemaTable()");
		checkClosed();
		if (m_ColInfo == null)
		{
			return null;
		}
		DataTable dataTable = new DataTable("SchemaTable");
		BuildSchemaColumns(dataTable);
		FillSchemaTable(dataTable);
		return dataTable;
	}

	internal DataTable do_GetDataTable()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "do_GetDataTable()");
		checkClosed();
		DataTable dataTable = new DataTable();
		new DmDataAdapter().do_Fill(dataTable, this);
		return dataTable;
	}

	internal string do_GetString(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetString(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (value == null)
		{
			return null;
		}
		return m_GetVal.GetString(i, value, cType, precision, scale);
	}

	internal object do_GetValue(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetValue(int i)");
		CheckIndex(i);
		checkClosed();
		byte[] value = null;
		GetByteArrayValue(i, ref value);
		if (value == null)
		{
			return DBNull.Value;
		}
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (cType == 1)
		{
			int num = ((value != null) ? value.Length : 0);
			int precision2 = m_ColInfo[i].GetPrecision();
			if (num < precision2)
			{
				byte[] array = new byte[precision2];
				if (num != 0)
				{
					Array.Copy(value, 0, array, 0, num);
				}
				for (int j = num; j < precision2; j++)
				{
					array[j] = 32;
				}
				value = array;
			}
		}
		if (DmSqlType.isComplexType(cType, scale))
		{
			if (m_Statement.f().ComplexTypeToBytes)
			{
				DmBlob blob = GetBlob((short)i);
				return blob.GetBytes(0L, (int)blob.do_length());
			}
			return DB2N.toComplexType(value, m_ColInfo[i], m_Conn);
		}
		return m_GetVal.GetObject(i, value, cType, precision, scale);
	}

	internal int do_GetValues(object[] values)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetValues(object[] values)");
		checkClosed();
		if (values == null)
		{
			return 0;
		}
		int num = m_DbInfo.GetColumnCount();
		if (values.Length < num)
		{
			num = values.Length;
		}
		for (int i = 0; i < num; i++)
		{
			values[i] = do_GetValue(i);
		}
		return num;
	}

	internal bool do_IsDBNull(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "IsDBNull(int i)");
		checkClosed();
		byte[] value = null;
		GetByteArrayValue(i, ref value);
		if (value == null)
		{
			return true;
		}
		return false;
	}

	internal bool do_NextResult()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "NextResult()");
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SingleResult))
		{
			return false;
		}
		if (m_Statement == null)
		{
			return false;
		}
		if (m_Statement.f() == null)
		{
			return false;
		}
		string commandText = m_Statement.f().GetCommandText();
		if (!m_Conn.ConnProperty.EFCoreNextResult && !commandText.isEmpty())
		{
			int num = commandText.IndexOf("/*EFCOREROWCOUNT*/");
			if (num < 0)
			{
				return false;
			}
			if (commandText.LastIndexOf("/*EFCOREROWCOUNT*/") == num)
			{
				return false;
			}
		}
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		if (statement.f().RefCursorStmtArr != null && statement.f().RefCursorStmtArr.Count > statement.f().RefCursorStmtArr_cur)
		{
			statement = (global::W.Dm.Internal.Legacy.A.A)statement.f().RefCursorStmtArr[statement.f().RefCursorStmtArr_cur];
			statement.f().IncRefCur();
			m_DbInfo = statement.F();
			m_ColInfo = m_DbInfo.GetColumnsInfo();
			m_RsCache = statement.l();
			m_RowCount = m_DbInfo.GetRowCount();
			m_StartRow = 0L;
			m_CurrentRow = -1L;
			m_IsClosed = false;
		}
		else
		{
			m_DbInfo = statement.h().A(statement, m_DbInfo, 0);
			m_ColInfo = m_DbInfo.GetColumnsInfo();
			m_RsCache = statement.l();
			m_RowCount = m_DbInfo.GetRowCount();
			m_StartRow = 0L;
			m_CurrentRow = -1L;
			m_IsClosed = false;
		}
		return m_DbInfo.GetHasResultSet();
	}

	internal bool do_Read()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Read()");
		checkClosed();
		ClearClobs();
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SequentialAccess))
		{
			m_SequentialSeq = -1;
		}
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SingleRow) && m_CurrentRow != -1)
		{
			m_is_single_row = 1;
			return false;
		}
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SchemaOnly))
		{
			m_RowCount = 0L;
			return false;
		}
		if (!do_HasRows || m_RsCache == null)
		{
			return false;
		}
		bool result = m_RsCache.do_next();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	internal void do_Dispose(bool disposing)
	{
		base.Dispose(disposing);
	}

	internal DmDataReader do_GetDbDataReader(int ordinal)
	{
		return (DmDataReader)base.GetDbDataReader(ordinal);
	}

	public override void Close()
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

	public override bool GetBoolean(int i)
	{
		if (filterHead == null)
		{
			return do_GetBoolean(i);
		}
		return filterHead.GetBoolean(this, i);
	}

	public override byte GetByte(int i)
	{
		if (filterHead == null)
		{
			return do_GetByte(i);
		}
		return filterHead.GetByte(this, i);
	}

	public override long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
	{
		if (filterHead == null)
		{
			return do_GetBytes(i, fieldOffset, buffer, bufferoffset, length);
		}
		return filterHead.GetBytes(this, i, fieldOffset, buffer, bufferoffset, length);
	}

	public override char GetChar(int i)
	{
		if (filterHead == null)
		{
			return do_GetChar(i);
		}
		return filterHead.GetChar(this, i);
	}

	public override long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
	{
		if (filterHead == null)
		{
			return do_GetChars(i, fieldoffset, buffer, bufferoffset, length);
		}
		return filterHead.GetChars(this, i, fieldoffset, buffer, bufferoffset, length);
	}

	public override string GetDataTypeName(int i)
	{
		if (filterHead == null)
		{
			return do_GetDataTypeName(i);
		}
		return filterHead.GetDataTypeName(this, i);
	}

	public override DateTime GetDateTime(int i)
	{
		if (filterHead == null)
		{
			return do_GetDateTime(i);
		}
		return filterHead.GetDateTime(this, i);
	}

	public override decimal GetDecimal(int i)
	{
		if (filterHead == null)
		{
			return do_GetDecimal(i);
		}
		return filterHead.GetDecimal(this, i);
	}

	public override double GetDouble(int i)
	{
		if (filterHead == null)
		{
			return do_GetDouble(i);
		}
		return filterHead.GetDouble(this, i);
	}

	public override IEnumerator GetEnumerator()
	{
		if (filterHead == null)
		{
			return do_GetEnumerator();
		}
		return filterHead.GetEnumerator(this);
	}

	public override Type GetFieldType(int i)
	{
		if (filterHead == null)
		{
			return do_GetFieldType(i);
		}
		return filterHead.GetFieldType(this, i);
	}

	public override float GetFloat(int i)
	{
		if (filterHead == null)
		{
			return do_GetFloat(i);
		}
		return filterHead.GetFloat(this, i);
	}

	public override Guid GetGuid(int i)
	{
		if (filterHead == null)
		{
			return do_GetGuid(i);
		}
		return filterHead.GetGuid(this, i);
	}

	public override short GetInt16(int i)
	{
		if (filterHead == null)
		{
			return do_GetInt16(i);
		}
		return filterHead.GetInt16(this, i);
	}

	public override int GetInt32(int i)
	{
		if (filterHead == null)
		{
			return do_GetInt32(i);
		}
		return filterHead.GetInt32(this, i);
	}

	public override long GetInt64(int i)
	{
		if (filterHead == null)
		{
			return do_GetInt64(i);
		}
		return filterHead.GetInt64(this, i);
	}

	public override string GetName(int i)
	{
		if (filterHead == null)
		{
			return do_GetName(i);
		}
		return filterHead.GetName(this, i);
	}

	public override int GetOrdinal(string name)
	{
		if (filterHead == null)
		{
			return do_GetOrdinal(name);
		}
		return filterHead.GetOrdinal(this, name);
	}

	public override Type GetProviderSpecificFieldType(int ordinal)
	{
		if (filterHead == null)
		{
			return do_GetProviderSpecificFieldType(ordinal);
		}
		return filterHead.GetProviderSpecificFieldType(this, ordinal);
	}

	public override object GetProviderSpecificValue(int ordinal)
	{
		if (filterHead == null)
		{
			return do_GetProviderSpecificValue(ordinal);
		}
		return filterHead.GetProviderSpecificValue(this, ordinal);
	}

	public override int GetProviderSpecificValues(object[] values)
	{
		if (filterHead == null)
		{
			return do_GetProviderSpecificValues(values);
		}
		return filterHead.GetProviderSpecificValues(this, values);
	}

	public override DataTable GetSchemaTable()
	{
		if (filterHead == null)
		{
			return do_GetSchemaTable();
		}
		return filterHead.GetSchemaTable(this);
	}

	public override string GetString(int i)
	{
		if (filterHead == null)
		{
			return do_GetString(i);
		}
		return filterHead.GetString(this, i);
	}

	public override object GetValue(int i)
	{
		if (filterHead == null)
		{
			return do_GetValue(i);
		}
		return filterHead.GetValue(this, i);
	}

	public override T GetFieldValue<T>(int ordinal)
	{
		object value = GetValue(ordinal);
		if (typeof(T) == typeof(DateOnly) && value is DateTime dateTime)
		{
			return (T)(object)DateOnly.FromDateTime(dateTime);
		}
		if (typeof(T) == typeof(TimeOnly) && value is DateTime dateTime2)
		{
			return (T)(object)TimeOnly.FromDateTime(dateTime2);
		}
		if (typeof(T) == typeof(TimeOnly) && value is TimeSpan timeSpan)
		{
			return (T)(object)TimeOnly.FromTimeSpan(timeSpan);
		}
		return (T)Convert.ChangeType(value, typeof(T));
	}

	public override int GetValues(object[] values)
	{
		if (filterHead == null)
		{
			return do_GetValues(values);
		}
		return filterHead.GetValues(this, values);
	}

	public override bool IsDBNull(int i)
	{
		if (filterHead == null)
		{
			return do_IsDBNull(i);
		}
		return filterHead.IsDBNull(this, i);
	}

	public override bool NextResult()
	{
		if (filterHead == null)
		{
			return do_NextResult();
		}
		return filterHead.NextResult(this);
	}

	public override bool Read()
	{
		if (filterHead == null)
		{
			return do_Read();
		}
		return filterHead.Read(this);
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

	protected override DbDataReader GetDbDataReader(int ordinal)
	{
		if (filterHead == null)
		{
			return do_GetDbDataReader(ordinal);
		}
		return filterHead.GetDbDataReader(this, ordinal);
	}

	public new void Dispose()
	{
		Close();
	}

	private void BuildSchemaColumns(DataTable schTbl)
	{
		schTbl.Columns.Add("ColumnName", typeof(string));
		schTbl.Columns.Add("ColumnOrdinal", typeof(int));
		schTbl.Columns.Add("ColumnSize", typeof(int));
		schTbl.Columns.Add("NumericPrecision", typeof(int));
		schTbl.Columns.Add("NumericScale", typeof(int));
		schTbl.Columns.Add("IsUnique", typeof(bool));
		schTbl.Columns.Add("IsKey", typeof(bool));
		schTbl.Columns.Add("BaseServerName", typeof(string));
		schTbl.Columns.Add("BaseCatalogName", typeof(string));
		schTbl.Columns.Add("BaseSchemaName", typeof(string));
		schTbl.Columns.Add("BaseTableName", typeof(string));
		schTbl.Columns.Add("BaseColumnName", typeof(string));
		schTbl.Columns.Add("DataType", typeof(Type));
		schTbl.Columns.Add("AllowDBNull", typeof(bool));
		schTbl.Columns.Add("ProviderType", typeof(int));
		schTbl.Columns.Add("IsAliased", typeof(bool));
		schTbl.Columns.Add("IsExpression", typeof(bool));
		schTbl.Columns.Add("IsIdentity", typeof(bool));
		schTbl.Columns.Add("IsAutoIncrement", typeof(bool));
		schTbl.Columns.Add("IsRowVersion", typeof(bool));
		schTbl.Columns.Add("IsHidden", typeof(bool));
		schTbl.Columns.Add("IsLong", typeof(bool));
		schTbl.Columns.Add("IsReadOnly", typeof(bool));
	}

	private void FillSchemaColumn(DataRow row, DmColumn dmCol, int i)
	{
		row["ColumnName"] = dmCol.GetName();
		row["ColumnOrdinal"] = i + 1;
		row["ColumnSize"] = dmCol.GetSize();
		row["NumericPrecision"] = dmCol.GetPrecision();
		row["NumericScale"] = dmCol.GetScale();
		row["IsUnique"] = false;
		row["IsKey"] = false;
		row["BaseServerName"] = "DM";
		row["BaseCatalogName"] = "";
		row["BaseSchemaName"] = dmCol.GetSchema();
		row["BaseTableName"] = dmCol.GetTable();
		row["BaseColumnName"] = dmCol.GetBaseColumn();
		row["DataType"] = DmSqlType.CTypeToSystemType(dmCol.GetCType(), dmCol.GetPrecision(), m_Conn.ConnProperty);
		row["AllowDBNull"] = dmCol.GetNullable();
		row["ProviderType"] = dmCol.GetCType();
		row["IsAliased"] = false;
		row["IsExpression"] = false;
		row["IsIdentity"] = dmCol.GetIdentity();
		row["IsAutoIncrement"] = dmCol.GetIdentity();
		row["IsRowVersion"] = false;
		row["IsHidden"] = false;
		row["IsLong"] = dmCol.GetIsLob();
		row["IsReadOnly"] = false;
	}

	private void FillSchemaTable(DataTable schTbl)
	{
		bool flag = true;
		string text = null;
		string text2 = null;
		for (int i = 0; i < m_ColInfo.Length; i++)
		{
			if (i == 0)
			{
				text = m_ColInfo[i].GetSchema();
				text2 = m_ColInfo[i].GetTable();
			}
			if (text == null || text2 == null)
			{
				flag = false;
			}
			else if (!text.Equals(m_ColInfo[i].GetSchema()) || !text2.Equals(m_ColInfo[i].GetTable()))
			{
				flag = false;
			}
			DataRow dataRow = schTbl.NewRow();
			FillSchemaColumn(dataRow, m_ColInfo[i], i);
			dataRow["IsReadOnly"] = !m_DbInfo.GetUpdatable();
			schTbl.Rows.Add(dataRow);
		}
		if (!flag)
		{
			return;
		}
		string text3 = null;
		DataRow dataRow2 = null;
		DmDataReader keyCols = GetKeyCols(text, text2);
		if (keyCols == null)
		{
			return;
		}
		while (keyCols.do_Read())
		{
			text3 = keyCols.GetString(0);
			for (int j = 0; j < schTbl.Rows.Count; j++)
			{
				dataRow2 = schTbl.Rows[j];
				if (Convert.ToString(dataRow2["BaseColumnName"]).Equals(text3))
				{
					dataRow2["IsKey"] = true;
					if (keyCols.m_RowCount == 1)
					{
						dataRow2["IsUnique"] = true;
					}
					break;
				}
			}
		}
		keyCols.do_Close();
		keyCols = GetUniqueCols(text, text2);
		if (keyCols == null)
		{
			return;
		}
		while (keyCols.do_Read())
		{
			text3 = keyCols.GetString(0);
			for (int k = 0; k < schTbl.Rows.Count; k++)
			{
				dataRow2 = schTbl.Rows[k];
				if (Convert.ToString(dataRow2["BaseColumnName"]).Equals(text3))
				{
					if (keyCols.m_RowCount == 1)
					{
						dataRow2["IsUnique"] = true;
					}
					break;
				}
			}
		}
		keyCols.do_Close();
	}

	public DmDataReader GetKeyCols(string schema, string table)
	{
		if (schema == null || table == null)
		{
			return null;
		}
		string escStringName = DmStringUtil.GetEscStringName(schema);
		string escStringName2 = DmStringUtil.GetEscStringName(table);
		string text = "SELECT COLS.NAME FROM SYS.SYSINDEXES INDS, (SELECT OBJ.NAME, CON.ID, CON.TYPE$, CON.TABLEID, CON.COLID, CON.INDEXID FROM SYS.SYSCONS AS CON, SYS.SYSOBJECTS AS OBJ WHERE OBJ.SUBTYPE$='CONS' AND OBJ.ID=CON.ID) CONS, SYS.SYSCOLUMNS COLS, (SELECT NAME ,ID FROM SYS.SYSOBJECTS WHERE SUBTYPE$='UTAB' AND NAME = '" + escStringName2 + "' AND SCHID=(SELECT ID FROM SYS.SYSOBJECTS WHERE NAME = '" + escStringName + "' AND TYPE$='SCH')) TAB, (SELECT ID, NAME FROM SYS.SYSOBJECTS WHERE SUBTYPE$='INDEX')OBJ_INDS WHERE CONS.TYPE$='P' AND CONS.INDEXID=INDS.ID AND INDS.ID=OBJ_INDS.ID AND TAB.ID=COLS.ID AND CONS.TABLEID=TAB.ID AND SF_COL_IS_IDX_KEY(INDS.KEYNUM, INDS.KEYINFO,COLS.COLID)=1";
		DmDataReader result = null;
		try
		{
			result = m_Conn.GetStmtFromPool((DmCommand)m_Conn.Conn.CreateCommand()).__t02_method_060008B1(text, CommandBehavior.Default);
		}
		catch (Exception)
		{
		}
		return result;
	}

	public DmDataReader GetUniqueCols(string schema, string table)
	{
		if (schema == null || table == null)
		{
			return null;
		}
		string escStringName = DmStringUtil.GetEscStringName(schema);
		string escStringName2 = DmStringUtil.GetEscStringName(table);
		string text = "SELECT COLS.NAME FROM SYS.SYSINDEXES INDS, (SELECT OBJ.NAME, CON.ID, CON.TYPE$, CON.TABLEID, CON.COLID, CON.INDEXID FROM SYS.SYSCONS AS CON, SYS.SYSOBJECTS AS OBJ WHERE OBJ.SUBTYPE$='CONS' AND OBJ.ID=CON.ID) CONS, SYS.SYSCOLUMNS COLS, (SELECT NAME ,ID FROM SYS.SYSOBJECTS WHERE SUBTYPE$='UTAB' AND NAME = '" + escStringName2 + "' AND SCHID=(SELECT ID FROM SYS.SYSOBJECTS WHERE NAME = '" + escStringName + "' AND TYPE$='SCH')) TAB, (SELECT ID, NAME FROM SYS.SYSOBJECTS WHERE SUBTYPE$='INDEX')OBJ_INDS WHERE CONS.TYPE$='U' AND CONS.INDEXID=INDS.ID AND INDS.ID=OBJ_INDS.ID AND TAB.ID=COLS.ID AND CONS.TABLEID=TAB.ID AND SF_COL_IS_IDX_KEY(INDS.KEYNUM, INDS.KEYINFO,COLS.COLID)=1";
		DmDataReader result = null;
		try
		{
			result = m_Conn.GetStmtFromPool((DmCommand)m_Conn.Conn.CreateCommand()).__t02_method_060008B1(text, CommandBehavior.Default);
		}
		catch (Exception)
		{
		}
		return result;
	}

	public bool Previous()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Previous()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_previous();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool First()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "First()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_first();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Last()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Last()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_last();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Absolute(int pos)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Absolute()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_absolute(pos);
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Relative(int pos)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Relative()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_relative(pos);
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public new DbDataReader GetData(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetData(int i)");
		checkClosed();
		DmError.ThrowUnsupportedException();
		return null;
	}

	public DateTimeOffset GetDateTimeOffset(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDateTime(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetTimestampTZ(i, value, cType, precision, scale);
	}

	public DmXDec GetDmDecimal(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDecimal(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetDmDecimal(i, value, cType, precision, scale);
	}

	internal Type GetFieldTypeInner(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFieldTypeInner(int i)");
		CheckIndex(i);
		return DmSqlType.CTypeToSystemTypeInner(m_ColInfo[i].GetCType());
	}

	public DmBlob GetBlob(short i)
	{
		checkClosed();
		return new DmBlob(GetByteArrayValue(i), m_Conn, m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
	}

	public DmClob GetClob(short i)
	{
		checkClosed();
		return new DmClob(GetByteArrayValue(i), m_Conn, m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
	}

	private void GetByteArrayValue(int columnIndex, ref byte[] value)
	{
		if (columnIndex >= m_RsCache.colNum)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		}
		if (is_SequentialAccess && m_SequentialSeq > columnIndex)
		{
			skipCol = true;
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
		}
		m_RsCache.GetBytes((short)columnIndex, ref value);
		if (is_SequentialAccess)
		{
			m_SequentialSeq = columnIndex;
			if (skipCol)
			{
				m_SequentialSeq++;
			}
		}
	}

	private byte[] GetByteArrayValue(int columnIndex)
	{
		byte[] data = null;
		if ((Convert.ToByte(m_Behavior) & 0x3F) == Convert.ToByte(CommandBehavior.SingleRow) && m_is_single_row == 1)
		{
			return null;
		}
		if (is_SequentialAccess && m_SequentialSeq > columnIndex)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
		}
		m_RsCache.GetBytes((short)columnIndex, ref data);
		m_SequentialSeq = columnIndex + 1;
		if (m_ColInfo[columnIndex].GetCType() == 1)
		{
			int num = ((data != null) ? data.Length : 0);
			int precision = m_ColInfo[columnIndex].GetPrecision();
			if (num < precision)
			{
				byte[] array = new byte[precision];
				if (num != 0)
				{
					Array.Copy(data, 0, array, 0, num);
				}
				for (int i = num; i < precision; i++)
				{
					array[i] = 32;
				}
				return array;
			}
		}
		return data;
	}

	private void CheckIndex(int i)
	{
		if (i < 0 || i > m_DbInfo.GetColumnCount() - 1)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		}
	}

	private void checkClosed()
	{
		if (m_IsClosed || m_Statement == null || m_Statement.P())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_RESULTSET_CLOSED);
		}
	}

	private void ClearClobs()
	{
		m_Clobs.Clear();
		for (int i = 0; i < m_DbInfo.GetColumnCount(); i++)
		{
			m_Clobs.Add(null);
		}
	}

	internal DmdbResultSetMetaData getMetaData()
	{
		checkClosed();
		return new DmdbResultSetMetaData(m_Conn.Conn, m_ColInfo, innerMeta: false);
	}
}
