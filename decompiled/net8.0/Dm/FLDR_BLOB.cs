using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using A;
using Dm.util;

namespace Dm;

internal class FLDR_BLOB : MSG<FLDR_BLOB>
{
	private int setId;

	private int isOver;

	private int columnOver;

	private int offset;

	private List<LobData> lobList;

	public const short NEGATIVE_ONE = -1;

	private const int FIXED_LOB_LENGTH = 16;

	private const int ZERO = 0;

	private const int ONE = 1;

	public const long LOB_MAX_SIZE = 534773760L;

	public int columnIndex;

	public int rowIndex;

	private bool interrupt;

	private byte[] streamResult;

	public FLDR_BLOB(B access, int setId, List<LobData> lobList, int columnIndex, int rowIndex, int offset, byte[] streamResult)
		: base(access, (short)61)
	{
		this.setId = setId;
		this.lobList = lobList;
		this.columnIndex = columnIndex;
		this.rowIndex = rowIndex;
		this.offset = offset;
		this.streamResult = streamResult;
	}

	public FLDR_BLOB(B access, int setId, List<LobData> lobList, int columnIndex, int rowIndex, int offset, byte[] streamResult, Dictionary<int, Exception> errRowMap)
		: base(access, (short)61)
	{
		this.setId = setId;
		this.lobList = lobList;
		this.columnIndex = columnIndex;
		this.rowIndex = rowIndex;
		this.offset = offset;
		this.streamResult = streamResult;
		if (errRowMap == null || errRowMap.Count <= 0 || lobList == null)
		{
			return;
		}
		foreach (int item in errRowMap.Keys.OrderByDescending((int key) => key).ToList())
		{
			this.lobList.RemoveAt(item);
		}
	}

	protected override void doEncode()
	{
		if (lobList != null && lobList.Count > 0)
		{
			while (columnIndex < lobList.Count)
			{
				LobData lobData = lobList.get(columnIndex);
				try
				{
					processSingleColumn(lobData.getSqlType(), lobData.getColumnIndex(), lobData.getLobs());
				}
				catch (IOException value)
				{
					Console.WriteLine(value);
				}
				if (interrupt)
				{
					break;
				}
				rowIndex = 0;
				columnIndex++;
			}
		}
		checkOver();
	}

	private void processSingleColumn(int sqlType, short colIndex, List<object> lobs)
	{
		while (rowIndex < lobs.Count)
		{
			switch (sqlType)
			{
			case -1:
				writeNullLob(colIndex);
				break;
			case 12:
				writeBlob(colIndex, lobs);
				break;
			case 19:
				writeClob(colIndex, lobs);
				break;
			}
			if (!interrupt)
			{
				offset = 0;
				rowIndex++;
				continue;
			}
			break;
		}
	}

	private void checkOver()
	{
		if (interrupt)
		{
			isOver = 0;
			columnOver = 0;
		}
		else
		{
			isOver = 1;
			columnOver = 1;
		}
	}

	private void writeClob(short colIndex, List<object> lobs)
	{
		object obj = lobs.get(rowIndex);
		if (obj == null)
		{
			writeNullLob(colIndex);
		}
		else if (obj is DmClob)
		{
			processClob(colIndex, obj);
		}
		else if (obj is byte[] o)
		{
			processBytes(colIndex, o);
		}
		else
		{
			processClob(colIndex, DmClob.newInstance(obj.ToString(), access.A.Conn));
		}
	}

