using System;
using System.Data;

namespace Dm.util;

public class ConvertUtil
{
	public static string TypeToDmSqlType(Type type)
	{
		return DmSqlType.DbTypeToDmSqlType(TypeToDbType(type)).ToString();
	}

	public static DbType TypeToDbType(Type type)
	{
		switch (Type.GetTypeCode(type))
		{
		case TypeCode.Empty:
		case TypeCode.DBNull:
			return DbType.Object;
		case TypeCode.Boolean:
			return DbType.Boolean;
		case TypeCode.Char:
			return DbType.StringFixedLength;
		case TypeCode.SByte:
			return DbType.SByte;
		case TypeCode.Byte:
			return DbType.Byte;
		case TypeCode.Int16:
			return DbType.Int16;
		case TypeCode.UInt16:
			return DbType.UInt16;
		case TypeCode.Int32:
			return DbType.Int32;
		case TypeCode.UInt32:
			return DbType.UInt32;
		case TypeCode.Int64:
			return DbType.Int64;
		case TypeCode.UInt64:
			return DbType.UInt64;
		case TypeCode.Single:
			return DbType.Single;
		case TypeCode.Double:
			return DbType.Double;
		case TypeCode.Decimal:
			return DbType.Decimal;
		case TypeCode.DateTime:
			return DbType.DateTime;
		case TypeCode.String:
			return DbType.String;
		case TypeCode.Object:
			return TypeToDbType(type.GetProperties()[1].PropertyType);
		default:
			return DbType.Object;
		}
	}
}
