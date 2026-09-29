using System;
using System.Text;
using W.Dm.util;

namespace W.Dm;

public class DmdbNumeric
{
	public const decimal MAX_VALUE = decimal.MaxValue;

	public const decimal MIN_VALUE = decimal.MinValue;

	public static readonly decimal SERVER_NEG_MIN_VALUE = 0.0000000000000000000000000001m;

	public static readonly decimal SERVER_POSITIVE_MIN_VALUE = -0.0000000000000000000000000001m;

	public static bool ROUND_HALF_SWITCH = false;

	public const int XDEC_MAX_PREC = 40;

	private const int XDEC_SIZE = 21;

	private const int FLAG_ZERO = 128;

	public const int FLAG_POSITIVE = 193;

	public const int FLAG_NEGTIVE = 62;

	private const int EXP_MAX = 61;

	private const int EXP_MIN = -64;

	private const int NUM_POSITIVE = 1;

	private const int NUM_NEGTIVE = 101;

	private int sign;

	private int weight;

	private string digits = "";

	private long digitLong;

	private int digitLongLen;

	private int prec;

	private int scale;

	private bool hasScale;

	private int integerPart;

	private bool roundHalfParse;

	private static readonly string[] numToString = new string[100]
	{
		"00", "01", "02", "03", "04", "05", "06", "07", "08", "09",
		"10", "11", "12", "13", "14", "15", "16", "17", "18", "19",
		"20", "21", "22", "23", "24", "25", "26", "27", "28", "29",
		"30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
		"40", "41", "42", "43", "44", "45", "46", "47", "48", "49",
		"50", "51", "52", "53", "54", "55", "56", "57", "58", "59",
		"60", "61", "62", "63", "64", "65", "66", "67", "68", "69",
		"70", "71", "72", "73", "74", "75", "76", "77", "78", "79",
		"80", "81", "82", "83", "84", "85", "86", "87", "88", "89",
		"90", "91", "92", "93", "94", "95", "96", "97", "98", "99"
	};

	private DmdbNumeric(int prec, int scale)
	{
		this.prec = prec;
		this.scale = scale;
		hasScale = ((scale != -1 && (prec > 0 || scale > 0)) ? true : false);
	}

	public DmdbNumeric(byte[] values, int prec, int scale)
		: this(prec, scale)
	{
		decode(values);
	}

	public static DmdbNumeric valueOf(decimal dec, int prec, int scale)
	{
		DmdbNumeric dmdbNumeric = new DmdbNumeric(prec, scale);
		dmdbNumeric.parse(dec);
		return dmdbNumeric;
	}

	public static DmdbNumeric valueOf(string str, int prec, int scale)
	{
		DmdbNumeric dmdbNumeric = new DmdbNumeric(prec, scale);
		dmdbNumeric.parse(str);
		return dmdbNumeric;
	}

	public static DmdbNumeric valueOf(long val, int prec, int scale)
	{
		return valueOf(new decimal(val), prec, scale);
	}

	public decimal toDecimal(bool compatibleOracle)
	{
		decimal num;
		if (isZero())
		{
			num = default(decimal);
		}
		else if (digitLong != 0L)
		{
			num = new decimal(digitLong);
			if (weight > 0)
			{
				num = num.movePointRight(weight).setScale(0, MidpointRounding.AwayFromZero);
			}
			else if (weight < 0)
			{
				num = num.movePointLeft(-weight).stripTrailingZeros();
			}
		}
		else
		{
			num = decimal.Parse((sign < 0) ? ("-" + digits) : digits);
			if (weight > 0)
			{
				num = num.movePointRight(weight);
			}
			else if (weight < 0)
			{
				num = num.movePointLeft(-weight);
			}
		}
		if (!roundHalfParse)
		{
			num = roundHalfup(num);
		}
		if (!compatibleOracle && hasScale)
		{
			num = num.setScale(scale, MidpointRounding.AwayFromZero);
		}
		return num;
	}

	public string toString(bool compatibleOracle)
	{
		return toDecimal(compatibleOracle).ToString();
	}

	public string toString()
	{
		return toString(compatibleOracle: false);
	}

	public bool isZero()
	{
		return sign == 0;
	}

