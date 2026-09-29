using System;
using W.Dm.util;

namespace W.Dm;

internal class DmRowId
{
	private static readonly char[] toBase64 = new char[64]
	{
		'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
		'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
		'U', 'V', 'W', 'X', 'Y', 'Z', 'a', 'b', 'c', 'd',
		'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n',
		'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x',
		'y', 'z', '0', '1', '2', '3', '4', '5', '6', '7',
		'8', '9', '+', '/'
	};

	private static readonly byte[] fromBase64 = new byte[256]
	{
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 62, 64, 64, 64, 63, 52, 53,
		54, 55, 56, 57, 58, 59, 60, 61, 64, 64,
		64, 64, 64, 64, 64, 0, 1, 2, 3, 4,
		5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
		15, 16, 17, 18, 19, 20, 21, 22, 23, 24,
		25, 64, 64, 64, 64, 64, 64, 26, 27, 28,
		29, 30, 31, 32, 33, 34, 35, 36, 37, 38,
		39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
		49, 50, 51, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64, 64, 64, 64, 64,
		64, 64, 64, 64, 64, 64
	};

	private const byte BASE64_DECODE_UNDEFINED = 64;

	private const int BYTE_LEN_OLD = 8;

	private const int CHAR_LEN = 18;

	private const int BYTE_LEN_NEW = 12;

	private byte[] value;

	internal DmRowId(byte[] bytesValue)
	{
		value = bytesValue;
	}

	public static DmRowId valueOf(byte[] bytesValue)
	{
		if (bytesValue.Length != 12 && bytesValue.Length != 8)
		{
			throw new InvalidCastException();
		}
		return new DmRowId(bytesValue);
	}

	public static DmRowId valueOf(string strValue)
	{
		strValue = StringUtil.trimToEmpty(strValue);
		if (StringUtil.isEmpty(strValue))
		{
			return null;
		}
		if (strValue.Length == 18)
		{
			return new DmRowId(parse(strValue));
		}
		try
		{
			return valueOf(long.Parse(strValue));
		}
		catch (Exception)
		{
			throw new InvalidCastException();
		}
	}

	public static DmRowId valueOf(long longValue)
	{
		return new DmRowId(DmConvertion.LongToByteArray(longValue));
	}

	public byte[] getBytes()
	{
		return value;
	}

	public string toString()
	{
		if (value.Length == 8)
		{
			return DmConvertion.EightByteToLong(value).ToString() ?? "";
		}
		byte[] array = value;
		int num = 0;
		char[] array2 = new char[18];
		int num2 = 0;
		byte b = 0;
		byte b2 = array[num++];
		byte b3 = array[num++];
		array2[num2++] = toBase64[0];
		array2[num2++] = toBase64[(b2 & 0xF0) >> 4];
		array2[num2++] = toBase64[((b2 & 0xF) << 2) | ((b3 & 0xC0) >> 6)];
		array2[num2++] = toBase64[b3 & 0x3F];
		b3 = array[num++];
		array2[num2++] = toBase64[(b3 & 0xC0) >> 6];
		array2[num2++] = toBase64[b3 & 0x3F];
		for (int i = 0; i < 3; i++)
		{
			b = array[num++];
			b2 = array[num++];
			b3 = array[num++];
			array2[num2++] = toBase64[(b & 0xFC) >> 2];
			array2[num2++] = toBase64[((b & 3) << 4) | ((b2 & 0xF0) >> 4)];
			array2[num2++] = toBase64[((b2 & 0xF) << 2) | ((b3 & 0xC0) >> 6)];
			array2[num2++] = toBase64[b3 & 0x3F];
		}
		return new string(array2);
	}

