using System.Collections.Generic;

namespace Dm;

public class HorizontalTableInfo
{
	private string tableName;

	private int tableId;

	private int baseTableId;

	private int tableType;

	private short baseTableType;

	private List<int> subTableIds;

	private short[] partitionColIds;

	private short[] sqlType;

	private List<object[]> boundaries;

	private List<int> includeFlag;

	private int defaultPartition = -1;

	private List<HorizontalTableInfo> children;

	private HorizontalTableInfo parent;

	private int raftId;

	public string getTableName()
	{
		return tableName;
	}

	public void setTableName(string tableName)
	{
		this.tableName = tableName;
	}

	public int getTableId()
	{
		return tableId;
	}

	public void setTableId(int tableId)
	{
		this.tableId = tableId;
	}

	public int getBaseTableId()
	{
		return baseTableId;
	}

	public void setBaseTableId(int baseTableId)
	{
		this.baseTableId = baseTableId;
	}

	public int getTableType()
	{
		return tableType;
	}

	public void setTableType(int tableType)
	{
		this.tableType = tableType;
	}

	public int getDefaultPartition()
	{
		return defaultPartition;
	}

	public void setDefaultPartition(int defaultPartition)
	{
		this.defaultPartition = defaultPartition;
	}

	public short getBaseTableType()
	{
		return baseTableType;
	}

	public void setBaseTableType(short baseTableType)
	{
		this.baseTableType = baseTableType;
	}

	public List<int> getSubTableIds()
	{
		return subTableIds;
	}

	public void setSubTableIds(List<int> subTableIds)
	{
		this.subTableIds = subTableIds;
	}

	public short[] getPartitionColIds()
	{
		return partitionColIds;
	}

	public void setPartitionColIds(short[] partitionColIds)
	{
		this.partitionColIds = partitionColIds;
	}

	public short[] getSqlType()
	{
		return sqlType;
	}

	public void setSqlType(short[] sqlType)
	{
		this.sqlType = sqlType;
	}

	public List<object[]> getBoundaries()
	{
		return boundaries;
	}

	public void setBoundaries(List<object[]> boundaries)
	{
		this.boundaries = boundaries;
	}

	public List<int> getIncludeFlag()
	{
		return includeFlag;
	}

	public void setIncludeFlag(List<int> includeFlag)
	{
		this.includeFlag = includeFlag;
	}

	public List<HorizontalTableInfo> getChildren()
	{
		return children;
	}

	public void setChildren(List<HorizontalTableInfo> children)
	{
		this.children = children;
	}

	public HorizontalTableInfo getParent()
	{
		return parent;
	}

	public int getRaftId()
	{
		return raftId;
	}

	public void setRaftId(int raftId)
	{
		this.raftId = raftId;
	}

	public void setParent(HorizontalTableInfo parent)
	{
		this.parent = parent;
	}
}
