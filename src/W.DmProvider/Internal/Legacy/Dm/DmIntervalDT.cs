using System;
using W.Dm.util;

namespace W.Dm;

public class DmIntervalDT
{
	public const byte QUA_Y = 0;

	public const byte QUA_YM = 1;

	public const byte QUA_MO = 2;

	public const byte QUA_D = 3;

	public const byte QUA_DH = 4;

	public const byte QUA_DHM = 5;

	public const byte QUA_DHMS = 6;

	public const byte QUA_H = 7;

	public const byte QUA_HM = 8;

	public const byte QUA_HMS = 9;

	public const byte QUA_M = 10;

	public const byte QUA_MS = 11;

	public const byte QUA_S = 12;

	public const int LEADSCALE_MAX = 9;

	private byte type = 3;

	private int leadScale = 2;

	private int secScale = 6;

	private bool negative;

	public int days;

	public int hours;

	public int minutes;

	public int seconds;

	public int fraction;

	private int scaleForSvr;

	public DmIntervalDT(int days, int hours, int minutes, int seconds, int millseconds)
	{
		this.days = days;
		this.hours = hours;
		this.minutes = minutes;
		this.seconds = seconds;
		fraction = millseconds * 1000;
		checkSignAndReset();
		type = 6;
	}

	private DmIntervalDT(byte type, bool negative, int days, int hours, int minutes, int seconds, int fraction, int scale)
	{
		this.type = type;
		this.negative = negative;
		leadScale = (scale >> 4) & 0xF;
		secScale = scale & 0xF;
		this.days = days;
		this.hours = hours;
		this.minutes = minutes;
		this.seconds = seconds;
		this.fraction = fraction;
		scaleForSvr = scale;
	}

	public DmIntervalDT(TimeSpan timeSpan, int scale)
	{
		days = timeSpan.Days;
		hours = timeSpan.Hours;
		minutes = timeSpan.Minutes;
		seconds = timeSpan.Seconds;
		string text = timeSpan.ToString();
		int num = text.LastIndexOf('.');
		int num2 = text.LastIndexOf(':');
		if (num > num2)
		{
			fraction = Convert.ToInt32(text.Substring(num + 1, 6));
			if (timeSpan.Milliseconds < 0)
			{
				fraction = -fraction;
			}
		}
		checkSignAndReset();
		secScale = scale & 0xF;
		leadScale = (scale >> 4) & 0xF;
		type = (byte)((scale >> 8) & 0xF);
		scaleForSvr = scale;
	}

	public DmIntervalDT(byte[] interval, int leadScale = 2, int secScale = 6)
	{
		reset(interval);
		this.leadScale = leadScale;
		this.secScale = secScale;
	}

	public DmIntervalDT(string str)
		: this(str, 6, 2, 6)
	{
		if (!str.ToUpper().StartsWith("INTERVAL"))
		{
			return;
		}
		string[] array = str.Split(' ');
		switch (array.Length)
		{
		case 3:
		{
			int num = array[2].IndexOf("(");
			int num2 = array[2].IndexOf(")");
			string text = array[2].Substring(num + 1, num2 - num - 1);
			switch (((num >= 0) ? array[2].Substring(0, num) : array[2]).ToLower())
			{
			case "second":
			{
				string[] array2 = text.Split(',');
				leadScale = Convert.ToInt32(array2[0].Trim());
				secScale = Convert.ToInt32(array2[1].Trim());
				break;
			}
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
				break;
			case "day":
			case "hour":
			case "minute":
				break;
			}
			break;
		}
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			break;
		case 5:
		case 6:
			break;
		}
		scaleForSvr = (type << 8) + (leadScale << 4) + secScale;
	}

