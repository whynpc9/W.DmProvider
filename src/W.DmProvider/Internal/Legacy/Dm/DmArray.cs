using System;

namespace W.Dm;

public class DmArray : ComplexTypeData
{
	internal ComplexTypeDesc m_arrDesc;

	internal ComplexTypeData[] m_arrData;

	internal object m_objArray;

	internal int m_itemCount;

	internal int m_itemSize;

	internal int m_objCount;

	internal int m_strCount;

	internal int[] m_objStrOffs;

	private void initData()
	{
		m_itemCount = 0;
		m_itemSize = 0;
		m_objCount = 0;
		m_strCount = 0;
		m_objStrOffs = null;
		m_dumyData = null;
		m_offset = 0;
		m_objArray = null;
	}

	public DmArray(ComplexTypeData[] atData, ComplexTypeDesc desc)
		: base(null, null)
	{
		m_arrDesc = desc;
		m_arrData = atData;
	}

	internal DmArray(ComplexTypeDesc arrDesc, DmConnection conn, Array objArr)
		: base(null, null)
	{
		if (arrDesc == null)
		{
			throw new InvalidOperationException("DmArray");
		}
		conn.checkClosed();
		initData();
		m_arrDesc = arrDesc;
		if (objArr == null)
		{
			m_arrData = new ComplexTypeData[0];
		}
		else
		{
			if (arrDesc.GetDType() == 122 && objArr.Length > arrDesc.GetStaticArrayLength())
			{
				throw new InvalidOperationException("DmArray");
			}
			m_arrData = ComplexTypeData.toArray(objArr, m_arrDesc);
		}
		m_itemCount = m_arrData.Length;
	}

	internal DmArray(ComplexTypeDesc arrDesc, DmConnection conn, object[] objArr)
		: base(null, null)
	{
		if (arrDesc == null)
		{
			throw new InvalidOperationException("DmArray");
		}
		conn.checkClosed();
		initData();
		m_arrDesc = arrDesc;
		if (objArr == null)
		{
			m_arrData = new ComplexTypeData[0];
		}
		else
		{
			if (arrDesc.GetDType() == 122 && objArr.Length > arrDesc.GetStaticArrayLength())
			{
				throw new InvalidOperationException("DmArray");
			}
			m_arrData = ComplexTypeData.toArray(objArr, m_arrDesc);
		}
		m_itemCount = m_arrData.Length;
	}

	internal ComplexTypeDesc GetItemDesc()
	{
		return m_arrDesc.m_arrObj;
	}

	internal int GetLength()
	{
		return m_arrDesc.m_length;
	}
}
