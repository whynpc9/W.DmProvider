using System;
using System.Runtime.InteropServices;
using System.Text;
using W.Dm.Internal.Types;

namespace W.Dm;

[StructLayout(LayoutKind.Sequential)]
internal class Dmxdec
{
	private byte sign;

	private byte ndigits;

	private byte rscale;

	private short weight;

	private byte len;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)]
	private byte[] value;

	public Dmxdec()
	{
		value = new byte[22];
	}

	public byte[] GetValue()
	{
		return value;
	}

	public byte GetLen()
	{
		return len;
	}

	public static int xdec_from_char(nint xdec, string str, uint len) =>
		throw new NotSupportedException("Native decimal conversion is not enabled by this provider version.");
}
public class DmXDec
{
	internal ReadOnlySpan<byte> WireValue => m_b;
	private const int XDEC_TEMPBUF_SIZE = 21;

	private const int XDEC_POSITIVE = 193;

	private const int XDEC_NEGTIVE = 62;

	private const int XDEC_NEGTIVE_NUM = 101;

	private const int XDEC_SIGN_POSITIVE = 1;

	private const int XDEC_SIGN_NEGTIVE = 0;

	private const int XDEC_MAX_LEN = 38;

	private int n;

	private int m;

	private int m_sign = 1;

	private int m_exp;

	private string m_strInt;

	private string m_strDec;

	private int m_max_len = 38;

	private byte[] m_b;

	public DmXDec()
	{
	}

	public DmXDec(byte[] b)
	{
		m_b = b;
	}

	private string checkStr(string strOrg, int prec, int scale)
	{
		string text = null;
		try
		{
			decimal d = decimal.Parse(strOrg, DmConst.invariantCulture);
			text = d.ToString();
			if (prec != 0 || scale != 0)
			{
				text = decimal.Round(d, Math.Min(28, scale), MidpointRounding.AwayFromZero).ToString();
				m_max_len = prec;
			}
		}
		catch (Exception)
		{
			throw new InvalidCastException();
		}
		return text;
	}

	private string setSign(string strOrg)
	{
		m_sign = 1;
		if (strOrg.StartsWith("-"))
		{
			m_sign = 0;
			return strOrg.Substring(1);
		}
		if (strOrg.StartsWith("+"))
		{
			m_sign = 1;
			return strOrg.Substring(1);
		}
		return strOrg;
	}

	private string rmvUnnecessaryZeros(string strOrg)
	{
		int i;
		for (i = 0; i < strOrg.Length && '0' == strOrg[i]; i++)
		{
		}
		int num = strOrg.Substring(i).IndexOf('.');
		int num2 = strOrg.Length - 1;
		while (-1 != num && '0' == strOrg[num2] && num2 >= 0)
		{
			num2--;
		}
		if (-1 != num)
		{
			return strOrg.Substring(i, num2 + 1 - i);
		}
		return strOrg.Substring(i);
	}

	private void setDecInt(string strRet)
	{
		int num = strRet.IndexOf('.');
		if (num == 0)
		{
			m_strInt = null;
			if (1 == strRet.Length)
			{
				m_strDec = null;
			}
			else
			{
				m_strDec = strRet.Substring(1);
			}
		}
		else if (-1 == num || strRet.Length - 1 == num)
		{
			m_strDec = null;
			if (-1 == num)
			{
				m_strInt = strRet;
			}
			else
			{
				m_strInt = strRet.Substring(0, strRet.Length - 1);
			}
		}
		else
		{
			m_strInt = strRet.Substring(0, num);
			m_strDec = strRet.Substring(num + 1);
		}
	}

	private bool checkZero(string strRet)
	{
		decimal num = decimal.Parse(strRet, DmConst.invariantCulture);
		decimal value = 0m;
		if (num.CompareTo(value) == 0)
		{
			return true;
		}
		return false;
	}

	private bool checkZero2(string strRet)
	{
		int num = strRet.IndexOf('.');
		int num2 = strRet.Length;
		int num3 = 0;
		int num4 = strRet.IndexOf('e');
		if (-1 == num4)
		{
			num4 = strRet.IndexOf('E');
		}
		if (-1 != num4)
		{
			num2 = num4;
		}
		for (num3 = 0; num3 < num; num3++)
		{
			if (strRet[num3] != '0')
			{
				return false;
			}
		}
		for (num3 = num2 - 1; num3 > num; num3--)
		{
			if (strRet[num3] != '0')
			{
				return false;
			}
		}
		if (num == 0 && num2 == 1 && strRet[num3] != '0')
		{
			return false;
		}
		return true;
	}

