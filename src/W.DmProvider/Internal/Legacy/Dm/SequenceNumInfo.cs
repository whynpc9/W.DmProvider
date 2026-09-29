namespace W.Dm;

public class SequenceNumInfo
{
	private string schemaName;

	private string tableVersionName;

	private string columnName;

	private short columnId;

	private int sequenceId;

	public SequenceNumInfo()
	{
	}

	public SequenceNumInfo(string schemaName, string tableVersionName, string columnName, short columnId, int sequenceId)
	{
		this.schemaName = schemaName;
		this.tableVersionName = tableVersionName;
		this.columnName = columnName;
		this.columnId = columnId;
		this.sequenceId = sequenceId;
	}

	public string getSchemaName()
	{
		return schemaName;
	}

	public void setSchemaName(string schemaName)
	{
		this.schemaName = schemaName;
	}

	public string getTableVersionName()
	{
		return tableVersionName;
	}

	public void setTableVersionName(string tableVersionName)
	{
		this.tableVersionName = tableVersionName;
	}

	public string getColumnName()
	{
		return columnName;
	}

	public void setColumnName(string columnName)
	{
		this.columnName = columnName;
	}

	public int getSequenceId()
	{
		return sequenceId;
	}

	public void setSequenceId(int sequenceId)
	{
		this.sequenceId = sequenceId;
	}

	public short getColumnId()
	{
		return columnId;
	}

	public void setColumnId(short columnId)
	{
		this.columnId = columnId;
	}

	public override string ToString()
	{
		return "SequenceNumInfo{schemaName='" + schemaName + "', tableVersionName='" + tableVersionName + "', columnName='" + columnName + "', columnId=" + columnId + ", sequenceId=" + sequenceId + "}";
	}

	protected object Clone()
	{
		return MemberwiseClone();
	}
}
