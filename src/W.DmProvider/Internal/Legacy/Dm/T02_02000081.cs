using System.Collections.Generic;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.net.buffer;
using W.Dm.util;

namespace W.Dm;

internal class FLDR_GET_TABLE_INFO : MSG<TableInfo>
{
	private string schemaName;

	private string tableName;

	private byte lockFlag;

	private bool containLob;

	private bool containDecimal;

	private bool containChar;

	public const byte NOT_EMPTY = 1;

	public const byte EMPTY = 0;

	public const byte MaxValue = 2;

	public const int INVALID_TIME_ZONE = 1000;

	private const short VERSION_DEFAULT = 0;

	public const short VERSION_INDEX = 1;

	public const short VERSION_COLUMN_FUNC = 2;

	public const short VERSION_INTERVAL_COLUMN = 3;

	public const short VERSION_LOB_FROM_MSG = 4;

	public const short VERSION_REAL_NAME = 5;

	private new short VERSION = 5;

	private Dictionary<string, int> subTableNameIdMap;

	public FLDR_GET_TABLE_INFO(B access, string schemaName, string tableName, byte lockFlag)
		: base(access, (short)53)
	{
		this.schemaName = schemaName;
		this.tableName = tableName;
		this.lockFlag = lockFlag;
	}

	public bool isContainDecimal()
	{
		return containDecimal;
	}

	protected override void afterEncode()
	{
		access.__t02_field_04000AB9.B(0, (command != null) ? command.Statement.g() : 0);
		access.__t02_field_04000AB9.A(4, cmd);
		access.__t02_field_04000AB9.B(6, access.__t02_field_04000AB9.g() - 64);
		access.__t02_field_04000AB9.A(20, VERSION);
	}

