using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using W.Dm.util;

namespace W.Dm;

public class DmBulkCopy : IDisposable
{
	private int m_BatchSize = -1;

	private string m_DestSchema;

	private string m_DestTable;

	private DmBulkCopyOptions m_CopyOpt;

	private int m_timeout = 30;

	private DmBulkCopyColumnMappingCollection m_columnMappings;

	private int m_notifyAfter;

	private DmConnection m_Conn;

	private bool closed;

	public int BatchSize
	{
		get
		{
			if (m_BatchSize != -1)
			{
				return m_BatchSize;
			}
			return 100;
		}
		set
		{
			if ((long)value >= 0L && value <= int.MaxValue)
			{
				m_BatchSize = value;
			}
		}
	}

	public int BulkCopyTimeout
	{
		get
		{
			return m_timeout;
		}
		set
		{
			if ((long)value >= 0L && value <= int.MaxValue)
			{
				m_timeout = value;
			}
		}
	}

	public DmBulkCopyColumnMappingCollection ColumnMappings => m_columnMappings;

	public string DestinationSchemaName
	{
		get
		{
			if (StringUtil.isEmpty(m_DestSchema))
			{
				return StringUtil.EMPTY;
			}
			return getCaseSensitiveName(m_DestSchema);
		}
		set
		{
			if (value == null)
			{
				throw new ArgumentNullException("DestinationSchemaName");
			}
			if (value.Length == 0)
			{
				throw new ArgumentOutOfRangeException("DestinationSchemaName");
			}
			m_DestSchema = value;
		}
	}

	public string DestinationTableName
	{
		get
		{
			if (StringUtil.isEmpty(m_DestTable))
			{
				return StringUtil.EMPTY;
			}
			if (StringUtil.isEmpty(DestinationSchemaName))
			{
				return getCaseSensitiveName(m_DestTable);
			}
			return DestinationSchemaName + "." + getCaseSensitiveName(m_DestTable);
		}
		set
		{
			if (value == null)
			{
				throw new ArgumentNullException("DestinationTableName");
			}
			if (value.Length == 0)
			{
				throw new ArgumentOutOfRangeException("DestinationTableName");
			}
			m_DestTable = value;
		}
	}

