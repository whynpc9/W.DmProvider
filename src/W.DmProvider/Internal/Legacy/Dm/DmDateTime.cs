using System;
using W.Dm.util;

namespace W.Dm;

public class DmDateTime
{
	private static int[] global_day_by_month = new int[13]
	{
		31, 31, 28, 31, 30, 31, 30, 31, 31, 30,
		31, 30, 31
	};

	private static int[] global_days_befor_month = new int[12]
	{
		0, 31, 59, 90, 120, 151, 181, 212, 243, 273,
		304, 334
	};

	private const int DMDT_YEAR_POS = 0;

	private const int DMDT_YEAR_LEN = 15;

	private const int DMDT_MONTH_POS = 15;

	private const int DMDT_MONTH_LEN = 4;

	private const int DMDT_DAY_POS = 19;

	private const int DMDT_DAY_LEN = 5;

	private const int DMDT_DATE_SIZE = 24;

	private const int DMDT_HOUR_POS = 0;

	private const int DMDT_HOUR_LEN = 5;

	private const int DMDT_MIN_POS = 5;

	private const int DMDT_MIN_LEN = 6;

	private const int DMDT_SEC_POS = 11;

	private const int DMDT_SEC_LEN = 6;

	private const int DMDT_MSEC_POS = 17;

	private const int DMDT_MSEC_LEN = 20;

	private const int DMDT_TZ_POS = 37;

	private const int DMDT_TZ_LEN = 15;

	private const int LOCAL_TIME_ZONE_MASK = 4096;

	private byte[] dateTime = new byte[3];

	public const int DMDT_DATE = 0;

	public const int DMDT_TIME = 1;

	public const int DMDT_TIMESTAMP = 2;

	public const int DMDT_TIMESTAMP_TZ = 3;

	public const int DMDT_TIME_TZ = 4;

	private int prec;

	public const int MAX_YEAR = 9999;

	public const int MIN_YEAR = -4712;

	internal const int OFFSET_YEAR = 0;

	internal const int OFFSET_MONTH = 1;

	internal const int OFFSET_DAY = 2;

	internal const int OFFSET_HOUR = 3;

	internal const int OFFSET_MINUTE = 4;

	internal const int OFFSET_SECOND = 5;

	internal const int OFFSET_NANOSECOND = 6;

	internal const int OFFSET_TIMEZONE = 7;

	internal const int DT_LEN = 8;

	public const int INVALID_VALUE = int.MinValue;

	public const int NANOSECOND_DIGITS = 9;

	public const string ZEROS = "000000000";

	public int[] dt;

	public int dtype;

	public int scale;

	public static int LTZ_GET_REAL_PREC(int prec)
	{
		return prec & -4097;
	}

	public static bool NTYPE_IS_LOCAL_TIME_ZONE(int sql_pl_type, int prec)
	{
		if (sql_pl_type == 16 && (prec & 0x1000) != 0)
		{
			return true;
		}
		return false;
	}

	public static void DmTimeGetDateByNdaysFromZero(int ndays, ref int ret_year, ref int ret_month, ref int ret_day)
	{
		if (ndays <= 365 || ndays > 3652424)
		{
			ret_year = (ret_month = (ret_day = 99));
			return;
		}
		int num = ndays * 100 / 36525;
		int num2 = ((num - 1) / 100 + 1) * 3 / 4;
		int num3 = ndays - num * 365 - (num - 1) / 4 + num2;
		int num4 = (DmTimeIsLeapYear(num) ? 366 : 365);
		while (num3 > num4)
		{
			num3 -= num4;
			num++;
			num4 = (DmTimeIsLeapYear(num) ? 366 : 365);
		}
		if (num4 == 366 && num3 > 59)
		{
			num3--;
			if (num3 == 59)
			{
				ret_year = num;
				ret_month = 2;
				ret_day = 29;
				return;
			}
		}
		int i;
		for (i = 1; num3 > global_day_by_month[i]; i++)
		{
			num3 -= global_day_by_month[i];
		}
		ret_year = num;
		ret_month = i;
		ret_day = num3;
	}

	public static int DmtimeCalcNDaysFromZero(int year, int month, int day)
	{
		if (year <= 0 && month <= 0 && day <= 0)
		{
			return 0;
		}
		int num = year * 365 + global_days_befor_month[month - 1] + day;
		if (month <= 2)
		{
			year--;
		}
		int num2 = year / 4 - year / 100 + year / 400;
		return num + num2;
	}

	public static void DmTimeDdateAdd(ref int year, ref int month, ref int day, int n)
	{
		DmTimeGetDateByNdaysFromZero(DmtimeCalcNDaysFromZero(year, month, day) + n, ref year, ref month, ref day);
	}

