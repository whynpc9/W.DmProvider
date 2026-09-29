using System.Collections.Generic;
using System.Threading;
using W.Dm.Internal.Legacy.A;

namespace W.Dm;

internal class Fldr
{
	public B dbAccess;

	public short setId;

	public Dictionary<string, short> tableNameId = new Dictionary<string, short>();

	public Dictionary<short, string> idTableName = new Dictionary<short, string>();

	public object lockForClearParameters = new object();

	public Fldr(B dbAccess)
	{
		if (dbAccess == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL);
		}
		this.dbAccess = dbAccess;
	}

	public TableInfo getTableInfo(string schemaName, string tableName, byte lockFlag)
	{
		try
		{
			return dbAccess.A(schemaName, tableName, lockFlag);
		}
		catch (DmException ex)
		{
			if (ex.ErrorCode == -9106)
			{
				throw new DmException("存在使用相同FldrConfig和DmConnection创建的FldrStatement且未关闭，若使用DmBulkCopy2请检查是否调用Dispose方法或Close方法");
			}
			throw;
		}
	}

	public List<FldrIndexInfo> getIndexInfo(string schemaName, string tableName, int parallelFlag, int indexOption)
	{
		return dbAccess.A(schemaName, tableName, parallelFlag, indexOption);
	}

	public FldrClusterInfo getMppInfo(string schemaName, string tableName)
	{
		return dbAccess.A(schemaName, tableName);
	}

	public string setEnvironment(SetEnvInfo setTableInfo, HashSet<int> defaultColumns, List<FldrIndexInfo> indexInfos)
	{
		return dbAccess.A(setTableInfo, defaultColumns, indexInfos);
	}

	public void insert(List<ColumnData> columnDataList, TableInfo tableInfo, int rows, int setId, int maxError, CancellationTokenSource serviceCts)
	{
		dbAccess.A(columnDataList, tableInfo, rows, setId, maxError, serviceCts);
	}

	public List<FldrIndexInfo> clearEnvironment(int commitFlag)
	{
		return dbAccess.A(commitFlag);
	}

	public void resetIndexInfo(List<FldrIndexInfo> indexInfos)
	{
		dbAccess.A(indexInfos);
	}

	public short getSetId(string tableName, short bldrNum)
	{
		try
		{
			return tableNameId[tableName];
		}
		catch (KeyNotFoundException)
		{
			if (setId < bldrNum)
			{
				tableNameId.Add(tableName, setId);
				idTableName.Add(setId, tableName);
				return setId++;
			}
			short num = (short)(setId % bldrNum);
			string key = idTableName[num];
			tableNameId.Remove(key);
			tableNameId.Add(tableName, num);
			idTableName.Add(num, tableName);
			setId++;
			return num;
		}
	}

	public void clearParameters()
	{
		lock (lockForClearParameters)
		{
			tableNameId.Clear();
			idTableName.Clear();
		}
	}
}