	public DmIntervalDT(string str, byte type, int leadScale, int secScale)
	{
		if (string.IsNullOrEmpty(str))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
		}
		this.type = type;
		this.leadScale = leadScale;
		this.secScale = secScale;
		scaleForSvr = (this.type << 8) + (this.leadScale << 4) + this.secScale;
		if (!str.ToLower().StartsWith("interval"))
		{
			switch (type)
			{
			case 3:
				SetDay(str);
				break;
			case 4:
				SetDayToHour(str);
				break;
			case 5:
				SetDayToMinute(str);
				break;
			case 6:
				SetDayToSecond(str);
				break;
			case 7:
				SetHour(str);
				break;
			case 8:
				SetHourToMinute(str);
				break;
			case 9:
				SetHourToSecond(str);
				break;
			case 10:
				SetMinute(str);
				break;
			case 11:
				SetMinuteToSecond(str);
				break;
			case 12:
				SetSecond(str);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
				break;
			}
			return;
		}
		string[] array = str.Split(' ');
		switch (array.Length)
		{
		case 3:
		{
			checkStrHead(array[0]);
			int num5 = array[2].IndexOf("(");
			string text = ((num5 >= 0) ? array[2].Substring(0, num5) : array[2]);
			if (text.ToLower().Equals("day"))
			{
				SetDay(array[1]);
			}
			else if (text.ToLower().Equals("hour"))
			{
				SetHour(array[1]);
			}
			else if (text.ToLower().Equals("minute"))
			{
				SetMinute(array[1]);
			}
			else if (text.ToLower().Equals("second"))
			{
				SetSecond(array[1]);
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			break;
		}
		case 5:
		{
			checkStrHead(array[0]);
			int num3 = array[2].IndexOf("(");
			int num4 = array[4].IndexOf("(");
			string text = ((num3 >= 0) ? array[2].Substring(0, num3) : array[2]) + ((num4 >= 0) ? array[4].Substring(0, num4) : array[4]);
			if (text.ToLower().Equals("hoursecond"))
			{
				SetHourToSecond(array[1]);
			}
			else if (text.ToLower().Equals("hourminute"))
			{
				SetHourToMinute(array[1]);
			}
			else if (text.ToLower().Equals("minutesecond"))
			{
				SetMinuteToSecond(array[1]);
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			break;
		}
		case 6:
		{
			checkStrHead(array[0]);
			int num = array[3].IndexOf("(");
			int num2 = array[5].IndexOf("(");
			string text = ((num >= 0) ? array[3].Substring(0, num) : array[3]) + ((num2 >= 0) ? array[5].Substring(0, num2) : array[5]);
			if (text.ToLower().Equals("dayhour"))
			{
				SetDayToHour(array[1] + " " + array[2]);
			}
			else if (text.ToLower().Equals("dayminute"))
			{
				SetDayToMinute(array[1] + " " + array[2]);
			}
			else if (text.ToLower().Equals("daysecond"))
			{
				SetDayToSecond(array[1] + " " + array[2]);
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			break;
		}
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			break;
		}
	}

	private void checkSignAndReset()
	{
		negative = days < 0 || hours < 0 || minutes < 0 || seconds < 0 || fraction < 0;
		if (negative)
		{
			days = -days;
			hours = -hours;
			minutes = -minutes;
			seconds = -seconds;
			fraction = -fraction;
		}
	}

	public int getScaleForSvr()
	{
		return scaleForSvr;
	}

	private static void checkStrHead(string str)
	{
		if (!str.ToLower().StartsWith("interval"))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
		}
	}

	public void reset(byte[] interval)
	{
		type = interval[21];
		scaleForSvr = ByteUtil.getInt(interval, 20);
		leadScale = (scaleForSvr >> 4) & 0xF;
		secScale = scaleForSvr & 0xF;
		switch (type)
		{
		case 3:
			days = ByteUtil.getInt(interval, 0);
			break;
		case 4:
			days = ByteUtil.getInt(interval, 0);
			hours = ByteUtil.getInt(interval, 4);
			break;
		case 5:
			days = ByteUtil.getInt(interval, 0);
			hours = ByteUtil.getInt(interval, 4);
			minutes = ByteUtil.getInt(interval, 8);
			break;
		case 6:
			days = ByteUtil.getInt(interval, 0);
			hours = ByteUtil.getInt(interval, 4);
			minutes = ByteUtil.getInt(interval, 8);
			seconds = ByteUtil.getInt(interval, 12);
			fraction = ByteUtil.getInt(interval, 16);
			break;
		case 7:
			hours = ByteUtil.getInt(interval, 4);
			break;
		case 8:
			hours = ByteUtil.getInt(interval, 4);
			minutes = ByteUtil.getInt(interval, 8);
			break;
		case 9:
			hours = ByteUtil.getInt(interval, 4);
			minutes = ByteUtil.getInt(interval, 8);
			seconds = ByteUtil.getInt(interval, 12);
			fraction = ByteUtil.getInt(interval, 16);
			break;
		case 10:
			minutes = ByteUtil.getInt(interval, 8);
			break;
		case 11:
			minutes = ByteUtil.getInt(interval, 8);
			seconds = ByteUtil.getInt(interval, 12);
			fraction = ByteUtil.getInt(interval, 16);
			break;
		case 12:
			seconds = ByteUtil.getInt(interval, 12);
			fraction = ByteUtil.getInt(interval, 16);
			break;
		}
		checkSignAndReset();
	}

	private void SetDay(string value)
	{
		type = 3;
		value = getAbsValue(value);
		string[] array = value.Split(' ');
		days = Convert.ToInt32(array[0]);
	}

	private void SetDayToHour(string value)
	{
		type = 4;
		value = getAbsValue(value);
		string[] array = value.Split(' ');
		days = Convert.ToInt32(array[0]);
		if (array.Length > 1)
		{
			hours = Convert.ToInt32(array[1]);
		}
	}

	private void SetDayToMinute(string value)
	{
		type = 5;
		value = getAbsValue(value);
		string[] array = value.Split(' ', ':');
		days = Convert.ToInt32(array[0]);
		if (array.Length > 1)
		{
			hours = Convert.ToInt32(array[1]);
			if (array.Length > 2)
			{
				minutes = Convert.ToInt32(array[2]);
			}
		}
	}

	private void SetDayToSecond(string value)
	{
		type = 6;
		value = getAbsValue(value);
		string[] array = value.Split(' ', ':', '.');
		days = Convert.ToInt32(array[0]);
		if (array.Length <= 1)
		{
			return;
		}
		hours = Convert.ToInt32(array[1]);
		if (array.Length <= 2)
		{
			return;
		}
		minutes = Convert.ToInt32(array[2]);
		if (array.Length > 3)
		{
			seconds = Convert.ToInt32(array[3]);
			if (array.Length > 4)
			{
				SetNano(array[4]);
			}
		}
	}

	private void SetHour(string value)
	{
		type = 7;
		value = getAbsValue(value);
		string[] array = value.Split(' ');
		hours = Convert.ToInt32(array[0]);
	}

	private void SetHourToMinute(string value)
	{
		type = 8;
		value = getAbsValue(value);
		string[] array = value.Split(' ', ':');
		hours = Convert.ToInt32(array[0]);
		if (array.Length > 1)
		{
			minutes = Convert.ToInt32(array[1]);
		}
	}

	private void SetHourToSecond(string value)
	{
		type = 9;
		value = getAbsValue(value);
		string[] array = value.Split(' ', ':', '.');
		hours = Convert.ToInt32(array[0]);
		if (array.Length <= 1)
		{
			return;
		}
		minutes = Convert.ToInt32(array[1]);
		if (array.Length > 2)
		{
			seconds = Convert.ToInt32(array[2]);
			if (array.Length > 3)
			{
				SetNano(array[4]);
			}
		}
	}

	private void SetMinute(string value)
	{
		type = 10;
		value = getAbsValue(value);
		string[] array = value.Split(' ');
		minutes = Convert.ToInt32(array[0]);
	}

	private void SetMinuteToSecond(string value)
	{
		type = 11;
		value = getAbsValue(value);
		string[] array = value.Split(' ', ':', '.');
		minutes = Convert.ToInt32(array[0]);
		if (array.Length > 1)
		{
			seconds = Convert.ToInt32(array[1]);
			if (array.Length > 2)
			{
				SetNano(array[2]);
			}
		}
	}

	private void SetSecond(string value)
	{
		type = 12;
		value = getAbsValue(value);
		string[] array = value.Split(' ', '.');
		seconds = Convert.ToInt32(array[0]);
		if (array.Length > 1)
		{
			SetNano(array[1]);
		}
	}

	private void SetNano(string nano)
	{
		double num = Convert.ToDouble("0." + nano);
		int num2 = (int)Math.Pow(10.0, secScale);
		fraction = (int)(num * (double)num2);
	}

	private void checkStrValue(string str, int maxNum, params char[] seperators)
	{
		if (str.Split(seperators).Length > maxNum)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
		}
	}

