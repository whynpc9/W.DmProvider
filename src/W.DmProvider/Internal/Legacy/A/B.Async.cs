using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.util;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Protocol;
namespace W.Dm.Internal.Legacy.A;
internal partial class B
{
	// The coordinator records the first cause; a later caller-token state must
	// never replace a deadline or Command.Cancel classification.
	internal static void CheckAsyncTermination(CancellationToken cancellationToken)
	{
		DmInvocation invocation = DmInvocation.Current;
		if (invocation != null) invocation.ThrowIfTerminated();
		else cancellationToken.ThrowIfCancellationRequested();
	}
	internal static CancellationToken AsyncCancellationToken(CancellationToken cancellationToken)
	{
		CheckAsyncTermination(cancellationToken);
		return DmInvocation.Current?.CancellationToken ?? cancellationToken;
	}
	private async Task<T> WireAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		DmInvocation invocation = DmInvocation.Current ?? throw new InvalidOperationException("Wire operation has no invocation.");
		DmSession session = invocation.Lease.Session;
		B prior = decodeOwner;
		try
		{
			if (DmWireExchange.Current != null)
			{
				session.RequireActiveWireExchange();
				if (!ReferenceEquals(decodeOwner,this) && !ReferenceEquals(parameterUploadOwner,this))
					throw new InvalidOperationException("Nested wire operation has no protocol continuation owner.");
				try { return await operation().ConfigureAwait(false); }
				catch (Exception error)
				{
					if (invocation.ShouldAbortAfterFailure(error)) BreakNestedExchange(session);
					throw;
				}
			}
			using var exchange = session.BeginWireExchange();
			DmWireTestHooks.ExchangeEntered();
			T result = await operation().ConfigureAwait(false);
			exchange.Complete();
			return result;
		}
		catch (Exception error)
		{
			Exception translated = invocation.TranslateFailure(error);
			if (ReferenceEquals(translated, error)) throw;
			throw translated;
		}
		finally { decodeOwner = prior; }
	}
	private Task WireAsync(Func<Task> operation,CancellationToken cancellationToken) =>
		WireAsync(async () => { await operation().ConfigureAwait(false); return true; }, cancellationToken);
	private async Task EncodeWithParameterUploadsAsync(Func<Task> encode)
	{
		B prior = parameterUploadOwner;
		parameterUploadOwner = this;
		try
		{
			CheckAsyncTermination(default);
			await encode().ConfigureAwait(false);
			CheckAsyncTermination(default);
		}
		finally { parameterUploadOwner = prior; }
	}
	private async Task<b> ExchangeAsync(b send,b receive,int timeout,CancellationToken cancellationToken)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		C();
		DmWireTestHooks.HandshakeFrameEncoded(send.I());
		await A().SendFrameAsync(send,timeout,a().crcBody,a().encryptMsg,cancellationToken).ConfigureAwait(false);
		DmWireTestHooks.FrameSent(send.I());
		DmWireTestHooks.Sent();
		await A().ReadFrameAsync(receive,timeout,a().crcBody,a().encryptMsg,cancellationToken).ConfigureAwait(false);
		return receive;
	}
	internal async Task OpenAsync(DmDeadline deadline,CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		try
		{
			__t02_field_04000ABD = true;
			if (a().Compress != 0) throw new NotSupportedException("Protocol compression is not supported.");
			await A().OpenAsync(deadline,cancellationToken).ConfigureAwait(false);
			await WireAsync(async () => {
				global::W.Dm.Internal.Legacy.A.C.A(handshakeSend,a(),A());
				await ExchangeAsync(handshakeSend,handshakeReceive,a().ConnectionTimeout,cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(handshakeSend.I(), handshakeReceive);
		DmWireTestHooks.ResponseReady();
				global::W.Dm.Internal.Legacy.A.c.a(handshakeReceive,a());
			},cancellationToken).ConfigureAwait(false);
			DmWireTestHooks.StartupNegotiatedEncryptMode(a().Encrypt);
			DmConnectionSettings settings = __t02_field_04000ABA.Conn.Settings;
			bool requiresTls = DmHandshakeSecurityGuard.RequiresFullTls(a().Encrypt,settings.TransportSecurity);
			DmHandshakeSecurityGuard.ValidateMessageSecurity(a());
			if (requiresTls) await A().UpgradeTlsAsync(DmTlsOptions.FromSettings(settings),deadline,cancellationToken).ConfigureAwait(false);
			await WireAsync(async () => {
				global::W.Dm.Internal.Legacy.A.C.a(handshakeSend,a(),A());
				await ExchangeAsync(handshakeSend,handshakeReceive,a().ConnectionTimeout,cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(handshakeSend.I(), handshakeReceive);
		DmWireTestHooks.ResponseReady();
				global::W.Dm.Internal.Legacy.A.c.B(handshakeReceive,a());
			},cancellationToken).ConfigureAwait(false);
			__t02_field_04000ABD = false;
			__t02_field_04000ABC = a().SocketTimeout;
			if(a().msgVersion < 10) a().lobOffRowLen = 2048;
		}
		catch { __t02_field_04000AC1?.C(); throw; }
	}
	private Task<T> MessageAsync<T>(MSG<T> message,CancellationToken cancellationToken) => WireAsync(async () =>
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		message.encode();
		CheckAsyncTermination(cancellationToken);
		a(message);
		message.setCRC();
		DmFrameWriter.Validate(__t02_field_04000AB9);
		DmWireTestHooks.HandshakeFrameEncoded(message.cmd);
		await A().SendAllAsync(__t02_field_04000AB9.A(),0,DmFrameWriter.Validate(__t02_field_04000AB9),CurrentMessageIdleTimeout(),cancellationToken).ConfigureAwait(false);
		DmWireTestHooks.FrameSent(message.cmd);
		DmWireTestHooks.Sent();
		A().ConfigureReadTimeout(CurrentMessageIdleTimeout());
		await DmFrameReader.ReadAsync(A().ReceiveExactlyAsync,__t02_field_04000AB9,
			(frame,total)=>DmFrameReader.ValidateChecksum(frame,total,a().crcBody),DmInvocation.Current.Deadline,cancellationToken,
			header=>(a().crcBody && DmFrameReader.Command(header)!=200)||DmFrameReader.ValidateHeaderChecksum(header),message.MaxResponseBodyLength).ConfigureAwait(false);
		message.checkCRC();
		DmResultProtocolTrace.RecordFrame(message.cmd,__t02_field_04000AB9);
		C(message);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		return message.decode();
	},cancellationToken);
	internal Task<long> GetLobLengthAsync(AbstractLob lob,CancellationToken cancellationToken = default) => MessageAsync(new GET_LOB_LEN(this,lob),cancellationToken);
	internal Task<Data> ReadLobAsync(AbstractLob lob,long start,int length,CancellationToken cancellationToken = default) => MessageAsync(new GET_LOB_DATA(this,lob,start,length),cancellationToken);
	internal async Task AAsync(b P_0, b P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, a(), A());
		await ExchangeAsync(P_0, P_1, a().ConnectionTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		global::W.Dm.Internal.Legacy.A.c.a(P_1, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(b P_0, b P_1, A P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		if (!__t02_field_04000ABD)
		{
			global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.__t02_method_06000883());
			await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
		}
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		if (P_4)
		{
			await global::W.Dm.Internal.Legacy.A.c.AAsync(P_1, P_2, a(), P_0.I(), executionVsPrepare: true, cancellationToken: cancellationToken).ConfigureAwait(false);
			P_2.d(false);
		}
		else
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.__t02_method_0600088A(true);
		}
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5, bool P_6, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		if (P_4)
		{
			await global::W.Dm.Internal.Legacy.A.c.AAsync(P_1, P_2, a(), P_0.I(), executionVsPrepare: true, cancellationToken: cancellationToken).ConfigureAwait(false);
			P_2.d(false);
		}
		else
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.__t02_method_0600088A(true);
		}
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(A P_0, DmInfo P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		b _t02_field_ = P_0.__t02_field_04000925;
		b _t02_field_2 = P_0.__t02_field_04000926;
		if (a().msgVersion >= 26 || !await ExecuteLegacyRowsAsync(_t02_field_, _t02_field_2, P_0, P_1, cancellationToken).ConfigureAwait(false))
		{
			await EncodeWithParameterUploadsAsync(() => global::W.Dm.Internal.Legacy.A.C.AAsync(_t02_field_, P_0, P_1, a(), cancellationToken)).ConfigureAwait(false);
			await ExchangeAsync(_t02_field_, _t02_field_2, P_0.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(_t02_field_.I(), _t02_field_2);
		DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			await global::W.Dm.Internal.Legacy.A.c.AAsync(_t02_field_2, P_0, a(), _t02_field_.I(), executionVsPrepare: true, cancellationToken: cancellationToken).ConfigureAwait(false);
			P_0.d(false);
		}
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(int P_0, A P_1, DmParameterInternal[] P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(P_0, b2, P_1.g(), P_2);
		await ExchangeAsync(b2, b3, P_1.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(b2.I(), b3);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		global::W.Dm.Internal.Legacy.A.c.A(b3, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task<bool> AAsync(A P_0, DmResultSetCache P_1, short P_2, long P_3, long P_4, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		return await WireAsync(async () =>
		{
		b _t02_field_ = P_0.__t02_field_04000925;
		b _t02_field_2 = P_0.__t02_field_04000926;
		global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, P_0.g(), P_3, P_2, P_4, P_0.G().ConnProperty.BufPrefetch);
		await ExchangeAsync(_t02_field_, _t02_field_2, P_0.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(_t02_field_.I(), _t02_field_2);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		return global::W.Dm.Internal.Legacy.A.c.A(_t02_field_2, P_3, P_1);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task __t02_method_06000A82Async(b P_0, b P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
			global::W.Dm.Internal.Legacy.A.C.a(P_0, 8, 0);
			await ExchangeAsync(P_0, P_1, __t02_field_04000ABC, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			ConfirmLocalTransactionResponse(P_1, DmTransactionControlKind.Commit, 8);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task bAsync(b P_0, b P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
			global::W.Dm.Internal.Legacy.A.C.a(P_0, 9, 0);
			await ExchangeAsync(P_0, P_1, __t02_field_04000ABC, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			ConfirmLocalTransactionResponse(P_1, DmTransactionControlKind.Rollback, 9);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(A P_0, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.a(P_0.__t02_field_04000925, P_0.__t02_method_06000883());
		await ExchangeAsync(P_0.__t02_field_04000925, P_0.__t02_field_04000926, P_0.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.__t02_field_04000925.I(), P_0.__t02_field_04000926);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		global::W.Dm.Internal.Legacy.A.c.A(P_0.__t02_field_04000926, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(b P_0, b P_1, A P_2, string P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2, P_3, a());
		await ExchangeAsync(P_2.__t02_field_04000925, P_2.__t02_field_04000926, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_2.__t02_field_04000925.I(), P_2.__t02_field_04000926);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		global::W.Dm.Internal.Legacy.A.c.A(P_2.__t02_field_04000926, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(b P_0, b P_1, A P_2, short P_3, byte[] P_4, int P_5, int P_6, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task<byte[]> AAsync(b P_0, b P_1, A P_2, int P_3, byte[] P_4, int P_5, byte[] P_6, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		return await WireAsync(async () =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		return global::W.Dm.Internal.Legacy.A.c.A(P_1);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task AAsync(A P_0, short P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		await WireAsync(async () =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.g(), P_1);
		await ExchangeAsync(b2, b3, P_0.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(b2.I(), b3);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		await global::W.Dm.Internal.Legacy.A.c.aAsync(b3, P_0, a(), cancellationToken).ConfigureAwait(false);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task<long[]> AAsync(DmResultSetCache P_0, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		return await WireAsync(async () =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.ids);
		await ExchangeAsync(b2, b3, __t02_field_04000ABC, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(b2.I(), b3);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		return global::W.Dm.Internal.Legacy.A.c.b(b3, a());
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task<long> aAsync(DmResultSetCache P_0, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		return await WireAsync(async () =>
		{
		A statement = P_0.statement;
		long num = long.MaxValue;
		int num2 = 1;
		b _t02_field_ = statement.__t02_field_04000925;
		b _t02_field_2 = statement.__t02_field_04000926;
		global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, statement.g(), num, 0, num2, statement.G().ConnProperty.BufPrefetch);
		await ExchangeAsync(_t02_field_, _t02_field_2, statement.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(_t02_field_.I(), _t02_field_2);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
		return global::W.Dm.Internal.Legacy.A.c.a(_t02_field_2, num, P_0);
			}, cancellationToken).ConfigureAwait(false);
	}
	public async Task<DmInfo> MoreResultsAsync(A P_0, DmInfo P_1, short P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		return await WireAsync(async () =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.g(), P_2);
		await ExchangeAsync(b2, b3, P_0.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(b2.I(), b3);
		DmWireTestHooks.ResponseReady();
		decodeOwner = this;
			await global::W.Dm.Internal.Legacy.A.c.AAsync(b3, P_0, a(), b2.I(), executionVsPrepare: true, cancellationToken: cancellationToken).ConfigureAwait(false);
		return P_0.F();
			}, cancellationToken).ConfigureAwait(false);
	}
	private async Task<bool> ExecuteLegacyRowsAsync(b P_0, b P_1, A P_2, DmInfo P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		DmParameterInternal[] ParamsInfo = null;
		P_3.GetParamsInfo(out ParamsInfo);
		int num = 0;
		if (ParamsInfo != null)
		{
			num = P_3.GetParameterCount();
		}
		bool flag = false;
		for (int i = 0; i < num; i++)
		{
			CheckAsyncTermination(cancellationToken);
			if (ParamsInfo[i].GetCType() == 12 || ParamsInfo[i].GetCType() == 19)
			{
				for (int j = 0; j < ParamsInfo[i].GetParamValue().Count; j++)
				{
					CheckAsyncTermination(cancellationToken);
					if (ParamsInfo[i].GetInBytes(j) != null && ParamsInfo[i].GetInBytes(j).Length >= a().lobOffRowLen)
					{
						flag = true;
						break;
					}
				}
			}
			if (flag)
			{
				break;
			}
		}
		if (flag)
		{
			for (int k = 0; k < ParamsInfo[0].GetParamValue().Count; k++)
			{
				CheckAsyncTermination(cancellationToken);
				await EncodeWithParameterUploadsAsync(() => global::W.Dm.Internal.Legacy.A.C.AAsync(k, P_0, P_2, P_3, a(), cancellationToken)).ConfigureAwait(false);
				await ExchangeAsync(P_0, P_1, P_2.G().ConnProperty.SocketTimeout, cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
		DmWireTestHooks.ResponseReady();
				decodeOwner = this;
				await global::W.Dm.Internal.Legacy.A.c.AAsync(P_1, P_2, a(), P_0.I(), executionVsPrepare: true, cancellationToken: cancellationToken).ConfigureAwait(false);
			}
			P_2.d(false);
			return true;
		}
		return false;
	}
internal Task<bool> AllocateStatementAsync(A statement,b send,b receive,bool updated,CancellationToken cancellationToken = default) => WireAsync(async () =>
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		global::W.Dm.Internal.Legacy.A.C.A(send,(short)3,0);
		await ExchangeAsync(send,receive,__t02_field_04000ABC,cancellationToken).ConfigureAwait(false);
		DmResultProtocolTrace.RecordFrame(send.I(), receive);
		DmWireTestHooks.ResponseReady();
		statement.__t02_method_06000884(global::W.Dm.Internal.Legacy.A.c.A(receive,a(),ref updated));
		return updated;
	},cancellationToken);
	public async Task<byte[]> ReadLobAsync(DmBlob P_0, long P_1, int P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		if (P_2 < 0) throw new ArgumentOutOfRangeException(nameof(P_2));
		if (a().MaxLobDataLenPerMsg <= 0) throw new InvalidDataException("Invalid negotiated LOB chunk size.");
		byte[] array = new byte[P_2];
		int num = 0;
		int num2 = 0;
		byte[] array2 = null;
		while (num < P_2)
		{
			CheckAsyncTermination(cancellationToken);
			num2 = P_2 - num;
			if (num2 > a().MaxLobDataLenPerMsg)
			{
				num2 = a().MaxLobDataLenPerMsg;
			}
			array2 = (await ReadLobAsync((AbstractLob)P_0, P_1 + num, num2, cancellationToken).ConfigureAwait(false)).value;
			if (array2 == null || array2.Length == 0)
			{
				break;
			}
			if (array2.Length > num2) throw new InvalidDataException("LOB chunk exceeds requested length.");
			ByteUtil.setBytes(array, num, array2);
			num += array2.Length;
			if (P_0.readOver)
			{
				break;
			}
		}
		return array;
	}
	public async Task<string> ReadLobAsync(DmClob P_0, long P_1, int P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		byte[] array = null;
		string text = null;
		while (num < P_2)
		{
			CheckAsyncTermination(cancellationToken);
			num2 = P_2 - num;
			if (num2 > a().MaxLobDataLenPerMsg)
			{
				num2 = a().MaxLobDataLenPerMsg;
			}
			Data data = await ReadLobAsync((AbstractLob)P_0, P_1 + num, num2, cancellationToken).ConfigureAwait(false);
			array = data.value;
			if (array == null || array.Length == 0)
			{
				break;
			}
			text = ByteUtil.getString(array, 0, array.Length, P_0.serverEncoding);
			stringBuilder.append(text);
			num += (int)((data.len == -1) ? text.length() : data.len);
			if (P_0.readOver)
			{
				break;
			}
		}
		return stringBuilder.ToString();
	}
	public async Task<int> WriteLobAsync(AbstractLob P_0, long P_1, string P_2, string P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		byte[] array = ByteUtil.fromString(P_2, P_3);
		int num = 0;
		int num2 = array.Length;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = num2 / a().MaxLobDataLenPerMsg + 1;
		byte b2 = 0;
		byte b3 = 1;
		byte b4 = 2;
		for (int i = 0; i < num6; i++)
		{
			CheckAsyncTermination(cancellationToken);
			b2 = 0;
			if (i == 0)
			{
				b2 |= b3;
			}
			if (i == num6 - 1)
			{
				b2 |= b4;
			}
			num5 = num2 - num4;
			if (num5 > a().MaxLobDataLenPerMsg)
			{
				num5 = a().MaxLobDataLenPerMsg;
			}
			if (num + num5 != num2)
			{
				num5 = DriverUtil.checkCompleteCharLen(array, num, num5, P_3);
				if (num5 <= 0)
				{
					throw new Exception("invalid Object Blob Data");
				}
			}
			int num7 = await MessageAsync(new SET_LOB_DATA(this, P_0, b2, P_1, array, num, num5), cancellationToken).ConfigureAwait(false);
			if (num7 <= 0)
			{
				return num3;
			}
			P_1 += num7;
			num3 += num7;
			num4 += num5;
			num += num5;
		}
		return num3;
	}
	public async Task<int> WriteLobAsync(AbstractLob P_0, long P_1, byte[] P_2, int P_3, int P_4, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = P_4 / a().MaxLobDataLenPerMsg + 1;
		byte b2 = 0;
		byte b3 = 1;
		byte b4 = 2;
		for (int i = 0; i < num4; i++)
		{
			CheckAsyncTermination(cancellationToken);
			b2 = 0;
			if (i == 0)
			{
				b2 |= b3;
			}
			if (i == num4 - 1)
			{
				b2 |= b4;
			}
			num3 = P_4 - num2;
			if (num3 > a().MaxLobDataLenPerMsg)
			{
				num3 = a().MaxLobDataLenPerMsg;
			}
			int num5 = await MessageAsync(new SET_LOB_DATA(this, P_0, b2, P_1, P_2, P_3, num3), cancellationToken).ConfigureAwait(false);
			if (num5 <= 0)
			{
				return num;
			}
			P_1 += num5;
			num += num5;
			num2 += num3;
			P_3 += num3;
		}
		return num;
	}
	public async Task UploadLegacyTextAsync(A P_0, int P_1, DmParameterInternal P_2, int P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		int maxLobDataLenPerMsg;
		int num = (maxLobDataLenPerMsg = P_0.G().ConnProperty.MaxLobDataLenPerMsg) / 4;
		int num2 = 0;
		b b2 = new b();
		b b3 = new b();
		byte[] array = new byte[maxLobDataLenPerMsg];
		string text = DmConvertion.GetString(P_2.GetParamValue()[P_3].m_InValue, 0, P_2.GetParamValue()[P_3].GetStreamLen(), a().ServerEncoding);
		int num3 = text.Length;
		while (num3 > 0)
		{
			CheckAsyncTermination(cancellationToken);
			if (num3 < num)
			{
				array = DmConvertion.GetBytes(text.Substring(num2, num3), a().ServerEncoding);
				await AAsync(b2, b3, P_0, (short)P_1, array, array.Length, num3, cancellationToken).ConfigureAwait(false);
				return;
			}
			array = DmConvertion.GetBytes(text.Substring(num2, num), a().ServerEncoding);
			await AAsync(b2, b3, P_0, (short)P_1, array, array.Length, num, cancellationToken).ConfigureAwait(false);
			num3 -= num;
			num2 += num;
		}
		await AAsync(b2, b3, P_0, (short)P_1, array, array.Length, 0, cancellationToken).ConfigureAwait(false);
	}
	public async Task UploadLegacyBinaryAsync(A P_0, int P_1, DmParameterInternal P_2, int P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		int maxLobDataLenPerMsg = P_0.G().ConnProperty.MaxLobDataLenPerMsg;
		if (maxLobDataLenPerMsg <= 0) throw new InvalidDataException("Invalid negotiated LOB chunk size.");
		int num = 0;
		b b2 = new b();
		b b3 = new b();
		byte[] val = new byte[maxLobDataLenPerMsg];
		int bytes;
		for (bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3); bytes > 0; bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3))
		{
			CheckAsyncTermination(cancellationToken);
			if (bytes < maxLobDataLenPerMsg)
			{
				await AAsync(b2, b3, P_0, (short)P_1, val, bytes, int.MinValue, cancellationToken).ConfigureAwait(false);
				return;
			}
			await AAsync(b2, b3, P_0, (short)P_1, val, maxLobDataLenPerMsg, int.MinValue, cancellationToken).ConfigureAwait(false);
			num += maxLobDataLenPerMsg;
		}
		await AAsync(b2, b3, P_0, (short)P_1, val, bytes, int.MinValue, cancellationToken).ConfigureAwait(false);
	}
	public async Task<byte[]> UploadParameterAsync(A P_0, int P_1, DmParameterInternal P_2, int P_3, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncCancellationToken(cancellationToken);
		int maxLobDataLenPerMsg = P_0.G().ConnProperty.MaxLobDataLenPerMsg;
		if (maxLobDataLenPerMsg <= 0) throw new InvalidDataException("Invalid negotiated LOB chunk size.");
		int num = 0;
		b b2 = new b();
		b b3 = new b();
		byte[] val = new byte[maxLobDataLenPerMsg];
		int bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3);
		byte[] array = global::W.Dm.Internal.Legacy.A.C.A();
		while (bytes > 0)
		{
			CheckAsyncTermination(cancellationToken);
			if (bytes < maxLobDataLenPerMsg)
			{
				return await AAsync(b2, b3, P_0, P_1, val, bytes, array, cancellationToken).ConfigureAwait(false);
			}
			array = await AAsync(b2, b3, P_0, P_1, val, maxLobDataLenPerMsg, array, cancellationToken).ConfigureAwait(false);
			num += maxLobDataLenPerMsg;
			bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3);
		}
		return await AAsync(b2, b3, P_0, (short)P_1, val, bytes, array, cancellationToken).ConfigureAwait(false);
	}
}
