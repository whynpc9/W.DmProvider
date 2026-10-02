using System.Threading;
using System.Threading.Tasks;
using System;
using System.Text;
using System.Numerics;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.util;
using W.Dm.Internal.Types;
using W.Dm.Internal.Sessions;

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

	private DmDecimal ReadExactDecimal(byte[] value, int cType, int precision, int scale) => cType switch
	{
		9 => DmNumericCodec.DecodeDecimal(value, precision > 0 && scale >= 0 ? scale : null),
		24 => DmNumericCodec.DecodeScaledInt64(value, scale),
		_ => throw new InvalidCastException("Column is not a supported exact DECIMAL wire type.")
	};

	private object ReadIntegerSource(int i, byte[] value, int cType, int precision, int scale)
	{
		if (value == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		return cType switch
		{
			3 or 5 => DmConvertion.OneByteToSByte(value),
			6 => DmConvertion.TwoByteToShort(value),
			7 => DmConvertion.FourByteToInt(value),
			8 => DmConvertion.EightByteToLong(value),
			9 or 24 => ReadExactDecimal(value, cType, precision, scale),
			10 => DmConvertion.GetSingle(value),
			11 => DmConvertion.GetDouble(value),
			0 or 1 or 2 or 19 or 54 => GetString(i, value, cType, precision, scale).Trim(),
			28 => new DmRowId(value).longValue(m_Statement.G().Conn),
			_ => throw new InvalidCastException("Column cannot be read as an integer.")
		};
	}

	private BigInteger ReadIntegerExact(int i, byte[] value, int cType, int precision, int scale,
		BigInteger minimum, BigInteger maximum) =>
		DmNumericInput.ToIntegerExact(ReadIntegerSource(i, value, cType, precision, scale), minimum, maximum);

	internal int GetInt(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((int)ReadIntegerExact(i, val, CType, prec, scale, int.MinValue, int.MaxValue));
	}

	internal byte GetByte(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((byte)ReadIntegerExact(i, val, CType, prec, scale, byte.MinValue, byte.MaxValue));
	}

	internal short GetShort(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((short)ReadIntegerExact(i, val, CType, prec, scale, short.MinValue, short.MaxValue));
	}

	internal long GetLong(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((long)ReadIntegerExact(i, val, CType, prec, scale, long.MinValue, long.MaxValue));
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
			result = float.Parse(ReadExactDecimal(val, CType, prec, scale).ToString(), DmConst.invariantCulture);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
		case 54:
			try
			{
				double parsed = double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture);
				CheckRangeSingle(parsed);
				result = (float)parsed;
			}
			catch (Exception)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			break;
		default:
			throw new InvalidCastException();
		}
		if (!float.IsFinite(result)) throw new OverflowException("Non-finite floating values are unsupported.");
		return result;
	}

	internal double GetDouble(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		double result = CType switch
		{
			11 => DmConvertion.GetDouble(val),
			10 => DmConvertion.GetSingle(val),
			3 or 5 => DmConvertion.OneByteToSByte(val),
			6 => DmConvertion.TwoByteToShort(val),
			7 => DmConvertion.FourByteToInt(val),
			8 => DmConvertion.EightByteToLong(val),
			9 or 24 => double.Parse(ReadExactDecimal(val, CType, prec, scale).ToString(), DmConst.invariantCulture),
			0 or 1 or 2 or 19 or 54 => double.Parse(GetString(i, val, CType, prec, scale).Trim(), DmConst.invariantCulture),
			_ => throw new InvalidCastException("Column cannot be read as double.")
		};
		if (!double.IsFinite(result)) throw new OverflowException("Non-finite floating values are unsupported.");
		return result;
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
			text = GetFloat(i, val, CType, prec, scale).ToString("R", DmConst.invariantCulture);
			return ReplaceNumPoint(text);
		case 11:
			text = GetDouble(i, val, CType, prec, scale).ToString("R", DmConst.invariantCulture);
			return ReplaceNumPoint(text);
		case 9:
		case 24:
			text = ReadExactDecimal(val, CType, prec, scale).ToString();
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
			// Hex returns two UTF-16 chars per input byte; guard before fetching.
			DmBlob blob = new DmBlob(val, m_Statement.G(), m_ColInfo[i], false, hexPayload: true);
			int length = DmLobMaterialization.HexInput(blob.do_length());
			return Convert.ToHexString(blob.GetBytes(0L, length));
		}
		case 19:
			return new DmClob(val, m_Statement.G(), m_ColInfo[i], false).MaterializeStringUnderOwner();
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
		throw new NotSupportedException("XDEC is not supported; use GetProviderSpecificValue for exact DECIMAL values.");
	}

	internal decimal GetBigDecimal(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		return CType switch
		{
			9 or 24 => ReadExactDecimal(val, CType, prec, scale).ToDecimalExact(),
			3 or 5 => new decimal(DmConvertion.OneByteToSByte(val)),
			6 => new decimal(DmConvertion.TwoByteToShort(val)),
			7 => new decimal(DmConvertion.FourByteToInt(val)),
			8 => new decimal(DmConvertion.EightByteToLong(val)),
			0 or 1 or 2 or 19 or 54 => DmDecimal.Parse(GetString(i, val, CType, prec, scale).Trim()).ToDecimalExact(),
			_ => throw new InvalidCastException("Column cannot be read as an exact decimal.")
		};
	}

	internal sbyte GetSByte(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((sbyte)ReadIntegerExact(i, val, CType, prec, scale, sbyte.MinValue, sbyte.MaxValue));
	}

	internal ushort GetUshort(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((ushort)ReadIntegerExact(i, val, CType, prec, scale, ushort.MinValue, ushort.MaxValue));
	}

	internal uint GetUint(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((uint)ReadIntegerExact(i, val, CType, prec, scale, uint.MinValue, uint.MaxValue));
	}

	internal ulong GetUlong(int i, byte[] val, int CType, int prec, int scale)
	{
		return checked((ulong)ReadIntegerExact(i, val, CType, prec, scale, ulong.MinValue, ulong.MaxValue));
	}

	public DateTimeOffset GetTimeTZ(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		if (CType != 22) throw new InvalidCastException("Column has no TIME WITH TIME ZONE offset.");
		return new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec).GetTimeTZ();
	}

	public DateTimeOffset GetTimestampTZ(int i, byte[] val, int CType, int prec, int scale)
	{
		if (val == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		if (CType == 22) return GetTimeTZ(i, val, CType, prec, scale);
		if (CType is not (23 or 27))
			throw new InvalidCastException("Column has no TIMESTAMP WITH TIME ZONE offset.");
		return new DmDateTime(DmDateTime.DmTimeFromRec4(val, CType), prec).GetTimestampTZ();
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
			return iNTERVALDT.ToTimeSpanExact();
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


	internal async Task<object> GetObjectAsync(int i, byte[] value, int type, int precision, int scale, CancellationToken token)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		token.ThrowIfCancellationRequested();
		if (value == null) return DBNull.Value;
		if (type == 12) return await GetBytesAsync(i, value, type, precision, scale, token).ConfigureAwait(false);
		if (type == 19) return await GetStringAsync(i, value, type, precision, scale, token).ConfigureAwait(false);
		return GetObject(i, value, type, precision, scale);
	}

	internal async Task<byte[]> GetBytesAsync(int i, byte[] value, int type, int precision, int scale, CancellationToken token)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		token.ThrowIfCancellationRequested();
		if (value == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		if (type == 12)
		{
			if (value.Length < 13) DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			var blob = new DmBlob(value, m_Statement.G(), m_ColInfo[i], false);
			int length = DmLobMaterialization.Bytes(await blob.do_lengthAsync(token).ConfigureAwait(false));
			return await blob.do_getBytesAsync(1L, length, token).ConfigureAwait(false);
		}
		if (type == 19)
		{
			if (value.Length < 13) DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			return await new DmClob(value, m_Statement.G(), m_ColInfo[i], false).MaterializeBytesUnderOwnerAsync(token).ConfigureAwait(false);
		}
		return GetBytes(i, value, type, precision, scale);
	}

	internal async Task<string> GetStringAsync(int i, byte[] value, int type, int precision, int scale, CancellationToken token)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		token.ThrowIfCancellationRequested();
		if (value == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		if (type == 12)
		{
			var blob = new DmBlob(value, m_Statement.G(), m_ColInfo[i], false, hexPayload: true);
			int length = DmLobMaterialization.HexInput(await blob.do_lengthAsync(token).ConfigureAwait(false));
			return Convert.ToHexString(await blob.do_getBytesAsync(1L, length, token).ConfigureAwait(false));
		}
		if (type == 19)
			return await new DmClob(value, m_Statement.G(), m_ColInfo[i], false).MaterializeStringUnderOwnerAsync(token).ConfigureAwait(false);
		return GetString(i, value, type, precision, scale);
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
			return ReadExactDecimal(val, CType, prec, scale).ToDecimalExact();
		case 0:
		case 1:
			return GetString(i, val, CType, prec, scale);
		case 2:
		case 54:
			return GetString(i, val, CType, prec, scale);
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
			return GetTimestampTZ(i, val, CType, prec, scale);
		case 12:
			return GetBytes(i, val, CType, prec, scale);
		case 19:
			return new DmClob(val, m_Statement.G(), m_ColInfo[i], false).MaterializeStringUnderOwner();
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
		if (val == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		int num = val.Length;
		switch (CType)
		{
		case 3:
		case 17:
		case 18:
		case 54:
			return val;
		case 12:
		{
			if (num < 13) DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			DmBlob blob = new DmBlob(val, m_Statement.G(), m_ColInfo[i], false);
			return blob.GetBytes(0L, DmLobMaterialization.Bytes(blob.do_length()));
		}
		case 19:
		{
			if (num < 13) DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			return new DmClob(val, m_Statement.G(), m_ColInfo[i], false).MaterializeBytesUnderOwner();
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
