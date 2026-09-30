using System;
using W.Dm.util;

namespace W.Dm;

public class DmIntervalYM
{
	public const byte QUA_Y = 0;

	public const byte QUA_YM = 1;

	public const byte QUA_MO = 2;

	private int leadScale;

	private bool negative;

	private byte type;

	public int years;

	public int months;

	private int scaleForSvr;

	public DmIntervalYM(byte type, bool negative, int year, int month, int scaleForSvr)
	{
		this.type = type;
		years = year;
		months = month;
		this.negative = negative;
		this.scaleForSvr = scaleForSvr;
		leadScale = (scaleForSvr >> 4) & 0xF;
	}

	public DmIntervalYM(long months, int leadScale)
	{
		years = (int)(months / 12);
		this.months = (int)(months % 12);
		checkSignAndReset();
		this.leadScale = leadScale;
		if ((double)years > Math.Pow(10.0, this.leadScale) - 1.0 || (double)years < 1.0 - Math.Pow(10.0, this.leadScale))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
		}
		type = 1;
		scaleForSvr = (byte)((this.leadScale << 4) | 0);
	}

	public DmIntervalYM(byte[] bs, int leadScale)
	{
		reset(bs);
		this.leadScale = leadScale;
	}

	public DmIntervalYM(byte[] bs)
	{
		reset(bs);
	}

	public DmIntervalYM(string str, int scale = 288)
	{
		string[] array = str.Split(' ');
		int num = 1;
		leadScale = (scale >> 4) & 0xF;
		if (array.Length == 1)
		{
			num = (scale >> 8) & 0xF;
			parseStr(array[0], num);
			return;
		}
		if (array[2].ToUpper().StartsWith("YEAR"))
		{
			num = ((array.Length > 3) ? 1 : 0);
		}
		else if (array[2].ToUpper().StartsWith("MONTH"))
		{
			num = 2;
		}
		parseStr(array[1], num);
	}

	private void reset(byte[] ym)
	{
		scaleForSvr = ByteUtil.getInt(ym, 8);
		leadScale = (scaleForSvr >> 4) & 0xF;
		type = ym[9];
		switch (type)
		{
		case 0:
			years = ByteUtil.getInt(ym, 0);
			break;
		case 1:
			years = ByteUtil.getInt(ym, 0);
			months = ByteUtil.getInt(ym, 4);
			break;
		case 2:
			months = ByteUtil.getInt(ym, 4);
			break;
		}
		checkSignAndReset();
	}

	private void checkSignAndReset()
	{
		negative = years < 0 || months < 0;
		if (negative)
		{
			years = -years;
			months = -months;
		}
	}

	public int getYear()
	{
		return years;
	}

	public int getMonth()
	{
		return months;
	}

	public byte GetYMType()
	{
		return type;
	}

	public int getScaleForSvr()
	{
		return (GetYMType() << 8) + (leadScale << 4);
	}

	public string GetYMString()
	{
		string text = "INTERVAL '";
		if (negative)
		{
			text += "-";
		}
		switch (type)
		{
		case 0:
		{
			string text3 = formatStrByScale(getYear().ToString(), leadScale);
			text = text + text3 + "' YEAR(" + leadScale + ")";
			break;
		}
		case 1:
		{
			string text3 = formatStrByScale(getYear().ToString(), leadScale);
			string text2 = formatStrByDefault(getMonth().ToString());
			text = text + text3 + "-" + text2 + "' YEAR(" + leadScale + ") TO MONTH";
			break;
		}
		case 2:
		{
			string text2 = formatStrByScale(getMonth().ToString(), leadScale);
			text = text + text2 + "' MONTH(" + leadScale + ")";
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
		return GetYMString();
	}

	private void parseStr(string str, int type)
	{
		str = getAbsValue(str);
		string[] array = ((type != 0 && type != 2) ? str.Split('-') : new string[1] { str });
		switch (type)
		{
		case 0:
		{
			int num2 = int.Parse(array[0], DmConst.invariantCulture);
			if ((double)num2 > Math.Pow(10.0, leadScale) - 1.0 || (double)num2 < 1.0 - Math.Pow(10.0, leadScale))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			years = num2;
			this.type = 0;
			scaleForSvr = (byte)((leadScale << 4) | 0);
			break;
		}
		case 1:
		{
			int num2 = int.Parse(array[0], DmConst.invariantCulture);
			if ((double)num2 > Math.Pow(10.0, leadScale) - 1.0 || (double)num2 < 1.0 - Math.Pow(10.0, leadScale))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			int num = int.Parse(array[1], DmConst.invariantCulture);
			years = num2;
			months = num;
			this.type = 1;
			scaleForSvr = (byte)((leadScale << 4) | 0);
			break;
		}
		case 2:
		{
			int num = int.Parse(array[0], DmConst.invariantCulture);
			if ((double)num > Math.Pow(10.0, leadScale) - 1.0 || (double)num < 1.0 - Math.Pow(10.0, leadScale))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			months = num;
			this.type = 2;
			scaleForSvr = (byte)((leadScale << 4) | 0);
			break;
		}
		}
	}

	public byte[] encode(int scale)
	{
		if (scale == 0)
		{
			scale = scaleForSvr;
		}
		checkScale(leadScale);
		if (scale != scaleForSvr)
		{
			DmIntervalYM dmIntervalYM = convertTo(scale);
			years = dmIntervalYM.years;
			months = dmIntervalYM.months;
		}
		else
		{
			checkScale(leadScale);
		}
		byte[] array = new byte[12];
		ByteUtil.setInt(array, 0, negative ? (-years) : years);
		ByteUtil.setInt(array, 4, negative ? (-months) : months);
		ByteUtil.setInt(array, 8, scale);
		return array;
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

	private void checkScale(int prec)
	{
		switch (GetYMType())
		{
		case 0:
			if (prec < Convert.ToString(Math.Abs(years)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 1:
			if (prec < Convert.ToString(Math.Abs(years)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			if (Math.Abs(months) > 11)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
			}
			break;
		case 2:
			if (prec < Convert.ToString(Math.Abs(months)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		}
	}

	public DmIntervalYM convertTo(int scale)
	{
		int num = (scale & 0xFF00) >> 8;
		int num2 = (scale >> 4) & 0xF;
		long num3 = (long)years * 12L + months;
		long num4 = 0L;
		long num5 = 0L;
		switch (num)
		{
		case 0:
			num4 = num3 / 12;
			if (num3 % 12 >= 6)
			{
				num4++;
			}
			else if (num3 % 12 <= -6)
			{
				num4--;
			}
			if (num2 < Convert.ToString(Math.Abs(num4)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 1:
			num4 = years;
			num5 = months;
			if (num2 < Convert.ToString(Math.Abs(num4)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		case 2:
			num5 = num3;
			if (num2 < Convert.ToString(Math.Abs(num5)).Length)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_INTERVAL_OVERFLOW);
			}
			break;
		}
		return new DmIntervalYM((byte)num, negative, (int)num4, (int)num5, scale);
	}
}