	private int cntZeroEnd(string strInt)
	{
		int num = strInt.Length - 1;
		while (num >= 0 && '0' == strInt[num])
		{
			num--;
		}
		return strInt.Length - 1 - num;
	}

	private int cntZeroStart(string strDec)
	{
		int i;
		for (i = 0; i < strDec.Length && '0' == strDec[i]; i++)
		{
		}
		return i;
	}

	private byte[] processStrInt()
	{
		if (m_strInt == null)
		{
			return null;
		}
		int length = m_strInt.Length;
		int num = 0;
		int i = 0;
		int num2 = length / 2;
		if (length % 2 != 0)
		{
			num2++;
		}
		byte[] array = new byte[num2];
		bool flag = false;
		if (length % 2 != 0)
		{
			num = Convert.ToInt32(m_strInt.Substring(0, 1));
			if (m_sign == 1)
			{
				array[i] = (byte)(num + 1);
			}
			else
			{
				array[i] = (byte)(101 - num);
			}
			i++;
			flag = true;
		}
		for (; i < num2; i++)
		{
			num = ((!flag) ? Convert.ToInt32(m_strInt.Substring(i * 2, 2)) : Convert.ToInt32(m_strInt.Substring(i * 2 - 1, 2)));
			if (m_sign == 1)
			{
				array[i] = (byte)(num + 1);
			}
			else
			{
				array[i] = (byte)(101 - num);
			}
		}
		return array;
	}

	private byte[] processStrDec()
	{
		if (m_strDec == null)
		{
			return null;
		}
		int length = m_strDec.Length;
		int num = 0;
		if (length % 2 != 0)
		{
			m_strDec += "0";
			length = m_strDec.Length;
		}
		int num2 = length / 2;
		byte[] array = new byte[num2];
		for (int i = 0; i < num2; i++)
		{
			num = Convert.ToInt32(m_strDec.Substring(i * 2, 2));
			if (m_sign == 1)
			{
				array[i] = (byte)(num + 1);
			}
			else
			{
				array[i] = (byte)(101 - num);
			}
		}
		return array;
	}

	private byte fixFlag()
	{
		byte b = 0;
		if (m_sign == 1)
		{
			return (byte)(193 + m_exp / 2);
		}
		return (byte)(62 - m_exp / 2);
	}

	private byte[] fixDec(byte flag, byte[] btBefore, byte[] btAfter)
	{
		int num = 0;
		byte[] array = new byte[21];
		array[0] = flag;
		num++;
		if (btBefore != null)
		{
			byte[] array2 = null;
			if (btBefore.Length > 21 - num)
			{
				array2 = new byte[Math.Abs(21 - num)];
				Array.Copy(btBefore, 0, array2, 0, array2.Length);
			}
			else
			{
				array2 = btBefore;
			}
			Array.Copy(array2, 0, array, num, array2.Length);
			num += array2.Length;
		}
		if (btAfter != null)
		{
			byte[] array3 = null;
			if (btAfter.Length > 21 - num)
			{
				array3 = new byte[Math.Abs(21 - num)];
				Array.Copy(btAfter, 0, array3, 0, array3.Length);
			}
			else
			{
				array3 = btAfter;
			}
			Array.Copy(array3, 0, array, num, array3.Length);
			num += array3.Length;
		}
		if (m_sign == 0 && num < 20)
		{
			array[num++] = 102;
		}
		if (num < 20)
		{
			array[num] = 0;
		}
		byte[] array4 = new byte[num];
		Array.Copy(array, 0, array4, 0, num);
		m_b = array4;
		return array4;
	}

	private byte[] fixDec(ref byte[] Ret, byte flag, byte[] btBefore, byte[] btAfter)
	{
		int num = 0;
		byte[] array = new byte[21];
		array[0] = flag;
		num++;
		if (btBefore != null)
		{
			byte[] array2 = null;
			if (btBefore.Length > 21 - num)
			{
				array2 = new byte[Math.Abs(21 - num)];
				Array.Copy(btBefore, 0, array2, 0, array2.Length);
			}
			else
			{
				array2 = btBefore;
			}
			Array.Copy(array2, 0, array, num, array2.Length);
			num += array2.Length;
		}
		if (btAfter != null)
		{
			byte[] array3 = null;
			if (btAfter.Length > 21 - num)
			{
				array3 = new byte[Math.Abs(21 - num)];
				Array.Copy(btAfter, 0, array3, 0, array3.Length);
			}
			else
			{
				array3 = btAfter;
			}
			Array.Copy(array3, 0, array, num, array3.Length);
			num += array3.Length;
		}
		if (m_sign == 0 && num < 20)
		{
			array[num++] = 102;
		}
		if (num < 20)
		{
			array[num] = 0;
		}
		Ret = new byte[num];
		Array.Copy(array, 0, Ret, 0, num);
		m_b = Ret;
		return Ret;
	}

