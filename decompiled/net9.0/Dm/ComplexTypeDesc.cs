using System.Data;
using A;
using Dm.util;

namespace Dm;

public class ComplexTypeDesc
{
	internal const int OBJ_BLOB_MAGIC = 78111999;

	internal const int CLTN_TYPE_IND_TABLE = 3;

	internal const int CLTN_TYPE_NST_TABLE = 2;

	internal const int CLTN_TYPE_VARRAY = 1;

	internal DmField column;

	internal SQLName m_sqlName;

	internal int m_objId = -1;

	internal int m_objVersion = -1;

	internal int m_outerId;

	internal int m_outerVer;

	internal int m_subId;

	internal int m_cltnType;

	internal int m_maxCnt;

	internal int m_length;

	internal int m_size;

	internal DmConnection m_conn;

	internal string m_serverEncoding;

	internal ComplexTypeDesc m_arrObj;

	internal ComplexTypeDesc[] m_fieldsObj;

	internal byte[] m_descBuf;

	internal ComplexTypeDesc m_keyDesc;

	internal ComplexTypeDesc m_valueDesc;

	internal ComplexTypeDesc(string fulName, DmConnection conn)
	{
		m_sqlName = new SQLName(fulName);
		m_conn = conn;
		column = new DmField(conn.GetConnInstance());
		ParseDescByName();
	}

	internal ComplexTypeDesc(DmConnection conn)
	{
		m_sqlName = new SQLName(conn);
		m_conn = conn;
		column = new DmField(conn.GetConnInstance());
	}

	internal void ParseDescByName()
	{
		string sql = "BEGIN :p1 = SF_DESCRIBE_TYPE(:p2); END;";
		DmParameter dmParameter = new DmParameter();
		dmParameter.do_ParameterName = "p1";
		dmParameter.do_Direction = ParameterDirection.Output;
		dmParameter.DmSqlType = DmDbType.Blob;
		DmParameter dmParameter2 = new DmParameter();
		dmParameter2.do_ParameterName = "p2";
		dmParameter2.do_Value = m_sqlName.m_fulName;
		DriverUtil.executeNonQuery(m_conn, sql, new DmParameter[2] { dmParameter, dmParameter2 });
		byte[] array = (byte[])dmParameter.do_Value;
		m_serverEncoding = m_conn.GetConnInstance().ConnProperty.ServerEncoding;
		Unpack(new b(array));
	}

	internal string getFulName()
	{
		return m_sqlName.GetFulName();
	}

	internal int GetDType()
	{
		return column.GetCType();
	}

	public int GetPrec()
	{
		return column.GetPrecision();
	}

	public int GetScale()
	{
		return column.GetScale();
	}

	public string GetServerEncoding()
	{
		if (m_serverEncoding != null)
		{
			return m_serverEncoding;
		}
		return m_conn.GetConnInstance().ConnProperty.ServerEncoding;
	}

	public int GetObjId()
	{
		return m_objId;
	}

	public int GetStaticArrayLength()
	{
		return m_length;
	}

	public int GetOuterId()
	{
		return m_outerId;
	}

	public int GetCltnType()
	{
		return m_cltnType;
	}

	public int GetMaxCnt()
	{
		return m_maxCnt;
	}

	private static int GetPackSize(ComplexTypeDesc complexTypeDesc)
	{
		int num = 0;
		switch (complexTypeDesc.column.GetCType())
		{
		case 117:
		case 122:
			return GetPackArraySize(complexTypeDesc);
		case 119:
			return GetPackClassSize(complexTypeDesc);
		case 121:
			return GetPackRecordSize(complexTypeDesc);
		default:
			num += 4;
			num += 4;
			return num + 4;
		}
	}

	private static void Pack(ComplexTypeDesc complexTypeDesc, b msg)
	{
		switch (complexTypeDesc.column.GetCType())
		{
		case 117:
		case 122:
			PackArray(complexTypeDesc, msg);
			return;
		case 119:
			PackClass(complexTypeDesc, msg);
			return;
		case 121:
			PackRecord(complexTypeDesc, msg);
			return;
		}
		msg.H(complexTypeDesc.column.GetCType());
		msg.H(complexTypeDesc.column.GetPrecision());
		msg.H(complexTypeDesc.column.GetScale());
	}