	private void writeBlob(short colIndex, List<object> lobs)
	{
		object obj = lobs.get(rowIndex);
		if (obj == null)
		{
			writeNullLob(colIndex);
		}
		else if (obj is DmBlob)
		{
			processBlob(colIndex, obj);
		}
		else if (obj is byte[])
		{
			processBlob(colIndex, DmBlob.newInstanceOfLocal((byte[])obj, access.A.Conn));
		}
		else if (obj is int || obj is long || obj is short || obj is byte)
		{
			processBlob(colIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromLong(Convert.ToInt64(obj.ToString(), 16)), access.A.Conn));
		}
		else if (obj is double)
		{
			processBlob(colIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromDouble((double)obj), access.A.Conn));
		}
		else if (obj is float)
		{
			processBlob(colIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromFloat((float)obj), access.A.Conn));
		}
		else
		{
			processBlob(colIndex, DmBlob.newInstanceOfLocal(obj.ToString().getBytes(access.a().ServerEncoding), access.A.Conn));
		}
	}

	private void processBlob(short colIndex, object o)
	{
		DmBlob dmBlob = (DmBlob)o;
		if (dmBlob == null)
		{
			writeNullLob(colIndex);
			return;
		}
		streamResult = dmBlob.GetBytes(offset, (int)dmBlob.do_length());
		fillStreamBody(colIndex);
	}

	private void processClob(short colIndex, object o)
	{
		DmClob dmClob = (DmClob)o;
		if (dmClob == null)
		{
			writeNullLob(colIndex);
			return;
		}
		streamResult = dmClob.GetBytes(0L, (int)dmClob.do_length());
		fillStreamBody(colIndex);
	}

	private void processBytes(short colIndex, byte[] o)
	{
		if (o == null || o.Length == 0)
		{
			writeNullLob(colIndex);
			return;
		}
		streamResult = o;
		fillStreamBody(colIndex);
	}

	private void fillStreamBody(short colIndex)
	{
		int length = streamResult.Length;
		if (534773760L - (long)access.A.g() < 16)
		{
			interrupt = true;
			return;
		}
		length = writeLobProperties(colIndex, length);
		access.A.a(streamResult, offset, length);
		offset += length;
		interruptCheck(offset, streamResult.Length);
	}

	private void interruptCheck(int curOffset, int totalLen)
	{
		if (curOffset < totalLen)
		{
			interrupt = true;
		}
	}

	private void writeNullLob(short colIndex)
	{
		if (534773760L - (long)access.A.g() < 8)
		{
			interrupt = true;
			return;
		}
		access.A.A(colIndex);
		access.A.A((short)rowIndex);
		access.A.H(-1);
	}

	private int writeLobProperties(short colIndex, int length)
	{
		access.A.A(colIndex);
		access.A.A((short)rowIndex);
		access.A.H(length);
		access.A.H(offset);
		length = Math.Min(300000000, length - offset);
		access.A.H(length);
		return length;
	}

	protected override void afterEncode()
	{
		access.A.B(0, (command != null) ? command.Statement.g() : 0);
		access.A.A(4, cmd);
		access.A.B(6, access.A.g() - 64);
		access.A.B(20, setId);
		access.A.B(52, isOver);
		access.A.B(56, columnOver);
	}

	protected override FLDR_BLOB doDecode()
	{
		if (interrupt)
		{
			interrupt = false;
			return this;
		}
		streamResult = null;
		return null;
	}

	public static List<LobData> lobProcess(List<ColumnData> columnDataList, TableInfo tableInfo)
	{
		List<ColumnInfo> columnInfos = tableInfo.getColumnInfos();
		if (columnInfos == null || columnInfos.Count == 0)
		{
			return Enumerable.Empty<LobData>().ToList();
		}
		List<int> list = new List<int>();
		foreach (ColumnInfo item in columnInfos)
		{
			if (19 == item.getColumnType() || 12 == item.getColumnType())
			{
				list.add(Convert.ToInt32(item.getColumnId()));
			}
		}
		if (list != null && list.Count > 0)
		{
			List<LobData> list2 = new List<LobData>();
			Dictionary<int, ColumnData> dictionary = new Dictionary<int, ColumnData>();
			foreach (ColumnData columnData2 in columnDataList)
			{
				if (19 == columnData2.getSqlType() || 12 == columnData2.getSqlType())
				{
					dictionary.put(columnData2.getColumnIndex(), columnData2);
				}
			}
			{
				foreach (int item2 in list)
				{
					ColumnData columnData = dictionary.get(item2);
					if (columnData == null)
					{
						list2.add(new LobData((short)item2, null, -1));
					}
					else
					{
						list2.add(new LobData((short)item2, columnData.getData(), columnData.getSqlType()));
					}
				}
				return list2;
			}
		}
		return Enumerable.Empty<LobData>().ToList();
	}
}