	private string getAbsValue(string value)
	{
		if (value.StartsWith("'"))
		{
			value = value.Substring(1, value.Length - 2);
			if (value.StartsWith("-"))
			{
				negative = true;
				value = value.Substring(1);
			}
		}
		else if (value.StartsWith("-"))
		{
			negative = true;
			value = value.Substring(1);
			if (value.StartsWith("'"))
			{
				value = value.Substring(1, value.Length - 2);
			}
		}
		return value;
	}

	public int getDay()
	{
		return days;
	}

	public int getHour()
	{
		return hours;
	}

	public int getMinute()
	{
		return minutes;
	}

	public int getSecond()
	{
		return seconds;
	}

	public int getMsec()
	{
		return fraction;
	}

	public int GetDTType()
	{
		return type;
	}

	public string GetDTString()
	{
		string text = "INTERVAL '";
		if (negative)
		{
			text += "-";
		}
		switch (type)
		{
		case 3:
		{
			string text6 = formatStrByScale(getDay().ToString(), leadScale);
			text = text + text6 + "' DAY(" + leadScale + ")";
			break;
		}
		case 4:
		{
			string text6 = formatStrByScale(getDay().ToString(), leadScale);
			string text5 = formatStrByDefault(getHour().ToString());
			text = text + text6 + " " + text5 + "' DAY(" + leadScale + ") TO HOUR";
			break;
		}
		case 5:
		{
			string text6 = formatStrByScale(getDay().ToString(), leadScale);
			string text5 = formatStrByDefault(getHour().ToString());
			string text4 = formatStrByDefault(getMinute().ToString());
			text = text + text6 + " " + text5 + ":" + text4 + "' DAY(" + leadScale + ") TO MINUTE";
			break;
		}
		case 6:
		{
			string text6 = formatStrByScale(getDay().ToString(), leadScale);
			string text5 = formatStrByDefault(getHour().ToString());
			string text4 = formatStrByDefault(getMinute().ToString());
			string text2 = formatStrByDefault(getSecond().ToString());
			string text3 = formatStrByScale(getMsec().ToString(), secScale);
			text = text + text6 + " " + text5 + ":" + text4 + ":" + text2 + "." + text3 + "' DAY(" + leadScale + ") TO SECOND(" + secScale + ")";
			break;
		}
		case 7:
		{
			string text5 = formatStrByScale(getHour().ToString(), leadScale);
			text = text + text5 + "' HOUR(" + leadScale + ")";
			break;
		}
		case 8:
		{
			string text5 = formatStrByScale(getHour().ToString(), leadScale);
			string text4 = formatStrByDefault(getMinute().ToString());
			text = text + text5 + ":" + text4 + "' HOUR(" + leadScale + ") TO MINUTE";
			break;
		}
		case 9:
		{
			string text5 = formatStrByScale(getHour().ToString(), leadScale);
			string text4 = formatStrByDefault(getMinute().ToString());
			string text2 = formatStrByDefault(getSecond().ToString());
			string text3 = formatStrByScale(getMsec().ToString(), secScale);
			text = text + text5 + ":" + text4 + ":" + text2 + "." + text3 + "' HOUR(" + leadScale + ") TO SECOND(" + secScale + ")";
			break;
		}
		case 10:
		{
			string text4 = formatStrByScale(getMinute().ToString(), leadScale);
			text = text + text4 + "' MINUTE(" + leadScale + ")";
			break;
		}
		case 11:
		{
			string text4 = formatStrByScale(getMinute().ToString(), leadScale);
			string text2 = formatStrByDefault(getSecond().ToString());
			string text3 = formatStrByScale(getMsec().ToString(), secScale);
			text = text + text4 + ":" + text2 + "." + text3 + "' MINUTE(" + leadScale + ") TO SECOND(" + secScale + ")";
			break;
		}
		case 12:
		{
			string text2 = formatStrByScale(getSecond().ToString(), leadScale);
			string text3 = formatStrByScale(getMsec().ToString(), secScale);
			text = text + text2 + "." + text3 + "' SECOND(" + leadScale + "," + secScale + ")";
			break;
		}
		}
		return text;
	}

