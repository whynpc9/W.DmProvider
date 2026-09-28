namespace Dm;

public class ColumnInfo
{
	private string columnName;

	private short columnId;

	private short columnType;

	private short columnLen;

	private short columnPrecise;

	public ColumnInfo()
	{
	}

	public ColumnInfo(string columnName, short columnId, short columnType, short columnLen, short columnPrecise)
	{
		this.columnName = columnName;
		this.columnId = columnId;
		this.columnType = columnType;
		this.columnLen = columnLen;
		this.columnPrecise = columnPrecise;
	}

	public string getColumnName()
	{
		return columnName;
	}

	public void setColumnName(string columnName)
	{
		this.columnName = columnName;
	}

	public short getColumnId()
	{
		return columnId;
	}

	public void setColumnId(short columnId)
	{
		this.columnId = columnId;
	}

	public short getColumnType()
	{
		return columnType;
	}

	public void setColumnType(short columnType)
	{
		this.columnType = columnType;
	}

	public short getColumnLen()
	{
		return columnLen;
	}

	public void setColumnLen(short columnLen)
	{
		this.columnLen = columnLen;
	}

	public short getColumnPrecise()
	{
		return columnPrecise;
	}

	public void setColumnPrecise(short columnPrecise)
	{
		this.columnPrecise = columnPrecise;
	}

	public override string ToString()
	{
		return "ColumnInfo2{columnName='" + columnName + "', columnId=" + columnId + ", columnType=" + columnType + ", columnLen=" + columnLen + ", columnPrecise=" + columnPrecise + "}";
	}
}
