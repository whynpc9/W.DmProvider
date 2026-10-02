using System;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Protocol;
using AsyncProtocol = W.Dm.Internal.Legacy.A.B;
namespace W.Dm.Internal.Legacy.A;
internal partial class D
{
	internal async Task OpenAsync(DmDeadline deadline, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireConnectOwnership();
		await transport.OpenAsync(deadline, cancellationToken).ConfigureAwait(false);
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireConnectOwnership();
		__t02_field_04000AAD = false;
	}
	internal async Task UpgradeTlsAsync(DmTlsOptions options, DmDeadline deadline, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireConnectOwnership();
		await transport.UpgradeTlsAsync(options, deadline, cancellationToken).ConfigureAwait(false);
		AsyncProtocol.CheckAsyncTermination(cancellationToken);
		RequireConnectOwnership();
	}
	internal ValueTask SendAllAsync(byte[] buffer,int offset,int count,int timeout,CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireWireExchange();
		return transport.SendAllAsync(buffer,offset,count,ActiveDeadline(timeout),timeout,cancellationToken,DmWireTestHooks.RecordSentBytes);
	}
	internal ValueTask ReceiveExactlyAsync(byte[] buffer,int offset,int count,CancellationToken cancellationToken)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireWireExchange();
		return transport.ReadExactlyAsync(buffer,offset,count,ActiveDeadline(readTimeout),readTimeout,cancellationToken);
	}
	internal async Task SendFrameAsync(b P_0, int P_1, bool P_2, bool P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		int num = P_0.J() + 64;
		if (P_3 && num > 64)
		{
			byte[] array = __t02_field_04000AB3.Encrypt(P_0.A(64, num - 64), genDigest: true);
			P_0.A(64);
			P_0.A(array);
			P_0.i();
			num = P_0.J() + 64;
		}
		if (P_2 && P_0.I() != 200)
		{
			P_0.n(checked(P_0.J() + 4));
			int num2 = A(P_0, 0, num);
			P_0.H(num2);
		}
		else
		{
			P_0.i();
			P_0.a(P_0.H());
		}
		AsyncProtocol.CheckAsyncTermination(cancellationToken);
		byte[] array2 = P_0.A();
		await SendAllAsync(array2, 0, DmFrameWriter.Validate(P_0), P_1, cancellationToken).ConfigureAwait(false);
	}
	internal async Task<b> ReadFrameAsync(b P_0, int P_1, bool P_2, bool P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		RequireWireExchange();
		readTimeout = P_1;
		int num3 = await DmFrameReader.ReadAsync(ReceiveExactlyAsync, P_0,
			(frame, total) => DmFrameReader.ValidateChecksum(frame, total, P_2), ActiveDeadline(P_1), cancellationToken,
			header => (P_2 && DmFrameReader.Command(header) != 200) || DmFrameReader.ValidateHeaderChecksum(header)).ConfigureAwait(false);
		if (P_2 && P_0.I() != 200)
		{
			int num4 = num3 - 4;
			int num5 = P_0.d(num4);
			int num6 = A(P_0, 0, num4);
			if (num5 != num6)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_CRC_CHECK_FAIL_);
			}
			P_0.n(num4 - 64);
			P_0.A(num4);
		}
		else
		{
			byte num7 = P_0.H();
			byte b2 = P_0.__t02_method_06000AB4(19);
			if (num7 != b2)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_CRC_CHECK_FAIL_);
			}
		}
		if (P_3 && num3 > 64)
		{
			byte[] ciphertext = P_0.A(64, num3 - 64);
			byte[] array = __t02_field_04000AB3.Decrypt(ciphertext, checkDigest: true);
			P_0.A(64);
			P_0.A(array);
			P_0.n(array.Length);
		}
		return P_0;
	}
}
