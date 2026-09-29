using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Legacy.A;
using W.Dm.filter.fldr;
using W.Dm.util;

namespace W.Dm;

internal class FLDR_INSERT : MSG<int>
{
	public class Callback2
	{
		public delegate void runDelegate(List<ColumnData> siteObject, int colIndex);

		public runDelegate run;
	}

	private int rows;

	private short columns;

	private static byte compress = 0;

	private List<ColumnData> columnDataList;

	public int setId;

	private TableInfo tableInfo;

	public const byte NEGATIVE_END = 102;

	public const string ZERO_STR = "0";

	public const char ZERO_CHAR = '0';

	private const byte DEC_SIGN_ZERO = 128;

	private const int DEC_MAX_INDEX = 22;

	public static readonly byte[][] charToDigit = new byte[10][]
	{
		new byte[10] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
		new byte[10] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
		new byte[10] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 },
		new byte[10] { 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 },
		new byte[10] { 41, 42, 43, 44, 45, 46, 47, 48, 49, 50 },
		new byte[10] { 51, 52, 53, 54, 55, 56, 57, 58, 59, 60 },
		new byte[10] { 61, 62, 63, 64, 65, 66, 67, 68, 69, 70 },
		new byte[10] { 71, 72, 73, 74, 75, 76, 77, 78, 79, 80 },
		new byte[10] { 81, 82, 83, 84, 85, 86, 87, 88, 89, 90 },
		new byte[10] { 91, 92, 93, 94, 95, 96, 97, 98, 99, 100 }
	};

	private static readonly byte[] ZERO_ARR = new byte[28]
	{
		128, 0, 0, 0, 0, 1, 128, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0
	};

	private CancellationTokenSource bindServiceCts;

	public int maxError = 1;

	public FLDR_INSERT(B access, List<ColumnData> columnDataList, TableInfo tableInfo, int rows, int setId, int maxError, CancellationTokenSource serviceCts)
		: base(access, (short)55)
	{
		this.columnDataList = columnDataList;
		columns = (short)tableInfo.getColumnInfos().size();
		this.rows = rows;
		this.setId = setId;
		this.tableInfo = tableInfo;
		bindServiceCts = serviceCts;
		this.maxError = maxError;
		multiThreadJ2DB();
	}

	public FLDR_INSERT(B access, List<ColumnData> columnDataList, TableInfo tableInfo, int rows, int setId)
		: base(access, (short)55)
	{
		this.columnDataList = columnDataList;
		columns = (short)tableInfo.getColumnInfos().size();
		this.rows = rows;
		this.setId = setId;
		this.tableInfo = tableInfo;
	}

	public FLDR_INSERT()
		: base((B)null, (short)55)
	{
	}

	public void multiThreadJ2DB()
	{
		if (columnDataList == null || columnDataList.size() == 0)
		{
			return;
		}
		doTaskOnEverySite(bindServiceCts, columnDataList, columnDataList.size(), new Callback2
		{
			run = delegate(List<ColumnData> list, int colIndex)
			{
				ColumnData columnData = columnDataList.get(colIndex);
				List<object> data = columnData.getData();
				switch (columnData.getSqlType())
				{
				case 7:
					getInteger(data);
					break;
				case 0:
				case 1:
				case 2:
					getVarchar(data, access.a().ServerEncoding);
					break;
				case 17:
				case 18:
					getBinary(data, tableInfo.getColumnInfos().get(colIndex));
					break;
				case 9:
					getDecimal(data);
					break;
				case 11:
					getDouble(data);
					break;
				case 10:
					getReal(data);
					break;
				case 8:
					getBigInt(data);
					break;
				case 3:
					getBit(data);
					break;
				case 5:
					getTinyInt(data);
					break;
				case 6:
					getSmallInt(data);
					break;
				case 14:
				case 15:
				case 16:
				case 22:
				case 23:
					getDate(data);
					break;
				case 26:
				case 27:
					getDate2(data);
					break;
				case 20:
					getIntervalYM(data);
					break;
				case 21:
					getIntervalDT(data);
					break;
				case 28:
					getRowId(data, access.__t02_field_04000ABA.Conn);
					break;
				default:
					DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORTED_TYPE);
					break;
				case 12:
				case 19:
					break;
				}
			}
		});
	}

	public static object[] batchThreadJ2DB(List<ColumnData> columnDataList, TableInfo tableInfo, DmConnection conn, int maxError)
	{
		if (columnDataList != null && columnDataList.size() != 0)
		{
			int num = 0;
			Dictionary<int, Exception> dictionary = new Dictionary<int, Exception>();
			for (int i = 0; i < columnDataList.size(); i++)
			{
				ColumnData columnData = columnDataList.get(i);
				List<object> data = columnData.getData();
				switch (columnData.getSqlType())
				{
				case 7:
					num += getInteger(maxError, data, dictionary);
					break;
				case 0:
				case 1:
				case 2:
					num += getVarchar(maxError, data, conn.GetConnInstance().ConnProperty.ServerEncoding, tableInfo.getColumnInfos().get(i).getColumnLen(), dictionary);
					break;
				case 17:
				case 18:
					num += getBinary(maxError, data, tableInfo.getColumnInfos().get(i), dictionary);
					break;
				case 9:
					num += getDecimal(maxError, data, dictionary);
					break;
				case 11:
					num += getDouble(maxError, data, dictionary);
					break;
				case 10:
					num += getReal(maxError, data, dictionary);
					break;
				case 8:
					num += getBigInt(maxError, data, dictionary);
					break;
				case 3:
					num += getBit(maxError, data, dictionary);
					break;
				case 5:
					num += getTinyInt(maxError, data, dictionary);
					break;
				case 6:
					num += getSmallInt(maxError, data, dictionary);
					break;
				case 14:
				case 15:
				case 16:
					num += getDate(maxError, data, dictionary);
					break;
				case 22:
				case 23:
					num += getDateTZ(maxError, data, dictionary);
					break;
				case 26:
					num += getDate2(maxError, data, dictionary);
					break;
				case 27:
					num += getDate2TZ(maxError, data, dictionary);
					break;
				case 20:
					num += getIntervalYM(maxError, data, dictionary);
					break;
				case 21:
					num += getIntervalDT(maxError, data, dictionary);
					break;
				case 28:
					num += getRowId(maxError, data, conn, dictionary);
					break;
				default:
					num += getDefault(data, dictionary, i + 1, columnData.getSqlType());
					break;
				case 12:
				case 19:
					break;
				}
				if (maxError > 0 && num >= maxError)
				{
					return new object[2] { num, dictionary };
				}
			}
			return new object[2] { num, dictionary };
		}
		return new object[2]
		{
			0,
			new Dictionary<int, List<Exception>>(0)
		};
	}

	protected override void doEncode()
	{
		access.__t02_field_04000AB9.H(rows);
		access.__t02_field_04000AB9.A(columns);
		access.__t02_field_04000AB9.A(8, true, true);
		access.__t02_field_04000AB9.A(compress);
		foreach (ColumnData columnData in columnDataList)
		{
			access.__t02_field_04000AB9.A(columnData.getSqlType());
		}
		foreach (ColumnData columnData2 in columnDataList)
		{
			if (columnData2.getSqlType() == 19 || columnData2.getSqlType() == 12)
			{
				access.__t02_field_04000AB9.H(0);
				access.__t02_field_04000AB9.A(new byte[rows]);
				continue;
			}
			access.__t02_field_04000AB9.H(columnData2.getIsAllNotNull());
			if (columnData2.getIsAllNotNull() == 0)
			{
				access.__t02_field_04000AB9.A(columnData2.getNullArr());
			}
			foreach (object datum in columnData2.getData())
			{
				access.__t02_field_04000AB9.A((byte[])datum);
			}
		}
	}

	private static int getBinary(int maxError, List<object> dataObjects, ColumnInfo columnInfo, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		int columnLen = columnInfo.getColumnLen();
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					byte[] array = processBinary(columnLen, obj);
					dataObjects.set(i, new object[2] { obj, array });
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getBinary(List<object> dataObjects, ColumnInfo columnInfo)
	{
		int columnLen = columnInfo.getColumnLen();
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				byte[] item = processBinary(columnLen, dataObjects.get(i));
				dataObjects.set(i, item);
			}
		}
	}

	private static byte[] processBinary(int precise, object o)
	{
		byte[] array;
		if (!(o is string))
		{
			array = ((!(o is byte[])) ? N2DB.toBinary(Convert.ToInt64(o.ToString(), 16), precise) : ((byte[])o));
		}
		else
		{
			string text = o.ToString();
			if (text.startsWith("0x") || text.startsWith("0X"))
			{
				text = text.substring(2);
			}
			array = StringUtil.hexStringToBytes(text, isbyte: true);
		}
		byte[] array2 = new byte[8 + array.Length];
		array2[4] = (byte)array.Length;
		array2[5] = (byte)(array.Length >> 8);
		array2[6] = (byte)(array.Length >> 16);
		array2[7] = (byte)(array.Length >> 24);
		Array.Copy(array, 0, array2, 8, array.Length);
		return array2;
	}

	private static int getRowId(int maxError, List<object> dataObjects, DmConnection conn, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					DmRowId dmRowId = null;
					dmRowId = ((!(obj is DmRowId)) ? DmRowId.valueOf(obj.ToString()) : ((DmRowId)obj));
					dataObjects.set(i, new object[2]
					{
						obj,
						dmRowId.encode(conn)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	public static int getDefault(List<object> dataObjects, Dictionary<int, Exception> errRowMap, int index, short sqlType)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) == null)
				{
					errRowMap.put(i, new Exception("the index: " + index + " cannot cast to sqlType: " + sqlType + " data: " + dataObjects.get(i)?.ToString() + StringUtil.LINE_SEPARATOR));
				}
			}
			return dataObjects.size();
		}
		return 0;
	}

	private static void getRowId(List<object> dataObjects, DmConnection conn)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				DmRowId dmRowId = (DmRowId)dataObjects.get(i);
				dataObjects.set(i, dmRowId.encode(conn));
			}
		}
	}

	private static int getIntervalDT(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					byte[] array = processIntervalDt(obj);
					dataObjects.set(i, new object[2] { obj, array });
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getIntervalDT(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processIntervalDt(dataObjects.get(i)));
			}
		}
	}

	private static byte[] processIntervalDt(object o)
	{
		DmIntervalDT dmIntervalDT = ((!(o is DmIntervalDT dmIntervalDT2)) ? new DmIntervalDT(o.ToString()) : dmIntervalDT2);
		return new byte[24]
		{
			(byte)dmIntervalDT.getDay(),
			(byte)(dmIntervalDT.getDay() >> 8),
			(byte)(dmIntervalDT.getDay() >> 16),
			(byte)(dmIntervalDT.getDay() >> 24),
			(byte)dmIntervalDT.getMsec(),
			(byte)(dmIntervalDT.getMsec() >> 8),
			(byte)(dmIntervalDT.getMsec() >> 16),
			(byte)(dmIntervalDT.getMsec() >> 24),
			(byte)dmIntervalDT.getHour(),
			(byte)(dmIntervalDT.getHour() >> 8),
			(byte)(dmIntervalDT.getHour() >> 16),
			(byte)(dmIntervalDT.getHour() >> 24),
			(byte)dmIntervalDT.getMinute(),
			(byte)(dmIntervalDT.getMinute() >> 8),
			(byte)(dmIntervalDT.getMinute() >> 16),
			(byte)(dmIntervalDT.getMinute() >> 24),
			(byte)dmIntervalDT.getScaleForSvr(),
			(byte)(dmIntervalDT.getScaleForSvr() >> 8),
			(byte)(dmIntervalDT.getScaleForSvr() >> 16),
			(byte)(dmIntervalDT.getScaleForSvr() >> 24),
			(byte)dmIntervalDT.getSecond(),
			(byte)(dmIntervalDT.getSecond() >> 8),
			(byte)(dmIntervalDT.getSecond() >> 16),
			(byte)(dmIntervalDT.getSecond() >> 24)
		};
	}

	private static int getIntervalYM(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					byte[] array = processIntervalYM(obj);
					dataObjects.set(i, new object[2] { obj, array });
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getIntervalYM(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processIntervalYM(dataObjects.get(i)));
			}
		}
	}

	private static byte[] processIntervalYM(object o)
	{
		DmIntervalYM dmIntervalYM = ((!(o is DmIntervalYM dmIntervalYM2)) ? new DmIntervalYM(o.ToString()) : dmIntervalYM2);
		return new byte[12]
		{
			(byte)dmIntervalYM.getYear(),
			(byte)(dmIntervalYM.getYear() >> 8),
			(byte)(dmIntervalYM.getYear() >> 16),
			(byte)(dmIntervalYM.getYear() >> 24),
			(byte)dmIntervalYM.getMonth(),
			(byte)(dmIntervalYM.getMonth() >> 8),
			(byte)(dmIntervalYM.getMonth() >> 16),
			(byte)(dmIntervalYM.getMonth() >> 24),
			(byte)dmIntervalYM.getScaleForSvr(),
			(byte)(dmIntervalYM.getScaleForSvr() >> 8),
			(byte)(dmIntervalYM.getScaleForSvr() >> 16),
			(byte)(dmIntervalYM.getScaleForSvr() >> 24)
		};
	}

	private static int getBit(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					dataObjects.set(i, new object[2]
					{
						obj,
						processBit(obj)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getBit(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processBit(dataObjects.get(i)));
			}
		}
	}

	private static byte[] processBit(object o)
	{
		int i;
		try
		{
			i = ((!Convert.ToInt64(o).Equals(0L)) ? 1 : 0);
		}
		catch (FormatException)
		{
			string text = o.ToString();
			i = ((!text.ToLower().Equals("false") && !text.Equals("0")) ? 1 : 0);
		}
		return ByteUtil.fromInt(i);
	}

	private static int getSmallInt(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					long num2 = Convert.ToInt64(obj);
					N2DB.checkSmallint(num2);
					dataObjects.set(i, new object[2]
					{
						obj,
						ByteUtil.fromInt((int)num2)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getSmallInt(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				long num = Convert.ToInt64(dataObjects.get(i));
				N2DB.checkSmallint(num);
				dataObjects.set(i, ByteUtil.fromInt((int)num));
			}
		}
	}

	private static int getTinyInt(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					long num2 = Convert.ToInt64(obj);
					N2DB.checkTinyint(num2);
					dataObjects.set(i, new object[2]
					{
						obj,
						ByteUtil.fromInt((int)num2)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getTinyInt(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				long num = Convert.ToInt64(dataObjects.get(i));
				N2DB.checkTinyint(num);
				dataObjects.set(i, ByteUtil.fromInt((int)num));
			}
		}
	}

	private static int getDate(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					byte[] array = processDate(obj);
					dataObjects.set(i, new object[2] { obj, array });
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getDate(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processDate(dataObjects.get(i)));
			}
		}
	}

	private static byte[] processDate(object o)
	{
		DateTime dateTime2;
		if (!(o is DateTime dateTime))
		{
			if (!(o is DateTimeOffset dateTimeOffset))
			{
				if (!(o is TimeSpan timeSpan))
				{
					throw new Exception($"数据{o}无法处理");
				}
				dateTime2 = new DateTime(timeSpan.Ticks);
			}
			else
			{
				dateTime2 = dateTimeOffset.DateTime;
			}
		}
		else
		{
			dateTime2 = dateTime;
		}
		byte[] array = FldrUtil.fromDate(dateTime2);
		byte[] array2 = ByteUtil.fromShort((short)(TimeZoneInfo.Local.BaseUtcOffset.TotalMilliseconds / 60000.0));
		return new byte[12]
		{
			array[0],
			array[1],
			array[2],
			array[3],
			array[4],
			array[5],
			array[6],
			array[7],
			array[8],
			array[9],
			array2[0],
			array2[1]
		};
	}

	private static int getDateTZ(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		return getDate2TZ(maxError, dataObjects, errRowMap);
	}

	private static byte[] processDate2Tz(object o)
	{
		int num = -1;
		DateTime dateTime2;
		if (o is DateTime dateTime)
		{
			dateTime2 = dateTime;
		}
		else
		{
			object[] array = parseStringDate(o.ToString());
			dateTime2 = (DateTime)array[0];
			num = (int)array[1];
		}
		byte[] array2 = FldrUtil.fromDate(dateTime2);
		if (num == -1)
		{
			num = (int)(TimeZoneInfo.Local.BaseUtcOffset.TotalMilliseconds / 60000.0);
		}
		byte[] array3 = ByteUtil.fromShort((short)num);
		return new byte[12]
		{
			array2[0],
			array2[1],
			array2[2],
			array2[3],
			array2[4],
			array2[5],
			array2[6],
			array2[7],
			array2[8],
			array2[9],
			array3[0],
			array3[1]
		};
	}

	public static object[] parseStringDate(string dateStr)
	{
		string str = null;
		bool flag = false;
		int num;
		if ((num = dateStr.indexOf(" +")) > 0)
		{
			str = dateStr.substring(Math.Min(num + 2, dateStr.length()));
			dateStr = dateStr.substring(0, num);
		}
		else if ((num = dateStr.indexOf(" -")) > 0)
		{
			flag = true;
			str = dateStr.substring(Math.Min(num + 2, dateStr.length()));
			dateStr = dateStr.substring(0, num);
		}
		DateTime dateTime = DateTime.Parse(dateStr);
		if (StringUtil.isNotEmpty(str))
		{
			string[] array = str.split(":");
			if (array.Length != 2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT);
			}
			int num2 = Convert.ToInt32(array[0]) * 60 + Convert.ToInt32(array[1]);
			return new object[2]
			{
				dateTime,
				flag ? (-num2) : num2
			};
		}
		return new object[2]
		{
			dateTime,
			TimeZoneInfo.Local.BaseUtcOffset.TotalMilliseconds / 60000.0
		};
	}

	private static int getDate2(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					dataObjects.set(i, new object[2]
					{
						obj,
						processDate(obj)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getDate2(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processDate(dataObjects.get(i)));
			}
		}
	}

	private static int getDate2TZ(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					byte[] array = processDate2Tz(obj);
					dataObjects.set(i, new object[2] { obj, array });
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static int getBigInt(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					long num2 = Convert.ToInt64(obj);
					N2DB.checkBigint(num2);
					dataObjects.set(i, new object[2]
					{
						obj,
						N2DB.toBigint(num2)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getBigInt(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				long num = Convert.ToInt64(dataObjects.get(i));
				N2DB.checkBigint(num);
				dataObjects.set(i, N2DB.toBigint(num));
			}
		}
	}

	private static int getInteger(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					long num2 = Convert.ToInt64(obj);
					N2DB.checkInt(num2);
					dataObjects.set(i, new object[2]
					{
						obj,
						ByteUtil.fromInt((int)num2)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getInteger(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				long num = Convert.ToInt64(dataObjects.get(i));
				N2DB.checkInt(num);
				dataObjects.set(i, ByteUtil.fromInt((int)num));
			}
		}
	}

	private static int getVarchar(int maxError, List<object> dataObjects, string charset, int precise, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					string text = dataObjects.get(i).ToString();
					if (text.getBytes(charset).Length > precise)
					{
						DmError.ThrowDmException(DmErrorDefinition.EC_STR_TRUNC_WARN);
					}
					dataObjects.set(i, new object[2]
					{
						text,
						processVarchar(charset, text)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getVarchar(List<object> dataObjects, string charset)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				dataObjects.set(i, processVarchar(charset, dataObjects.get(i)));
			}
		}
	}

	private static byte[] processVarchar(string charset, object o)
	{
		byte[] bytes = o.ToString().getBytes(charset);
		int endBlankLength = getEndBlankLength(bytes);
		int num = bytes.Length - endBlankLength;
		byte[] array = new byte[8 + num];
		array[0] = (byte)endBlankLength;
		array[1] = (byte)(endBlankLength >> 8);
		array[2] = (byte)(endBlankLength >> 16);
		array[3] = (byte)(endBlankLength >> 24);
		array[4] = (byte)num;
		array[5] = (byte)(num >> 8);
		array[6] = (byte)(num >> 16);
		array[7] = (byte)(num >> 24);
		if (num >= 0)
		{
			Array.Copy(bytes, 0, array, 8, num);
		}
		return array;
	}

	private static int getDouble(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					byte[] array = N2DB.toDouble(Convert.ToDouble(dataObjects.get(i).ToString()));
					dataObjects.set(i, new object[2]
					{
						dataObjects.get(i),
						array
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getDouble(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				byte[] item = N2DB.toDouble(Convert.ToDouble(dataObjects.get(i).ToString()));
				dataObjects.set(i, item);
			}
		}
	}

	private static int getReal(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					float num2 = Convert.ToSingle(obj);
					N2DB.checkReal(num2);
					dataObjects.set(i, new object[2]
					{
						obj,
						N2DB.toReal(num2)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static void getReal(List<object> dataObjects)
	{
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				float num = Convert.ToSingle(dataObjects.get(i));
				N2DB.checkReal(num);
				dataObjects.set(i, N2DB.toReal(num));
			}
		}
	}

	private static int getDecimal(int maxError, List<object> dataObjects, Dictionary<int, Exception> errRowMap)
	{
		int num = 0;
		if (dataObjects != null && !dataObjects.isEmpty())
		{
			for (int i = 0; i < dataObjects.size(); i++)
			{
				if (errRowMap.get(i) != null)
				{
					continue;
				}
				try
				{
					object obj = dataObjects.get(i);
					writeDecimal2(convertToDec(obj), dataObjects, i);
					dataObjects.set(i, new object[2]
					{
						obj,
						dataObjects.get(i)
					});
				}
				catch (Exception value)
				{
					num++;
					errRowMap.put(i, value);
					if (maxError > 0 && num >= maxError)
					{
						return num;
					}
				}
			}
		}
		return num;
	}

	private static decimal convertToDec(object o)
	{
		decimal value = ((o is decimal) ? ((decimal)o) : ((o is long) ? new decimal((long)o) : ((o is int) ? new decimal((int)o) : ((o is short) ? new decimal((short)o) : ((!(o is byte)) ? DmdbNumeric.valueOf(o.ToString(), 0, 0).toDecimal(compatibleOracle: false) : new decimal((byte)o))))));
		return value.stripTrailingZeros();
	}

	private static void getDecimal(List<object> dataObjects)
	{
		if (dataObjects == null || dataObjects.isEmpty())
		{
			return;
		}
		for (int i = 0; i < dataObjects.size(); i++)
		{
			object obj = dataObjects.get(i);
			if (obj is decimal)
			{
				writeDecimal2((decimal)obj, dataObjects, i);
			}
			else if (obj is long)
			{
				writeDecimal2(new decimal((long)obj), dataObjects, i);
			}
			else if (obj is int)
			{
				writeDecimal2(new decimal((int)obj), dataObjects, i);
			}
			else if (obj is short)
			{
				writeDecimal2(new decimal((short)obj), dataObjects, i);
			}
			else if (obj is byte)
			{
				writeDecimal2(new decimal((byte)obj), dataObjects, i);
			}
			else
			{
				writeDecimal2(decimal.Parse(obj.ToString()), dataObjects, i);
			}
		}
	}

	public static byte[] processDecimal(decimal value)
	{
		if (value.compareTo(0m) == 0)
		{
			return ZERO_ARR;
		}
		value = value.stripTrailingZeros();
		int num = value.precision();
		int num2 = value.scale();
		int num3 = num - num2;
		num = Math.Max(num, num3);
		num2 = Math.Max(0, num2);
		num = Math.Max(num, num2);
		short num4 = (short)((MathUtil.isEven(num3) ? (num3 - 2) : (num3 - 1)) / 2);
		if (num4 >= 0 && num > 38)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		byte[] array = new byte[28]
		{
			(byte)((value.signum() == 1) ? 193 : 62),
			(byte)num,
			(byte)num2,
			(byte)num4,
			(byte)(num4 >> 8),
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0
		};
		int num5 = 6;
		int num6 = 0;
		if (value.signum() < 0)
		{
			value = value.negate();
			num6 = 1;
		}
		string text;
		if (num4 >= 0)
		{
			text = value.toPlainString();
			if (MathUtil.isOdd(num3))
			{
				text = "0" + text;
			}
		}
		else
		{
			text = value.toPlainString().substring(2 + (-num4 - 1) * 2);
		}
		if (MathUtil.isOdd(num2))
		{
			text += "0";
		}
		array[num5++] = ((num6 == 0) ? ((byte)(193 + num4)) : ((byte)(~(193 + num4))));
		int num7 = -1;
		for (int i = 0; i < text.length() - 1; i += 2)
		{
			if (text.charAt(i) == '.')
			{
				i--;
				continue;
			}
			int num8 = text.charAt(i) - 48;
			int num9 = text.charAt(i + 1) - 48;
			if (num7 == -1 && num8 == 0 && num9 == 0)
			{
				num7 = num5;
			}
			else if (num8 != 0 || num9 != 0)
			{
				num7 = -1;
			}
			array[num5++] = ((num6 == 0) ? charToDigit[num8][num9] : ((byte)(102 - charToDigit[num8][num9])));
			if (num5 == 22)
			{
				break;
			}
		}
		if (num7 > 0)
		{
			num5 = Math.Min(num7, num5);
		}
		if (num6 == 1)
		{
			array[num5] = 102;
			array[5] = (byte)(num5 - 5);
		}
		else
		{
			num5 = Math.Min(21, num5);
			array[num5] = 0;
			array[5] = (byte)Math.Max(num5 - 6, 1);
		}
		return array;
	}

	private static void writeDecimal2(decimal value, List<object> dataObjects, int index)
	{
		dataObjects.set(index, processDecimal(value));
	}

	public static byte[] getDecimal2(decimal value)
	{
		return processDecimal(value);
	}

	private static byte[] getPrecisionScale(decimal value)
	{
		if (value.compareTo(0m) == 0)
		{
			return new byte[2];
		}
		int num = value.precision();
		int num2 = value.scale();
		if (value.compareTo(0m) > 0)
		{
			if (value.compareTo(1m) < 0)
			{
				num = value.scale();
			}
		}
		else if (value.compareTo(-1m) > 0)
		{
			num = value.scale();
		}
		return new byte[2]
		{
			(byte)num,
			(byte)num2
		};
	}

	private static byte getDecimalSign(decimal value)
	{
		if (value.compareTo(0m) == 0)
		{
			return 128;
		}
		if (value.signum() == 1)
		{
			return 193;
		}
		return 62;
	}

	private static int getDecWeight(decimal value)
	{
		if (value.compareTo(0m) == 0)
		{
			return 0;
		}
		int num = value.precision();
		int num2 = value.scale();
		int num3 = num - num2;
		int num4 = num3 % 2;
		int num5 = num3 / 2 + num4 - 1;
		if (num5 < 0)
		{
			decimal value2 = value;
			if (value.signum() < 0)
			{
				value2 = value2.negate();
			}
			num5 = -getZeroCount(value2) / 2 - 1;
		}
		return num5;
	}

	private static void checkDecLength(decimal value)
	{
		if (value.compareTo(decimal.MaxValue) > 0 || value.compareTo(decimal.MinValue) < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
	}

	private static int getZeroCount(decimal value)
	{
		string str = value.toPlainString();
		int num = 0;
		for (int i = 2; i < str.length() && str.charAt(i) == '0'; i++)
		{
			num++;
		}
		return num;
	}

	private static byte[] getDecData(int weight, decimal value)
	{
		int num = 1;
		if (value.compareTo(0m) == 0)
		{
			return ZERO_ARR;
		}
		int num2 = 0;
		if (value.signum() < 0)
		{
			value = value.negate();
			num2 = 1;
		}
		string decStr = value.toPlainString();
		int integerNum = value.precision() - value.scale();
		int fractionNum = value.scale();
		decStr = getFinalDecStr(integerNum, fractionNum, weight, decStr);
		byte[] array = new byte[22];
		array[0] = ((num2 == 0) ? ((byte)(193 + weight)) : ((byte)(~(193 + weight))));
		for (int i = 0; i < decStr.length() - 1; i += 2)
		{
			if (decStr.charAt(i) == '.')
			{
				i--;
				continue;
			}
			int num3 = decStr.charAt(i) - 48;
			int num4 = decStr.charAt(i + 1) - 48;
			array[num++] = ((num2 == 0) ? charToDigit[num3][num4] : ((byte)(102 - charToDigit[num3][num4])));
			if ((num == 21 && num2 == 1) || num == 22)
			{
				break;
			}
		}
		if (num2 == 1)
		{
			array[num] = 102;
		}
		byte[] array2 = new byte[23];
		array2[0] = (byte)(num + num2);
		Array.Copy(array, 0, array2, 1, array.Length);
		return array2;
	}

	private static string getFinalDecStr(int integerNum, int fractionNum, int weight, string decStr)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (weight >= 0)
		{
			if (integerNum % 2 != 0)
			{
				stringBuilder.append("0").append(decStr);
			}
			else
			{
				stringBuilder.append(decStr);
			}
			if (fractionNum % 2 != 0)
			{
				stringBuilder.append("0");
			}
			decStr = stringBuilder.ToString();
		}
		else if (fractionNum % 2 != 0)
		{
			stringBuilder.append(decStr).append("0");
			decStr = stringBuilder.substring(2 + (-weight - 1) * 2);
		}
		else
		{
			decStr = decStr.substring(2 + (-weight - 1) * 2);
		}
		return decStr;
	}

	private static int getEndBlankLength(byte[] data)
	{
		int num = 0;
		int num2 = data.Length - 1;
		while (num2 >= 0 && data[num2] == 32)
		{
			num++;
			num2--;
		}
		return num;
	}

	protected override void afterEncode()
	{
		access.__t02_field_04000AB9.B(0, (command != null) ? command.Statement.g() : 0);
		access.__t02_field_04000AB9.A(4, cmd);
		access.__t02_field_04000AB9.B(6, access.__t02_field_04000AB9.g() - 64);
		access.__t02_field_04000AB9.B(20, setId);
		access.__t02_field_04000AB9.B(70, access.__t02_field_04000AB9.g() - 64);
		access.__t02_field_04000AB9.B(74, access.__t02_field_04000AB9.g() - 64);
	}

	public static object[] batchEncode(b buffer, FldrStatement statement, int rows, short columns, List<ColumnData> columnDataList, TableInfo tableInfo, int maxError, object[][] arr, FldrErrorWriter fldrErrorWriter)
	{
		batchBeforeEncode(buffer);
		object[] array = batchThreadJ2DB(columnDataList, tableInfo, statement.connection, maxError);
		int num = (int)array[0];
		Dictionary<int, Exception> dictionary = (Dictionary<int, Exception>)array[1];
		if (dictionary != null && !dictionary.isEmpty())
		{
			foreach (KeyValuePair<int, Exception> item in dictionary)
			{
				fldrErrorWriter.writeLines(statement.schemaTable, item.getValue().Message, "[DATA]" + string.Join(", ", arr[item.getKey()]), StringUtil.LINE_SEPARATOR);
			}
		}
		if (maxError > 0 && num >= maxError)
		{
			return array;
		}
		rows -= dictionary.Count;
		if (rows <= 0)
		{
			return array;
		}
		batchDoEncode(buffer, rows, columns, columnDataList, dictionary);
		batchAfterEncode(buffer, statement);
		return array;
	}

	private static void batchAfterEncode(b buffer, FldrStatement statement)
	{
		buffer.B(0, 0);
		buffer.A(4, (short)55);
		buffer.B(6, buffer.g() - 64);
		buffer.B(70, buffer.g() - 64);
		buffer.B(74, buffer.g() - 64);
	}

	private static void batchBeforeEncode(b buffer)
	{
		buffer.a(0);
		buffer.A(64, true, true);
	}

	private static void batchDoEncode(b buffer, int rows, short columns, List<ColumnData> columnDataList, Dictionary<int, Exception> errRowMap)
	{
		buffer.H(rows);
		buffer.A(columns);
		buffer.A(8, true, true);
		buffer.A(compress);
		foreach (ColumnData columnData in columnDataList)
		{
			buffer.A(columnData.getSqlType());
		}
		foreach (ColumnData columnData2 in columnDataList)
		{
			if (columnData2.getSqlType() == 19 || columnData2.getSqlType() == 12)
			{
				buffer.H(0);
				buffer.A(new byte[rows]);
				continue;
			}
			ColumnData current2 = processErrCol(rows, columnData2, errRowMap);
			buffer.H(current2.getIsAllNotNull());
			if (current2.getIsAllNotNull() == 0)
			{
				buffer.A(current2.getNullArr());
			}
			List<object> data = current2.getData();
			for (int i = 0; i < data.size(); i++)
			{
				if (errRowMap.get(i) == null)
				{
					if (data.get(i) is byte[])
					{
						buffer.A((byte[])data.get(i));
					}
					else
					{
						buffer.A((byte[])((object[])data.get(i))[1]);
					}
				}
			}
		}
	}

	public static ColumnData processErrCol(int rows, ColumnData columnData, Dictionary<int, Exception> errRowMap)
	{
		if (columnData.getIsAllNotNull() == 0)
		{
			byte[] nullArr = columnData.getNullArr();
			byte[] array = new byte[rows];
			int num = 0;
			int num2 = 1;
			for (int i = 0; i < nullArr.Length; i++)
			{
				if (errRowMap.get(i) == null)
				{
					array[num++] = nullArr[i];
					num2 = ((nullArr[i] != 0) ? num2 : 0);
				}
			}
			columnData.setIsAllNotNull(num2);
			columnData.setNullArr(array);
		}
		return columnData;
	}

	protected override int doDecode()
	{
		return 0;
	}

	public static void doTaskOnEverySite(CancellationTokenSource executorCts, List<ColumnData> list, int columnSize, Callback2 callback)
	{
		if (columnSize == 0 || executorCts == null)
		{
			return;
		}
		CountdownEvent countDownLatch = new CountdownEvent(columnSize);
		FldrTask[] fldrTasks = new FldrTask[columnSize];
		for (int i = 0; i < columnSize; i++)
		{
			int colIndex = i;
			fldrTasks[colIndex] = new FldrTask(countDownLatch)
			{
				doTask = delegate
				{
					callback.run(list, colIndex);
				}
			};
		}
		for (int num = 0; num < columnSize; num++)
		{
			int colIndex2 = num;
			Task.Factory.StartNew(delegate
			{
				try
				{
					executorCts.Token.ThrowIfCancellationRequested();
					fldrTasks[colIndex2].run();
				}
				catch (Exception)
				{
					executorCts.Cancel();
					throw;
				}
			});
		}
		FldrStatement.threadCountDownHelper(countDownLatch, fldrTasks);
	}
}