	internal static int GetPackArraySize(ComplexTypeDesc arrDesc)
	{
		int num = 0 + 4;
		string name = arrDesc.m_sqlName.m_name;
		int num2 = num + 2;
		string serverEncoding = arrDesc.GetServerEncoding();
		byte[] bytes = DmConvertion.GetBytes(name, serverEncoding);
		return num2 + bytes.Length + 4 + 4 + 4 + GetPackSize(arrDesc.m_arrObj);
	}

	internal static void PackArray(ComplexTypeDesc arrDesc, b msg)
	{
		msg.H(arrDesc.column.GetCType());
		msg.a(arrDesc.m_sqlName.m_name, arrDesc.GetServerEncoding());
		msg.H(arrDesc.m_objId);
		msg.H(arrDesc.m_objVersion);
		msg.H(arrDesc.m_length);
		Pack(arrDesc.m_arrObj, msg);
	}

	internal static void PackRecord(ComplexTypeDesc strctDesc, b msg)
	{
		msg.H(strctDesc.column.GetCType());
		msg.a(strctDesc.m_sqlName.m_name, strctDesc.GetServerEncoding());
		msg.H(strctDesc.m_objId);
		msg.H(strctDesc.m_objVersion);
		msg.A((short)strctDesc.m_size);
		for (int i = 0; i < strctDesc.m_size; i++)
		{
			Pack(strctDesc.m_fieldsObj[i], msg);
		}
	}

	internal static int GetPackRecordSize(ComplexTypeDesc strctDesc)
	{
		int num = 0;
		num += 4;
		string name = strctDesc.m_sqlName.m_name;
		num += 2;
		string serverEncoding = strctDesc.GetServerEncoding();
		byte[] bytes = DmConvertion.GetBytes(name, serverEncoding);
		num += bytes.Length;
		num += 4;
		num += 4;
		num += 2;
		for (int i = 0; i < strctDesc.m_size; i++)
		{
			num += GetPackSize(strctDesc.m_fieldsObj[i]);
		}
		return num;
	}

	internal static int GetPackClassSize(ComplexTypeDesc strctDesc)
	{
		int num = 0;
		num += 4;
		string name = strctDesc.m_sqlName.m_name;
		num += 2;
		string serverEncoding = strctDesc.GetServerEncoding();
		byte[] bytes = DmConvertion.GetBytes(name, serverEncoding);
		num += bytes.Length;
		num += 4;
		num += 4;
		if (strctDesc.m_objId == 4)
		{
			num += 4;
			num += 4;
			num += 2;
		}
		return num;
	}

	internal static void PackClass(ComplexTypeDesc strctDesc, b msg)
	{
		msg.H(strctDesc.column.GetCType());
		msg.a(strctDesc.m_sqlName.m_name, strctDesc.GetServerEncoding());
		msg.H(strctDesc.m_objId);
		msg.H(strctDesc.m_objVersion);
		if (strctDesc.m_objId == 4)
		{
			msg.H(strctDesc.m_outerId);
			msg.H(strctDesc.m_outerVer);
			msg.A((short)strctDesc.m_subId);
		}
	}

	internal void Unpack(b msg)
	{
		column.SetCType(msg.d());
		switch (column.GetCType())
		{
		case 117:
		case 122:
			UnpackArray(msg);
			break;
		case 119:
			UnpackClass(msg);
			break;
		case 121:
			UnpackRecord(msg);
			break;
		default:
			column.SetPrecision(msg.d());
			column.SetScale(msg.d());
			break;
		}
	}

	private void UnpackArray(b msg)
	{
		m_sqlName.m_name = msg.B(GetServerEncoding());
		m_sqlName.m_schId = msg.d();
		m_sqlName.m_packId = msg.d();
		m_objId = msg.d();
		m_objVersion = msg.d();
		m_length = msg.d();
		if (column.GetCType() == 117)
		{
			m_length = 0;
		}
		m_arrObj = new ComplexTypeDesc(m_conn);
		m_arrObj.Unpack(msg);
	}

