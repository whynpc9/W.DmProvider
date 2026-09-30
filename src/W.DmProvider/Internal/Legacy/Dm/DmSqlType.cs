using System;
using System.Data;
using W.Dm.Config;
using W.Dm.util;

namespace W.Dm;

internal class DmSqlType
{
	internal const string VERSION = "8.3.1.3";

	internal const string BUILD_TIME = "2011.01.10";

	internal const string DATABASE_PRODUCT_VERSION = "7.0.0.0";

	internal const int CHAR = 0;

	internal const int VARCHAR2 = 1;

	internal const int VARCHAR = 2;

	internal const int BIT = 3;

	internal const int TINYINT = 5;

	internal const int SMALLINT = 6;

	internal const int INT = 7;

	internal const int BIGINT = 8;

	internal const int DECIMAL = 9;

	internal const int REAL = 10;

	internal const int DOUBLE = 11;

	internal const int BLOB = 12;

	internal const int BOOLEAN = 13;

	internal const int DATE = 14;

	internal const int TIME = 15;

	internal const int DATETIME = 16;

	internal const int BINARY = 17;

	internal const int VARBINARY = 18;

	internal const int CLOB = 19;

	internal const int INTERVAL_YM = 20;

	internal const int INTERVAL_DT = 21;

	internal const int TIME_TZ = 22;

	internal const int DATETIME_TZ = 23;

	internal const int DEC_INT64 = 24;

	internal const int NULL = 25;

	internal const int DATETIME2 = 26;

	internal const int DATETIME2_TZ = 27;

	internal const int ROWID = 28;

	internal const int ANY = 31;

	internal const int STAR_ALL = 32;

	internal const int STAR = 33;

	internal const int RECORD = 40;

	internal const int TYPE = 41;

	internal const int TYPE_REF = 42;

	internal const int UNKNOWN = 54;

	internal const int INTERVAL_YEAR = 0;

	internal const int INTERVAL_YEAR_TO_MONTH = 1;

	internal const int INTERVAL_MONTH = 2;

	internal const int INTERVAL_DAY = 3;

	internal const int INTERVAL_DAY_TO_HOUR = 4;

	internal const int INTERVAL_DAY_TO_MIMUTE = 5;

	internal const int INTERVAL_DAY_TO_SECOND = 6;

	internal const int INTERVAL_HOUR = 7;

	internal const int INTERVAL_HOUR_TO_MIMUTE = 8;

	internal const int INTERVAL_HOUR_TO_SECOND = 9;

	internal const int INTERVAL_MIMUTE = 10;

	internal const int INTERVAL_MIMUTE_TO_SECOND = 11;

	internal const int INTERVAL_SECOND = 12;

	internal const int ARRAY = 117;

	internal const int CLASS = 119;

	internal const int CURSOR = 120;

	internal const int PLTYPE_RECORD = 121;

	internal const int SARRAY = 122;

	internal const int CURSOR_ORACLE = -10;

	internal const int MAX_STRING_LEN = 8188;

	internal const int BIT_PREC = 1;

	internal const int TINYINT_PREC = 1;

	internal const int SMALLINT_PREC = 2;

	internal const int INT_PREC = 4;

	internal const int BIGINT_PREC = 8;

	internal const int REAL_PREC = 4;

	internal const int DOUBLE_PREC = 8;

	internal const int DATE_PREC = 3;

	internal const int TIME_PREC = 5;

	internal const int DATETIME_PREC = 8;

	internal const int DATETIME2_PREC = 9;

	internal const int TIME_TZ_PREC = 7;

	internal const int DATETIME_TZ_PREC = 10;

	internal const int DATETIME2_TZ_PREC = 11;

	internal const int INTERVAL_YM_PREC = 12;

	internal const int INTERVAL_DT_PREC = 24;

	internal const int DEC_INT64_PREC = 8;

	internal const int VARCHAR_PREC = 32767;

	internal const int VARBINARY_PREC = 32767;

	internal const int BLOB_PREC = int.MaxValue;

	internal const int CLOB_PREC = int.MaxValue;

	internal const int CLASS_PREC = 32768;

	internal const int CURSOR_PREC = 32768;

