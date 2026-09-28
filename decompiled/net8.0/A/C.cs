using System;
using System.Collections.Generic;
using System.Data;
using Dm;
using Dm.Config;
using Dm.parser;
using Dm.util;

namespace A;

internal class C
{
	private C()
	{
	}

	public static void A(b P_0, short P_1, int P_2)
	{
		P_0.a(64);
		P_0.a(P_1);
		P_0.N(P_2);
		P_0.G((byte)1);
		P_0.i();
	}

	public static void a(b P_0, short P_1, int P_2)
	{
		P_0.a(64);
		P_0.a(P_1);
		P_0.N(P_2);
		P_0.i();
	}

	public static void A(b P_0, DmConnProperty P_1, D P_2)
	{
		P_0.a(64);
		P_0.a((short)200);
		P_0.N(0);
		P_0.O(0);
		P_0.o(P_1.Compress);
		P_0.B(P_1.LoginEncrypt ? ((byte)1) : ((byte)0));
		P_0.b((byte)(P_1.LoginEncrypt ? 2 : 0));
		P_0.C((byte)(P_1.IsBdtaRs ? 2 : 0));
		P_0.A(34, P_1.crcBody ? ((byte)1) : ((byte)0));
		P_0.P(P_1.msgVersion);
		P_0.B(DmConvertion.GetBytes("8.3.1.3", P_1.ServerEncoding));
		P_0.A((byte)0);
		if (P_1.LoginEncrypt)
		{
			P_0.B(P_2.B());
		}
		if (StringUtil.isNotEmpty(P_1.Catalog))
		{
			P_0.A(P_1.Catalog, P_1.ServerEncoding);
		}
		P_0.h(1);
		P_0.h(0);
		P_0.i();
	}

	public static void a(b P_0, DmConnProperty P_1, D P_2)
	{
		int num = -1;
		P_0.a(64);
		P_0.a((short)1);
		P_0.N(0);
		P_0.p(P_1.SessClt);
		num = ((P_1.IsolationLevel == IsolationLevel.ReadCommitted) ? 1 : ((P_1.IsolationLevel != IsolationLevel.Serializable) ? (-1) : 3));
		P_0.Q(num);
		P_0.q((P_1.Language == Convert.ToInt32(SupportedLanguage.cn_tw)) ? Convert.ToInt32(SupportedLanguage.cn_hk) : P_1.Language);
		P_0.D(P_1.AccessMode);
		P_0.B(P_1.TimeZone);
		P_0.R(P_1.CommandTimeout);
		P_0.d(P_1.MppType);
		byte b2 = Convert.ToByte(P_1.RwSeparate);
		P_0.c(b2);
		P_0.E(P_1.NewLobFlag ? ((byte)1) : ((byte)0));
		P_0.F(P_1.LongLobFlag ? ((byte)1) : ((byte)0));
		P_0.f((byte)1);
		byte[] array = DmConvertion.GetBytes(P_1.User, P_1.ServerEncoding);
		byte[] array2 = DmConvertion.GetBytes(P_1.Pwd, P_1.ServerEncoding);
		if (P_1.encryptPwd && P_2.a != null)
		{
			array = P_2.a.Encrypt(array, genDigest: false);
			array2 = P_2.a.Encrypt(array2, genDigest: false);
		}
		P_0.B(array);
		P_0.B(array2);
		P_0.B(DmConvertion.GetBytes(P_1.AppName, P_1.ServerEncoding));
		P_0.B(DmConvertion.GetBytes(P_1.OS, P_1.ServerEncoding));
		P_0.B(DmConvertion.GetBytes(P_1.Host, P_1.ServerEncoding));
		if (P_1.RWStandby)
		{
			P_0.A((byte)1);
		}
		else
		{
			P_0.A((byte)0);
		}
		P_0.i();
	}

	public static void A(b P_0, int P_1)
	{
		P_0.a(64);
		P_0.a((short)4);
		P_0.N(P_1);
		P_0.i();
	}

	public static void A(b P_0, int P_1, DmConnProperty P_2, string P_3, bool P_4, int P_5, byte P_6, int P_7)
	{
		P_0.a(64);
		P_0.N(P_1);
		P_0.a((short)5);
		if (P_2.AutoCommit)
		{
			P_0.g((byte)1);
		}
		else
		{
			P_0.g((byte)0);
		}
		if (P_4)
		{
			P_0.H((byte)1);
		}
		else
		{
			P_0.H((byte)0);
		}
		P_0.i((byte)((P_2.CompatibleMode != CompatibleMode.ORACLE) ? ((byte)P_5) : 0));
		P_0.I(P_6);
		P_0.b((short)0);
		P_0.B((P_2.MaxRows <= 0 || P_2.EnRsCache) ? long.MaxValue : P_2.MaxRows);
		P_0.J((byte)(P_2.IsBdtaRs ? 2 : 0));
		P_0.C((short)0);
		P_0.r(P_7);
		P_0.B(P_3, P_2.ServerEncoding);
		P_0.i();
	}

