using System;
using System.Collections.Generic;
using A;
using Dm.net.buffer;
using Dm.util;

namespace Dm;

internal class FLDR_FIND_INTERVAL : MSG<HorizontalTableInfo>
{
	private int tableId;

	private byte lanMode;

	private object data;

	private TableInfo tableInfo;

	private int boundaryIndex;

	private int innerIndex;

	private object newBoundary;

	public FLDR_FIND_INTERVAL(B access, int boundaryIndex, int innerIndex, int tableId, byte lanMode, object data, object newBoundary, TableInfo tableInfo)
		: base(access, (short)64)
	{
		this.boundaryIndex = boundaryIndex;
		this.innerIndex = innerIndex;
		this.tableId = tableId;
		this.lanMode = lanMode;
		this.data = data;
		this.tableInfo = tableInfo;
		this.newBoundary = newBoundary;
	}

	protected override void doEncode()
	{
		access.A.H(tableId);
		access.A.H(tableInfo.intervalColumnInfo.sqlType);
		access.A.A(lanMode);
		setDopDataPack();
		setIntervalDop();
	}

	private void setDopDataPack()
	{
		IntervalColumnInfo intervalColumnInfo = tableInfo.intervalColumnInfo;
		access.A.A((short)intervalColumnInfo.sqlType);
		if (data == null)
		{
			access.A.H(0);
			return;
		}
		access.A.H(1);
		switch (intervalColumnInfo.sqlType)
		{
		case 20:
		{
			DmIntervalYM dmIntervalYM = ((!(data is DmIntervalYM)) ? new DmIntervalYM(data.ToString()) : ((DmIntervalYM)data));
			access.A.H(dmIntervalYM.years);
			access.A.H(dmIntervalYM.months);
			access.A.H(((DmIntervalYM)intervalColumnInfo.intervalValue).getScaleForSvr());
			break;
		}
		case 21:
		{
			DateTime dateTime = default(DateTime);
			if (data is DmIntervalDT dmIntervalDT)
			{
				dateTime = new DateTime(0, 0, dmIntervalDT.days, dmIntervalDT.hours, dmIntervalDT.minutes, dmIntervalDT.seconds, dmIntervalDT.getMsec());
			}
			else if (data is DateTime dateTime2)
			{
				dateTime = dateTime2;
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			}
			byte[] array = Arrays.CopyOf(FldrUtil.fromDate12(dateTime), 24);
			access.A.A(array);
			break;
		}
		case 7:
			access.A.H(Convert.ToInt32(data));
			break;
		case 8:
			access.A.A(Convert.ToInt64(data));
			break;
		case 11:
			access.A.A(Convert.ToDouble(data));
			break;
		case 10:
			access.A.A(Convert.ToSingle(data));
			break;
		case 9:
		{
			decimal value = Convert.ToDecimal(intervalColumnInfo.intervalValue);
			access.A.A(FLDR_INSERT.getDecimal2(DmdbNumeric.valueOf(data.ToString(), value.precision(), value.scale()).toDecimal(compatibleOracle: false)));
			break;
		}
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_TYPE);
			break;
		}
	}

	private void setIntervalDop()
	{
		IntervalColumnInfo intervalColumnInfo = tableInfo.intervalColumnInfo;
		access.A.A((short)intervalColumnInfo.sqlType);
		access.A.H(1);
		access.A.A(intervalColumnInfo.intervalValueBytes);
	}

	protected override HorizontalTableInfo doDecode()
	{
		string text = access.A.B(access.a().ServerEncoding);
		int num = access.A.d();
		int raftId = access.A.C();
		int num2 = access.A.d();
		int num3 = access.A.d();
		short baseTableType = access.A.C();
		if (TableInfo.msgVersion < 6)
		{
			int num4 = num3 & 0x3F;
			if (num4 == 6 || num4 == 8 || num4 == 11 || num4 == 34 || num4 == 36 || num4 == 38)
			{
				DmError.ThrowDmException(DmErrorDefinition.FLDR_INTERVAL_PARTITION_ERROR);
			}
		}
		if (tableInfo.subTableNameIdMap == null)
		{
			tableInfo.subTableNameIdMap = new Dictionary<string, int>();
		}
		tableInfo.subTableNameIdMap.put(text, num);
		HorizontalTableInfo horizontalTableInfo = new HorizontalTableInfo();
		horizontalTableInfo.setTableName(text);
		horizontalTableInfo.setTableId(num);
		horizontalTableInfo.setTableType(num3);
		horizontalTableInfo.setBaseTableId(num2);
		horizontalTableInfo.setBaseTableType(baseTableType);
		horizontalTableInfo.setSubTableIds(new List<int>(0));
		horizontalTableInfo.setIncludeFlag(new List<int>(0));
		horizontalTableInfo.setChildren(null);
		horizontalTableInfo.setRaftId(raftId);
		HorizontalTableInfo horizontalTableInfo2 = tableInfo.hTableNodeMap.get(num2);
		if (horizontalTableInfo2 != null)
		{
			horizontalTableInfo2.getSubTableIds().add(innerIndex, horizontalTableInfo.getTableId());
			horizontalTableInfo2.getIncludeFlag().add(innerIndex, horizontalTableInfo2.getIncludeFlag().get(0));
			horizontalTableInfo2.getChildren().add(innerIndex, horizontalTableInfo);
			setBoundary(horizontalTableInfo2);
			tableInfo.hTableNodeMap.put(num, horizontalTableInfo);
			return horizontalTableInfo;
		}
		throw new Exception("Interval partition id not found");
	}

	private void setBoundary(HorizontalTableInfo head)
	{
		object[] array = head.getBoundaries().get(boundaryIndex);
		object[] array2 = new object[array.Length + 1];
		for (int i = 0; i < array2.Length; i++)
		{
			if (i < innerIndex)
			{
				array2[i] = array[i];
			}
			else if (i == innerIndex)
			{
				array2[i] = newBoundary;
			}
			else
			{
				array2[i] = array[i - 1];
			}
		}
		head.getBoundaries().set(boundaryIndex, array2);
	}
}