	public int NotifyAfter
	{
		get
		{
			return m_notifyAfter;
		}
		set
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException("NotifyAfter");
			}
			m_notifyAfter = value;
		}
	}

	public event DmRowsCopiedEventHandler DmRowsCopied;

	public DmBulkCopy(DmConnection conn)
	{
		throw new NotSupportedException("Native bulk loading is unsupported.");
		if (conn == null)
		{
			throw new InvalidOperationException();
		}
		m_columnMappings = new DmBulkCopyColumnMappingCollection();
		m_Conn = conn;
		conn.Open();
	}

	public DmBulkCopy(string connectionString)
		: this(new DmConnection(connectionString))
	{
	}

	public DmBulkCopy(string connectionString, DmBulkCopyOptions copyOptions)
		: this(connectionString)
	{
		m_CopyOpt = copyOptions;
	}

	public DmBulkCopy(DmConnection conn, DmBulkCopyOptions copyOptions, DmTransaction externalTran)
		: this(conn)
	{
		m_CopyOpt = copyOptions;
	}

	public void WriteToServer(DataRow[] rows)
	{
		if (rows == null)
		{
			throw new ArgumentNullException("rows");
		}
		DataTable dataTable = new DataTable();
		foreach (DataColumn column in rows[0].Table.Columns)
		{
			dataTable.Columns.Add(column.ColumnName, column.DataType);
		}
		foreach (DataRow dataRow in rows)
		{
			dataTable.Rows.Add(dataRow.ItemArray);
		}
		WriteToServer(dataTable);
	}

	public void WriteToServer(DataRow[] rows, DataColumnCollection columns)
	{
		DataTable dataTable = new DataTable();
		foreach (DataColumn column in columns)
		{
			dataTable.Columns.Add(column.ColumnName, column.DataType);
		}
		if (rows == null)
		{
			throw new ArgumentNullException("rows");
		}
		foreach (DataRow dataRow in rows)
		{
			dataTable.Rows.Add(dataRow.ItemArray);
		}
		WriteToServer(dataTable);
	}

	public void WriteToServer(DataTable table, DataRowState rowState)
	{
		DataTable dataTable = new DataTable();
		foreach (DataColumn column in table.Columns)
		{
			dataTable.Columns.Add(column.ColumnName, column.DataType);
		}
		foreach (DataRow row in table.Rows)
		{
			if (row.RowState != DataRowState.Deleted && row.RowState == rowState)
			{
				dataTable.Rows.Add(row.ItemArray);
			}
		}
		WriteToServer(dataTable);
	}

	private List<string> getDestColNames()
	{
		List<string> list = new List<string>();
		using DmCommand dmCommand = m_Conn.do_CreateDbCommand();
		dmCommand.do_CommandText = "select top 1 * from " + DestinationTableName;
		dmCommand.do_CommandTimeout = m_timeout;
		using DmDataReader dmDataReader = dmCommand.do_ExecuteDbDataReader(CommandBehavior.Default);
		DataTable dataTable = dmDataReader.do_GetSchemaTable();
		for (int i = 0; i < dmDataReader.FieldCount; i++)
		{
			list.Add((string)dataTable.Rows[i][0]);
		}
		return list;
	}

	private Dictionary<string, string> getcolumnMappingMap(DataTable srcTable, List<string> destTable)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(getCaseSensitiveComparer());
		if (ColumnMappings != null && ColumnMappings.Count > 0)
		{
			foreach (DmBulkCopyColumnMapping columnMapping in ColumnMappings)
			{
				if (!StringUtil.isEmpty(columnMapping.SourceColumn) && !StringUtil.isEmpty(columnMapping.DestinationColumn))
				{
					dictionary[columnMapping.SourceColumn] = columnMapping.DestinationColumn;
				}
			}
		}
		else
		{
			bool num = ((object)srcTable).ToString().StartsWith("SchemaTable", StringComparison.InvariantCultureIgnoreCase);
			int num2 = (num ? srcTable.Rows.Count : srcTable.Columns.Count);
			if (num2 > destTable.Count)
			{
				throw new InvalidOperationException("source table count is greater than Destination table count");
			}
			if (num)
			{
				for (int i = 0; i < num2; i++)
				{
					dictionary[srcTable.Rows[i][0].ToString()] = destTable[i];
				}
			}
			else
			{
				for (int j = 0; j < num2; j++)
				{
					dictionary[srcTable.Columns[j].ColumnName] = destTable[j];
				}
			}
		}
		return dictionary;
	}

	private string getCaseSensitiveName(string name)
	{
		if (m_Conn.ConnProperty.caseSensitive)
		{
			return StringUtil.processDoubleQuoteOfNameForLink(name);
		}
		return name;
	}

	private StringComparer getCaseSensitiveComparer()
	{
		if (m_Conn.ConnProperty.caseSensitive)
		{
			return StringComparer.Ordinal;
		}
		return StringComparer.OrdinalIgnoreCase;
	}

	protected void insert(Dictionary<string, List<object>> srcColValueMap)
	{
		StringBuilder stringBuilder = new StringBuilder("insert into " + DestinationTableName + "(");
		List<string> list = new List<string>();
		foreach (string key in srcColValueMap.Keys)
		{
			if (list.Count > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append(key);
			list.Add(key);
		}
		stringBuilder.Append(") values (");
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append("?");
		}
		stringBuilder.Append(")");
		using DmCommand dmCommand = m_Conn.do_CreateDbCommand();
		dmCommand.do_CommandText = stringBuilder.ToString();
		dmCommand.do_CommandTimeout = m_timeout;
		for (int j = 0; j < list.Count; j++)
		{
			dmCommand.do_DbParameterCollection.do_Add(new DmParameter(":" + list[j], srcColValueMap[list[j]].ToArray()));
		}
		dmCommand.do_ExecuteNonQuery();
	}

	public void WriteToServer(DbDataReader reader)
	{
		if (m_DestTable == null)
		{
			throw new InvalidOperationException();
		}
		List<string> destColNames = getDestColNames();
		DataTable schemaTable = reader.GetSchemaTable();
		Dictionary<string, string> dictionary = getcolumnMappingMap(schemaTable, destColNames);
		Dictionary<int, string> dictionary2 = new Dictionary<int, string>();
		Dictionary<string, List<object>> dictionary3 = new Dictionary<string, List<object>>();
		for (int i = 0; i < reader.FieldCount; i++)
		{
			string text = (string)schemaTable.Rows[i][0];
			if (dictionary.Count > 0 && dictionary.ContainsKey(text))
			{
				text = dictionary[text];
			}
			if (destColNames.Contains<string>(text, getCaseSensitiveComparer()))
			{
				dictionary2.Add(i, getCaseSensitiveName(text));
				dictionary3.Add(getCaseSensitiveName(text), new List<object>());
			}
		}
		if (dictionary2.Count == 0)
		{
			throw new InvalidOperationException();
		}
		int num = 0;
		while (reader.Read())
		{
			num++;
			foreach (int key in dictionary2.Keys)
			{
				dictionary3[dictionary2[key]].Add(reader.GetValue(key));
			}
			if (num != BatchSize)
			{
				continue;
			}
			num = 0;
			insert(dictionary3);
			foreach (string key2 in dictionary3.Keys)
			{
				dictionary3[key2].Clear();
			}
		}
		if (num > 0)
		{
			insert(dictionary3);
		}
	}

	public static int GetLenFromType(Type type)
	{
		int result = 8188;
		switch (Type.GetTypeCode(type))
		{
		case TypeCode.Int16:
		case TypeCode.UInt16:
			result = 6;
			break;
		case TypeCode.Char:
			result = 2;
			break;
		case TypeCode.SByte:
		case TypeCode.Byte:
			result = 1;
			break;
		case TypeCode.Int32:
		case TypeCode.UInt32:
		case TypeCode.Single:
			result = 4;
			break;
		case TypeCode.Int64:
		case TypeCode.UInt64:
		case TypeCode.Double:
			result = 8;
			break;
		case TypeCode.String:
			result = -1;
			break;
		case TypeCode.Decimal:
		case TypeCode.DateTime:
			result = 40;
			break;
		case TypeCode.Empty:
		case TypeCode.DBNull:
			result = 0;
			break;
		case TypeCode.Boolean:
			result = 5;
			break;
		case TypeCode.Object:
			result = ((!type.Equals(typeof(DmXDec))) ? ((!type.Equals(typeof(DmIntervalDT))) ? ((!type.Equals(typeof(DmIntervalYM))) ? ((!type.Equals(typeof(byte[]))) ? (-3) : (-2)) : 50) : 50) : 40);
			break;
		}
		return result;
	}

	public int[] GetMaxColLen(DataTable table)
	{
		string serverEncoding = m_Conn.GetConnInstance().ConnProperty.ServerEncoding;
		if (table == null)
		{
			return null;
		}
		int count = table.Columns.Count;
		int count2 = table.Rows.Count;
		int[] array = new int[count];
		for (int i = 0; i < count; i++)
		{
			int num = GetLenFromType(table.Columns[i].DataType);
			switch (num)
			{
			case -3:
			case -1:
			{
				num = 0;
				for (int j = 0; j < count2; j++)
				{
					int num2 = ((table.Rows[j][i] != DBNull.Value && table.Rows[j][i] != null) ? DmConvertion.GetBytes((string)table.Rows[j][i], serverEncoding).Length : 0);
					if (num2 > num)
					{
						num = num2;
					}
				}
				break;
			}
			case -2:
			{
				num = 0;
				for (int j = 0; j < count2; j++)
				{
					int num2 = ((table.Rows[j][i] != DBNull.Value && table.Rows[j][i] != null) ? ((byte[])table.Rows[j][i]).Length : 0);
					if (num2 > num)
					{
						num = num2;
					}
				}
				break;
			}
			}
			array[i] = num;
		}
		return array;
	}

	public void WriteToServer(DataTable table)
	{
		if (m_DestTable == null)
		{
			throw new InvalidOperationException();
		}
		List<string> destColNames = getDestColNames();
		Dictionary<string, string> dictionary = getcolumnMappingMap(table, destColNames);
		Dictionary<int, string> dictionary2 = new Dictionary<int, string>();
		Dictionary<string, List<object>> dictionary3 = new Dictionary<string, List<object>>();
		string text = null;
		for (int i = 0; i < table.Columns.Count; i++)
		{
			text = table.Columns[i].ColumnName;
			if (dictionary.Count > 0 && dictionary.ContainsKey(text))
			{
				text = dictionary[text];
			}
			if (destColNames.Contains<string>(text, getCaseSensitiveComparer()))
			{
				dictionary2.Add(i, getCaseSensitiveName(text));
				dictionary3.Add(getCaseSensitiveName(text), new List<object>());
			}
		}
		if (dictionary2.Count == 0)
		{
			throw new InvalidOperationException();
		}
		int num = 0;
		DataRow dataRow = null;
		for (int j = 0; j < table.Rows.Count; j++)
		{
			num++;
			dataRow = table.Rows[j];
			foreach (int key in dictionary2.Keys)
			{
				dictionary3[dictionary2[key]].Add(dataRow[key]);
			}
			if (num != BatchSize)
			{
				continue;
			}
			num = 0;
			insert(dictionary3);
			foreach (string key2 in dictionary3.Keys)
			{
				dictionary3[key2].Clear();
			}
		}
		if (num > 0)
		{
			insert(dictionary3);
		}
	}

	public void Close()
	{
		closed = true;
	}

	private void Dispose(bool disposing)
	{
		if (!closed)
		{
			Close();
		}
	}

	void IDisposable.Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected void OnRowCopied(DmRowsCopiedEventArgs arg)
	{
		if (this.DmRowsCopied != null)
		{
			this.DmRowsCopied(this, arg);
		}
	}
}