	private static void A(int P_0, b P_1, DmParameterInternal[] P_2)
	{
		DmParameterInternal dmParameterInternal = null;
		for (int i = 0; i < P_0; i++)
		{
			dmParameterInternal = P_2[i];
			int num;
			int num2;
			int num3;
			if (dmParameterInternal.GetTypeFlag() == 1)
			{
				a(dmParameterInternal, out num, out num2, out num3);
			}
			else if (dmParameterInternal.GetInDataBound())
			{
				if (dmParameterInternal.GetTypeFlag() == 0 || !dmParameterInternal.GetIsInDataNull(0))
				{
					if (dmParameterInternal.ComplexTypeDesc != null && dmParameterInternal.ComplexTypeDesc.GetObjId() == 4)
					{
						a(dmParameterInternal, out num, out num2, out num3);
					}
					else
					{
						A(dmParameterInternal, out num, out num2, out num3);
					}
				}
				else
				{
					a(dmParameterInternal, out num, out num2, out num3);
				}
			}
			else if (dmParameterInternal.GetTypeFlag() == 2 && 54 != dmParameterInternal.GetCType())
			{
				a(dmParameterInternal, out num, out num2, out num3);
			}
			else
			{
				num = 2;
				num2 = 8188;
				num3 = 0;
			}
			if (num == 120)
			{
				P_1.A((byte)2);
			}
			else
			{
				P_1.A((byte)dmParameterInternal.GetInOutType());
			}
			P_1.H(num);
			ComplexTypeDesc complexTypeDesc = P_2[i].ComplexTypeDesc;
			if (complexTypeDesc != null)
			{
				if (num == 12 && DmSqlType.isComplexType(num, num3))
				{
					num2 = complexTypeDesc.GetObjId();
					if (num2 == 4)
					{
						num2 = complexTypeDesc.GetOuterId();
					}
				}
				else
				{
					switch (complexTypeDesc.GetDType())
					{
					case 117:
					case 122:
						num2 = ComplexTypeDesc.GetPackArraySize(complexTypeDesc);
						break;
					case 121:
						num2 = ComplexTypeDesc.GetPackRecordSize(complexTypeDesc);
						break;
					case 119:
						num2 = ComplexTypeDesc.GetPackClassSize(complexTypeDesc);
						break;
					}
				}
			}
			if ((uint)num <= 2u && dmParameterInternal.maxValueLen > num2)
			{
				num2 = ((dmParameterInternal.maxValueLen < 8191) ? 8191 : ((dmParameterInternal.maxValueLen < 16383) ? 16383 : ((dmParameterInternal.maxValueLen >= 24575) ? 32767 : 24575)));
			}
			P_1.H(num2);
			P_1.H(num3);
			if (complexTypeDesc != null && num != 12)
			{
				switch (complexTypeDesc.GetDType())
				{
				case 117:
				case 122:
					ComplexTypeDesc.PackArray(complexTypeDesc, P_1);
					break;
				case 121:
					ComplexTypeDesc.PackRecord(complexTypeDesc, P_1);
					break;
				case 119:
					ComplexTypeDesc.PackClass(complexTypeDesc, P_1);
					break;
				}
			}
		}
	}

	private static void A(DmParameterInternal P_0, out int P_1, out int P_2, out int P_3)
	{
		if (120 == P_0.type)
		{
			P_1 = P_0.type;
			P_2 = 0;
			P_3 = 0;
		}
		else
		{
			P_1 = P_0.GetSqlType();
			P_2 = P_0.GetPrec();
			P_3 = P_0.GetBindScale();
		}
	}

	internal static void a(DmParameterInternal P_0, out int P_1, out int P_2, out int P_3)
	{
		P_1 = P_0.GetCType();
		P_2 = P_0.GetPrecision();
		P_3 = P_0.GetScale();
	}

	private static void A(b P_0)
	{
		P_0.A((short)0);
	}

