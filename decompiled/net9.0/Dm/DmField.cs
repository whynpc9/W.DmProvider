using A;

namespace Dm;

internal class DmField
{
	internal string schemaName;

	private byte[] m_SchNameForSet;

	internal string tableName;

	private byte[] m_TabNameForSet;

	internal string name;

	private byte[] m_NameForSet;

	internal string typeName;

	private byte[] m_TypeNameForSet;

	private byte m_TypeFlag;

	internal int type;

	internal int prec;

	internal int scale;

	public const int MASK_ORACLE_DATE = 1;

	public const int MASK_ORACLE_FLOAT = 2;

	public const int MASK_BFILE = 3;

	public const int MASK_LOCAL_DATETIME = 4;

	public int mask;

	public int originalPrec;

	private short m_InOutType = 3;

	private int m_Size;

	internal bool nullable;

	internal bool identity;

	private bool m_IsLob;

	private short m_Dbid;

	private int m_Schid;

	private int m_Tabid;

	private short m_Colid;

	private string m_BaseCatalog;

	private byte[] m_BaseCataLogForSet;

	private string m_BaseSchema;

	private byte[] m_BaseSchemaForSet;

	private string m_BaseTable;

	private byte[] m_BaseTableForSet;

	internal string baseName;

	private byte[] m_BaseColumnForSet;

	internal string ServerEncoding;

	public bool isBdta;

	public bool Readonly;

	public ComplexTypeDesc typeDescriptor;

	internal ComplexTypeDesc ComplexTypeDesc
	{
		get
		{
			return typeDescriptor;
		}
		set
		{
			typeDescriptor = value;
		}
	}

	public DmField(DmConnInstance conn)
	{
		ServerEncoding = conn.ConnProperty.ServerEncoding;
	}

	public DmField(string ServerEncoding)
	{
		this.ServerEncoding = ServerEncoding;
	}

	public void SetCType(int type)
	{
		this.type = type;
	}

	public void SetTypeFlag(byte typeFlag)
	{
		m_TypeFlag = typeFlag;
	}

	public void SetPrecision(int precision)
	{
		prec = precision;
	}

	public void SetSize(int size)
	{
		m_Size = size;
	}

	public void SetScale(int colnumscale)
	{
		scale = colnumscale;
	}

	public void SetInOutType(short type)
	{
		m_InOutType = type;
	}

	public void SetNullable(bool nullable)
	{
		this.nullable = nullable;
	}

	public void SetIdentity(bool identity)
	{
		this.identity = identity;
	}

	public void SetIsLob(bool isLob)
	{
		m_IsLob = isLob;
	}

	public void SetDbID(short dbid)
	{
		m_Dbid = dbid;
	}

	public void SetSchemaID(int schid)
	{
		m_Schid = schid;
	}

	public void SetTableID(int tabid)
	{
		m_Tabid = tabid;
	}

	public void SetColID(short colid)
	{
		m_Colid = colid;
	}

	public string GetName()
	{
		if (name == null)
		{
			if (m_NameForSet != null && m_NameForSet.Length != 0)
			{
				name = DmConvertion.GetString(m_NameForSet, 0, m_NameForSet.Length, ServerEncoding);
			}
			else
			{
				name = "";
			}
		}
		return name;
	}

	public int GetCType()
	{
		return type;
	}

	public byte GetTypeFlag()
	{
		return m_TypeFlag;
	}

	public int GetPrecision()
	{
		return prec;
	}

	public int GetSize()
	{
		return m_Size;
	}

	public int GetScale()
	{
		return scale;
	}

	public short GetInOutType()
	{
		return m_InOutType;
	}

	public bool GetNullable()
	{
		return nullable;
	}

	public bool GetIdentity()
	{
		return identity;
	}

	public bool GetIsLob()
	{
		return m_IsLob;
	}

	public short GetDbID()
	{
		return m_Dbid;
	}

	public string GetBaseCatalog()
	{
		if (m_BaseCatalog == null)
		{
			if (m_BaseCataLogForSet != null && m_BaseCataLogForSet.Length != 0)
			{
				m_BaseCatalog = DmConvertion.GetString(m_BaseCataLogForSet, 0, m_BaseCataLogForSet.Length, ServerEncoding);
			}
			else
			{
				m_BaseCatalog = "";
			}
		}
		return m_BaseCatalog;
	}

	public int GetSchemaID()
	{
		return m_Schid;
	}

	public string GetBaseSchema()
	{
		if (m_BaseSchema == null)
		{
			if (m_BaseSchemaForSet != null && m_BaseSchemaForSet.Length != 0)
			{
				m_BaseSchema = DmConvertion.GetString(m_BaseSchemaForSet, 0, m_BaseSchemaForSet.Length, ServerEncoding);
			}
			else
			{
				m_BaseSchema = "";
			}
		}
		return m_BaseSchema;
	}

	public int GetTableID()
	{
		return m_Tabid;
	}

	public string GetBaseTable()
	{
		if (m_BaseTable == null)
		{
			if (m_BaseTableForSet != null && m_BaseTableForSet.Length != 0)
			{
				m_BaseTable = DmConvertion.GetString(m_BaseTableForSet, 0, m_BaseTableForSet.Length, ServerEncoding);
			}
			else
			{
				m_BaseTable = "";
			}
		}
		return m_BaseTable;
	}

