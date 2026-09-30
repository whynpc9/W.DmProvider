using System;
using System.Collections.Generic;
using W.Dm.util;

namespace W.Dm;

public class ComplexTypeData
{
	internal const int ARRAY_TYPE_SHORT = 1;

	internal const int ARRAY_TYPE_INTEGER = 2;

	internal const int ARRAY_TYPE_LONG = 3;

	internal const int ARRAY_TYPE_FLOAT = 4;

	internal const int ARRAY_TYPE_DOUBLE = 5;

	internal object m_dumyData;

	internal int m_offset;

	internal int m_bufLen;

	internal byte[] m_dataBuf;

	internal byte[] m_objBlobDescBuf;

	internal bool m_isFromBlob;

	internal int m_packid = -1;

	internal List<object> m_objRefArr = new List<object>();

	protected ComplexTypeData(object val, byte[] dataBuf)
	{
		m_dumyData = val;
		m_offset = 0;
		m_bufLen = 0;
		m_dataBuf = dataBuf;
	}

	internal static ComplexTypeData[] toStruct(object[] objArr, ComplexTypeDesc desc)
	{
		int size = desc.GetSize();
		if (size == 0 && objArr != null)
		{
			return toArray(objArr, desc);
		}
		ComplexTypeData[] array = new ComplexTypeData[size];
		for (int i = 0; i < size; i++)
		{
			if (objArr[i] == null || objArr[i] is DmStruct || objArr[i] is DmArray)
			{
				array[i] = new ComplexTypeData(objArr[i], null);
				continue;
			}
			switch (desc.m_fieldsObj[i].GetDType())
			{
			case 119:
			case 121:
				array[i] = new ComplexTypeData(new DmStruct(toStruct((object[])objArr[i], desc.m_fieldsObj[i]), desc.m_fieldsObj[i]), null);
				break;
			case 117:
			case 122:
				array[i] = new ComplexTypeData(new DmArray(toArray((Array)objArr[i], desc.m_fieldsObj[i]), desc.m_fieldsObj[i]), null);
				break;
			default:
				array[i] = toMemberObj(objArr[i], desc.m_fieldsObj[i]);
				break;
			}
		}
		return array;
	}

	internal static ComplexTypeData[] toArray(Array objArr, ComplexTypeDesc desc)
	{
		int length = objArr.Length;
		ComplexTypeData[] array = new ComplexTypeData[length];
		for (int i = 0; i < length; i++)
		{
			if (objArr.GetValue(i) == null || objArr.GetValue(i) is DmStruct || objArr.GetValue(i) is DmArray)
			{
				array[i] = new ComplexTypeData(objArr.GetValue(i), null);
				continue;
			}
			switch (desc.m_arrObj.GetDType())
			{
			case 119:
			case 121:
				array[i] = new ComplexTypeData(new DmStruct(toStruct((object[])objArr.GetValue(i), desc.m_arrObj), desc.m_arrObj), null);
				break;
			case 117:
			case 122:
				if (!(objArr.GetValue(i) is Array) && desc.m_arrObj.m_arrObj != null)
				{
					objArr.SetValue(makeupObjToArr(objArr.GetValue(i), desc.m_arrObj), i);
				}
				array[i] = new ComplexTypeData(new DmArray(toArray((Array)objArr.GetValue(i), desc.m_arrObj), desc.m_arrObj), null);
				break;
			default:
				array[i] = toMemberObj(objArr.GetValue(i), desc.m_arrObj);
				break;
			}
		}
		return array;
	}

	private static object[] makeupObjToArr(object obj, ComplexTypeDesc objDesc)
	{
		int dType = objDesc.GetDType();
		bool flag = true;
		int num = 0;
		if (dType == 122)
		{
			flag = false;
			num = objDesc.m_length;
		}
		int dType2 = objDesc.m_arrObj.GetDType();
		if (dType2 == 17 || dType2 == 18 || dType2 == 3)
		{
			string text = "";
			if (obj is int)
			{
				throw new InvalidCastException("makeupObjToArr");
			}
			if (obj is long)
			{
				throw new InvalidCastException("makeupObjToArr");
			}
			if (obj is string)
			{
				text = (string)obj;
				int num2 = (flag ? text.Length : num);
				object[] array = new object[num2];
				byte[] bytes = DmConvertion.GetBytes(text, objDesc.GetServerEncoding());
				for (int i = 0; i < num2; i++)
				{
					array[i] = bytes[i];
				}
				return array;
			}
			throw new InvalidCastException("makeupObjToArr");
		}
		throw new NotSupportedException("makeupObjToArr");
	}