	private static void A(int P_0, b P_1, DmParameterInternal[] P_2, A P_3, int P_4)
	{
		DmParameterInternal dmParameterInternal = null;
		byte[] InValue = null;
		for (int i = 0; i < P_0; i++)
		{
			dmParameterInternal = P_2[i];
			int num = ((dmParameterInternal.GetTypeFlag() == 1) ? dmParameterInternal.GetCType() : (dmParameterInternal.GetInDataBound() ? dmParameterInternal.GetCType() : ((dmParameterInternal.GetTypeFlag() != 2) ? dmParameterInternal.GetSqlType() : dmParameterInternal.GetCType())));
			if (dmParameterInternal.GetInOutType() == 1 && num != 120)
			{
				continue;
			}
			dmParameterInternal.GetInValue(ref InValue, P_4);
			int num2;
			if (dmParameterInternal.GetIsInDataNull(P_4))
			{
				num2 = 65534;
				P_1.h(num2);
				continue;
			}
			num2 = InValue.Length;
			if (num2 > P_3.G().ConnProperty.lobOffRowLen && (num == 12 || num == 19))
			{
				num2 = 0;
				if (!P_3.E())
				{
					P_3.h().A(P_0, P_3, P_2);
					P_3.b(true);
				}
				if (num == 12)
				{
					if (P_3.G().ConnProperty.msgVersion >= 10)
					{
						byte[] array = P_3.h().b(P_3, i, dmParameterInternal, P_4);
						num2 = 65529;
						P_1.h(num2);
						P_1.A(array);
					}
					else
					{
						P_1.h(num2);
						P_3.h().B(P_3, i, dmParameterInternal, P_4);
					}
				}
				else if (P_3.G().ConnProperty.msgVersion >= 10)
				{
					byte[] array2 = P_3.h().a(P_3, i, dmParameterInternal, P_4);
					num2 = 65529;
					P_1.h(num2);
					P_1.A(array2);
				}
				else
				{
					P_1.h(num2);
					P_3.h().A(P_3, i, dmParameterInternal, P_4);
				}
			}
			else if (num2 > 65535)
			{
				if (P_3.G().ConnProperty.msgVersion >= 6 && num2 < uint.MaxValue && DmSqlType.isComplexType(dmParameterInternal.GetSqlType(), dmParameterInternal.GetScale()))
				{
					P_1.h(65535);
					P_1.B(InValue);
				}
				else
				{
					DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_TOO_LONG);
				}
			}
			else
			{
				P_1.h(num2);
				P_1.A(InValue);
			}
		}
	}

	public static void A(b P_0, A P_1, DmInfo P_2, DmConnProperty P_3)
	{
		DmParameterInternal[] ParamsInfo = null;
		P_2.GetParamsInfo(out ParamsInfo);
		int num = ((ParamsInfo != null) ? P_2.GetParameterCount() : 0);
		P_0.a(64);
		P_0.N(P_1.g());
		if (P_3.VerNum > 117506688)
		{
			P_0.a((short)13);
		}
		else
		{
			P_0.a((short)6);
		}
		if (P_3.AutoCommit)
		{
			P_0.j((byte)1);
		}
		else
		{
			P_0.j((byte)0);
		}
		P_0.S(num);
		P_0.K(P_1.D());
		if (ParamsInfo.Length != 0)
		{
			P_0.b((long)ParamsInfo[0].GetParamValue().Count);
		}
		P_0.C(P_1.R());
		P_0.c((P_3.MaxRows <= 0 || P_3.EnRsCache) ? long.MaxValue : P_3.MaxRows);
		P_0.B(P_3.BatchContinueOnError);
		P_0.s((P_1.I() == 0) ? (-1) : P_1.I());
		P_0.T(P_3.BatchAllowMaxErrors);
		A(num, P_0, ParamsInfo);
		if (ParamsInfo.Length != 0)
		{
			for (int i = 0; i < ParamsInfo[0].GetParamValue().Count; i++)
			{
				A(num, P_0, ParamsInfo, P_1, i);
			}
		}
		P_0.i();
		if (P_3.msgVersion >= 10 && P_0.J() > 536838144)
		{
			throw new DmException("消息超长，调整batchSize大小!");
		}
	}

	public static void A(int P_0, b P_1, A P_2, DmInfo P_3, DmConnProperty P_4)
	{
		DmParameterInternal[] ParamsInfo = null;
		P_3.GetParamsInfo(out ParamsInfo);
		int num = ((ParamsInfo != null) ? P_3.GetParameterCount() : 0);
		P_1.a(64);
		P_1.N(P_2.g());
		if (P_4.VerNum > 117506688)
		{
			P_1.a((short)13);
		}
		else
		{
			P_1.a((short)6);
		}
		if (P_4.AutoCommit)
		{
			P_1.j((byte)1);
		}
		else
		{
			P_1.j((byte)0);
		}
		P_1.S(num);
		P_1.K(P_2.D());
		if (ParamsInfo.Length != 0)
		{
			P_1.b(1L);
		}
		P_1.C(P_2.R());
		P_1.c((P_4.MaxRows <= 0 || P_4.EnRsCache) ? long.MaxValue : P_4.MaxRows);
		P_1.B(P_4.BatchContinueOnError);
		P_1.s((P_2.I() == 0) ? (-1) : P_2.I());
		P_1.T(P_4.BatchAllowMaxErrors);
		A(num, P_1, ParamsInfo);
		if (ParamsInfo.Length != 0)
		{
			A(num, P_1, ParamsInfo, P_2, P_0);
		}
		P_1.i();
		if (P_4.msgVersion >= 10 && P_1.J() > 536838144)
		{
			throw new DmException("消息超长，调整batchSize大小!");
		}
	}

	public static void A(int P_0, b P_1, int P_2, DmParameterInternal[] P_3)
	{
		P_1.a(64);
		P_1.N(P_2);
		P_1.a((short)90);
		P_1.c((short)P_0);
		A(P_0, P_1, P_3);
		P_1.i();
	}

	public static void A(b P_0, int P_1, long P_2, short P_3, long P_4, int P_5)
	{
		P_0.a(64);
		P_0.a((short)7);
		P_0.N(P_1);
		P_0.d(P_4);
		P_0.D(P_2);
		P_0.D(P_3);
		P_0.U(P_5);
		P_0.i();
	}

	public static void a(b P_0, int P_1)
	{
		P_0.a(64);
		P_0.a((short)17);
		P_0.N(P_1);
		P_0.i();
	}

	public static void A(b P_0, A P_1, string P_2, DmConnProperty P_3)
	{
		P_0.a(64);
		P_0.a((short)27);
		P_0.N(P_1.g());
		P_0.B(P_2, P_3.ServerEncoding);
		P_0.H(1);
		P_0.i();
	}

	public static void A(b P_0, int P_1, short P_2, byte[] P_3, int P_4, DmConnProperty P_5, int P_6)
	{
		P_0.a(64);
		P_0.N(P_1);
		P_0.a((short)14);
		P_0.t(P_2);
		P_0.H(P_4);
		if (P_5.NewLobFlag)
		{
			P_0.H(-1);
		}
		P_0.A(P_3, P_4);
		P_0.i();
	}

	public static void A(b P_0, int P_1, int P_2, byte[] P_3, int P_4, DmConnProperty P_5, byte[] P_6)
	{
		P_0.a(64);
		P_0.N(P_1);
		P_0.a((short)26);
		P_0.t(P_2);
		P_0.A(P_6);
		P_0.H(P_4);
		if (P_5.NewLobFlag)
		{
			P_0.H(-1);
		}
		P_0.A(P_3, P_4);
		P_0.i();
	}

	public static byte[] A()
	{
		byte[] array = new byte[21];
		int num = 0;
		array[num++] = 2;
		byte[] array2 = DmConvertion.LongToByteArray(-1L);
		Array.Copy(array2, 0, array, num, array2.Length);
		num += array2.Length;
		array2 = DmConvertion.IntToByteArray(0L);
		Array.Copy(array2, 0, array, num, array2.Length);
		num += array2.Length;
		array2 = DmConvertion.ShortToByteArray(-1);
		Array.Copy(array2, 0, array, num, array2.Length);
		num += array2.Length;
		array2 = DmConvertion.ShortToByteArray(-1);
		Array.Copy(array2, 0, array, num, array2.Length);
		num += array2.Length;
		array2 = DmConvertion.IntToByteArray(-1L);
		Array.Copy(array2, 0, array, num, array2.Length);
		num += array2.Length;
		return array;
	}

	public static void A(b P_0, int P_1, short P_2)
	{
		P_0.a(64);
		P_0.N(P_1);
		P_0.a((short)44);
		P_0.d(P_2);
		P_0.i();
	}

	public static void A(b P_0, A P_1, List<SQLProcessor.Parameter> P_2, DmConnProperty P_3)
	{
		P_0.a(64);
		P_0.N(P_1.g());
		P_0.a((short)91);
		if (P_1.G().GetAutoCommit())
		{
			P_0.j((byte)1);
		}
		else
		{
			P_0.j((byte)0);
		}
		P_0.S(P_2.Count);
		P_0.K((byte)0);
		P_0.b(1L);
		P_0.C(P_1.R());
		P_0.c(long.MaxValue);
		P_0.s(P_1.f().CommandTimeout);
		P_0.B(P_1.C(), P_3.ServerEncoding);
		foreach (SQLProcessor.Parameter item in P_2)
		{
			P_0.A((byte)item.ioType);
			P_0.H(item.type);
			P_0.H(item.prec);
			P_0.H(item.scale);
		}
		foreach (SQLProcessor.Parameter item2 in P_2)
		{
			if (item2.bytes == null)
			{
				P_0.A((short)(-2));
				continue;
			}
			P_0.A((short)item2.bytes.Length);
			P_0.A(item2.bytes);
		}
		P_0.i();
	}

	public static void B(b P_0, int P_1)
	{
		P_0.a(64);
		P_0.N(0);
		P_0.a((short)52);
		P_0.X(P_1);
		P_0.i();
	}

	public static void A(b P_0, int[] P_1)
	{
		P_0.a(64);
		P_0.a((short)71);
		P_0.E((short)P_1.Length);
		foreach (int num in P_1)
		{
			P_0.H(num);
		}
		P_0.i();
	}
}
internal class c
{
	public static void A(b P_0, DmConnProperty P_1)
	{
		if (P_0.L() < 0)
		{
			A(P_0, P_1.ServerEncoding, P_1.RWStandby);
		}
	}