	private void UnpackRecord(b msg)
	{
		m_sqlName.m_name = msg.B(GetServerEncoding());
		m_sqlName.m_schId = msg.d();
		m_sqlName.m_packId = msg.d();
		m_objId = msg.d();
		m_objVersion = msg.d();
		m_size = msg.C();
		m_fieldsObj = new ComplexTypeDesc[m_size];
		for (int i = 0; i < m_size; i++)
		{
			m_fieldsObj[i] = new ComplexTypeDesc(m_conn);
			m_fieldsObj[i].Unpack(msg);
		}
	}

	private void UnpackCltn_nestTab(b msg)
	{
		m_maxCnt = msg.d();
		m_arrObj = new ComplexTypeDesc(m_conn);
		m_arrObj.Unpack(msg);
	}

	private void UnpackCltn_indexTab(b msg)
	{
		m_maxCnt = msg.d();
		m_keyDesc = new ComplexTypeDesc(m_conn);
		m_keyDesc.Unpack(msg);
		m_valueDesc = new ComplexTypeDesc(m_conn);
		m_valueDesc.Unpack(msg);
	}

	private void UnpackCltn(b msg)
	{
		m_outerId = msg.d();
		m_outerVer = msg.d();
		m_subId = msg.C();
		m_cltnType = msg.C();
		switch (m_cltnType)
		{
		case 3:
			UnpackCltn_indexTab(msg);
			break;
		case 1:
		case 2:
			UnpackCltn_nestTab(msg);
			break;
		}
	}

	private void UnpackClass(b msg)
	{
		m_sqlName.m_name = msg.B(GetServerEncoding());
		m_sqlName.m_schId = msg.d();
		m_sqlName.m_packId = msg.d();
		m_objId = msg.d();
		m_objVersion = msg.d();
		if (m_objId == 4)
		{
			UnpackCltn(msg);
			return;
		}
		m_size = msg.C();
		m_fieldsObj = new ComplexTypeDesc[m_size];
		for (int i = 0; i < m_size; i++)
		{
			m_fieldsObj[i] = new ComplexTypeDesc(m_conn);
			m_fieldsObj[i].Unpack(msg);
		}
	}

	internal int CalcChkDescLen_array(ComplexTypeDesc desc)
	{
		return 0 + 2 + 4 + CalcChkDescLen(desc.m_arrObj);
	}

	private int CalcChkDescLen_record(ComplexTypeDesc desc)
	{
		int num = 0;
		num += 2;
		num += 2;
		for (int i = 0; i < desc.m_size; i++)
		{
			num += CalcChkDescLen(desc.m_fieldsObj[i]);
		}
		return num;
	}

	internal int CalcChkDescLen_class_normal(ComplexTypeDesc desc)
	{
		int num = 0;
		num += 2;
		for (int i = 0; i < desc.m_size; i++)
		{
			num += CalcChkDescLen(desc.m_fieldsObj[i]);
		}
		return num;
	}

	private int CalcChkDescLen_class_cnlt(ComplexTypeDesc desc)
	{
		int num = 0;
		num += 2;
		num += 4;
		switch (desc.GetCltnType())
		{
		case 3:
			num += CalcChkDescLen(desc.m_keyDesc);
			num += CalcChkDescLen(desc.m_valueDesc);
			break;
		case 1:
		case 2:
			num += CalcChkDescLen(desc.m_arrObj);
			break;
		}
		return num;
	}

	private int CalcChkDescLen_class(ComplexTypeDesc desc)
	{
		int num = 0;
		num += 2;
		num++;
		if (desc.m_objId == 4)
		{
			return num + CalcChkDescLen_class_cnlt(desc);
		}
		return num + CalcChkDescLen_class_normal(desc);
	}

	private int CalcChkDescLen_buildin()
	{
		return 0 + 2 + 2 + 2;
	}

