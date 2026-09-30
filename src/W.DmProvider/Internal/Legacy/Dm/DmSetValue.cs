using System;
using System.Globalization;
using System.Numerics;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Types;
using W.Dm.util;

namespace W.Dm;

internal class DmSetValue
{
	private string m_ServerEncoding;

	private global::W.Dm.Internal.Legacy.A.A m_Statement;

	internal DmConnProperty ConnProperty => m_Statement.G().ConnProperty;

	public DmSetValue(string servEncoding)
	{
		m_ServerEncoding = servEncoding;
	}

	public DmSetValue(string servEncoding, global::W.Dm.Internal.Legacy.A.A stmt)
	{
		m_ServerEncoding = servEncoding;
		m_Statement = stmt;
	}

	public void ChangeSetValue(string servEncoding, global::W.Dm.Internal.Legacy.A.A stmt)
	{
		m_ServerEncoding = servEncoding;
		m_Statement = stmt;
	}

	public void SetNull(DmParamValue paraVal)
	{
		paraVal.SetInNull();
	}

	private void SetBoolean(DmParamValue paraVal, bool x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		int num = (x ? 1 : 0);
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.ByteToByteArray((byte)num));
			paraVal.SetSqlType(5);
			paraVal.SetPrec(1);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 3:
			paraVal.SetInValue(DmConvertion.ByteToByteArray((byte)num));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, num.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 5:
			SetByte(paraVal, (sbyte)num, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			SetShort(paraVal, (short)num, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, num, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, num, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(num), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, num, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, num, cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetByte(DmParamValue paraVal, sbyte x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.ByteToByteArray((byte)x));
			paraVal.SetSqlType(5);
			paraVal.SetPrec(1);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 3:
		{
			byte b2 = 0;
			if (x != 0)
			{
				b2 = 1;
			}
			paraVal.SetInValue(DmConvertion.ByteToByteArray(b2));
			break;
		}
		case 5:
			paraVal.SetInValue(DmConvertion.ByteToByteArray((byte)x));
			break;
		case 6:
			SetShort(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetShort(DmParamValue paraVal, short x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.ShortToByteArray(x));
			paraVal.SetSqlType(6);
			paraVal.SetPrec(2);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 6:
			paraVal.SetInValue(DmConvertion.ShortToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127 || x < -128)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetInt(DmParamValue paraVal, int x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.IntToByteArray(x));
			paraVal.SetSqlType(7);
			paraVal.SetPrec(4);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 7:
			paraVal.SetInValue(DmConvertion.IntToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127 || x < -128)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767 || x < -32768)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetLong(DmParamValue paraVal, long x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1 && cType != 20 && cType != 21)
		{
			if (x <= int.MaxValue && x >= int.MinValue && cType == 7)
			{
				SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
				return;
			}
			paraVal.SetInValue(DmConvertion.LongToByteArray(x));
			paraVal.SetSqlType(8);
			paraVal.SetPrec(8);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 8:
			paraVal.SetInValue(DmConvertion.LongToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127 || x < -128)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767 || x < -32768)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			if (x > int.MaxValue || x < int.MinValue)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		case 20:
		{
			int leadScale = (scale >> 4) & 0xF;
			SetINTERVALYM(paraVal, new DmIntervalYM(x, leadScale), cType, prec, scale, typeFlag, paraInternal);
			break;
		}
		default:
			throw new InvalidCastException();
		}
	}

