using System;
using System.IO;
using System.Text;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;

namespace W.Dm;

public class Bdta
{
	public const int BDTA3_PACKAGE_NROWS = 0;

	public const int BDTA3_PACKAGE_NFLDS = 4;

	public const int BDTA3_PACKAGE_LENGTH = 6;

	public const int BDTA3_PACKAGE_ORG_LEN = 10;

	public const int BDTA3_PACKAGE_COMPRESS = 14;

	public const int BDTA3_PACKAGE_HEAD_LEN = 15;

	public const int VAR_DATA_LEN_STR = -2;

	public const int VAR_DATA_LEN_DEC = -3;

	internal static void decode(byte[][][] datas, int colNum, int rownum_offset, int cur_rownum, int nflds, b buffer, int rowidCol, ref long decodedValueBytes)
	{
		if (datas == null || colNum < 0 || rownum_offset < 0 || cur_rownum < 0 || cur_rownum > datas.Length - rownum_offset || nflds < 0 || nflds > checked(colNum + 1))
			throw new InvalidDataException("Invalid BDTA dimensions.");
		DmFrameReader.ValidateCount(nflds, 6, buffer.a(false) - 5);
		if (rowidCol < -1 || rowidCol > colNum) throw new InvalidDataException("Invalid BDTA row ID column.");
		bool flag = rowidCol >= 0 && nflds == colNum + 1;
		buffer.A(4, false, true);
		buffer.A(1, false, true);
		int[] array = new int[nflds];
		for (int i = 0; i < nflds; i++)
		{
			array[i] = buffer.D();
		}
		int[] array2 = new int[nflds];
		for (int j = 0; j < nflds; j++)
		{
			array2[j] = (int)buffer.E();
		}
		for (int k = 0; k < nflds; k++)
		{
			int num = k + 1;
			if (flag && k == rowidCol)
			{
				num = 0;
			}
			else if (flag && k > rowidCol)
			{
				num = k;
			}
			bool flag2 = buffer.d() == 1;
			bool[] array3 = null;
			if (!flag2)
			{
				DmFrameReader.ValidateCount(cur_rownum, 1, buffer.a(false));
				array3 = new bool[cur_rownum];
				for (int l = 0; l < cur_rownum; l++)
				{
					array3[l] = buffer.__t02_method_06000ABD() == 0;
				}
			}
			for (int m = 0; m < cur_rownum; m++)
			{
				if (flag2 || !array3[m])
				{
					datas[rownum_offset + m][num] = dataBytes(array[k], buffer, ref decodedValueBytes);
				}
			}
		}
		if (!flag && rowidCol >= 0)
		{
			for (int n = 0; n < cur_rownum; n++)
			{
				datas[rownum_offset + n][0] = datas[rownum_offset + n][rowidCol + 1];
			}
		}
	}