	private byte[] fixZero()
	{
		return new byte[1] { 128 };
	}

	private void fixZero(ref byte[] ret)
	{
		ret = new byte[1];
		ret[0] = 128;
	}

	private string setExp(string strOrg)
	{
		int num = strOrg.IndexOf('e');
		if (-1 == num)
		{
			num = strOrg.IndexOf('E');
		}
		if (-1 == num)
		{
			m_exp = 0;
			return strOrg;
		}
		string value = strOrg.Substring(num + 1);
		m_exp = Convert.ToInt32(value);
		return strOrg.Substring(0, num);
	}

	private void checkOverFlow()
	{
		if (m_exp > 125 || m_exp < -128)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	private void processExp()
	{
		if (m_exp % 2 == 0)
		{
			return;
		}
		string strDec = m_strDec;
		string strInt = m_strInt;
		if (strInt == null && strDec != null)
		{
			if (strDec.StartsWith("0"))
			{
				m_strDec = m_strDec.Substring(1);
			}
			else
			{
				m_strInt = m_strDec.Substring(0, 1);
				m_strDec = m_strDec.Substring(1);
			}
		}
		else if (strInt != null && strDec == null)
		{
			m_strInt += "0";
		}
		else
		{
			m_strInt += m_strDec.Substring(0, 1);
			m_strDec = m_strDec.Substring(1);
		}
		m_exp--;
		if (m_strInt != null)
		{
			m_strInt = rmvUnnecessaryZeros(m_strInt);
		}
		if (m_strDec != null)
		{
			m_strDec = rmvUnnecessaryZeros("." + m_strDec).Substring(1);
		}
	}

	private void setMN()
	{
		string strInt = m_strInt;
		string text = m_strDec;
		if (strInt != null && text == null)
		{
			n = m_strInt.Length;
			m = cntZeroEnd(m_strInt);
			if (m % 2 != 0)
			{
				m--;
			}
			m_exp += m;
			m_strInt = m_strInt.Substring(0, n - m);
			n = m_strInt.Length;
			if (n % 2 != 0)
			{
				n++;
			}
			m_exp += n - 2;
		}
		else if (strInt == null && text != null)
		{
			n = text.Length;
			m = cntZeroStart(text);
			if (m % 2 != 0)
			{
				m--;
			}
			m_exp -= m;
			if (n - m < 2)
			{
				text += "0";
			}
			m_strInt = text.Substring(m, 2);
			m_strDec = text.Substring(m + 2);
			m_exp -= 2;
			if (m_strInt.StartsWith("0"))
			{
				m_strInt = m_strInt.Substring(1);
			}
		}
		else
		{
			n = strInt.Length;
			if (n % 2 != 0)
			{
				n++;
			}
			m_exp += n - 2;
		}
	}

	private void checkMaxLen()
	{
		int num = ((m_strInt != null) ? m_strInt.Length : 0);
		int num2 = ((m_strDec != null) ? m_strDec.Length : 0);
		if (num + num2 > m_max_len)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public byte[] StrToDec(string str, int prec, int scal, bool dmxdec_direct)
	{
		// dmxdec_direct is retained for source compatibility, not permission to
		// round, truncate or bypass the declared DECIMAL precision and scale.
		return DmNumericCodec.EncodeDecimal(DmDecimal.Parse(str), prec, scal);
	}

	internal void StrToDec(ref byte[] ret, string str, int prec, int scal, bool dmxdec_direct)
	{
		ret = StrToDec(str, prec, scal, dmxdec_direct);
	}

	internal string decToString(byte[] arr) => DmNumericCodec.DecodeDecimal(arr).ToString();

	public DmXDec Parse(string s)
	{
		return new DmXDec(StrToDec(s, 0, -1, dmxdec_direct: true));
	}

	public override string ToString()
	{
		return decToString(m_b);
	}
}