	public short GetColID()
	{
		return m_Colid;
	}

	public string GetBaseColumn()
	{
		if (baseName == null)
		{
			if (m_BaseColumnForSet != null && m_BaseColumnForSet.Length != 0)
			{
				baseName = DmConvertion.GetString(m_BaseColumnForSet, 0, m_BaseColumnForSet.Length, ServerEncoding);
			}
			else
			{
				baseName = "";
			}
		}
		return baseName;
	}

	public void SetTypeName(b msg, int len)
	{
		if (len != 0)
		{
			m_TypeNameForSet = msg.F(len);
		}
		else
		{
			m_TypeNameForSet = new byte[0];
		}
	}

	public void SetBaseColumn(b msg, int len)
	{
		if (len != 0)
		{
			m_BaseColumnForSet = msg.F(len);
		}
		else
		{
			m_BaseColumnForSet = new byte[0];
		}
	}

	public void SetName(b msg, int len)
	{
		if (len != 0)
		{
			m_NameForSet = msg.F(len);
		}
		else
		{
			m_NameForSet = new byte[0];
		}
	}

	public void SetSchema(b msg, int len)
	{
		if (len != 0)
		{
			m_SchNameForSet = msg.F(len);
		}
		else
		{
			m_SchNameForSet = new byte[0];
		}
	}

	public void SetTable(b msg, int len)
	{
		if (len != 0)
		{
			m_TabNameForSet = msg.F(len);
		}
		else
		{
			m_TabNameForSet = new byte[0];
		}
	}

	public string GetSchema()
	{
		if (schemaName == null)
		{
			if (m_SchNameForSet != null && m_SchNameForSet.Length != 0)
			{
				schemaName = DmConvertion.GetString(m_SchNameForSet, 0, m_SchNameForSet.Length, ServerEncoding);
			}
			else
			{
				schemaName = "";
			}
		}
		return schemaName;
	}

	public string GetTable()
	{
		if (tableName == null)
		{
			if (m_TabNameForSet != null && m_TabNameForSet.Length != 0)
			{
				tableName = DmConvertion.GetString(m_TabNameForSet, 0, m_TabNameForSet.Length, ServerEncoding);
			}
			else
			{
				tableName = "";
			}
		}
		return tableName;
	}

	public string GetTypeName()
	{
		if (typeName == null)
		{
			if (m_TypeNameForSet != null && m_TypeNameForSet.Length != 0)
			{
				typeName = DmConvertion.GetString(m_TypeNameForSet, 0, m_TypeNameForSet.Length, ServerEncoding);
			}
			else
			{
				typeName = "";
			}
		}
		return typeName;
	}

	public void recommendType(DmConnection conn, DmParameterInternal serverParam, int bindType, bool isNull)
	{
		if (serverParam.type != bindType && !checkComplexType(serverParam.type, serverParam.scale) && useClientBind(serverParam, bindType, isNull))
		{
			resetType(bindType);
		}
	}

	private bool checkComplexType(int type, int scale)
	{
		if ((type != 12 || scale != 5) && type != 119 && type != 117 && type != 122)
		{
			return type == 121;
		}
		return true;
	}

	public static bool useClientBind(DmParameterInternal serverParam, int bindType, bool isNull)
	{
		if (serverParam == null || serverParam.GetCType() == 54)
		{
			return true;
		}
		if (serverParam.GetTypeFlag() == 1 || serverParam.mask != 0 || isNull || wrongBindType(bindType, serverParam.GetCType()))
		{
			return false;
		}
		if (serverParam.GetCType() == 20 || serverParam.GetCType() == 21)
		{
			return false;
		}
		return true;
	}

	public static bool wrongBindType(int bindType, int serverType)
	{
		if (117 <= bindType && bindType <= 122 && 0 <= serverType && serverType <= 42)
		{
			return true;
		}
		return false;
	}

	public void resetType(int type)
	{
		this.type = type;
		scale = 0;
		switch (type)
		{
		case 3:
		case 13:
			prec = 1;
			break;
		case 5:
			prec = 1;
			break;
		case 6:
			prec = 2;
			break;
		case 7:
			prec = 4;
			break;
		case 8:
			prec = 8;
			break;
		case 0:
		case 1:
		case 2:
			prec = 32767;
			break;
		case 19:
			prec = int.MaxValue;
			break;
		case 17:
		case 18:
			prec = 32767;
			break;
		case 12:
			prec = int.MaxValue;
			break;
		case 14:
			prec = 3;
			break;
		case 15:
			prec = 5;
			scale = 6;
			break;
		case 22:
			prec = 7;
			scale = 6;
			break;
		case 16:
			prec = 8;
			scale = 6;
			break;
		case 23:
			prec = 10;
			scale = 6;
			break;
		case 26:
			prec = 9;
			scale = 9;
			break;
		case 27:
			prec = 11;
			scale = 9;
			break;
		case 9:
		case 10:
		case 11:
		case 20:
		case 21:
		case 117:
		case 119:
		case 121:
		case 122:
			prec = 0;
			break;
		case 25:
		case 54:
			this.type = 2;
			prec = 32767;
			break;
		}
	}
}
