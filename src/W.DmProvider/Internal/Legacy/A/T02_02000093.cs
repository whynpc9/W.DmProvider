using System;
using System.Collections.Generic;
using System.Data;
using W.Dm;
using W.Dm.Config;
using W.Dm.parser;
using W.Dm.util;

namespace W.Dm.Internal.Legacy.A;

internal partial class C
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
		P_0.__t02_method_06000B0B((byte)(P_1.LoginEncrypt ? 2 : 0));
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
		if (P_1.encryptPwd && P_2.__t02_field_04000AB4 != null)
		{
			array = P_2.__t02_field_04000AB4.Encrypt(array, genDigest: false);
			array2 = P_2.__t02_field_04000AB4.Encrypt(array2, genDigest: false);
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
		P_0.__t02_method_06000B3B(0);
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
			var streaming = dmParameterInternal.GetStreamingInput(P_4);
			if (streaming != null)
			{
				if (num != streaming.WireType || P_3.G().ConnProperty.msgVersion < 10)
					throw new NotSupportedException("Streaming input requires matching modern LOB parameter metadata.");
				using var cursor = P_3.h().OpenStreamingParameterCursor(P_3, streaming);
				int firstCount = cursor.ReadChunk(global::W.Dm.Internal.Sessions.DmInvocation.Current?.CancellationToken ??
					throw new InvalidOperationException("Input upload has no invocation."));
				if (!P_3.E()) { P_3.h().A(P_0, P_3, P_2); P_3.b(true); }
				byte[] token = P_3.h().UploadStreamingParameter(P_3, i, streaming, cursor, firstCount);
				P_1.h(65529); P_1.A(token);
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
						P_3.h().__t02_method_06000A9A(P_3, i, dmParameterInternal, P_4);
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
			P_0.__t02_method_06000B47(ParamsInfo[0].GetParamValue().Count);
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
			P_1.__t02_method_06000B47(1L);
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
		P_0.__t02_method_06000B47(1L);
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
