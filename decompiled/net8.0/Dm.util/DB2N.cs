using System;
using System.Collections.Generic;

namespace Dm.util;

internal class DB2N
{
	internal static string toString(byte[] bytes, DmField column, DmConnection connection)
	{
		string result = null;
		column.GetCType();
		switch (column.GetCType())
		{
		case 0:
		case 1:
		case 2:
			result = charToString(bytes, column, connection);
			break;
		case 3:
		case 5:
		case 13:
			result = bytes[0].ToString();
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0).ToString();
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0).ToString();
			break;
		case 8:
			result = ByteUtil.getLong(bytes, 0).ToString();
			break;
		case 10:
			result = ByteUtil.getFloat(bytes, 0).ToString();
			break;
		case 11:
			result = ByteUtil.getDouble(bytes, 0).ToString();
			break;
		case 9:
			result = decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()).ToString();
			break;
		case 17:
		case 18:
			result = StringUtil.bytesToHexString(bytes, pre: false);
			break;
		case 12:
		{
			if (DmSqlType.isComplexType(column.GetCType(), column.GetScale()))
			{
				result = ((bytes != null) ? toComplexType(bytes, column, connection.m_ConnInst).ToString() : null);
				break;
			}
			DmBlob dmBlob = DmBlob.newInstanceFromDB(bytes, connection.m_ConnInst, column, fetchAll: true);
			result = StringUtil.bytesToHexString(dmBlob.GetBytes(0L, (int)dmBlob.do_length()));
			break;
		}
		case 19:
		{
			DmClob dmClob = DmClob.newInstance(bytes, connection.m_ConnInst, column, fetchAll: true);
			result = dmClob.getSubString(0L, (int)dmClob.do_length());
			break;
		}
		case 14:
		case 15:
		case 16:
		case 22:
		case 23:
		case 26:
		case 27:
			result = DmDateTime.valueOf(bytes, column, connection).toString(column, connection);
			break;
		case 21:
			result = new DmIntervalDT(bytes).GetDTString();
			break;
		case 20:
			result = new DmIntervalYM(bytes).ToString();
			break;
		case 117:
			result = ComplexTypeData.bytesToArray(bytes, null, column.ComplexTypeDesc).ToString();
			break;
		case 122:
			result = ComplexTypeData.bytesToSArray(bytes, null, column.ComplexTypeDesc).ToString();
			break;
		case 119:
			result = ComplexTypeData.bytesToObj(bytes, null, column.ComplexTypeDesc).ToString();
			break;
		case 121:
			result = ComplexTypeData.bytesToRecord(bytes, null, column.ComplexTypeDesc).ToString();
			break;
		case 28:
			result = DmRowId.valueOf(bytes).toString();
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static bool toBoolean(byte[] bytes, DmField column, DmConnection connection)
	{
		bool result = false;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = bytes[0] != 0;
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0) != 0;
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0) != 0;
			break;
		case 8:
			result = ByteUtil.getLong(bytes, 0) != 0;
			break;
		case 10:
			result = ByteUtil.getFloat(bytes, 0) != 0f;
			break;
		case 11:
			result = ByteUtil.getDouble(bytes, 0) != 0.0;
			break;
		case 9:
			result = !new DmdbNumeric(bytes, column.GetPrecision(), column.GetScale()).isZero();
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			result = Convert.ToBoolean(charToString(bytes, column, connection));
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static byte toByte(byte[] bytes, DmField column, DmConnection connection)
	{
		byte result = 0;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = (byte)((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = (byte)ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = (byte)ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = (byte)ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = (byte)ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = (byte)ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToByte(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = byte.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		}
		case 12:
		case 17:
		case 18:
			result = (byte)binaryToLong(bytes, column, connection);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static short toShort(byte[] bytes, DmField column, DmConnection connection)
	{
		short result = 0;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = (short)((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = (short)ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = (short)ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = (short)ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = (short)ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToInt16(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = short.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		}
		case 12:
		case 17:
		case 18:
			result = (short)binaryToLong(bytes, column, connection);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static int toInt(byte[] bytes, DmField column, DmConnection connection)
	{
		int result = 0;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = ((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = (int)ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = (int)ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = (int)ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToInt32(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = int.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		}
		case 12:
		case 17:
		case 18:
			result = (int)binaryToLong(bytes, column, connection);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static long toLong(byte[] bytes, DmField column, DmConnection connection)
	{
		long result = 0L;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = (uint)((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = (long)ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = (long)ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToInt64(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = long.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		}
		case 12:
		case 17:
		case 18:
			result = binaryToLong(bytes, column, connection);
			break;
		case 28:
			return DmRowId.valueOf(bytes).longValue(connection);
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static float toFloat(byte[] bytes, DmField column, DmConnection connection)
	{
		float result = 0f;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = ((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = (float)ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToSingle(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = float.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
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

	internal static double toDouble(byte[] bytes, DmField column, DmConnection connection)
	{
		double result = 0.0;
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = ((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			result = ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			result = ByteUtil.getLong(bytes, 0);
			break;
		case 10:
			result = ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			result = ByteUtil.getDouble(bytes, 0);
			break;
		case 9:
			result = Convert.ToDouble(decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = double.Parse(StringUtil.trimToEmpty(str));
			}
			catch (Exception)
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

	internal static decimal toBigDecimal(byte[] bytes, DmField column, DmConnection connection, int outScale)
	{
		decimal num = toBigDecimal(bytes, column, connection);
		if (outScale != -1)
		{
			num = num.setScale(outScale, MidpointRounding.AwayFromZero);
		}
		return num;
	}

	internal static decimal toBigDecimal(byte[] bytes, DmField column, DmConnection connection)
	{
		decimal result = default(decimal);
		switch (column.GetCType())
		{
		case 3:
		case 5:
		case 13:
			result = new decimal((bytes != null && bytes.Length != 0) ? bytes[0] : 0);
			break;
		case 6:
			result = new decimal(ByteUtil.getShort(bytes, 0));
			break;
		case 7:
			result = new decimal(ByteUtil.getInt(bytes, 0));
			break;
		case 8:
			result = new decimal(ByteUtil.getLong(bytes, 0));
			break;
		case 10:
			result = new decimal(ByteUtil.getFloat(bytes, 0));
			break;
		case 11:
			result = new decimal(ByteUtil.getDouble(bytes, 0));
			break;
		case 9:
			return decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle());
		case 0:
		case 1:
		case 2:
		case 19:
		{
			string str = charToString(bytes, column, connection);
			try
			{
				result = decimal.Parse(StringUtil.trimToEmpty(str));
				return result;
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		}
		case 28:
			return new decimal(DmRowId.valueOf(bytes).longValue(connection));
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	internal static byte[] toBytes(byte[] bytes, DmField column, DmConnection connection)
	{
		byte[] bytes2 = ByteUtil.getBytes(bytes, 0, bytes.Length);
		switch (column.GetCType())
		{
		case 19:
		{
			DmClob dmClob = DmClob.newInstance(bytes2, connection.m_ConnInst, column, fetchAll: true);
			bytes2 = dmClob.GetBytes(0L, (int)dmClob.do_length());
			break;
		}
		case 12:
		{
			DmBlob dmBlob = DmBlob.newInstanceFromDB(bytes2, connection.m_ConnInst, column, fetchAll: true);
			bytes2 = dmBlob.GetBytes(0L, (int)dmBlob.do_length());
			break;
		}
		}
		return bytes2;
	}

	internal static DateTime toDate(byte[] bytes, DmField column, DmConnection connection)
	{
		switch (column.GetCType())
		{
		case 14:
		case 15:
		case 16:
		case 26:
			return DmDateTime.valueOf(bytes, column, connection).toDate(column, connection);
		case 22:
		case 23:
		case 27:
			return DmDateTime.valueOf(bytes, column, connection).toDate(column, connection);
		case 0:
		case 1:
		case 2:
		case 19:
			return DmDateTime.valueOf(charToString(bytes, column, connection), column, connection).toDate(column, connection);
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return default(DateTime);
		}
	}

	internal static DateTime toTime(byte[] bytes, DmField column, DmConnection connection)
	{
		switch (column.GetCType())
		{
		case 14:
		case 15:
		case 16:
		case 26:
			return DmDateTime.valueOf(bytes, column, connection).toTime(column, connection);
		case 22:
		case 23:
		case 27:
			return DmDateTime.valueOf(bytes, column, connection).toTime(column, connection);
		case 0:
		case 1:
		case 2:
		case 19:
			return DmDateTime.valueOf(charToString(bytes, column, connection)).toTime(column, connection);
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			return default(DateTime);
		}
	}

	internal static DmBlob toBlob(byte[] bytes, DmField column, DmConnection connection)
	{
		if (column.GetCType() == 12)
		{
			return DmBlob.newInstanceFromDB(bytes, connection.m_ConnInst, column, connection.lobFetchAll());
		}
		return DmBlob.newInstanceOfLocal(bytes, connection);
	}

	internal static DmClob toClob(byte[] bytes, DmField column, DmConnection connection)
	{
		if (column.GetCType() == 19)
		{
			return DmClob.newInstance(bytes, connection.m_ConnInst, column, connection.lobFetchAll());
		}
		return DmClob.newInstance(toString(bytes, column, connection), connection);
	}

	internal static object toObject(byte[] bytes, DmField column, DmConnection connection, Dictionary<string, Type> typeMap)
	{
		object obj = null;
		switch (column.GetCType())
		{
		case 3:
		case 13:
			obj = bytes[0] != 0;
			break;
		case 5:
			obj = ByteUtil.getByte(bytes, 0);
			break;
		case 6:
			obj = ByteUtil.getShort(bytes, 0);
			break;
		case 7:
			obj = ByteUtil.getInt(bytes, 0);
			break;
		case 8:
			obj = ByteUtil.getLong(bytes, 0);
			break;
		case 9:
			obj = decToBigDecimal(bytes, column.GetPrecision(), column.GetScale(), connection.compatibleOracle());
			break;
		case 10:
			obj = ByteUtil.getFloat(bytes, 0);
			break;
		case 11:
			obj = ByteUtil.getDouble(bytes, 0);
			break;
		case 14:
			obj = DmDateTime.valueOf(bytes, column, connection).toDate(column, connection);
			break;
		case 15:
			obj = DmDateTime.valueOf(bytes, column, connection).toTime(column, connection);
			break;
		case 16:
		case 26:
			obj = DmDateTime.valueOf(bytes, column, connection).toTimestamp(column, connection);
			break;
		case 22:
		case 23:
		case 27:
			obj = DmDateTime.valueOf(bytes, column, connection);
			break;
		case 17:
		case 18:
			obj = ByteUtil.getBytes(bytes, 0, bytes.Length);
			break;
		case 12:
		{
			DmBlob dmBlob = DmBlob.newInstanceFromDB(bytes, connection.m_ConnInst, column, connection.lobFetchAll());
			obj = ((!StringUtil.equalsIgnoreCase(column.GetTypeName(), "LONGVARBINARY")) ? ((object)dmBlob) : ((object)dmBlob.GetBytes(0L, (int)dmBlob.do_length())));
			break;
		}
		case 0:
		case 1:
		case 2:
			obj = charToString(bytes, column, connection);
			break;
		case 19:
		{
			DmClob dmClob = DmClob.newInstance(bytes, connection.m_ConnInst, column, connection.lobFetchAll());
			obj = ((!StringUtil.equalsIgnoreCase(column.GetTypeName(), "LONGVARCHAR")) ? ((object)dmClob) : ((object)dmClob.getSubString(0L, (int)dmClob.do_length())));
			break;
		}
		case 20:
			obj = new DmIntervalYM(bytes);
			break;
		case 21:
			obj = new DmIntervalDT(bytes);
			break;
		case 117:
			obj = ComplexTypeData.bytesToArray(bytes, null, column.ComplexTypeDesc);
			break;
		case 122:
			obj = ComplexTypeData.bytesToSArray(bytes, null, column.ComplexTypeDesc);
			break;
		case 119:
			obj = ComplexTypeData.bytesToObj(bytes, null, column.ComplexTypeDesc);
			if (obj is DmStruct)
			{
				obj = toSQLData((DmStruct)obj);
			}
			break;
		case 121:
			obj = ComplexTypeData.bytesToRecord(bytes, null, column.ComplexTypeDesc);
			obj = toSQLData((DmStruct)obj);
			break;
		case 28:
			obj = DmRowId.valueOf(bytes);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return obj;
	}

	internal static object toSQLData(DmStruct dmStruct)
	{
		return dmStruct;
	}

	internal static object toComplexType(byte[] bytes, DmField column, DmConnInstance connInstance)
	{
		object obj = null;
		if (column.GetCType() != 12 && column.ComplexTypeDesc != null)
		{
			switch (column.ComplexTypeDesc.GetDType())
			{
			case 117:
				obj = ComplexTypeData.bytesToArray(bytes, null, column.ComplexTypeDesc);
				break;
			case 122:
				obj = ComplexTypeData.bytesToSArray(bytes, null, column.ComplexTypeDesc);
				break;
			case 121:
				obj = ComplexTypeData.bytesToRecord(bytes, null, column.ComplexTypeDesc);
				obj = toSQLData((DmStruct)obj);
				break;
			case 119:
				obj = ComplexTypeData.bytesToObj(bytes, null, column.ComplexTypeDesc);
				if (obj is DmStruct dmStruct)
				{
					obj = toSQLData(dmStruct);
				}
				break;
			}
			return obj;
		}
		switch (column.GetCType())
		{
		case 12:
			if (!DmSqlType.isComplexType(column.GetCType(), column.GetScale()))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			obj = ComplexTypeData.objBlobToObj(DmBlob.newInstanceFromDB(bytes, connInstance, column, connInstance.ConnProperty.LobMode == 2), column.typeDescriptor);
			if (obj is DmStruct)
			{
				obj = toSQLData((DmStruct)obj);
			}
			break;
		case 117:
			obj = ComplexTypeData.bytesToArray(bytes, null, column.typeDescriptor);
			break;
		case 122:
			obj = ComplexTypeData.bytesToSArray(bytes, null, column.typeDescriptor);
			break;
		case 119:
			obj = ComplexTypeData.bytesToObj(bytes, null, column.typeDescriptor);
			if (obj is DmStruct)
			{
				obj = toSQLData((DmStruct)obj);
			}
			break;
		case 121:
			obj = ComplexTypeData.bytesToRecord(bytes, null, column.typeDescriptor);
			obj = toSQLData((DmStruct)obj);
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return obj;
	}

	internal static DmArray toArray(byte[] bytes, DmField column, DmConnection connection)
	{
		object obj = toComplexType(bytes, column, connection.m_ConnInst);
		if (!(obj is DmArray))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return (DmArray)obj;
	}

	internal static DmStruct toStruct(byte[] bytes, DmField column, DmConnection connection)
	{
		object obj = toComplexType(bytes, column, connection.m_ConnInst);
		if (!(obj is DmStruct))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return (DmStruct)obj;
	}

	internal static DmRowId toRowId(byte[] bytes, DmField column, DmConnection connection)
	{
		DmRowId result = null;
		switch (column.GetCType())
		{
		case 8:
			result = DmRowId.valueOf(ByteUtil.getLong(bytes, 0));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			result = DmRowId.valueOf(toString(bytes, column, connection));
			break;
		case 12:
		case 17:
		case 18:
		case 28:
			result = DmRowId.valueOf(toBytes(bytes, column, connection));
			break;
		default:
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			break;
		}
		return result;
	}

	private static byte[] processVarchar2(byte[] bytes, int prec)
	{
		byte[] array = new byte[prec];
		Array.Copy(bytes, 0, array, 0, bytes.Length);
		for (int i = bytes.Length; i < array.Length; i++)
		{
			array[i] = 32;
		}
		return array;
	}

	private static decimal decToBigDecimal(byte[] bytes, int prec, int scale, bool compatibleOracle)
	{
		return new DmdbNumeric(bytes, prec, scale).toDecimal(compatibleOracle);
	}

	private static string charToString(byte[] bytes, DmField column, DmConnection connection)
	{
		if (column.GetCType() == 1)
		{
			bytes = processVarchar2(bytes, column.GetPrecision());
		}
		else if (column.GetCType() == 19)
		{
			DmClob dmClob = DmClob.newInstance(bytes, connection.m_ConnInst, column, fetchAll: true);
			return dmClob.getSubString(0L, (int)dmClob.do_length());
		}
		return ByteUtil.getString(bytes, 0, bytes.Length, connection.ConnProperty.ServerEncoding);
	}

	private static long binaryToLong(byte[] bytes, DmField column, DmConnection connection)
	{
		if (column.GetCType() == 12)
		{
			DmBlob dmBlob = DmBlob.newInstanceFromDB(bytes, connection.m_ConnInst, column, fetchAll: true);
			bytes = dmBlob.GetBytes(0L, (int)dmBlob.do_length());
		}
		long num = 0L;
		int num2 = 0;
		int num3 = ((bytes.Length > 8) ? 8 : bytes.Length);
		if (bytes.Length > 8)
		{
			for (int i = 0; i < bytes.Length - 8; i++)
			{
				if (bytes[i] != 0)
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
				}
			}
			num2 = bytes.Length - 8;
			num3 = 8;
		}
		for (int j = num2; j < num2 + num3; j++)
		{
			num = (0xFF & bytes[j]) | (num << 8);
		}
		return num;
	}
}