	internal const int NULL_PREC = 4;

	internal const int BFILE_PREC = 512;

	internal const int CURRENCY_PREC = 19;

	internal const int ROWID_PREC = 12;

	internal const int BFILE_SCALE = 6;

	internal const int COMPLEX_SCALE = 5;

	internal const int CURRENCY_SCALE = 4;

	internal const int LOCAL_DATETIME_SCALE_MASK = 4096;

	internal const int FLOAT_SCALE_MASK = 129;

	internal const int ORACLE_FLOAT_SCALE_MASK = 129;

	internal const int ORACLE_DATE_SCALE_MASK = 8192;

	public static bool isInteger(int type)
	{
		if (type != 5 && type != 6 && type != 7)
		{
			return type == 8;
		}
		return true;
	}

	public static bool isNumber(int type)
	{
		if (type != 5 && type != 6 && type != 7 && type != 8 && type != 10 && type != 11)
		{
			return type == 9;
		}
		return true;
	}

	internal static bool isComplexType(int type, int scale)
	{
		if ((type != 12 || scale != 5) && type != 117 && type != 122 && type != 119)
		{
			return type == 121;
		}
		return true;
	}

	internal static bool isFloat(int type, int scale)
	{
		if (type == 9)
		{
			return scale == 129;
		}
		return false;
	}

	public static int getFloatPrec(int type, int prec)
	{
		return (int)Math.Round((double)prec * 0.30103) + 1;
	}

	public static int getFloatScale(int type, int scale)
	{
		return scale & -130;
	}

	internal DmSqlType()
	{
	}

	internal static int TypeNameToCType(string typeName)
	{
		int result = 0;
		typeName = typeName.ToUpper();
		if (typeName.Equals("CHAR"))
		{
			result = 0;
		}
		else if (typeName.Equals("CHARACTER"))
		{
			result = 0;
		}
		else if (typeName.Equals("VARCHAR"))
		{
			result = 2;
		}
		else if (typeName.Equals("VARCHAR2"))
		{
			result = 2;
		}
		else if (typeName.Equals("NUMERIC"))
		{
			result = 9;
		}
		else if (typeName.Equals("DECIMAL"))
		{
			result = 9;
		}
		else if (typeName.Equals("DEC"))
		{
			result = 9;
		}
		else if (typeName.Equals("NUMBER"))
		{
			result = 9;
		}
		else if (typeName.Equals("BYTE"))
		{
			result = 5;
		}
		else if (typeName.Equals("TINYINT"))
		{
			result = 5;
		}
		else if (typeName.Equals("SMALLINT"))
		{
			result = 6;
		}
		else if (typeName.Equals("BIGINT"))
		{
			result = 8;
		}
		else if (typeName.Equals("INT"))
		{
			result = 7;
		}
		else if (typeName.Equals("INTEGER"))
		{
			result = 7;
		}
		else if (typeName.Equals("FLOAT"))
		{
			result = 11;
		}
		else if (typeName.Equals("REAL"))
		{
			result = 10;
		}
		else if (typeName.Equals("DOUBLE"))
		{
			result = 11;
		}
		else if (typeName.Equals("DOUBLE PRECISION"))
		{
			result = 11;
		}
		else if (typeName.Equals("DATE"))
		{
			result = 14;
		}
		else if (typeName.Equals("TIME"))
		{
			result = 15;
		}
		else if (typeName.Equals("TIME WITH TIME ZONE"))
		{
			result = 22;
		}
		else if (typeName.Equals("DATETIME"))
		{
			result = 16;
		}
		else if (typeName.Equals("TIMESTAMP"))
		{
			result = 16;
		}
		else if (typeName.Equals("TIMESTAMP WITH TIME ZONE"))
		{
			result = 23;
		}
		else if (typeName.Equals("TEXT"))
		{
			result = 19;
		}
		else if (typeName.Equals("CLOB"))
		{
			result = 19;
		}
		else if (typeName.Equals("SOUND"))
		{
			result = 12;
		}
		else if (typeName.Equals("IMAGE"))
		{
			result = 12;
		}
		else if (typeName.Equals("BINARY"))
		{
			result = 17;
		}
		else if (typeName.Equals("VARBINARY"))
		{
			result = 18;
		}
		else if (typeName.Equals("BLOB"))
		{
			result = 12;
		}
		else if (typeName.Equals("BIT"))
		{
			result = 3;
		}
		else if (typeName.Equals("MONEY"))
		{
			result = 9;
		}
		else if (typeName.Equals("BOOL"))
		{
			result = 3;
		}
		else if (typeName.Equals("BOOLEAN"))
		{
			result = 3;
		}
		else if (typeName.Equals("LONGVARCHAR"))
		{
			result = 19;
		}
		else if (typeName.Equals("LONGVARBINARY"))
		{
			result = 12;
		}
		else if (typeName.Equals("CURSOR"))
		{
			result = 120;
		}
		else if (typeName.Equals("ROWID"))
		{
			result = 28;
		}
		else
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return result;
	}

