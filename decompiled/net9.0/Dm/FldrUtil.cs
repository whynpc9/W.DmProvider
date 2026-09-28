using System;
using Dm.util;

namespace Dm;

public class FldrUtil
{
	public static readonly object lockObject = new object();

	public static byte[] fromDate(DateTime dateTime)
	{
		lock (lockObject)
		{
			byte[] array = new byte[13];
			byte[] array2 = ByteUtil.fromShort((short)dateTime.Year);
			array[0] = array2[0];
			array[1] = array2[1];
			array[2] = (byte)dateTime.Month;
			array[3] = (byte)dateTime.Day;
			array[4] = (byte)dateTime.Hour;
			array[5] = (byte)dateTime.Minute;
			array[6] = (byte)dateTime.Second;
			int millisecond = dateTime.Millisecond;
			array[7] = (byte)(millisecond & 0xFF);
			array[8] = (byte)((millisecond >> 8) & 0xFF);
			array[9] = (byte)((millisecond >> 16) & 0xFF);
			array[12] = (byte)((millisecond >> 24) & 0xFF);
			return array;
		}
	}

	public static byte[] fromDate12(DateTime dateTime)
	{
		lock (lockObject)
		{
			byte[] array = new byte[12];
			byte[] array2 = ByteUtil.fromShort((short)dateTime.Year);
			array[0] = array2[0];
			array[1] = array2[1];
			array[2] = (byte)dateTime.Month;
			array[3] = (byte)dateTime.Day;
			array[4] = (byte)dateTime.Hour;
			array[5] = (byte)dateTime.Minute;
			array[6] = (byte)dateTime.Second;
			int millisecond = dateTime.Millisecond;
			array[7] = (byte)(millisecond & 0xFF);
			array[8] = (byte)((millisecond >> 8) & 0xFF);
			array[9] = (byte)((millisecond >> 16) & 0xFF);
			byte[] array3 = ByteUtil.fromShort((short)(TimeZoneInfo.Local.BaseUtcOffset.TotalMilliseconds / 60000.0));
			array[10] = array3[0];
			array[11] = array3[1];
			return array;
		}
	}
}