	private int CalcChkDescLen(ComplexTypeDesc desc)
	{
		switch (desc.GetDType())
		{
		case 117:
		case 122:
			return CalcChkDescLen_array(desc);
		case 121:
			return CalcChkDescLen_record(desc);
		case 119:
			return CalcChkDescLen_class(desc);
		default:
			return CalcChkDescLen_buildin();
		}
	}

	private int MakeChkDesc_array(int offset, ComplexTypeDesc desc)
	{
		DmConvertion.SetShort(m_descBuf, offset, 117);
		offset += 2;
		DmConvertion.SetInt(m_descBuf, offset, desc.m_length);
		offset += 4;
		offset = MakeChkDesc(offset, desc.m_arrObj);
		return offset;
	}

	private int MakeChkDesc_record(int offset, ComplexTypeDesc desc)
	{
		DmConvertion.SetShort(m_descBuf, offset, 121);
		offset += 2;
		DmConvertion.SetShort(m_descBuf, offset, (short)desc.m_size);
		offset += 2;
		for (int i = 0; i < desc.m_size; i++)
		{
			offset = MakeChkDesc(offset, desc.m_fieldsObj[i]);
		}
		return offset;
	}

	private int MakeChkDesc_buildin(int offset, ComplexTypeDesc desc)
	{
		short num = (short)desc.GetDType();
		short val = 0;
		short val2 = 0;
		if (num != 12)
		{
			val = (short)desc.GetPrec();
			val2 = (short)desc.GetScale();
		}
		DmConvertion.SetShort(m_descBuf, offset, num);
		offset += 2;
		DmConvertion.SetShort(m_descBuf, offset, val);
		offset += 2;
		DmConvertion.SetShort(m_descBuf, offset, val2);
		offset += 2;
		return offset;
	}

	private int MakeChkDesc_class_normal(int offset, ComplexTypeDesc desc)
	{
		DmConvertion.SetShort(m_descBuf, offset, (short)desc.m_size);
		offset += 2;
		for (int i = 0; i < desc.m_size; i++)
		{
			offset = MakeChkDesc(offset, desc.m_fieldsObj[i]);
		}
		return offset;
	}

	private int MakeChkDesc_class_cltn(int offset, ComplexTypeDesc desc)
	{
		DmConvertion.SetShort(m_descBuf, offset, (short)desc.m_cltnType);
		offset += 2;
		DmConvertion.SetInt(m_descBuf, offset, desc.GetMaxCnt());
		offset += 4;
		switch (desc.m_cltnType)
		{
		case 3:
			offset = MakeChkDesc(offset, desc.m_keyDesc);
			offset = MakeChkDesc(offset, desc.m_valueDesc);
			break;
		case 1:
		case 2:
			offset = MakeChkDesc(offset, desc.m_arrObj);
			break;
		}
		return offset;
	}

	private int MakeChkDesc_class(int offset, ComplexTypeDesc desc)
	{
		DmConvertion.SetShort(m_descBuf, offset, 119);
		offset += 2;
		bool num = desc.m_objId == 4;
		if (num)
		{
			DmConvertion.SetByte(m_descBuf, offset, 1);
		}
		else
		{
			DmConvertion.SetByte(m_descBuf, offset, 0);
		}
		offset++;
		offset = ((!num) ? MakeChkDesc_class_normal(offset, desc) : MakeChkDesc_class_cltn(offset, desc));
		return offset;
	}

	private int MakeChkDesc(int offset, ComplexTypeDesc subDesc)
	{
		switch (subDesc.GetDType())
		{
		case 117:
		case 122:
			offset = MakeChkDesc_array(offset, subDesc);
			break;
		case 121:
			offset = MakeChkDesc_record(offset, subDesc);
			break;
		case 119:
			offset = MakeChkDesc_class(offset, subDesc);
			break;
		default:
			offset = MakeChkDesc_buildin(offset, subDesc);
			break;
		}
		return offset;
	}

	internal byte[] GetClassDescChkInfo()
	{
		if (m_descBuf != null)
		{
			return m_descBuf;
		}
		int num = CalcChkDescLen(this);
		m_descBuf = new byte[num];
		MakeChkDesc(0, this);
		return m_descBuf;
	}

	public int GetSize()
	{
		return m_size;
	}
}
