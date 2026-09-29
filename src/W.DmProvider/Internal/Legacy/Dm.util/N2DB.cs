using System;
using System.Globalization;

namespace W.Dm.util;

internal class N2DB
{
	public const double MAX_REAL = 3.4E+38;

	public const double MIN_REAL = -3.4E+38;

	public const long THIRTEEN_TIMESTAMP_BOUND = 999999999999L;

	public static byte[] fromBoolean(bool val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 3:
			case 5:
			case 6:
			case 7:
			case 8:
			case 9:
			case 10:
			case 11:
			case 13:
			case 19:
				result = toBit(val ? 1 : 0);
				break;
			case 12:
			case 17:
			case 18:
				if (DmSqlType.isComplexType(paraInternal.GetCType(), paraInternal.GetScale()))
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = toBit(val ? 1 : 0);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromLong(long val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 3:
			case 13:
				checkBit(val);
				result = toBit(val);
				break;
			case 5:
				checkTinyint(val);
				result = toTinyint((sbyte)val);
				break;
			case 6:
				checkSmallint(val);
				result = toSmallint((short)val);
				break;
			case 7:
				checkInt(val);
				result = toInt((int)val);
				break;
			case 8:
				checkBigint(val);
				result = toBigint(val);
				break;
			case 10:
				checkReal(val);
				result = toReal(val);
				break;
			case 11:
				result = toDouble(val);
				break;
			case 9:
			case 24:
				result = toDecimal(val.ToString(), paraInternal.GetPrecision(), paraInternal.GetScale(), direct: true);
				break;
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(Convert.ToString(val), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 12:
			case 17:
			case 18:
				if (DmSqlType.isComplexType(paraInternal.GetCType(), paraInternal.GetScale()))
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = toBinary(val, paraInternal.GetPrecision());
				break;
			case 28:
				result = DmRowId.valueOf(val).encode(connection);
				break;
			case 20:
			{
				int leadScale = (paraInternal.GetScale() >> 4) & 0xF;
				result = fromDmIntervalYM(new DmIntervalYM(val, leadScale), paraInternal, connection);
				break;
			}
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromULong(ulong val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 3:
			case 13:
				checkBit((long)val);
				result = toBit((long)val);
				break;
			case 5:
				checkTinyint(val);
				result = toTinyint((byte)val);
				break;
			case 6:
				checkSmallint(val);
				result = toSmallint((short)val);
				break;
			case 7:
				checkInt(val);
				result = toInt((int)val);
				break;
			case 8:
				checkBigint(val);
				result = toBigint((long)val);
				break;
			case 10:
				checkReal(val);
				result = toReal(val);
				break;
			case 11:
				result = toDouble(val);
				break;
			case 9:
			case 24:
				result = toDecimal(val.ToString(), paraInternal.GetPrecision(), paraInternal.GetScale(), direct: true);
				break;
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(Convert.ToString(val), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 12:
			case 17:
			case 18:
				if (DmSqlType.isComplexType(paraInternal.GetCType(), paraInternal.GetScale()))
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = toBinary((long)val, paraInternal.GetPrecision());
				break;
			case 28:
				result = DmRowId.valueOf((long)val).encode(connection);
				break;
			case 20:
			{
				int leadScale = (paraInternal.GetScale() >> 4) & 0xF;
				result = fromDmIntervalYM(new DmIntervalYM((long)val, leadScale), paraInternal, connection);
				break;
			}
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromFloat(float val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 3:
			case 13:
				result = toBit((!isFloatEqualsZero(val)) ? 1 : 0);
				break;
			case 5:
				checkTinyint(val);
				result = toTinyint((byte)val);
				break;
			case 6:
				checkSmallint(val);
				result = toSmallint((short)val);
				break;
			case 7:
				checkInt(val);
				result = toInt((int)val);
				break;
			case 8:
				checkBigint(val);
				result = toBigint((long)val);
				break;
			case 10:
				checkReal(val);
				result = toReal(val);
				break;
			case 11:
				result = toDouble(val);
				break;
			case 9:
				result = toDecimal(val.ToString(), paraInternal.GetPrecision(), paraInternal.GetScale(), direct: true);
				break;
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(Convert.ToString(val), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static bool isFloatEqualsZero(double val)
	{
		return Math.Abs(val - 0.0) < 9.999999974752427E-07;
	}

	public static byte[] fromDouble(double val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 3:
			case 13:
				result = toBit((!isFloatEqualsZero(val)) ? 1 : 0);
				break;
			case 5:
				checkTinyint(val);
				result = toTinyint((byte)val);
				break;
			case 6:
				checkSmallint(val);
				result = toSmallint((short)val);
				break;
			case 7:
				checkInt(val);
				result = toInt((int)val);
				break;
			case 8:
				checkBigint(val);
				result = toBigint((long)val);
				break;
			case 10:
				checkReal(val);
				result = toReal((float)val);
				break;
			case 11:
				result = toDouble(val);
				break;
			case 9:
				result = toDecimal(val.ToString(), paraInternal.GetPrecision(), paraInternal.GetScale(), direct: true);
				break;
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(Convert.ToString(val), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromString(string val, DmField paraInternal, DmConnection connection)
	{
		int precision = paraInternal.GetPrecision();
		int cType = paraInternal.GetCType();
		int scale = paraInternal.GetScale();
		try
		{
			if (string.IsNullOrEmpty(val))
			{
				return new byte[0];
			}
			byte[] result = null;
			switch (cType)
			{
			case 0:
			case 1:
			case 2:
				result = toVarchar(val, connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 19:
				result = toClob(val, paraInternal, connection);
				break;
			case 3:
			case 13:
				result = toBit(Convert.ToBoolean(val) ? 1 : 0);
				break;
			case 5:
				result = toTinyint(Convert.ToInt64(val));
				break;
			case 6:
				result = toSmallint(Convert.ToInt64(val));
				break;
			case 7:
				result = toInt(Convert.ToInt64(val));
				break;
			case 8:
				result = toBigint(Convert.ToInt64(val));
				break;
			case 10:
				result = toReal(Convert.ToSingle(val));
				break;
			case 11:
				result = toDouble(Convert.ToDouble(val));
				break;
			case 9:
			case 24:
				result = toDecimal(val, precision, scale, direct: true);
				break;
			case 17:
				result = toBinary(val, precision);
				break;
			case 12:
			case 18:
				result = toVarBinaryOrBlob(val, cType, scale, precision);
				break;
			case 14:
			case 15:
			case 16:
			case 22:
			case 23:
				result = DmDateTime.valueOf(val, paraInternal, connection).encode(paraInternal, connection);
				break;
			case 21:
			{
				int secScale = scale & 0xF;
				int leadScale = (scale >> 4) & 0xF;
				byte type = (byte)((scale >> 8) & 0xF);
				result = fromDmIntervalDT(new DmIntervalDT(val, type, leadScale, secScale), paraInternal, connection);
				break;
			}
			case 20:
				result = fromDmIntervalYM(new DmIntervalYM(val, scale), paraInternal, connection);
				break;
			case 28:
				result = DmRowId.valueOf(val).encode(connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	internal static void adjustTime(int[] dt, int scale, string val)
	{
		dt = OracleDateFormat.Round(dt, scale);
		if (val.StartsWith("-"))
		{
			dt[0] = -dt[0];
		}
	}

	public static byte[] toVarBinaryOrBlob(string val, int cType, int scale, int prec)
	{
		if (DmSqlType.isComplexType(cType, scale))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		byte[] array = (byte[])(object)StringUtil.hexStringToBytes(val);
		byte[] array2;
		if (prec < array.Length && prec != 0)
		{
			array2 = new byte[prec];
			Array.Copy(array, 0, array2, 0, prec);
		}
		else
		{
			array2 = array;
		}
		return array2;
	}

	public static byte[] toBinary(string val, int prec)
	{
		byte[] array = (byte[])(object)StringUtil.hexStringToBytes(val);
		int num = array.Length;
		byte[] array2;
		if (prec == num || prec == 0)
		{
			array2 = array;
		}
		else
		{
			array2 = new byte[prec];
			if (prec > num)
			{
				Array.Copy(array, 0, array2, 0, num);
				for (int i = num; i < prec; i++)
				{
					array2[i] = 0;
				}
			}
			else
			{
				Array.Copy(array, 0, array2, 0, prec);
			}
		}
		return array2;
	}

	public static byte[] fromStringBytes(string val, byte[] valBytes, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] array = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
				array = valBytes;
				break;
			case 19:
				array = changeOffRowData(paraInternal, valBytes, connection);
				break;
			default:
				return fromString(val, paraInternal, connection);
			}
			return array;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromBytes(byte[] val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 5:
				if (val.Length > 1)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = val;
				break;
			case 6:
				if (val.Length > 2)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = val;
				break;
			case 7:
				if (val.Length > 4)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = val;
				break;
			case 8:
				if (val.Length > 8)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				result = val;
				break;
			case 0:
			case 1:
			case 2:
				result = toVarchar(val);
				break;
			case 19:
				result = toClob(val, paraInternal, connection);
				break;
			case 17:
			case 18:
				result = toVarbinary(val);
				break;
			case 12:
				result = toBlob(val, paraInternal, connection);
				break;
			case 117:
			case 119:
			case 121:
			case 122:
			{
				ComplexTypeDesc typeDescriptor = paraInternal.typeDescriptor;
				if (typeDescriptor != null)
				{
					result = ComplexTypeData.objBlobToBytes(val, typeDescriptor);
				}
				else
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				break;
			}
			case 28:
				result = DmRowId.valueOf(val).encode(connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromDate(DateTime val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 14:
				result = DmDateTime.DateEncodeFast(new DmDateTime(val, paraInternal.GetPrecision(), paraInternal.GetScale(), 0, connection.GetConnInstance().ConnProperty.TimeZone).GetByteArrayValue());
				break;
			case 16:
			case 23:
				result = fromTimestamp(val, paraInternal, connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromTime(DateTime val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 15:
				result = DmTime.TimeEncodeFast(val);
				break;
			case 22:
				result = DmTime.TimeTzEncodeFast(val, connection.GetConnInstance().ConnProperty.TimeZone);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromTime(DmTime val, DmField paraInternal, DmConnection connection)
	{
		if (val == null)
		{
			return new byte[0];
		}
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 15:
				result = DmTime.TimeEncodeFast(val.GetByteArrayValue());
				break;
			case 22:
				result = DmTime.TimeTzEncodeFast(val.GetTzByteArrayValue());
				break;
			case 16:
			case 23:
				result = fromTimestamp(DateTime.Parse(val.ToString(), DmConst.invariantCulture), paraInternal, connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromTimestamp(DateTime val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] ret = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				ret = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 14:
				ret = fromDate(val, paraInternal, connection);
				break;
			case 15:
			case 22:
				ret = fromTime(val, paraInternal, connection);
				break;
			case 16:
			{
				DmDateTime dmDateTime = new DmDateTime(val, paraInternal.GetPrecision(), paraInternal.GetScale(), 2, connection.GetConnInstance().ConnProperty.TimeZone);
				byte[] ret2 = null;
				dmDateTime.GetByteArrayValue(ref ret2);
				if (ret2.Length != 8)
				{
					DmDateTime.DmdtEncodeFast(ref ret, ref ret2);
				}
				break;
			}
			case 23:
				ret = DmDateTime.DmdttzEncodeFast(new DmDateTime(val, paraInternal.GetPrecision(), paraInternal.GetScale(), 3, connection.GetConnInstance().ConnProperty.TimeZone).GetByteArrayValue());
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return ret;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromTimeTZ(DateTimeOffset val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				result = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 15:
				result = DmTime.TimeEncodeFast(new DmTime(val.DateTime, paraInternal.GetPrecision(), paraInternal.GetScale(), Convert.ToInt16(val.Offset.TotalMinutes)).GetByteArrayValue());
				break;
			case 22:
				result = DmTime.TimeTzEncodeFast(new DmTime(val.DateTime, paraInternal.GetPrecision(), paraInternal.GetScale(), Convert.ToInt16(val.Offset.TotalMinutes)).GetTzByteArrayValue());
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromTimestampTZ(DateTimeOffset val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] ret = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
			case 19:
				ret = toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 14:
				ret = fromDate(val.DateTime, paraInternal, connection);
				break;
			case 15:
				ret = fromTime(val.DateTime, paraInternal, connection);
				break;
			case 22:
				ret = fromTimeTZ(val, paraInternal, connection);
				break;
			case 16:
			{
				DmDateTime dmDateTime = new DmDateTime(val.DateTime, paraInternal.GetPrecision(), paraInternal.GetScale(), 2, Convert.ToInt16(val.Offset.TotalMinutes));
				byte[] ret2 = null;
				dmDateTime.GetByteArrayValue(ref ret2);
				if (ret2.Length != 8)
				{
					DmDateTime.DmdtEncodeFast(ref ret, ref ret2);
				}
				break;
			}
			case 23:
				ret = DmDateTime.DmdttzEncodeFast(new DmDateTime(val.DateTime, paraInternal.GetPrecision(), paraInternal.GetScale(), 3, Convert.ToInt16(val.Offset.TotalMinutes)).GetByteArrayValue());
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return ret;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromDmIntervalYM(DmIntervalYM val, DmField paraInternal, DmConnection connection)
	{
		if (val == null)
		{
			return new byte[0];
		}
		try
		{
			return (paraInternal.GetCType() != 20) ? toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding) : val.encode(paraInternal.GetScale());
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromDmIntervalDT(DmIntervalDT val, DmField paraInternal, DmConnection connection)
	{
		try
		{
			return (paraInternal.GetCType() != 21) ? toVarchar(val.ToString(), connection.GetConnInstance().ConnProperty.ServerEncoding) : val.encode(paraInternal.GetScale());
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromBlob(DmBlob x, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 17:
			case 18:
				result = x.GetBytes(0L, (int)x.do_length());
				break;
			case 12:
				result = toBlob(x, paraInternal, connection);
				break;
			case 117:
			case 119:
			case 121:
			case 122:
			{
				ComplexTypeDesc typeDescriptor = paraInternal.typeDescriptor;
				if (typeDescriptor != null)
				{
					result = ComplexTypeData.objBlobToBytes(x.GetBytes(0L, (int)x.do_length()), typeDescriptor);
				}
				else
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
				break;
			}
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromClob(DmClob x, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 0:
			case 1:
			case 2:
				result = ByteUtil.fromString(x.getSubString(0L, (int)x.do_length()), connection.GetConnInstance().ConnProperty.ServerEncoding);
				break;
			case 19:
				result = toClob(x, paraInternal, connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromArray(DmArray x, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 122:
				result = ComplexTypeData.sarrayToBytes(x, paraInternal.typeDescriptor);
				break;
			case 117:
			case 119:
				result = ComplexTypeData.arrayToBytes(x, paraInternal.typeDescriptor);
				break;
			case 12:
				result = toBlob(ComplexTypeData.toBytes(x, paraInternal.typeDescriptor), paraInternal, connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromStruct(DmStruct x, DmField paraInternal, DmConnection connection)
	{
		try
		{
			byte[] result = null;
			switch (paraInternal.GetCType())
			{
			case 119:
				result = ComplexTypeData.structToBytes(x, paraInternal.typeDescriptor);
				break;
			case 121:
				result = ComplexTypeData.recordToBytes(x, paraInternal.typeDescriptor);
				break;
			case 12:
				result = toBlob(ComplexTypeData.toBytes(x, paraInternal.typeDescriptor), paraInternal, connection);
				break;
			default:
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				break;
			}
			return result;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromObject(object val, DmField paraInternal, DmConnection connection)
	{
		int cType = paraInternal.GetCType();
		byte typeFlag = paraInternal.GetTypeFlag();
		int precision = paraInternal.GetPrecision();
		try
		{
			if (val == null || val is DBNull)
			{
				return new byte[0];
			}
			if (val is byte)
			{
				return fromLong((byte)val, paraInternal, connection);
			}
			if (val is sbyte)
			{
				return fromLong((sbyte)val, paraInternal, connection);
			}
			if (val is string)
			{
				return fromString((string)val, paraInternal, connection);
			}
			if (val is char)
			{
				return fromString(Convert.ToString(val), paraInternal, connection);
			}
			if (val is char[])
			{
				return fromString(Convert.ToString(val), paraInternal, connection);
			}
			if (val is decimal)
			{
				return fromBigDecimal((decimal)val, paraInternal, connection);
			}
			if (val is short)
			{
				return fromLong((short)val, paraInternal, connection);
			}
			if (val is ushort)
			{
				return fromLong((ushort)val, paraInternal, connection);
			}
			if (val is int)
			{
				return fromLong((int)val, paraInternal, connection);
			}
			if (val is uint)
			{
				return fromLong((uint)val, paraInternal, connection);
			}
			if (val is long)
			{
				return fromLong((long)val, paraInternal, connection);
			}
			if (val is ulong)
			{
				return fromULong((ulong)val, paraInternal, connection);
			}
			if (val is float)
			{
				return fromDouble((float)val, paraInternal, connection);
			}
			if (val is double)
			{
				return fromDouble((double)val, paraInternal, connection);
			}
			if (val is byte[])
			{
				return fromBytes((byte[])val, paraInternal, connection);
			}
			if (val is sbyte[])
			{
				return fromBytes((byte[])val, paraInternal, connection);
			}
			if (val is DateTime)
			{
				return fromTimestamp((DateTime)val, paraInternal, connection);
			}
			if (val is DateTimeOffset)
			{
				return fromTimestampTZ((DateTimeOffset)val, paraInternal, connection);
			}
			if (val is bool)
			{
				return fromBoolean((bool)val, paraInternal, connection);
			}
			if (val is DmTime)
			{
				return fromTime((DmTime)val, paraInternal, connection);
			}
			if (val is DmIntervalYM)
			{
				return fromDmIntervalYM((DmIntervalYM)val, paraInternal, connection);
			}
			if (val is DmIntervalDT)
			{
				return fromDmIntervalDT((DmIntervalDT)val, paraInternal, connection);
			}
			if (val is TimeSpan timeSpan)
			{
				return cType switch
				{
					21 => fromDmIntervalDT(new DmIntervalDT(timeSpan, paraInternal.GetScale()), paraInternal, connection), 
					15 => fromTime(new DateTime(timeSpan.Ticks), paraInternal, connection), 
					22 => fromTime(new DateTime(timeSpan.Ticks), paraInternal, connection), 
					_ => fromString(val.ToString(), paraInternal, connection), 
				};
			}
			if (val is DmXDec)
			{
				return fromDmDecimal((DmXDec)val, paraInternal, connection);
			}
			if (val is Guid)
			{
				if (typeFlag == 1)
				{
					if ((cType != 0 && cType != 2 && cType != 1) || (precision != 36 && precision != 8188))
					{
						throw new InvalidCastException("not support this GUID cast");
					}
					fromString(((Guid)val/*cast due to constrained. prefix*/).ToString(), paraInternal, connection);
				}
				else
				{
					fromString(((Guid)val/*cast due to constrained. prefix*/).ToString(), paraInternal, connection);
				}
			}
			if (val.GetType().BaseType == typeof(Enum))
			{
				string[] names = Enum.GetNames(val.GetType());
				int num = 0;
				string[] array = names;
				for (int i = 0; i < array.Length; i++)
				{
					if (array[i].Equals(val.ToString()))
					{
						return toInt(num);
					}
					num++;
				}
				throw new SystemException("Value is of unknown data type");
			}
			if (val is DmBlob)
			{
				DmBlob dmBlob = (DmBlob)val;
				byte[] bytes = dmBlob.GetBytes(0L, (int)dmBlob.do_length());
				byte[] array2;
				if (precision < bytes.Length && precision != 0)
				{
					array2 = new byte[precision];
					Array.Copy(bytes, 0, array2, 0, precision);
				}
				else
				{
					array2 = bytes;
				}
				return array2;
			}
			if (val is DmClob)
			{
				DmClob dmClob = (DmClob)val;
				return ByteUtil.fromString(dmClob.getSubString(0L, (int)dmClob.do_length()), connection.GetConnInstance().ConnProperty.ServerEncoding);
			}
			DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORTED_TYPE);
			return null;
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return null;
		}
	}

	public static byte[] fromDmDecimal(DmXDec val, DmField paraInternal, DmConnection connection)
	{
		if (val == null)
		{
			return new byte[0];
		}
		switch (paraInternal.GetCType())
		{
		case 9:
			return new DmXDec().StrToDec(val.ToString(), paraInternal.GetPrecision(), paraInternal.GetScale(), dmxdec_direct: true);
		case 3:
		case 5:
			return toTinyint(Convert.ToSByte(val.ToString()));
		case 6:
			return toSmallint(Convert.ToInt16(val.ToString()));
		case 7:
			return toInt(Convert.ToInt32(val.ToString()));
		case 8:
			return toBigint(Convert.ToInt64(val.ToString()));
		case 10:
			return toReal(Convert.ToSingle(val.ToString()));
		case 11:
			return fromDouble(Convert.ToDouble(val.ToString()), paraInternal, connection);
		case 0:
		case 1:
		case 2:
		case 19:
			return fromString(val.ToString(), paraInternal, connection);
		default:
			throw new InvalidCastException();
		}
	}

	public static byte[] fromBigDecimal(decimal? val, DmField paraInternal, DmConnection connection)
	{
		int cType = paraInternal.GetCType();
		int precision = paraInternal.GetPrecision();
		int scale = paraInternal.GetScale();
		byte[] ret = null;
		if (!val.HasValue)
		{
			return new byte[0];
		}
		switch (cType)
		{
		case 9:
			new DmXDec().StrToDec(ref ret, val.ToString(), precision, scale, dmxdec_direct: false);
			break;
		case 3:
			ret = toBit(decimal.ToByte(val.Value));
			break;
		case 5:
			ret = toTinyint(decimal.ToByte(val.Value));
			break;
		case 6:
			ret = toSmallint(decimal.ToInt16(val.Value));
			break;
		case 7:
			ret = toInt(decimal.ToInt32(val.Value));
			break;
		case 8:
			ret = toBigint(decimal.ToInt64(val.Value));
			break;
		case 10:
			ret = toReal(decimal.ToSingle(val.Value));
			break;
		case 11:
			ret = toDouble(decimal.ToDouble(val.Value));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			ret = fromString(val.ToString(), paraInternal, connection);
			break;
		default:
			throw new InvalidCastException();
		}
		return ret;
	}

	public static byte[] toBit(long val)
	{
		return new byte[1] { (val != 0) ? ((byte)1) : ((byte)0) };
	}

	public static byte[] toTinyint(long val)
	{
		checkTinyint(val);
		byte[] array = new byte[1];
		ByteUtil.setByte(array, 0, (byte)val);
		return array;
	}

	public static byte[] toSmallint(long val)
	{
		checkSmallint(val);
		byte[] array = new byte[2];
		ByteUtil.setShort(array, 0, (short)val);
		return array;
	}

	public static byte[] toInt(long val)
	{
		checkInt(val);
		byte[] array = new byte[4];
		ByteUtil.setInt(array, 0, (int)val);
		return array;
	}

	public static byte[] toBigint(long val)
	{
		checkBigint(val);
		byte[] array = new byte[8];
		ByteUtil.setLong(array, 0, val);
		return array;
	}

	public static byte[] toReal(float val)
	{
		byte[] array = new byte[4];
		ByteUtil.setInt(array, 0, FloatToIntBits(val));
		return array;
	}

	public static int FloatToIntBits(float value)
	{
		byte[] bytes = BitConverter.GetBytes(value);
		if (!BitConverter.IsLittleEndian)
		{
			Array.Reverse(bytes);
		}
		return BitConverter.ToInt32(bytes, 0);
	}

	public static byte[] toDouble(double val)
	{
		byte[] array = new byte[8];
		ByteUtil.setLong(array, 0, BitConverter.DoubleToInt64Bits(val));
		return array;
	}

	public static byte[] toDecimal(string val, int prec, int scale, bool direct)
	{
		if (val.Trim().Length > 19)
		{
			return new DmXDec().StrToDec(val, prec, scale, direct);
		}
		if (val.Trim().Length > 0)
		{
			return new DmXDec().StrToDec(decimal.Parse(val, NumberStyles.Any, DmConst.invariantCulture).ToString(), prec, scale, direct);
		}
		return new byte[0];
	}

	public static byte[] toVarchar(string val, string serverEncoding)
	{
		return ByteUtil.fromString(val, serverEncoding);
	}

	public static byte[] toVarchar(byte[] bsArr)
	{
		if (bsArr == null || bsArr.Length == 0)
		{
			return new byte[0];
		}
		byte[] array = new byte[bsArr.Length * 2];
		for (int i = 0; i < bsArr.Length; i++)
		{
			byte[] array2 = toChar(bsArr[i]);
			array[i * 2] = array2[0];
			array[i * 2 + 1] = array2[1];
		}
		return array;
	}

	public static byte[] toChar(byte bt)
	{
		return new byte[2]
		{
			getCharByNumVal((bt >> 4) & 0xF),
			getCharByNumVal(bt & 0xF)
		};
	}

	public static byte getCharByNumVal(int val)
	{
		if (val >= 0 && val <= 9)
		{
			return (byte)(val + 48);
		}
		if (val >= 10 && val <= 15)
		{
			return (byte)(val + 65 - 10);
		}
		DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_HEX);
		return 0;
	}

	public static byte[] toBinary(long x, int prec)
	{
		byte[] array = new byte[8];
		int num = 7;
		array[num--] = (byte)(x & 0xFF);
		array[num--] = (byte)((ulong)x >> 8);
		array[num--] = (byte)((ulong)x >> 16);
		array[num--] = (byte)((ulong)x >> 24);
		array[num--] = (byte)((ulong)x >> 32);
		array[num--] = (byte)((ulong)x >> 40);
		array[num--] = (byte)((ulong)x >> 48);
		array[num--] = (byte)((ulong)x >> 56);
		if (prec > 0 && prec < array.Length)
		{
			byte[] array2 = new byte[prec];
			Array.Copy(array, array.Length - prec, array2, 0, prec);
			return array2;
		}
		return array;
	}

	public static byte[] toBlob(DmBlob x, DmField paraInternal, DmConnection connection)
	{
		return x.GetBytes(0L, (int)x.do_length());
	}

	public static byte[] toBlob(byte[] bytes, DmField paraInternal, DmConnection connection)
	{
		return changeOffRowData(paraInternal, bytes, connection);
	}

	public static byte[] toClob(DmClob x, DmField paraInternal, DmConnection connection)
	{
		return ByteUtil.fromString(x.getSubString(0L, (int)x.do_length()), connection.GetConnInstance().ConnProperty.ServerEncoding);
	}

	public static byte[] toClob(string val, DmField paraInternal, DmConnection connection)
	{
		return changeOffRowData(paraInternal, ByteUtil.fromString(val, connection.GetConnInstance().ConnProperty.ServerEncoding), connection);
	}

	public static byte[] toClob(byte[] bytes, DmField paraInternal, DmConnection connection)
	{
		return changeOffRowData(paraInternal, toVarchar(bytes), connection);
	}

	public static byte[] toVarbinary(byte[] bs)
	{
		return bs;
	}

	public static sbyte[] toVarbinary(string str)
	{
		return StringUtil.hexStringToBytes(str);
	}

	public static bool isOffRow(int dtype, long len, DmConnection conn)
	{
		DmError.ThrowUnsupportedException();
		return false;
	}

	public static byte[] changeOffRowData(DmField para, byte[] paramData, DmConnection conn)
	{
		return paramData;
	}

	public static void checkReal(double val)
	{
		if (!double.IsNegativeInfinity(val) && !double.IsPositiveInfinity(val) && !double.IsNaN(val) && (val < -3.4E+38 || val > 3.4E+38))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public static void checkBigint(double val)
	{
		if (val < -9.223372036854776E+18 || val > 9.223372036854776E+18)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public static void checkInt(double val)
	{
		if (val < -2147483648.0 || val > 2147483647.0)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public static void checkSmallint(double val)
	{
		if (val < -32768.0 || val > 32767.0)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public static void checkTinyint(double val)
	{
		if (val < -128.0 || val > 127.0)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	public static void checkBit(long val)
	{
		if (val != 0L && val != 1)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}
}
