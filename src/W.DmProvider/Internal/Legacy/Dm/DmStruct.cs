using System;
using System.Collections.Generic;

namespace W.Dm;

public class DmStruct : ComplexTypeData
{
	internal ComplexTypeDesc m_strctDesc;

	internal ComplexTypeData[] m_attribs;

	internal int m_objCount;

	internal int m_strCount;

	internal Dictionary<string, object> Dict;

	public DmStruct(ComplexTypeData[] atData, ComplexTypeDesc desc)
		: base(null, null)
	{
		m_strctDesc = desc;
		m_attribs = atData;
	}

	public ComplexTypeData[] getAttribsTypeData()
	{
		return m_attribs;
	}

	public DmStruct(ComplexTypeDesc desc, DmConnection conn, object[] objArr)
		: base(null, null)
	{
		if (desc == null)
		{
			throw new InvalidOperationException("DmStruct");
		}
		conn.checkClosed();
		m_strctDesc = desc;
		if (objArr == null)
		{
			m_attribs = new ComplexTypeData[desc.GetSize()];
			return;
		}
		if (desc.GetSize() != objArr.Length && desc.GetObjId() != 4)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_STRUCT_MEM_NOT_MATCH);
		}
		m_attribs = ComplexTypeData.toStruct(objArr, m_strctDesc);
	}

	public DmStruct(ComplexTypeDesc desc, DmConnection conn, Dictionary<string, object> dict)
		: base(null, null)
	{
		if (desc == null)
		{
			throw new InvalidOperationException("DmStruct initialize with dict");
		}
		conn.checkClosed();
		m_strctDesc = desc;
		Dict = dict;
	}

	public string getSQLTypeName()
	{
		return m_strctDesc.getFulName();
	}

	public object[] getAttributes()
	{
		return toJavaArray(this);
	}

	public Dictionary<string, object> GetIndexTable()
	{
		return Dict;
	}

	public ComplexTypeDesc getDesc()
	{
		return m_strctDesc;
	}

	public static DmStruct newInstanceOfLocal(ComplexTypeDesc desc, object[] objArr)
	{
		return new DmStruct(desc, desc.m_conn, objArr);
	}
}
