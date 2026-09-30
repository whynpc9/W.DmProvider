using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using W.Dm.util;

namespace W.Dm;

public class DmBulkCopy2 : IDisposable
{
	private int m_BatchSize = -1;

	private string m_DestTable;

	private DmConnection m_Conn;

	private bool closed;

	public string schema;

	private int m_timeout = -1;

	private int indexOption = 1;

	private bool setIdentity;

	public readonly object DestinationTableNameLock = new object();

	private FldrConfig fldrConfig;

	private FldrStatement fldrStatement;

	private List<ColumnInfo> destTableColumnInfo;

	private List<int> ColumnMapIndexList;

	public List<DmBulkCopyColumnMapping> ColumnMappings = new List<DmBulkCopyColumnMapping>();

	public int BatchSize
	{
		get
		{
			if (m_BatchSize == -1)
			{
				return 100;
			}
			return m_BatchSize;
		}
		set
		{
			if (value > 10000 || value < 100)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, "BatchSize range is [100, 10000]");
			}
			m_BatchSize = value;
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
			m_timeout = value;
		}
	}

	public string DestinationTableName
	{
		get
		{
			return m_DestTable;
		}
		set
		{
			lock (DestinationTableNameLock)
			{
				if (value == null)
				{
					throw new ArgumentNullException("DestinationTableName");
				}
				if (value.Length == 0)
				{
					throw new ArgumentOutOfRangeException("DestinationTableName");
				}
				if (!m_DestTable.isEmpty() && !m_DestTable.Equals(value))
				{
					DmError.ThrowDmException("DestTable不能二次赋值");
				}
				m_DestTable = value;
			}
		}
	}

	public int IndexOption
	{
		get
		{
			return indexOption;
		}
		set
		{
			if (value != 1 && value != 2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, "IndexOption目前只支持1、2");
			}
			indexOption = value;
		}
	}

	public bool SetIdentity
	{
		get
		{
			return setIdentity;
		}
		set
		{
			setIdentity = value;
		}
	}

	public DmBulkCopy2(DmConnection conn)
	{
		throw new NotSupportedException("Native bulk loading is unsupported.");
		if (conn == null)
		{
			throw new InvalidOperationException();
		}
		m_Conn = conn;
	}

	public DmBulkCopy2(string connectionString)
		: this(new DmConnection(connectionString))
	{
	}

	public void WriteToServer(DataTable dataTable)
	{
		InitFldrConfig(null, dataTable);
		int rowCount = 0;
		for (int i = 0; i < dataTable.Rows.Count; i++)
		{
			for (int j = 0; j < ColumnMapIndexList.Count; j++)
			{
				if (ColumnMapIndexList[j] >= 0)
				{
					fldrStatement.setObject(j + 1, dataTable.Rows[i][ColumnMapIndexList[j]]);
				}
			}
			AddAndExecuteBatch(ref rowCount);
		}
		ExecuteRemainingData(rowCount);
	}

	public void WriteToServer(DataRow[] dataRows)
	{
		if (dataRows == null || dataRows.Length == 0)
		{
			throw new ArgumentNullException("dataRows is null");
		}
		DataTable table = dataRows[0].Table;
		InitFldrConfig(null, table);
		int rowCount = 0;
		for (int i = 0; i < dataRows.Length; i++)
		{
			for (int j = 0; j < ColumnMapIndexList.Count; j++)
			{
				if (ColumnMapIndexList[j] >= 0)
				{
					fldrStatement.setObject(j + 1, dataRows[i][ColumnMapIndexList[j]]);
				}
			}
			AddAndExecuteBatch(ref rowCount);
		}
		ExecuteRemainingData(rowCount);
	}

	public void WriteToServer(DataTable dataTable, DataRowState rowState)
	{
		InitFldrConfig(null, dataTable);
		int rowCount = 0;
		for (int i = 0; i < dataTable.Rows.Count; i++)
		{
			DataRow dataRow = dataTable.Rows[i];
			if (dataRow.RowState == DataRowState.Deleted || dataRow.RowState != rowState)
			{
				continue;
			}
			for (int j = 0; j < ColumnMapIndexList.Count; j++)
			{
				if (ColumnMapIndexList[j] >= 0)
				{
					fldrStatement.setObject(j + 1, dataRow[ColumnMapIndexList[j]]);
				}
			}
			AddAndExecuteBatch(ref rowCount);
		}
		ExecuteRemainingData(rowCount);
	}

	public void WriteToServer(IDataReader reader)
	{
		InitFldrConfig(reader, null);
		int rowCount = 0;
		while (reader.Read())
		{
			for (int i = 0; i < ColumnMapIndexList.Count; i++)
			{
				if (ColumnMapIndexList[i] >= 0)
				{
					fldrStatement.setObject(i + 1, reader.GetValue(ColumnMapIndexList[i]));
				}
			}
			AddAndExecuteBatch(ref rowCount);
		}
		ExecuteRemainingData(rowCount);
	}

	private void InitFldrConfig(IDataReader reader, DataTable table)
	{
		if (fldrConfig != null)
		{
			return;
		}
		if (reader == null && table == null)
		{
			throw new ArgumentNullException("reader and table is null");
		}
		if (DestinationTableName == null)
		{
			throw new InvalidOperationException("DestinationTableName is null");
		}
		fldrConfig = new FldrConfig
		{
			maxRows = BatchSize,
			schemaName = getSchema(),
			tableName = DestinationTableName,
			indexOption = IndexOption,
			setIdentity = SetIdentity
		};
		fldrStatement = m_Conn.fldrStatement(fldrConfig);
		destTableColumnInfo = fldrStatement.getTableColumnInfo();
		bool flag = false;
		ColumnMapIndexList = new List<int>();
		for (int i = 0; i < destTableColumnInfo.Count; i++)
		{
			ColumnMapIndexList.Add(-1);
			foreach (DmBulkCopyColumnMapping columnMapping in ColumnMappings)
			{
				if (columnMapping.DestinationOrdinal >= 0 && columnMapping.DestinationOrdinal == i)
				{
					ColumnMapIndexList[i] = columnMapping.SourceOrdinal;
					break;
				}
				if (string.IsNullOrEmpty(columnMapping.DestinationColumn) || !columnMapping.DestinationColumn.Equals(destTableColumnInfo[i].getColumnName()))
				{
					continue;
				}
				if (reader != null)
				{
					for (int j = 0; j < reader.FieldCount; j++)
					{
						if (reader.GetName(j).Equals(columnMapping.SourceColumn))
						{
							ColumnMapIndexList[i] = j;
							break;
						}
					}
					break;
				}
				for (int k = 0; k < table.Columns.Count; k++)
				{
					if (table.Columns[k].ColumnName.Equals(columnMapping.SourceColumn))
					{
						ColumnMapIndexList[i] = k;
						break;
					}
				}
				break;
			}
			if (!flag && ColumnMapIndexList[i] >= 0)
			{
				flag = true;
			}
		}
		if (flag)
		{
			return;
		}
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		int num = 0;
		if (reader != null)
		{
			for (int l = 0; l < reader.FieldCount; l++)
			{
				dictionary.Add(reader.GetName(l), num++);
			}
		}
		else
		{
			foreach (DataColumn column in table.Columns)
			{
				dictionary.Add(column.ColumnName, num++);
			}
		}
		if (!ColumnNameMatched(destTableColumnInfo, dictionary))
		{
			throw new Exception("源表与目标表所有列均不匹配");
		}
		for (int m = 0; m < destTableColumnInfo.Count; m++)
		{
			if (dictionary.TryGetValue(destTableColumnInfo[m].getColumnName(), out var value))
			{
				ColumnMapIndexList[m] = value;
			}
		}
	}

	private static bool ColumnNameMatched(List<ColumnInfo> destTableColumnInfo, Dictionary<string, int> nameIdMap)
	{
		return destTableColumnInfo.Any((ColumnInfo destTableColumn) => nameIdMap.TryGetValue(destTableColumn.getColumnName(), out var _));
	}

	private void AddAndExecuteBatch(ref int rowCount)
	{
		fldrStatement.addBatch();
		rowCount++;
		if (rowCount % BatchSize == 0)
		{
			fldrStatement.executeBatch();
			if (m_Conn.GetConnInstance().ConnProperty.AutoCommit)
			{
				Commit();
			}
			rowCount = 0;
		}
	}

	private void ExecuteRemainingData(int rowCount)
	{
		if (rowCount % BatchSize != 0)
		{
			fldrStatement.executeBatch();
			if (m_Conn.GetConnInstance().ConnProperty.AutoCommit)
			{
				Commit();
			}
		}
	}

	public string getSchema()
	{
		if (!schema.isEmpty())
		{
			return schema;
		}
		return m_Conn.Schema;
	}

	public void Commit()
	{
		fldrStatement.Commit();
	}

	public void Close()
	{
		fldrStatement?.close();
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
}
