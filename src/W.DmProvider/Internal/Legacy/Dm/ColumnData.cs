using System.Collections.Generic;
using System.Text;

namespace W.Dm;

public class ColumnData
{
	private int isAllNotNull;

	private byte[] nullArr;

	private int columnIndex;

	private short sqlType;

	private List<object> data;

	public ColumnData()
	{
	}

	public ColumnData(int isAllNotNull, byte[] nullArr, int columnIndex, short sqlType, List<object> data)
	{
		this.isAllNotNull = isAllNotNull;
		this.nullArr = nullArr;
		this.columnIndex = columnIndex;
		this.sqlType = sqlType;
		this.data = data;
	}

	public int getIsAllNotNull()
	{
		return isAllNotNull;
	}

	public void setIsAllNotNull(int isAllNotNull)
	{
		this.isAllNotNull = isAllNotNull;
	}

	public byte[] getNullArr()
	{
		return nullArr;
	}

	public void setNullArr(byte[] nullArr)
	{
		this.nullArr = nullArr;
	}

	public int getColumnIndex()
	{
		return columnIndex;
	}

	public void setColumnIndex(int columnIndex)
	{
		this.columnIndex = columnIndex;
	}

	public short getSqlType()
	{
		return sqlType;
	}

	public void setSqlType(short sqlType)
	{
		this.sqlType = sqlType;
	}

	public List<object> getData()
	{
		return data;
	}

	public void setData(List<object> data)
	{
		this.data = data;
	}

	public override string ToString()
	{
		return "ColumnData{isAllNotNull=" + isAllNotNull + ", nullArr=" + Encoding.UTF8.GetString(nullArr) + ", columnIndex=" + columnIndex + ", sqlType=" + sqlType + ", data=" + data?.ToString() + "}";
	}
}
