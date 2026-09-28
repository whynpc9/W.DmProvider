using System.Threading;
using Dm.filter;
using Dm.util;

namespace Dm;

internal class DmdbResultSetMetaData : IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator;

	public DmConnection connection;

	private DmColumn[] columns;

	private TypeDesc[] typeDescs;

	private bool innerMeta;

	private int columnNoNulls;

	private int columnNullable = 1;

	private int columnNullableUnknown = 2;

	public long ID
	{
		get
		{
			if (id < 0)
			{
				id = Interlocked.Increment(ref idGenerator);
			}
			return id;
		}
	}

	public BaseFilter filterHead { get; set; }

	public LogInfo LogInfo { get; set; }

	public RWInfo RWInfo { get; set; }

	public RecoverInfo RecoverInfo { get; set; }

	public DmdbResultSetMetaData(DmConnection connection, DmColumn[] columns)
	{
		BaseFilter.CreateFilterChain(this, connection.ConnProperty);
		this.connection = connection;
		this.columns = columns;
		typeDescs = new TypeDesc[columns.Length];
	}

	public DmdbResultSetMetaData(DmConnection connection, DmColumn[] columns, bool innerMeta)
	{
		BaseFilter.CreateFilterChain(this, connection.ConnProperty);
		this.connection = connection;
		this.columns = columns;
		typeDescs = new TypeDesc[columns.Length];
		this.innerMeta = innerMeta;
	}

	private DmColumn checkIndex(int column)
	{
		if (column < 1 || column > columns.Length)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		}
		if (typeDescs[column - 1] == null)
		{
			typeDescs[column - 1] = DmSqlType.getTypeDesc(columns[column - 1], connection);
		}
		return columns[column - 1];
	}

	public int do_getColumnCount()
	{
		return columns.Length;
	}

	public bool do_isAutoIncrement(int column)
	{
		return checkIndex(column).identity;
	}

	public bool do_isCaseSensitive(int column)
	{
		int type = checkIndex(column).type;
		if ((uint)type <= 2u || type == 19)
		{
			return true;
		}
		return false;
	}

	public bool do_isSearchable(int column)
	{
		int type = checkIndex(column).type;
		if (type == 12 || (uint)(type - 17) <= 2u)
		{
			return false;
		}
		return true;
	}

	public bool do_isCurrency(int column)
	{
		DmColumn dmColumn = checkIndex(column);
		if (dmColumn.type == 9 && dmColumn.prec == 19)
		{
			return dmColumn.scale == 4;
		}
		return false;
	}

	public int do_isNullable(int column)
	{
		if (!checkIndex(column).nullable)
		{
			return columnNoNulls;
		}
		return columnNullable;
	}

	public bool do_isSigned(int column)
	{
		return DmSqlType.isNumber(checkIndex(column).type);
	}

	public int do_getColumnDisplaySize(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].jdisplaySize;
	}

	public string do_getColumnLabel(int column)
	{
		string name = checkIndex(column).GetName();
		if (StringUtil.isEmpty(name))
		{
			return do_getColumnName(column);
		}
		if (name == null)
		{
			return name;
		}
		if (connection.isColumnNameUpperCase())
		{
			return name.ToUpper();
		}
		if (connection.isColumnNameLowerCase())
		{
			return name.ToLower();
		}
		return name;
	}

	public string do_getColumnName(int column)
	{
		DmColumn dmColumn = checkIndex(column);
		string text = ((connection.compatibleOracle() || innerMeta) ? dmColumn.name : (StringUtil.isEmpty(dmColumn.baseName) ? dmColumn.name : dmColumn.baseName));
		if (text == null)
		{
			return text;
		}
		if (connection.isColumnNameUpperCase())
		{
			return text.ToUpper();
		}
		if (connection.isColumnNameLowerCase())
		{
			return text.ToLower();
		}
		return text;
	}

	public string do_getSchemaName(int column)
	{
		string schemaName = checkIndex(column).schemaName;
		if (schemaName != null)
		{
			return schemaName;
		}
		return "";
	}

	public int do_getPrecision(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].jprec;
	}

	public int do_getScale(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].jscale;
	}

	public string do_getTableName(int column)
	{
		return checkIndex(column).tableName;
	}

	public string do_getCatalogName(int column)
	{
		return StringUtil.EMPTY;
	}

	public int do_getColumnType(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].jtype;
	}

	public string do_getColumnTypeName(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].dtypeName;
	}

	public bool do_isReadOnly(int column)
	{
		DmColumn dmColumn = checkIndex(column);
		if (!connection.compatibleOracle())
		{
			return dmColumn.Readonly;
		}
		return false;
	}

	public bool do_isWritable(int column)
	{
		return !do_isReadOnly(column);
	}

	public bool do_isDefinitelyWritable(int column)
	{
		return do_isWritable(column);
	}

	public string do_getColumnClassName(int column)
	{
		checkIndex(column);
		return typeDescs[column - 1].jclassName;
	}

	public int getColumnCount()
	{
		if (filterHead == null)
		{
			return do_getColumnCount();
		}
		return filterHead.ResultSetMetaData_getColumnCount(this);
	}

	public bool isAutoIncrement(int column)
	{
		if (filterHead == null)
		{
			return do_isAutoIncrement(column);
		}
		return filterHead.ResultSetMetaData_isAutoIncrement(this, column);
	}

	public bool isCaseSensitive(int column)
	{
		if (filterHead == null)
		{
			return do_isCaseSensitive(column);
		}
		return filterHead.ResultSetMetaData_isCaseSensitive(this, column);
	}

	public bool isSearchable(int column)
	{
		if (filterHead == null)
		{
			return do_isSearchable(column);
		}
		return filterHead.ResultSetMetaData_isSearchable(this, column);
	}

	public bool isCurrency(int column)
	{
		if (filterHead == null)
		{
			return do_isCurrency(column);
		}
		return filterHead.ResultSetMetaData_isCurrency(this, column);
	}

	public int isNullable(int column)
	{
		if (filterHead == null)
		{
			return do_isNullable(column);
		}
		return filterHead.ResultSetMetaData_isNullable(this, column);
	}

	public bool isSigned(int column)
	{
		if (filterHead == null)
		{
			return do_isSigned(column);
		}
		return filterHead.ResultSetMetaData_isSigned(this, column);
	}

	public int getColumnDisplaySize(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnDisplaySize(column);
		}
		return filterHead.ResultSetMetaData_getColumnDisplaySize(this, column);
	}

	public string getColumnLabel(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnLabel(column);
		}
		return filterHead.ResultSetMetaData_getColumnLabel(this, column);
	}

	public string getColumnName(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnName(column);
		}
		return filterHead.ResultSetMetaData_getColumnName(this, column);
	}

	public string getSchemaName(int column)
	{
		if (filterHead == null)
		{
			return do_getSchemaName(column);
		}
		return filterHead.ResultSetMetaData_getSchemaName(this, column);
	}

	public int getPrecision(int column)
	{
		if (filterHead == null)
		{
			return do_getPrecision(column);
		}
		return filterHead.ResultSetMetaData_getPrecision(this, column);
	}

	public int getScale(int column)
	{
		if (filterHead == null)
		{
			return do_getScale(column);
		}
		return filterHead.ResultSetMetaData_getScale(this, column);
	}

	public string getTableName(int column)
	{
		if (filterHead == null)
		{
			return do_getTableName(column);
		}
		return filterHead.ResultSetMetaData_getTableName(this, column);
	}

	public string getCatalogName(int column)
	{
		if (filterHead == null)
		{
			return do_getCatalogName(column);
		}
		return filterHead.ResultSetMetaData_getCatalogName(this, column);
	}

	public int getColumnType(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnType(column);
		}
		return filterHead.ResultSetMetaData_getColumnType(this, column);
	}

	public string getColumnTypeName(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnTypeName(column);
		}
		return filterHead.ResultSetMetaData_getColumnTypeName(this, column);
	}

	public bool isReadOnly(int column)
	{
		if (filterHead == null)
		{
			return do_isReadOnly(column);
		}
		return filterHead.ResultSetMetaData_isReadOnly(this, column);
	}

	public bool isWritable(int column)
	{
		if (filterHead == null)
		{
			return do_isWritable(column);
		}
		return filterHead.ResultSetMetaData_isWritable(this, column);
	}

	public bool isDefinitelyWritable(int column)
	{
		if (filterHead == null)
		{
			return do_isDefinitelyWritable(column);
		}
		return filterHead.ResultSetMetaData_isDefinitelyWritable(this, column);
	}

	public string getColumnClassName(int column)
	{
		if (filterHead == null)
		{
			return do_getColumnClassName(column);
		}
		return filterHead.ResultSetMetaData_getColumnClassName(this, column);
	}
}
