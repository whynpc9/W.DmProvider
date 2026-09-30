using System;
using W.Dm.util;

namespace W.Dm;

public class FldrHashCode
{
	private const long HC_MASK = 4294967295L;

	private const int TZ_INVALID_VALUE = 1000;

	private const int TZ_DEFAULT_VALUE = 480;

	private static readonly int[] global_days_befor_month = new int[12]
	{
		0, 31, 59, 90, 120, 151, 181, 212, 243, 273,
		304, 334
	};

	private const int MAXIMUM_CAPACITY = 1073741824;

	public static int tableSizeFor(int cap)
	{
		int num = cap - 1;
		num |= num >> 1;
		num |= num >> 2;
		num |= num >> 4;
		num |= num >> 8;
		num |= num >> 16;
		if (num >= 0)
		{
			if (num < 1073741824)
			{
				return num + 1;
			}
			return 1073741824;
		}
		return 1;
	}

	public static long hc_get_fold_fun(int destType, int hashSize, object data, DmConnection conn)
	{
		DmField dmField = new DmField(conn.m_ConnInst);
		dmField.resetType(destType);
		byte[] value = null;
		bool caseSensitive = conn.GetConnInstance().ConnProperty.CaseSensitive;
		switch (destType)
		{
		case 3:
		case 5:
		case 6:
		case 25:
			return hc_get_l4_fold(data);
		case 7:
		case 10:
			return hc_get_int_fold(data);
		case 8:
		case 28:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
			}
			return hc_get_int64_fold(value);
		case 11:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
			}
			return hc_get_double_fold(value);
		case 14:
		case 16:
		case 26:
			if (data != null)
			{
				value = FldrUtil.fromDate((DateTime)data);
			}
			return hc_get_datetime_fold(value);
		case 23:
		case 27:
			return hc_get_datetime_with_tz_fold(data);
		case 20:
			return hc_get_ivym_fold((DmIntervalYM)data);
		case 21:
			return hc_get_ivdt_fold((DmIntervalDT)data);
		case 9:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
			}
			return hc_get_dec_fold(value);
		case 15:
			if (data != null)
			{
				value = FldrUtil.fromDate((DateTime)data);
			}
			return hc_get_time_fold(value);
		case 22:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
				return hc_get_time_with_tz_fold(DmDateTime.valueOf(value, dmField, conn).dt);
			}
			return ((long)hashSize << 1) - 1;
		case 0:
		case 1:
		case 2:
		case 12:
		case 19:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
			}
			if (caseSensitive)
			{
				return hc_get_varlen_cs_fold_fnv1a(value);
			}
			return hc_get_varlen_ncs_fold_fnv1a(value);
		case 17:
		case 18:
			if (data != null)
			{
				value = N2DB.fromObject(data, dmField, conn);
			}
			if (!conn.compatibleMysql())
			{
				return hc_get_varlen_cs_fold_fnv1a(value);
			}
			return hc_get_binary_fold_in_sqlserver_mode(value);
		default:
			return 0L;
		}
	}

	public static int compareNumHash(int hashSize, int subTableNums, object value)
	{
		if (value == null)
		{
			return 0;
		}
		long num;
		for (num = (long)value % hashSize; num >= subTableNums; num -= hashSize / 2)
		{
		}
		return (int)num;
	}

	private static long hc_get_l4_fold(object value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = ((value is byte || value is sbyte) ? Convert.ToInt32(value) : ((!(value is short)) ? ((int)value) : ((short)value)));
		return num & 0xFFFFFFFFu;
	}

	private static long hc_get_int_fold(object value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = 0;
		num = ((!(value is float)) ? ((int)value) : ByteUtil.SingleToInt32Bits((float)value));
		return num & 0xFFFFFFFFu;
	}

	private static long hc_get_int64_fold(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		return ByteUtil.getInt(value, 0) & 0xFFFFFFFFu;
	}

	private static long hc_get_double_fold(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		long num = ByteUtil.getInt(value, 0) & 0xFFFFFFFFu;
		long num2 = ByteUtil.getInt(value, 4) & 0xFFFFFFFFu;
		return num + num2;
	}

	private static long hc_get_time_fold(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		return ByteUtil.getInt(value, 4) & 0xFFFFFFFFu;
	}

	private static long hc_get_datetime_fold(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		return ((value[6] + value[3]) | (value[5] << 6) | (value[4] << 12) | (value[3] << 17) | (value[2] << 22) | ((value[0] & 7) << 26) | ((value[7] & 7) << 29)) & 0xFFFFFFFFu;
	}

	private static long hc_get_ivym_fold(DmIntervalYM value)
	{
		if (value == null)
		{
			return 0L;
		}
		return (((long)value.getYear() << 4) | value.getMonth()) & 0xFFFFFFFFu;
	}

	private static long hc_get_ivdt_fold(DmIntervalDT value)
	{
		if (value == null)
		{
			return 0L;
		}
		return (((long)value.getDay() << 18) | ((long)value.getHour() << 12) | ((long)value.getMinute() << 6) | value.getSecond()) & 0xFFFFFFFFu;
	}

	private static long hc_get_varlen_cs_fold_fnv1a(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = value.Length;
		if (num == 0)
		{
			return 0L;
		}
		int num2 = 0;
		if (num == 1)
		{
			return ByteUtil.getByte(value, num2);
		}
		num2 = num2 + num - 1;
		while (value[num2] == 32 && num != 0)
		{
			num2--;
			num--;
		}
		return hc_get_varlen_fold_cs_low_fnv1a(value, num);
	}

	private static long hc_get_varlen_fold_cs_low_fnv1a(byte[] value, int length)
	{
		if (length == 2)
		{
			return ByteUtil.getUB2(value, 0);
		}
		int num = 0;
		for (int i = 0; i < length; i++)
		{
			num ^= value[i];
			num += (num << 1) + (num << 4) + (num << 7) + (num << 8) + (num << 24);
		}
		return num & 0xFFFFFFFFu;
	}

	private static long hc_get_varlen_ncs_fold_fnv1a(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = value.Length;
		if (num == 0)
		{
			return 0L;
		}
		int num2 = 0;
		num2 = num2 + num - 1;
		while (value[num2] == 32 && num != 0)
		{
			num2--;
			num--;
		}
		return hc_get_varlen_fold_ncs_low_fnv1a(value, num);
	}

	private static long hc_get_varlen_fold_ncs_low_fnv1a(byte[] value, int length)
	{
		int num = 0;
		switch (length)
		{
		case 1:
		{
			byte b = ByteUtil.getByte(value, 0);
			if (b > 96)
			{
				return b - 32;
			}
			return b;
		}
		case 2:
		{
			byte b = value[0];
			if (b > 96)
			{
				b -= 32;
			}
			byte b2 = value[1];
			if (b2 > 96)
			{
				b2 -= 32;
			}
			return (b2 << 8) + b;
		}
		default:
		{
			for (int i = 0; i < length; i++)
			{
				num = ((value[i] <= 96) ? (num ^ value[i]) : (num ^ (value[i] - 32)));
				num += (num << 1) + (num << 4) + (num << 7) + (num << 8) + (num << 24);
			}
			return num & 0xFFFFFFFFu;
		}
		}
	}

	private static long hc_get_datetime_with_tz_fold(object value)
	{
		if (value == null)
		{
			return 0L;
		}
		DateTime dateTime = (DateTime)value;
		int num = dateTime.Year;
		int num2 = num * 365 + global_days_befor_month[dateTime.Month - 1] + dateTime.Day;
		if (dateTime.Month <= 1)
		{
			num--;
		}
		int num3 = ((num >= 1600) ? (400 + (num - 1600) / 4 - (num - 1600) / 100 + (num - 1600) / 400) : (num / 4));
		if (num >= 0)
		{
			num3++;
		}
		num2 += num3;
		int num4 = (int)(TimeZoneInfo.Local.BaseUtcOffset.TotalMilliseconds / 60000.0);
		if (num4 == 1000)
		{
			num4 = 480;
		}
		int num5 = dateTime.Hour * 60 + dateTime.Minute - num4;
		int num6 = num5 / 1440;
		num2 += num6;
		num5 -= num6 * 24 * 60;
		if (num5 < 0)
		{
			num2--;
			num5 += 1440;
		}
		int millisecond = dateTime.Millisecond;
		return num2 + num5 + millisecond + dateTime.Second;
	}

	private static long hc_get_time_with_tz_fold(int[] dt)
	{
		int num = dt[3];
		int num2 = dt[4];
		int num3 = dt[5];
		int num4 = dt[6];
		int num5 = dt[7];
		if (num5 == 1000)
		{
			num5 = 480;
		}
		int num6 = num * 60 + num2 - num5;
		if (num6 < 0)
		{
			num6 += 1440;
		}
		return (num6 + num4 + num3) & 0xFFFFFFFFu;
	}

	private static long hc_get_dec_fold(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = 1;
		int num2 = 0;
		int num3 = 0;
		int num4;
		for (num4 = value.Length - 1; num4 > 4; num4 -= 4)
		{
			num3 += ByteUtil.getInt(value, num);
			num += 4;
		}
		if (num4 > 0)
		{
			byte[] array = new byte[4];
			Array.Copy(value, num, array, 0, num4);
			num2 = ByteUtil.getInt(array, 0);
		}
		return (num2 + num3) & 0xFFFFFFFFu;
	}

	private static long hc_get_varlen_fold_cs_low(byte[] value, int length)
	{
		int num = 1;
		int num2 = 4;
		if (length == 0)
		{
			return num;
		}
		if (length == 2)
		{
			return ByteUtil.getUB2(value, 0);
		}
		if (length > 64)
		{
			return bfd_varlen_low_cs_max(value, length);
		}
		for (int i = 0; i < length; i++)
		{
			byte b = value[i];
			num ^= ((num & 0x3F) + num2) * b + (num << 8);
			num2 += 3;
		}
		return num & 0xFFFFFFFFu;
	}

	private static long bfd_varlen_low_cs_max(byte[] value, int length)
	{
		int num = 1;
		int num2 = 4;
		int num3 = length >> 6;
		int num4 = (length >> 1) - 1;
		int num5 = num4 + 1;
		int num6 = 32 + length % 64 / num3 / 2;
		for (int i = 0; i < num6; i++)
		{
			int num7 = num4;
			num ^= ((num & 0x3F) + num2) * value[num7] + (num << 8);
			num2 += 3;
			num4 -= num3;
			num7 = num5;
			num ^= ((num & 0x3F) + num2) * value[num7] + (num << 8);
			num2 += 3;
			num5 += num3;
		}
		return num & 0xFFFFFFFFu;
	}

	private static long hc_get_binary_fold_in_sqlserver_mode(byte[] value)
	{
		if (value == null)
		{
			return 0L;
		}
		int num = value.Length;
		if (num == 0)
		{
			return 0L;
		}
		int num2 = 0;
		if (num == 1)
		{
			return ByteUtil.getByte(value, num2);
		}
		num2 = num2 + num - 1;
		while ((value[num2] == 32 || value[num2] == 0) && num != 0)
		{
			num2--;
			num--;
		}
		return hc_get_varlen_fold_cs_low(value, num);
	}
}