	private static int dataLength(int dtype)
	{
		int result = 0;
		switch (dtype)
		{
		case 3:
		case 5:
		case 6:
		case 7:
		case 10:
		case 13:
		case 25:
			result = 4;
			break;
		case 8:
			result = 8;
			break;
		case 0:
		case 1:
		case 2:
		case 12:
		case 17:
		case 18:
		case 19:
			result = -2;
			break;
		case 9:
			result = -3;
			break;
		case 11:
			result = 8;
			break;
		case 14:
		case 15:
		case 16:
		case 20:
		case 22:
		case 23:
		case 28:
			result = 12;
			break;
		case 26:
		case 27:
			result = 13;
			break;
		case 21:
			result = 24;
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_COLUMN_TYPE);
			break;
		}
		return result;
	}

	private static byte[] dataBytes(int dataType, b buffer, ref long decodedValueBytes)
	{
		int num = dataLength(dataType);
		int num2 = 0;
		switch (num)
		{
		case -2:
			num2 = buffer.d();
			num = buffer.d();
			break;
		case -3:
			num = buffer.d();
			break;
		}
		if (num < 0 || num2 < 0) throw new InvalidDataException("Negative BDTA value length.");
		int allocation;
		try { allocation = checked(num + num2); }
		catch (OverflowException ex) { throw new InvalidDataException("BDTA value length overflow.", ex); }
		if (num > buffer.a(false) || allocation > DmFrameReader.MaxFrameSize)
			throw new InvalidDataException("BDTA value exceeds frame data.");
		DmFrameReader.ReserveDecodedValueBytes(ref decodedValueBytes, allocation);
		byte[] array = new byte[allocation];
		buffer.A(array, 0, num);
		if (num2 == 0)
		{
			return array;
		}
		for (int i = num; i < array.Length; i++)
		{
			array[i] = 32;
		}
		return array;
	}

	internal static decimal readDecimal(B access)
	{
		int num = access.__t02_field_04000AB9.__t02_method_06000ABD();
		int num2 = access.__t02_field_04000AB9.__t02_method_06000ABD();
		int num3 = access.__t02_field_04000AB9.__t02_method_06000ABD();
		short num4 = access.__t02_field_04000AB9.C();
		int len = access.__t02_field_04000AB9.__t02_method_06000ABD();
		byte[] data = access.__t02_field_04000AB9.F(22);
		StringBuilder stringBuilder = new StringBuilder();
		int integerPart = num2 - num3;
		if (num == 128)
		{
			return decimal.MaxValue;
		}
		if (num4 >= 0)
		{
			fillFromIntegerPart(stringBuilder, num, integerPart, len, num2, num3, data);
		}
		else
		{
			fillFromFractionPart(stringBuilder, num, num4, len, data);
		}
		return decimal.Parse(stringBuilder.ToString());
	}

	private static void fillFromIntegerPart(StringBuilder sb, int sign, int integerPart, int len, int precision, int scale, byte[] data)
	{
		if (sign != 193)
		{
			sb.Append("-");
		}
		if (integerPart % 2 != 0)
		{
			integerPart++;
		}
		int num = len * 2;
		for (int i = 1; 2 * i <= num; i++)
		{
			byte index = data[i];
			if (!checkStop(index, sb, precision, scale))
			{
				findDecXandY(index, sign, sb);
				if (2 * i == integerPart)
				{
					sb.Append(".");
				}
				continue;
			}
			break;
		}
	}

	private static bool checkStop(byte index, StringBuilder sb, int precision, int scale)
	{
		int num = 0;
		bool flag = index == 0;
		if (index == 102)
		{
			num++;
			flag = true;
		}
		if (scale == 0 && flag)
		{
			int num2 = sb.Length;
			if (sb[num] == '0')
			{
				num2 = num2 - num - 1;
			}
			for (int i = 0; i < precision - num2; i++)
			{
				sb.Append(0);
			}
		}
		return flag;
	}

	private static void fillFromFractionPart(StringBuilder sb, int sign, int weight, int len, byte[] data)
	{
		if (sign != 193)
		{
			sb.Append("-");
		}
		sb.Append(".");
		if (weight < -1)
		{
			int num = -weight - 1;
			for (int i = 0; i < num; i++)
			{
				sb.Append("00");
			}
		}
		for (int j = 1; j <= len; j++)
		{
			byte b2 = data[j];
			if (b2 != 0 && b2 != 102)
			{
				findDecXandY(b2, sign, sb);
				continue;
			}
			break;
		}
	}

	private static void findDecXandY(byte index, int sign, StringBuilder sb)
	{
		if (sign != 193)
		{
			index = (byte)(102 - index);
		}
		int num = index / 10;
		int num2 = index % 10 - 1;
		if (num2 < 0)
		{
			int num3 = num * 10 + num2;
			if (num3 < 10)
			{
				sb.Append(0);
				sb.Append(num3);
			}
			else
			{
				sb.Append(num3);
			}
		}
		else
		{
			sb.Append(num);
			sb.Append(num2);
		}
	}

	internal static string ReadString(B access)
	{
		int num = access.__t02_field_04000AB9.d();
		int num2 = access.__t02_field_04000AB9.d();
		byte[] array = new byte[num2 + num];
		access.__t02_field_04000AB9.A(array, 0, num2);
		if (num == 0)
		{
			return Encoding.GetEncoding(access.a().ServerEncoding).GetString(array);
		}
		for (int i = num2; i < array.Length; i++)
		{
			array[i] = 32;
		}
		return Encoding.GetEncoding(access.a().ServerEncoding).GetString(array);
	}
}