	private void SetUShort(DmParamValue paraVal, ushort x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.ShortToByteArray(x));
			paraVal.SetSqlType(6);
			paraVal.SetPrec(2);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 6:
			if (x > 32767)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			paraVal.SetInValue(DmConvertion.ShortToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetUByte(DmParamValue paraVal, byte x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.ByteToByteArray(x));
			paraVal.SetSqlType(5);
			paraVal.SetPrec(1);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 3:
		{
			byte b2 = 0;
			if (x != 0)
			{
				b2 = 1;
			}
			paraVal.SetInValue(DmConvertion.ByteToByteArray(b2));
			break;
		}
		case 5:
			if (x > 127)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			paraVal.SetInValue(DmConvertion.ByteToByteArray(x));
			break;
		case 6:
			SetShort(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetUInt(DmParamValue paraVal, uint x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.IntToByteArray(x));
			paraVal.SetSqlType(7);
			paraVal.SetPrec(4);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 7:
			if (x > int.MaxValue)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			paraVal.SetInValue(DmConvertion.IntToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetULong(DmParamValue paraVal, ulong x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			if (x <= uint.MaxValue && cType == 7)
			{
				SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
				return;
			}
			paraVal.SetInValue(DmConvertion.LongToByteArray((long)x));
			paraVal.SetSqlType(8);
			paraVal.SetPrec(8);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 8:
			if (x > long.MaxValue)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			paraVal.SetInValue(DmConvertion.LongToByteArray((long)x));
			break;
		case 3:
		case 5:
			if (x > 127)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			if (x > int.MaxValue)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetBigDecimal(paraVal, new decimal(x), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf((long)x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetFloat(DmParamValue paraVal, float x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.FloatToByteArray(x));
			paraVal.SetSqlType(10);
			paraVal.SetPrec(0);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 10:
			paraVal.SetInValue(DmConvertion.FloatToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127f || x < -128f)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767f || x < -32768f)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			if (x > 2.1474836E+09f || x < -2.1474836E+09f)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			if (x > 9.223372E+18f || x < -9.223372E+18f)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetLong(paraVal, (long)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetDmDecimal(paraVal, new DmXDec().Parse(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetDouble(DmParamValue paraVal, double x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmConvertion.DoubleToByteArray(x));
			paraVal.SetSqlType(11);
			paraVal.SetPrec(0);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 11:
			paraVal.SetInValue(DmConvertion.DoubleToByteArray(x));
			break;
		case 3:
		case 5:
			if (x > 127.0 || x < -128.0)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetByte(paraVal, (sbyte)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			if (x > 32767.0 || x < -32768.0)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetShort(paraVal, (short)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			if (x > 2147483647.0 || x < -2147483648.0)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetInt(paraVal, (int)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			if (x > 9.223372036854776E+18 || x < -9.223372036854776E+18)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetLong(paraVal, (long)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			if (x > 3.4028234663852886E+38 || x < -3.4028234663852886E+38)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_OVER_FLOW);
			}
			SetFloat(paraVal, (float)x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 9:
		case 24:
			SetDmDecimal(paraVal, new DmXDec().Parse(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetDmDecimal(DmParamValue paraVal, DmXDec x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (x == null)
		{
			SetNull(paraVal);
			return;
		}
		if (typeFlag != 1)
		{
			paraVal.SetSqlType(cType);
			paraVal.SetPrec(prec);
			paraVal.SetScale(scale);
		}
		switch (cType)
		{
		case 9:
		{
			string str = x.ToString();
			byte[] inValue = new DmXDec().StrToDec(str, prec, scale, dmxdec_direct: true);
			paraVal.SetInValue(inValue);
			break;
		}
		case 3:
		case 5:
			SetByte(paraVal, Convert.ToSByte(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			SetShort(paraVal, Convert.ToInt16(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, Convert.ToInt32(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, Convert.ToInt64(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, Convert.ToSingle(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, Convert.ToDouble(x.ToString()), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetBigDecimal(DmParamValue paraVal, decimal? x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (!x.HasValue)
		{
			SetNull(paraVal);
			return;
		}
		if (typeFlag != 1)
		{
			paraVal.SetSqlType(cType);
			paraVal.SetPrec(prec);
			paraVal.SetScale(scale);
		}
		switch (cType)
		{
		case 9:
			new DmXDec().StrToDec(ref paraVal.m_InValue, x.ToString(), prec, scale, dmxdec_direct: false);
			paraVal.SetInValue();
			break;
		case 3:
		case 5:
			SetByte(paraVal, decimal.ToSByte(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 6:
			SetShort(paraVal, decimal.ToInt16(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 7:
			SetInt(paraVal, decimal.ToInt32(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 8:
			SetLong(paraVal, decimal.ToInt64(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 10:
			SetFloat(paraVal, decimal.ToSingle(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 11:
			SetDouble(paraVal, decimal.ToDouble(x.Value), cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetString(DmParamValue paraVal, string x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		byte[] array = null;
		if (x == null)
		{
			SetNull(paraVal);
			return;
		}
		if (typeFlag != 1)
		{
			array = DmConvertion.GetBytes(x, m_ServerEncoding);
			paraVal.SetInValue(array);
			if (array != null && array.Length < 32767)
			{
				paraVal.SetSqlType(2);
				paraVal.SetPrec(8188);
				paraVal.SetScale(6);
			}
			else
			{
				paraVal.SetSqlType(19);
				paraVal.SetPrec(int.MaxValue);
				paraVal.SetScale(0);
			}
			paraInternal.maxValueLen = Math.Max(array.Length, paraInternal.maxValueLen);
			return;
		}
		switch (cType)
		{
		case 0:
		case 1:
		case 2:
		{
			paraVal.m_InValue = DmConvertion.GetBytes(x, m_ServerEncoding);
			int num = paraVal.m_InValue.Length;
			if (num > 32767)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_STR_CUT);
			}
			paraVal.SetInValue();
			paraInternal.maxValueLen = Math.Max(num, paraInternal.maxValueLen);
			break;
		}
		case 19:
		{
			paraVal.m_InValue = DmConvertion.GetBytes(x, m_ServerEncoding);
			int num = paraVal.m_InValue.Length;
			if (prec < num && prec != 0)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_STR_CUT);
			}
			paraVal.SetInValue();
			break;
		}
		case 3:
		{
			byte b2 = 0;
			string text = x.Trim().ToUpper();
			if (text.ToUpper().Equals("FALSE") || text.Equals("0"))
			{
				b2 = 0;
			}
			else if (text.ToUpper().Equals("TRUE") || text.Equals("1"))
			{
				b2 = 1;
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
			}
			SetByte(paraVal, (sbyte)b2, cType, prec, scale, typeFlag, paraInternal);
			break;
		}
		case 5:
			if (x.Trim().Length > 0)
			{
				SetByte(paraVal, sbyte.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 6:
			if (x.Trim().Length > 0)
			{
				SetShort(paraVal, short.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 7:
			if (x.Trim().Length > 0)
			{
				SetInt(paraVal, int.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 8:
			if (x.Trim().Length > 0)
			{
				SetLong(paraVal, long.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 10:
			if (x.Trim().Length > 0)
			{
				x = ReplaceNumPoint(x);
				SetFloat(paraVal, float.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 11:
			if (x.Trim().Length > 0)
			{
				x = ReplaceNumPoint(x);
				SetDouble(paraVal, double.Parse(x, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 9:
		case 24:
			if (x.Trim().Length > 19)
			{
				x = ReplaceNumPoint(x);
				SetDmDecimal(paraVal, new DmXDec().Parse(x), cType, prec, scale, typeFlag, paraInternal);
			}
			else if (x.Trim().Length > 0)
			{
				x = ReplaceNumPoint(x);
				SetBigDecimal(paraVal, decimal.Parse(x, NumberStyles.Any, DmConst.invariantCulture), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 17:
			throw new NotSupportedException("Implicit text-to-binary parameter conversion is not supported.");
		case 12:
		case 18:
			throw new NotSupportedException("Implicit text-to-binary parameter conversion is not supported.");
		case 14:
		{
			if (typeFlag != 1)
			{
				paraVal.SetSqlType(14);
				paraVal.SetPrec(3);
				paraVal.SetScale(6);
				paraInternal.scale = 6;
			}
			DmDateTime dmDateTime = DmDateTime.valueOf(x, paraInternal, m_Statement.G().Conn);
			paraVal.SetInValue(dmDateTime.encode(paraInternal, m_Statement.G().Conn));
			break;
		}
		case 15:
		{
			if (typeFlag != 1)
			{
				paraVal.SetSqlType(15);
				paraVal.SetPrec(5);
				paraVal.SetScale(6);
				paraInternal.scale = 6;
			}
			DmDateTime dmDateTime = DmDateTime.valueOf(x, paraInternal, m_Statement.G().Conn);
			paraVal.SetInValue(dmDateTime.encode(paraInternal, m_Statement.G().Conn));
			break;
		}
		case 22:
		{
			DmDateTime dmDateTime = DmDateTime.valueOf(x, paraInternal, m_Statement.G().Conn);
			paraVal.SetInValue(dmDateTime.encode(paraInternal, m_Statement.G().Conn));
			break;
		}
		case 16:
		{
			DmDateTime dmDateTime = DmDateTime.valueOf(x, paraInternal, m_Statement.G().Conn);
			if (typeFlag != 1)
			{
				paraVal.SetSqlType(16);
				paraVal.SetPrec(8);
				paraVal.SetScale(6);
				paraInternal.scale = 6;
			}
			paraVal.SetInValue(dmDateTime.encode(paraInternal, m_Statement.G().Conn));
			break;
		}
		case 23:
		{
			DmDateTime dmDateTime = DmDateTime.valueOf(x, paraInternal, m_Statement.G().Conn);
			paraVal.SetInValue(dmDateTime.encode(paraInternal, m_Statement.G().Conn));
			break;
		}
		case 21:
		{
			int secScale = scale & 0xF;
			int leadScale = (scale >> 4) & 0xF;
			byte type = (byte)((scale >> 8) & 0xF);
			if (x.Trim().Length > 0)
			{
				SetINTERVALDT(paraVal, new DmIntervalDT(x, type, leadScale, secScale), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		}
		case 20:
			if (x.Trim().Length > 0)
			{
				SetINTERVALYM(paraVal, new DmIntervalYM(x, scale), cType, prec, scale, typeFlag, paraInternal);
			}
			else
			{
				SetNull(paraVal);
			}
			break;
		case 28:
			paraVal.SetInValue(DmRowId.valueOf(x).encode(m_Statement.G().Conn));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	internal string ReplaceNumPoint(string x)
	{
		if (ConnProperty.formatNumericChars != null)
		{
			return x.Replace(ConnProperty.formatNumericChars[0], '.');
		}
		return x;
	}

	public void SetINTERVALYM(DmParamValue paraVal, DmIntervalYM ym, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (ym == null)
		{
			SetNull(paraVal);
		}
		else if (typeFlag != 1)
		{
			paraVal.SetInValue(ym.encode(scale));
			paraVal.SetSqlType(20);
			paraVal.SetPrec(0);
			paraVal.SetScale(ym.getScaleForSvr());
		}
		else if (cType == 20)
		{
			paraVal.SetInValue(ym.encode(scale));
		}
		else
		{
			SetString(paraVal, ym.ToString(), cType, prec, scale, typeFlag, paraInternal);
		}
	}

	public void SetINTERVALDT(DmParamValue paraVal, DmIntervalDT dt, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (dt == null)
		{
			SetNull(paraVal);
		}
		else if (typeFlag != 1)
		{
			paraVal.SetInValue(dt.encode(scale));
			paraVal.SetSqlType(21);
			paraVal.SetPrec(0);
			paraVal.SetScale(dt.getScaleForSvr());
		}
		else if (cType == 21)
		{
			paraVal.SetInValue(dt.encode(scale));
		}
		else
		{
			SetString(paraVal, dt.ToString(), cType, prec, scale, typeFlag, paraInternal);
		}
	}

	private void SetBytes(DmParamValue paraVal, byte[] x, int cType, int prec, int scale, byte typeFlag, bool isComplexType)
	{
		if (x == null)
		{
			SetNull(paraVal);
			return;
		}
		if (prec > 0 && x.Length > prec)
			throw new OverflowException("Binary parameter exceeds the declared byte capacity.");
		if (prec < 0) prec = 0; // Unknown capacity must never become an array length.
		if (typeFlag != 1)
		{
			paraVal.SetInValue(x);
			paraVal.SetSqlType(18);
			paraVal.SetPrec(8188);
			paraVal.SetScale(0);
			return;
		}
		switch (cType)
		{
		case 17:
		{
			int num = x.Length;
			byte[] array;
			if (prec <= num)
			{
				array = x;
			}
			else
			{
				array = new byte[prec];
				Array.Copy(x, 0, array, 0, num);
			}
			paraVal.SetInValue(array);
			break;
		}
		case 1:
		case 2:
		case 12:
		case 18:
		case 19:
		{
			paraVal.SetInValue(ref x);
			break;
		}
		case 0:
		{
			int num = x.Length;
			byte[] array;
			if (prec <= num)
			{
				array = x;
			}
			else
			{
				array = new byte[prec];
				Array.Copy(x, 0, array, 0, num);
				for (int j = num; j < prec; j++)
				{
					array[j] = 32;
				}
			}
			paraVal.SetInValue(array);
			break;
		}
		case 3:
		{
			byte[] array = new byte[1];
			if (x[0] != 0)
			{
				array[0] = 1;
			}
			else
			{
				array[0] = 0;
			}
			paraVal.SetInValue(array);
			break;
		}
		case 5:
			paraVal.SetInValue(new byte[1] { x[0] });
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetDate(DmParamValue paraVal, DateTime x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			DmDateTime dmDateTime = new DmDateTime(x, prec, 6, 0, m_Statement.G().ConnProperty.TimeZone);
			paraVal.SetInValue(DmDateTime.DateEncodeFast(dmDateTime.GetByteArrayValue()));
			paraVal.SetSqlType(14);
			paraVal.SetPrec(3);
			paraVal.SetScale(6);
			return;
		}
		switch (cType)
		{
		case 14:
		{
			DmDateTime dmDateTime2 = new DmDateTime(x, prec, scale, 0, m_Statement.G().ConnProperty.TimeZone);
			paraVal.SetInValue(DmDateTime.DateEncodeFast(dmDateTime2.GetByteArrayValue()));
			break;
		}
		case 16:
		case 23:
			SetTimestamp(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetTime(DmParamValue paraVal, DateTime x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			DmTime dmTime = new DmTime(x, prec, scale, m_Statement.G().ConnProperty.TimeZone);
			paraVal.SetInValue(DmTime.TimeEncodeFast(dmTime.GetByteArrayValue()));
			paraVal.SetSqlType(cType);
			paraVal.SetPrec(prec);
			paraVal.SetScale(scale);
			return;
		}
		switch (cType)
		{
		case 15:
			paraVal.SetInValue(DmTime.TimeEncodeFast(x));
			break;
		case 22:
			paraVal.SetInValue(DmTime.TimeTzEncodeFast(x, m_Statement.G().ConnProperty.TimeZone));
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	public void SetTime(DmParamValue paraVal, DmTime t, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (t == null)
		{
			SetNull(paraVal);
			return;
		}
		if (typeFlag != 1)
		{
			paraVal.SetInValue(DmTime.TimeEncodeFast(t.GetByteArrayValue()));
			paraVal.SetSqlType(15);
			paraVal.SetPrec(5);
			paraVal.SetScale(6);
			return;
		}
		switch (cType)
		{
		case 15:
			paraVal.SetInValue(DmTime.TimeEncodeFast(t.GetByteArrayValue()));
			break;
		case 22:
			paraVal.SetInValue(DmTime.TimeTzEncodeFast(t.GetTzByteArrayValue()));
			break;
		case 16:
		case 23:
		{
			DateTime x = DateTime.Parse(t.ToString(), DmConst.invariantCulture);
			SetTimestamp(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		}
		default:
			SetString(paraVal, t.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		}
	}

	private void SetTimestamp(DmParamValue paraVal, DateTime x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		byte[] ret = null;
		if (typeFlag != 1)
		{
			if (cType == 14)
			{
				SetDate(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			}
			else if (cType == 15)
			{
				SetTime(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			}
			else if (cType == 26)
			{
				byte[] ret2 = null;
				DmDateTime.DmdtEncodeFast2(ref ret2, x);
				paraVal.SetInValue(ret2);
				paraVal.SetSqlType(26);
				paraVal.SetPrec(prec);
				paraVal.SetScale(scale);
			}
			else if (cType == 16)
			{
				DmDateTime dmDateTime = new DmDateTime(x, prec, scale, 2, m_Statement.G().ConnProperty.TimeZone);
				paraVal.SetInValue(DmDateTime.DmdtEncodeFast(dmDateTime.GetByteArrayValue()));
				paraVal.SetSqlType(16);
				paraVal.SetPrec(prec);
				paraVal.SetScale(scale);
			}
			else throw new NotSupportedException("DateTime target type is not supported by this wire mode.");
			return;
		}
		switch (cType)
		{
		case 14:
			SetDate(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 15:
		case 22:
			SetTime(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 16:
		{
			DmDateTime dmDateTime2 = new DmDateTime(x, prec, scale, 2, m_Statement.G().ConnProperty.TimeZone);
			dmDateTime2.GetByteArrayValue(ref ret);
			if (ret.Length == 8)
			{
				paraVal.SetInValue(ret);
				break;
			}
			DmDateTime.DmdtEncodeFast(ref paraVal.m_InValue, ref ret);
			paraVal.SetInValue();
			break;
		}
		case 26:
			DmDateTime.DmdtEncodeFast2(ref paraVal.m_InValue, x);
			paraVal.SetInValue();
			break;
		case 23:
		{
			DmDateTime dmDateTime2 = new DmDateTime(x, prec, scale, 3, m_Statement.G().ConnProperty.TimeZone);
			paraVal.SetInValue(DmDateTime.DmdttzEncodeFast(dmDateTime2.GetByteArrayValue()));
			break;
		}
		case 27:
			DmDateTime.Dmdt2TzEncodeFast2(ref paraVal.m_InValue, x, m_Statement.G().ConnProperty.TimeZone);
			paraVal.SetInValue();
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetTimeTZ(DmParamValue paraVal, DateTimeOffset x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		if (typeFlag != 1)
		{
			DmTime dmTime = new DmTime(x.DateTime, prec, scale, Convert.ToInt16(x.Offset.TotalMinutes));
			paraVal.SetInValue(DmTime.TimeTzEncodeFast(dmTime.GetByteArrayValue()));
			paraVal.SetSqlType(22);
			paraVal.SetPrec(5);
			paraVal.SetScale(6);
			return;
		}
		switch (cType)
		{
		case 15:
		{
			DmTime dmTime2 = new DmTime(x.DateTime, prec, scale, Convert.ToInt16(x.Offset.TotalMinutes));
			paraVal.SetInValue(DmTime.TimeEncodeFast(dmTime2.GetByteArrayValue()));
			break;
		}
		case 22:
		{
			DmTime dmTime2 = new DmTime(x.DateTime, prec, scale, Convert.ToInt16(x.Offset.TotalMinutes));
			paraVal.SetInValue(DmTime.TimeTzEncodeFast(dmTime2.GetTzByteArrayValue()));
			break;
		}
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetTimestampTZ(DmParamValue paraVal, DateTimeOffset x, int cType, int prec, int scale, byte typeFlag, DmParameterInternal paraInternal)
	{
		byte[] ret = null;
		if (typeFlag != 1)
		{
			if (cType == 23)
			{
				DmDateTime dmDateTime = new DmDateTime(x.DateTime, prec, scale, 3, Convert.ToInt16(x.Offset.TotalMinutes));
				paraVal.SetInValue(DmDateTime.DmdttzEncodeFast(dmDateTime.GetByteArrayValue()));
				paraVal.SetSqlType(23);
				paraVal.SetPrec(prec);
				paraVal.SetScale(scale);
			}
			else if (cType == 27)
			{
				DmDateTime.Dmdt2TzEncodeFast2(ref paraVal.m_InValue, x.DateTime, Convert.ToInt16(x.Offset.TotalMinutes));
				paraVal.SetInValue();
				paraVal.SetSqlType(27);
				paraVal.SetPrec(prec);
				paraVal.SetScale(scale);
			}
			else throw new NotSupportedException("DateTimeOffset target type is not supported by this wire mode.");
			return;
		}
		switch (cType)
		{
		case 14:
			SetDate(paraVal, x.DateTime, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 15:
			SetTime(paraVal, x.DateTime, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 22:
			SetTimeTZ(paraVal, x, cType, prec, scale, typeFlag, paraInternal);
			break;
		case 16:
		{
			DmDateTime dmDateTime2 = new DmDateTime(x.ToOffset(new TimeSpan(0, m_Statement.G().ConnProperty.TimeZone, 0)).DateTime, prec, scale, 3, Convert.ToInt16(x.Offset.TotalMinutes));
			dmDateTime2.GetByteArrayValue(ref ret);
			if (ret.Length == 8)
			{
				paraVal.SetInValue(ret);
				break;
			}
			DmDateTime.DmdtEncodeFast(ref paraVal.m_InValue, ref ret);
			paraVal.SetInValue();
			break;
		}
		case 26:
		{
			DateTimeOffset dateTimeOffset = x.ToOffset(new TimeSpan(0, m_Statement.G().ConnProperty.TimeZone, 0));
			DmDateTime.DmdtEncodeFast2(ref paraVal.m_InValue, dateTimeOffset.DateTime);
			paraVal.SetInValue();
			break;
		}
		case 23:
		{
			DmDateTime dmDateTime2 = new DmDateTime(x.DateTime, prec, scale, 3, Convert.ToInt16(x.Offset.TotalMinutes));
			paraVal.SetInValue(DmDateTime.DmdttzEncodeFast(dmDateTime2.GetByteArrayValue()));
			break;
		}
		case 27:
			DmDateTime.Dmdt2TzEncodeFast2(ref paraVal.m_InValue, x.DateTime, Convert.ToInt16(x.Offset.TotalMinutes));
			paraVal.SetInValue();
			break;
		case 0:
		case 1:
		case 2:
		case 19:
			SetString(paraVal, x.ToString(), cType, prec, scale, typeFlag, paraInternal);
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private void SetArray(DmParamValue paraVal, Array x, DmConnection conn, string typeName, int cType, DmParameterInternal paraInternal)
	{
		DmArray data = new DmArray(new ComplexTypeDesc(typeName, conn), conn, x);
		if (paraInternal.GetTypeFlag() != 1)
		{
			paraVal.SetInValue(ComplexTypeData.arrayToBytes(data, paraInternal.ComplexTypeDesc));
			paraVal.SetSqlType(cType);
			return;
		}
		switch (cType)
		{
		case 122:
			paraVal.SetInValue(ComplexTypeData.sarrayToBytes(data, paraInternal.ComplexTypeDesc));
			break;
		case 117:
		case 119:
			paraVal.SetInValue(ComplexTypeData.arrayToBytes(data, paraInternal.ComplexTypeDesc));
			break;
		default:
			throw new InvalidCastException();
		}
	}

	private static bool TrySetExactNumeric(DmParamValue paraVal, object value, int cType,
		int precision, int scale, byte typeFlag)
	{
		if (value is bool booleanValue) value = booleanValue ? 1 : 0;
		byte[] encoded;
		switch (cType)
		{
		case 3:
		{
			BigInteger integer = DmNumericInput.ToIntegerExact(value, BigInteger.Zero, BigInteger.One);
			encoded = DmConvertion.ByteToByteArray((byte)(int)integer);
			break;
		}
		case 5:
		{
			BigInteger integer = DmNumericInput.ToIntegerExact(value, sbyte.MinValue, sbyte.MaxValue);
			encoded = DmConvertion.ByteToByteArray(unchecked((byte)(sbyte)integer));
			break;
		}
		case 6:
		{
			BigInteger integer = DmNumericInput.ToIntegerExact(value, short.MinValue, short.MaxValue);
			encoded = DmConvertion.ShortToByteArray((short)integer);
			break;
		}
		case 7:
		{
			BigInteger integer = DmNumericInput.ToIntegerExact(value, int.MinValue, int.MaxValue);
			encoded = DmConvertion.IntToByteArray((int)integer);
			break;
		}
		case 8:
		{
			BigInteger integer = DmNumericInput.ToIntegerExact(value, long.MinValue, long.MaxValue);
			encoded = DmConvertion.LongToByteArray((long)integer);
			break;
		}
		case 9:
			encoded = DmNumericInput.EncodeDecimalInput(value, precision, scale);
			break;
		case 24:
			if (scale < 0) throw new InvalidOperationException("Scaled INT64 requires a known scale.");
			encoded = DmNumericInput.EncodeScaledInt64Input(value, precision, scale);
			break;
		default:
			return false;
		}
		paraVal.SetInValue(encoded);
		if (typeFlag != 1)
		{
			paraVal.SetSqlType(cType);
			paraVal.SetPrec(precision);
			int wireScale = scale < 0 ? DmNumericInput.ToExactDecimal(value).Scale : scale;
			paraVal.SetScale(wireScale);
		}
		return true;
	}

	// Command binding has already resolved its wire type. Legacy advisory branches must
	// not replace it based on the CLR runtime type; the real server flag stays intact.
	internal void SetResolvedObject(DmParamValue paraVal, object x, DmConnection conn, string typeName,
		int cType, DmParameterInternal paraInternal, int? precisionOverride = null, int? scaleOverride = null)
	{
		int precision = precisionOverride ?? paraInternal.GetPrecision();
		int scale = scaleOverride ?? (cType == 9 && paraInternal.GetTypeFlag() != 1 ? -1 : paraInternal.GetScale());
		if (paraInternal.GetTypeFlag() != 1 && cType == 17 && precisionOverride == null)
			precision = x is Guid ? 16 : x is byte[] bytes ? bytes.Length : precision;
		if (cType is 0 or 1 or 2 or 19)
		{
			if (x is Enum) x = DmNumericInput.ToEnumUnderlying(x);
			x = x switch
			{
				float value => ((float)DmNumericInput.ValidateFiniteFloating(value)).ToString("R", CultureInfo.InvariantCulture),
				double value => ((double)DmNumericInput.ValidateFiniteFloating(value)).ToString("R", CultureInfo.InvariantCulture),
				bool value => value ? "1" : "0",
				sbyte or byte or short or ushort or int or uint or long or ulong or decimal or BigInteger or DmDecimal
					=> DmNumericInput.ToExactDecimal(x).ToString(),
				_ => x
			};
		}
		if (cType == 10 && x is double wide)
		{
			DmNumericInput.ValidateFiniteFloating(wide);
			float narrow = (float)wide;
			if (!float.IsFinite(narrow) || (double)narrow != wide)
				throw new OverflowException("Double input cannot be represented exactly as Single.");
			x = narrow;
		}
		SetObjectCore(paraVal, x, conn, typeName, cType, paraInternal, precision, scale, codecTypeFlag: 1);
		if (!paraVal.GetIsInDataNull() && cType is 10 or 11)
		{
			byte[] encoded = paraVal.GetInValue();
			if (encoded.Length != (cType == 10 ? 4 : 8))
				throw new InvalidOperationException("Floating codec does not match the resolved wire type.");
			if (cType == 10) DmNumericInput.ValidateFiniteFloating(BitConverter.ToSingle(encoded, 0));
			else DmNumericInput.ValidateFiniteFloating(BitConverter.ToDouble(encoded, 0));
		}
		if (paraInternal.GetTypeFlag() != 1)
		{
			int wireScale = scale;
			if (wireScale < 0) wireScale = x is null or DBNull ? 0 : DmNumericInput.ToExactDecimal(x).Scale;
			paraVal.SetSqlType(cType);
			paraVal.SetPrec(precision);
			paraVal.SetScale(wireScale);
		}
	}

	public void SetObject(DmParamValue paraVal, object x, DmConnection conn, string typeName, int cType,
		DmParameterInternal paraInternal, int? precisionOverride = null, int? scaleOverride = null)
		=> SetObjectCore(paraVal, x, conn, typeName, cType, paraInternal, precisionOverride, scaleOverride,
			paraInternal.GetTypeFlag());

	private void SetObjectCore(DmParamValue paraVal, object x, DmConnection conn, string typeName, int cType,
		DmParameterInternal paraInternal, int? precisionOverride, int? scaleOverride, byte codecTypeFlag)
	{
		if (54 == cType)
		{
			ResetCTypeIfUnknown(paraInternal);
			cType = 2;
		}
		int precision = precisionOverride ?? paraInternal.GetPrecision();
		int scale = scaleOverride ?? (cType == 9 && paraInternal.GetTypeFlag() != 1 ? -1 : paraInternal.GetScale());
		byte typeFlag = codecTypeFlag;
		if (x == null)
		{
			SetNull(paraVal);
			return;
		}
		if (x is DBNull)
		{
			SetNull(paraVal);
			return;
		}
		if ((cType is 12 or 17 or 18) && (x is string or char[]))
			throw new NotSupportedException("Implicit text-to-binary parameter conversion is not supported.");
		if ((cType is 0 or 1 or 2 or 19) && x is byte[])
			throw new NotSupportedException("Implicit binary-to-text parameter conversion is not supported.");
		x = DmTemporalCodec.NormalizeForWire(x, cType, scale,
			conn?.GetConnInstance()?.ConnProperty.ServerVersion);
		if (x is Enum) x = DmNumericInput.ToEnumUnderlying(x);
		if (x is float or double) x = DmNumericInput.ValidateFiniteFloating(x);
		if (TrySetExactNumeric(paraVal, x, cType, precision, scale, typeFlag)) return;
		if (x is byte)
		{
			SetInt(paraVal, (byte)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is sbyte)
		{
			SetInt(paraVal, (sbyte)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is string)
		{
			SetString(paraVal, (string)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is char)
		{
			SetString(paraVal, x.ToString(), cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is char[])
		{
			string x2 = new string((char[])x);
			SetString(paraVal, x2, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is decimal)
		{
			SetBigDecimal(paraVal, (decimal)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is DmDecimal exactDecimal && cType is 0 or 1 or 2 or 19)
		{
			SetString(paraVal, exactDecimal.ToString(), cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is short)
		{
			SetShort(paraVal, (short)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is ushort)
		{
			SetInt(paraVal, (ushort)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is int)
		{
			SetInt(paraVal, (int)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is uint)
		{
			SetLong(paraVal, (uint)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is long)
		{
			SetLong(paraVal, (long)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is ulong)
		{
			SetULong(paraVal, (ulong)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is float)
		{
			SetFloat(paraVal, (float)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is double)
		{
			SetDouble(paraVal, (double)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is sbyte[])
		{
			SetBytes(paraVal, (byte[])x, cType, precision, scale, typeFlag, isComplexType: false);
			return;
		}
		if (x is byte[])
		{
			SetBytes(paraVal, (byte[])x, cType, precision, scale, typeFlag, isComplexType: false);
			return;
		}
		if (x is DateTime)
		{
			SetTimestamp(paraVal, (DateTime)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is DateTimeOffset)
		{
			SetTimestampTZ(paraVal, (DateTimeOffset)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is bool)
		{
			SetBoolean(paraVal, (bool)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is DmTime)
		{
			SetTime(paraVal, (DmTime)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is DmIntervalYM)
		{
			SetINTERVALYM(paraVal, (DmIntervalYM)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is DmIntervalDT)
		{
			SetINTERVALDT(paraVal, (DmIntervalDT)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is TimeSpan timeSpan)
		{
			switch (cType)
			{
			case 21:
				SetINTERVALDT(paraVal, new DmIntervalDT(timeSpan, scale), cType, precision, scale, typeFlag, paraInternal);
				break;
			case 15:
			{
				DateTime x4 = new DateTime(timeSpan.Ticks);
				SetTime(paraVal, x4, cType, precision, scale, typeFlag, paraInternal);
				break;
			}
			case 22:
			{
				DateTime x3 = new DateTime(timeSpan.Ticks);
				SetTime(paraVal, x3, cType, precision, scale, typeFlag, paraInternal);
				break;
			}
			default:
				SetString(paraVal, ((TimeSpan)x/*cast due to constrained. prefix*/).ToString(), cType, precision, scale, typeFlag, paraInternal);
				break;
			}
			return;
		}
		if (x is DmXDec)
		{
			SetDmDecimal(paraVal, (DmXDec)x, cType, precision, scale, typeFlag, paraInternal);
			return;
		}
		if (x is Guid guid)
		{
			if (cType is 0 or 1 or 2 or 19)
			{
				if (typeFlag == 1 && precision is > 0 and < 36)
					throw new OverflowException("GUID text requires at least 36 characters.");
				SetString(paraVal, guid.ToString("D"), cType, precision, scale, typeFlag, paraInternal);
				return;
			}
			if (cType is 17 or 18)
			{
				if (precision != 16 && (typeFlag == 1 || precision != 0))
					throw new OverflowException("GUID binary storage requires exactly 16 bytes.");
				SetBytes(paraVal, guid.ToByteArray(), cType, 16, scale, typeFlag, isComplexType: false);
				return;
			}
			throw new NotSupportedException("GUID requires a character or 16-byte binary parameter.");
		}
		if (x is Array && !typeName.Equals(""))
		{
			SetArray(paraVal, (Array)x, conn, typeName, cType, paraInternal);
		}
		else if (x is DmBlob dmBlob)
		{
			long num2 = dmBlob.do_length();
			byte[] bytes = dmBlob.GetBytes(0L, (int)num2);
			byte[] array2;
			if (precision < num2 && precision != 0)
			{
				array2 = new byte[precision];
				Array.Copy(bytes, 0, array2, 0, precision);
			}
			else
			{
				array2 = bytes;
			}
			paraVal.SetInValue(array2);
		}
		else if (x is DmStruct x5)
		{
			SetBytes(paraVal, N2DB.fromStruct(x5, paraInternal, conn), cType, precision, scale, typeFlag, isComplexType: true);
		}
		else
		{
			if (!(x is DmArray dmArray))
			{
				throw new SystemException("Value is of unknown data type");
			}
			SetBytes(paraVal, ComplexTypeData.toBytes(dmArray, dmArray.m_arrDesc), cType, precision, scale, typeFlag, isComplexType: true);
		}
	}

	private string fixDecString(string val, int prec, int scale, int signNum)
	{
		string text = "";
		string text2 = "";
		string text3 = "";
		text3 = ((signNum >= 0) ? "+" : "-");
		string[] array = val.Split(new char[1] { '.' });
		if (array.Length == 1)
		{
			if (array[0].Length < prec - scale)
			{
				for (int i = 0; i < prec - scale - array[0].Length; i++)
				{
					text += "0";
				}
				array[0] = text + array[0];
			}
			for (int j = 0; j < scale; j++)
			{
				text2 += "0";
			}
			return text3 + array[0] + "." + text2;
		}
		if (array[0].Length > prec - scale)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_DIGITAL_FORMAT);
		}
		if (array[0].Length < prec - scale)
		{
			if (array[0].Equals("0"))
			{
				array[0] = "";
			}
			for (int k = 0; k < prec - scale - array[0].Length; k++)
			{
				text += "0";
			}
		}
		array[0] = text + array[0];
		if (array[1].Length > scale)
		{
			array[1] = array[1].Substring(0, scale);
		}
		else if (array[1].Length < scale)
		{
			for (int l = 0; l < scale - array[1].Length; l++)
			{
				text2 += "0";
			}
		}
		array[1] += text2;
		return text3 + array[0] + "." + array[1];
	}

	private byte[] decStringToBcd(string decString)
	{
		bool flag = true;
		byte[] array = new byte[(decString.Length + 1) / 2];
		int num = 0;
		char[] array2 = decString.ToCharArray();
		for (int i = 0; i < array2.Length; i++)
		{
			byte b2 = (byte)(('0' <= array2[i] && array2[i] <= '9') ? ((byte)(array2[i] - 48)) : (array2[i] switch
			{
				'.' => 10, 
				'+' => 11, 
				'-' => 12, 
				_ => 15, 
			}));
			if (flag)
			{
				array[num] = (byte)((array[num] & 0xF0) | (b2 & 0xF));
				flag = false;
			}
			else
			{
				array[num] = (byte)((array[num] & 0xF) | ((b2 << 4) & 0xF0));
				flag = true;
				num++;
			}
		}
		if (!flag)
		{
			array[num] = (byte)((array[num] & 0xF) | 0xF0);
		}
		return array;
	}

	internal void ResetCTypeIfUnknown(DmParameterInternal paraInternal)
	{
		int cType = paraInternal.GetCType();
		int precision = paraInternal.GetPrecision();
		if (cType == 54 && precision == 0)
		{
			paraInternal.SetCType(2);
			paraInternal.SetPrecision(32767);
		}
	}
}
