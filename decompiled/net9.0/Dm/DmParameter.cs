using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using A;
using Dm.filter;

namespace Dm;

public class DmParameter : DbParameter, IDbDataParameter, IDataParameter, ICloneable, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmParameter";

	private byte m_Prec;

	private byte m_Scale;

	private int m_Size;

	private string m_Name = string.Empty;

	private DbType m_DbType = DbType.String;

	private DmDbType m_DmSqlType = DmDbType.VarChar;

	private string m_DmSqlTypeName = string.Empty;

	private bool m_IsNullable;

	private string m_SourceCol = string.Empty;

	private string m_pre = string.Empty;

	private object m_value;

	private DataRowVersion m_DataRowVer = DataRowVersion.Current;

	private ParameterDirection m_Direct = ParameterDirection.Input;

	private bool m_SourceColumnNullMapping;

	private global::A.A m_refCursorStmt;

	private int m_EFParaKind;

	internal bool m_SetDbTypeFlag;

	internal bool m_SetSizeFlag;

	internal bool m_SetPrecFlag;

	internal bool m_SetScaleFlag;

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

	internal DbType do_DbType
	{
		get
		{
			return m_DbType;
		}
		set
		{
			m_DbType = value;
			m_DmSqlType = Dm.DmSqlType.DbTypeToDmSqlType(m_DbType);
			m_SetDbTypeFlag = true;
		}
	}

	internal ParameterDirection do_Direction
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "Direction");
			return m_Direct;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "Direction");
			CheckParameterDirection(value);
			m_Direct = value;
		}
	}

	internal bool do_IsNullable
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "IsNullable");
			return m_IsNullable;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "IsNullable");
			m_IsNullable = value;
		}
	}

	internal string do_ParameterName
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "ParameterName");
			return m_Name;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "ParameterName");
			string name = m_Name;
			GetParameterName(value);
			if (parameterCollection != null)
			{
				parameterCollection.ChangeName(this, name, value);
			}
			m_Name = value;
		}
	}

	internal int do_Size
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "Size");
			return m_Size;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "Size");
			m_Size = value;
			m_SetSizeFlag = true;
		}
	}

	internal string do_SourceColumn
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "SourceColumn");
			return m_SourceCol;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "SourceColumn");
			m_SourceCol = value;
		}
	}

	internal bool do_SourceColumnNullMapping
	{
		get
		{
			return m_SourceColumnNullMapping;
		}
		set
		{
			m_SourceColumnNullMapping = value;
		}
	}

	internal DataRowVersion do_SourceVersion
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "SourceVersion");
			return m_DataRowVer;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "SourceVersion");
			CheckSourceVersion(value);
			m_DataRowVer = value;
		}
	}

	internal object do_Value
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "Value");
			return m_value;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "Value");
			m_value = value;
			byte[] array = value as byte[];
			string text = value as string;
			if (!m_SetSizeFlag)
			{
				if (array != null)
				{
					m_Size = array.Length;
				}
				else if (text != null)
				{
					m_Size = text.Length;
				}
			}
			if (m_SetDbTypeFlag || value == null)
			{
				return;
			}
			if (value is Array && !(value is byte[]))
			{
				object obj = null;
				for (int i = 0; i < ((Array)value).Length; i++)
				{
					obj = ((Array)value).GetValue(i);
					if (obj != null)
					{
						SetDbTypeFromValue(obj);
						break;
					}
				}
			}
			else
			{
				SetDbTypeFromValue(value);
			}
		}
	}

	internal byte do_Precision
	{
		get
		{
			return m_Prec;
		}
		set
		{
			m_Prec = value;
			m_SetPrecFlag = true;
		}
	}

	internal byte do_Scale
	{
		get
		{
			return m_Scale;
		}
		set
		{
			m_Scale = value;
			m_SetScaleFlag = true;
		}
	}

	public override DbType DbType
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbType;
			}
			return filterHead.getDbType(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_DbType = value;
			}
			else
			{
				filterHead.setDbType(this, value);
			}
		}
	}

	public override ParameterDirection Direction
	{
		get
		{
			if (filterHead == null)
			{
				return do_Direction;
			}
			return filterHead.getDirection(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_Direction = value;
			}
			else
			{
				filterHead.setDirection(this, value);
			}
		}
	}

	public override bool IsNullable
	{
		get
		{
			if (filterHead == null)
			{
				return do_IsNullable;
			}
			return filterHead.getIsNullable(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_IsNullable = value;
			}
			else
			{
				filterHead.setIsNullable(this, value);
			}
		}
	}

	public override string ParameterName
	{
		get
		{
			if (filterHead == null)
			{
				return do_ParameterName;
			}
			return filterHead.getParameterName(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_ParameterName = value;
			}
			else
			{
				filterHead.setParameterName(this, value);
			}
		}
	}

	public override int Size
	{
		get
		{
			if (filterHead == null)
			{
				return do_Size;
			}
			return filterHead.getSize(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_Size = value;
			}
			else
			{
				filterHead.setSize(this, value);
			}
		}
	}

	public override string SourceColumn
	{
		get
		{
			if (filterHead == null)
			{
				return do_SourceColumn;
			}
			return filterHead.getSourceColumn(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_SourceColumn = value;
			}
			else
			{
				filterHead.setSourceColumn(this, value);
			}
		}
	}

	public override bool SourceColumnNullMapping
	{
		get
		{
			if (filterHead == null)
			{
				return do_SourceColumnNullMapping;
			}
			return filterHead.getSourceColumnNullMapping(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_SourceColumnNullMapping = value;
			}
			else
			{
				filterHead.setSourceColumnNullMapping(this, value);
			}
		}
	}

	public override DataRowVersion SourceVersion
	{
		get
		{
			if (filterHead == null)
			{
				return do_SourceVersion;
			}
			return filterHead.getSourceVersion(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_SourceVersion = value;
			}
			else
			{
				filterHead.setSourceVersion(this, value);
			}
		}
	}

	public override object Value
	{
		get
		{
			if (filterHead == null)
			{
				return do_Value;
			}
			return filterHead.getValue(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_Value = value;
			}
			else
			{
				filterHead.setValue(this, value);
			}
		}
	}

	public override byte Precision
	{
		get
		{
			if (filterHead == null)
			{
				return do_Precision;
			}
			return filterHead.getPrecision(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_Precision = value;
			}
			else
			{
				filterHead.setPrecision(this, value);
			}
		}
	}

	public override byte Scale
	{
		get
		{
			if (filterHead == null)
			{
				return do_Scale;
			}
			return filterHead.getScale(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_Scale = value;
			}
			else
			{
				filterHead.setScale(this, value);
			}
		}
	}

	internal DmParameterCollection parameterCollection { get; set; }

	internal string Pre
	{
		get
		{
			return m_pre;
		}
		set
		{
			m_pre = value;
		}
	}

	internal global::A.A refCursorStmt
	{
		get
		{
			return m_refCursorStmt;
		}
		set
		{
			m_refCursorStmt = value;
		}
	}

	public int EFParaKind
	{
		get
		{
			return m_EFParaKind;
		}
		set
		{
			m_EFParaKind = value;
		}
	}

	public DmDbType DmSqlType
	{
		get
		{
			DmTrace.TracePropertyGet(TraceLevel.Debug, ClassName, "DmSqlType");
			return m_DmSqlType;
		}
		set
		{
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "DmSqlType");
			CheckParameterDbType(value);
			m_DmSqlType = value;
			m_DbType = Dm.DmSqlType.DmSqlTypeToDbType(m_DmSqlType);
			m_SetDbTypeFlag = true;
		}
	}

	public string DmSqlTypeName
	{
		get
		{
			return m_DmSqlTypeName;
		}
		set
		{
			m_DmSqlTypeName = value;
		}
	}

	public DmParameter()
	{
		BaseFilter.CreateFilterChain(this);
		Pre = string.Empty;
		m_SetDbTypeFlag = false;
		m_SetSizeFlag = false;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
	}

	public DmParameter(string parameterName, DmDbType parameterType)
		: this(parameterName, parameterType, 0, string.Empty)
	{
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = false;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
	}

	public DmParameter(string parameterName, DmDbType parameterType, ParameterDirection direction)
		: this(parameterName, parameterType, 0, string.Empty)
	{
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = false;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
		CheckParameterDirection(direction);
		do_Direction = direction;
	}

	public DmParameter(string parameterName, DmDbType parameterType, int size)
		: this(parameterName, parameterType, size, string.Empty)
	{
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = true;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
	}

	public DmParameter(string parameterName, DmDbType parameterType, int size, string sourceColumn)
	{
		BaseFilter.CreateFilterChain(this);
		CheckParameterDbType(parameterType);
		do_ParameterName = parameterName;
		DmSqlType = parameterType;
		do_Size = size;
		do_SourceColumn = sourceColumn;
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = true;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
	}

	public DmParameter(string parameterName, DmDbType parameterType, int size, ParameterDirection direction, bool isNullable, byte precision, byte scale, string sourceColumn, DataRowVersion sourceVersion, object value)
	{
		BaseFilter.CreateFilterChain(this);
		CheckSourceVersion(sourceVersion);
		CheckParameterDirection(direction);
		CheckParameterDbType(parameterType);
		do_ParameterName = parameterName;
		DmSqlType = parameterType;
		do_Size = size;
		do_SourceColumn = sourceColumn;
		do_Direction = direction;
		do_IsNullable = isNullable;
		do_Precision = precision;
		do_Scale = scale;
		do_SourceVersion = sourceVersion;
		do_Value = value;
		if (m_value == null)
		{
			m_value = DBNull.Value;
		}
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = true;
		m_SetPrecFlag = true;
		m_SetScaleFlag = true;
	}

	public DmParameter(string parameterName, DmDbType parameterType, int size, string sourceColumn, ParameterDirection direction, bool isNullable, byte precision, byte scale, DataRowVersion sourceVersion, object value)
	{
		BaseFilter.CreateFilterChain(this);
		CheckSourceVersion(sourceVersion);
		CheckParameterDirection(direction);
		CheckParameterDbType(parameterType);
		do_ParameterName = parameterName;
		DmSqlType = parameterType;
		do_Size = size;
		do_SourceColumn = sourceColumn;
		do_Direction = direction;
		do_IsNullable = isNullable;
		do_Precision = precision;
		do_Scale = scale;
		do_SourceVersion = sourceVersion;
		do_Value = value;
		if (m_value == null)
		{
			m_value = DBNull.Value;
		}
		m_SetDbTypeFlag = true;
		m_SetSizeFlag = true;
		m_SetPrecFlag = true;
		m_SetScaleFlag = true;
	}

	public DmParameter(string parameterName, object value)
	{
		BaseFilter.CreateFilterChain(this);
		do_ParameterName = parameterName;
		m_value = value;
		if (m_value == null || m_value == DBNull.Value)
		{
			m_value = DBNull.Value;
		}
		m_SetDbTypeFlag = false;
		m_SetSizeFlag = false;
		m_SetPrecFlag = false;
		m_SetScaleFlag = false;
	}

	internal void do_ResetDbType()
	{
		DbType = DbType.String;
	}

	public override void ResetDbType()
	{
		if (filterHead == null)
		{
			do_ResetDbType();
		}
		else
		{
			filterHead.ResetDbType(this);
		}
	}

	private void SetDbTypeFromValue(object value)
	{
		if (value is Enum)
		{
			do_DbType = DbType.Int32;
			return;
		}
		switch (value.GetType().Name)
		{
		case "SByte":
			do_DbType = DbType.SByte;
			break;
		case "Byte":
			do_DbType = DbType.Byte;
			break;
		case "Int16":
			do_DbType = DbType.Int16;
			break;
		case "UInt16":
			do_DbType = DbType.UInt16;
			break;
		case "Int32":
			do_DbType = DbType.Int32;
			break;
		case "UInt32":
			do_DbType = DbType.UInt32;
			break;
		case "Int64":
			do_DbType = DbType.Int64;
			break;
		case "UInt64":
			do_DbType = DbType.UInt64;
			break;
		case "DateTime":
			do_DbType = DbType.DateTime;
			break;
		case "String":
			do_DbType = DbType.String;
			break;
		case "Single":
			do_DbType = DbType.Single;
			break;
		case "Double":
			do_DbType = DbType.Double;
			break;
		case "Decimal":
			do_DbType = DbType.Decimal;
			break;
		case "TimeSpan":
			DmSqlType = DmDbType.IntervalDayToSecond;
			break;
		case "Guid":
			do_DbType = DbType.Guid;
			break;
		case "Boolean":
			do_DbType = DbType.Boolean;
			break;
		case "DateTimeOffset":
			do_DbType = DbType.DateTimeOffset;
			break;
		default:
			do_DbType = DbType.Object;
			break;
		}
	}

	internal string GetParameterName(string name)
	{
		if (!string.IsNullOrEmpty(name) && (name[0] == '@' || name[0] == ':'))
		{
			Pre = ":";
		}
		return Pre + name;
	}

	private void CheckSourceVersion(DataRowVersion datarowversion)
	{
		if (datarowversion != DataRowVersion.Current && datarowversion != DataRowVersion.Default && datarowversion != DataRowVersion.Original && datarowversion != DataRowVersion.Proposed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_ENUM_VALUE);
		}
	}

	private void CheckParameterDirection(ParameterDirection direction)
	{
		if (direction != ParameterDirection.Input && direction != ParameterDirection.InputOutput && direction != ParameterDirection.Output && direction != ParameterDirection.ReturnValue)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_ENUM_VALUE);
		}
	}

	private void CheckParameterDbType(DmDbType parameterType)
	{
		if (parameterType < DmDbType.Blob || parameterType > DmDbType.Class)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_DmDbTYPE);
		}
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	public DmParameter Clone()
	{
		return new DmParameter
		{
			m_Prec = m_Prec,
			m_Scale = m_Scale,
			m_Size = m_Size,
			m_DbType = m_DbType,
			m_DmSqlType = m_DmSqlType,
			m_Direct = m_Direct,
			m_IsNullable = m_IsNullable,
			m_Name = m_Name,
			Pre = Pre,
			m_SourceCol = m_SourceCol,
			m_DataRowVer = m_DataRowVer,
			m_value = m_value,
			m_SourceColumnNullMapping = m_SourceColumnNullMapping,
			m_refCursorStmt = m_refCursorStmt,
			DmSqlTypeName = DmSqlTypeName,
			m_pre = m_pre,
			EFParaKind = m_EFParaKind,
			m_SetDbTypeFlag = m_SetDbTypeFlag,
			m_SetSizeFlag = m_SetSizeFlag,
			m_SetPrecFlag = m_SetPrecFlag,
			m_SetScaleFlag = m_SetScaleFlag
		};
	}
}