	private string formatStrByScale(string str, int scale)
	{
		while (str.Length < scale)
		{
			str = "0" + str;
		}
		if (str.Length > scale)
		{
			str = str.Substring(0, scale);
		}
		return str;
	}

	private string formatStrByDefault(string str)
	{
		if (str.Length < 2)
		{
			str = "0" + str;
		}
		return str;
	}

	public override string ToString()
	{
		return GetDTString();
	}

	public byte[] encode(int scale)
	{
		if (scale == 0)
		{
			scale = scaleForSvr;
		}
		if (scale != scaleForSvr)
		{
			DmIntervalDT dmIntervalDT = convertTo(scale);
			days = dmIntervalDT.days;
			hours = dmIntervalDT.hours;
			minutes = dmIntervalDT.minutes;
			seconds = dmIntervalDT.seconds;
			fraction = dmIntervalDT.fraction;
		}
		else
		{
			int num = (scale >> 4) & 0xF;
			checkScale(num);
		}
		if (type == 7)
		{
			ToHours();
		}
		else if (type == 10)
		{
			ToHours();
			ToMinutes();
		}
		else if (type == 12)
		{
			ToHours();
			ToMinutes();
			ToSeconds();
		}
		byte[] array = new byte[24];
		ByteUtil.setInt(array, 0, negative ? (-days) : days);
		ByteUtil.setInt(array, 4, negative ? (-hours) : hours);
		ByteUtil.setInt(array, 8, negative ? (-minutes) : minutes);
		ByteUtil.setInt(array, 12, negative ? (-seconds) : seconds);
		ByteUtil.setInt(array, 16, negative ? (-fraction) : fraction);
		ByteUtil.setInt(array, 20, scaleForSvr);
		return array;
	}