	protected override void doEncode()
	{
		byte[] array = ByteUtil.fromString(schemaName, access.a().ServerEncoding);
		if (array.Length > 128)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_SCHEMA_NAME, $"schemaName标示符长度非法，超过最大长度{128}");
		}
		byte[] array2 = ByteUtil.fromString(tableName, access.a().ServerEncoding);
		if (array2.Length > 128)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_DB_NAME, $"tableName标示符长度非法，超过最大长度{128}");
		}
		access.__t02_field_04000AB9.a(array);
		access.__t02_field_04000AB9.a(array2);
		access.__t02_field_04000AB9.A(lockFlag);
	}

	protected override TableInfo doDecode()
	{
		access.__t02_field_04000AB9.G(20);
		short num = access.__t02_field_04000AB9.C();
		short num2 = access.__t02_field_04000AB9.C();
		short num3 = access.__t02_field_04000AB9.C();
		access.__t02_field_04000AB9.A(2, false, true);
		byte b2 = access.__t02_field_04000AB9.__t02_method_06000ABD();
		int num4 = access.__t02_field_04000AB9.C();
		access.__t02_field_04000AB9.A(33, false, true);
		List<ColumnInfo> list = new List<ColumnInfo>(num);
		Dictionary<string, short> dictionary = new Dictionary<string, short>(num);
		for (int i = 0; i < num; i++)
		{
			ColumnInfo columnInfo = new ColumnInfo();
			int num5 = access.__t02_field_04000AB9.C();
			columnInfo.setColumnName(access.__t02_field_04000AB9.A(num5, access.a().ServerEncoding));
			columnInfo.setColumnId(access.__t02_field_04000AB9.C());
			columnInfo.setColumnType(access.__t02_field_04000AB9.C());
			if (!containLob && (19 == columnInfo.getColumnType() || 12 == columnInfo.getColumnType()))
			{
				containLob = true;
			}
			if (!containDecimal && 9 == columnInfo.getColumnType())
			{
				containDecimal = true;
			}
			else if (!containChar && columnInfo.getColumnType() <= 2)
			{
				containChar = true;
			}
			columnInfo.setColumnLen(access.__t02_field_04000AB9.C());
			columnInfo.setColumnPrecise(access.__t02_field_04000AB9.C());
			dictionary.Add(columnInfo.getColumnName(), columnInfo.getColumnId());
			list.Add(columnInfo);
		}
		HorizontalTableInfo hTableHead = null;
		Dictionary<int, HorizontalTableInfo> dictionary2 = new Dictionary<int, HorizontalTableInfo>();
		if (num2 > 0)
		{
			hTableHead = getHorizontalTableInfo(num2, list, dictionary2);
		}
		if (num3 > 0)
		{
			getVerticalTableInfo();
		}
		TableInfo tableInfo = new TableInfo(list, dictionary, hTableHead, containLob);
		tableInfo.setSchemaName(schemaName);
		tableInfo.setTableName(tableName);
		tableInfo.hTableNodeMap = dictionary2;
		tableInfo.autoIncrementColId = access.__t02_field_04000AB9.C();
		tableInfo.containDecimal = containDecimal;
		tableInfo.containChar = containChar;
		tableInfo.setDBTimeZone(access.a().DbTimeZone);
		tableInfo.setLocalTimeZone((short)DmOptionHelper.localtimezone());
		if (tableInfo.autoIncrementColId != -1)
		{
			tableInfo.seed = access.__t02_field_04000AB9.e();
			tableInfo.currentValue = access.__t02_field_04000AB9.e();
			tableInfo.increment = access.__t02_field_04000AB9.e();
		}
		if (num4 >= 3)
		{
			short num6 = access.__t02_field_04000AB9.C();
			if (num6 > 0)
			{
				tableInfo.intervalColumnInfo = getIntervalColumnInfo(num6);
			}
		}
		if (num4 >= 5)
		{
			tableInfo.setSchemaName(access.__t02_field_04000AB9.B(access.a().ServerEncoding));
			tableInfo.setTableName(access.__t02_field_04000AB9.B(access.a().ServerEncoding));
		}
		if (b2 == 1)
		{
			tableInfo.dpcFlag = true;
		}
		tableInfo.subTableNameIdMap = subTableNameIdMap;
		TableInfo.msgVersion = num4;
		return tableInfo;
	}

	private IntervalColumnInfo getIntervalColumnInfo(short sqlType)
	{
		IntervalColumnInfo intervalColumnInfo = new IntervalColumnInfo();
		intervalColumnInfo.sqlType = sqlType;
		switch (intervalColumnInfo.sqlType)
		{
		case 20:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(12);
			intervalColumnInfo.intervalValue = new DmIntervalYM(intervalColumnInfo.intervalValueBytes);
			break;
		case 21:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(24);
			intervalColumnInfo.intervalValue = new DmIntervalDT(intervalColumnInfo.intervalValueBytes);
			break;
		case 7:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(4);
			intervalColumnInfo.intervalValue = ByteUtil.toInt(intervalColumnInfo.intervalValueBytes);
			break;
		case 8:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(8);
			intervalColumnInfo.intervalValue = ByteUtil.toLong(intervalColumnInfo.intervalValueBytes);
			break;
		case 10:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(4);
			intervalColumnInfo.intervalValue = ByteUtil.toFloat(intervalColumnInfo.intervalValueBytes);
			break;
		case 11:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.F(8);
			intervalColumnInfo.intervalValue = ByteUtil.toDouble(intervalColumnInfo.intervalValueBytes);
			break;
		case 9:
			intervalColumnInfo.intervalValueBytes = access.__t02_field_04000AB9.A(access.__t02_field_04000AB9.A(false), 28);
			intervalColumnInfo.intervalValue = Bdta.readDecimal(access);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_TYPE);
			break;
		}
		intervalColumnInfo.firstValue = access.__t02_field_04000AB9.C();
		return intervalColumnInfo;
	}

	private HorizontalTableInfo getHorizontalTableInfo(short hTableNum, List<ColumnInfo> list, Dictionary<int, HorizontalTableInfo> nodeMap)
	{
		Dictionary<int, HorizontalTableInfo> dictionary = new Dictionary<int, HorizontalTableInfo>(hTableNum);
		subTableNameIdMap = new Dictionary<string, int>();
		int key = 0;
		for (int i = 0; i < hTableNum; i++)
		{
			HorizontalTableInfo horizontalTableInfo = new HorizontalTableInfo();
			int num = access.__t02_field_04000AB9.C();
			string text = access.__t02_field_04000AB9.A(num, access.a().ServerEncoding);
			horizontalTableInfo.setTableName(text);
			int num2 = access.__t02_field_04000AB9.d();
			if (i == 0)
			{
				key = num2;
			}
			horizontalTableInfo.setTableId(num2);
			int baseTableId = access.__t02_field_04000AB9.d();
			horizontalTableInfo.setBaseTableId(baseTableId);
			int tableType = access.__t02_field_04000AB9.d();
			horizontalTableInfo.setTableType(tableType);
			short baseTableType = access.__t02_field_04000AB9.C();
			horizontalTableInfo.setBaseTableType(baseTableType);
			int num3 = access.__t02_field_04000AB9.C();
			List<int> list2 = new List<int>(num3);
			for (int j = 0; j < num3; j++)
			{
				list2.Add(access.__t02_field_04000AB9.d());
			}
			horizontalTableInfo.setSubTableIds(list2);
			int num4 = access.__t02_field_04000AB9.C();
			short[] array = new short[num4];
			short[] array2 = new short[num4];
			for (int k = 0; k < num4; k++)
			{
				array2[k] = list[array[k] = access.__t02_field_04000AB9.C()].getColumnType();
			}
			horizontalTableInfo.setPartitionColIds(array);
			horizontalTableInfo.setSqlType(array2);
			if (access.__t02_field_04000AB9.d() > 0)
			{
				readBdtaData(num4, horizontalTableInfo);
			}
			dictionary.Add(num2, horizontalTableInfo);
			subTableNameIdMap.Add(horizontalTableInfo.getTableName(), horizontalTableInfo.getTableId());
			nodeMap.Add(num2, horizontalTableInfo);
		}
		foreach (KeyValuePair<int, HorizontalTableInfo> item in dictionary)
		{
			HorizontalTableInfo value = item.Value;
			List<int> subTableIds = value.getSubTableIds();
			if (subTableIds == null || subTableIds.Count <= 0)
			{
				continue;
			}
			List<HorizontalTableInfo> list3 = new List<HorizontalTableInfo>(subTableIds.Count);
			foreach (int item2 in subTableIds)
			{
				list3.Add(dictionary[item2]);
			}
			value.setChildren(list3);
			value.setParent(dictionary.get(value.getBaseTableId()));
		}
		return dictionary[key];
	}

	private void readBdtaData(int colNum, HorizontalTableInfo hTableInfo)
	{
		List<short> list = new List<short>(colNum);
		int num = access.__t02_field_04000AB9.d();
		short num2 = access.__t02_field_04000AB9.C();
		access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.__t02_method_06000ABD();
		for (int i = 0; i < num2; i++)
		{
			list.Add(access.__t02_field_04000AB9.C());
		}
		List<object[]> list2 = new List<object[]>();
		hTableInfo.setBoundaries(list2);
		byte[] nullArr = new byte[num];
		for (int j = 0; j < list.Count; j++)
		{
			short num3 = list[j];
			nullArr = nullArrPrepare(nullArr);
			if (j == list.Count - 2)
			{
				hTableInfo.setSubTableIds(getIncludeOrSubTableId(nullArr));
				continue;
			}
			if (j == list.Count - 1)
			{
				hTableInfo.setIncludeFlag(getIncludeOrSubTableId(nullArr));
				break;
			}
			switch (num3)
			{
			case 5:
			case 6:
			case 7:
				list2.Add(integerProcess(num3, nullArr, hTableInfo));
				break;
			case 10:
				list2.Add(realProcess(nullArr, hTableInfo));
				break;
			case 11:
				list2.Add(doubleProcess(nullArr, hTableInfo));
				break;
			case 8:
				list2.Add(longProcess(nullArr, hTableInfo));
				break;
			case 2:
			case 12:
			case 17:
			case 18:
			case 19:
				list2.Add(stringProcess(nullArr, hTableInfo));
				break;
			case 0:
			case 1:
				list2.Add(charProcess(nullArr, hTableInfo));
				break;
			case 9:
				list2.Add(decimalProcess(nullArr, hTableInfo));
				break;
			case 14:
			case 15:
			case 16:
			case 22:
			case 23:
			case 26:
			case 27:
				list2.Add(dateProcess(nullArr, num3, hTableInfo));
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_DB_OBJECT);
				break;
			}
		}
	}

	private byte[] nullArrPrepare(byte[] nullArr)
	{
		Arrays.Fill(nullArr, (byte)1);
		if (access.__t02_field_04000AB9.d() == 0)
		{
			nullArr = access.__t02_field_04000AB9.F(nullArr.Length);
		}
		return nullArr;
	}

	private List<int> getIncludeOrSubTableId(byte[] nullArr)
	{
		List<int> list = new List<int>(nullArr.Length);
		for (int i = 0; i < nullArr.Length; i++)
		{
			list.Add(access.__t02_field_04000AB9.d());
		}
		return list;
	}

	private object[] dateProcess(byte[] nullArr, int sqlType, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		int[] array2 = new int[8];
		for (int i = 0; i < nullArr.Length; i++)
		{
			switch (nullArr[i])
			{
			case 0:
				access.__t02_field_04000AB9.A(12, false, true);
				array[i] = long.MinValue;
				break;
			case 1:
			{
				array2[0] = access.__t02_field_04000AB9.C();
				array2[1] = access.__t02_field_04000AB9.__t02_method_06000ABD();
				array2[2] = access.__t02_field_04000AB9.__t02_method_06000ABD();
				array2[3] = access.__t02_field_04000AB9.__t02_method_06000ABD();
				array2[4] = access.__t02_field_04000AB9.__t02_method_06000ABD();
				array2[5] = access.__t02_field_04000AB9.__t02_method_06000ABD();
				array2[6] = (access.__t02_field_04000AB9.__t02_method_06000ABD() & 0xFF) + ((access.__t02_field_04000AB9.__t02_method_06000ABD() & 0xFF) << 8) + ((access.__t02_field_04000AB9.__t02_method_06000ABD() & 0xFF) << 16);
				short num = (short)(array2[7] = access.__t02_field_04000AB9.C());
				short dbTimeZone = access.a().DbTimeZone;
				if (num != 1000 && dbTimeZone != num && (23 == sqlType || 27 == sqlType || 22 == sqlType))
				{
					array2 = DmDateTime.transformTZ(array2, num, dbTimeZone);
				}
				if (15 == sqlType || 22 == sqlType)
				{
					array[i] = timeHelper(array2[3], array2[4], array2[5], array2[6]);
				}
				else
				{
					array[i] = dateHelper(array2[0], array2[1], array2[2], array2[3], array2[4], array2[5], array2[6]);
				}
				break;
			}
			case 2:
				access.__t02_field_04000AB9.A(12, false, true);
				array[i] = long.MaxValue;
				if (TableInfo.isListPartition(hTableInfo.getTableType()))
				{
					hTableInfo.setDefaultPartition(i);
				}
				break;
			}
		}
		return array;
	}

	public static long timeHelper(int hour, int min, int sec, int ms)
	{
		return (long)hour * 3600000L + (long)min * 60000L + (long)sec * 1000L + ms;
	}

	public static long dateHelper(int year, int month, int day, int hour, int min, int sec, int ms)
	{
		return year * 31104000000L + month * 2592000000u + (long)day * 86400000L + timeHelper(hour, min, sec, ms);
	}

	private object[] longProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			switch (nullArr[i])
			{
			case 0:
				array[i] = long.MinValue;
				break;
			case 1:
				array[i] = access.__t02_field_04000AB9.e();
				break;
			case 2:
				access.__t02_field_04000AB9.A(8, false, true);
				array[i] = long.MaxValue;
				if (TableInfo.isListPartition(hTableInfo.getTableType()))
				{
					hTableInfo.setDefaultPartition(i);
				}
				break;
			}
		}
		return array;
	}

	private object[] doubleProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			switch (nullArr[i])
			{
			case 0:
				array[i] = double.MinValue;
				break;
			case 1:
				array[i] = access.__t02_field_04000AB9.f();
				break;
			case 2:
				access.__t02_field_04000AB9.A(8, false, true);
				array[i] = double.MaxValue;
				if (TableInfo.isListPartition(hTableInfo.getTableType()))
				{
					hTableInfo.setDefaultPartition(i);
				}
				break;
			}
		}
		return array;
	}

	private object[] realProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			switch (nullArr[i])
			{
			case 0:
				array[i] = float.MinValue;
				break;
			case 1:
				array[i] = access.__t02_field_04000AB9.F();
				break;
			case 2:
				access.__t02_field_04000AB9.A(4, false, true);
				array[i] = float.MaxValue;
				if (TableInfo.isListPartition(hTableInfo.getTableType()))
				{
					hTableInfo.setDefaultPartition(i);
				}
				break;
			}
		}
		return array;
	}

	private object[] integerProcess(int sqlType, byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		int num = 0;
		int num2 = 0;
		switch (sqlType)
		{
		case 7:
			num = int.MaxValue;
			num2 = int.MinValue;
			break;
		case 6:
			num = 32767;
			num2 = -32768;
			break;
		case 5:
			num = 255;
			num2 = 0;
			break;
		}
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			switch (nullArr[i])
			{
			case 0:
				array[i] = num2;
				break;
			case 1:
				array[i] = access.__t02_field_04000AB9.d();
				break;
			case 2:
				access.__t02_field_04000AB9.A(4, false, true);
				array[i] = num;
				if (TableInfo.isListPartition(hTableInfo.getTableType()))
				{
					hTableInfo.setDefaultPartition(i);
				}
				break;
			}
		}
		return array;
	}

	private object[] charProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			if (2 == nullArr[i] && TableInfo.isListPartition(hTableInfo.getTableType()))
			{
				hTableInfo.setDefaultPartition(i);
			}
			array[i] = StringUtil.rightTrim(Bdta.ReadString(access));
		}
		return array;
	}

	private object[] stringProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			if (2 == nullArr[i] && TableInfo.isListPartition(hTableInfo.getTableType()))
			{
				hTableInfo.setDefaultPartition(i);
			}
			array[i] = Bdta.ReadString(access);
		}
		return array;
	}

	private object[] decimalProcess(byte[] nullArr, HorizontalTableInfo hTableInfo)
	{
		object[] array = new object[nullArr.Length];
		for (int i = 0; i < nullArr.Length; i++)
		{
			if (2 == nullArr[i] && TableInfo.isListPartition(hTableInfo.getTableType()))
			{
				hTableInfo.setDefaultPartition(i);
			}
			array[i] = Bdta.readDecimal(access);
		}
		return array;
	}

	private void getVerticalTableInfo()
	{
		int num = access.__t02_field_04000AB9.C();
		access.__t02_field_04000AB9.A(num, access.a().ServerEncoding);
		int num2 = access.__t02_field_04000AB9.C();
		List<short> list = new List<short>(num2);
		for (int i = 0; i < num2; i++)
		{
			list.Add(access.__t02_field_04000AB9.C());
		}
		access.__t02_field_04000AB9.C();
		int num3 = access.__t02_field_04000AB9.C();
		List<short> list2 = new List<short>(num3);
		for (int j = 0; j < num3; j++)
		{
			list2.Add(access.__t02_field_04000AB9.C());
		}
		int num4 = access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.F(num4);
		access.__t02_field_04000AB9.d();
	}
}