	internal static Type CTypeToSystemType(int cType, int prec, DmConnProperty connProperty)
	{
		switch (cType)
		{
		case 0:
		case 1:
			return typeof(string);
		case 2:
		case 54:
			return typeof(string);
		case 3:
			return typeof(bool);
		case 5:
			return typeof(sbyte);
		case 6:
			return typeof(short);
		case 7:
			return typeof(int);
		case 8:
			return typeof(long);
		case 9:
		case 24:
			return typeof(decimal);
		case 10:
			return typeof(float);
		case 11:
			return typeof(double);
		case 12:
			return typeof(byte[]);
		case 14:
			return typeof(DateTime);
		case 15:
			return connProperty.DbTimeToTimeSpan ? typeof(TimeSpan) : typeof(DateTime);
		case 16:
			return typeof(DateTime);
		case 17:
			return typeof(byte[]);
		case 18:
			return typeof(byte[]);
		case 28:
			return typeof(string);
		case 19:
			return typeof(string);
		case 21:
			return typeof(DmIntervalDT);
		case 20:
			return typeof(DmIntervalYM);
		case 22:
			return typeof(DateTimeOffset);
		case 23:
		case 27:
			return typeof(DateTimeOffset);
		case 26:
			return typeof(DateTime);
		default:
			return typeof(object);
		}
	}

	internal static Type CTypeToSystemType(int cType, int prec, int scale, DmConnProperty connProperty)
	{
		switch (cType)
		{
		case 0:
		case 1:
			return typeof(string);
		case 2:
		case 54:
			return typeof(string);
		case 3:
			return typeof(bool);
		case 5:
			return typeof(sbyte);
		case 6:
			return typeof(short);
		case 7:
			return typeof(int);
		case 8:
			return typeof(long);
		case 9:
		case 24:
			return typeof(decimal);
		case 10:
			return typeof(float);
		case 11:
			return typeof(double);
		case 12:
			return typeof(byte[]);
		case 14:
			return typeof(DateTime);
		case 15:
			return connProperty.DbTimeToTimeSpan ? typeof(TimeSpan) : typeof(DateTime);
		case 16:
			return typeof(DateTime);
		case 17:
			return typeof(byte[]);
		case 18:
			return typeof(byte[]);
		case 28:
			return typeof(string);
		case 19:
			return typeof(string);
		case 21:
			if (connProperty.IntervalMode == IntervalMode.DT || connProperty.IntervalMode == IntervalMode.ALL || scale == 1574)
			{
				return typeof(TimeSpan);
			}
			return typeof(DmIntervalDT);
		case 20:
			if (connProperty.IntervalMode == IntervalMode.YM || connProperty.IntervalMode == IntervalMode.ALL)
			{
				return typeof(long);
			}
			return typeof(DmIntervalYM);
		case 22:
			return typeof(DateTimeOffset);
		case 23:
		case 27:
			return typeof(DateTimeOffset);
		case 26:
			return typeof(DateTime);
		default:
			return typeof(object);
		}
	}