	public static void a(b P_0, DmConnProperty P_1)
	{
		DmError dmError = new DmError();
		if (P_0.L() < 0)
		{
			A(P_0, P_1.ServerEncoding, P_1.RWStandby);
		}
		P_1.Encrypt = P_0.m();
		P_1.Serials = P_0.N();
		switch (P_0.n())
		{
		case 1:
			P_1.ServerEncoding = "UTF-8";
			break;
		case 0:
			if (P_1.Language == Convert.ToInt32(SupportedLanguage.cn_hk) || P_1.Language == Convert.ToInt32(SupportedLanguage.cn_tw))
			{
				P_1.ServerEncoding = "BIG5";
			}
			else
			{
				P_1.ServerEncoding = "GB18030";
			}
			break;
		case 2:
			P_1.ServerEncoding = "euc-kr";
			break;
		}
		P_1.Compress = P_0.O();
		int num = P_0.P();
		byte num2 = P_0.p();
		P_1.CaseSensitive = P_0.o() == 1;
		P_1.IsBdtaRs = P_0.Q() == 2;
		P_1.crcBody = P_0.q() == 1;
		P_1.msgVersion = Math.Min(P_1.msgVersion, P_0.R());
		P_0.A(dmError, P_1.ServerEncoding);
		if (P_1.msgVersion < 21)
		{
			A(P_0.b(P_1.ServerEncoding), P_1);
		}
		if (num2 > 0)
		{
			P_1.encryptType = P_0.d();
		}
		if (num > 0)
		{
			if (P_1.encryptType == -1)
			{
				P_1.encryptPwd = true;
			}
			else
			{
				P_1.encryptMsg = true;
			}
			P_1.serverPubKey = P_0.F(P_0.d());
		}
		if (num2 == 2)
		{
			P_1.hashType = P_0.d();
		}
		if (P_1.msgVersion >= 17)
		{
			P_0.f(8);
			P_1.algorithm = P_0.D();
		}
	}

	public static void B(b P_0, DmConnProperty P_1)
	{
		DmError dmError = new DmError();
		if (P_0.L() < 0)
		{
			A(P_0, P_1.ServerEncoding, P_1.RWStandby);
		}
		P_1.MaxRowSize = P_0.r();
		P_1.MaxSession = P_0.S();
		P_1.DDLAutoCommit = P_0.s();
		switch (P_0.T())
		{
		case 1:
			P_1.IsolationLevel = IsolationLevel.ReadCommitted;
			break;
		case 3:
			P_1.IsolationLevel = IsolationLevel.Serializable;
			break;
		case 0:
			P_1.IsolationLevel = IsolationLevel.ReadUncommitted;
			break;
		default:
			P_1.IsolationLevel = IsolationLevel.Unspecified;
			break;
		}
		P_1.CaseSensitive = P_0.t() == 1;
		P_1.BackslashEsc = P_0.U();
		P_1.C2p = P_0.u();
		P_1.DbTimeZone = P_0.V();
		P_1.DbrwSeparate = P_0.v() == 1;
		P_1.NewLobFlag = P_0.W() == 1;
		P_1.LongLobFlag = P_0.x() == 1;
		P_1.HeartBeatTimeout = P_0.Y() * 1000;
		if (P_1.BufPrefetch == 0)
		{
			P_1.BufPrefetch = P_0.w();
		}
		P_1.SvrMode = P_0.Z();
		P_1.SvrStat = P_0.y();
		P_1.DscControl = P_0.X() == 1;
		P_0.A(dmError, P_1.ServerEncoding);
		P_1.Database = P_0.b(P_1.ServerEncoding);
		int num = P_0.d();
		if (num == 0 && P_1.msgVersion > 0)
		{
			P_1.CurrentSchema = P_1.User.ToUpper();
		}
		else
		{
			P_1.CurrentSchema = P_0.A(num, P_1.ServerEncoding);
		}
		string lastLoginIP = P_0.b(P_1.ServerEncoding);
		P_1.LastLoginIP = lastLoginIP;
		string lastLoginTime = P_0.b(P_1.ServerEncoding);
		P_1.LastLoginTime = lastLoginTime;
		P_1.FailedAttempts = P_0.d();
		P_1.LoginWarningID = P_0.d();
		P_1.GracetimeRemainder = P_0.d();
		P_1.Guid = P_0.b(P_1.ServerEncoding);
		P_0.f(P_0.d());
		if (P_1.DbrwSeparate)
		{
			P_1.StandbyIp = P_0.b(P_1.ServerEncoding);
			P_1.StandbyPort = P_0.d();
			P_1.StandbyNum = P_0.C();
		}
		if (P_0.a() < P_0.J() + 64)
		{
			P_1.SessId = P_0.e();
		}
		if (P_0.a() < P_0.k() + 64 && P_0.b() == 1)
		{
			P_1.oracleDateFormat = "DD-MON-YY";
			P_1.oracleTimeFormat = "HH12.MI.SS.FF6 AM";
			P_1.oracleTimestampFormat = "DD-MON-YY HH12.MI.SS.FF6 AM";
			P_1.oracleTimestampTZFormat = "DD-MON-YY HH12.MI.SS.FF6 AM +TZH:TZM";
			P_1.oracleTimeTZFormat = "HH12.MI.SS.FF6 AM +TZH:TZM";
			P_1.nlsDateLang = 0;
		}
		if (P_0.a() < P_0.k() + 64)
		{
			string text = P_0.a(P_1.ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				P_1.oracleDateFormat = text;
			}
			text = P_0.a(P_1.ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				P_1.oracleTimeFormat = text;
			}
			text = P_0.a(P_1.ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				P_1.oracleTimestampFormat = text;
			}
			text = P_0.a(P_1.ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				P_1.oracleTimestampTZFormat = text;
			}
			text = P_0.a(P_1.ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				P_1.oracleTimeTZFormat = text;
			}
		}
		if (P_0.a() < P_0.k() + 64)
		{
			P_1.proxyClient = P_0.a(P_1.ServerEncoding);
		}
		if (P_0.a() < P_0.k() + 64)
		{
			P_0.f(1);
		}
		if (P_1.msgVersion >= 9)
		{
			bool flag = P_0.E() != 0;
			long num2 = P_0.E();
			long num3 = P_0.E();
			P_1.rowidNBitsEpno = A(num2, flag, num3);
			P_1.rowidMaxHpno = a(num2, flag, num3);
			P_1.rowidMaxEpno = A(P_1.rowidNBitsEpno, flag, num3);
		}
		if (P_0.a() < P_0.k() + 64)
		{
			P_0.A(2, false, true);
		}
		if (P_0.a() < P_0.k() + 64)
		{
			short num4 = P_0.C();
			if (((num4 > 1) ? 1 : num4) == 1)
			{
				P_0.A(4, false, true);
			}
		}
		if (P_0.a() < P_0.k() + 64 && P_1.msgVersion >= 21)
		{
			A(P_0.b(P_1.ServerEncoding), P_1);
		}
	}

