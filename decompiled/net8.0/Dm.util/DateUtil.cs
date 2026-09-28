using System;

namespace Dm.util;

public class DateUtil
{
	public const int DATE = 1;

	public const int DATE_TIME = 2;

	public const int YYYY_MM_DD_HH_MM_SS = 3;

	public static string formatMilliSecond(int ms, int prec)
	{
		string text = null;
		text = ((ms < 10) ? ("00000000" + ms) : ((ms < 100) ? ("0000000" + ms) : ((ms < 1000) ? ("000000" + ms) : ((ms < 10000) ? ("00000" + ms) : ((ms < 100000) ? ("0000" + ms) : ((ms < 1000000) ? ("000" + ms) : ((ms < 10000000) ? ("00" + ms) : ((ms >= 100000000) ? Convert.ToString(ms) : ("0" + ms)))))))));
		if (prec < 9)
		{
			text = text.Substring(0, prec);
		}
		return text;
	}

	public static string formatTZ(int tz)
	{
		int value = Math.Abs(tz / 60);
		int value2 = Math.Abs(tz % 60);
		if (tz >= 0)
		{
			return "+" + format2(value) + ":" + format2(value2);
		}
		return "-" + format2(value) + ":" + format2(value2);
	}

	public static string formatYear(int value)
	{
		if (value >= 0)
		{
			if (value < 10)
			{
				return "000" + value;
			}
			if (value < 100)
			{
				return "00" + value;
			}
			if (value < 1000)
			{
				return "0" + value;
			}
			return Convert.ToString(value);
		}
		if (value > -10)
		{
			return "-000" + -value;
		}
		if (value > -100)
		{
			return "-00" + -value;
		}
		if (value > -1000)
		{
			return "-0" + -value;
		}
		return Convert.ToString(value);
	}

	public static string format2(int value)
	{
		if (value < 10)
		{
			return "0" + value;
		}
		return Convert.ToString(value);
	}

	public static bool checkDate(int year, int month, int day)
	{
		if (year > 9999 || year < -4712 || month > 12 || month < 1)
		{
			return false;
		}
		int daysOfMonth = getDaysOfMonth(year, month);
		if (day > daysOfMonth || day < 1)
		{
			return false;
		}
		return true;
	}

	public static bool isLeapYear(int year)
	{
		if (year <= 1582)
		{
			return year % 4 == 0;
		}
		if (year % 4 != 0 || year % 100 == 0)
		{
			return year % 400 == 0;
		}
		return true;
	}

	public static int getDaysOfMonth(int year, int month)
	{
		switch (month)
		{
		case 1:
		case 3:
		case 5:
		case 7:
		case 8:
		case 10:
		case 12:
			return 31;
		case 4:
		case 6:
		case 9:
		case 11:
			return 30;
		case 2:
			if (!isLeapYear(year))
			{
				return 28;
			}
			return 29;
		default:
			return 0;
		}
	}

	public static string formatDate(DateTime date, int style)
	{
		int year = date.Year;
		int month = date.Month;
		string strFromInt = getStrFromInt(date.Day);
		string strFromInt2 = getStrFromInt(date.Hour);
		string strFromInt3 = getStrFromInt(date.Minute);
		string strFromInt4 = getStrFromInt(date.Second);
		return style switch
		{
			1 => year + 1900 + "-" + getStrFromInt(month + 1) + "-" + strFromInt, 
			2 => year + 1900 + "-" + getStrFromInt(month + 1) + "-" + strFromInt + " " + strFromInt2 + ":" + strFromInt3 + ":" + strFromInt4, 
			3 => year + 1900 + "_" + getStrFromInt(month + 1) + "_" + strFromInt + "_" + strFromInt2 + "_" + strFromInt3 + "_" + strFromInt4, 
			_ => date.ToString(), 
		};
	}

	private static string getStrFromInt(int i)
	{
		if (i >= 10)
		{
			return Convert.ToString(i);
		}
		return "0" + i;
	}
}