	internal static Type CTypeToSystemTypeInner(int cType)
	{
		switch (cType)
		{
		case 0:
			return typeof(string);
		case 1:
			return typeof(string);
		case 2:
			return typeof(string);
		case 3:
			return typeof(bool);
		case 5:
			return typeof(sbyte);
		case 6:
			return typeof(short);
		case 7:
			return typeof(int);
		case 8:
			return typeof(long);
		case 9:
		case 24:
			return typeof(decimal);
		case 10:
			return typeof(float);
		case 11:
			return typeof(double);
		case 12:
			return typeof(byte[]);
		case 14:
			return typeof(DateTime);
		case 15:
			return typeof(DateTime);
		case 22:
			return typeof(DateTimeOffset);
		case 16:
			return typeof(DateTime);
		case 23:
			return typeof(DateTimeOffset);
		case 17:
			return typeof(byte[]);
		case 18:
		case 28:
			return typeof(byte[]);
		case 19:
			return typeof(string);
		case 21:
			return typeof(DmIntervalDT);
		case 20:
			return typeof(DmIntervalYM);
		case 54:
			return typeof(string);
		default:
			return typeof(object);
		}
	}

	internal static int DmDbTypeToDmSqlType(DmDbType sqlType)
	{
		switch (sqlType)
		{
		case DmDbType.Binary:
			return 17;
		case DmDbType.Bit:
			return 3;
		case DmDbType.Blob:
			return 12;
		case DmDbType.Byte:
			return 5;
		case DmDbType.Char:
			return 0;
		case DmDbType.Clob:
			return 19;
		case DmDbType.Date:
			return 14;
		case DmDbType.DateTime:
			return 16;
		case DmDbType.DateTimeOffset:
			return 23;
		case DmDbType.TimeOffset:
			return 22;
		case DmDbType.Decimal:
			return 9;
		case DmDbType.Double:
			return 11;
		case DmDbType.Float:
			return 10;
		case DmDbType.Int16:
			return 6;
		case DmDbType.Int32:
			return 7;
		case DmDbType.Int64:
			return 8;
		case DmDbType.IntervalDayToSecond:
			return 21;
		case DmDbType.IntervalYearToMonth:
			return 20;
		case DmDbType.SByte:
			return 5;
		case DmDbType.Text:
			return 2;
		case DmDbType.Time:
			return 15;
		case DmDbType.UInt16:
			return 6;
		case DmDbType.UInt32:
			return 7;
		case DmDbType.UInt64:
			return 8;
		case DmDbType.VarBinary:
			return 18;
		case DmDbType.VarChar:
			return 2;
		case DmDbType.Cursor:
		case DmDbType.RefCursor:
			return 120;
		case DmDbType.XDEC:
			return 24;
		case DmDbType.ARRAY:
			return 117;
		case DmDbType.Class:
			return 119;
		default:
			return 2;
		}
	}

	internal static DbType DmSqlTypeToDbType(DmDbType sqlType)
	{
		switch (sqlType)
		{
		case DmDbType.Binary:
			return DbType.Binary;
		case DmDbType.Bit:
			return DbType.Boolean;
		case DmDbType.Blob:
			return DbType.Binary;
		case DmDbType.Byte:
			return DbType.Byte;
		case DmDbType.Char:
			return DbType.StringFixedLength;
		case DmDbType.Clob:
			return DbType.String;
		case DmDbType.Date:
			return DbType.Date;
		case DmDbType.DateTime:
			return DbType.DateTime;
		case DmDbType.DateTimeOffset:
			return DbType.DateTimeOffset;
		case DmDbType.TimeOffset:
			return DbType.DateTimeOffset;
		case DmDbType.Decimal:
			return DbType.Decimal;
		case DmDbType.Double:
			return DbType.Double;
		case DmDbType.Float:
			return DbType.Single;
		case DmDbType.Int16:
			return DbType.Int16;
		case DmDbType.Int32:
			return DbType.Int32;
		case DmDbType.Int64:
			return DbType.Int64;
		case DmDbType.IntervalDayToSecond:
		case DmDbType.IntervalYearToMonth:
			return DbType.Object;
		case DmDbType.SByte:
			return DbType.SByte;
		case DmDbType.Text:
			return DbType.String;
		case DmDbType.Time:
			return DbType.Time;
		case DmDbType.UInt16:
			return DbType.UInt16;
		case DmDbType.UInt32:
			return DbType.UInt32;
		case DmDbType.UInt64:
			return DbType.UInt64;
		case DmDbType.VarBinary:
			return DbType.Binary;
		case DmDbType.VarChar:
			return DbType.String;
		case DmDbType.Cursor:
		case DmDbType.RefCursor:
			return DbType.Object;
		case DmDbType.XDEC:
			return DbType.VarNumeric;
		case DmDbType.ARRAY:
		case DmDbType.Class:
			return DbType.Object;
		default:
			return DbType.Object;
		}
	}

