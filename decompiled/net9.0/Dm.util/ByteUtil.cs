using System;
using System.Text;

namespace Dm.util;

public class ByteUtil
{
	public static int setByte(sbyte[] bytes, int offset, sbyte b)
	{
		bytes[offset] = b;
		return 1;
	}

	public static int setByte(byte[] bytes, int offset, byte b)
	{
		bytes[offset] = b;
		return 1;
	}

	public static int setShort(sbyte[] bytes, int offset, short s)
	{
		bytes[offset++] = (sbyte)s;
		bytes[offset++] = (sbyte)(s >> 8);
		return 2;
	}

	public static int setShort(byte[] bytes, int offset, short s)
	{
		bytes[offset++] = (byte)s;
		bytes[offset++] = (byte)(s >> 8);
		return 2;
	}

	public static int setInt(sbyte[] bytes, int offset, int i)
	{
		bytes[offset++] = (sbyte)i;
		bytes[offset++] = (sbyte)(i >> 8);
		bytes[offset++] = (sbyte)(i >> 16);
		bytes[offset++] = (sbyte)(i >> 24);
		return 4;
	}

	public static int setInt(byte[] bytes, int offset, int i)
	{
		bytes[offset++] = (byte)i;
		bytes[offset++] = (byte)(i >> 8);
		bytes[offset++] = (byte)(i >> 16);
		bytes[offset++] = (byte)(i >> 24);
		return 4;
	}

	public static int setLong(sbyte[] bytes, int offset, long l)
	{
		bytes[offset++] = (sbyte)l;
		bytes[offset++] = (sbyte)(l >> 8);
		bytes[offset++] = (sbyte)(l >> 16);
		bytes[offset++] = (sbyte)(l >> 24);
		bytes[offset++] = (sbyte)(l >> 32);
		bytes[offset++] = (sbyte)(l >> 40);
		bytes[offset++] = (sbyte)(l >> 48);
		bytes[offset++] = (sbyte)(l >> 56);
		return 8;
	}

	public static int setLong(byte[] bytes, int offset, long l)
	{
		bytes[offset++] = (byte)l;
		bytes[offset++] = (byte)(l >> 8);
		bytes[offset++] = (byte)(l >> 16);
		bytes[offset++] = (byte)(l >> 24);
		bytes[offset++] = (byte)(l >> 32);
		bytes[offset++] = (byte)(l >> 40);
		bytes[offset++] = (byte)(l >> 48);
		bytes[offset++] = (byte)(l >> 56);
		return 8;
	}

	public static int setFloat(sbyte[] bytes, int offset, float f)
	{
		return setBytes(bytes, offset, (sbyte[])(object)BitConverter.GetBytes(f));
	}

	public static int setFloat(byte[] bytes, int offset, float f)
	{
		return setBytes(bytes, offset, BitConverter.GetBytes(f));
	}

	public static int setDouble(sbyte[] bytes, int offset, double d)
	{
		return setLong(bytes, offset, BitConverter.DoubleToInt64Bits(d));
	}

	public static int setDouble(byte[] bytes, int offset, double d)
	{
		return setLong(bytes, offset, BitConverter.DoubleToInt64Bits(d));
	}

	public static int setUB1(sbyte[] bytes, int offset, int i)
	{
		bytes[offset] = (sbyte)i;
		return 1;
	}

	public static int setUB2(sbyte[] bytes, int offset, int i)
	{
		bytes[offset++] = (sbyte)i;
		bytes[offset++] = (sbyte)(i >> 8);
		return 2;
	}

	public static int setUB3(sbyte[] bytes, int offset, int i)
	{
		bytes[offset++] = (sbyte)i;
		bytes[offset++] = (sbyte)(i >> 8);
		bytes[offset++] = (sbyte)(i >> 16);
		return 3;
	}

	public static int setUB4(sbyte[] bytes, int offset, long l)
	{
		bytes[offset++] = (sbyte)l;
		bytes[offset++] = (sbyte)(l >> 8);
		bytes[offset++] = (sbyte)(l >> 16);
		bytes[offset++] = (sbyte)(l >> 24);
		return 4;
	}

	public static int setBytes(sbyte[] bytes, int offset, sbyte[] srcBytes)
	{
		Array.Copy(srcBytes, 0, bytes, offset, srcBytes.Length);
		return srcBytes.Length;
	}

	public static int setBytes(byte[] bytes, int offset, byte[] srcBytes)
	{
		Array.Copy(srcBytes, 0, bytes, offset, srcBytes.Length);
		return srcBytes.Length;
	}

	public static int setBytes(sbyte[] bytes, int offset, sbyte[] srcBytes, int srcOffset, int len)
	{
		Array.Copy(srcBytes, srcOffset, bytes, offset, len);
		return len;
	}

	public static int setBytes(byte[] bytes, int offset, byte[] srcBytes, int srcOffset, int len)
	{
		Array.Copy(srcBytes, srcOffset, bytes, offset, len);
		return len;
	}