	public static DateTime DmtimeAddByFmt(int prec, DmDateTime dt_new, int fmt, int n)
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		int year = dt_new.GetYear();
		int month = dt_new.GetMonth();
		int day = dt_new.GetDay();
		int num = dt_new.GetHour(24);
		int num2 = dt_new.GetMinute(24);
		int num3 = dt_new.GetSecond(24);
		int num4 = dt_new.GetNano(24);
		short tZ = dt_new.GetTZ(24);
		if (n != 0)
		{
			while (true)
			{
				if (fmt == 1)
				{
					year += n;
				}
				else if (fmt == 8 || fmt == 2)
				{
					if (fmt == 8)
					{
						n *= 3;
					}
					month += n;
					year += month / 12;
					month %= 12;
					if (month < 1)
					{
						month += 12;
						year--;
					}
					if (day > global_day_by_month[month])
					{
						day = global_day_by_month[month];
						if (month == 2 && DmTimeIsLeapYear(year))
						{
							day++;
						}
					}
				}
				else if (fmt == 7 || fmt == 6 || fmt == 5)
				{
					if (fmt == 7)
					{
						flag = true;
						num4 = num4 / 1000 + n;
						n = num4 / 1000;
						num4 %= 1000;
						while (num4 < 0)
						{
							num4 += 1000;
							n--;
						}
						num4 *= 1000;
						if (n == 0)
						{
							break;
						}
					}
					if (flag || fmt == 6)
					{
						flag2 = true;
						int num5 = num3 + n;
						n = num5 / 60;
						num3 = num5 % 60;
						if (num3 < 0)
						{
							num3 += 60;
							n--;
						}
						if (n == 0)
						{
							break;
						}
					}
					if (flag || flag2 || fmt == 5)
					{
						flag3 = true;
						int num6 = num2 + n;
						n = num6 / 60;
						num2 = num6 % 60;
						if (num2 < 0)
						{
							num2 += 60;
							n--;
						}
						if (n == 0)
						{
							break;
						}
					}
					if (flag || flag2 || flag3 || fmt == 4)
					{
						flag4 = true;
						int num7 = num + n;
						n = num7 / 24;
						num = num7 % 24;
						if (num < 0)
						{
							num += 24;
							n--;
						}
						if (n == 0)
						{
							break;
						}
					}
				}
				if (flag4 || fmt == 3 || fmt == 9)
				{
					DmTimeDdateAdd(ref year, ref month, ref day, n);
					break;
				}
				if (fmt == 10)
				{
					DmTimeDdateAdd(ref year, ref month, ref day, n * 7);
					break;
				}
			}
		}
		if (year > 9999 || year < 1)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATETIME_OVERFLOW);
		}
		return DmTimeEncodeTz(prec, year, month, day, num, num2, num3, num4, tZ);
	}

	public static bool DmTimeIsLeapYear(int year)
	{
		if (year < 0)
		{
			year = -year;
		}
		if (year % 4 == 0)
		{
			if (year % 100 == 0 && year % 400 != 0)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool DmTimeDataValidate(int year, int month, int day)
	{
		if (year > 9999 || year < 1 || month > 12 || month < 1)
		{
			return true;
		}
		switch (month)
		{
		case 1:
		case 3:
		case 5:
		case 7:
		case 8:
		case 10:
		case 12:
			if (day > 31 || day < 1)
			{
				return true;
			}
			break;
		case 4:
		case 6:
		case 9:
		case 11:
			if (day > 30 || day < 1)
			{
				return true;
			}
			break;
		case 2:
			if (DmTimeIsLeapYear(year))
			{
				if (day > 29 || day < 1)
				{
					return true;
				}
			}
			else if (day > 28 || day < 1)
			{
				return true;
			}
			break;
		}
		return false;
	}

	public static bool DmTimeTzValidate(short tz)
	{
		if (tz < -779 || (tz > 780 && tz != 1000))
		{
			return true;
		}
		return false;
	}

	public static bool DmTimeTimeValidate(int hour, int min, int sec, int msec)
	{
		if (hour > 23 || min > 59 || sec > 59 || msec > 9999999)
		{
			return true;
		}
		return false;
	}

	public static DateTime DmTimeEncodeTz(int prec, int year, int month, int day, int hour, int min, int sec, int msec, short tz)
	{
		byte[] array = new byte[12];
		if (DmTimeDataValidate(year, month, day))
		{
			if (year == 0 && month == 0 && day == 0)
			{
				DateTime today = DateTime.Today;
				year = today.Year + 1900;
				month = today.Month + 1;
				day = today.Day;
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_DATETIME_OVERFLOW);
			}
		}
		if (DmTimeTimeValidate(hour, min, sec, msec))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATETIME_OVERFLOW);
		}
		if (DmTimeTzValidate(tz))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATETIME_OVERFLOW);
		}
		array[0] = (byte)(year & 0xFF);
		array[1] = (byte)(year >> 8);
		array[2] = (byte)month;
		array[3] = (byte)day;
		array[4] = (byte)hour;
		array[5] = (byte)min;
		array[6] = (byte)sec;
		array[7] = (byte)(msec & 0xFF);
		array[8] = (byte)((msec >> 8) & 0xFF);
		array[9] = (byte)((msec >> 16) & 0xFF);
		array[10] = (byte)(tz & 0xFF);
		array[11] = (byte)(tz >> 8);
		return new DmDateTime(array, prec).GetTimestamp();
	}

	public DmDateTime()
	{
		dt = new int[8];
		dtype = 16;
		scale = 6;
	}

	public DmDateTime(byte[] dt, int prec)
	{
		dateTime = dt;
		this.prec = prec;
	}

	public DmDateTime(string str, int prec, int flag)
	{
		this.prec = prec;
		dateTime = new byte[12];
		switch (flag)
		{
		case 0:
		{
			char[] separator4 = "-".ToCharArray();
			string[] array7 = str.Split(separator4);
			short i3 = short.Parse(array7[0], DmConst.invariantCulture);
			byte b14 = byte.Parse(array7[1], DmConst.invariantCulture);
			byte b15 = byte.Parse(array7[2], DmConst.invariantCulture);
			byte[] array8 = DmConvertion.ShortToByteArray(i3);
			dateTime[0] = array8[0];
			dateTime[1] = array8[1];
			dateTime[2] = b14;
			dateTime[3] = b15;
			break;
		}
		case 2:
		{
			char[] separator3 = "-: .".ToCharArray();
			string[] array5 = str.Split(separator3);
			short i2 = short.Parse(array5[0], DmConst.invariantCulture);
			byte b9 = byte.Parse(array5[1], DmConst.invariantCulture);
			byte b10 = byte.Parse(array5[2], DmConst.invariantCulture);
			byte[] array6 = DmConvertion.ShortToByteArray(i2);
			dateTime[0] = array6[0];
			dateTime[1] = array6[1];
			dateTime[2] = b9;
			dateTime[3] = b10;
			byte b11 = byte.Parse(array5[3], DmConst.invariantCulture);
			byte b12 = byte.Parse(array5[4], DmConst.invariantCulture);
			byte b13 = byte.Parse(array5[5], DmConst.invariantCulture);
			dateTime[4] = b11;
			dateTime[5] = b12;
			dateTime[6] = b13;
			Array.Copy(DmConvertion.IntToByteArray((int)(double.Parse(string.Concat("" + "0.", array5[6]), DmConst.invariantCulture) * 1000000.0)), 0, dateTime, 7, 3);
			break;
		}
		case 3:
		{
			char[] separator2 = "-: .".ToCharArray();
			string[] array3 = str.Split(separator2);
			short i = short.Parse(array3[0], DmConst.invariantCulture);
			byte b4 = byte.Parse(array3[1], DmConst.invariantCulture);
			byte b5 = byte.Parse(array3[2], DmConst.invariantCulture);
			byte[] array4 = DmConvertion.ShortToByteArray(i);
			dateTime[0] = array4[0];
			dateTime[1] = array4[1];
			dateTime[2] = b4;
			dateTime[3] = b5;
			byte b6 = byte.Parse(array3[3], DmConst.invariantCulture);
			byte b7 = byte.Parse(array3[4], DmConst.invariantCulture);
			byte b8 = byte.Parse(array3[5], DmConst.invariantCulture);
			dateTime[4] = b6;
			dateTime[5] = b7;
			dateTime[6] = b8;
			Array.Copy(DmConvertion.IntToByteArray((int)(double.Parse(string.Concat("" + "0.", array3[6]), DmConst.invariantCulture) * 1000000.0)), 0, dateTime, 7, 3);
			short num3 = short.Parse(array3[7], DmConst.invariantCulture);
			short num4 = short.Parse(array3[8], DmConst.invariantCulture);
			array4 = DmConvertion.ShortToByteArray((short)(num3 * 60 + num4));
			dateTime[10] = array4[0];
			dateTime[11] = array4[1];
			break;
		}
		case 4:
		{
			char[] separator = ": .".ToCharArray();
			string[] array = str.Split(separator);
			byte b = byte.Parse(array[0], DmConst.invariantCulture);
			byte b2 = byte.Parse(array[1], DmConst.invariantCulture);
			byte b3 = byte.Parse(array[2], DmConst.invariantCulture);
			dateTime[4] = b;
			dateTime[5] = b2;
			dateTime[6] = b3;
			Array.Copy(DmConvertion.IntToByteArray((int)(double.Parse(string.Concat("" + "0.", array[3]), DmConst.invariantCulture) * 1000000.0)), 0, dateTime, 7, 3);
			short num = short.Parse(array[4], DmConst.invariantCulture);
			short num2 = short.Parse(array[5], DmConst.invariantCulture);
			byte[] array2 = DmConvertion.ShortToByteArray((short)(num * 60 + num2));
			dateTime[10] = array2[0];
			dateTime[11] = array2[1];
			break;
		}
		}
	}

	public DmDateTime(DateTime dt, int prec, int scale, int flag, short mTimeZone)
	{
		if (scale > 7)
		{
			scale &= 0xF;
		}
		dt = MathUtil.Round(dt, scale);
		this.prec = prec;
		dateTime = new byte[12];
		switch (flag)
		{
		case 0:
		{
			short i3 = (short)dt.Year;
			byte b11 = (byte)dt.Month;
			byte b12 = (byte)dt.Day;
			byte[] array3 = DmConvertion.ShortToByteArray(i3);
			dateTime[0] = array3[0];
			dateTime[1] = array3[1];
			dateTime[2] = b11;
			dateTime[3] = b12;
			break;
		}
		case 2:
		{
			short i2 = (short)dt.Year;
			byte b6 = (byte)dt.Month;
			byte b7 = (byte)dt.Day;
			byte[] array2 = DmConvertion.ShortToByteArray(i2);
			dateTime[0] = array2[0];
			dateTime[1] = array2[1];
			dateTime[2] = b6;
			dateTime[3] = b7;
			byte b8 = (byte)dt.Hour;
			byte b9 = (byte)dt.Minute;
			byte b10 = (byte)dt.Second;
			dateTime[4] = b8;
			dateTime[5] = b9;
			dateTime[6] = b10;
			Array.Copy(DmConvertion.IntToByteArray(GetMicroSecond(dt)), 0, dateTime, 7, 3);
			break;
		}
		case 3:
		{
			short i = (short)dt.Year;
			byte b = (byte)dt.Month;
			byte b2 = (byte)dt.Day;
			byte[] array = DmConvertion.ShortToByteArray(i);
			dateTime[0] = array[0];
			dateTime[1] = array[1];
			dateTime[2] = b;
			dateTime[3] = b2;
			byte b3 = (byte)dt.Hour;
			byte b4 = (byte)dt.Minute;
			byte b5 = (byte)dt.Second;
			dateTime[4] = b3;
			dateTime[5] = b4;
			dateTime[6] = b5;
			Array.Copy(DmConvertion.IntToByteArray(GetMicroSecond(dt)), 0, dateTime, 7, 3);
			array = DmConvertion.ShortToByteArray(mTimeZone);
			dateTime[10] = array[0];
			dateTime[11] = array[1];
			break;
		}
		}
	}

	internal static int GetMicroSecond(DateTime dt)
	{
		string text = dt.TimeOfDay.TotalSeconds.ToString();
		int num = text.IndexOf(".");
		text = ((num == -1) ? "" : text.Substring(num + 1));
		int num2 = text.Length;
		while (text.Length < 6)
		{
			text += "0";
			num2++;
		}
		return int.Parse(text);
	}

	internal static int GetNanoSecond(DateTime dt)
	{
		string text = dt.TimeOfDay.TotalSeconds.ToString();
		int num = text.IndexOf(".");
		text = ((num == -1) ? "" : text.Substring(num + 1));
		int num2 = text.Length;
		while (text.Length < 9)
		{
			text += "0";
			num2++;
		}
		return int.Parse(text);
	}

	public byte[] GetByteArrayValue()
	{
		return dateTime;
	}

	public void GetByteArrayValue(ref byte[] ret)
	{
		ret = dateTime;
	}

	private short GetYear()
	{
		int num;
		if (dateTime.Length == 12)
		{
			num = DmConvertion.TwoByteToShort(new byte[2]
			{
				dateTime[0],
				dateTime[1]
			});
			if (num > 9999)
			{
				num = (short)((short)num | 0x8000);
			}
		}
		else
		{
			num = GetBit(dateTime, 0, 15);
		}
		return (short)num;
	}

	private byte GetMonth()
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, 15, 4) : dateTime[2]);
		return (byte)num;
	}

	private byte GetDay()
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, 19, 5) : dateTime[3]);
		return (byte)num;
	}

	private byte GetHour(int offset)
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, offset, 5) : dateTime[4]);
		return (byte)num;
	}

	private byte GetMinute(int offset)
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, offset + 5, 6) : dateTime[5]);
		return (byte)num;
	}

	private byte GetSecond(int offset)
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, offset + 11, 6) : dateTime[6]);
		return (byte)num;
	}

	private short GetTZ(int offset)
	{
		int num = ((dateTime.Length != 12) ? GetBit(dateTime, offset + 37, 15) : DmConvertion.TwoByteToShort(new byte[2]
		{
			dateTime[10],
			dateTime[11]
		}));
		return (short)num;
	}

	private int GetNano(int offset)
	{
		if (dateTime.Length == 12)
		{
			byte[] array = new byte[4];
			Array.Copy(dateTime, 7, array, 0, 3);
			return DmConvertion.FourByteToInt(array);
		}
		return GetBit(dateTime, offset + 17, 20);
	}

	public DateTime GetDate()
	{
		return Convert.ToDateTime(GetDateInString());
	}

	public DateTime GetTime()
	{
		return Convert.ToDateTime(GetTimeInString());
	}

	public DateTimeOffset GetTimeTZ()
	{
		string timeTZInString = GetTimeTZInString();
		string format = "HH:mm:ss.fffffff zzz";
		return DateTimeOffset.ParseExact(timeTZInString, format, null);
	}

	public string GetDateInString()
	{
		string text = GetYear().ToString();
		string text2 = GetMonth().ToString();
		string text3 = GetDay().ToString();
		for (int i = text.Length; i < 4; i++)
		{
			text = "0" + text;
		}
		for (int i = text2.Length; i < 2; i++)
		{
			text2 = "0" + text2;
		}
		for (int i = text3.Length; i < 2; i++)
		{
			text3 = "0" + text3;
		}
		return text + "-" + text2 + "-" + text3;
	}

	public string GetDateInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		return OracleDateFormat.Format(new int[10] { year, month, day, 0, 0, 0, 0, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetDateTimeInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		int nano = GetNano(24);
		return OracleDateFormat.Format(new int[10] { year, month, day, hour, minute, second, nano, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetDateTimeTzInString()
	{
		return GetDateInString() + " " + GetTimeTZInString();
	}

	public string GetDateTimeTzInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		int nano = GetNano(24);
		int tZ = GetTZ(24);
		int[] array = new int[10];
		array[0] = year;
		array[1] = month;
		array[2] = day;
		array[3] = hour;
		array[4] = minute;
		array[5] = second;
		array[6] = nano;
		array[OracleDateFormat.OFFSET_TIMEZONE_SIGN] = ((tZ < 0) ? 1 : 0);
		array[7] = tZ;
		return OracleDateFormat.Format(array, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeInString()
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = GetNano(0);
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		string text4 = nano.ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		for (int i = text4.Length; i < 7; i++)
		{
			text4 = "0" + text4;
		}
		if (prec == 0)
		{
			return text + ":" + text2 + ":" + text3;
		}
		return text + ":" + text2 + ":" + text3 + "." + text4;
	}

	public string GetTimeInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = GetNano(0);
		return OracleDateFormat.Format(new int[10] { 0, 0, 0, hour, minute, second, nano, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeTZInString()
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = GetNano(0);
		int tZ = GetTZ(0);
		int num = ((tZ < 0) ? (-tZ) : tZ);
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		string text4 = nano.ToString() ?? "";
		string text5 = (num / 60).ToString() ?? "";
		string text6 = (num % 60).ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		for (int i = text4.Length; i < 7; i++)
		{
			text4 = "0" + text4;
		}
		if (text5.Length < 2)
		{
			text5 = "0" + text5;
		}
		text5 = ((tZ < 0) ? ("-" + text5) : ("+" + text5));
		if (text6.Length < 2)
		{
			text6 = "0" + text6;
		}
		if (prec == 0)
		{
			return text + ":" + text2 + ":" + text3 + " " + text5 + ":" + text6;
		}
		return text + ":" + text2 + ":" + text3 + "." + text4 + " " + text5 + ":" + text6;
	}

	public string GetTimeTZInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = GetNano(0);
		int tZ = GetTZ(0);
		int[] array = new int[10];
		array[3] = hour;
		array[4] = minute;
		array[5] = second;
		array[6] = nano;
		array[OracleDateFormat.OFFSET_TIMEZONE_SIGN] = ((tZ < 0) ? 1 : 0);
		array[7] = tZ;
		return OracleDateFormat.Format(array, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeOfTimestamp()
	{
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		return text + ":" + text2 + ":" + text3;
	}

	public DateTime GetTimestamp()
	{
		string text = GetNano(24).ToString();
		string text2 = GetDateInString() + " " + GetTimeOfTimestamp() + ".";
		for (int i = text.Length; i < 7; i++)
		{
			text = "0" + text;
		}
		text2 += text;
		return Convert.ToDateTime(text2);
	}

	public DateTimeOffset GetTimestampTZ()
	{
		string dateTimeTzInString = GetDateTimeTzInString();
		string format = "yyyy-MM-dd HH:mm:ss.fffffff zzz";
		return DateTimeOffset.ParseExact(dateTimeTzInString, format, null);
	}

	public static DateTime GetDateByString(string s)
	{
		return Convert.ToDateTime(s);
	}

	public static bool DmdtIsLeapYear(int year)
	{
		if (year < 0)
		{
			year = -year;
		}
		if (year % 4 == 0)
		{
			if (year % 100 == 0 && year % 400 != 0)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool CheckDate(int year, int month, int day)
	{
		if (year == 0 && month == 0 && day == 0)
		{
			return false;
		}
		if (year > 10000 || year < -10000 || month > 12 || month < 1)
		{
			return false;
		}
		switch (month)
		{
		case 1:
		case 3:
		case 5:
		case 7:
		case 8:
		case 10:
		case 12:
			if (day > 31 || day < 1)
			{
				return false;
			}
			break;
		case 4:
		case 6:
		case 9:
		case 11:
			if (day > 30 || day < 1)
			{
				return false;
			}
			break;
		case 2:
			if (DmdtIsLeapYear(year))
			{
				if (day > 29 || day < 1)
				{
					return false;
				}
			}
			else if (day > 28 || day < 1)
			{
				return false;
			}
			break;
		}
		return true;
	}

	public static DateTime GetTimestampByString(string s)
	{
		return Convert.ToDateTime(s);
	}

	public static string GetTimeFromTimestamp(DateTime ts)
	{
		int hour = ts.Hour;
		int minute = ts.Minute;
		int second = ts.Second;
		int millisecond = ts.Millisecond;
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		return text + ":" + text2 + ":" + text3 + "." + millisecond;
	}

	private int GetBit(byte[] x, int start, int len)
	{
		int num = 0;
		int num2 = start & 7;
		int num3 = start >> 3;
		int num4 = (start + len - 1 >> 3) + 1 - num3;
		int num5 = start >> 3;
		switch (num4)
		{
		case 1:
		{
			byte[] array3 = new byte[4];
			Array.Copy(x, num5, array3, 0, 1);
			num = DmConvertion.FourByteToInt(array3);
			break;
		}
		case 2:
		{
			byte[] array2 = new byte[4];
			Array.Copy(x, num5, array2, 0, 1);
			int num9 = DmConvertion.FourByteToInt(array2);
			Array.Copy(x, num5 + 1, array2, 0, 1);
			int num10 = DmConvertion.FourByteToInt(array2);
			num10 = (int)((num10 << 8) & 0xFFFFFFFFu);
			num = num9 + num10;
			break;
		}
		case 3:
		{
			byte[] array = new byte[4];
			Array.Copy(x, num5, array, 0, 1);
			int num6 = DmConvertion.FourByteToInt(array);
			Array.Copy(x, num5 + 1, array, 0, 1);
			int num7 = DmConvertion.FourByteToInt(array);
			num7 = (int)((num7 << 8) & 0xFFFFFFFFu);
			Array.Copy(x, num5 + 2, array, 0, 1);
			int num8 = DmConvertion.FourByteToInt(array);
			num8 = (int)((num8 << 16) & 0xFFFFFFFFu);
			num = num6 + num7 + num8;
			break;
		}
		}
		return (int)((num >> num2) & (uint)(-1 >>> 32 - len));
	}

	public static byte[] DmDateDecodeFast(byte[] val)
	{
		byte[] array = new byte[12];
		short num = DmConvertion.GetShort(val, 0);
		num &= 0x7FFF;
		DmConvertion.SetShort(array, 0, num);
		byte b = (byte)((DmConvertion.GetByte(val, 1) >> 7) + ((DmConvertion.GetByte(val, 2) & 7) << 1));
		array[2] = b;
		byte b2 = (byte)((DmConvertion.GetByte(val, 2) & 0xF8) >> 3);
		array[3] = b2;
		array[4] = 0;
		array[5] = 0;
		array[6] = 0;
		array[7] = 0;
		array[8] = 0;
		array[9] = 0;
		array[10] = 0;
		array[11] = 0;
		return array;
	}

	public static byte[] DmTimeDecodeFast(byte[] val)
	{
		byte[] array = new byte[12];
		byte b = (byte)(DmConvertion.GetByte(val, 0) & 0x1F);
		array[4] = b;
		byte b2 = (byte)((DmConvertion.GetByte(val, 0) >> 5) + ((DmConvertion.GetByte(val, 1) & 7) << 3));
		array[5] = b2;
		byte b3 = (byte)((DmConvertion.GetByte(val, 1) >> 3) + ((DmConvertion.GetByte(val, 2) & 1) << 5));
		array[6] = b3;
		int num = (DmConvertion.GetByte(val, 2) >> 1) + (DmConvertion.GetByte(val, 3) << 7) + ((DmConvertion.GetByte(val, 4) & 0x1F) << 15);
		num *= 1000;
		if (num > 0)
		{
			string text = num.ToString();
			num = int.Parse(text.Substring(0, 7 - Math.Max(0, 9 - text.Length)));
		}
		array[7] = (byte)(num & 0xFF);
		array[8] = (byte)((num >> 8) & 0xFF);
		array[9] = (byte)((num >> 16) & 0xFF);
		array[10] = 0;
		array[11] = 0;
		DmConvertion.SetShort(array, 0, 1900);
		array[2] = 1;
		array[3] = 1;
		return array;
	}

	public static byte[] DmDtDecodeFast(byte[] value)
	{
		short num = 0;
		byte b = 0;
		byte b2 = 0;
		byte b3 = 0;
		byte b4 = 0;
		byte b5 = 0;
		int num2 = 0;
		if (value.Length == 12)
		{
			return value;
		}
		byte[] array = new byte[12];
		int num3 = 0;
		if (value.Length == 3)
		{
			num3 = 14;
		}
		else if (value.Length == 5)
		{
			num3 = 15;
		}
		else if (value.Length == 7)
		{
			num3 = 22;
		}
		else if (value.Length == 8)
		{
			num3 = 16;
		}
		else if (value.Length == 9)
		{
			num3 = 26;
		}
		else if (value.Length == 10)
		{
			num3 = 23;
		}
		else if (value.Length == 11)
		{
			num3 = 27;
		}
		switch (num3)
		{
		case 16:
			num = (short)(DmConvertion.GetShort(value, 0) & 0x7FFF);
			if (num > 9999)
			{
				num = (short)(num | 0x8000);
			}
			b = (byte)((DmConvertion.GetByte(value, 1) >> 7) + ((DmConvertion.GetByte(value, 2) & 7) << 1));
			b2 = (byte)((DmConvertion.GetByte(value, 2) & 0xF8) >> 3);
			b3 = (byte)(DmConvertion.GetByte(value, 3) & 0x1F);
			b4 = (byte)((DmConvertion.GetByte(value, 3) >> 5) + ((DmConvertion.GetByte(value, 4) & 7) << 3));
			b5 = (byte)((DmConvertion.GetByte(value, 4) >> 3) + ((DmConvertion.GetByte(value, 5) & 1) << 5));
			num2 = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0x1F) << 15);
			num2 *= 1000;
			break;
		case 23:
			num = (short)(DmConvertion.GetShort(value, 0) & 0x7FFF);
			if (num > 9999)
			{
				num = (short)(num | 0x8000);
			}
			b = (byte)((DmConvertion.GetByte(value, 1) >> 7) + ((DmConvertion.GetByte(value, 2) & 7) << 1));
			b2 = (byte)((DmConvertion.GetByte(value, 2) & 0xF8) >> 3);
			b3 = (byte)(DmConvertion.GetByte(value, 3) & 0x1F);
			b4 = (byte)((DmConvertion.GetByte(value, 3) >> 5) + ((DmConvertion.GetByte(value, 4) & 7) << 3));
			b5 = (byte)((DmConvertion.GetByte(value, 4) >> 3) + ((DmConvertion.GetByte(value, 5) & 1) << 5));
			num2 = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0x1F) << 15);
			num2 *= 1000;
			break;
		case 26:
			num = (short)(DmConvertion.GetShort(value, 0) & 0x7FFF);
			if (num > 9999)
			{
				num = (short)(num | 0x8000);
			}
			b = (byte)((DmConvertion.GetByte(value, 1) >> 7) + ((DmConvertion.GetByte(value, 2) & 7) << 1));
			b2 = (byte)((DmConvertion.GetByte(value, 2) & 0xF8) >> 3);
			b3 = (byte)(DmConvertion.GetByte(value, 3) & 0x1F);
			b4 = (byte)((DmConvertion.GetByte(value, 3) >> 5) + ((DmConvertion.GetByte(value, 4) & 7) << 3));
			b5 = (byte)((DmConvertion.GetByte(value, 4) >> 3) + ((DmConvertion.GetByte(value, 5) & 1) << 5));
			num2 = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0xFF) << 15) + ((value[8] & 0x7F) << 23);
			break;
		case 27:
			num = (short)(DmConvertion.GetShort(value, 0) & 0x7FFF);
			if (num > 9999)
			{
				num = (short)(num | 0x8000);
			}
			b = (byte)((DmConvertion.GetByte(value, 1) >> 7) + ((DmConvertion.GetByte(value, 2) & 7) << 1));
			b2 = (byte)((DmConvertion.GetByte(value, 2) & 0xF8) >> 3);
			b3 = (byte)(DmConvertion.GetByte(value, 3) & 0x1F);
			b4 = (byte)((DmConvertion.GetByte(value, 3) >> 5) + ((DmConvertion.GetByte(value, 4) & 7) << 3));
			b5 = (byte)((DmConvertion.GetByte(value, 4) >> 3) + ((DmConvertion.GetByte(value, 5) & 1) << 5));
			num2 = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0xFF) << 15) + ((value[8] & 0x7F) << 23);
			break;
		}
		DmConvertion.SetShort(array, 0, num);
		array[2] = b;
		array[3] = b2;
		array[4] = b3;
		array[5] = b4;
		array[6] = b5;
		if (num2 > 0)
		{
			string text = num2.ToString();
			num2 = int.Parse(text.Substring(0, 7 - Math.Max(0, 9 - text.Length)));
		}
		array[7] = (byte)(num2 & 0xFF);
		array[8] = (byte)((num2 >> 8) & 0xFF);
		array[9] = (byte)((num2 >> 16) & 0xFF);
		return array;
	}

	public static byte[] DmTimeFromRec4(byte[] dateTime, int CType)
	{
		switch (CType)
		{
		case 16:
		case 26:
			return DmDtDecodeFast(dateTime);
		case 23:
		case 27:
		{
			byte[] array2 = DmDtDecodeFast(dateTime);
			if (dateTime.Length == 10)
			{
				array2[10] = dateTime[8];
				array2[11] = dateTime[9];
			}
			else
			{
				array2[10] = dateTime[9];
				array2[11] = dateTime[10];
			}
			return array2;
		}
		case 14:
			return DmDateDecodeFast(dateTime);
		case 15:
			return DmTimeDecodeFast(dateTime);
		case 22:
		{
			byte[] array = DmTimeDecodeFast(dateTime);
			array[10] = dateTime[5];
			array[11] = dateTime[6];
			return array;
		}
		default:
			throw new InvalidCastException();
		}
	}

	public static byte[] DmdtEncodeFast(byte[] dateTime)
	{
		if (dateTime.Length == 8)
		{
			return dateTime;
		}
		byte[] array = new byte[8];
		short num = DmConvertion.GetShort(dateTime, 0);
		byte b = dateTime[2];
		byte b2 = dateTime[3];
		array[0] = dateTime[0];
		if (num >= 0)
		{
			array[1] = (byte)((num >> 8) | ((b & 1) << 7));
		}
		else
		{
			array[1] = (byte)((num >> 8) & (((b & 1) << 7) | 0x7F));
		}
		array[2] = (byte)(((b & 0xE) >> 1) | (b2 << 3));
		byte b3 = dateTime[4];
		byte b4 = dateTime[5];
		byte b5 = dateTime[6];
		int num2 = dateTime[7] + ((dateTime[8] & 0xFF) << 8) + ((dateTime[9] & 0xFF) << 16);
		array[3] = (byte)(b3 | ((b4 & 7) << 5));
		array[4] = (byte)(((b4 & 0x38) >> 3) | ((b5 & 0x1F) << 3));
		array[5] = (byte)(((b5 & 0x20) >> 5) | ((num2 & 0x7F) << 1));
		array[6] = (byte)((num2 >> 7) & 0xFF);
		array[7] = (byte)((num2 >> 15) & 0xFF);
		return array;
	}

	public static byte[] DmdtEncodeFast(int[] dt)
	{
		int num = dt[0];
		int num2 = dt[1];
		int num3 = dt[2];
		int num4 = dt[3];
		int num5 = dt[4];
		int num6 = dt[5];
		int num7 = dt[6];
		byte[] array = new byte[8]
		{
			(byte)(num & 0xFF),
			0,
			0,
			0,
			0,
			0,
			0,
			0
		};
		if (num >= 0)
		{
			array[1] = (byte)((num >> 8) | ((num2 & 1) << 7));
		}
		else
		{
			array[1] = (byte)((num >> 8) & (((num2 & 1) << 7) | 0x7F));
		}
		array[2] = (byte)(((num2 & 0xE) >> 1) | (num3 << 3));
		array[3] = (byte)(num4 | ((num5 & 7) << 5));
		array[4] = (byte)(((num5 & 0x38) >> 3) | ((num6 & 0x1F) << 3));
		array[5] = (byte)(((num6 & 0x20) >> 5) | ((num7 & 0x7F) << 1));
		array[6] = (byte)((num7 >> 7) & 0xFF);
		array[7] = (byte)((num7 >> 15) & 0xFF);
		return array;
	}

	public static void DmdtEncodeFast(ref byte[] ret, ref byte[] dateTime)
	{
		ret = new byte[8];
		short num = DmConvertion.GetShort(dateTime, 0);
		byte b = dateTime[2];
		byte b2 = dateTime[3];
		ret[0] = dateTime[0];
		if (num >= 0)
		{
			ret[1] = (byte)((num >> 8) | ((b & 1) << 7));
		}
		else
		{
			ret[1] = (byte)((num >> 8) & (((b & 1) << 7) | 0x7F));
		}
		ret[2] = (byte)(((b & 0xE) >> 1) | (b2 << 3));
		byte b3 = dateTime[4];
		byte b4 = dateTime[5];
		byte b5 = dateTime[6];
		int num2 = dateTime[7] + ((dateTime[8] & 0xFF) << 8) + ((dateTime[9] & 0xFF) << 16);
		ret[3] = (byte)(b3 | ((b4 & 7) << 5));
		ret[4] = (byte)(((b4 & 0x38) >> 3) | ((b5 & 0x1F) << 3));
		ret[5] = (byte)(((b5 & 0x20) >> 5) | ((num2 & 0x7F) << 1));
		ret[6] = (byte)((num2 >> 7) & 0xFF);
		ret[7] = (byte)((num2 >> 15) & 0xFF);
	}

	public static void DmdtEncodeFast2(ref byte[] ret, DateTime x)
	{
		int year = x.Year;
		int month = x.Month;
		int day = x.Day;
		int hour = x.Hour;
		int minute = x.Minute;
		int second = x.Second;
		int nanoSecond = GetNanoSecond(x);
		ret = new byte[9];
		ret[0] = (byte)(year & 0xFF);
		if (year >= 0)
		{
			ret[1] = (byte)((year >> 8) | ((month & 1) << 7));
		}
		else
		{
			ret[1] = (byte)((year >> 8) & (((month & 1) << 7) | 0x7F));
		}
		ret[2] = (byte)(((month & 0xE) >> 1) | (day << 3));
		ret[3] = (byte)(hour | ((minute & 7) << 5));
		ret[4] = (byte)(((minute & 0x38) >> 3) | ((second & 0x1F) << 3));
		ret[5] = (byte)(((second & 0x20) >> 5) | ((nanoSecond & 0x7F) << 1));
		ret[6] = (byte)((nanoSecond >> 7) & 0xFF);
		ret[7] = (byte)((nanoSecond >> 15) & 0xFF);
		ret[8] = (byte)((nanoSecond >> 23) & 0xFF);
	}

	public static void Dmdt2TzEncodeFast2(ref byte[] ret, DateTime x, short timezone)
	{
		int year = x.Year;
		int month = x.Month;
		int day = x.Day;
		int hour = x.Hour;
		int minute = x.Minute;
		int second = x.Second;
		int nanoSecond = GetNanoSecond(x);
		ret = new byte[11];
		ret[0] = (byte)(year & 0xFF);
		if (year >= 0)
		{
			ret[1] = (byte)((year >> 8) | ((month & 1) << 7));
		}
		else
		{
			ret[1] = (byte)((year >> 8) & (((month & 1) << 7) | 0x7F));
		}
		ret[2] = (byte)(((month & 0xE) >> 1) | (day << 3));
		ret[3] = (byte)(hour | ((minute & 7) << 5));
		ret[4] = (byte)(((minute & 0x38) >> 3) | ((second & 0x1F) << 3));
		ret[5] = (byte)(((second & 0x20) >> 5) | ((nanoSecond & 0x7F) << 1));
		ret[6] = (byte)((nanoSecond >> 7) & 0xFF);
		ret[7] = (byte)((nanoSecond >> 15) & 0xFF);
		ret[8] = (byte)((nanoSecond >> 23) & 0xFF);
		byte[] array = DmConvertion.ShortToByteArray(timezone);
		ret[9] = array[0];
		ret[10] = array[1];
	}

	public static byte[] DmdttzEncodeFast(byte[] dateTime)
	{
		byte[] array = new byte[10];
		short num = DmConvertion.GetShort(dateTime, 0);
		byte b = dateTime[2];
		byte b2 = dateTime[3];
		array[0] = dateTime[0];
		if (num >= 0)
		{
			array[1] = (byte)((num >> 8) | ((b & 1) << 7));
		}
		else
		{
			array[1] = (byte)((num >> 8) & (((b & 1) << 7) | 0x7F));
		}
		array[2] = (byte)(((b & 0xE) >> 1) | (b2 << 3));
		byte b3 = dateTime[4];
		byte b4 = dateTime[5];
		byte b5 = dateTime[6];
		int num2 = dateTime[7] + ((dateTime[8] & 0xFF) << 8) + ((dateTime[9] & 0xFF) << 16);
		array[3] = (byte)(b3 | ((b4 & 7) << 5));
		array[4] = (byte)(((b4 & 0x38) >> 3) | ((b5 & 0x1F) << 3));
		array[5] = (byte)(((b5 & 0x20) >> 5) | ((num2 & 0x7F) << 1));
		array[6] = (byte)((num2 >> 7) & 0xFF);
		array[7] = (byte)((num2 >> 15) & 0xFF);
		array[8] = dateTime[10];
		array[9] = dateTime[11];
		return array;
	}

	public static byte[] DmdttzEncodeFast(int[] dt)
	{
		int num = dt[0];
		int num2 = dt[1];
		int num3 = dt[2];
		int num4 = dt[3];
		int num5 = dt[4];
		int num6 = dt[5];
		int num7 = dt[6];
		byte[] array = DmConvertion.ShortToByteArray((dt[OracleDateFormat.OFFSET_TIMEZONE_SIGN] > 0) ? (-1 * dt[7]) : dt[7]);
		byte[] array2 = new byte[10]
		{
			(byte)(num & 0xFF),
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
		if (num >= 0)
		{
			array2[1] = (byte)((num >> 8) | ((num2 & 1) << 7));
		}
		else
		{
			array2[1] = (byte)((num >> 8) & (((num2 & 1) << 7) | 0x7F));
		}
		array2[2] = (byte)(((num2 & 0xE) >> 1) | (num3 << 3));
		array2[3] = (byte)(num4 | ((num5 & 7) << 5));
		array2[4] = (byte)(((num5 & 0x38) >> 3) | ((num6 & 0x1F) << 3));
		array2[5] = (byte)(((num6 & 0x20) >> 5) | ((num7 & 0x7F) << 1));
		array2[6] = (byte)((num7 >> 7) & 0xFF);
		array2[7] = (byte)((num7 >> 15) & 0xFF);
		array2[8] = array[0];
		array2[9] = array[1];
		return array2;
	}

	public static byte[] DateEncodeFast(byte[] dateTime)
	{
		byte[] array = new byte[3];
		if (dateTime.Length == 3)
		{
			return dateTime;
		}
		short num = DmConvertion.GetShort(dateTime, 0);
		byte b = dateTime[2];
		byte b2 = dateTime[3];
		if (RevertToGlgl(num, b, b2))
		{
			b2 = 15;
		}
		array[0] = dateTime[0];
		if (num >= 0)
		{
			array[1] = (byte)((num >> 8) | ((b & 1) << 7));
		}
		else
		{
			array[1] = (byte)((num >> 8) & (((b & 1) << 7) | 0x7F));
		}
		array[2] = (byte)(((b & 0xE) >> 1) | (b2 << 3));
		return array;
	}

	public static bool RevertToGlgl(short year, byte month, byte day)
	{
		if (year == 1582 && month == 10 && 5 <= day)
		{
			return day <= 14;
		}
		return false;
	}

	public static byte[] DateEncodeFast(int[] dt)
	{
		byte[] array = new byte[3];
		int num = dt[0];
		int num2 = dt[1];
		int num3 = dt[2];
		array[0] = (byte)(num & 0xFF);
		if (num >= 0)
		{
			array[1] = (byte)((num >> 8) | ((num2 & 1) << 7));
		}
		else
		{
			array[1] = (byte)((num >> 8) & (((num2 & 1) << 7) | 0x7F));
		}
		array[2] = (byte)(((num2 & 0xE) >> 1) | (num3 << 3));
		return array;
	}

	internal static DmDateTime valueOf(byte[] bytes, DmField column, DmConnection connection)
	{
		DmDateTime dmDateTime = new DmDateTime();
		dmDateTime.decode(bytes, column, connection.GetConnInstance().ConnProperty.TimeZone, connection.GetConnInstance().ConnProperty.DbTimeZone);
		return dmDateTime;
	}

	public static DmDateTime valueOf(string str)
	{
		DmDateTime dmDateTime = new DmDateTime();
		dmDateTime.parseString(str);
		return dmDateTime;
	}

	internal static DmDateTime valueOf(string str, DmField column, DmConnection connection)
	{
		DmDateTime dmDateTime = new DmDateTime();
		string format = connection.getFormat(column);
		if (StringUtil.isNotEmpty(format))
		{
			dmDateTime.dt = OracleDateFormat.Parse(str, format, connection.GetConnInstance().ConnProperty.nlsDateLang);
			dmDateTime.dtype = column.type;
			dmDateTime.scale = column.scale;
			return dmDateTime;
		}
		dmDateTime.parseString(str);
		return dmDateTime;
	}

	private string toString(int dtype, int scale)
	{
		switch (dtype)
		{
		case 14:
			return DateUtil.formatYear(dt[0]) + "-" + DateUtil.format2(dt[1]) + "-" + DateUtil.format2(dt[2]);
		case 15:
			if (scale > 0)
			{
				return DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + "." + DateUtil.formatMilliSecond(dt[6], scale);
			}
			return DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]);
		case 22:
			if (scale > 0)
			{
				return DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + "." + DateUtil.formatMilliSecond(dt[6], scale) + " " + DateUtil.formatTZ(dt[7]);
			}
			return DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + " " + DateUtil.formatTZ(dt[7]);
		case 16:
		case 26:
			if (scale > 0)
			{
				return DateUtil.formatYear(dt[0]) + "-" + DateUtil.format2(dt[1]) + "-" + DateUtil.format2(dt[2]) + " " + DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + "." + DateUtil.formatMilliSecond(dt[6], scale);
			}
			return DateUtil.formatYear(dt[0]) + "-" + DateUtil.format2(dt[1]) + "-" + DateUtil.format2(dt[2]) + " " + DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]);
		case 23:
		case 27:
			if (scale > 0)
			{
				return DateUtil.formatYear(dt[0]) + "-" + DateUtil.format2(dt[1]) + "-" + DateUtil.format2(dt[2]) + " " + DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + "." + DateUtil.formatMilliSecond(dt[6], scale) + " " + DateUtil.formatTZ(dt[7]);
			}
			return DateUtil.formatYear(dt[0]) + "-" + DateUtil.format2(dt[1]) + "-" + DateUtil.format2(dt[2]) + " " + DateUtil.format2(dt[3]) + ":" + DateUtil.format2(dt[4]) + ":" + DateUtil.format2(dt[5]) + " " + DateUtil.formatTZ(dt[7]);
		default:
			return "";
		}
	}

	internal string toString(DmField column, DmConnection connection)
	{
		string format = connection.getFormat(column);
		if (StringUtil.isNotEmpty(format))
		{
			return OracleDateFormat.Format(dt, format, connection.GetConnInstance().ConnProperty.nlsDateLang, connection.GetConnInstance().ConnProperty.formatNumericChars);
		}
		int num = ((column.mask == 1) ? 14 : column.type);
		return toString(num, column.scale);
	}

	internal DateTime toDate(DmField column, DmConnection connection)
	{
		return Convert.ToDateTime(toString(14, column.scale));
	}

	internal DateTime toTime(DmField column, DmConnection connection)
	{
		return Convert.ToDateTime(toString(15, column.scale));
	}

	internal DateTime toTimestamp(DmField column, DmConnection connection)
	{
		return Convert.ToDateTime(toString(16, column.scale));
	}

	internal int[] decode(byte[] value, DmField column, int ltz, int dbtz)
	{
		if (column.isBdta)
		{
			dt = dmdtDecodeBdta(value);
		}
		else
		{
			dt = dmdtDecodeFast(value);
		}
		if (column.mask == 4)
		{
			dt = transformTZ(dt, dbtz, ltz);
		}
		dtype = column.type;
		scale = column.scale;
		return dt;
	}

	private static int[] dmdtDecodeFast(byte[] value)
	{
		int[] array = new int[8] { 0, 0, 0, 0, 0, 0, 0, -2147483648 };
		int num = 0;
		if (value.Length == 3)
		{
			num = 14;
		}
		else if (value.Length == 5)
		{
			num = 15;
		}
		else if (value.Length == 7)
		{
			num = 22;
		}
		else if (value.Length == 8)
		{
			num = 16;
		}
		else if (value.Length == 9)
		{
			num = 26;
		}
		else if (value.Length == 10)
		{
			num = 23;
		}
		else if (value.Length == 11)
		{
			num = 27;
		}
		switch (num)
		{
		case 14:
			array[0] = ByteUtil.getShort(value, 0) & 0x7FFF;
			if (array[0] > 9999)
			{
				array[0] = (short)((short)array[0] | 0x8000);
			}
			array[1] = ((value[1] >> 7) & 1) + ((value[2] & 7) << 1);
			array[2] = ((value[2] & 0xF8) >> 3) & 0x1F;
			break;
		case 15:
			array[3] = (short)(value[0] & 0x1F);
			array[4] = (short)(((value[0] >> 5) & 7) + ((value[1] & 7) << 3));
			array[5] = (short)(((value[1] >> 3) & 0x1F) + ((value[2] & 1) << 5));
			array[6] = ((value[2] >> 1) & 0x7F) + ((value[3] & 0xFF) << 7) + ((value[4] & 0x1F) << 15);
			array[6] *= 1000;
			break;
		case 22:
			array[3] = (short)(value[0] & 0x1F);
			array[4] = (short)(((value[0] >> 5) & 7) + ((value[1] & 7) << 3));
			array[5] = (short)(((value[1] >> 3) & 0x1F) + ((value[2] & 1) << 5));
			array[6] = ((value[2] >> 1) & 0x7F) + ((value[3] & 0xFF) << 7) + ((value[4] & 0x1F) << 15);
			array[6] *= 1000;
			array[7] = ByteUtil.getShort(value, 5);
			break;
		case 16:
			array[0] = ByteUtil.getShort(value, 0) & 0x7FFF;
			if (array[0] > 9999)
			{
				array[0] = (short)((short)array[0] | 0x8000);
			}
			array[1] = ((value[1] >> 7) & 1) + ((value[2] & 7) << 1);
			array[2] = ((value[2] & 0xF8) >> 3) & 0x1F;
			array[3] = value[3] & 0x1F;
			array[4] = ((value[3] >> 5) & 7) + ((value[4] & 7) << 3);
			array[5] = ((value[4] >> 3) & 0x1F) + ((value[5] & 1) << 5);
			array[6] = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0x1F) << 15);
			array[6] *= 1000;
			break;
		case 23:
			array[0] = ByteUtil.getShort(value, 0) & 0x7FFF;
			if (array[0] > 9999)
			{
				array[0] = (short)((short)array[0] | 0x8000);
			}
			array[1] = ((value[1] >> 7) & 1) + ((value[2] & 7) << 1);
			array[2] = ((value[2] & 0xF8) >> 3) & 0x1F;
			array[3] = value[3] & 0x1F;
			array[4] = ((value[3] >> 5) & 7) + ((value[4] & 7) << 3);
			array[5] = ((value[4] >> 3) & 0x1F) + ((value[5] & 1) << 5);
			array[6] = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0x1F) << 15);
			array[6] *= 1000;
			array[7] = ByteUtil.getShort(value, value.Length - 2);
			break;
		case 26:
			array[0] = ByteUtil.getShort(value, 0) & 0x7FFF;
			if (array[0] > 9999)
			{
				array[0] = (short)((short)array[0] | 0x8000);
			}
			array[1] = ((value[1] >> 7) & 1) + ((value[2] & 7) << 1);
			array[2] = ((value[2] & 0xF8) >> 3) & 0x1F;
			array[3] = value[3] & 0x1F;
			array[4] = ((value[3] >> 5) & 7) + ((value[4] & 7) << 3);
			array[5] = ((value[4] >> 3) & 0x1F) + ((value[5] & 1) << 5);
			array[6] = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0xFF) << 15) + ((value[8] & 0x7F) << 23);
			break;
		case 27:
			array[0] = ByteUtil.getShort(value, 0) & 0x7FFF;
			if (array[0] > 9999)
			{
				array[0] = (short)((short)array[0] | 0x8000);
			}
			array[1] = ((value[1] >> 7) & 1) + ((value[2] & 7) << 1);
			array[2] = ((value[2] & 0xF8) >> 3) & 0x1F;
			array[3] = value[3] & 0x1F;
			array[4] = ((value[3] >> 5) & 7) + ((value[4] & 7) << 3);
			array[5] = ((value[4] >> 3) & 0x1F) + ((value[5] & 1) << 5);
			array[6] = ((value[5] >> 1) & 0x7F) + ((value[6] & 0xFF) << 7) + ((value[7] & 0xFF) << 15) + ((value[8] & 0x7F) << 23);
			array[7] = ByteUtil.getShort(value, value.Length - 2);
			break;
		}
		return array;
	}

	private static int[] dmdtDecodeBdta(byte[] value)
	{
		int[] array = new int[8]
		{
			ByteUtil.getShort(value, 0),
			(short)(value[2] & 0xFF),
			(short)(value[3] & 0xFF),
			(short)(value[4] & 0xFF),
			(short)(value[5] & 0xFF),
			(short)(value[6] & 0xFF),
			(value[7] & 0xFF) + (value[8] << 8) + (value[9] << 16),
			ByteUtil.getShort(value, 10)
		};
		if (value.Length > 12)
		{
			array[6] += value[12] << 24;
		}
		return array;
	}

	internal byte[] encode(DmField column, DmConnection connection)
	{
		int[] array = dt;
		int type = dtype;
		int num = scale;
		if (column != null)
		{
			if ((column.scale & 0x1000) != 0)
			{
				column.scale &= -4097;
				column.mask = 4;
			}
			else if ((column.scale & 0x2000) != 0)
			{
				column.scale &= -8193;
				column.mask = 1;
			}
			type = column.type;
			num = column.scale;
		}
		if (column != null && column.mask == 4)
		{
			array = transformTZ(array, connection.GetConnInstance().ConnProperty.TimeZone, connection.GetConnInstance().ConnProperty.DbTimeZone);
		}
		array = roundHalfup(array, type, num);
		if (array[0] < -4712 || array[0] > 9999)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATETIME_OVERFLOW);
		}
		int num2 = array[0];
		int num3 = array[1];
		int num4 = array[2];
		int num5 = array[3];
		int num6 = array[4];
		int num7 = array[5];
		int num8 = array[6];
		int num9 = ((array[7] == int.MinValue && connection != null) ? connection.GetConnInstance().ConnProperty.TimeZone : array[7]);
		byte[] array2 = null;
		switch (type)
		{
		case 14:
			array2 = new byte[3]
			{
				(byte)(num2 & 0xFF),
				0,
				0
			};
			if (num2 >= 0)
			{
				array2[1] = (byte)((num2 >> 8) | ((num3 & 1) << 7));
			}
			else
			{
				array2[1] = (byte)((num2 >> 8) & (((num3 & 1) << 7) | 0x7F));
			}
			array2[2] = (byte)(((num3 & 0xE) >> 1) | (num4 << 3));
			break;
		case 16:
			num8 /= 1000;
			array2 = new byte[8]
			{
				(byte)(num2 & 0xFF),
				0,
				0,
				0,
				0,
				0,
				0,
				0
			};
			if (num2 >= 0)
			{
				array2[1] = (byte)((num2 >> 8) | ((num3 & 1) << 7));
			}
			else
			{
				array2[1] = (byte)((num2 >> 8) & (((num3 & 1) << 7) | 0x7F));
			}
			array2[2] = (byte)(((num3 & 0xE) >> 1) | (num4 << 3));
			array2[3] = (byte)(num5 | ((num6 & 7) << 5));
			array2[4] = (byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3));
			array2[5] = (byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1));
			array2[6] = (byte)((num8 >> 7) & 0xFF);
			array2[7] = (byte)((num8 >> 15) & 0xFF);
			break;
		case 26:
			array2 = new byte[9]
			{
				(byte)(num2 & 0xFF),
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0
			};
			if (num2 >= 0)
			{
				array2[1] = (byte)((num2 >> 8) | ((num3 & 1) << 7));
			}
			else
			{
				array2[1] = (byte)((num2 >> 8) & (((num3 & 1) << 7) | 0x7F));
			}
			array2[2] = (byte)(((num3 & 0xE) >> 1) | (num4 << 3));
			array2[3] = (byte)(num5 | ((num6 & 7) << 5));
			array2[4] = (byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3));
			array2[5] = (byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1));
			array2[6] = (byte)((num8 >> 7) & 0xFF);
			array2[7] = (byte)((num8 >> 15) & 0xFF);
			array2[8] = (byte)((num8 >> 23) & 0xFF);
			break;
		case 23:
			num8 /= 1000;
			array2 = new byte[10]
			{
				(byte)(num2 & 0xFF),
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
			if (num2 >= 0)
			{
				array2[1] = (byte)((num2 >> 8) | ((num3 & 1) << 7));
			}
			else
			{
				array2[1] = (byte)((num2 >> 8) & (((num3 & 1) << 7) | 0x7F));
			}
			array2[2] = (byte)(((num3 & 0xE) >> 1) | (num4 << 3));
			array2[3] = (byte)(num5 | ((num6 & 7) << 5));
			array2[4] = (byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3));
			array2[5] = (byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1));
			array2[6] = (byte)((num8 >> 7) & 0xFF);
			array2[7] = (byte)((num8 >> 15) & 0xFF);
			ByteUtil.setShort(array2, 8, (short)num9);
			break;
		case 27:
			array2 = new byte[11]
			{
				(byte)(num2 & 0xFF),
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
			if (num2 >= 0)
			{
				array2[1] = (byte)((num2 >> 8) | ((num3 & 1) << 7));
			}
			else
			{
				array2[1] = (byte)((num2 >> 8) & (((num3 & 1) << 7) | 0x7F));
			}
			array2[2] = (byte)(((num3 & 0xE) >> 1) | (num4 << 3));
			array2[3] = (byte)(num5 | ((num6 & 7) << 5));
			array2[4] = (byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3));
			array2[5] = (byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1));
			array2[6] = (byte)((num8 >> 7) & 0xFF);
			array2[7] = (byte)((num8 >> 15) & 0xFF);
			array2[8] = (byte)((num8 >> 23) & 0xFF);
			ByteUtil.setShort(array2, 9, (short)num9);
			break;
		case 15:
			num8 /= 1000;
			array2 = new byte[5]
			{
				(byte)(num5 | ((num6 & 7) << 5)),
				(byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3)),
				(byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1)),
				(byte)((num8 >> 7) & 0xFF),
				(byte)((num8 >> 15) & 0xFF)
			};
			break;
		case 22:
			num8 /= 1000;
			array2 = new byte[7]
			{
				(byte)(num5 | ((num6 & 7) << 5)),
				(byte)(((num6 & 0x38) >> 3) | ((num7 & 0x1F) << 3)),
				(byte)(((num7 & 0x20) >> 5) | ((num8 & 0x7F) << 1)),
				(byte)((num8 >> 7) & 0xFF),
				(byte)((num8 >> 15) & 0xFF),
				0,
				0
			};
			ByteUtil.setShort(array2, 5, (short)num9);
			break;
		}
		return array2;
	}

	public int parseString(string s)
	{
		string extraInfo = s;
		try
		{
			string text = null;
			string text2 = null;
			string text3 = null;
			string text4 = null;
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			int num8 = 0;
			int num9 = 0;
			int num10 = 0;
			int num11 = 0;
			int num12 = int.MinValue;
			if (s == null)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
			}
			s = s.Trim();
			if (s.StartsWith("-", StringComparison.CurrentCulture))
			{
				s = s.Substring(1).Trim();
				num11 = 1;
			}
			string[] array = s.Split(' ');
			switch (array.Length)
			{
			case 3:
				text = array[0];
				text2 = array[1];
				text4 = array[2];
				dtype = 23;
				break;
			case 2:
				if (array[0].IndexOf(":") > 0)
				{
					text2 = array[0];
					text4 = array[1];
					dtype = 22;
				}
				else
				{
					text = array[0];
					text2 = array[1];
					dtype = 16;
				}
				break;
			case 1:
				if (array[0].IndexOf(":") > 0)
				{
					text2 = array[0];
					dtype = 15;
				}
				else
				{
					text = array[0];
					dtype = 14;
				}
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
				break;
			}
			if (text != null)
			{
				int num13 = text.IndexOf('-');
				int num14 = text.IndexOf('-', num13 + 1);
				if (num13 < 0 || num14 < 0)
				{
					num13 = s.IndexOf('.');
					num14 = s.IndexOf('.', num13 + 1);
				}
				if (num13 < 0 || num14 < 0)
				{
					num13 = s.IndexOf('/');
					num14 = s.IndexOf('/', num13 + 1);
				}
				if ((num13 > 0 && num14 > 0) & (num14 < text.Length - 1))
				{
					num = ((num11 != 1) ? (Convert.ToInt32(text.Substring(0, num13)) - 1900) : (-Convert.ToInt32(text.Substring(0, num13)) - 1900));
					num2 = Convert.ToInt32(text.Substring(num13 + 1, num14 - (num13 + 1))) - 1;
					num3 = Convert.ToInt32(text.Substring(num14 + 1));
					if (!DateUtil.checkDate(num + 1900, num2 + 1, num3))
					{
						DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
					}
				}
				else
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
				}
			}
			if (text2 != null)
			{
				num8 = text2.IndexOf(':');
				num9 = text2.IndexOf(':', num8 + 1);
				num10 = text2.IndexOf('.', num9 + 1);
				if ((num8 > 0 && num9 > 0) & (num9 < text2.Length - 1))
				{
					num4 = Convert.ToInt32(text2.Substring(0, num8));
					num5 = Convert.ToInt32(text2.Substring(num8 + 1, num9 - (num8 + 1)));
					if ((num10 > 0) & (num10 < text2.Length - 1))
					{
						num6 = Convert.ToInt32(text2.Substring(num9 + 1, num10 - (num9 + 1)));
						text3 = text2.Substring(num10 + 1);
						if (text3.Length > 9)
						{
							DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
						}
						if (!char.IsDigit(text3[0]))
						{
							DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
						}
						text3 += "000000000".Substring(0, 9 - text3.Length);
						num7 = Convert.ToInt32(text3);
					}
					else if (num10 > 0)
					{
						DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
					}
					else
					{
						num6 = Convert.ToInt32(text2.Substring(num9 + 1));
					}
					if (num4 >= 24 || num4 < 0 || num5 >= 60 || num5 < 0 || num6 >= 60 || num6 < 0)
					{
						DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
					}
				}
				else
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
				}
			}
			if (text4 != null)
			{
				bool flag = false;
				if (text4.StartsWith("-"))
				{
					flag = true;
				}
				if (text4.StartsWith("-") || text4.StartsWith("+"))
				{
					text4 = text4.Substring(1).Trim();
				}
				string[] array2 = text4.Split(':');
				short num15 = 0;
				short num16 = 0;
				switch (array2.Length)
				{
				case 2:
					num15 = Convert.ToInt16(array2[0].Trim());
					num16 = Convert.ToInt16(array2[1].Trim());
					break;
				case 1:
					num15 = Convert.ToInt16(array2[0].Trim());
					break;
				default:
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
					break;
				}
				num12 = (short)(num15 * 60 + num16);
				if (num12 < 0)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
				}
				if (flag)
				{
					num12 *= -1;
				}
				if (num12 <= -780 || num12 > 840)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
				}
			}
			dt[0] = num + 1900;
			dt[1] = num2 + 1;
			dt[2] = ((num3 == 0) ? 1 : num3);
			dt[3] = num4;
			dt[4] = num5;
			dt[5] = num6;
			dt[6] = num7;
			dt[7] = num12;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DATETIME_FORMAT, extraInfo);
		}
		scale = ((dtype != 14) ? 6 : 0);
		return dtype;
	}

	public static int[] transformTZ(int[] dt, int defaultSrcTz, int destTz)
	{
		int num = defaultSrcTz;
		if (dt[7] != int.MinValue)
		{
			num = dt[7];
		}
		if (destTz == num)
		{
			return dt;
		}
		int[] destinationArray = new int[dt.Length];
		Array.Copy(dt, destinationArray, dt.Length);
		destinationArray = AddMinute(destinationArray, destTz - num);
		if (destinationArray[7] != int.MinValue)
		{
			destinationArray[7] = destTz;
		}
		return destinationArray;
	}

	private static int[] roundHalfup(int[] dt, int dtype, int scale)
	{
		if (((uint)(dtype - 15) <= 1u || (uint)(dtype - 22) <= 1u || (uint)(dtype - 26) <= 1u) && scale < 9)
		{
			int[] array = new int[dt.Length];
			Array.Copy(dt, array, dt.Length);
			int num = dt[6];
			int num2 = (int)Math.Pow(10.0, 9 - scale);
			int num3 = num / (int)Math.Pow(10.0, 9 - scale - 1) % 10;
			array[6] = num / num2 * num2;
			if (num3 > 4)
			{
				AddNanoSecond(array, num2);
			}
			return array;
		}
		return dt;
	}

	public static int[] AddNanoSecond(int[] dt, int n)
	{
		int num = dt[6] + n;
		int num2 = num / 1000000000;
		if ((num %= 1000000000) < 0)
		{
			num += 1000000000;
			num2--;
		}
		dt[6] = num;
		AddSecond(dt, num2);
		return dt;
	}

	public static int[] AddSecond(int[] dt, int n)
	{
		int num = dt[5] + n;
		int num2 = num / 60;
		if ((num %= 60) < 0)
		{
			num += 60;
			num2--;
		}
		dt[5] = num;
		AddMinute(dt, num2);
		return dt;
	}

	public static int[] AddMinute(int[] dt, int n)
	{
		int num = dt[4] + n;
		int num2 = num / 60;
		if ((num %= 60) < 0)
		{
			num += 60;
			num2--;
		}
		dt[4] = num;
		AddHour(dt, num2);
		return dt;
	}

	private static int[] AddHour(int[] dt, int n)
	{
		int num = dt[3] + n;
		int num2 = num / 24;
		if ((num %= 24) < 0)
		{
			num += 24;
			num2--;
		}
		dt[3] = num;
		AddDay(dt, num2);
		return dt;
	}

	private static int[] AddDay(int[] dt, int n)
	{
		int num = dt[2] + n;
		int num2 = 0;
		while (num > (num2 = DateUtil.getDaysOfMonth(dt[0], dt[1])) || num <= 0)
		{
			AddMonth(dt, (num > num2) ? 1 : (-1));
			num = ((num > num2) ? (num - num2) : (num + num2));
		}
		dt[2] = num;
		return dt;
	}

	private static int[] AddMonth(int[] dt, int n)
	{
		int num = dt[1] + n;
		int num2 = num / 12;
		if ((num %= 12) < 1)
		{
			num += 12;
			num2--;
		}
		int daysOfMonth = DateUtil.getDaysOfMonth(dt[0], num);
		if (dt[2] > daysOfMonth)
		{
			dt[2] = daysOfMonth;
		}
		dt[1] = num;
		AddYear(dt, num2);
		return dt;
	}

	private static int[] AddYear(int[] dt, int n)
	{
		dt[0] += n;
		return dt;
	}
}