	public void decode(byte[] values)
	{
		if (values == null || values.Length == 0 || values.Length > 21)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_FATAL_ERROR);
		}
		if (values[0] == 128 || values.Length == 1)
		{
			sign = 0;
			return;
		}
		sign = (((values[0] & 0x80) != 0) ? 1 : (-1));
		int uB = ByteUtil.getUB1(values, 0);
		int num = ((sign > 0) ? (uB - 193) : (62 - uB));
		if (values.Length <= 9)
		{
			long num2 = 0L;
			for (int i = 1; i < values.Length; i++)
			{
				int num3 = ((sign > 0) ? (values[i] - 1) : (101 - values[i]));
				if (num3 < 0 || num3 > 99)
				{
					break;
				}
				num2 = num2 * 100 + num3;
				digitLongLen += 2;
			}
			digitLong = ((sign == 1) ? num2 : (-num2));
		}
		else
		{
			StringBuilder stringBuilder = new StringBuilder(values.Length << 1);
			for (int j = 1; j < values.Length; j++)
			{
				int num4 = ((sign > 0) ? (values[j] - 1) : (101 - values[j]));
				if (num4 < 0 || num4 > 99)
				{
					break;
				}
				stringBuilder.append(numToString[num4]);
				digitLongLen += 2;
			}
			digits = stringBuilder.ToString();
		}
		weight = (num << 1) - (digitLongLen - 2);
		if (weight != 0 && digitLong == 0L)
		{
			formatDigits();
		}
	}

	public byte[] encode(bool allowOverflow)
	{
		if (isZero())
		{
			return new byte[1] { 128 };
		}
		checkPrec(allowOverflow);
		if (MathUtil.isOdd(weight))
		{
			digits += "0";
			weight--;
		}
		if (MathUtil.isOdd(digits.length()))
		{
			digits = "0" + digits;
		}
		int num = (weight + digits.length()) / 2 - 1;
		if (num > 61 || num < -64)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
		int num2 = digits.length();
		int num3 = digits.length() / 2 + 1;
		int num4 = 0;
		if (num3 > 21)
		{
			if (!allowOverflow)
			{
				DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
			}
			num3 = 21;
			num2 = 2 * (num3 - 1);
			num4 = ((CharUtil.digit(digits.charAt(num2), 10) >= 5) ? 1 : 0);
		}
		byte[] array = new byte[(sign < 0 && num3 < 21) ? (num3 + 1) : num3];
		int num5 = 0;
		int num6 = num3 - 1;
		bool flag = true;
		int num7 = num2 - 1;
		while (num7 > 0 && num6 > 0)
		{
			num5 = CharUtil.digit(digits.charAt(num7--), 10) + CharUtil.digit(digits.charAt(num7--), 10) * 10 + num4;
			num4 = ((num5 > 99) ? 1 : 0);
			if (num5 > 99)
			{
				num5 %= 100;
			}
			flag = flag && num5 == 0;
			if (flag)
			{
				num3--;
			}
			array[num6] = (byte)((sign > 0) ? (num5 + 1) : (101 - num5));
			num6--;
		}
		if (flag && num4 > 0)
		{
			num++;
			num5 = num4;
			array[num3] = (byte)((sign > 0) ? (num5 + 1) : (101 - num5));
			num3++;
		}
		array[0] = (byte)((sign > 0) ? (num + 193) : (62 - num));
		if (sign < 0 && num3 < 21)
		{
			array[num3++] = 102;
		}
		if (num3 < array.Length)
		{
			byte[] array2 = new byte[num3];
			Array.Copy(array, 0, array2, 0, num3);
			array = array2;
		}
		return array;
	}

	private void parse(decimal dec)
	{
		dec = roundHalfup(dec);
		roundHalfParse = true;
		sign = dec.signum();
		if (!isZero())
		{
			if (sign < 0)
			{
				dec = dec.negate();
			}
			string str = dec.toPlainString();
			if (str.indexOf(".") != -1)
			{
				digits = str.replace(".", "");
				weight = -dec.scale();
			}
			else
			{
				digits = str;
				weight = 0;
			}
			if (dec.scale() < 0)
			{
				integerPart = dec.precision();
			}
			else
			{
				integerPart = digits.length() + weight;
			}
			formatDigits();
		}
	}

	private decimal roundHalfup(decimal dec)
	{
		if (hasScale && dec.scale() > scale)
		{
			dec = dec.setScale(scale, MidpointRounding.AwayFromZero);
		}
		int num = dec.precision();
		if (hasScale && num > prec)
		{
			if (dec.scale() >= num - prec)
			{
				dec = dec.setScale(dec.scale() - (num - prec), MidpointRounding.AwayFromZero);
			}
			else if (dec.scale() < num - prec)
			{
				int num2 = num - prec - dec.scale();
				dec = dec.movePointLeft(num - prec - dec.scale());
				weight += num2;
				dec = dec.setScale(0, MidpointRounding.AwayFromZero);
			}
		}
		if (ROUND_HALF_SWITCH)
		{
			if (SERVER_NEG_MIN_VALUE.compareTo(dec) < 0 && dec.signum() != 1)
			{
				dec = default(decimal);
			}
			if (SERVER_POSITIVE_MIN_VALUE.compareTo(dec) > 0 && dec.signum() == 1)
			{
				dec = default(decimal);
			}
		}
		return dec;
	}

	private void formatDigits()
	{
		int num = 0;
		char[] array = digits.toCharArray();
		for (int i = 0; i < array.Length && array[i] == '0'; i++)
		{
			num++;
		}
		int num2 = 0;
		int num3 = array.Length - 1;
		while (num3 >= num && array[num3] == '0')
		{
			num2++;
			num3--;
		}
		if (num2 > 0 || num > 0)
		{
			digits = new string(array, num, array.Length - num - num2);
			weight += num2;
		}
	}

	private void parse(string str)
	{
		decimal dec = default(decimal);
		try
		{
			dec = decimal.Parse(str);
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		parse(dec);
	}

	private bool checkPrec(bool allowOverflow)
	{
		int num = digits.length();
		if (hasScale && (num > prec || num > 40))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
			return false;
		}
		if (!allowOverflow && integerPart > 40)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
			return false;
		}
		return true;
	}
}