	private static void A(string P_0, DmConnProperty P_1)
	{
		int num = A(P_0.Insert(P_0.Length, "-"));
		if (num < 117440512)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SVR_VERSION_WRONG);
		}
		P_1.ServerVersion = P_0;
		P_1.VerNum = num;
	}

	private static int A(long P_0, bool P_1, long P_2)
	{
		long num = 1L;
		int num2 = 0;
		if (!P_1 && P_2 == 0L)
		{
			return num2;
		}
		while (num < P_0)
		{
			num <<= 1;
			num2++;
		}
		return num2;
	}

	private static long a(long P_0, bool P_1, long P_2)
	{
		if (!P_1 && P_2 == 0L)
		{
			return 65534L;
		}
		int num = A(P_0, P_1, P_2);
		return 1L << 16 - num - 1;
	}

	private static int A(int P_0, bool P_1, long P_2)
	{
		if (!P_1 && P_2 == 0L)
		{
			return 0;
		}
		return (int)((1L << P_0) - 1);
	}

	public static int A(b P_0, DmConnProperty P_1, ref bool P_2)
	{
		if (P_0.L() < 0)
		{
			A(P_0, P_1.ServerEncoding, P_1.RWStandby);
		}
		P_2 = P_0.z() == 1;
		return P_0.j();
	}

	private static void A(b P_0, int P_1, ref DmInfo P_2)
	{
		P_2.GetParamsInfo(out var ParamsInfo);
		for (int i = 0; i < P_1; i++)
		{
			ParamsInfo[i].SetCType(P_0.d());
			ParamsInfo[i].SetPrecision(P_0.d());
			ParamsInfo[i].SetScale(P_0.d());
			ParamsInfo[i].SetNullable(P_0.d() != 0);
			short num = P_0.C();
			ParamsInfo[i].SetTypeFlag((byte)(((num & 8) == 0) ? 1 : 2));
			ParamsInfo[i].SetIsLob((num & 2) != 0);
			ParamsInfo[i].Readonly = (num & 4) != 0;
			P_0.d();
			ParamsInfo[i].SetInOutType(P_0.C());
			short len = P_0.C();
			short len2 = P_0.C();
			short len3 = P_0.C();
			short len4 = P_0.C();
			ParamsInfo[i].SetName(P_0, len);
			ParamsInfo[i].SetTypeName(P_0, len2);
			ParamsInfo[i].SetTable(P_0, len3);
			ParamsInfo[i].SetSchema(P_0, len4);
			if (ParamsInfo[i].GetIsLob())
			{
				ParamsInfo[i].SetTableID(P_0.d());
				ParamsInfo[i].SetColID(P_0.C());
			}
			if (9 == ParamsInfo[i].GetCType() && 129 == ParamsInfo[i].GetScale())
			{
				ParamsInfo[i].SetPrecision((int)Math.Round((double)ParamsInfo[i].GetPrec() * 0.30103));
				ParamsInfo[i].SetScale(0);
			}
		}
		for (int j = 0; j < P_1; j++)
		{
			if (DmSqlType.isComplexType(ParamsInfo[j].GetCType(), ParamsInfo[j].GetScale()))
			{
				ComplexTypeDesc complexTypeDesc = new ComplexTypeDesc(P_2.ConnInstance.Conn);
				complexTypeDesc.Unpack(P_0);
				ParamsInfo[j].ComplexTypeDesc = complexTypeDesc;
			}
		}
	}

	internal static void A(b P_0, int P_1, DmInfo P_2, DmConnInstance P_3, bool P_4)
	{
		if (P_1 == 0)
		{
			return;
		}
		int num = P_0.a();
		DmColumn[] array = new DmColumn[P_1];
		for (int i = 0; i < P_1; i++)
		{
			array[i] = new DmColumn(P_3);
			num = P_0.a();
			int cType = P_0.l(num);
			int num2 = P_0.M(num);
			long num3 = P_0.m(num);
			int scale = (int)(num3 << 16) >> 16;
			bool nullable = P_0.i(num);
			short num4 = P_0.J(num);
			short inOutType = P_0.I(num);
			short len = P_0.j(num);
			short len2 = P_0.K(num);
			short len3 = P_0.k(num);
			short len4 = P_0.L(num);
			P_0.f(32);
			bool identity = (num4 & 1) == 1;
			bool flag = (num4 & 2) == 2;
			array[i].SetName(P_0, len);
			array[i].SetTypeName(P_0, len2);
			array[i].SetTable(P_0, len3);
			array[i].SetSchema(P_0, len4);
			if (P_4)
			{
				short num5 = 0;
				num5 = P_0.C();
				if (num5 > 0)
				{
					array[i].SetBaseColumn(P_0, num5);
				}
			}
			array[i].SetCType(cType);
			array[i].SetPrecision(num2);
			array[i].SetSize(DmSqlType.GetSizeByCType(cType, num2));
			array[i].SetScale(scale);
			array[i].SetNullable(nullable);
			array[i].SetIdentity(identity);
			array[i].SetIsLob(flag);
			array[i].SetInOutType(inOutType);
			if (flag)
			{
				array[i].SetTableID(P_0.d());
				array[i].SetColID(P_0.C());
			}
			if (array[i].type == 16 || array[i].type == 26)
			{
				if ((array[i].scale & 0x1000) != 0)
				{
					array[i].scale = array[i].scale & -4097;
					array[i].mask = 4;
				}
				else if ((array[i].scale & 0x2000) != 0)
				{
					array[i].scale = array[i].scale & -8193;
					array[i].mask = 1;
				}
			}
			if (array[i].type == 9 && array[i].scale == 129)
			{
				array[i].originalPrec = array[i].prec;
				array[i].prec = (int)(Math.Round((double)array[i].prec * 0.30103) + 1.0);
				array[i].scale = -1;
				array[i].mask = 2;
			}
			if (array[i].type == 2 && array[i].prec == 512 && num3 == 6)
			{
				array[i].mask = 3;
				array[i].scale = (int)num3;
			}
		}
		for (int j = 0; j < P_1; j++)
		{
			if (DmSqlType.isComplexType(array[j].GetCType(), array[j].GetScale()))
			{
				ComplexTypeDesc complexTypeDesc = new ComplexTypeDesc(P_2.ConnInstance.Conn);
				complexTypeDesc.Unpack(P_0);
				array[j].ComplexTypeDesc = complexTypeDesc;
			}
		}
		P_2.SetColumnsInfo(array);
		P_2.SetHasResultSet(hasResultSet: true);
	}

	public static void A(b P_0, A P_1)
	{
		if (P_0.L() < 0)
		{
			string text = A(P_1);
			A(P_0, P_1.G().ConnProperty.ServerEncoding, P_1.G().ConnProperty.RWStandby, text);
		}
		DmInfo dmInfo = new DmInfo(P_1.G());
		int num = P_0.j();
		int num2 = P_0.aa();
		int num3 = P_0.aB();
		int num4 = P_0.aA();
		dmInfo.SetRetStmtType(num4);
		P_1.G().trxStatus = P_0.ab();
		if (num4 == 200 || num4 == 201)
		{
			foreach (A stmt in P_1.G().Stmts)
			{
				if (stmt.g() == num)
				{
					P_1.A(stmt.l());
					break;
				}
			}
			return;
		}
		if (num2 > 0)
		{
			dmInfo.SetParaNum(num2);
			A(P_0, num2, ref dmInfo);
		}
		else
		{
			dmInfo.SetParaNum(0);
		}
		if (num3 > 0)
		{
			A(P_0, num3, dmInfo, P_1.G(), P_1.c());
		}
		P_1.A(dmInfo);
	}

	private static void a(b P_0, A P_1)
	{
		string serverEncoding = P_1.G().ConnProperty.ServerEncoding;
		bool newLobFlag = P_1.G().ConnProperty.NewLobFlag;
		DmField[] paramsInfo = P_1.F().GetParamsInfo();
		DmGetValue dmGetValue = new DmGetValue(serverEncoding, P_1, newLobFlag, paramsInfo);
		DmParameterInternal[] ParamsInfo = null;
		DmParameterInternal dmParameterInternal = null;
		DmParameter dmParameter = null;
		P_1.H().GetParamsInfo(out ParamsInfo);
		bool flag = ArrayUtil.IsAllMatch(P_1.f().Parameters, ParamsInfo);
		bool flag2 = P_1.G().ConnProperty.msgVersion >= 12;
		for (int i = 0; i < P_1.H().GetParameterCount(); i++)
		{
			dmParameterInternal = ParamsInfo[i];
			int sqlType = dmParameterInternal.GetSqlType();
			int scale = dmParameterInternal.GetScale();
			if (sqlType == 12 && scale == 5)
			{
				throw new InvalidOperationException("UNSUPPORT USERDEFINED TYPE");
			}
			dmParameter = (flag ? ((DmParameter)P_1.f().Parameters[dmParameterInternal.GetName()]) : ((DmParameter)P_1.f().Parameters[i]));
			if (dmParameter.do_Direction == ParameterDirection.Input)
			{
				continue;
			}
			if (flag2)
			{
				int num = A(P_0.F(4));
				for (int j = 0; j < num; j++)
				{
					A(dmParameter, P_0, P_1, dmParameterInternal, dmGetValue, i, j == 0);
				}
			}
			else
			{
				A(dmParameter, P_0, P_1, dmParameterInternal, dmGetValue, i, true);
			}
		}
	}

	private static int A(byte[] P_0)
	{
		return ByteUtil.getInt(P_0, 0) << 1 >> 1;
	}

	private static void A(DmParameter P_0, b P_1, A P_2, DmParameterInternal P_3, DmGetValue P_4, int P_5, bool P_6)
	{
		object obj = null;
		byte[] array = null;
		int num = P_1.D();
		if ((num == 65534 || num == 65533) && P_0.DmSqlType != DmDbType.Cursor && P_0.DmSqlType != DmDbType.RefCursor)
		{
			P_0.do_Value = DBNull.Value;
			return;
		}
		if (num >= 0 && num != 65534 && num != 65533)
		{
			array = P_1.F(num);
		}
		switch (P_0.DmSqlType)
		{
		case DmDbType.Cursor:
		case DmDbType.RefCursor:
			if (P_3.GetCType() == 120)
			{
				obj = P_3;
				if (P_0.do_Direction == ParameterDirection.ReturnValue)
				{
					P_2.h().A(P_2.f().RetRefCursorStmt, 1);
				}
				else
				{
					P_2.h().A(P_0.refCursorStmt, 1);
				}
			}
			break;
		case DmDbType.Blob:
		case DmDbType.Binary:
		case DmDbType.VarBinary:
			obj = P_4.GetBytes(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Bit:
		{
			bool boolean = P_4.GetBoolean(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			obj = ((P_2.G().ConnProperty.CompatibleMode != CompatibleMode.MYSQL) ? ((object)boolean) : ((object)(boolean ? 1 : 0)));
			break;
		}
		case DmDbType.Char:
		case DmDbType.Clob:
		case DmDbType.Text:
		case DmDbType.VarChar:
			obj = P_4.GetString(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Time:
			obj = P_4.GetTime(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Date:
			obj = P_4.GetDate(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.DateTime:
			obj = P_4.GetTimestamp(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.DateTimeOffset:
			obj = P_4.GetTimestampTZ(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.TimeOffset:
			obj = P_4.GetTimeTZ(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Decimal:
			obj = P_4.GetBigDecimal(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.XDEC:
			obj = P_4.GetDmDecimal(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Double:
			obj = P_4.GetDouble(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Float:
			obj = P_4.GetFloat(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.IntervalDayToSecond:
			obj = P_4.GetINTERVALDT(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.IntervalYearToMonth:
			obj = P_4.GetINTERVALYM(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int16:
			obj = P_4.GetShort(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int32:
			obj = P_4.GetInt(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int64:
			obj = P_4.GetLong(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.SByte:
			obj = P_4.GetSByte(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Byte:
			obj = P_4.GetByte(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt16:
			obj = P_4.GetUshort(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt32:
			obj = P_4.GetUint(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt64:
			obj = P_4.GetUlong(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.ARRAY:
			obj = P_4.ToComplexType(array, P_3, P_2.G().Conn);
			if (obj is DmArray)
			{
				ComplexTypeData[] arrData = ((DmArray)obj).m_arrData;
				if (arrData == null || arrData.Length == 0)
				{
					obj = null;
					break;
				}
				object[] array2 = new object[arrData.Length];
				for (int i = 0; i < arrData.Length; i++)
				{
					array2[i] = arrData[i].m_dumyData;
				}
				obj = array2;
			}
			else if (obj is DmStruct)
			{
				throw new NotSupportedException("GetParamData");
			}
			break;
		default:
			array = P_1.F(num);
			obj = P_4.GetObject(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		}
		if (P_6)
		{
			P_0.do_Value = obj;
		}
	}

	public static byte[] A(b P_0)
	{
		return P_0.F(P_0.k());
	}

	public static void A(b P_0, A P_1, DmConnProperty P_2)
	{
		int num = P_0.L();
		if (num < 0)
		{
			string text = A(P_1);
			A(P_0, P_2.ServerEncoding, P_2.RWStandby, text);
		}
		DmInfo dmInfo = new DmInfo(P_1.G());
		dmInfo.SetParaNum(P_1.F().GetParameterCount());
		dmInfo.SetParamsInfo(P_1.F().GetParamsInfo());
		int num2 = P_0.j();
		P_0.k();
		short num3 = P_0.aC();
		short num4 = P_0.ac();
		long num5 = P_0.aD();
		int num6 = P_0.ad();
		byte num7 = P_0.aE();
		int num8 = P_0.ae();
		int num9 = P_0.aF();
		DmRowId rowId = null;
		dmInfo.Execid = P_0.aH();
		int num10 = P_0.ah();
		DmConnInstance dmConnInstance = P_1.G();
		dmConnInstance.trxStatus = P_0.aI();
		dmConnInstance.do_setTrxFinish(dmConnInstance.trxStatus);
		short rsBdtaRowidCol = -1;
		if (num3 == 160 || num3 == 162)
		{
			dmInfo.rsBdta = P_0.aG() == 2;
			rsBdtaRowidCol = P_0.ag();
		}
		else if (P_2.msgVersion < 9 && (num3 == 157 || num3 == 159 || num3 == 158))
		{
			rowId = DmRowId.valueOf(P_0.A(43, 12));
		}
		dmInfo.SetRetStmtType(num3);
		dmInfo.SetOutParamNum(num6);
		if (num3 == 159 || num3 == 158 || num3 == 157 || num3 == 160 || num3 == 162 || num3 == 164)
		{
			dmInfo.SetRowCount(num5);
		}
		else
		{
			dmInfo.SetRowCount(-1L);
		}
		switch (num3)
		{
		case 157:
		case 158:
		case 159:
		case 162:
			dmInfo.SetRecordsAffected(num5);
			break;
		case 160:
			dmInfo.SetRecordsAffected(-1L);
			break;
		}
		if (num7 == 1)
		{
			dmInfo.SetUpdatable(val: true);
		}
		else
		{
			dmInfo.SetUpdatable(val: false);
		}
		if (num9 > 0)
		{
			string printMsg = P_0.A(num9, P_1.G().ConnProperty.ServerEncoding);
			dmInfo.SetPrintMsg(printMsg);
		}
		if (P_2.msgVersion >= 9 && (num3 == 157 || num3 == 159 || num3 == 200))
		{
			rowId = DmRowId.valueOf(P_0.F(12));
		}
		if (num6 > 0)
		{
			a(P_0, P_1);
		}
		int num11;
		switch (num3)
		{
		case 150:
		case 166:
		{
			short num13 = P_0.C();
			P_0.f(1);
			if (num3 == 166)
			{
				IsolationLevel trxISO = IsolationLevel.ReadCommitted;
				switch (num13)
				{
				case 1:
					trxISO = IsolationLevel.ReadCommitted;
					break;
				case 3:
					trxISO = IsolationLevel.Serializable;
					break;
				case 0:
					trxISO = IsolationLevel.ReadUncommitted;
					break;
				}
				P_1.G().SetTrxISO(trxISO);
				P_1.h().A(true);
			}
			else
			{
				if (P_1.f() != null)
				{
					P_1.f().SetStmtSerial(num13);
				}
				if (P_1.G().Transaction != null)
				{
					P_1.G().Transaction.SetStmtSerial(num13);
				}
			}
			break;
		}
		case 165:
			P_2.TimeZone = P_0.C();
			break;
		case 147:
			P_2.Commited = true;
			break;
		case 148:
			P_2.Commited = true;
			P_1.G().RestAllStmt();
			break;
		case 200:
		case 201:
			if (num != 107)
			{
				break;
			}
			foreach (A stmt in P_1.G().Stmts)
			{
				if (stmt.g() == num2)
				{
					P_1.A(stmt.l());
					break;
				}
			}
			P_1.h().A(P_1, P_1.l().statement.F());
			break;
		case 153:
		{
			int num14 = P_0.d();
			string currentSchema = P_0.A(num14, P_2.ServerEncoding);
			P_2.CurrentSchema = currentSchema;
			break;
		}
		case 160:
			dmInfo.SetHasResultSet(hasResultSet: true);
			goto IL_0474;
		case 162:
			if (num4 > 0 || num8 > 0)
			{
				dmInfo.SetHasResultSet(hasResultSet: true);
			}
			goto IL_0474;
		case 157:
		case 159:
			dmInfo.SetRowId(rowId);
			break;
		case 149:
			P_1.b(P_0.b(P_2.ServerEncoding));
			break;
		case 251:
			P_2.oracleDateFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 253:
			P_2.oracleTimestampFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 254:
			P_2.oracleTimestampTZFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 252:
			P_2.oracleTimeFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 255:
			P_2.oracleTimeTZFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 256:
			P_2.nlsDateLang = P_0.b();
			break;
		case 271:
			{
				P_2.formatNumericChars = P_0.A(2, P_2.ServerEncoding);
				break;
			}
			IL_0474:
			if (num4 != 0)
			{
				A(P_0, num4, dmInfo, P_1.G(), P_1.c());
				num11 = dmInfo.GetColumnsInfo().Length;
			}
			else if (P_1.F() != null)
			{
				DmColumn[] columnsInfo = P_1.F().GetColumnsInfo();
				dmInfo.SetColumnsInfo(columnsInfo);
				num11 = ((columnsInfo != null) ? columnsInfo.Length : 0);
			}
			else
			{
				num11 = 0;
			}
			if (P_1.l() == null)
			{
				P_1.A(new DmResultSetCache(P_1, num11, dmInfo.GetRowCount()));
			}
			else
			{
				P_1.l().SetCols(num11);
				P_1.l().totalRowCount = dmInfo.GetRowCount();
			}
			if (num8 > 0)
			{
				P_1.l().FillRows(0L, num8, P_0, dmInfo.rsBdta, rsBdtaRowidCol);
				if (P_1.G().ConnProperty.EnRsCache && num10 > 0 && dmInfo.GetRowCount() == num8)
				{
					RsKey key = new RsKey(P_1.G().ConnProperty.Guid, P_1.G().ConnProperty.CurrentSchema, P_1.C(), P_1.f().do_DbParameterCollection.Count, P_1.f().do_DbParameterCollection);
					DmConnection.rsLRUCache.Add(key, P_1.l());
				}
			}
			if (num10 > 0)
			{
				P_0.f(num10 - P_0.a());
				short num12 = P_0.C();
				int[] array = new int[num12];
				long[] array2 = new long[num12];
				for (int i = 0; i < num12; i++)
				{
					array[i] = P_0.d();
					array2[i] = P_0.e();
				}
				P_1.l().ids = array;
				P_1.l().tss = array2;
				P_1.l().lastCheckDt = DateTime.Now;
			}
			break;
		}
		P_1.A(dmInfo);
	}

	public static bool A(b P_0, long P_1, DmResultSetCache P_2)
	{
		int num = P_0.L();
		if (num < 0 && num != -7036)
		{
			A(P_0, P_2.statement.G().ConnProperty.ServerEncoding, P_2.statement.G().ConnProperty.RWStandby);
		}
		P_2.totalRowCount = P_0.ai();
		int num2 = P_0.aJ();
		P_2.FillRows(P_1, num2, P_0, P_2.isRsBdta, P_2.rsBdtaRowidCol);
		if (num2 > 0)
		{
			P_2.datasStartPos = P_1;
			P_2.datasOffset = 0;
			return true;
		}
		return false;
	}

	public static long a(b P_0, long P_1, DmResultSetCache P_2)
	{
		int num = P_0.L();
		if (num < 0 && num != -7036)
		{
			A(P_0, P_2.statement.G().ConnProperty.ServerEncoding, P_2.statement.G().ConnProperty.RWStandby);
		}
		return P_0.ai();
	}

	public static void a(b P_0, A P_1, DmConnProperty P_2)
	{
		if (P_0.L() == 111)
		{
			P_1.A(new DmInfo(P_1.G()));
		}
		else
		{
			A(P_0, P_1, P_2);
		}
	}

	public static long[] b(b P_0, DmConnProperty P_1)
	{
		if (P_0.L() < 0)
		{
			A(P_0, P_1.ServerEncoding, P_1.RWStandby);
		}
		int num = P_0.aj();
		if (num <= 0)
		{
			return null;
		}
		long[] array = new long[num];
		for (int i = 0; i < num; i++)
		{
			array[i] = P_0.e();
		}
		return array;
	}

	private static int A(string P_0)
	{
		int num = 0;
		char[] array = P_0.ToCharArray();
		int i = 0;
		if (array[i] == 'V')
		{
			i++;
		}
		int num2 = P_0.IndexOf('.');
		if (num2 == -1)
		{
			return 0;
		}
		int num3 = 0;
		for (; i < num2; i++)
		{
			if (array[i] < '0' || array[i] > '9')
			{
				return 0;
			}
			num3 = num3 * 10 + (array[i] - 48);
			if (num3 > 99)
			{
				return 0;
			}
		}
		i++;
		num = num3 << 24;
		P_0 = P_0.Substring(num2 + 1, P_0.Length - num2 - 1);
		int num4 = P_0.IndexOf('.');
		if (num4 == -1)
		{
			return 0;
		}
		num2++;
		num2 += num4;
		num3 = 0;
		for (; i < num2; i++)
		{
			if (array[i] < '0' || array[i] > '9')
			{
				return 0;
			}
			num3 = num3 * 10 + (array[i] - 48);
			if (num3 > 99)
			{
				return 0;
			}
		}
		i++;
		num += num3 << 16;
		P_0 = P_0.Substring(num4 + 1, P_0.Length - num4 - 1);
		num4 = P_0.IndexOf('.');
		if (num4 == -1)
		{
			return 0;
		}
		num2++;
		num2 += num4;
		num3 = 0;
		for (; i < num2; i++)
		{
			if (array[i] < '0' || array[i] > '9')
			{
				return 0;
			}
			num3 = num3 * 10 + (array[i] - 48);
			if (num3 > 99)
			{
				return 0;
			}
		}
		i++;
		num += num3 << 8;
		P_0 = P_0.Substring(num4 + 1, P_0.Length - num4 - 1);
		num4 = P_0.IndexOf('-');
		if (num4 == -1)
		{
			return 0;
		}
		num2++;
		num2 += num4;
		num3 = 0;
		for (; i < num2; i++)
		{
			if (array[i] < '0' || array[i] > '9')
			{
				return 0;
			}
			num3 = num3 * 10 + (array[i] - 48);
			if (num3 > 999)
			{
				return 0;
			}
		}
		i++;
		return num + (num3 & 0xFF);
	}

	private static int a(string P_0)
	{
		return A(P_0);
	}

	private static void A(b P_0, string P_1, bool P_2)
	{
		DmError dmError = new DmError();
		dmError.State = P_0.L();
		P_0.A(dmError, P_1);
		if (P_2)
		{
			dmError.Message = "[S]" + dmError.Message;
		}
		DmError.ThrowDmException(dmError);
	}

	private static void A(b P_0, string P_1, bool P_2, string P_3)
	{
		DmError dmError = new DmError();
		dmError.State = P_0.L();
		P_0.A(dmError, P_1);
		dmError.Message += P_3;
		if (P_2)
		{
			dmError.Message = "[S]" + dmError.Message;
		}
		DmError.ThrowDmException(dmError);
	}

	private static string A(A P_0)
	{
		if (!P_0.G().ConnProperty.ShowExtraInfo)
		{
			return "";
		}
		string text = " [sql]: {" + P_0.C() + "};";
		DmParameterCollection parameters = P_0.f().Parameters;
		if (parameters != null && parameters.Count > 0)
		{
			text += " [params]: ";
			for (int i = 0; i < parameters.Count; i++)
			{
				text = text + "{" + parameters[i].ParameterName + "=" + parameters[i].Value?.ToString() + "}";
				if (i != parameters.Count - 1)
				{
					text += " ";
				}
			}
			text += ";";
		}
		return text;
	}
}
