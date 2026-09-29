using System;
using W.Dm.util;

namespace W.Dm;

internal class TypeDesc
{
	public const int NET_REF_CURSOR = 2012;

	public const int NET_TIME_WITH_TIMEZONE = 2013;

	public const int NET_TIMESTAMP_WITH_TIMEZONE = 2014;

	public int dtype;

	public string dtypeName;

	public int jtype;

	public int jscale;

	public int jprec;

	public string jclassName;

	public int jdisplaySize;

	public static TypeDesc bitDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "BIT",
			jtype = -7,
			jprec = 1,
			jscale = column.scale,
			jclassName = typeof(bool).FullName,
			jdisplaySize = 1
		};
	}

	public static TypeDesc booleanDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "BIT",
			jtype = 16,
			jprec = 1,
			jscale = column.scale,
			jclassName = typeof(bool).FullName,
			jdisplaySize = 1
		};
	}

	public static TypeDesc tinyintDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "TINYINT",
			jtype = -6,
			jprec = 3,
			jscale = column.scale,
			jclassName = typeof(byte).FullName,
			jdisplaySize = 4
		};
	}

	public static TypeDesc smallintDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "SMALLINT",
			jtype = 5,
			jprec = 5,
			jscale = column.scale,
			jclassName = typeof(short).FullName,
			jdisplaySize = 6
		};
	}

	public static TypeDesc integerDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "INTEGER",
			jtype = 4,
			jprec = 10,
			jscale = column.scale,
			jclassName = typeof(int).FullName,
			jdisplaySize = 11
		};
	}

	public static TypeDesc bigintDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "BIGINT",
			jtype = -5,
			jprec = 19,
			jscale = column.scale,
			jclassName = typeof(long).FullName,
			jdisplaySize = 20
		};
	}

	public static TypeDesc realDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "REAL",
			jtype = 7,
			jclassName = typeof(float).FullName,
			jprec = 24,
			jscale = column.scale,
			jdisplaySize = 25
		};
	}

	public static TypeDesc floatDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "FLOAT",
			jtype = 6,
			jclassName = typeof(float).FullName,
			jprec = ((column.mask == 2) ? column.originalPrec : 53),
			jscale = column.scale,
			jdisplaySize = 54
		};
	}

	public static TypeDesc doubleDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "DOUBLE",
			jtype = 8,
			jclassName = typeof(double).FullName,
			jprec = 53,
			jscale = column.scale,
			jdisplaySize = 53
		};
	}

	public static TypeDesc decimalDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		typeDesc.dtypeName = "DECIMAL";
		typeDesc.jtype = 3;
		if (column.scale == -1)
		{
			typeDesc.jprec = 0;
			typeDesc.jscale = -1;
			typeDesc.jdisplaySize = 39;
		}
		else
		{
			typeDesc.jprec = column.prec;
			typeDesc.jscale = column.scale;
			typeDesc.jdisplaySize = typeDesc.jprec + 2;
		}
		typeDesc.jclassName = typeof(decimal).FullName;
		return typeDesc;
	}

	public static TypeDesc oracleNumberDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		typeDesc.dtypeName = "NUMBER";
		typeDesc.jtype = 2;
		if (column.scale == -1 || StringUtil.isEmpty(column.baseName))
		{
			typeDesc.jprec = 0;
			typeDesc.jscale = -127;
			typeDesc.jdisplaySize = 39;
		}
		else if (DmSqlType.isInteger(column.type))
		{
			typeDesc.jprec = 38;
			typeDesc.jscale = 0;
			typeDesc.jdisplaySize = 39;
		}
		else
		{
			typeDesc.jprec = column.prec;
			typeDesc.jscale = column.scale;
			if (typeDesc.jprec != 0)
			{
				typeDesc.jdisplaySize = typeDesc.jprec + ((column.scale == 0) ? 1 : 2);
			}
			else
			{
				typeDesc.jdisplaySize = 39;
			}
		}
		typeDesc.jclassName = typeof(decimal).FullName;
		return typeDesc;
	}

	public static TypeDesc numericDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		typeDesc.dtypeName = "NUMERIC";
		typeDesc.jtype = 2;
		if (column.scale == -1)
		{
			typeDesc.jprec = 0;
			typeDesc.jscale = -1;
			typeDesc.jdisplaySize = 39;
		}
		else
		{
			typeDesc.jprec = column.prec;
			typeDesc.jscale = column.scale;
			typeDesc.jdisplaySize = typeDesc.jprec + 2;
		}
		typeDesc.jclassName = typeof(decimal).FullName;
		return typeDesc;
	}

	public static TypeDesc charDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "CHAR",
			jtype = 1,
			jprec = column.prec,
			jscale = ((!connection.compatibleOracle()) ? column.scale : 0),
			jclassName = typeof(string).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc characterDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "CHARACTER",
			jtype = 1,
			jprec = column.prec,
			jscale = ((!connection.compatibleOracle()) ? column.scale : 0),
			jclassName = typeof(string).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc varcharDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "VARCHAR",
			jtype = 12,
			jprec = column.prec,
			jscale = ((!connection.compatibleOracle()) ? column.scale : 0),
			jdisplaySize = column.prec,
			jclassName = typeof(string).FullName
		};
	}

	public static TypeDesc varchar2Desc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "VARCHAR2",
			jtype = 12,
			jprec = column.prec,
			jscale = ((!connection.compatibleOracle()) ? column.scale : 0),
			jclassName = typeof(string).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc longvarcharDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "LONGVARCHAR",
			jtype = -1,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(string).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc clobDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		if (string.Equals(column.typeName, "TEXT", StringComparison.OrdinalIgnoreCase))
		{
			typeDesc.dtypeName = "TEXT";
		}
		else
		{
			typeDesc.dtypeName = "CLOB";
		}
		typeDesc.jtype = 2005;
		typeDesc.jprec = column.prec;
		typeDesc.jscale = column.scale;
		typeDesc.jclassName = typeof(DmClob).FullName;
		typeDesc.jdisplaySize = column.prec;
		return typeDesc;
	}

	public static TypeDesc bfileDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "BFILE",
			jtype = 12,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc binaryDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "BINARY",
			jtype = -2,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec * 2
		};
	}

	public static TypeDesc varbinaryDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "VARBINARY",
			jtype = -3,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec * 2
		};
	}

	public static TypeDesc longvarbinaryDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "LONGVARBINARY",
			jtype = -4,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = ((column.prec * 2 < 0) ? int.MaxValue : (column.prec * 2))
		};
	}

	public static TypeDesc ncharDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "NCHAR",
			jtype = -15,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc nvarcharDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "NVARCHAR",
			jtype = -9,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc nvarchar2Desc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "NVARCHAR2",
			jtype = -9,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(byte[]).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc blobDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		if (string.Equals(column.typeName, "IMAGE", StringComparison.OrdinalIgnoreCase))
		{
			typeDesc.dtypeName = "IMAGE";
		}
		else
		{
			typeDesc.dtypeName = "BLOB";
		}
		typeDesc.jtype = 2004;
		typeDesc.jprec = column.prec;
		typeDesc.jscale = column.scale;
		typeDesc.jclassName = typeof(DmBlob).FullName;
		typeDesc.jdisplaySize = column.prec;
		return typeDesc;
	}

	public static TypeDesc dateDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "DATE",
			jtype = 91,
			jprec = 10,
			jscale = column.scale,
			jclassName = typeof(DateTime).FullName,
			jdisplaySize = 10
		};
	}

	public static TypeDesc timeDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "TIME",
			jtype = 92,
			jprec = ((column.scale != 0) ? (9 + column.scale) : 8),
			jscale = column.scale,
			jclassName = typeof(DateTime).FullName,
			jdisplaySize = ((column.scale != 0) ? (9 + column.scale) : 8)
		};
	}

	public static TypeDesc timeTZDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "TIME WITH TIME ZONE",
			jtype = 2013,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(DateTime).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc timestampDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "TIMESTAMP",
			jtype = 93,
			jprec = ((column.scale != 0) ? (20 + column.scale) : 19),
			jscale = column.scale,
			jclassName = typeof(DmDateTime).FullName,
			jdisplaySize = ((column.scale != 0) ? (20 + column.scale) : 19)
		};
	}

	public static TypeDesc timestampLocalTZDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = timestampDesc(column, connection);
		typeDesc.dtypeName = "TIMESTAMP WITH LOCAL TIME ZONE";
		return typeDesc;
	}

	public static TypeDesc timestampTZDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "TIMESTAMP WITH TIME ZONE",
			jtype = 2014,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(DmDateTime).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc intervalYMDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		switch ((column.scale & 0xFF00) >> 8)
		{
		case 0:
			typeDesc.dtypeName = "INTERVAL YEAR";
			break;
		case 1:
			typeDesc.dtypeName = "INTERVAL YEAR TO MONTH";
			break;
		case 2:
			typeDesc.dtypeName = "INTERVAL MONTH";
			break;
		default:
			typeDesc.dtypeName = "INTERVAL YEAR TO MONTH";
			break;
		}
		typeDesc.jtype = 1111;
		typeDesc.jprec = DmSqlType.getIntervalPrec(column.originalPrec, column.scale);
		typeDesc.jscale = DmSqlType.getIntervalScale(column.scale);
		typeDesc.jclassName = typeof(DmIntervalYM).FullName;
		typeDesc.jdisplaySize = column.prec;
		return typeDesc;
	}

	public static TypeDesc intervalDTDesc(DmColumn column, DmConnection connection)
	{
		TypeDesc typeDesc = new TypeDesc();
		typeDesc.dtype = column.type;
		switch ((column.scale & 0xFF00) >> 8)
		{
		case 3:
			typeDesc.dtypeName = "INTERVAL DAY";
			break;
		case 4:
			typeDesc.dtypeName = "INTERVAL DAY TO HOUR";
			break;
		case 5:
			typeDesc.dtypeName = "INTERVAL DAY TO MINUTE";
			break;
		case 6:
			typeDesc.dtypeName = "INTERVAL DAY TO SECOND";
			break;
		case 7:
			typeDesc.dtypeName = "INTERVAL HOUR";
			break;
		case 8:
			typeDesc.dtypeName = "INTERVAL HOUR TO MINUTE";
			break;
		case 9:
			typeDesc.dtypeName = "INTERVAL HOUR TO SECOND";
			break;
		case 10:
			typeDesc.dtypeName = "INTERVAL MINUTE";
			break;
		case 11:
			typeDesc.dtypeName = "INTERVAL MINUTE TO SECOND";
			break;
		case 12:
			typeDesc.dtypeName = "INTERVAL SECOND";
			break;
		default:
			typeDesc.dtypeName = "INTERVAL YEAR TO MONTH";
			break;
		}
		typeDesc.jtype = 1111;
		typeDesc.jprec = DmSqlType.getIntervalPrec(column.originalPrec, column.scale);
		typeDesc.jscale = DmSqlType.getIntervalScale(column.scale);
		typeDesc.jclassName = typeof(DmIntervalDT).FullName;
		typeDesc.jdisplaySize = column.prec;
		return typeDesc;
	}

	public static TypeDesc rowidDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "ROWID",
			jtype = -8,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(DmRowId).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc arrayDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = ((column.typeDescriptor != null) ? column.typeDescriptor.getFulName() : "ARRAY"),
			jtype = 2003,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(Array).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc structDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = ((column.typeDescriptor != null) ? column.typeDescriptor.getFulName() : "STRUCT"),
			jtype = 2002,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(DmStruct).FullName,
			jdisplaySize = column.prec
		};
	}

	public static TypeDesc cursorDesc(DmColumn column, DmConnection connection)
	{
		return new TypeDesc
		{
			dtype = column.type,
			dtypeName = "CURSOR",
			jtype = 2012,
			jprec = column.prec,
			jscale = column.scale,
			jclassName = typeof(object).FullName,
			jdisplaySize = column.prec
		};
	}
}