	internal static DmDbType DbTypeToDmSqlType(DbType dbType)
	{
		switch (dbType)
		{
		case DbType.AnsiString:
			return DmDbType.VarChar;
		case DbType.AnsiStringFixedLength:
			return DmDbType.Char;
		case DbType.Binary:
			return DmDbType.Binary;
		case DbType.Boolean:
			return DmDbType.Bit;
		case DbType.Byte:
			return DmDbType.Byte;
		case DbType.Currency:
			return DmDbType.Decimal;
		case DbType.Date:
			return DmDbType.Date;
		case DbType.DateTime:
		case DbType.DateTime2:
			return DmDbType.DateTime;
		case DbType.DateTimeOffset:
			return DmDbType.DateTimeOffset;
		case DbType.Decimal:
			return DmDbType.Decimal;
		case DbType.Double:
			return DmDbType.Double;
		case DbType.Guid:
			return DmDbType.VarChar;
		case DbType.Int16:
			return DmDbType.Int16;
		case DbType.Int32:
			return DmDbType.Int32;
		case DbType.Int64:
			return DmDbType.Int64;
		case DbType.Object:
			return DmDbType.VarChar;
		case DbType.SByte:
			return DmDbType.SByte;
		case DbType.Single:
			return DmDbType.Float;
		case DbType.String:
			return DmDbType.VarChar;
		case DbType.StringFixedLength:
			return DmDbType.Char;
		case DbType.Time:
			return DmDbType.Time;
		case DbType.UInt16:
			return DmDbType.UInt16;
		case DbType.UInt32:
			return DmDbType.UInt32;
		case DbType.UInt64:
			return DmDbType.UInt64;
		case DbType.VarNumeric:
			return DmDbType.XDEC;
		default:
			return DmDbType.VarChar;
		}
	}

	internal static Type DbTypeToType(DbType dbType)
	{
		switch (dbType)
		{
		case DbType.AnsiString:
			return typeof(string);
		case DbType.AnsiStringFixedLength:
			return typeof(string);
		case DbType.Binary:
			return typeof(sbyte[]);
		case DbType.Boolean:
			return typeof(bool);
		case DbType.Byte:
			return typeof(byte);
		case DbType.Currency:
			return typeof(decimal);
		case DbType.Date:
			return typeof(DateTime);
		case DbType.DateTime:
		case DbType.DateTime2:
			return typeof(DateTime);
		case DbType.DateTimeOffset:
			return typeof(DateTimeOffset);
		case DbType.Decimal:
			return typeof(decimal);
		case DbType.Double:
			return typeof(double);
		case DbType.Guid:
			return typeof(Guid);
		case DbType.Int16:
			return typeof(short);
		case DbType.Int32:
			return typeof(int);
		case DbType.Int64:
			return typeof(long);
		case DbType.Object:
			return typeof(object);
		case DbType.SByte:
			return typeof(sbyte);
		case DbType.Single:
			return typeof(float);
		case DbType.String:
			return typeof(string);
		case DbType.StringFixedLength:
			return typeof(string);
		case DbType.Time:
			return typeof(TimeSpan);
		case DbType.UInt16:
			return typeof(ushort);
		case DbType.UInt32:
			return typeof(uint);
		case DbType.UInt64:
			return typeof(ulong);
		case DbType.VarNumeric:
			return typeof(DmXDec);
		default:
			return typeof(string);
		}
	}