	private static byte[] parse(string strValue)
	{
		if (strValue.Length != 18)
		{
			throw new InvalidCastException();
		}
		char[] array = strValue.ToCharArray();
		int num = 0;
		byte[] array2 = new byte[12];
		int num2 = 0;
		byte b = fromBase64[(uint)array[num++]];
		byte b2 = fromBase64[(uint)array[num++]];
		byte b3 = fromBase64[(uint)array[num++]];
		byte b4 = fromBase64[(uint)array[num++]];
		if (b == 64 || b2 == 64 || b3 == 64 || b4 == 64)
		{
			throw new InvalidCastException();
		}
		if ((byte)((b << 2) | ((b2 & 0x30) >> 4)) != 0)
		{
			throw new InvalidCastException();
		}
		array2[num2++] = (byte)((b2 << 4) | ((b3 & 0x3C) >> 2));
		array2[num2++] = (byte)((b3 << 6) | b4);
		b3 = fromBase64[(uint)array[num++]];
		b4 = fromBase64[(uint)array[num++]];
		if (b3 == 64 || b4 == 64)
		{
			throw new InvalidCastException();
		}
		if ((byte)((b3 & 0x3C) >> 2) != 0)
		{
			throw new InvalidCastException();
		}
		array2[num2++] = (byte)((b3 << 6) | b4);
		for (int i = 0; i < 3; i++)
		{
			b = fromBase64[(uint)array[num++]];
			b2 = fromBase64[(uint)array[num++]];
			b3 = fromBase64[(uint)array[num++]];
			b4 = fromBase64[(uint)array[num++]];
			if (b == 64 || b2 == 64 || b3 == 64 || b4 == 64)
			{
				throw new InvalidCastException();
			}
			array2[num2++] = (byte)((b << 2) | ((b2 & 0x30) >> 4));
			array2[num2++] = (byte)((b2 << 4) | ((b3 & 0x3C) >> 2));
			array2[num2++] = (byte)((b3 << 6) | b4);
		}
		return array2;
	}

	public long longValue(DmConnection conn)
	{
		if (value.Length == 8)
		{
			return DmConvertion.EightByteToLong(value);
		}
		int uB = BEByteUtil.getUB2(value, 0);
		long uB2 = BEByteUtil.getUB4(value, 2);
		byte[] array = new byte[8];
		BEByteUtil.getBytes(value, 6, array, 2, 6);
		long num = BEByteUtil.toLong(array);
		if (uB2 > conn.GetConnInstance().ConnProperty.rowidMaxHpno || uB > conn.GetConnInstance().ConnProperty.rowidMaxEpno)
		{
			throw new InvalidCastException();
		}
		ulong num2 = (ulong)uB2;
		long num3 = num | (long)((num2 & 0xFFFFFFFFFFFFFFFFuL) << 48);
		if (conn.GetConnInstance().ConnProperty.rowidNBitsEpno != 0)
		{
			ulong num4 = (ulong)uB;
			num3 |= (long)((num4 & 0xFFFFFFFFFFFFFFFFuL) << 64 - conn.GetConnInstance().ConnProperty.rowidNBitsEpno);
		}
		return num3;
	}

	public byte[] encode(DmConnection conn)
	{
		if (value.Length == 12)
		{
			return value;
		}
		long num = DmConvertion.EightByteToLong(value);
		int num2 = (int)((num >> 64 - conn.GetConnInstance().ConnProperty.rowidNBitsEpno) & (uint)((1 << conn.GetConnInstance().ConnProperty.rowidNBitsEpno) - 1));
		if (num2 > conn.GetConnInstance().ConnProperty.rowidMaxEpno)
		{
			throw new InvalidCastException();
		}
		long num3 = (num >> 48) & (uint)((1 << 16 - conn.GetConnInstance().ConnProperty.rowidNBitsEpno) - 1);
		if (num3 > conn.GetConnInstance().ConnProperty.rowidMaxEpno)
		{
			throw new InvalidCastException();
		}
		long l = num & 0xFFFFFFFFFFFFL;
		byte[] array = new byte[12];
		BEByteUtil.setUB2(array, 0, num2);
		BEByteUtil.setUB4(array, 2, num3);
		BEByteUtil.setBytes(array, 6, BEByteUtil.fromLong(l), 2, 6);
		return array;
	}
}