	public DmIntervalDT convertTo(int scale)
	{
		int num = (scale & 0xFF00) >> 8;
		int num2 = (scale >> 4) & 0xF;
		int destSecScale = scale & 0xF;
		int num3 = 0;
		int num4 = 1;
		int num5 = 2;
		int num6 = 3;
		int num7 = 4;
		long[] array = new long[5];
		long[] array2 = new long[5];
		switch (type)
		{
		case 3:
			array[num3] = days;
			break;
		case 4:
			array[num3] = days;
			array[num4] = hours;
			break;
		case 5:
			array[num3] = days;
			array[num4] = hours;
			array[num5] = minutes;
			break;
		case 6:
			array[num3] = days;
			array[num4] = hours;
			array[num5] = minutes;
			array[num6] = seconds;
			array[num7] = fraction;
			break;
		case 7:
			array[num3] = hours / 24;
			array[num4] = hours % 24;
			break;
		case 8:
			array[num3] = hours / 24;
			array[num4] = hours % 24;
			array[num5] = minutes;
			break;
		case 9:
			array[num3] = hours / 24;
			array[num4] = hours % 24;
			array[num5] = minutes;
			array[num6] = seconds;
			array[num7] = fraction;
			break;
		case 10:
			array[num3] = minutes / 1440;
			array[num4] = minutes % 1440 / 60;
			array[num5] = minutes % 1440 % 60;
			break;
		case 11:
			array[num3] = minutes / 1440;
			array[num4] = minutes % 1440 / 60;
			array[num5] = minutes % 1440 % 60;
			array[num6] = seconds;
			array[num7] = fraction;
			break;
		case 12:
			array[num3] = seconds / 86400;
			array[num4] = seconds % 86400 / 3600;
			array[num5] = seconds % 86400 % 3600 / 60;
			array[num6] = seconds % 86400 % 3600 % 60;
			array[num7] = fraction;
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			break;
		}
		switch (num)
		{
		case 3:
			array2[num3] = array[num3];
			if (array[num4] >= 12)
			{
				incrementDay(3, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num3])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 4:
			array2[num3] = array[num3];
			array2[num4] = array[num4];
			if (array[num5] >= 30)
			{
				incrementHour(4, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num3])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 5:
			array2[num3] = array[num3];
			array2[num4] = array[num4];
			array2[num5] = array[num5];
			if (array[num6] >= 30)
			{
				incrementMinute(5, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num3])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 6:
			array2[num3] = array[num3];
			array2[num4] = array[num4];
			array2[num5] = array[num5];
			array2[num6] = array[num6];
			array2[num7] = array[num7];
			convertMSecond(6, array2, destSecScale);
			if (num2 < Convert.ToString(Math.Abs(array2[num3])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 7:
			array2[num4] = array[num3] * 24 + array[num4];
			if (array[num5] >= 30)
			{
				incrementHour(7, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num4])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 8:
			array2[num4] = array[num3] * 24 + array[num4];
			array2[num5] = array[num5];
			if (array[num6] >= 30)
			{
				incrementMinute(8, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num4])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 9:
			array2[num4] = array[num3] * 24 + array[num4];
			array2[num5] = array[num5];
			array2[num6] = array[num6];
			array2[num7] = array[num7];
			convertMSecond(9, array2, destSecScale);
			if (num2 < Convert.ToString(Math.Abs(array2[num4])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 10:
			array2[num5] = array[num3] * 24 * 60 + array[num4] * 60 + array[num5];
			if (array[num6] >= 30)
			{
				incrementMinute(10, array2);
			}
			if (num2 < Convert.ToString(Math.Abs(array2[num5])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 11:
			array2[num5] = array[num3] * 24 * 60 + array[num4] * 60 + array[num5];
			array2[num6] = array[num6];
			array2[num7] = array[num7];
			convertMSecond(11, array2, destSecScale);
			if (num2 < Convert.ToString(Math.Abs(array2[num5])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 12:
			array2[num6] = array[num3] * 24 * 60 * 60 + array[num4] * 60 * 60 + array[num5] * 60 + array[num6];
			array2[num7] = array[num7];
			convertMSecond(12, array2, destSecScale);
			if (num2 < Convert.ToString(Math.Abs(array2[num6])).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			break;
		}
		return new DmIntervalDT((byte)num, negative, (int)array2[num3], (int)array2[num4], (int)array2[num5], (int)array2[num6], (int)array2[num7], scale);
	}

	private void convertMSecond(int destType, long[] destDT, int destSecScale)
	{
		int num = 4;
		long num2 = destDT[num];
		if (destSecScale != 0 && destSecScale >= secScale)
		{
			return;
		}
		long num3 = MathUtil.pow(10, 6 - destSecScale - 1);
		int num4 = (int)(num2 / num3 / 10);
		if (num2 / num3 % 10 >= 5)
		{
			num4++;
			num4 = (int)(num4 * num3 * 10);
			if (num4 == 1000000)
			{
				destDT[num] = 0L;
				incrementSecond(destType, destDT);
				return;
			}
		}
		destDT[num] = num4;
	}

	private void incrementDay(int destType, long[] dt)
	{
		int num = 0;
		dt[num]++;
	}

	private void incrementHour(int destType, long[] dt)
	{
		int num = 1;
		dt[num]++;
		if (dt[num] == 24 && destType < 7)
		{
			incrementDay(destType, dt);
			dt[num] = 0L;
		}
	}

	private void incrementMinute(int destType, long[] dt)
	{
		int num = 2;
		dt[num]++;
		if (dt[num] == 60 && destType < 10)
		{
			incrementHour(destType, dt);
			dt[num] = 0L;
		}
	}

	private void incrementSecond(int destType, long[] dt)
	{
		int num = 3;
		dt[num]++;
		if (dt[num] == 60 && destType < 12)
		{
			incrementMinute(destType, dt);
			dt[num] = 0L;
		}
	}

	private void checkScale(int leadScale)
	{
		switch (type)
		{
		case 3:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(days)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(days)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 4:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(days)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(days)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(hours) > 23)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 5:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(days)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(days)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(hours) > 23 || Math.Abs(minutes) > 59)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 6:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(days)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(days)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(hours) > 23 || Math.Abs(minutes) > 59 || Math.Abs(seconds) > 59)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 7:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(hours)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(hours)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 8:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(hours)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(hours)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(minutes) > 59)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 9:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(hours)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(hours)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(minutes) > 59 || Math.Abs(seconds) > 59)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 10:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(minutes)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(minutes)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 11:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(minutes)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(minutes)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(seconds) > 59)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 12:
			if (leadScale == -1)
			{
				leadScale = Convert.ToString(Math.Abs(seconds)).Length;
			}
			else if (leadScale < Convert.ToString(Math.Abs(seconds)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		}
		if (leadScale > 9)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
		}
	}

	public void ToHours()
	{
		hours += days * 24;
		days = 0;
	}

	public void ToMinutes()
	{
		minutes += hours * 60;
		hours = 0;
	}

	public void ToSeconds()
	{
		seconds += minutes * 60;
		minutes = 0;
	}

	public void Clear()
	{
		days = 0;
		hours = 0;
		minutes = 0;
		seconds = 0;
		fraction = 0;
	}

	public string GetTimeSpanFormatString()
	{
		DmIntervalDT dmIntervalDT = convertTo(1574);
		string text = dmIntervalDT.days + "." + dmIntervalDT.hours + ":" + dmIntervalDT.minutes + ":" + dmIntervalDT.seconds + ".";
		string text2 = dmIntervalDT.fraction.ToString();
		for (int num = 6 - text2.Length; num > 0; num--)
		{
			text += "0";
		}
		text += text2;
		if (!dmIntervalDT.negative)
		{
			return text;
		}
		return "-" + text;
	}
}