	internal static DbType DbTypeFromObject(object x)
	{
		DbType dbType = DbType.Object;
		if (x == null)
		{
			return DbType.Object;
		}
		if (x is DBNull)
		{
			return DbType.Object;
		}
		if (x is byte)
		{
			return DbType.Byte;
		}
		if (x is sbyte)
		{
			return DbType.SByte;
		}
		if (x is string)
		{
			return DbType.String;
		}
		if (x is char)
		{
			return DbType.StringFixedLength;
		}
		if (x is char[])
		{
			return DbType.StringFixedLength;
		}
		if (x is decimal)
		{
			return DbType.Decimal;
		}
		if (x is short)
		{
			return DbType.Int16;
		}
		if (x is ushort)
		{
			return DbType.UInt16;
		}
		if (x is int)
		{
			return DbType.Int32;
		}
		if (x is uint)
		{
			return DbType.UInt32;
		}
		if (x is long)
		{
			return DbType.Int64;
		}
		if (x is ulong)
		{
			return DbType.UInt64;
		}
		if (x is float)
		{
			return DbType.Single;
		}
		if (x is double)
		{
			return DbType.Double;
		}
		if (x is sbyte[])
		{
			return DbType.Binary;
		}
		if (x is byte[])
		{
			return DbType.Binary;
		}
		if (x is DateTime)
		{
			return DbType.DateTime;
		}
		if (x is DateTimeOffset)
		{
			return DbType.DateTimeOffset;
		}
		if (x is bool)
		{
			return DbType.Boolean;
		}
		if (x is DmTime)
		{
			return DbType.DateTime;
		}
		if (x is DmIntervalYM)
		{
			return DbType.Int32;
		}
		if (x is DmIntervalDT)
		{
			return DbType.Object;
		}
		if (x is TimeSpan)
		{
			return DbType.Time;
		}
		if (x is Guid)
		{
			return DbType.Guid;
		}
		throw new SystemException("Value is of unknown data type");
	}

	internal static DmDbType CTypeToDmDbType(int cType)
	{
		switch (cType)
		{
		case 0:
			return DmDbType.Char;
		case 1:
			return DmDbType.Char;
		case 2:
			return DmDbType.VarChar;
		case 3:
			return DmDbType.Bit;
		case 5:
			return DmDbType.SByte;
		case 6:
			return DmDbType.Int16;
		case 7:
			return DmDbType.Int32;
		case 8:
			return DmDbType.Int64;
		case 9:
		case 24:
			return DmDbType.Decimal;
		case 10:
			return DmDbType.Float;
		case 11:
			return DmDbType.Double;
		case 12:
			return DmDbType.Blob;
		case 14:
			return DmDbType.Date;
		case 15:
			return DmDbType.Time;
		case 16:
			return DmDbType.DateTime;
		case 17:
			return DmDbType.Binary;
		case 18:
		case 28:
			return DmDbType.VarBinary;
		case 19:
			return DmDbType.Text;
		case 21:
			return DmDbType.IntervalDayToSecond;
		case 20:
			return DmDbType.IntervalYearToMonth;
		case -10:
		case 120:
			return DmDbType.Cursor;
		case 54:
			return DmDbType.VarChar;
		case 117:
			return DmDbType.ARRAY;
		case 119:
			return DmDbType.Class;
		default:
			return DmDbType.VarChar;
		}
	}

	internal static int GetSizeByCType(int cType, int cPrec)
	{
		int num = 0;
		switch (cType)
		{
		case 0:
			if (cPrec != 0)
			{
				return cPrec;
			}
			return 8188;
		case 1:
			if (cPrec != 0)
			{
				return cPrec;
			}
			return 8188;
		case 2:
			if (cPrec != 0)
			{
				return cPrec;
			}
			return 8188;
		case 3:
			return 1;
		case 5:
			return 3;
		case 6:
			return 5;
		case 7:
			return 10;
		case 8:
			return 19;
		case 9:
		case 24:
			return cPrec;
		case 10:
			return 24;
		case 11:
			return 53;
		case 12:
			return cPrec;
		case 14:
			return 10;
		case 15:
			return 15;
		case 16:
			return 26;
		case 17:
			return cPrec;
		case 18:
			return cPrec;
		case 19:
			return cPrec;
		case 20:
		case 21:
			return cPrec;
		case 54:
			return cPrec;
		case 28:
			return 12;
		default:
			return cPrec;
		}
	}

