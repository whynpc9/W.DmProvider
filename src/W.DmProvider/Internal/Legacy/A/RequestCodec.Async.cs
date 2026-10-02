using System;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.util;
using AsyncProtocol = W.Dm.Internal.Legacy.A.B;
namespace W.Dm.Internal.Legacy.A;
internal partial class C
{
	private static async Task AAsync(int P_0, b P_1, DmParameterInternal[] P_2, A P_3, int P_4, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		DmParameterInternal dmParameterInternal = null;
		byte[] InValue = null;
		for (int i = 0; i < P_0; i++)
		{
			AsyncProtocol.CheckAsyncTermination(cancellationToken);
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
					await P_3.h().AAsync(P_0, P_3, P_2, cancellationToken).ConfigureAwait(false);
					P_3.b(true);
				}
				if (num == 12)
				{
					if (P_3.G().ConnProperty.msgVersion >= 10)
					{
						byte[] array = await P_3.h().UploadParameterAsync(P_3, i, dmParameterInternal, P_4, cancellationToken).ConfigureAwait(false);
						num2 = 65529;
						P_1.h(num2);
						P_1.A(array);
					}
					else
					{
						P_1.h(num2);
						await P_3.h().UploadLegacyBinaryAsync(P_3, i, dmParameterInternal, P_4, cancellationToken).ConfigureAwait(false);
					}
				}
				else if (P_3.G().ConnProperty.msgVersion >= 10)
				{
					byte[] array2 = await P_3.h().UploadParameterAsync(P_3, i, dmParameterInternal, P_4, cancellationToken).ConfigureAwait(false);
					num2 = 65529;
					P_1.h(num2);
					P_1.A(array2);
				}
				else
				{
					P_1.h(num2);
					await P_3.h().UploadLegacyTextAsync(P_3, i, dmParameterInternal, P_4, cancellationToken).ConfigureAwait(false);
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
	public static async Task AAsync(b P_0, A P_1, DmInfo P_2, DmConnProperty P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
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
				AsyncProtocol.CheckAsyncTermination(cancellationToken);
				await AAsync(num, P_0, ParamsInfo, P_1, i, cancellationToken).ConfigureAwait(false);
			}
		}
		P_0.i();
		if (P_3.msgVersion >= 10 && P_0.J() > 536838144)
		{
			throw new DmException("消息超长，调整batchSize大小!");
		}
	}
	public static async Task AAsync(int P_0, b P_1, A P_2, DmInfo P_3, DmConnProperty P_4, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
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
			await AAsync(num, P_1, ParamsInfo, P_2, P_0, cancellationToken).ConfigureAwait(false);
		}
		P_1.i();
		if (P_4.msgVersion >= 10 && P_1.J() > 536838144)
		{
			throw new DmException("消息超长，调整batchSize大小!");
		}
	}
}
