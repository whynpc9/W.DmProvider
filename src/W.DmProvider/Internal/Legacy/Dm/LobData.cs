using System.Collections.Generic;

namespace W.Dm;

public class LobData
{
	private short columnIndex;

	private List<object> lobs;

	private int sqlType;

	private List<short> nullIndex;

	public LobData()
	{
	}

	public LobData(short columnIndex, List<object> lobs, int sqlType)
	{
		this.columnIndex = columnIndex;
		this.lobs = lobs;
		this.sqlType = sqlType;
	}

	public LobData(short columnIndex, List<object> lobs, int sqlType, List<short> nullIndex)
	{
		this.columnIndex = columnIndex;
		this.lobs = lobs;
		this.sqlType = sqlType;
		this.nullIndex = nullIndex;
	}

	public short getColumnIndex()
	{
		return columnIndex;
	}

	public void setColumnIndex(short columnIndex)
	{
		this.columnIndex = columnIndex;
	}

	public List<object> getLobs()
	{
		return lobs;
	}

	public void setLobs(List<object> lobs)
	{
		this.lobs = lobs;
	}

	public int getSqlType()
	{
		return sqlType;
	}

	public void setSqlType(int sqlType)
	{
		this.sqlType = sqlType;
	}

	public List<short> getNullIndex()
	{
		return nullIndex;
	}

	public void setNullIndex(List<short> nullIndex)
	{
		this.nullIndex = nullIndex;
	}

	public override string ToString()
	{
		return "LobData{columnIndex=" + columnIndex + ", lobs=" + lobs?.ToString() + ", sqlType=" + sqlType + ", nullIndex=" + nullIndex?.ToString() + "}";
	}
}