	internal static string IntervalDTtypeToName(int scale)
	{
		string result = "";
		int num = (scale & 0xFF00) >> 8;
		int num2 = (scale & 0xF0) >> 4;
		int num3 = scale & 0xF;
		switch (num)
		{
		case 0:
			result = "INTERVAL YEAR(" + num2 + ")";
			break;
		case 2:
			result = "INTERVAL MONTH(" + num2 + ")";
			break;
		case 3:
			result = "INTERVAL DAY(" + num2 + ")";
			break;
		case 7:
			result = "INTERVAL HOUR(" + num2 + ")";
			break;
		case 10:
			result = "INTERVAL MINUTE(" + num2 + ")";
			break;
		case 12:
			result = "INTERVAL SECOND(" + num2 + "," + num3 + ")";
			break;
		case 1:
			result = "INTERVAL YEAR(" + num2 + ") TO MONTH";
			break;
		case 4:
			result = "INTERVAL DAY(" + num2 + ") TO HOUR";
			break;
		case 5:
			result = "INTERVAL DAY(" + num2 + ") TO MINUTE";
			break;
		case 6:
			result = "INTERVAL DAY(" + num2 + ") TO SECOND(" + num3 + ")";
			break;
		case 8:
			result = "INTERVAL HOUR(" + num2 + ") TO MINUTE";
			break;
		case 9:
			result = "INTERVAL HOUR(" + num2 + ") TO SECOND(" + num3 + ")";
			break;
		case 11:
			result = "INTERVAL MINUTE(" + num2 + ") TO SECOND(" + num3 + ")";
			break;
		}
		return result;
	}

	internal static bool DtypeIsFixedLow(int dtype)
	{
		switch (dtype)
		{
		case 0:
			return true;
		case 1:
		case 2:
			return false;
		case 3:
			return true;
		case 7:
			return true;
		case 8:
			return true;
		case 6:
			return true;
		case 5:
			return true;
		case 9:
		case 24:
			return true;
		case 10:
			return true;
		case 11:
			return true;
		case 12:
			return false;
		case 19:
			return false;
		case 14:
			return true;
		case 15:
			return true;
		case 16:
			return true;
		case 20:
			return true;
		case 21:
			return true;
		case 17:
		case 28:
			return true;
		case 18:
			return false;
		case 25:
			return true;
		default:
			return false;
		}
	}

	internal static int DtypeGetInternalLenLow(int dtype, int len)
	{
		return dtype switch
		{
			7 => 4, 
			0 => len, 
			9 => (2 + len + 1) / 2, 
			11 => 8, 
			6 => 2, 
			8 => 8, 
			5 => 1, 
			10 => 4, 
			14 => 3, 
			15 => 5, 
			16 => 8, 
			17 => len, 
			3 => 1, 
			20 => 12, 
			21 => 24, 
			25 => 4, 
			24 => 8, 
			28 => 12, 
			_ => 0, 
		};
	}