	private static ComplexTypeData toMemberObj(object mem, ComplexTypeDesc desc)
	{
		int scale = desc.GetScale();
		int prec = desc.GetPrec();
		int dType = desc.GetDType();
		if (mem == null)
		{
			return new ComplexTypeData(null, null);
		}
		DmParameterInternal dmParameterInternal = new DmParameterInternal(desc.m_conn.m_ConnInst);
		dmParameterInternal.SetCType(dType);
		dmParameterInternal.SetPrecision(prec);
		dmParameterInternal.SetScale(scale);
		dmParameterInternal.SetTypeFlag(1);
		new DmSetValue(desc.GetServerEncoding()).SetObject(dmParameterInternal.GetParamValue()[0], mem, desc.m_conn, null, dType, dmParameterInternal);
		return new ComplexTypeData(mem, dmParameterInternal.GetInValue(0));
	}

	private static byte[] TypeDataToBytes(ComplexTypeData data, ComplexTypeDesc desc)
	{
		int dType = desc.GetDType();
		if (data.m_dumyData == null)
		{
			byte[] array = realocBuffer(null, 0, 2);
			DmConvertion.SetByte(array, 0, 0);
			DmConvertion.SetByte(array, 1, 0);
			return array;
		}
		switch (dType)
		{
		case 117:
		{
			byte[] array = arrayToBytes((DmArray)data.m_dumyData, desc);
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		case 122:
		{
			byte[] array = sarrayToBytes((DmArray)data.m_dumyData, desc);
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		case 119:
		{
			byte[] array = objToBytes(data.m_dumyData, desc);
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		case 121:
		{
			byte[] array = recordToBytes((DmStruct)data.m_dumyData, desc);
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		case 12:
		case 19:
		{
			byte[] array = convertLobToBytes(data.m_dumyData, desc.column.GetCType(), desc.GetServerEncoding());
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		case 13:
		{
			byte[] array = realocBuffer(null, 0, 2);
			DmConvertion.SetByte(array, 0, 0);
			if (data.m_dataBuf != null && data.m_dataBuf.Length != 0)
			{
				DmConvertion.SetByte(array, 1, data.m_dataBuf[0]);
			}
			else
			{
				DmConvertion.SetByte(array, 1, 0);
			}
			return array;
		}
		default:
		{
			byte[] array = data.m_dataBuf;
			int prec = desc.GetPrec();
			if (dType == 2 && array.Length > prec)
			{
				DmError.ThrowDmException("innerData(" + array?.ToString() + ")的长度大于prec(" + prec + ")");
			}
			byte[] array2 = realocBuffer(null, 0, array.Length + 1 + 1 + 2);
			DmConvertion.SetByte(array2, 0, 0);
			int num = 1;
			DmConvertion.SetByte(array2, num, 1);
			num++;
			DmConvertion.SetShort(array2, num, (short)array.Length);
			num += 2;
			Array.Copy(array, 0, array2, num, array.Length);
			return array2;
		}
		}
	}

	private static byte[] convertLobToBytes(object value, int dtype, string serverEncoding)
	{
		switch (dtype)
		{
		case 12:
		{
			DmBlob obj2 = (DmBlob)value;
			int num = (int)obj2.do_length();
			byte[] bytes2 = obj2.GetBytes(0L, num);
			byte[] array = new byte[num + 4];
			DmConvertion.SetInt(array, 0, num);
			Array.Copy(bytes2, 0, array, 4, num);
			return array;
		}
		case 19:
		{
			DmClob obj = (DmClob)value;
			int num = (int)obj.do_length();
			byte[] bytes = DmConvertion.GetBytes(obj.getSubString(0L, num), serverEncoding);
			byte[] array = new byte[bytes.Length + 4];
			DmConvertion.SetInt(array, 0, num);
			Array.Copy(bytes, 0, array, 4, num);
			return array;
		}
		default:
			throw new InvalidCastException();
		}
	}

	internal static byte[] sarrayToBytes(DmArray data, ComplexTypeDesc desc)
	{
		int num = data.m_arrData.Length;
		byte[][] array = new byte[num][];
		if (desc.GetObjId() == 4)
		{
			return cltnToBytes(data.m_arrData, desc);
		}
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			array[i] = TypeDataToBytes(data.m_arrData[i], desc.m_arrObj);
			num2 += array[i].Length;
		}
		num2 += 8;
		byte[] array2 = realocBuffer(null, 0, num2);
		int num3 = 0;
		DmConvertion.SetInt(array2, num3, num2);
		num3 += 4;
		DmConvertion.SetInt(array2, num3, data.GetLength());
		num3 += 4;
		for (int j = 0; j < num; j++)
		{
			Array.Copy(array[j], 0, array2, num3, array[j].Length);
			num3 += array[j].Length;
		}
		return array2;
	}

	internal static byte[] cltnToBytes(ComplexTypeData[] m_arrData, ComplexTypeDesc desc)
	{
		byte[][] array = new byte[m_arrData.Length][];
		int num = 5;
		num += 8;
		for (int i = 0; i < m_arrData.Length; i++)
		{
			array[i] = TypeDataToBytes(m_arrData[i], desc.m_arrObj);
			num += array[i].Length;
		}
		byte[] array2 = realocBuffer(null, 0, num);
		int num2 = 0;
		DmConvertion.SetByte(array2, num2, 0);
		num2++;
		num2 += 4;
		DmConvertion.SetShort(array2, num2, (short)desc.GetCltnType());
		num2 += 2;
		DmConvertion.SetShort(array2, num2, (short)desc.m_arrObj.GetDType());
		num2 += 2;
		DmConvertion.SetInt(array2, num2, m_arrData.Length);
		num2 += 4;
		for (int j = 0; j < m_arrData.Length; j++)
		{
			Array.Copy(array[j], 0, array2, num2, array[j].Length);
			num2 += array[j].Length;
		}
		DmConvertion.SetInt(array2, 1, num2);
		return array2;
	}

	internal static byte[] IndexTableToBytes(DmStruct data, ComplexTypeDesc desc)
	{
		Dictionary<string, object> dict = data.Dict;
		byte[][] array = new byte[dict.Count][];
		int num = 5;
		num += 8;
		string serverEncoding = desc.GetServerEncoding();
		int count = 0;
		foreach (KeyValuePair<string, object> item in dict)
		{
			KeyToBytes(item.Key, desc.m_keyDesc, serverEncoding, out var keyBytes);
			ValueToBytes(item.Value, desc.m_valueDesc, out var valueBytes);
			PairBytesToResult(array, ref count, keyBytes, valueBytes, ref num);
		}
		byte[] array2 = realocBuffer(null, 0, num);
		int num2 = 0;
		DmConvertion.SetByte(array2, num2, 0);
		num2++;
		num2 += 4;
		DmConvertion.SetShort(array2, num2, (short)desc.GetCltnType());
		num2 += 2;
		DmConvertion.SetShort(array2, num2, (short)desc.m_valueDesc.GetDType());
		num2 += 2;
		DmConvertion.SetInt(array2, num2, dict.Count);
		num2 += 4;
		for (int i = 0; i < dict.Count; i++)
		{
			Array.Copy(array[i], 0, array2, num2, array[i].Length);
			num2 += array[i].Length;
		}
		DmConvertion.SetInt(array2, 1, num2);
		return array2;
	}

	internal static void KeyToBytes(string key, ComplexTypeDesc keyDesc, string serverEncoding, out byte[] keyBytes)
	{
		int prec = keyDesc.GetPrec();
		if (keyDesc.GetDType() == 2)
		{
			if (key.Length > prec)
			{
				DmError.ThrowDmException("key(" + key + ")的长度大于prec(" + prec + ")");
			}
			keyBytes = new byte[4 + key.Length];
			ByteUtil.setStringWithLength4(keyBytes, 0, key, serverEncoding);
		}
		else
		{
			keyBytes = DmConvertion.IntToByteArray(Convert.ToInt32(key));
		}
	}

	internal static void ValueToBytes(object value, ComplexTypeDesc valueDesc, out byte[] valueBytes)
	{
		if (value == null)
		{
			valueBytes = TypeDataToBytes(new ComplexTypeData(null, DmConvertion.IntToByteArray(0L)), valueDesc);
		}
		else if (value is DmStruct || value is DmArray)
		{
			valueBytes = TypeDataToBytes(new ComplexTypeData(value, null), valueDesc);
		}
		else
		{
			valueBytes = TypeDataToBytes(new ComplexTypeData(value, N2DB.fromObject(value, valueDesc.column, valueDesc.m_conn)), valueDesc);
		}
	}

	internal static void PairBytesToResult(byte[][] results, ref int count, byte[] keyBytes, byte[] valueBytes, ref int totalLen)
	{
		results[count] = realocBuffer(null, 0, keyBytes.Length + valueBytes.Length);
		Array.Copy(keyBytes, 0, results[count], 0, keyBytes.Length);
		Array.Copy(valueBytes, 0, results[count], keyBytes.Length, valueBytes.Length);
		totalLen += results[count].Length;
		count++;
	}

	public static byte[] arrayToBytes(DmArray data, ComplexTypeDesc desc)
	{
		byte[][] array = new byte[data.m_arrData.Length][];
		if (desc.GetObjId() == 4)
		{
			return cltnToBytes(data.m_arrData, desc);
		}
		int num = 0;
		for (int i = 0; i < data.m_arrData.Length; i++)
		{
			array[i] = TypeDataToBytes(data.m_arrData[i], desc.m_arrObj);
			num += array[i].Length;
		}
		num += 20;
		int num2 = data.m_objCount + data.m_strCount;
		if (num2 > 0)
		{
			num += 2 * num2;
		}
		byte[] array2 = realocBuffer(null, 0, num);
		DmConvertion.SetInt(array2, 0, num);
		int num3 = 4;
		DmConvertion.SetInt(array2, num3, data.m_arrData.Length);
		num3 += 4;
		DmConvertion.SetInt(array2, num3, 0);
		num3 += 4;
		DmConvertion.SetInt(array2, num3, data.m_objCount);
		num3 += 4;
		DmConvertion.SetInt(array2, num3, data.m_strCount);
		num3 += 4;
		for (int j = 0; j < num2; j++)
		{
			DmConvertion.SetInt(array2, num3, data.m_objStrOffs[j]);
			num3 += 4;
		}
		for (int k = 0; k < data.m_arrData.Length; k++)
		{
			Array.Copy(array[k], 0, array2, num3, array[k].Length);
			num3 += array[k].Length;
		}
		return array2;
	}

	public static byte[] objToBytes(object data, ComplexTypeDesc desc)
	{
		if (data is DmArray)
		{
			return arrayToBytes((DmArray)data, desc);
		}
		if (data is DmStruct)
		{
			return structToBytes((DmStruct)data, desc);
		}
		return null;
	}

	public static byte[] structToBytes(DmStruct data, ComplexTypeDesc desc)
	{
		if (desc.GetObjId() == 4)
		{
			switch (desc.GetCltnType())
			{
			case 3:
				return IndexTableToBytes(data, desc);
			case 2:
				return cltnToBytes(data.m_attribs, desc);
			}
		}
		int size = desc.GetSize();
		byte[][] array = new byte[size][];
		int num = 0;
		for (int i = 0; i < size; i++)
		{
			array[i] = TypeDataToBytes(data.m_attribs[i], desc.m_fieldsObj[i]);
			num += array[i].Length;
		}
		num += 5;
		byte[] array2 = realocBuffer(null, 0, num);
		int num2 = 0;
		DmConvertion.SetByte(array2, num2, 0);
		num2++;
		DmConvertion.SetInt(array2, num2, num);
		num2 += 4;
		for (int j = 0; j < size; j++)
		{
			Array.Copy(array[j], 0, array2, num2, array[j].Length);
			num2 += array[j].Length;
		}
		return array2;
	}

	public static byte[] recordToBytes(DmStruct data, ComplexTypeDesc desc)
	{
		int size = desc.GetSize();
		byte[][] array = new byte[size][];
		int num = 0;
		for (int i = 0; i < size; i++)
		{
			array[i] = TypeDataToBytes(data.m_attribs[i], desc.m_fieldsObj[i]);
			num += array[i].Length;
		}
		num += 4;
		byte[] array2 = realocBuffer(null, 0, num);
		DmConvertion.SetInt(array2, 0, num);
		int num2 = 4;
		for (int j = 0; j < desc.GetSize(); j++)
		{
			Array.Copy(array[j], 0, array2, num2, array[j].Length);
			num2 += array[j].Length;
		}
		return array2;
	}

	private static ComplexTypeData bytesToBlob(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		int offset = outVal.m_offset;
		int num = DmConvertion.GetInt(val, offset);
		offset += 4;
		byte[] bytes = DmConvertion.GetBytes(val, offset, num);
		offset += num;
		outVal.m_offset = offset;
		return new ComplexTypeData(DmBlob.newInstanceOfLocal(bytes, desc.m_conn), bytes);
	}

	private static ComplexTypeData bytesToClob(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc, string serverEncoding)
	{
		int offset = outVal.m_offset;
		int num = DmConvertion.GetInt(val, offset);
		offset += 4;
		DmConvertion.GetBytes(val, offset, num);
		offset += num;
		outVal.m_offset = offset;
		throw new NotSupportedException("bytesToClob");
	}

	private static ComplexTypeData bytesToTypeData(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		int offset = outVal.m_offset;
		offset++;
		byte b2 = DmConvertion.GetByte(val, offset);
		offset = (outVal.m_offset = offset + 1);
		if (desc.GetDType() == 13)
		{
			return new ComplexTypeData(b2 != 0, DmConvertion.GetBytes(val, offset - 1, 1));
		}
		byte[] dataBuf = null;
		switch (desc.GetDType())
		{
		case 119:
			if ((b2 & 1) != 0)
			{
				object val2 = bytesToObj(val, outVal, desc);
				if (outVal.m_offset > offset)
				{
					dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
				}
				return new ComplexTypeData(val2, dataBuf);
			}
			return new ComplexTypeData(null, null);
		case 117:
			if ((b2 & 1) != 0)
			{
				DmArray val3 = bytesToArray(val, outVal, desc);
				if (outVal.m_offset > offset)
				{
					dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
				}
				return new ComplexTypeData(val3, dataBuf);
			}
			return new ComplexTypeData(null, null);
		case 121:
			if ((b2 & 1) != 0)
			{
				DmStruct val5 = bytesToRecord(val, outVal, desc);
				if (outVal.m_offset > offset)
				{
					dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
				}
				return new ComplexTypeData(val5, dataBuf);
			}
			return new ComplexTypeData(null, null);
		case 122:
			if ((b2 & 1) != 0)
			{
				DmArray val4 = bytesToSArray(val, outVal, desc);
				if (outVal.m_offset > offset)
				{
					dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
				}
				return new ComplexTypeData(val4, dataBuf);
			}
			return new ComplexTypeData(null, null);
		case 12:
			if ((b2 & 1) != 0)
			{
				return bytesToBlob(val, outVal, desc);
			}
			return new ComplexTypeData(null, null);
		case 19:
			if ((b2 & 1) != 0)
			{
				return bytesToClob(val, outVal, desc, desc.GetServerEncoding());
			}
			return new ComplexTypeData(null, null);
		default:
			if ((b2 & 1) != 0)
			{
				return convertBytes2BaseData(val, outVal, desc);
			}
			return new ComplexTypeData(null, null);
		}
	}

	private static bool checkObjExist(byte[] val, ComplexTypeData outVal)
	{
		int offset = outVal.m_offset;
		byte num = DmConvertion.GetByte(val, offset);
		offset++;
		outVal.m_offset = offset;
		if (num == 1)
		{
			return true;
		}
		outVal.m_offset += 4;
		return false;
	}

	private static DmStruct findObjByPackId(byte[] val, ComplexTypeData outVal)
	{
		int offset = outVal.m_offset;
		int num = DmConvertion.GetInt(val, offset);
		offset += 4;
		outVal.m_offset = offset;
		if (num < 0 || num > outVal.m_packid)
		{
			throw new InvalidCastException("findObjByPackId");
		}
		return (DmStruct)outVal.m_objRefArr[num];
	}

	private static void addObjToRefArr(ComplexTypeData outVal, object objToAdd)
	{
		outVal.m_objRefArr.Add(objToAdd);
		outVal.m_packid++;
	}

	private static bool checkObjCltn(ComplexTypeDesc desc)
	{
		return desc.m_objId == 4;
	}

	private static DmStruct bytesToObj_EXACT(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		DmStruct dmStruct = new DmStruct(null, desc);
		int offset = outVal.m_offset;
		int size = desc.GetSize();
		outVal.m_offset = offset;
		dmStruct.m_attribs = new ComplexTypeData[size];
		for (int i = 0; i < size; i++)
		{
			ComplexTypeDesc desc2 = desc.m_fieldsObj[i];
			dmStruct.m_attribs[i] = bytesToTypeData(val, outVal, desc2);
		}
		dmStruct.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmStruct;
	}

	private static DmArray bytesToNestTab(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		int offset = outVal.m_offset;
		offset += 2;
		int num = DmConvertion.GetInt(val, offset);
		offset = (outVal.m_offset = offset + 4);
		DmArray dmArray = new DmArray(null, desc);
		dmArray.m_itemCount = num;
		dmArray.m_arrData = new ComplexTypeData[num];
		for (int i = 0; i < num; i++)
		{
			dmArray.m_arrData[i] = bytesToTypeData(val, outVal, desc.m_arrObj);
		}
		dmArray.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmArray;
	}

	private static DmStruct bytesToIndexTab(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		int offset = outVal.m_offset;
		offset += 2;
		int num = DmConvertion.GetInt(val, offset);
		offset = (outVal.m_offset = offset + 4);
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		DmStruct dmStruct = new DmStruct(desc, desc.m_conn, dictionary);
		string serverEncoding = desc.GetServerEncoding();
		for (int i = 0; i < num; i++)
		{
			string key = bytesToKey(val, outVal, desc.m_keyDesc, serverEncoding);
			object value = bytesToValue(val, outVal, desc.m_valueDesc);
			dictionary[key] = value;
		}
		dmStruct.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmStruct;
	}

	private static string bytesToKey(byte[] val, ComplexTypeData outVal, ComplexTypeDesc keyDesc, string serverEncoding)
	{
		int num = outVal.m_offset;
		int num2;
		string result;
		if (keyDesc.GetDType() == 2)
		{
			num2 = DmConvertion.GetInt(val, num);
			num += 4;
			result = DmConvertion.GetString(val, num, num2, serverEncoding);
		}
		else
		{
			num2 = 4;
			result = DmConvertion.GetInt(val, num).ToString();
		}
		num += num2;
		outVal.m_offset = num;
		return result;
	}

	private static object bytesToValue(byte[] val, ComplexTypeData outVal, ComplexTypeDesc valueDesc)
	{
		return bytesToTypeData(val, outVal, valueDesc).m_dumyData;
	}

	private static object bytesToCltn(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		object result = null;
		int offset = outVal.m_offset;
		int num = DmConvertion.GetShort(val, offset);
		offset += 2;
		outVal.m_offset = offset;
		switch (num)
		{
		case 3:
			result = bytesToIndexTab(val, outVal, desc);
			break;
		case 1:
		case 2:
			result = bytesToNestTab(val, outVal, desc);
			break;
		}
		return result;
	}

	public static object bytesToObj(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		object objToAdd = null;
		if (outVal == null)
		{
			outVal = new ComplexTypeData(null, null);
		}
		if (checkObjExist(val, outVal))
		{
			return findObjByPackId(val, outVal);
		}
		addObjToRefArr(outVal, objToAdd);
		if (checkObjCltn(desc))
		{
			return bytesToCltn(val, outVal, desc);
		}
		return bytesToObj_EXACT(val, outVal, desc);
	}

	public static DmArray bytesToArray(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		DmArray dmArray = new DmArray(null, desc);
		if (outVal == null)
		{
			outVal = new ComplexTypeData(null, null);
		}
		int offset = outVal.m_offset;
		dmArray.m_bufLen = DmConvertion.GetInt(val, offset);
		offset += 4;
		dmArray.m_itemCount = DmConvertion.GetInt(val, offset);
		offset += 4;
		dmArray.m_itemSize = DmConvertion.GetInt(val, offset);
		offset += 4;
		dmArray.m_objCount = DmConvertion.GetInt(val, offset);
		offset += 4;
		dmArray.m_strCount = DmConvertion.GetInt(val, offset);
		offset += 4;
		int num = dmArray.m_objCount + dmArray.m_strCount;
		dmArray.m_objStrOffs = new int[num];
		for (int i = 0; i < num; i++)
		{
			dmArray.m_objStrOffs[i] = DmConvertion.GetInt(val, offset);
			offset += 4;
		}
		outVal.m_offset = offset;
		dmArray.m_arrData = new ComplexTypeData[dmArray.m_itemCount];
		for (int j = 0; j < dmArray.m_itemCount; j++)
		{
			dmArray.m_arrData[j] = bytesToTypeData(val, outVal, desc.m_arrObj);
		}
		dmArray.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmArray;
	}

	public static DmArray bytesToSArray(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		if (outVal == null)
		{
			outVal = new ComplexTypeData(null, null);
		}
		int offset = outVal.m_offset;
		DmArray dmArray = new DmArray(null, desc);
		dmArray.m_bufLen = DmConvertion.GetInt(val, offset);
		offset += 4;
		dmArray.m_itemCount = DmConvertion.GetInt(val, offset);
		offset = (outVal.m_offset = offset + 4);
		dmArray.m_arrData = new ComplexTypeData[dmArray.m_itemCount];
		for (int i = 0; i < dmArray.m_itemCount; i++)
		{
			dmArray.m_arrData[i] = bytesToTypeData(val, outVal, desc.m_arrObj);
		}
		dmArray.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmArray;
	}

	public static DmStruct bytesToRecord(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		if (outVal == null)
		{
			outVal = new ComplexTypeData(null, null);
		}
		int offset = outVal.m_offset;
		DmStruct dmStruct = new DmStruct(null, desc);
		dmStruct.m_bufLen = DmConvertion.GetInt(val, offset);
		offset = (outVal.m_offset = offset + 4);
		dmStruct.m_attribs = new ComplexTypeData[desc.GetSize()];
		for (int i = 0; i < desc.GetSize(); i++)
		{
			dmStruct.m_attribs[i] = bytesToTypeData(val, outVal, desc.m_fieldsObj[i]);
		}
		dmStruct.m_dataBuf = DmConvertion.GetBytes(val, offset, outVal.m_offset - offset);
		return dmStruct;
	}

	private static void objBlob_GetChkBuf(byte[] buf, ComplexTypeData complexTypeData)
	{
		int num = 4;
		int num2 = DmConvertion.GetInt(buf, num);
		num += 4;
		complexTypeData.m_objBlobDescBuf = DmConvertion.GetBytes(buf, num, num2);
		num += num2;
		complexTypeData.m_isFromBlob = true;
		complexTypeData.m_offset = num;
	}

	internal static object objBlobToObj(DmBlob lob, ComplexTypeDesc desc)
	{
		ComplexTypeData complexTypeData = new ComplexTypeData(null, null);
		int len = (int)lob.do_length();
		byte[] bytes = lob.GetBytes(0L, len);
		objBlob_GetChkBuf(bytes, complexTypeData);
		return bytesToObj(bytes, complexTypeData, desc);
	}

	public static byte[] objBlobToBytes(byte[] lobBuf, ComplexTypeDesc desc)
	{
		int num = lobBuf.Length;
		int num2 = 0;
		int num3 = DmConvertion.GetInt(lobBuf, num2);
		num2 += 4;
		if (78111999 != num3)
		{
			throw new NotSupportedException("objBlobToBytes");
		}
		int num4 = DmConvertion.GetInt(lobBuf, num2);
		num2 += 4;
		byte[] bytes = DmConvertion.GetBytes(lobBuf, num2, num4);
		if (bytes.Length != desc.GetClassDescChkInfo().Length)
		{
			throw new NotSupportedException("objBlobToBytes");
		}
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] != desc.GetClassDescChkInfo()[i])
			{
				throw new NotSupportedException("objBlobToBytes");
			}
		}
		num2 += num4;
		byte[] array = new byte[num - num2];
		Array.Copy(lobBuf, num2, array, 0, array.Length);
		return array;
	}

	private static byte[] realocBuffer(byte[] oldBuf, int offset, int needLen)
	{
		if (oldBuf == null)
		{
			return new byte[needLen];
		}
		byte[] array;
		if (needLen + offset > oldBuf.Length)
		{
			array = new byte[oldBuf.Length + needLen];
			Array.Copy(oldBuf, 0, array, 0, offset);
		}
		else
		{
			array = oldBuf;
		}
		return array;
	}

	private static ComplexTypeData convertBytes2BaseData(byte[] val, ComplexTypeData outVal, ComplexTypeDesc desc)
	{
		int offset = outVal.m_offset;
		bool flag = false;
		int num = DmConvertion.GetShort(val, offset);
		offset += 2;
		if (num == 65534 || num == 65533)
		{
			num = 0;
			flag = true;
		}
		if (-1 == num)
		{
			num = DmConvertion.GetInt(val, offset);
			offset += 4;
		}
		if (flag)
		{
			outVal.m_offset = offset;
			return new ComplexTypeData(null, null);
		}
		byte[] bytes = DmConvertion.GetBytes(val, offset, num);
		offset += num;
		outVal.m_offset = offset;
		DmGetValue dmGetValue = new DmGetValue(desc.m_conn.GetConnInstance().ConnProperty.ServerEncoding, desc.m_conn.GetConnInstance().GetStmtFromPool((DmCommand)desc.m_conn.CreateCommand()), desc.m_conn.GetConnInstance().ConnProperty.NewLobFlag, new DmField[1] { desc.column });
		object val2 = dmGetValue.GetObject(0, bytes, desc.column.GetCType(), desc.column.GetPrecision(), desc.column.GetScale());
		dmGetValue.m_Statement.p();
		return new ComplexTypeData(val2, bytes);
	}

	public object toJavaArray(DmArray arr, int len, int dType)
	{
		return toJavaArray(arr, 1L, len, dType);
	}

	public object toJavaArray(DmArray arr, long index, int len, int dType)
	{
		throw new NotSupportedException("toJavaArray");
	}

	public object toNumericArray(DmArray arr, long index, int len, int flag)
	{
		throw new NotSupportedException("toNumericArray");
	}

	public object[] toJavaArray(DmStruct dmStruct)
	{
		ComplexTypeData[] attribsTypeData = dmStruct.getAttribsTypeData();
		if (dmStruct.getAttribsTypeData() == null || dmStruct.getAttribsTypeData().Length == 0)
		{
			return null;
		}
		ComplexTypeDesc[] fieldsObj = dmStruct.m_strctDesc.m_fieldsObj;
		if (attribsTypeData.Length != fieldsObj.Length)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_STRUCT_MEM_NOT_MATCH);
		}
		object[] array = new object[fieldsObj.Length];
		for (int i = 0; i < fieldsObj.Length; i++)
		{
			array[i] = attribsTypeData[i].m_dumyData;
		}
		return array;
	}

	public static byte[] toBytes(ComplexTypeData x, ComplexTypeDesc complexTypeDesc)
	{
		byte[] classDescChkInfo = complexTypeDesc.GetClassDescChkInfo();
		byte[] array = null;
		switch (complexTypeDesc.GetDType())
		{
		case 117:
			array = arrayToBytes((DmArray)x, complexTypeDesc);
			break;
		case 122:
			array = sarrayToBytes((DmArray)x, complexTypeDesc);
			break;
		case 121:
			array = recordToBytes((DmStruct)x, complexTypeDesc);
			break;
		case 119:
			array = objToBytes(x, complexTypeDesc);
			break;
		}
		byte[] array2 = new byte[8 + classDescChkInfo.Length + array.Length];
		DmConvertion.SetInt(array2, 0, 78111999);
		DmConvertion.SetInt(array2, 4, classDescChkInfo.Length);
		Array.Copy(classDescChkInfo, 0, array2, 8, classDescChkInfo.Length);
		Array.Copy(array, 0, array2, 8 + classDescChkInfo.Length, array.Length);
		return array2;
	}
}
