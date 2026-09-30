using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Numerics;
using System.Threading;
using W.Dm.Internal.Legacy.A;
using W.Dm.filter;
using W.Dm.Internal.Types;

namespace W.Dm;

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
	private bool explicitDbType;
	private DbType explicitDbTypeValue;
	private bool explicitDmSqlType;
	private DmDbType explicitDmSqlTypeValue;
	private bool inferredTypeKnown;
	internal DmParameterTypeSource TypeSource => explicitDmSqlType ? DmParameterTypeSource.ExplicitDmSqlType :
		explicitDbType ? DmParameterTypeSource.ExplicitDbType :
		inferredTypeKnown ? DmParameterTypeSource.ClrValue : DmParameterTypeSource.Unresolved;
	internal bool HasExplicitDbType => explicitDbType;
	internal bool HasExplicitDmSqlType => explicitDmSqlType;

	private string m_DmSqlTypeName = string.Empty;

	private bool m_IsNullable;

	private string m_SourceCol = string.Empty;

	private string m_pre = string.Empty;

	private object m_value;

	private DataRowVersion m_DataRowVer = DataRowVersion.Current;

	private ParameterDirection m_Direct = ParameterDirection.Input;

	private bool m_SourceColumnNullMapping;
	private readonly object ownershipGate = new();
	private DmParameterCollection ownerCollection;
	private int activeMutations;

	internal DmParameterCollection parameterCollection
	{
		get { lock (ownershipGate) return ownerCollection; }
	}

	internal void AttachCollection(DmParameterCollection collection)
	{
		lock (ownershipGate)
		{
			if (activeMutations != 0 || ownerCollection != null)
				throw new InvalidOperationException("Parameter already belongs to a collection or is being modified.");
			ownerCollection = collection;
		}
	}

	internal void DetachCollection(DmParameterCollection collection)
	{
		lock (ownershipGate)
		{
			if (!ReferenceEquals(ownerCollection, collection) || activeMutations != 0)
				throw new InvalidOperationException("Parameter collection ownership changed.");
			ownerCollection = null;
		}
	}

	private IDisposable BeginMutation()
	{
		lock (ownershipGate)
		{
			IDisposable commandMutation = ownerCollection?.PlanGate?.BeginMutation();
			activeMutations++;
			return new ParameterMutation(this, commandMutation);
		}
	}

	private sealed class ParameterMutation : IDisposable
	{
		private DmParameter owner;
		private readonly IDisposable commandMutation;
		internal ParameterMutation(DmParameter owner, IDisposable commandMutation)
		{
			this.owner = owner;
			this.commandMutation = commandMutation;
		}
		public void Dispose()
		{
			DmParameter captured = Interlocked.Exchange(ref owner, null);
			if (captured == null) return;
			try { commandMutation?.Dispose(); }
			finally { lock (captured.ownershipGate) captured.activeMutations--; }
		}
	}

	private global::W.Dm.Internal.Legacy.A.A m_refCursorStmt;

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

	public BaseFilter filterHead
	{
		get => null;
		set
		{
			if (value != null)
				throw new NotSupportedException("Legacy filter injection is unsupported.");
		}
	}

	private LogInfo logInfo;
	private RWInfo rwInfo;
	private RecoverInfo recoverInfo;
	public LogInfo LogInfo { get => logInfo; set { using var mutation = BeginMutation(); logInfo = value; } }
	public RWInfo RWInfo { get => rwInfo; set { using var mutation = BeginMutation(); rwInfo = value; } }
	public RecoverInfo RecoverInfo { get => recoverInfo; set { using var mutation = BeginMutation(); recoverInfo = value; } }

	internal DbType do_DbType
	{
		get
		{
			return m_DbType;
		}
		set
		{
			using var mutation = BeginMutation();
			if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
			explicitDbType = true;
			explicitDbTypeValue = value;
			m_DbType = value;
			if (!explicitDmSqlType) m_DmSqlType = DefaultProviderType(value);
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "ParameterName");
			string name = m_Name;
			GetParameterName(value);
			if (parameterCollection != null)
			{
				parameterCollection.ChangeNameIndex(this, name, value);
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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
			if (explicitDbType || explicitDmSqlType)
			{
				return;
			}
			InferFromValue(value);
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
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

	internal string Pre
	{
		get
		{
			return m_pre;
		}
		set
		{
			using var mutation = BeginMutation();
			m_pre = value;
		}
	}

	internal global::W.Dm.Internal.Legacy.A.A refCursorStmt
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
			using var mutation = BeginMutation();
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
			using var mutation = BeginMutation();
			DmTrace.TracePropertySet(TraceLevel.Debug, ClassName, "DmSqlType");
			CheckParameterDbType(value);
			explicitDmSqlType = true;
			explicitDmSqlTypeValue = value;
			m_DmSqlType = value;
			if (!explicitDbType) m_DbType = W.Dm.DmSqlType.DmSqlTypeToDbType(value);
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
			using var mutation = BeginMutation();
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
		InferFromValue(m_value);
	}

	internal void do_ResetDbType()
	{
		using var mutation = BeginMutation();
		explicitDbType = false;
		explicitDmSqlType = false;
		m_SetDbTypeFlag = false;
		InferFromValue(m_value);
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

	private void InferFromValue(object value)
	{
		m_SetDbTypeFlag = false;
		if (value is null or DBNull)
		{
			inferredTypeKnown = false;
			m_DbType = DbType.String;
			m_DmSqlType = DmDbType.VarChar;
			return;
		}
		if (value is Enum) value = Enum.GetUnderlyingType(value.GetType()) switch
		{
			Type type when type == typeof(byte) => (byte)0,
			Type type when type == typeof(sbyte) => (sbyte)0,
			Type type when type == typeof(ushort) => (ushort)0,
			Type type when type == typeof(short) => (short)0,
			Type type when type == typeof(uint) => (uint)0,
			Type type when type == typeof(int) => 0,
			Type type when type == typeof(ulong) => (ulong)0,
			_ => (long)0
		};
		DmDbType special = DmDbType.VarChar;
		m_DbType = value switch
		{
			sbyte => DbType.SByte,
			byte or short => DbType.Int16,
			ushort or int => DbType.Int32,
			uint or long => DbType.Int64,
			ulong or decimal or BigInteger or DmDecimal => DbType.Decimal,
			float => DbType.Single,
			double => DbType.Double,
			bool => DbType.Boolean,
			string or char[] => DbType.String,
			byte[] => DbType.Binary,
			Guid => DbType.Guid,
			DateOnly => DbType.Date,
			TimeOnly => DbType.Time,
			DateTime => DbType.DateTime,
			DateTimeOffset => DbType.DateTimeOffset,
			TimeSpan => DbType.Object,
			_ => DbType.Object
		};
		inferredTypeKnown = m_DbType != DbType.Object || value is TimeSpan;
		if (value is TimeSpan) special = DmDbType.IntervalDayToSecond;
		m_DmSqlType = value is TimeSpan ? special : DefaultProviderType(m_DbType);
	}

	internal void ValidateTypeConfiguration()
	{
		if (explicitDmSqlType && (explicitDmSqlTypeValue is DmDbType.ARRAY or DmDbType.Class or DmDbType.XDEC))
			throw new NotSupportedException("Complex and XDEC input types are not supported by this provider version.");
		if (!explicitDbType || !explicitDmSqlType) return;
		DmDbType expected = DefaultProviderType(explicitDbTypeValue);
		bool compatible = WidenUnsigned(expected) == WidenUnsigned(explicitDmSqlTypeValue) ||
			(explicitDbTypeValue == DbType.Binary && explicitDmSqlTypeValue is DmDbType.Binary or DmDbType.Blob or DmDbType.VarBinary) ||
			(explicitDbTypeValue == DbType.String && explicitDmSqlTypeValue is DmDbType.Clob or DmDbType.Text) ||
			(explicitDbTypeValue == DbType.Int32 && explicitDmSqlTypeValue == DmDbType.Int64) ||
			(explicitDbTypeValue == DbType.UInt16 && explicitDmSqlTypeValue is DmDbType.Int32 or DmDbType.Int64 or DmDbType.Decimal) ||
			(explicitDbTypeValue == DbType.UInt32 && explicitDmSqlTypeValue is DmDbType.Int64 or DmDbType.Decimal) ||
			(explicitDbTypeValue == DbType.UInt64 && explicitDmSqlTypeValue == DmDbType.Decimal);
		if (!compatible) throw new InvalidOperationException("Explicit DbType and DmSqlType conflict.");
	}

	internal void ValidateInputSourceRange(object value)
	{
		if (value is null or DBNull) return;
		if (explicitDbType) ValidateIntegerSourceRange(value, explicitDbTypeValue);
		if (explicitDmSqlType)
		{
			DbType? logicalType = explicitDmSqlTypeValue switch
			{
				DmDbType.Byte => DbType.Byte,
				DmDbType.UInt16 => DbType.UInt16,
				DmDbType.UInt32 => DbType.UInt32,
				DmDbType.UInt64 => DbType.UInt64,
				_ => null
			};
			if (logicalType is { } declared) ValidateIntegerSourceRange(value, declared);
		}
	}

	private static void ValidateIntegerSourceRange(object value, DbType declared)
	{
		if (value is bool boolean) value = boolean ? 1 : 0;
		switch (declared)
		{
		case DbType.Byte: DmNumericInput.ToIntegerExact(value, byte.MinValue, byte.MaxValue); break;
		case DbType.SByte: DmNumericInput.ToIntegerExact(value, sbyte.MinValue, sbyte.MaxValue); break;
		case DbType.Int16: DmNumericInput.ToIntegerExact(value, short.MinValue, short.MaxValue); break;
		case DbType.UInt16: DmNumericInput.ToIntegerExact(value, ushort.MinValue, ushort.MaxValue); break;
		case DbType.Int32: DmNumericInput.ToIntegerExact(value, int.MinValue, int.MaxValue); break;
		case DbType.UInt32: DmNumericInput.ToIntegerExact(value, uint.MinValue, uint.MaxValue); break;
		case DbType.Int64: DmNumericInput.ToIntegerExact(value, long.MinValue, long.MaxValue); break;
		case DbType.UInt64: DmNumericInput.ToIntegerExact(value, ulong.MinValue, ulong.MaxValue); break;
		}
	}

	internal (DmDbType Type, DmParameterTypeSource Source) ResolveType(DmParameterInternal described)
	{
		ValidateTypeConfiguration();
		if (explicitDmSqlType) return (WidenUnsigned(explicitDmSqlTypeValue), DmParameterTypeSource.ExplicitDmSqlType);
		if (explicitDbType) return (DefaultProviderType(explicitDbTypeValue), DmParameterTypeSource.ExplicitDbType);
		if (described != null && described.GetTypeFlag() == 1 &&
			DmParameterBinding.IsReliableDescribe(described.GetCType()))
		{
			DmDbType serverType = described.GetCType() switch
			{
				22 => DmDbType.Time,
				23 or 27 => DmDbType.DateTimeOffset,
				26 => DmDbType.DateTime,
				_ => W.Dm.DmSqlType.CTypeToDmDbType(described.GetCType())
			};
			return (serverType, DmParameterTypeSource.ServerDescribe);
		}
		if (inferredTypeKnown) return (m_DmSqlType, DmParameterTypeSource.ClrValue);
		throw new InvalidOperationException("Parameter type is unresolved; set DbType or DmSqlType.");
	}

	// DbType.Binary expresses bytes without a fixed-width contract. The provider's
	// explicit Binary type remains available when the caller supplies that contract.
	private static DmDbType DefaultProviderType(DbType type) => type == DbType.Binary
		? DmDbType.VarBinary : WidenUnsigned(W.Dm.DmSqlType.DbTypeToDmSqlType(type));

	private static DmDbType WidenUnsigned(DmDbType type) => type switch
	{
		DmDbType.Byte => DmDbType.Int16,
		DmDbType.UInt16 => DmDbType.Int32,
		DmDbType.UInt32 => DmDbType.Int64,
		DmDbType.UInt64 => DmDbType.Decimal,
		_ => type
	};

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
		return CloneCore(strictSnapshot: false);
	}

	internal DmParameter CloneForPlan() => CloneCore(strictSnapshot: true);

	private DmParameter CloneCore(bool strictSnapshot)
	{
		var clone = new DmParameter
		{
			m_Prec = m_Prec,
			m_Scale = m_Scale,
			m_Size = m_Size,
			m_DbType = m_DbType,
			m_DmSqlType = m_DmSqlType,
			explicitDbType = explicitDbType,
			explicitDbTypeValue = explicitDbTypeValue,
			explicitDmSqlType = explicitDmSqlType,
			explicitDmSqlTypeValue = explicitDmSqlTypeValue,
			inferredTypeKnown = inferredTypeKnown,
			m_Direct = m_Direct,
			m_IsNullable = m_IsNullable,
			m_Name = m_Name,
			m_pre = m_pre,
			m_SourceCol = m_SourceCol,
			m_DataRowVer = m_DataRowVer,
			m_value = CopyMutableValue(m_value, strictSnapshot),
			m_SourceColumnNullMapping = m_SourceColumnNullMapping,
			m_refCursorStmt = null,
			m_DmSqlTypeName = m_DmSqlTypeName,
			m_EFParaKind = m_EFParaKind,
			m_SetDbTypeFlag = m_SetDbTypeFlag,
			m_SetSizeFlag = m_SetSizeFlag,
			m_SetPrecFlag = m_SetPrecFlag,
			m_SetScaleFlag = m_SetScaleFlag
		};
		if (strictSnapshot && !clone.explicitDbType && !clone.explicitDmSqlType)
			clone.InferFromValue(clone.m_value);
		return clone;
	}

	private static object CopyMutableValue(object value, bool strictSnapshot)
	{
		if (value is null or DBNull) return value;
		if (value is byte[] bytes) return (byte[])bytes.Clone();
		if (value is char[] chars) return (char[])chars.Clone();
		if (strictSnapshot && value is DmXDec) return DmNumericInput.ToExactDecimal(value);
		if (!strictSnapshot) return value;
		if (value is Stream) throw new NotSupportedException("Streaming parameter snapshots are not supported.");
		if (value is Array) throw new NotSupportedException("Array parameter snapshots require an explicitly supported element type.");
		if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or
			float or double or decimal or BigInteger or DmDecimal or char or Guid or DateTime or DateTimeOffset or TimeSpan or DateOnly or TimeOnly or Enum)
			return value;
		throw new NotSupportedException("This parameter value type cannot be snapshotted safely.");
	}
}
