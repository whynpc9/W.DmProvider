using System;

namespace Dm.util;

internal class BEByteUtil
{
	public static byte[] fromLong(long l)
	{
		return new byte[8]
		{
			(byte)(l >> 56),
			(byte)(l >> 48),
			(byte)(l >> 40),
			(byte)(l >> 32),
			(byte)(l >> 24),
			(byte)(l >> 16),
			(byte)(l >> 8),
			(byte)l
		};
	}

	public static int setUB1(byte[] bytes, int offset, int i)
	{
		bytes[offset] = (byte)i;
		return 1;
	}

	public static int setUB2(byte[] bytes, int offset, int i)
	{
		bytes[offset++] = (byte)(i >> 8);
		bytes[offset++] = (byte)i;
		return 2;
	}

	public static int setUB3(byte[] bytes, int offset, int i)
	{
		bytes[offset++] = (byte)(i >> 16);
		bytes[offset++] = (byte)(i >> 8);
		bytes[offset++] = (byte)i;
		return 3;
	}

	public static int setUB4(byte[] bytes, int offset, long l)
	{
		bytes[offset++] = (byte)(l >> 24);
		bytes[offset++] = (byte)(l >> 16);
		bytes[offset++] = (byte)(l >> 8);
		bytes[offset++] = (byte)l;
		return 4;
	}

	public static int setBytes(byte[] bytes, int offset, byte[] srcBytes)
	{
		Array.Copy(srcBytes, 0, bytes, offset, srcBytes.Length);
		return srcBytes.Length;
	}

	public static int setBytes(byte[] bytes, int offset, byte[] srcBytes, int srcOffset, int len)
	{
		Array.Copy(srcBytes, srcOffset, bytes, offset, len);
		return len;
	}

	public static long getLong(byte[] bytes, int offset)
	{
		return ((long)(bytes[offset++] & 0xFF) << 56) | ((long)(bytes[offset++] & 0xFF) << 48) | ((long)(bytes[offset++] & 0xFF) << 40) | ((long)(bytes[offset++] & 0xFF) << 32) | ((long)(bytes[offset++] & 0xFF) << 24) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 8) | (bytes[offset++] & 0xFF);
	}

	public static int getUB1(byte[] bytes, int offset)
	{
		return bytes[offset] & 0xFF;
	}

	public static int getUB2(byte[] bytes, int offset)
	{
		return ((bytes[offset++] & 0xFF) << 8) | (bytes[offset++] & 0xFF);
	}

	public static int getUB3(byte[] bytes, int offset)
	{
		return ((bytes[offset++] & 0xFF) << 16) | ((bytes[offset++] & 0xFF) << 8) | (bytes[offset++] & 0xFF);
	}

	public static long getUB4(byte[] bytes, int offset)
	{
		return ((long)(bytes[offset++] & 0xFF) << 24) | ((long)(bytes[offset++] & 0xFF) << 16) | ((long)(bytes[offset++] & 0xFF) << 8) | (bytes[offset++] & 0xFF);
	}

	public static byte[] getBytes(byte[] bytes, int offset, int len)
	{
		byte[] array = new byte[len];
		Array.Copy(bytes, offset, array, 0, len);
		return array;
	}

	public static int getBytes(byte[] bytes, int offset, byte[] objBytes)
	{
		return getBytes(bytes, offset, objBytes, 0, objBytes.Length);
	}

	public static int getBytes(byte[] bytes, int offset, byte[] objBytes, int objOffset, int len)
	{
		Array.Copy(bytes, offset, objBytes, objOffset, len);
		return len;
	}

	public static long toLong(byte[] bytes)
	{
		return getLong(bytes, 0);
	}
}
