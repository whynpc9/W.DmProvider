using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Dm.util;

namespace Dm;

internal class TableInfo
{
	private string schemaName;

	private string tableName;

	public bool dpcFlag;

	private List<ColumnInfo> columnInfos;

	private Dictionary<string, short> nameIdMap;

	private HorizontalTableInfo hTableHead;

	public Dictionary<string, int> subTableNameIdMap;

	public Dictionary<string, List<object[]>> hTableNameData = new Dictionary<string, List<object[]>>();

	public Dictionary<int, HorizontalTableInfo> hTableNodeMap = new Dictionary<int, HorizontalTableInfo>();

	public int autoIncrementColId;

	public long seed;

	public long currentValue;

	public long increment;

	public bool containLob;

	public bool containDecimal;

	public bool containChar;

	private int dBTimeZone = 1000;

	private int localTimeZone = 480;

	private DmConnection conn;

	public static int msgVersion;

	public List<FldrIndexInfo> indexInfos = new List<FldrIndexInfo>();

	public IntervalColumnInfo intervalColumnInfo;

	public byte lanMode;

	public const int HUGE_LIST_MAIN_TABLE = 38;

	public const int NORMAL_LIST_MAIN_TABLE = 11;

	public const int HUGE_RANGE_MAIN_TABLE = 34;

	public const int NORMAL_RANGE_MAIN_TABLE = 6;

	public const int HUGE_HASH_MAIN_TABLE = 36;

	public const int NORMAL_HASH_MAIN_TABLE = 8;

	public object lockForGetHTabPartition = new object();

	public object lockForGetAsyncHTabPartition = new object();

	public TableInfo()
	{
	}

	public TableInfo(List<ColumnInfo> columnInfos, Dictionary<string, short> nameIdMap, HorizontalTableInfo hTableHead, bool containLob)
	{
		this.columnInfos = columnInfos;
		this.nameIdMap = nameIdMap;
		this.hTableHead = hTableHead;
		this.containLob = containLob;
	}

	public List<ColumnInfo> getColumnInfos()
	{
		return columnInfos;
	}

	public void setColumnInfos(List<ColumnInfo> columnInfos)
	{
		this.columnInfos = columnInfos;
	}

	public Dictionary<string, short> getNameIdMap()
	{
		return nameIdMap;
	}

	public void setNameIdMap(Dictionary<string, short> nameIdMap)
	{
		this.nameIdMap = nameIdMap;
	}

	public HorizontalTableInfo gethTableHead()
	{
		return hTableHead;
	}

	public void sethTableHead(HorizontalTableInfo hTableHead)
	{
		this.hTableHead = hTableHead;
	}