	public static void setStringWithLength4(byte[] bytes, int offset, string str, string encoding)
	{
		byte[] bytes2 = DmConvertion.GetBytes(str, encoding);
		DmConvertion.SetInt(bytes, offset, bytes2.Length);
		DmConvertion.SetBytes(bytes, offset + 4, bytes2);
	}

	public static sbyte getByte(sbyte[] bytes, int offset)
	{
		return bytes[offset];
	}

	public static byte getByte(byte[] bytes, int offset)
	{
		return bytes[offset];
	}

	public static short getShort(sbyte[] bytes, int offset)
	{
		return (short)((short)(bytes[offset++] & 0xFF) | (short)((short)(bytes[offset++] & 0xFF) << 8));
	}

	public static short getShort(byte[] bytes, int offset)
	{
		return (short)((short)(bytes[offset++] & 0xFF) | (short)((short)(bytes[offset++] & 0xFF) << 8));
	}

	public static int getInt(sbyte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8) | ((bytes[offset++] & 0xFF) << 16) | ((bytes[offset++] & 0xFF) << 24);
	}

	public static int getInt(byte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8) | ((bytes[offset++] & 0xFF) << 16) | ((bytes[offset++] & 0xFF) << 24);
	}

	public static long getLong(sbyte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((long)(bytes[offset++] & 0xFF) << 8) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 24) | ((long)(bytes[offset++] & 0xFF) << 32) | ((long)(bytes[offset++] & 0xFF) << 40) | ((long)(bytes[offset++] & 0xFF) << 48) | ((long)(bytes[offset++] & 0xFF) << 56);
	}

	public static long getLong(byte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((long)(bytes[offset++] & 0xFF) << 8) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 24) | ((long)(bytes[offset++] & 0xFF) << 32) | ((long)(bytes[offset++] & 0xFF) << 40) | ((long)(bytes[offset++] & 0xFF) << 48) | ((long)(bytes[offset++] & 0xFF) << 56);
	}

	public static float getFloat(sbyte[] bytes, int offset)
	{
		return (float)BitConverter.Int64BitsToDouble(getInt(bytes, offset));
	}

	public static float getFloat(byte[] bytes, int offset)
	{
		return (float)BitConverter.Int64BitsToDouble(getInt(bytes, offset));
	}

	public static double getDouble(sbyte[] bytes, int offset)
	{
		return BitConverter.Int64BitsToDouble(getLong(bytes, offset));
	}

	public static double getDouble(byte[] bytes, int offset)
	{
		return BitConverter.Int64BitsToDouble(getLong(bytes, offset));
	}

	public static int getUB1(sbyte[] bytes, int offset)
	{
		return bytes[offset] & 0xFF;
	}

	public static int getUB1(byte[] bytes, int offset)
	{
		return bytes[offset] & 0xFF;
	}

	public static int getUB2(sbyte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8);
	}

	public static int getUB2(byte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8);
	}

	public static int getUB3(sbyte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8) | ((bytes[offset++] & 0xFF) << 16);
	}

	public static int getUB3(byte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((bytes[offset++] & 0xFF) << 8) | ((bytes[offset++] & 0xFF) << 16);
	}

	public static long getUB4(sbyte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((long)(bytes[offset++] & 0xFF) << 8) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 24);
	}

	public static long getUB4(byte[] bytes, int offset)
	{
		return (bytes[offset++] & 0xFF) | ((long)(bytes[offset++] & 0xFF) << 8) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 24);
	}

	public static sbyte[] getBytesWithLength(sbyte[] bytes, int offset)
	{
		int num = getInt(bytes, offset);
		sbyte[] array = new sbyte[num];
		Array.Copy(bytes, offset + 4, array, 0, num);
		return array;
	}

	public static sbyte[] getBytesWithLength2(sbyte[] bytes, int offset)
	{
		int uB = getUB2(bytes, offset);
		sbyte[] array = new sbyte[uB];
		Array.Copy(bytes, offset + 2, array, 0, uB);
		return array;
	}

	public static sbyte[] getBytes(sbyte[] bytes, int offset, int len)
	{
		sbyte[] array = new sbyte[len];
		Array.Copy(bytes, offset, array, 0, len);
		return array;
	}

	public static byte[] getBytes(byte[] bytes, int offset, int len)
	{
		byte[] array = new byte[len];
		Array.Copy(bytes, offset, array, 0, len);
		return array;
	}

	public static sbyte[] getBytes(string str, string encoding)
	{
		if (str == null)
		{
			return new sbyte[0];
		}
		try
		{
			return StringUtil.getBytes(str, encoding);
		}
		catch (ArgumentException)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_CHAR_CODE_NOT_SUPPORTED);
		}
		return new sbyte[0];
	}

	public static byte[] getBytes(string str, string encoding, bool isSbyte)
	{
		if (isSbyte)
		{
			return null;
		}
		if (str == null)
		{
			return new byte[0];
		}
		try
		{
			return StringUtil.getBytes(str, encoding, isSbyte: false);
		}
		catch (ArgumentException)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_CHAR_CODE_NOT_SUPPORTED);
		}
		return new byte[0];
	}

	public static string getString(sbyte[] bytes, string encoding)
	{
		return getString(bytes, 0, (bytes != null) ? bytes.Length : 0, encoding);
	}

	public static string getString(sbyte[] bytes, int offset, int len, string encoding)
	{
		string result = null;
		try
		{
			result = Encoding.GetEncoding(encoding).GetString((byte[])(object)bytes, offset, len);
		}
		catch (ArgumentException)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_CHAR_CODE_NOT_SUPPORTED);
		}
		return result;
	}

	public static string getString(byte[] bytes, int offset, int len, string encoding)
	{
		string result = null;
		try
		{
			result = Encoding.GetEncoding(encoding).GetString(bytes, offset, len);
		}
		catch (ArgumentException)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_CHAR_CODE_NOT_SUPPORTED);
		}
		return result;
	}

	public static string getStringWithLength(sbyte[] bytes, int offset, string encoding)
	{
		int len = getInt(bytes, offset);
		offset += 4;
		return getString(bytes, offset, len, encoding);
	}

	public static string getStringWithLength2(sbyte[] bytes, int offset, string encoding)
	{
		int uB = getUB2(bytes, offset);
		offset += 2;
		return getString(bytes, offset, uB, encoding);
	}

	public static byte[] fromByte(byte b)
	{
		return new byte[1] { b };
	}

	public static byte[] fromShort(short s)
	{
		return new byte[2]
		{
			(byte)s,
			(byte)(s >> 8)
		};
	}

	public static byte[] fromInt(int i)
	{
		return new byte[4]
		{
			(byte)i,
			(byte)(i >> 8),
			(byte)(i >> 16),
			(byte)(i >> 24)
		};
	}

	public static byte[] fromLong(long l)
	{
		return new byte[8]
		{
			(byte)l,
			(byte)(l >> 8),
			(byte)(l >> 16),
			(byte)(l >> 24),
			(byte)(l >> 32),
			(byte)(l >> 40),
			(byte)(l >> 48),
			(byte)(l >> 56)
		};
	}

	public static byte[] fromFloat(float f)
	{
		return fromInt(SingleToInt32Bits(f));
	}

	public static int SingleToInt32Bits(float f)
	{
		return BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
	}

	public static byte[] fromDouble(double d)
	{
		return fromLong(BitConverter.DoubleToInt64Bits(d));
	}

	public static byte[] fromUB1(int i)
	{
		return new byte[1] { (byte)i };
	}

	public static byte[] fromUB2(int i)
	{
		return new byte[2]
		{
			(byte)i,
			(byte)(i >> 8)
		};
	}

	public static byte[] fromUB3(int i)
	{
		return new byte[3]
		{
			(byte)i,
			(byte)(i >> 8),
			(byte)(i >> 16)
		};
	}

	public static byte[] fromUB4(long l)
	{
		return new byte[4]
		{
			(byte)l,
			(byte)(l >> 8),
			(byte)(l >> 16),
			(byte)(l >> 24)
		};
	}

	public static byte[] fromString(string str, string encoding)
	{
		if (str == null)
		{
			str = "";
		}
		return getBytes(str, encoding, isSbyte: false);
	}

	public static byte toByte(byte[] bytes)
	{
		return getByte(bytes, 0);
	}

	public static short toShort(byte[] bytes)
	{
		return getShort(bytes, 0);
	}

	public static int toInt(byte[] bytes)
	{
		return getInt(bytes, 0);
	}

	public static long toLong(byte[] bytes)
	{
		return getLong(bytes, 0);
	}

	public static float toFloat(byte[] bytes)
	{
		return getFloat(bytes, 0);
	}

	public static double toDouble(byte[] bytes)
	{
		return getDouble(bytes, 0);
	}

	public static int toUB1(byte[] bytes)
	{
		return getUB1(bytes, 0);
	}

	public static int toUB2(byte[] bytes)
	{
		return getUB2(bytes, 0);
	}

	public static int toUB3(byte[] bytes)
	{
		return getUB3(bytes, 0);
	}

	public static long toUB4(byte[] bytes)
	{
		return getUB4(bytes, 0);
	}

	public static string toString(byte[] bytes, string encoding)
	{
		return Encoding.GetEncoding(encoding).GetString(bytes);
	}

	public static int indexOf(sbyte[] subByteArray, sbyte[] totalByteArray)
	{
		int result = -1;
		int num = totalByteArray.Length - subByteArray.Length;
		if (num < 0)
		{
			return result;
		}
		for (int i = 0; i <= num; i++)
		{
			int j;
			for (j = 0; j < subByteArray.Length; j++)
			{
				int num2 = subByteArray[j] & 0xFF;
				int num3 = totalByteArray[i + j] & 0xFF;
				if (num2 != num3)
				{
					break;
				}
			}
			if (j == subByteArray.Length)
			{
				result = i;
				break;
			}
		}
		return result;
	}
}
