using System;
using System.Text;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.util;

namespace W.Dm;

internal class DmGetValue
{
	private string m_ServerEncoding;

	internal global::W.Dm.Internal.Legacy.A.A m_Statement;

	private DmField[] m_ColInfo;

	private bool m_NewLobFlag;

	internal DmConnProperty connProperty => m_Statement.G().ConnProperty;

	public DmGetValue(string servEncoding, global::W.Dm.Internal.Legacy.A.A stmt, bool newLobFlag, DmField[] ColInfo)
	{
		m_ServerEncoding = servEncoding;
		m_Statement = stmt;
		m_NewLobFlag = newLobFlag;
		m_ColInfo = ColInfo;
	}

	private void CheckRangeSByte(object tmp_object)
	{
		sbyte b2 = sbyte.MaxValue;
		sbyte b3 = sbyte.MinValue;
		if (tmp_object is int)
		{
			if ((int)tmp_object > b2 || (int)tmp_object < b3)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is long)
		{
			if ((long)tmp_object > b2 || (long)tmp_object < b3)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is float)
		{
			if ((float)tmp_object > (float)b2 || (float)tmp_object < (float)b3)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is double)
		{
			if ((double)tmp_object > (double)b2 || (double)tmp_object < (double)b3)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is decimal && ((decimal)tmp_object > (decimal)b2 || (decimal)tmp_object < (decimal)b3))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRangeInt16(object tmp_object)
	{
		short num = short.MaxValue;
		short num2 = short.MinValue;
		if (tmp_object is long)
		{
			if ((long)tmp_object > num || (long)tmp_object < num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is float)
		{
			if ((float)tmp_object > (float)num || (float)tmp_object < (float)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is double)
		{
			if ((double)tmp_object > (double)num || (double)tmp_object < (double)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is decimal && ((decimal)tmp_object > (decimal)num || (decimal)tmp_object < (decimal)num2))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRangeInt32(object tmp_object)
	{
		int num = int.MaxValue;
		int num2 = int.MinValue;
		if (tmp_object is long)
		{
			if ((long)tmp_object > num || (long)tmp_object < num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is float)
		{
			if ((float)tmp_object > (float)num || (float)tmp_object < (float)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is double)
		{
			if ((double)tmp_object > (double)num || (double)tmp_object < (double)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is decimal && ((decimal)tmp_object > (decimal)num || (decimal)tmp_object < (decimal)num2))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRangeInt64(object tmp_object)
	{
		long num = long.MaxValue;
		long num2 = long.MinValue;
		if (tmp_object is float)
		{
			if ((float)tmp_object > (float)num || (float)tmp_object < (float)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is double)
		{
			if ((double)tmp_object > (double)num || (double)tmp_object < (double)num2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is decimal && ((decimal)tmp_object > (decimal)num || (decimal)tmp_object < (decimal)num2))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRangeSingle(object tmp_object)
	{
		float num = float.MaxValue;
		float num2 = float.MinValue;
		if (tmp_object is double && ((double)tmp_object > (double)num || (double)tmp_object < (double)num2))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRangeDecimal(object tmp_object)
	{
		decimal d = decimal.MaxValue;
		decimal d2 = decimal.MinValue;
		if (tmp_object is double)
		{
			if ((double)tmp_object > decimal.ToDouble(d) || (double)tmp_object < decimal.ToDouble(d2))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
		}
		else if (tmp_object is float && ((float)tmp_object > decimal.ToSingle(d) || (float)tmp_object < decimal.ToSingle(d2)))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	private void CheckRange(object tmp_object, TypeCode typecode)
	{
		switch (typecode)
		{
		case TypeCode.SByte:
			CheckRangeSByte(tmp_object);
			break;
		case TypeCode.Int16:
			CheckRangeInt16(tmp_object);
			break;
		case TypeCode.Int32:
			CheckRangeInt32(tmp_object);
			break;
		case TypeCode.Int64:
			CheckRangeInt64(tmp_object);
			break;
		case TypeCode.Single:
			CheckRangeSingle(tmp_object);
			break;
		case TypeCode.Decimal:
			CheckRangeDecimal(tmp_object);
			break;
		case TypeCode.Byte:
		case TypeCode.UInt16:
		case TypeCode.UInt32:
		case TypeCode.UInt64:
		case TypeCode.Double:
			break;
		}
	}

	private void CheckNetDecimal(string decstring)
	{
		int num = ((decstring[0] != '-') ? decstring.Length : (decstring.Length - 1));
		if (decstring.IndexOf('.') != -1)
		{
			num--;
		}
		if (num > 29)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_DATA_OVERFLOW);
		}
	}

	internal int GetInt(int i, byte[] val, int CType, int prec, int scale)
	{
		int result = 0;
		object obj = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 7:
			result = DmConvertion.FourByteToInt(val);
			break;
		case 3:
		case 5:
			result = GetSByte(i, val, CType, prec, scale);
			break;
		case 6:
			result = GetShort(i, val, CType, prec, scale);
			break;
		case 8:
			obj = GetLong(i, val, CType, prec, scale);
			CheckRangeInt32((long)obj);
			result = Convert.ToInt32(obj);
			break;
		case 10:
			obj = GetFloat(i, val, CType, prec, scale);
			CheckRangeInt32((float)obj);
			result = Convert.ToInt32(obj);
			break;
		case 11:
			obj = GetDouble(i, val, CType, prec, scale);
			CheckRangeInt32((double)obj);
			result = Convert.ToInt32(obj);
			break;
		case 9:
		case 24:
			obj = GetBigDecimal(i, val, CType, prec, scale);
			CheckRangeInt32((decimal)obj);
			result = decimal.ToInt32((decimal)obj);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				obj = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeInt32(obj);
				result = Convert.ToInt32(obj);
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0;
			break;
		case 28:
			obj = new DmRowId(val).longValue(m_Statement.G().Conn);
			CheckRangeInt32((long)obj);
			result = Convert.ToInt32(obj);
			break;
		case 20:
		{
			DmIntervalYM dmIntervalYM = new DmIntervalYM(val);
			result = dmIntervalYM.getYear() * 12 + dmIntervalYM.getMonth();
			break;
		}
		case 21:
		{
			DmIntervalDT dmIntervalDT = new DmIntervalDT(val);
			result = (dmIntervalDT.getDay() * 86400 + dmIntervalDT.getHour() * 3600 + dmIntervalDT.getMinute() * 60 + dmIntervalDT.getSecond()) * 1000 + dmIntervalDT.getMsec() / 1000;
			break;
		}
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal byte GetByte(int i, byte[] val, int CType, int prec, int scale)
	{
		byte result = 0;
		object obj = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 3:
		case 5:
			if (DmConvertion.OneByteToSByte(val) < 0)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			else
			{
				result = val[0];
			}
			break;
		case 6:
			obj = GetShort(i, val, CType, prec, scale);
			CheckRangeSByte((short)obj);
			result = Convert.ToByte(obj);
			break;
		case 7:
			obj = GetInt(i, val, CType, prec, scale);
			CheckRangeSByte((int)obj);
			result = Convert.ToByte(obj);
			break;
		case 8:
			obj = GetLong(i, val, CType, prec, scale);
			CheckRangeSByte((long)obj);
			result = Convert.ToByte(obj);
			break;
		case 10:
			obj = GetFloat(i, val, CType, prec, scale);
			CheckRangeSByte((float)obj);
			result = Convert.ToByte(obj);
			break;
		case 11:
			obj = GetDate(i, val, CType, prec, scale);
			CheckRangeSByte((double)obj);
			result = Convert.ToByte(obj);
			break;
		case 9:
		case 24:
			obj = GetBigDecimal(i, val, CType, prec, scale);
			CheckRangeSByte((decimal)obj);
			result = decimal.ToByte((decimal)obj);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				obj = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeSByte(obj);
				result = Convert.ToByte(obj);
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0;
			break;
		case 28:
			obj = new DmRowId(val).longValue(m_Statement.G().Conn);
			CheckRangeSByte((long)obj);
			result = Convert.ToByte(obj);
			break;
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal short GetShort(int i, byte[] val, int CType, int prec, int scale)
	{
		short result = 0;
		object obj = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 6:
			result = DmConvertion.TwoByteToShort(val);
			break;
		case 3:
		case 5:
			result = GetSByte(i, val, CType, prec, scale);
			break;
		case 7:
			obj = GetInt(i, val, CType, prec, scale);
			CheckRangeInt16((int)obj);
			result = Convert.ToInt16(obj);
			break;
		case 8:
			obj = GetLong(i, val, CType, prec, scale);
			CheckRangeInt16((long)obj);
			result = Convert.ToInt16(obj);
			break;
		case 10:
			obj = GetFloat(i, val, CType, prec, scale);
			CheckRangeInt16((float)obj);
			result = Convert.ToInt16(obj);
			break;
		case 11:
			obj = GetDouble(i, val, CType, prec, scale);
			CheckRangeInt16((double)obj);
			result = Convert.ToInt16(obj);
			break;
		case 9:
		case 24:
			obj = GetBigDecimal(i, val, CType, prec, scale);
			CheckRangeInt16((decimal)obj);
			result = decimal.ToInt16((decimal)obj);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				obj = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeInt16(obj);
				result = Convert.ToInt16(obj);
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0;
			break;
		case 28:
			obj = new DmRowId(val).longValue(m_Statement.G().Conn);
			CheckRangeInt16((long)obj);
			result = Convert.ToInt16(obj);
			break;
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal long GetLong(int i, byte[] val, int CType, int prec, int scale)
	{
		long result = 0L;
		object obj = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 8:
			result = DmConvertion.EightByteToLong(val);
			break;
		case 3:
		case 5:
			result = GetSByte(i, val, CType, prec, scale);
			break;
		case 6:
			result = GetShort(i, val, CType, prec, scale);
			break;
		case 7:
			result = GetInt(i, val, CType, prec, scale);
			break;
		case 10:
			obj = GetFloat(i, val, CType, prec, scale);
			CheckRangeSingle((float)obj);
			result = Convert.ToInt64(obj);
			break;
		case 11:
			obj = GetDouble(i, val, CType, prec, scale);
			CheckRangeSingle((double)obj);
			result = Convert.ToInt64(obj);
			break;
		case 9:
		case 24:
			obj = GetBigDecimal(i, val, CType, prec, scale);
			CheckRangeDecimal((decimal)obj);
			result = Convert.ToInt64(obj);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				obj = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeInt64(obj);
				result = Convert.ToInt64(obj);
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0L;
			break;
		case 28:
			result = new DmRowId(val).longValue(m_Statement.G().Conn);
			break;
		case 20:
		{
			DmIntervalYM dmIntervalYM = new DmIntervalYM(val);
			result = dmIntervalYM.getYear() * 12 + dmIntervalYM.getMonth();
			break;
		}
		case 21:
		{
			DmIntervalDT dmIntervalDT = new DmIntervalDT(val);
			result = ((long)dmIntervalDT.getDay() * 86400L + (long)dmIntervalDT.getHour() * 3600L + (long)dmIntervalDT.getMinute() * 60L + dmIntervalDT.getSecond()) * 1000 + (long)dmIntervalDT.getMsec() / 1000L;
			break;
		}
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal float GetFloat(int i, byte[] val, int CType, int prec, int scale)
	{
		float result = 0f;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 10:
			result = DmConvertion.GetSingle(val);
			break;
		case 3:
		case 5:
			result = GetSByte(i, val, CType, prec, scale);
			break;
		case 6:
			result = GetShort(i, val, CType, prec, scale);
			break;
		case 7:
			result = GetInt(i, val, CType, prec, scale);
			break;
		case 8:
			result = GetLong(i, val, CType, prec, scale);
			break;
		case 11:
			result = (float)GetDouble(i, val, CType, prec, scale);
			break;
		case 9:
		case 24:
			result = ((prec <= 29) ? decimal.ToSingle(GetBigDecimal(i, val, CType, prec, scale)) : Convert.ToSingle(GetDmDecimal(i, val, CType, prec, scale).ToString()));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				CheckRangeSingle(double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture));
				result = (float)double.Parse(GetString(i, val, CType, prec, scale).Trim());
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0f;
			break;
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal double GetDouble(int i, byte[] val, int CType, int prec, int scale)
	{
		double num = 0.0;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 11:
			return DmConvertion.GetDouble(val);
		case 3:
		case 5:
			return GetSByte(i, val, CType, prec, scale);
		case 6:
			return GetShort(i, val, CType, prec, scale);
		case 7:
			return GetInt(i, val, CType, prec, scale);
		case 8:
			return GetLong(i, val, CType, prec, scale);
		case 10:
			return GetFloat(i, val, CType, prec, scale);
		case 0:
		case 1:
		case 2:
		case 9:
		case 19:
		case 24:
		case 54:
			return double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
		case 25:
			return 0.0;
		default:
			throw new InvalidCastException();
		}
	}

	internal string GetString(int i, byte[] val, int CType, int prec, int scale)
	{
		string text = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		int byteLen = ((val != null) ? val.Length : 0);
		switch (CType)
		{
		case 0:
		case 1:
			return DmConvertion.GetString(val, 0, byteLen, m_ServerEncoding);
		case 2:
		case 54:
			return DmConvertion.GetString(val, 0, byteLen, m_ServerEncoding);
		case 5:
			return GetSByte(i, val, CType, prec, scale).ToString();
		case 6:
			return GetShort(i, val, CType, prec, scale).ToString();
		case 7:
			return GetInt(i, val, CType, prec, scale).ToString();
		case 8:
			return GetLong(i, val, CType, prec, scale).ToString();
		case 10:
			text = GetFloat(i, val, CType, prec, scale).ToString();
			return ReplaceNumPoint(text);
		case 11:
			text = GetDouble(i, val, CType, prec, scale).ToString();
			return ReplaceNumPoint(text);
		case 9:
		case 24:
			text = ((prec <= 29) ? GetBigDecimal(i, val, CType, prec, scale).ToString() : GetDmDecimal(i, val, CType, prec, scale).ToString());
			return ReplaceNumPoint(text);
		case 3:
			return GetBoolean(i, val, CType, prec, scale).ToString();
		case 17:
		case 18:
			return DmConvertion.BytesToHexString(val);
		case 28:
			return new DmRowId(val).toString();
		case 12:
		{
			DmBlob dmBlob = new DmBlob(val, m_Statement.G(), m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
			if (dmBlob.do_length() < int.MaxValue)
			{
				return DmConvertion.BytesToHexString(val);
			}
			return DmConvertion.BytesToHexString(dmBlob.GetBytes(0L, int.MaxValue));
		}
		case 19:
		{
			DmClob dmClob = new DmClob(val, m_Statement.G(), m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
			if (dmClob.do_length() < int.MaxValue)
			{
				return dmClob.getSubString(0L, (int)dmClob.do_length());
			}
			return dmClob.getSubString(0L, int.MaxValue);
		}
		case 14:
		case 15:
		case 16:
		case 22:
		case 23:
		case 26:
		case 27:
			return DmDateTime.valueOf(val, m_ColInfo[i], m_Statement.G().Conn).toString(m_ColInfo[i], m_Statement.G().Conn);
		case 21:
			return GetINTERVALDT(i, val, CType, prec, scale)?.GetDTString();
		case 20:
			return GetINTERVALYM(i, val, CType, prec, scale)?.GetYMString();
		case 25:
			return "";
		default:
			throw new InvalidCastException();
		}
	}

	internal string ReplaceNumPoint(string x)
	{
		if (connProperty.formatNumericChars == null)
		{
			return x;
		}
		return x.Replace('.', connProperty.formatNumericChars[0]);
	}

	internal bool GetBoolean(int i, byte[] val, int CType, int prec, int scale)
	{
		bool flag = false;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 3:
		case 5:
			if (GetSByte(i, val, CType, prec, scale) == 0)
			{
				return false;
			}
			return true;
		case 6:
			if (GetShort(i, val, CType, prec, scale) == 0)
			{
				return false;
			}
			return true;
		case 7:
			if (GetInt(i, val, CType, prec, scale) == 0)
			{
				return false;
			}
			return true;
		case 8:
			if (GetLong(i, val, CType, prec, scale) == 0L)
			{
				return false;
			}
			return true;
		case 10:
			if ((double)GetFloat(i, val, CType, prec, scale) == 0.0)
			{
				return false;
			}
			return true;
		case 11:
			if (GetDouble(i, val, CType, prec, scale) == 0.0)
			{
				return false;
			}
			return true;
		case 9:
		case 24:
			if ((byte)GetBigDecimal(i, val, CType, prec, scale) == 0)
			{
				return false;
			}
			return true;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
		{
			string s = GetString(i, val, CType, prec, scale);
			if (Encoding.Default.GetBytes(s)[0] == 48)
			{
				return false;
			}
			return true;
		}
		case 25:
			return false;
		default:
			throw new InvalidCastException();
		}
	}

	internal DmXDec GetDmDecimal(int i, byte[] val, int CType, int prec, int scale)
	{
		_ = val?.Length;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 9:
			return new DmXDec(val);
		case 24:
		{
			string text = DmConvertion.EightByteToLong(val).ToString() ?? "";
			int length = text.Length;
			if (length > scale)
			{
				string text2 = text.Substring(0, length - scale);
				string text3 = text.Substring(length - scale);
				text = text2 + "." + text3;
			}
			else
			{
				for (int j = 0; j < scale - length; j++)
				{
					text = "0" + text;
				}
				text = "0." + text;
			}
			return new DmXDec().Parse(text);
		}
		case 3:
		case 5:
			return new DmXDec().Parse(GetSByte(i, val, CType, prec, scale).ToString());
		case 6:
			return new DmXDec().Parse(GetShort(i, val, CType, prec, scale).ToString());
		case 7:
			return new DmXDec().Parse(GetInt(i, val, CType, prec, scale).ToString());
		case 8:
			return new DmXDec().Parse(GetLong(i, val, CType, prec, scale).ToString());
		case 10:
			return new DmXDec().Parse(GetFloat(i, val, CType, prec, scale).ToString());
		case 11:
			return new DmXDec().Parse(GetDouble(i, val, CType, prec, scale).ToString());
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			return new DmXDec().Parse(GetString(i, val, CType, prec, scale).Trim());
		case 25:
			return new DmXDec().Parse("0.0");
		default:
			throw new InvalidCastException();
		}
	}

	internal decimal GetBigDecimal(int i, byte[] val, int CType, int prec, int scale)
	{
		decimal num = default(decimal);
		string text = "";
		_ = val?.Length;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 9:
			text = new DmXDec().decToString(val);
			num = decimal.Parse(text, DmConst.invariantCulture);
			break;
		case 24:
		{
			text = DmConvertion.EightByteToLong(val).ToString() ?? "";
			int length = text.Length;
			if (length > scale)
			{
				string text2 = text.Substring(0, length - scale);
				string text3 = text.Substring(length - scale);
				text = text2 + "." + text3;
			}
			else
			{
				for (int j = 0; j < scale - length; j++)
				{
					text = "0" + text;
				}
				text = "0." + text;
			}
			num = decimal.Parse(text, DmConst.invariantCulture);
			break;
		}
		case 3:
		case 5:
			num = new decimal(GetSByte(i, val, CType, prec, scale));
			break;
		case 6:
			num = new decimal(GetShort(i, val, CType, prec, scale));
			break;
		case 7:
			num = new decimal(GetInt(i, val, CType, prec, scale));
			break;
		case 8:
			num = new decimal(GetLong(i, val, CType, prec, scale));
			break;
		case 10:
			num = new decimal(GetFloat(i, val, CType, prec, scale));
			break;
		case 11:
			num = new decimal(GetDouble(i, val, CType, prec, scale));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			text = GetString(i, val, CType, prec, scale).Trim();
			num = decimal.Parse(text, DmConst.invariantCulture);
			break;
		case 25:
			num = default(decimal);
			break;
		default:
			throw new InvalidCastException();
		}
		if (scale > 0 && connProperty.CompatibleMode == CompatibleMode.SQLSERVER)
		{
			num = num.setScale(scale, MidpointRounding.AwayFromZero);
		}
		return num;
	}

	internal sbyte GetSByte(int i, byte[] val, int CType, int prec, int scale)
	{
		sbyte result = 0;
		object obj = null;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 3:
		case 5:
			result = DmConvertion.OneByteToSByte(val);
			break;
		case 6:
			obj = GetShort(i, val, CType, prec, scale);
			CheckRangeSByte((short)obj);
			result = Convert.ToSByte(obj);
			break;
		case 7:
			obj = GetInt(i, val, CType, prec, scale);
			CheckRangeSByte((int)obj);
			result = Convert.ToSByte(obj);
			break;
		case 8:
			obj = GetLong(i, val, CType, prec, scale);
			CheckRangeSByte((long)obj);
			result = Convert.ToSByte(obj);
			break;
		case 10:
			obj = GetFloat(i, val, CType, prec, scale);
			CheckRangeSByte((float)obj);
			result = Convert.ToSByte(obj);
			break;
		case 11:
			obj = GetDate(i, val, CType, prec, scale);
			CheckRangeSByte((double)obj);
			result = Convert.ToSByte(obj);
			break;
		case 9:
		case 24:
			obj = GetBigDecimal(i, val, CType, prec, scale);
			CheckRangeSByte((decimal)obj);
			result = decimal.ToSByte((decimal)obj);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				obj = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeSByte(obj);
				result = Convert.ToSByte(obj);
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		case 25:
			result = 0;
			break;
		default:
			throw new InvalidCastException();
		}
		return result;
	}

	internal ushort GetUshort(int i, byte[] val, int CType, int prec, int scale)
	{
		short num = GetShort(i, val, CType, prec, scale);
		if (num < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return (ushort)num;
	}

	internal uint GetUint(int i, byte[] val, int CType, int prec, int scale)
	{
		int num = GetInt(i, val, CType, prec, scale);
		if (num < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return (uint)num;
	}

	internal ulong GetUlong(int i, byte[] val, int CType, int prec, int scale)
	{
		long num = GetLong(i, val, CType, prec, scale);
		if (num < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
		return (ulong)num;
	}

	public DateTimeOffset GetTimeTZ(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		DmDateTime dmDateTime = new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec);
		switch (CType)
		{
		case 15:
			return new DateTimeOffset(dmDateTime.GetTime());
		case 22:
		case 23:
			return dmDateTime.GetTimeTZ();
		case 16:
			return new DateTimeOffset(dmDateTime.GetTimestamp());
		case 0:
		case 1:
		case 2:
		case 19:
		case 25:
		case 54:
			return new DateTimeOffset(DateTime.MinValue);
		default:
			throw new InvalidCastException();
		}
	}

	public DateTimeOffset GetTimestampTZ(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		DmDateTime dmDateTime = new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec);
		switch (CType)
		{
		case 16:
		case 26:
		{
			DateTime dateTime = ((!DmDateTime.NTYPE_IS_LOCAL_TIME_ZONE(CType, scale)) ? dmDateTime.GetTimestamp() : DmDateTime.DmtimeAddByFmt(prec, dmDateTime, 5, m_Statement.G().ConnProperty.TimeZone - m_Statement.G().ConnProperty.DbTimeZone));
			return new DateTimeOffset(dateTime);
		}
		case 23:
		case 27:
			return dmDateTime.GetTimestampTZ();
		case 14:
			return new DateTimeOffset(dmDateTime.GetDate());
		case 15:
			return new DateTimeOffset(dmDateTime.GetTime());
		case 22:
			return dmDateTime.GetTimeTZ();
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			return new DateTimeOffset(DmDateTime.GetTimestampByString(GetString(i, val, CType, prec, scale).Trim()));
		case 25:
			return new DateTimeOffset(DateTime.MinValue);
		default:
			throw new InvalidCastException();
		}
	}

	public DateTime GetTimestamp(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		DmDateTime dmDateTime = new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec);
		switch (CType)
		{
		case 16:
		case 26:
			if (DmDateTime.NTYPE_IS_LOCAL_TIME_ZONE(CType, scale))
			{
				return DmDateTime.DmtimeAddByFmt(prec, dmDateTime, 5, m_Statement.G().ConnProperty.TimeZone - m_Statement.G().ConnProperty.DbTimeZone);
			}
			return dmDateTime.GetTimestamp();
		case 23:
		case 27:
			return dmDateTime.GetTimestampTZ().DateTime;
		case 14:
			return dmDateTime.GetDate();
		case 15:
		case 22:
			return dmDateTime.GetTime();
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			return DmDateTime.GetTimestampByString(GetString(i, val, CType, prec, scale).Trim());
		case 25:
			return DateTime.MinValue;
		default:
			throw new InvalidCastException();
		}
	}

	public DateTime GetDate(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		DmDateTime dmDateTime = new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec);
		switch (CType)
		{
		case 14:
		case 16:
		case 23:
			return dmDateTime.GetDate();
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			return DmDateTime.GetDateByString(GetString(i, val, CType, prec, scale).Trim());
		case 25:
			return DateTime.MinValue;
		default:
			throw new InvalidCastException();
		}
	}

	public DateTime GetTime(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		DmDateTime dmDateTime = new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec);
		switch (CType)
		{
		case 15:
		case 16:
		case 22:
		case 23:
			return dmDateTime.GetTime();
		case 0:
		case 1:
		case 2:
		case 19:
		case 25:
		case 54:
			return DateTime.MinValue;
		default:
			throw new InvalidCastException();
		}
	}

	public DmIntervalDT GetINTERVALDT(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		int secScale = scale & 0xF;
		int leadScale = (scale >> 4) & 0xF;
		byte type = (byte)((scale >> 8) & 0xF);
		return CType switch
		{
			21 => new DmIntervalDT(val, leadScale, secScale), 
			25 => null, 
			_ => new DmIntervalDT(GetString(i, val, CType, prec, scale), type, leadScale, secScale), 
		};
	}

	public DmIntervalYM GetINTERVALYM(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		int leadScale = (scale >> 4) & 0xF;
		return CType switch
		{
			20 => new DmIntervalYM(val, leadScale), 
			25 => null, 
			_ => new DmIntervalYM(GetString(i, val, CType, prec, scale), scale), 
		};
	}

	internal object GetIntervalDtByMode(int i, byte[] val, int CType, int prec, int scale)
	{
		DmIntervalDT iNTERVALDT = GetINTERVALDT(i, val, CType, prec, scale);
		if (connProperty.IntervalMode == IntervalMode.DT || connProperty.IntervalMode == IntervalMode.ALL || scale == 1574)
		{
			return TimeSpan.ParseExact(iNTERVALDT.GetTimeSpanFormatString(), "c", null);
		}
		return iNTERVALDT;
	}

	internal object GetIntervalYmByMode(int i, byte[] val, int CType, int prec, int scale)
	{
		DmIntervalYM iNTERVALYM = GetINTERVALYM(i, val, CType, prec, scale);
		long num = (long)iNTERVALYM.getYear() * 12L + iNTERVALYM.getMonth();
		IntervalMode intervalMode = connProperty.IntervalMode;
		if (intervalMode == IntervalMode.YM || intervalMode == IntervalMode.ALL)
		{
			return num;
		}
		return iNTERVALYM;
	}

	internal object GetObject(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null)
		{
			return DBNull.Value;
		}
		switch (CType)
		{
		case 3:
		{
			bool boolean = GetBoolean(i, val, CType, prec, scale);
			if (connProperty.CompatibleMode == CompatibleMode.MYSQL)
			{
				return boolean ? 1 : 0;
			}
			return boolean;
		}
		case 5:
			return GetSByte(i, val, CType, prec, scale);
		case 6:
			return GetShort(i, val, CType, prec, scale);
		case 7:
			return GetInt(i, val, CType, prec, scale);
		case 8:
			return GetLong(i, val, CType, prec, scale);
		case 10:
			return GetFloat(i, val, CType, prec, scale);
		case 11:
			return GetDouble(i, val, CType, prec, scale);
		case 9:
		case 24:
			if (prec > 29)
			{
				decimal num = decimal.Parse(GetDmDecimal(i, val, CType, prec, scale).ToString(), DmConst.invariantCulture);
				if (scale > 0 && connProperty.CompatibleMode == CompatibleMode.SQLSERVER)
				{
					num = num.setScale(scale, MidpointRounding.AwayFromZero);
				}
				return num;
			}
			return GetBigDecimal(i, val, CType, prec, scale);
		case 0:
		case 1:
		{
			string text2 = GetString(i, val, CType, prec, scale);
			if (prec == 36)
			{
				try
				{
					return new Guid(text2);
				}
				catch (Exception)
				{
					return text2;
				}
			}
			return text2;
		}
		case 2:
		case 54:
		{
			string text = GetString(i, val, CType, prec, scale);
			if (connProperty.Varchar36ToGuid && prec == 36)
			{
				try
				{
					return new Guid(text);
				}
				catch (Exception)
				{
					return text;
				}
			}
			return text;
		}
		case 14:
			return GetDate(i, val, CType, prec, scale);
		case 15:
			if (m_Statement.G().ConnProperty.DbTimeToTimeSpan)
			{
				return TimeSpan.ParseExact(new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec).GetTimeInString(), "c", null);
			}
			return GetTime(i, val, CType, prec, scale);
		case 22:
			return GetTimeTZ(i, val, CType, prec, scale);
		case 16:
		case 26:
			return GetTimestamp(i, val, CType, prec, scale);
		case 23:
		case 27:
			if (connProperty.ConvertToTz)
			{
				return GetTimestampTZ(i, val, CType, prec, scale);
			}
			return GetTimestamp(i, val, CType, prec, scale);
		case 12:
			return GetBytes(i, val, CType, prec, scale);
		case 19:
			return new DmClob(val, m_Statement.G(), m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2).getSubString(0L, int.MaxValue);
		case 17:
		case 18:
			return GetBytes(i, val, CType, prec, scale);
		case 21:
			return GetIntervalDtByMode(i, val, CType, prec, scale);
		case 20:
			return GetIntervalYmByMode(i, val, CType, prec, scale);
		case 25:
			return null;
		case 28:
			return new DmRowId(val).toString();
		case 31:
		case 32:
		case 33:
		case 40:
		case 41:
		case 42:
			throw new InvalidCastException("unsupported data type");
		default:
			return GetBytes(i, val, CType, prec, scale);
		}
	}

	internal byte[] GetBytes(int i, byte[] val, int CType, int prec, int scale)
	{
		int num = val.Length;
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		switch (CType)
		{
		case 3:
		case 17:
		case 18:
		case 54:
			return val;
		case 12:
		{
			if (num < 13)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			}
			if (IsRealData(val))
			{
				byte[] array = new byte[8];
				Array.Copy(val, 9, array, 0, 4);
				num = Math.Min(num - 13, DmConvertion.FourByteToInt(array));
				byte[] array2 = new byte[num];
				int num2 = 0;
				num2 = ((!m_NewLobFlag) ? 13 : 43);
				if (m_Statement.G().ConnProperty.msgVersion >= 9)
				{
					num2 += 4;
				}
				Array.Copy(val, num2, array2, 0, num);
				return array2;
			}
			DmBlob dmBlob = new DmBlob(val, m_Statement.G(), m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
			int len = (int)dmBlob.do_length();
			return dmBlob.GetBytes(0L, len);
		}
		case 19:
		{
			if (num < 13)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			}
			DmClob dmClob = new DmClob(val, m_Statement.G(), m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2);
			return dmClob.GetBytes(0L, (int)dmClob.do_length());
		}
		case 25:
			return null;
		default:
			throw new InvalidCastException();
		}
	}

	private bool IsRealData(byte[] bs)
	{
		bool result = false;
		byte b2 = bs[0];
		if ((b2 & 1) == 1)
		{
			result = true;
		}
		else if ((b2 & 2) == 2)
		{
			result = false;
		}
		else
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_RETURN_VALUE);
		}
		return result;
	}

	internal object ToComplexType(byte[] bytes, DmField column, DmConnection connection)
	{
		object obj = null;
		switch (column.GetCType())
		{
		case 12:
			throw new NotSupportedException("toComplexType");
		case 117:
			obj = ComplexTypeData.bytesToArray(bytes, null, column.ComplexTypeDesc);
			break;
		case 122:
			obj = ComplexTypeData.bytesToSArray(bytes, null, column.ComplexTypeDesc);
			break;
		case 119:
			obj = ComplexTypeData.bytesToObj(bytes, null, column.ComplexTypeDesc);
			if (obj is DmStruct)
			{
				throw new NotSupportedException("toComplexType");
			}
			break;
		case 121:
			obj = ComplexTypeData.bytesToRecord(bytes, null, column.ComplexTypeDesc);
			throw new NotSupportedException("toComplexType");
		default:
			throw new InvalidCastException("toComplexType");
		}
		return obj;
	}
}