	public short getColumnIdByName(string columnName)
	{
		if (columnName == null || columnName.Equals(""))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_NAME);
		}
		columnName = columnName.ToUpper();
		if (nameIdMap == null || nameIdMap.Count == 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_NAME);
		}
		return nameIdMap.get(columnName);
	}

	public int getDBTimeZone()
	{
		return dBTimeZone;
	}

	public void setDBTimeZone(int timeZone)
	{
		dBTimeZone = timeZone;
	}

	public int getLocalTimeZone()
	{
		return localTimeZone;
	}

	public void setLocalTimeZone(int localTimeZone)
	{
		this.localTimeZone = localTimeZone;
	}

	public DmConnection getConn()
	{
		return conn;
	}

	public void setConn(DmConnection conn)
	{
		this.conn = conn;
	}

	public string getSchemaName()
	{
		return schemaName;
	}

	public void setSchemaName(string schemaName)
	{
		this.schemaName = schemaName;
	}

	public string getTableName()
	{
		return tableName;
	}

	public void setTableName(string tableName)
	{
		this.tableName = tableName;
	}

	public Dictionary<string, List<object[]>> getHTabPartition(object[] singleRow, FldrClusterInfo clusterInfo, CopyOnWriteArrayList<DmConnection> connections, ConcurrentDictionary<int, Fldr> fldrsMap, Dictionary<int, int[]> columnIdScale)
	{
		if (!containDecimal)
		{
			_ = containChar;
		}
		if (hTableHead == null)
		{
			return hTableNameData;
		}
		if (intervalColumnInfo != null)
		{
			lock (lockForGetHTabPartition)
			{
				int num = hTabHelper(hTableHead, hTableNameData, singleRow, columnIdScale);
				if (num >= 0)
				{
					addNewConnection(num, clusterInfo, connections, fldrsMap);
				}
			}
		}
		else
		{
			hTabHelper(hTableHead, hTableNameData, singleRow, columnIdScale);
		}
		return hTableNameData;
	}

	public Dictionary<string, List<object[]>> getAsyncHTabPartition(object[][] rows, Dictionary<int, int[]> columnIdScale, CopyOnWriteArrayList<DmConnection> connections, FldrClusterInfo clusterInfo, ConcurrentDictionary<int, Fldr> fldrsMap)
	{
		HorizontalTableInfo horizontalTableInfo = hTableHead;
		Dictionary<string, List<object[]>> dictionary = new Dictionary<string, List<object[]>>();
		try
		{
			if (horizontalTableInfo == null)
			{
				List<object[]> list = new List<object[]>(rows.Length);
				object[][] array = rows;
				foreach (object[] item in array)
				{
					list.Add(item);
				}
				dictionary.Add(tableName, list);
				return dictionary;
			}
			if (intervalColumnInfo != null)
			{
				lock (lockForGetAsyncHTabPartition)
				{
					object[][] array = rows;
					foreach (object[] singleRow in array)
					{
						int raftId = hTabHelper(horizontalTableInfo, dictionary, singleRow, columnIdScale);
						addNewConnection(raftId, clusterInfo, connections, fldrsMap);
					}
				}
			}
			else
			{
				object[][] array = rows;
				foreach (object[] singleRow2 in array)
				{
					hTabHelper(horizontalTableInfo, dictionary, singleRow2, columnIdScale);
				}
			}
			return dictionary;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.FLDR_PARTITION_ERROR);
			return dictionary;
		}
	}

	private void addNewConnection(int raftId, FldrClusterInfo clusterInfo, CopyOnWriteArrayList<DmConnection> connections, ConcurrentDictionary<int, Fldr> fldrsMap)
	{
		if (!dpcFlag || clusterInfo.tabIdToBpIdMap.ContainsValue(raftId))
		{
			return;
		}
		FldrClusterInfo mppInfo = clusterInfo.primaryFldr.getMppInfo(schemaName, tableName);
		clusterInfo.ipInfoList = mppInfo.ipInfoList;
		clusterInfo.tabIdToBpIdMap = mppInfo.tabIdToBpIdMap;
		foreach (object[] ipInfo in mppInfo.ipInfoList)
		{
			if (int.Parse(ipInfo[0].ToString()) == raftId)
			{
				DmConnection dmConnection = connections[0].Clone();
				dmConnection.MppType = DmMppType.LOGIN_MPP_LOCAL;
				dmConnection.ConnProperty.Host = Convert.ToString(ipInfo[1]);
				dmConnection.ConnProperty.Port = Convert.ToInt32(ipInfo[2]);
				dmConnection.ConnProperty.EPGroup = null;
				dmConnection.Open();
				connections.Add(dmConnection);
				fldrsMap.put((int)ipInfo[0], dmConnection.getFldrInstance());
			}
		}
	}

	private int hTabHelper(HorizontalTableInfo head, Dictionary<string, List<object[]>> Dictionary, object[] singleRow, Dictionary<int, int[]> columnIdScale)
	{
		int result = -1;
		if (head.getParent() == null && intervalColumnInfo != null)
		{
			head = processIntervalColumn(head, singleRow[head.getPartitionColIds()[0]], columnIdScale);
			result = head.getRaftId();
		}
		while (head.getChildren() != null && head.getChildren().Count != 0)
		{
			short[] partitionColIds = head.getPartitionColIds();
			short[] sqlType = head.getSqlType();
			int val = -1;
			int num = int.MinValue;
			int hashSize = 0;
			if (isHashPartition(head.getTableType()))
			{
				hashSize = FldrHashCode.tableSizeFor(head.getSubTableIds().Count);
			}
			for (int i = 0; i < sqlType.Length; i++)
			{
				short num2 = sqlType[i];
				if (isHashPartition(head.getTableType()))
				{
					long num3 = FldrHashCode.hc_get_fold_fun(num2, hashSize, singleRow[partitionColIds[i]], conn);
					val = FldrHashCode.compareNumHash(hashSize, head.getSubTableIds().Count, num3);
				}
				else
				{
					if (!isListPartition(head.getTableType()) && !isRangePartition(head.getTableType()))
					{
						throw new Exception("unknown type of sub_partition");
					}
					switch (num2)
					{
					case 5:
					case 6:
					case 7:
					case 8:
						val = compareNum(head, i, singleRow[partitionColIds[i]]);
						break;
					case 10:
					{
						object value = ((singleRow[partitionColIds[i]] == null) ? null : ((object)float.Parse(singleRow[partitionColIds[i]].ToString())));
						val = compareNum(head, i, value);
						break;
					}
					case 11:
					{
						object value = ((singleRow[partitionColIds[i]] == null) ? null : ((object)double.Parse(singleRow[partitionColIds[i]].ToString())));
						val = compareNum(head, i, value);
						break;
					}
					case 1:
					case 2:
					case 12:
					case 17:
					case 18:
					case 19:
						val = compareVarchar(head, i, singleRow[partitionColIds[i]]);
						break;
					case 0:
						val = compareChar(head, i, singleRow[partitionColIds[i]]);
						break;
					case 9:
						val = compareDecimal(head, i, partitionColIds[i], singleRow[partitionColIds[i]], columnIdScale);
						break;
					case 14:
					case 15:
					case 16:
					case 22:
					case 23:
					case 26:
					case 27:
						val = compareDate(intervalFlag: false, head, i, singleRow[partitionColIds[i]], num2);
						break;
					default:
						DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SQL_TYPE, $"该类型{num2}不支持分区比较");
						break;
					}
				}
				num = Math.Max(val, num);
			}
			if (num >= 0)
			{
				head = head.getChildren().get(num);
			}
		}
		List<object[]> list = Dictionary.get(head.getTableName());
		if (list == null)
		{
			list = new List<object[]>();
			list.Add(singleRow);
			Dictionary.Add(head.getTableName(), list);
		}
		else
		{
			list.Add(singleRow);
		}
		return result;
	}

	private HorizontalTableInfo processIntervalColumn(HorizontalTableInfo head, object value, Dictionary<int, int[]> columnIdScale)
	{
		if (value == null)
		{
			if (isListPartition(head.getTableType()))
			{
				return head.getChildren().get(head.getDefaultPartition());
			}
			if (isRangePartition(head.getTableType()))
			{
				return head.getChildren().get(0);
			}
			throw new Exception("not list or range partition table");
		}
		switch (intervalColumnInfo.sqlType)
		{
		case 20:
		case 21:
			return compareIntervalTime(head, 0, value);
		case 7:
			return compareIntervalInt(head, 0, value);
		case 8:
			return compareIntervalLong(head, 0, value);
		case 11:
			return compareIntervalDouble(head, 0, value);
		case 10:
			return compareIntervalFloat(head, 0, value);
		case 9:
			return compareIntervalDec(head, 0, value, columnIdScale);
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_TYPE);
			return null;
		}
	}

	public void clearData()
	{
		hTableNameData = new Dictionary<string, List<object[]>>();
	}

	private HorizontalTableInfo compareIntervalTime(HorizontalTableInfo node, int index, object value)
	{
		if (value == null)
		{
			return node.getChildren().get(index);
		}
		object[] array = node.getBoundaries().get(index);
		long[] array2 = new long[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = (long)array[i];
		}
		long compareVal;
		if (node.getSqlType()[index] == 15)
		{
			compareVal = ((!(value is string)) ? timeHelper((DateTime)value) : timeHelper(DateTime.Parse(value.ToString())));
		}
		else
		{
			DateTime dateTime = ((!(value is DateTime)) ? DateTime.Parse(value.ToString()) : ((DateTime)value));
			int millisecond = dateTime.Millisecond;
			compareVal = FLDR_GET_TABLE_INFO.dateHelper(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, millisecond);
		}
		List<int> includeFlag = node.getIncludeFlag();
		long intervalTime;
		if (21 == intervalColumnInfo.sqlType)
		{
			DmIntervalDT dmIntervalDT = (DmIntervalDT)intervalColumnInfo.intervalValue;
			intervalTime = FLDR_GET_TABLE_INFO.dateHelper(0, 0, dmIntervalDT.days, dmIntervalDT.hours, dmIntervalDT.minutes, dmIntervalDT.seconds, dmIntervalDT.fraction);
		}
		else
		{
			DmIntervalYM dmIntervalYM = (DmIntervalYM)intervalColumnInfo.intervalValue;
			intervalTime = FLDR_GET_TABLE_INFO.dateHelper(dmIntervalYM.years, dmIntervalYM.months, 0, 0, 0, 0, 0);
		}
		compareVal = findNewBoundaryTime(includeFlag.get(index), compareVal, array2[0], intervalTime);
		int num = -1;
		try
		{
			num = Array.BinarySearch(array, compareVal);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		if (num >= 0)
		{
			return node.getChildren().get(num);
		}
		num = -1 - num;
		return conn.m_ConnInst.GetCsi().A(index, num, node.getTableId(), lanMode, value, compareVal, this);
	}

	private long findNewBoundaryTime(int includeFlag, long compareVal, long defualtVal, long intervalTime)
	{
		while (compareVal > defualtVal)
		{
			defualtVal += intervalTime;
		}
		if (defualtVal == compareVal && includeFlag == 0)
		{
			defualtVal += intervalTime;
		}
		return defualtVal;
	}

	private HorizontalTableInfo compareIntervalDec(HorizontalTableInfo node, int index, object value, Dictionary<int, int[]> columnIdScale)
	{
		List<int> includeFlag = node.getIncludeFlag();
		decimal num = (decimal)intervalColumnInfo.intervalValue;
		decimal num2 = ((value is decimal) ? ((decimal)value) : decimal.Parse(value.ToString()));
		if (columnIdScale != null && columnIdScale.Count != 0)
		{
			int[] array = columnIdScale.get(index);
			if (array != null)
			{
				num2 = FldrStatement.roundHalfUp(num2, array[0], array[1]);
			}
		}
		object[] array2 = node.getBoundaries().get(index);
		decimal num3 = (decimal)array2[index];
		if (num2.compareTo(num3) < 0)
		{
			return node.getChildren().get(index);
		}
		if (num2.compareTo(num3) == 0 && includeFlag.get(index) == 1)
		{
			return node.getChildren().get(index);
		}
		decimal num4 = num3;
		while (num4.compareTo(num2) < 0)
		{
			num4 = num4.add(num);
		}
		if (num4.compareTo(num2) == 0 && includeFlag.get(index) == 0)
		{
			num4 = num4.add(num);
		}
		int num5 = Array.BinarySearch(array2, num4);
		if (num5 >= 0)
		{
			return node.getChildren().get(num5);
		}
		num5 = -1 - num5;
		if (num5 < array2.Length)
		{
			decimal num6 = (decimal)array2[num5];
			if (num2.compareTo(num6) < 0)
			{
				if (num6.subtract(num2).compareTo(num) < 0)
				{
					return node.getChildren().get(num5);
				}
			}
			else if (num2.compareTo(num6) == 0)
			{
				num4 = ((includeFlag.get(index) == 1) ? num4 : num4.add(num));
			}
		}
		return findIntervalNode(node, index, num4, value);
	}

	private HorizontalTableInfo compareIntervalDouble(HorizontalTableInfo node, int index, object value)
	{
		List<int> includeFlag = node.getIncludeFlag();
		double num = Convert.ToDouble(intervalColumnInfo.intervalValue);
		double num2 = Convert.ToDouble(value);
		object[] array = node.getBoundaries().get(index);
		double num3 = Convert.ToDouble(array[index]);
		if (num2 < num3)
		{
			return node.getChildren().get(index);
		}
		if (num2 == num3 && includeFlag.get(index) == 1)
		{
			return node.getChildren().get(index);
		}
		double num4;
		for (num4 = num3; num4 < num2; num4 += num)
		{
		}
		if (num4 == num2 && includeFlag.get(index) == 0)
		{
			num4 += num;
		}
		int num5 = Array.BinarySearch(array, num4);
		if (num5 >= 0)
		{
			return node.getChildren().get(num5);
		}
		num5 = -1 - num5;
		if (num5 < array.Length)
		{
			double num6 = Convert.ToDouble(array[num5]);
			if (!(num2 < num6))
			{
				num4 = ((num2 != num6) ? (num4 + num) : ((includeFlag.get(index) == 1) ? num4 : (num4 + num)));
			}
			else if (num6 - num2 < num)
			{
				return node.getChildren().get(num5);
			}
		}
		return findIntervalNode(node, index, num4, value);
	}

	private HorizontalTableInfo compareIntervalFloat(HorizontalTableInfo node, int index, object value)
	{
		List<int> includeFlag = node.getIncludeFlag();
		float num = Convert.ToSingle(intervalColumnInfo.intervalValue);
		float num2 = Convert.ToSingle(value);
		object[] array = node.getBoundaries().get(index);
		float num3 = Convert.ToSingle(array[index]);
		if (num2 < num3)
		{
			return node.getChildren().get(index);
		}
		if (num2 == num3 && includeFlag.get(index) == 1)
		{
			return node.getChildren().get(index);
		}
		float num4;
		for (num4 = num3; num4 < num2; num4 += num)
		{
		}
		if (num4 == num2 && includeFlag.get(index) == 0)
		{
			num4 += num;
		}
		int num5 = Array.BinarySearch(array, num4);
		if (num5 >= 0)
		{
			return node.getChildren().get(num5);
		}
		num5 = -1 - num5;
		if (num5 < array.Length)
		{
			float num6 = Convert.ToSingle(array[num5]);
			if (!(num2 < num6))
			{
				num4 = ((num2 != num6) ? (num4 + num) : ((includeFlag.get(index) == 1) ? num4 : (num4 + num)));
			}
			else if ((double)(num6 - num2) < (double)num)
			{
				return node.getChildren().get(num5);
			}
		}
		return findIntervalNode(node, index, num4, value);
	}

	private HorizontalTableInfo compareIntervalLong(HorizontalTableInfo node, int index, object value)
	{
		List<int> includeFlag = node.getIncludeFlag();
		long num = Convert.ToInt64(intervalColumnInfo.intervalValue);
		long num2 = Convert.ToInt64(value);
		object[] array = node.getBoundaries().get(index);
		long num3 = Convert.ToInt64(array[index]);
		if (num2 < num3)
		{
			return node.getChildren().get(index);
		}
		if (num2 == num3 && includeFlag.get(index) == 1)
		{
			return node.getChildren().get(index);
		}
		long num4;
		for (num4 = num3; num4 < num2; num4 += num)
		{
		}
		if (num4 == num2 && includeFlag.get(index) == 0)
		{
			num4 += num;
		}
		int num5 = Array.BinarySearch(array, num4);
		if (num5 >= 0)
		{
			return node.getChildren().get(num5);
		}
		return findIntervalNode(node, index, num4, value);
	}

	private HorizontalTableInfo compareIntervalInt(HorizontalTableInfo node, int index, object value)
	{
		List<int> includeFlag = node.getIncludeFlag();
		int num = Convert.ToInt32(intervalColumnInfo.intervalValue);
		int num2 = Convert.ToInt32(value.ToString());
		object[] array = node.getBoundaries().get(index);
		int num3 = Convert.ToInt32(array[index]);
		if (num2 < num3)
		{
			return node.getChildren().get(index);
		}
		if (num2 == num3 && includeFlag.get(index) == 1)
		{
			return node.getChildren().get(index);
		}
		int i;
		for (i = num3; i < num2; i += num)
		{
		}
		if (i == num2 && includeFlag.get(index) == 0)
		{
			i += num;
		}
		int num4 = Array.BinarySearch(array, i);
		if (num4 >= 0)
		{
			return node.getChildren().get(num4);
		}
		return findIntervalNode(node, index, i, value);
	}

	private HorizontalTableInfo findIntervalNode(HorizontalTableInfo node, int index, object newBoundary, object value)
	{
		object[] array = node.getBoundaries().get(index);
		int num = -1;
		try
		{
			num = Array.BinarySearch(array, newBoundary);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		if (num < 0)
		{
			num = -1 - num;
			return conn.m_ConnInst.GetCsi().A(index, num, node.getTableId(), lanMode, value, newBoundary, this);
		}
		return node.getChildren().get(num);
	}

	private int compareNum(HorizontalTableInfo node, int index, object value)
	{
		if (value == null)
		{
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			if (isRangePartition(node.getTableType()))
			{
				return 0;
			}
			throw new Exception("unknown sub_partition type");
		}
		object[] array = node.getBoundaries().get(index);
		List<int> includeFlag = node.getIncludeFlag();
		int num = -1;
		try
		{
			if (value is byte || value is sbyte)
			{
				int num2 = Convert.ToInt32(value);
				num = Array.BinarySearch(array, num2);
			}
			else if (value is int num3)
			{
				num = Array.BinarySearch(array, num3);
			}
			else
			{
				if (array[0] is long && value is int)
				{
					value = Convert.ToInt64((int)value);
				}
				num = Array.BinarySearch(array, value);
			}
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		if (num < 0)
		{
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			num = -1 - num;
		}
		if (value.Equals(array[num]) && includeFlag.get(num) == 0)
		{
			num++;
			if (num >= array.Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			}
		}
		return num;
	}

	private int compareChar(HorizontalTableInfo node, int index, object value)
	{
		try
		{
			string value2 = StringUtil.rightTrim(value.ToString());
			return compareVarchar(node, index, value2);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			return -1;
		}
	}

	private int compareVarchar(HorizontalTableInfo node, int index, object value)
	{
		try
		{
			if (value == null)
			{
				value = "";
			}
			object[] array = node.getBoundaries().get(index);
			List<int> includeFlag = node.getIncludeFlag();
			int num = -1;
			string str = (string)value;
			if (isListPartition(node.getTableType()))
			{
				for (int i = 0; i < array.Length; i++)
				{
					string text = (string)array[i];
					if (text.Equals("") || str.compareTo(text) == 0)
					{
						num = i;
						break;
					}
				}
				if (num == -1)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
				}
			}
			else
			{
				if (!isRangePartition(node.getTableType()))
				{
					throw new Exception("not list or range partition table");
				}
				for (int j = 0; j < array.Length; j++)
				{
					string text2 = (string)array[j];
					if (text2.Equals("") || str.compareTo(text2) <= 0)
					{
						num = j;
						break;
					}
				}
				if (value.Equals(array[num]) && includeFlag.get(num) == 0)
				{
					num++;
					if (num >= array.Length)
					{
						DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
					}
				}
			}
			return num;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			return -1;
		}
	}

	private int compareDecimal(HorizontalTableInfo node, int index, int columnId, object value, Dictionary<int, int[]> columnIdScale)
	{
		if (value == null)
		{
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			if (isRangePartition(node.getTableType()))
			{
				return 0;
			}
			throw new Exception("not list or range partition table");
		}
		if (columnIdScale != null && columnIdScale.Count != 0)
		{
			if (!(value is decimal))
			{
				value = decimal.Parse(value.ToString());
			}
			int[] array = columnIdScale.get(columnId);
			if (array == null)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_ROW_SET_COL_KEY_INVALID);
			}
			value = FldrStatement.roundHalfUp((decimal)value, array[0], array[1]);
		}
		object[] array2 = node.getBoundaries().get(index);
		List<int> includeFlag = node.getIncludeFlag();
		int num = -1;
		try
		{
			if (!(value is decimal))
			{
				value = decimal.Parse(value.ToString());
			}
			num = Array.BinarySearch(array2, value);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		if (num < 0)
		{
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			num = -1 - num;
		}
		decimal value2 = ((!(value is decimal)) ? decimal.Parse(value.ToString()) : ((decimal)value));
		decimal value3 = (decimal)array2[num];
		if (value2.compareTo(value3) == 0 && includeFlag.get(num) == 0)
		{
			num++;
			if (num >= array2.Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			}
		}
		return num;
	}

	private int compareDate(bool intervalFlag, HorizontalTableInfo node, int index, object value, int sqlType)
	{
		if (value == null)
		{
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			if (isRangePartition(node.getTableType()))
			{
				return 0;
			}
			throw new Exception("not list or range partition table");
		}
		object[] array = node.getBoundaries().get(index);
		long[] array2 = new long[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = Convert.ToInt64(array[i]);
		}
		DateTime date;
		if (!(value is string dateStr))
		{
			if (!(value is DateTime dateTime))
			{
				if (!(value is DateTimeOffset dateTimeOffset))
				{
					if (!(value is TimeSpan timeSpan))
					{
						throw new Exception($"数据{value}无法处理");
					}
					date = new DateTime(timeSpan.Ticks);
				}
				else
				{
					date = dateTimeOffset.DateTime;
				}
			}
			else
			{
				date = dateTime;
			}
		}
		else
		{
			date = parseTimeTZ(dateStr);
		}
		long num = ((sqlType != 15 && sqlType != 22) ? FLDR_GET_TABLE_INFO.dateHelper(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, date.Millisecond) : timeHelper(date));
		List<int> includeFlag = node.getIncludeFlag();
		int num2 = -1;
		try
		{
			num2 = Array.BinarySearch(array2, num);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		if (num2 < 0)
		{
			if (intervalFlag)
			{
				return num2;
			}
			if (isListPartition(node.getTableType()))
			{
				return node.getDefaultPartition();
			}
			num2 = -1 - num2;
		}
		if (num == array2[num2] && includeFlag.get(num2) == 0)
		{
			num2++;
			if (num2 >= array.Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			}
		}
		return num2;
	}

	private long timeHelper(DateTime date)
	{
		return (long)date.Hour * 3600000L + (long)date.Minute * 60000L + (long)date.Second * 1000L + date.Millisecond;
	}

	private static DateTime parseTimeTZ(string dateStr)
	{
		int num = 0;
		int num2;
		if ((num2 = dateStr.indexOf(" +")) > 0)
		{
			num = Convert.ToInt32(dateStr.substring(Math.Min(num2 + 1, dateStr.Length), Math.Min(num2 + 2, dateStr.Length)));
			dateStr = dateStr.substring(0, num2);
		}
		else if ((num2 = dateStr.indexOf(" -")) > 0)
		{
			num = -Convert.ToInt32(dateStr.substring(Math.Min(num2 + 1, dateStr.Length), Math.Min(num2 + 2, dateStr.Length)));
			dateStr = dateStr.substring(0, num2);
		}
		DateTime result = DateTime.ParseExact(dateStr, dateStr.contains(" ") ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
		result.AddHours(num);
		return result;
	}

	public static bool isHashPartition(int tableType)
	{
		if (36 != tableType)
		{
			return 8 == tableType;
		}
		return true;
	}

	public static bool isRangePartition(int tableType)
	{
		if (34 != tableType)
		{
			return 6 == tableType;
		}
		return true;
	}

	public static bool isListPartition(int tableType)
	{
		if (38 != tableType)
		{
			return 11 == tableType;
		}
		return true;
	}
}
