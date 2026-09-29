namespace W.Dm.util;

public class DmDecimal
{
	public decimal value;

	public bool precisionChanged = true;

	public int precisionRes;

	public bool scaleChanged = true;

	public int scaleRes;

	public DmDecimal(decimal dec)
	{
		value = dec;
	}

	public int compareTo(decimal value1)
	{
		return value.CompareTo(value1);
	}

	public int signum()
	{
		if (!(value > 0m))
		{
			if (!(value < 0m))
			{
				return 0;
			}
			return -1;
		}
		return 1;
	}

	public int precision()
	{
		if (!precisionChanged)
		{
			return precisionRes;
		}
		string text = value.ToString();
		if (text.StartsWith("-"))
		{
			text = text.Substring(1);
		}
		int num = text.IndexOf('.');
		if (num == -1)
		{
			precisionRes = text.Length;
		}
		else
		{
			string text2 = text.Substring(0, num);
			if (text2.Equals("0"))
			{
				text2 = "";
			}
			precisionRes = text2.Length + text.Substring(num + 1).Length;
		}
		precisionChanged = false;
		return precisionRes;
	}

	public int scale()
	{
		if (!scaleChanged)
		{
			return scaleRes;
		}
		string text = value.ToString();
		if (text.StartsWith("-"))
		{
			text = text.Substring(1);
		}
		int num = text.IndexOf('.');
		scaleRes = ((num != -1) ? text.Substring(num + 1).Length : 0);
		scaleChanged = false;
		return scaleRes;
	}

	public DmDecimal negate()
	{
		value = -value;
		return this;
	}

	public string toPlainString()
	{
		return value.ToString();
	}
}