	public static TypeDesc getTypeDesc(DmColumn column, DmConnection connection)
	{
		if (connection.compatibleOracle() && isNumber(column.type))
		{
			return TypeDesc.oracleNumberDesc(column, connection);
		}
		if (column.mask == 3)
		{
			return TypeDesc.bfileDesc(column, connection);
		}
		if (column.mask == 1)
		{
			return TypeDesc.dateDesc(column, connection);
		}
		if (column.mask == 4)
		{
			return TypeDesc.timestampLocalTZDesc(column, connection);
		}
		int type = column.type;
		if (isComplexType(column.type, column.scale) && column.typeDescriptor != null)
		{
			type = column.typeDescriptor.column.type;
		}
		switch (type)
		{
		case 3:
			if (StringUtil.equalsIgnoreCase(column.typeName, "BOOLEAN"))
			{
				return TypeDesc.booleanDesc(column, connection);
			}
			return TypeDesc.bitDesc(column, connection);
		case 13:
			return TypeDesc.booleanDesc(column, connection);
		case 5:
			return TypeDesc.tinyintDesc(column, connection);
		case 6:
			return TypeDesc.smallintDesc(column, connection);
		case 7:
			return TypeDesc.integerDesc(column, connection);
		case 8:
			return TypeDesc.bigintDesc(column, connection);
		case 10:
			return TypeDesc.realDesc(column, connection);
		case 11:
			if (StringUtil.equalsIgnoreCase(column.typeName, "FLOAT"))
			{
				return TypeDesc.floatDesc(column, connection);
			}
			return TypeDesc.doubleDesc(column, connection);
		case 9:
			if (StringUtil.equalsIgnoreCase(column.typeName, "NUMERIC"))
			{
				return TypeDesc.numericDesc(column, connection);
			}
			if (StringUtil.isNotEmpty(column.typeName) && !StringUtil.equalsIgnoreCase(column.typeName, "DECIMAL") && !StringUtil.equalsIgnoreCase(column.typeName, "DEC"))
			{
				return TypeDesc.oracleNumberDesc(column, connection);
			}
			return TypeDesc.decimalDesc(column, connection);
		case 0:
		case 1:
		case 2:
			if (StringUtil.equalsIgnoreCase(column.typeName, "CHAR"))
			{
				return TypeDesc.charDesc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "CHARACTER"))
			{
				return TypeDesc.characterDesc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "VARCHAR"))
			{
				return TypeDesc.varcharDesc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "VARCHAR2"))
			{
				return TypeDesc.varchar2Desc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "NCHAR"))
			{
				return TypeDesc.ncharDesc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "NVARCHAR2"))
			{
				return TypeDesc.nvarchar2Desc(column, connection);
			}
			if (StringUtil.equalsIgnoreCase(column.typeName, "NVARCHAR"))
			{
				return TypeDesc.nvarcharDesc(column, connection);
			}
			return type switch
			{
				0 => TypeDesc.charDesc(column, connection), 
				1 => TypeDesc.varchar2Desc(column, connection), 
				_ => TypeDesc.varcharDesc(column, connection), 
			};
		case 19:
			if (StringUtil.equalsIgnoreCase(column.typeName, "LONGVARCHAR"))
			{
				return TypeDesc.longvarcharDesc(column, connection);
			}
			if (connection != null && connection.getClobAsString())
			{
				return TypeDesc.varcharDesc(column, connection);
			}
			return TypeDesc.clobDesc(column, connection);
		case 17:
			return TypeDesc.binaryDesc(column, connection);
		case 18:
			return TypeDesc.varbinaryDesc(column, connection);
		case 12:
			if (StringUtil.equalsIgnoreCase(column.typeName, "LONGVARBINARY"))
			{
				return TypeDesc.longvarbinaryDesc(column, connection);
			}
			return TypeDesc.blobDesc(column, connection);
		case 14:
			return TypeDesc.dateDesc(column, connection);
		case 15:
			return TypeDesc.timeDesc(column, connection);
		case 22:
			return TypeDesc.timeTZDesc(column, connection);
		case 16:
			return TypeDesc.timestampDesc(column, connection);
		case 23:
			return TypeDesc.timestampTZDesc(column, connection);
		case 26:
			return TypeDesc.timestampDesc(column, connection);
		case 27:
			return TypeDesc.timestampTZDesc(column, connection);
		case 21:
			return TypeDesc.intervalDTDesc(column, connection);
		case 20:
			return TypeDesc.intervalYMDesc(column, connection);
		case 28:
			return TypeDesc.rowidDesc(column, connection);
		case 117:
		case 122:
			return TypeDesc.arrayDesc(column, connection);
		case 40:
		case 41:
		case 42:
		case 119:
		case 121:
			return TypeDesc.structDesc(column, connection);
		case 120:
			return TypeDesc.cursorDesc(column, connection);
		default:
			return TypeDesc.varcharDesc(column, connection);
		}
	}

	public static int getIntervalPrec(int size, int scale)
	{
		int num = (scale >> 4) & 0xF;
		if (num <= 0)
		{
			return size;
		}
		return num;
	}

	public static int getIntervalScale(long scale)
	{
		return (int)(scale & 0xF);
	}
}
