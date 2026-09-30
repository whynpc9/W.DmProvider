using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Transport;
using W.Dm.filter.log;
using W.Dm.parser;
using W.Dm.util;

namespace W.Dm.Internal.Legacy.A;

// T08 observation points. Only fixed header and decoded result metadata may leave
// the codec; callbacks run inside the owned exchange so failures break the session.
internal static class DmResultProtocolTrace
{
	[ThreadStatic] private static short currentRequestOpcode;
	internal static Action<short, short, int, int> AfterFrame;
	internal static Action<short, int, long, long, int, bool, bool> AfterStatementDecode;
	internal static short CurrentRequestOpcode => currentRequestOpcode;
	internal static void RecordFrame(short requestOpcode, b response)
	{
		currentRequestOpcode = requestOpcode;
		Volatile.Read(ref AfterFrame)?.Invoke(requestOpcode, response.I(), response.L(), response.k());
	}
	internal static void RecordStatement(DmInfo info)
	{
		Volatile.Read(ref AfterStatementDecode)?.Invoke(currentRequestOpcode,
			info.GetRetStmtType(), info.GetRowCount(), info.GetRecordsAffected(),
			info.GetColumnCount(), info.GetHasResultSet(), info.IsTerminal);
	}
}

// Transaction diagnostics expose only numeric structure, never diagnostic text
// or frame bytes. Observation does not establish an ACK or change frame policy.
internal static class DmTransactionProtocolTrace
{
	internal static Action<OperationIdentity, short, short, int, int, int[], int, bool> AfterDiagnosticBody;
	internal static void RecordDiagnosticBody(short requestOpcode, b response)
	{
		var callback = Volatile.Read(ref AfterDiagnosticBody);
		if (callback == null) return;
		int bodyLength = response.k();
		int[] lengths = { -1, -1, -1, -1 };
		int offset = DmFrameReader.HeaderSize;
		long end = (long)offset + bodyLength;
		bool valid = bodyLength >= 0 && end == response.B();
		for (int index = 0; valid && index < lengths.Length; index++)
		{
			if (end - offset < sizeof(int)) { valid = false; break; }
			int length = response.d(offset);
			lengths[index] = length;
			offset += sizeof(int);
			if (length < 0 || length > end - offset) { valid = false; break; }
			offset += length;
		}
		int remaining = end >= offset && end - offset <= int.MaxValue ? (int)(end - offset) : -1;
		callback(DmInvocation.Current?.Identity ?? default, requestOpcode, response.I(), response.L(),
			bodyLength, lengths, remaining, valid && remaining == 0);
	}
}

internal class B
{
	private ILogger __t02_field_04000AB8 = (Logger)LogFactory.getLog(typeof(B));

	public b __t02_field_04000AB9 = new b();

	public DmConnInstance __t02_field_04000ABA;

	private volatile bool __t02_field_04000ABB;

	private int __t02_field_04000ABC;

	private volatile bool __t02_field_04000ABD;

	private DmConnProperty __t02_field_04000AC0;

	private D __t02_field_04000AC1;
	private readonly b handshakeSend;
	private readonly b handshakeReceive;

	private List<LobData> __t02_field_04000AC2;
	internal static int MessageIdleTimeout(DmOperationPurpose purpose, int socketTimeout)
	{
		if (socketTimeout < 0) throw new ArgumentOutOfRangeException(nameof(socketTimeout));
		return purpose == DmOperationPurpose.Handshake ? 0 : socketTimeout;
	}

	private int CurrentMessageIdleTimeout() => MessageIdleTimeout(
		DmInvocation.Current?.Lease.Purpose ?? throw new InvalidOperationException("Message has no invocation."),
		a().SocketTimeout);
	[ThreadStatic] private static B decodeOwner;
	[ThreadStatic] private static B parameterUploadOwner;

	private void EncodeWithParameterUploads(Action encode)
	{
		B prior = parameterUploadOwner;
		parameterUploadOwner = this;
		try { encode(); }
		finally { parameterUploadOwner = prior; }
	}

	private static void BreakNestedExchange(DmSession session)
	{
		DmInvocation invocation = DmInvocation.Current;
		var captured = session.Detach(invocation.Identity);
		try { captured?.AbortTransport(); } catch { }
	}

	private T Wire<T>(Func<T> operation)
	{
		DmSession session = __t02_field_04000ABA.Session;
		if (DmWireExchange.Current != null)
		{
			session.RequireActiveWireExchange();
			if (!ReferenceEquals(decodeOwner, this) && !ReferenceEquals(parameterUploadOwner, this))
				throw new InvalidOperationException("Nested wire operation has no protocol continuation owner.");
			B priorNestedDecodeOwner = decodeOwner;
			try { return operation(); }
			catch { BreakNestedExchange(session); throw; }
			finally { decodeOwner = priorNestedDecodeOwner; }
		}
		B priorDecodeOwner = decodeOwner;
		using var exchange = session.BeginWireExchange();
		try
		{
			DmWireTestHooks.ExchangeEntered();
			T result = operation();
			exchange.Complete();
			return result;
		}
		finally { decodeOwner = priorDecodeOwner; }
	}

	private void Wire(Action operation)
	{
		DmSession session = __t02_field_04000ABA.Session;
		if (DmWireExchange.Current != null)
		{
			session.RequireActiveWireExchange();
			if (!ReferenceEquals(decodeOwner, this) && !ReferenceEquals(parameterUploadOwner, this))
				throw new InvalidOperationException("Nested wire operation has no protocol continuation owner.");
			B priorNestedDecodeOwner = decodeOwner;
			try { operation(); }
			catch { BreakNestedExchange(session); throw; }
			finally { decodeOwner = priorNestedDecodeOwner; }
			return;
		}
		B priorDecodeOwner = decodeOwner;
		using var exchange = session.BeginWireExchange();
		try
		{
			DmWireTestHooks.ExchangeEntered();
			operation();
			exchange.Complete();
		}
		finally { decodeOwner = priorDecodeOwner; }
	}

	[SpecialName]
	public D A()
	{
		return __t02_field_04000AC1;
	}

	[SpecialName]
	public void A(D P_0)
	{
		__t02_field_04000AC1 = P_0;
	}

	[SpecialName]
	public DmConnProperty a()
	{
		return __t02_field_04000AC0;
	}

	[SpecialName]
	public void A(DmConnProperty P_0)
	{
		__t02_field_04000AC0 = P_0;
	}

	[SpecialName]
	public bool __t02_method_06000A6B()
	{
		return __t02_field_04000ABB;
	}

	[SpecialName]
	public void A(bool P_0)
	{
		__t02_field_04000ABB = P_0;
	}

	internal bool b()
	{
		if (A() == null)
		{
			return true;
		}
		return A().c();
	}

	internal void C()
	{
		if (b())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR);
		}
	}

	internal B(b P_0, b P_1, DmConnInstance P_2)
	{
		handshakeSend = P_0;
		handshakeReceive = P_1;
		__t02_field_04000ABA = P_2;
		A(P_2.ConnProperty);
		A(new D(a().Server, a().Port, a().ConnectionTimeout, P_2));
	}

	internal void Open(DmDeadline deadline)
	{
		try
		{
			__t02_field_04000ABD = true;
			if (a().Compress != 0) throw new NotSupportedException("Protocol compression is not supported.");
			A().Open(deadline);
			A(handshakeSend, handshakeReceive);
			DmWireTestHooks.StartupNegotiatedEncryptMode(a().Encrypt);
			UpgradeNegotiatedSecurity(deadline);
			a(handshakeSend, handshakeReceive);
			__t02_field_04000ABD = false;
			__t02_field_04000ABC = __t02_field_04000ABA.ConnProperty.SocketTimeout;
			if (__t02_field_04000ABA.ConnProperty.msgVersion < 10)
			{
				__t02_field_04000ABA.ConnProperty.lobOffRowLen = 2048;
			}
		}
		catch
		{
			__t02_field_04000AC1?.C();
			throw;
		}
	}

	private void UpgradeNegotiatedSecurity(DmDeadline deadline)
	{
		DmConnectionSettings settings = __t02_field_04000ABA.Conn.Settings;
		bool requiresTls = DmHandshakeSecurityGuard.RequiresFullTls(a().Encrypt, settings.TransportSecurity);
		DmHandshakeSecurityGuard.ValidateMessageSecurity(a());
		if (requiresTls)
			A().UpgradeTls(DmTlsOptions.FromSettings(settings), deadline);
	}

	protected A A<A>(MSG<A> P_0)
	{
		return Wire(() =>
		{
		try
		{
			P_0.encode();
			a(P_0);
			P_0.setCRC();
			DmFrameWriter.Validate(__t02_field_04000AB9);
			DmWireTestHooks.HandshakeFrameEncoded(P_0.cmd);
			__t02_method_06000A73(P_0);
			DmWireTestHooks.Sent();
			b(P_0);
			P_0.checkCRC();
			DmResultProtocolTrace.RecordFrame(P_0.cmd, __t02_field_04000AB9);
			C(P_0);
			DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			return P_0.decode();
		}
		catch (IOException)
		{
			E();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR);
			return default(A);
		}
			});
	}

	protected void a<A>(MSG<A> P_0)
	{
		int length = P_0.getLength();
		if (length > 0 && P_0.cmd != 200 && a().encryptMsg)
		{
			length = P_0.getLength();
			byte[] array = this.A().__t02_field_04000AB3.Encrypt(__t02_field_04000AB9.A(64, length - 64), genDigest: true);
			__t02_field_04000AB9.a(64);
			__t02_field_04000AB9.A(array);
			P_0.setLength(array.Length);
		}
	}

	protected void __t02_method_06000A73<A>(MSG<A> P_0)
	{
		byte[] array = P_0.access.__t02_field_04000AB9.A();
		int total = DmFrameWriter.Validate(P_0.access.__t02_field_04000AB9);
		this.A().SendAll(array, 0, total, CurrentMessageIdleTimeout());
	}

	protected void b<A>(MSG<A> P_0)
	{
		this.A().ConfigureReadTimeout(CurrentMessageIdleTimeout());
		DmFrameReader.Read(this.A().ReceiveExactly, P_0.access.__t02_field_04000AB9,
			(frame, total) => DmFrameReader.ValidateChecksum(frame, total, a().crcBody),
			DmInvocation.Current?.Deadline ?? DmDeadline.FromMilliseconds(a().ConnectionTimeout),
			header => (a().crcBody && DmFrameReader.Command(header) != 200) || DmFrameReader.ValidateHeaderChecksum(header));
	}

	protected void C<A>(MSG<A> P_0)
	{
		int length = P_0.getLength();
		if (length > 0 && P_0.cmd != 200 && a().encryptMsg)
		{
			length = P_0.getLength();
			byte[] ciphertext = __t02_field_04000AB9.A(64, length);
			byte[] array = this.A().__t02_field_04000AB3.Decrypt(ciphertext, checkDigest: true);
			__t02_field_04000AB9.a(64);
			__t02_field_04000AB9.A(array);
			P_0.setLength(array.Length);
		}
	}

	private b A(b P_0, b P_1, int P_2)
	{
		try
		{
			C();
			LogRecord logRecord = null;
			if (__t02_field_04000AB8.InfoEnabled)
				logRecord = new LogRecord(this, a().RWStandby ? ("accessStandby Cmd:" + P_0.I()) : ("access Cmd:" + P_0.I()));
			DmWireTestHooks.HandshakeFrameEncoded(P_0.I());
			A().A(P_0, P_2, a().crcBody, a().encryptMsg);
			DmWireTestHooks.Sent();
			A().__t02_method_06000A4D(P_1, P_2, a().crcBody, a().encryptMsg);
			DmResultProtocolTrace.RecordFrame(P_0.I(), P_1);
			DmWireTestHooks.ResponseReady();
			decodeOwner = this;
			if (__t02_field_04000AB8.InfoEnabled)
				__t02_field_04000AB8.Info(logRecord.ToString());
		}
		catch (SocketException ex)
		{
			E();
			if (ex.ErrorCode == 10060)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMAND_TIME_OUT);
			}
			else
			{
				DmError.ThrowDmException(ex);
			}
		}
		return P_1;
	}

	internal void A(b P_0, b P_1)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, a(), A());
		A(P_0, P_1, a().ConnectionTimeout);
		global::W.Dm.Internal.Legacy.A.c.a(P_1, a());
			});
	}

	public b a(b P_0, b P_1)
	{
		return Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.a(P_0, a(), A());
		A(P_0, P_1, a().ConnectionTimeout);
		global::W.Dm.Internal.Legacy.A.c.B(P_1, a());
		return P_1;
			});
	}

	internal void A(string P_0)
	{
		if (P_0.Trim().StartsWith("["))
		{
			int num = P_0.IndexOf("[");
			int num2 = P_0.IndexOf("]");
			a().Server = P_0.Substring(num + 1, num2 - 1).Trim();
			int num3 = P_0.Substring(num2).IndexOf(":");
			if (num3 != -1)
			{
				a().Port = Convert.ToInt32(P_0.Substring(num2).Substring(num3 + 1).Trim());
			}
		}
		else
		{
			int num4 = P_0.IndexOf(":");
			if (num4 != -1)
			{
				a().Server = P_0.Substring(0, num4).Trim();
				a().Port = Convert.ToInt32(P_0.Substring(num4 + 1, P_0.Length - num4 - 1).Trim());
			}
		}
	}

	public void A(A P_0, b P_1, b P_2, ref bool P_3)
	{
		bool updated = P_3;
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_1, (short)3, 0);
		A(P_1, P_2, __t02_field_04000ABC);
		P_0.__t02_method_06000884(global::W.Dm.Internal.Legacy.A.c.A(P_2, a(), ref updated));
			});
		P_3 = updated;
	}

	public void A(b P_0, b P_1, A P_2)
	{
		Wire(() =>
		{
		if (!__t02_field_04000ABD)
		{
			global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.__t02_method_06000883());
			A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
		}
			});
	}

	public void A(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		if (P_4)
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2, a(), P_0.I(), executionVsPrepare: true);
			P_2.d(false);
		}
		else
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.__t02_method_0600088A(true);
		}
			});
	}

	public void A(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5, bool P_6)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		if (P_4)
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2, a(), P_0.I(), executionVsPrepare: true);
			P_2.d(false);
		}
		else
		{
			global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.__t02_method_0600088A(true);
		}
			});
	}

	public void A(A P_0, DmInfo P_1)
	{
		Wire(() =>
		{
		b _t02_field_ = P_0.__t02_field_04000925;
		b _t02_field_2 = P_0.__t02_field_04000926;
		if (a().msgVersion >= 26 || !A(_t02_field_, _t02_field_2, P_0, P_1))
		{
			EncodeWithParameterUploads(() => global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, P_0, P_1, a()));
			A(_t02_field_, _t02_field_2, P_0.G().ConnProperty.SocketTimeout);
			global::W.Dm.Internal.Legacy.A.c.A(_t02_field_2, P_0, a(), _t02_field_.I(), executionVsPrepare: true);
			P_0.d(false);
		}
			});
	}

	private bool A(b P_0, b P_1, A P_2, DmInfo P_3)
	{
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
			if (ParamsInfo[i].GetCType() == 12 || ParamsInfo[i].GetCType() == 19)
			{
				for (int j = 0; j < ParamsInfo[i].GetParamValue().Count; j++)
				{
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
				EncodeWithParameterUploads(() => global::W.Dm.Internal.Legacy.A.C.A(k, P_0, P_2, P_3, a()));
				A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
				global::W.Dm.Internal.Legacy.A.c.A(P_1, P_2, a(), P_0.I(), executionVsPrepare: true);
			}
			P_2.d(false);
			return true;
		}
		return false;
	}

	public void A(int P_0, A P_1, DmParameterInternal[] P_2)
	{
		Wire(() =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(P_0, b2, P_1.g(), P_2);
		A(b2, b3, P_1.G().ConnProperty.SocketTimeout);
		global::W.Dm.Internal.Legacy.A.c.A(b3, a());
			});
	}

	public bool A(A P_0, DmResultSetCache P_1, short P_2, long P_3, long P_4)
	{
		return Wire(() =>
		{
		b _t02_field_ = P_0.__t02_field_04000925;
		b _t02_field_2 = P_0.__t02_field_04000926;
		global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, P_0.g(), P_3, P_2, P_4, P_0.G().ConnProperty.BufPrefetch);
		A(_t02_field_, _t02_field_2, P_0.G().ConnProperty.SocketTimeout);
		return global::W.Dm.Internal.Legacy.A.c.A(_t02_field_2, P_3, P_1);
			});
	}

	public void __t02_method_06000A82(b P_0, b P_1)
	{
		Wire(() =>
		{
			global::W.Dm.Internal.Legacy.A.C.a(P_0, 8, 0);
			A(P_0, P_1, __t02_field_04000ABC);
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			ConfirmLocalTransactionResponse(P_1, DmTransactionControlKind.Commit, 8);
			});
	}

	public void b(b P_0, b P_1)
	{
		Wire(() =>
		{
			global::W.Dm.Internal.Legacy.A.C.a(P_0, 9, 0);
			A(P_0, P_1, __t02_field_04000ABC);
			global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			ConfirmLocalTransactionResponse(P_1, DmTransactionControlKind.Rollback, 9);
			});
	}

	private void ConfirmLocalTransactionResponse(b response, DmTransactionControlKind kind, short requestOpcode)
	{
		DmInvocation invocation = DmInvocation.Current;
		// Close can clear this protocol object's connection field after a complete
		// response arrives. The invocation retains the exact old session/control;
		// confirming that ACK must never consult a cleared or reopened connection.
		DmSession capturedSession = invocation?.Lease.Session;
		if (capturedSession == null || !capturedSession.IsCurrentTransactionControl(invocation, out var currentKind) ||
			currentKind != kind)
			throw new InvalidOperationException("Local transaction response has no matching operation.");
		short responseOpcode = response.I();
		int sqlCode = response.L();
		DmTransactionProtocolTrace.RecordDiagnosticBody(requestOpcode, response);
		Volatile.Read(ref DmTransportTestHooks.AfterControlFrameValidated)?.Invoke(
			invocation.Identity, kind, requestOpcode, responseOpcode, sqlCode);
		// The TEST profile observed a validated opcode-0 success frame for both
		// request opcodes 8 and 9. Other profiles must be admitted explicitly.
		if (responseOpcode != 0 || sqlCode != 0)
			throw new InvalidDataException("Unrecognized local transaction response.");
		Volatile.Read(ref DmTransportTestHooks.BeforeControlAck)?.Invoke(invocation.Identity, kind);
		capturedSession.ConfirmTransactionAck(invocation, kind);
	}

	public void A(A P_0)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.a(P_0.__t02_field_04000925, P_0.__t02_method_06000883());
		A(P_0.__t02_field_04000925, P_0.__t02_field_04000926, P_0.G().ConnProperty.SocketTimeout);
		global::W.Dm.Internal.Legacy.A.c.A(P_0.__t02_field_04000926, a());
			});
	}

	public void A(b P_0, b P_1, A P_2, string P_3)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2, P_3, a());
		A(P_2.__t02_field_04000925, P_2.__t02_field_04000926, P_2.G().ConnProperty.SocketTimeout);
		global::W.Dm.Internal.Legacy.A.c.A(P_2.__t02_field_04000926, a());
			});
	}

	public void A(b P_0, b P_1, A P_2, short P_3, byte[] P_4, int P_5, int P_6)
	{
		Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			});
	}

	public byte[] A(b P_0, b P_1, A P_2, int P_3, byte[] P_4, int P_5, byte[] P_6)
	{
		return Wire(() =>
		{
		global::W.Dm.Internal.Legacy.A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		return global::W.Dm.Internal.Legacy.A.c.A(P_1);
			});
	}

	public long A(AbstractLob P_0)
	{
		return A(new GET_LOB_LEN(this, P_0));
	}

	public long A(AbstractLob P_0, int P_1)
	{
		return A(new LOB_TRUNCATE(this, P_0, P_1));
	}

	public byte[] A(DmBlob P_0, long P_1, int P_2)
	{
		byte[] array = new byte[P_2];
		int num = 0;
		int num2 = 0;
		byte[] array2 = null;
		while (num < P_2)
		{
			num2 = P_2 - num;
			if (num2 > a().MaxLobDataLenPerMsg)
			{
				num2 = a().MaxLobDataLenPerMsg;
			}
			array2 = A((AbstractLob)P_0, P_1 + num, num2).value;
			if (array2 == null || array2.Length == 0)
			{
				break;
			}
			ByteUtil.setBytes(array, num, array2);
			num += array2.Length;
			if (P_0.readOver)
			{
				break;
			}
		}
		return array;
	}

	public string A(DmClob P_0, long P_1, int P_2)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		int num2 = 0;
		byte[] array = null;
		string text = null;
		while (num < P_2)
		{
			num2 = P_2 - num;
			if (num2 > a().MaxLobDataLenPerMsg)
			{
				num2 = a().MaxLobDataLenPerMsg;
			}
			Data data = A((AbstractLob)P_0, P_1 + num, num2);
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

	public Data A(AbstractLob P_0, long P_1, int P_2)
	{
		return A(new GET_LOB_DATA(this, P_0, P_1, P_2));
	}

	public int A(AbstractLob P_0, long P_1, string P_2, string P_3)
	{
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
			int num7 = A(P_0, b2, P_1, array, num, num5);
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

	public int A(AbstractLob P_0, long P_1, byte[] P_2, int P_3, int P_4)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = P_4 / a().MaxLobDataLenPerMsg + 1;
		byte b2 = 0;
		byte b3 = 1;
		byte b4 = 2;
		for (int i = 0; i < num4; i++)
		{
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
			int num5 = A(P_0, b2, P_1, P_2, P_3, num3);
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

	public int A(AbstractLob P_0, byte P_1, long P_2, byte[] P_3, int P_4, int P_5)
	{
		return A(new SET_LOB_DATA(this, P_0, P_1, P_2, P_3, P_4, P_5));
	}

	public void A(A P_0, short P_1)
	{
		Wire(() =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.g(), P_1);
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
		global::W.Dm.Internal.Legacy.A.c.a(b3, P_0, a());
			});
	}

	public long[] A(DmResultSetCache P_0)
	{
		return Wire(() =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.ids);
		A(b2, b3, __t02_field_04000ABC);
		return global::W.Dm.Internal.Legacy.A.c.b(b3, a());
			});
	}

	public long a(DmResultSetCache P_0)
	{
		return Wire(() =>
		{
		A statement = P_0.statement;
		long num = long.MaxValue;
		int num2 = 1;
		b _t02_field_ = statement.__t02_field_04000925;
		b _t02_field_2 = statement.__t02_field_04000926;
		global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, statement.g(), num, 0, num2, statement.G().ConnProperty.BufPrefetch);
		A(_t02_field_, _t02_field_2, statement.G().ConnProperty.SocketTimeout);
		return global::W.Dm.Internal.Legacy.A.c.a(_t02_field_2, num, P_0);
			});
	}

	private void A(b P_0, A P_1, b P_2, int P_3, long P_4, int P_5)
	{
		int num = P_2.ae();
		int num2 = P_2.ah();
		if (num > 0)
		{
			P_2.k();
			short num3 = P_2.aC();
			bool isRsBdta = false;
			short rsBdtaRowidCol = -1;
			if (num3 == 160 || num3 == 162)
			{
				isRsBdta = P_2.aG() == 2;
				rsBdtaRowidCol = P_2.ag();
			}
			else
			{
				P_2.af();
			}
			if (P_1.l() == null)
			{
				P_1.__t02_method_060008B0(new DmResultSetCache(P_1, P_3, num));
			}
			else
			{
				P_1.l().SetCols(P_3);
				P_1.l().totalRowCount = P_4;
			}
			P_1.l().FillRows(0L, num, P_2, isRsBdta, rsBdtaRowidCol);
			if (P_1.G().ConnProperty.EnRsCache && num2 > 0 && P_4 == num)
			{
				RsKey key = new RsKey(P_1.G().ConnProperty.Guid, P_1.G().ConnProperty.CurrentSchema, P_1.C(), P_1.f().do_DbParameterCollection.Count, P_1.f().do_DbParameterCollection);
				DmConnection.rsLRUCache.Add(key, P_1.l());
			}
		}
		if (num2 > 0)
		{
			P_2.f(num2 - P_2.a());
			short num4 = P_2.C();
			int[] array = new int[num4];
			long[] array2 = new long[num4];
			for (int i = 0; i < num4; i++)
			{
				array[i] = P_2.d();
				array2[i] = P_2.e();
			}
			P_1.l().ids = array;
			P_1.l().tss = array2;
			P_1.l().lastCheckDt = DateTime.Now;
		}
	}

	public DmInfo A(A P_0, DmInfo P_1, short P_2)
	{
		return Wire(() =>
		{
		b b2 = new b();
		b b3 = new b();
		global::W.Dm.Internal.Legacy.A.C.A(b2, P_0.g(), P_2);
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
			global::W.Dm.Internal.Legacy.A.c.A(b3, P_0, a(), b2.I(), executionVsPrepare: true);
		return P_0.F();
			});
	}

	public bool D()
	{
		return __t02_field_04000ABD;
	}

	internal void d()
	{
		if (!__t02_field_04000ABD)
		{
			if (A() != null)
			{
				A().C();
			}
			__t02_field_04000ABA = null;
			__t02_field_04000ABD = true;
		}
	}

	public void E()
	{
		if (__t02_field_04000ABD)
		{
			return;
		}
		// Session detachment has already selected this physical transport. Do not
		// wait for an in-flight exchange to release its protocol lock.
		if (A() != null)
		{
			A().C();
		}
		__t02_field_04000ABA = null;
		__t02_field_04000ABD = true;
	}

	public void A(A P_0, int P_1, DmParameterInternal P_2, int P_3)
	{
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
			if (num3 < num)
			{
				array = DmConvertion.GetBytes(text.Substring(num2, num3), a().ServerEncoding);
				A(b2, b3, P_0, (short)P_1, array, array.Length, num3);
				return;
			}
			array = DmConvertion.GetBytes(text.Substring(num2, num), a().ServerEncoding);
			A(b2, b3, P_0, (short)P_1, array, array.Length, num);
			num3 -= num;
			num2 += num;
		}
		A(b2, b3, P_0, (short)P_1, array, array.Length, 0);
	}

	public byte[] a(A P_0, int P_1, DmParameterInternal P_2, int P_3)
	{
		return b(P_0, P_1, P_2, P_3);
	}

	public void __t02_method_06000A9A(A P_0, int P_1, DmParameterInternal P_2, int P_3)
	{
		int maxLobDataLenPerMsg = P_0.G().ConnProperty.MaxLobDataLenPerMsg;
		int num = 0;
		b b2 = new b();
		b b3 = new b();
		byte[] val = new byte[maxLobDataLenPerMsg];
		int bytes;
		for (bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3); bytes > 0; bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, 0))
		{
			if (bytes < maxLobDataLenPerMsg)
			{
				A(b2, b3, P_0, (short)P_1, val, bytes, int.MinValue);
				return;
			}
			A(b2, b3, P_0, (short)P_1, val, maxLobDataLenPerMsg, int.MinValue);
			num += maxLobDataLenPerMsg;
		}
		A(b2, b3, P_0, (short)P_1, val, bytes, int.MinValue);
	}

	public byte[] b(A P_0, int P_1, DmParameterInternal P_2, int P_3)
	{
		int maxLobDataLenPerMsg = P_0.G().ConnProperty.MaxLobDataLenPerMsg;
		int num = 0;
		b b2 = new b();
		b b3 = new b();
		byte[] val = new byte[maxLobDataLenPerMsg];
		int bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3);
		byte[] array = global::W.Dm.Internal.Legacy.A.C.A();
		while (bytes > 0)
		{
			if (bytes < maxLobDataLenPerMsg)
			{
				return A(b2, b3, P_0, P_1, val, bytes, array);
			}
			array = A(b2, b3, P_0, P_1, val, maxLobDataLenPerMsg, array);
			num += maxLobDataLenPerMsg;
			bytes = P_2.GetBytes(ref val, 0, num, maxLobDataLenPerMsg, P_3);
		}
		return A(b2, b3, P_0, (short)P_1, val, bytes, array);
	}

	private bool A(int P_0, b P_1, A P_2)
	{
		int num = 64;
		switch (P_0)
		{
		case 166:
		{
			short num3 = P_1.C(num);
			num += 2;
			num++;
			IsolationLevel isolationLevel = IsolationLevel.ReadCommitted;
			P_2.G().SetTrxISO(num3 switch
			{
				1 => IsolationLevel.ReadCommitted, 
				3 => IsolationLevel.Serializable, 
				0 => IsolationLevel.ReadUncommitted, 
				_ => IsolationLevel.Unspecified, 
			});
			P_2.h().A(true);
			return true;
		}
		case 150:
		{
			short stmtSerial = P_1.C(num);
			num += 2;
			num++;
			if (P_2.f() != null)
			{
				P_2.f().SetStmtSerial(stmtSerial);
			}
			if (P_2.G().Transaction != null)
			{
				P_2.G().Transaction.SetStmtSerial(stmtSerial);
			}
			return true;
		}
		case 152:
		{
			int num2 = P_1.d(num);
			num += 4;
			P_2.G().ConnProperty.Database = P_1.A(num, num2, __t02_field_04000ABA.ConnProperty.ServerEncoding);
			num += num2;
			num += 4;
			return true;
		}
		case 153:
			num += 4;
			return true;
		case 165:
			num += 2;
			return true;
		default:
			return false;
		}
	}

	private void A(b P_0)
	{
		DmError dmError = new DmError();
		dmError.State = P_0.L();
		P_0.A(dmError, __t02_field_04000ABA.ConnProperty.ServerEncoding);
		DmError.ThrowDmException(dmError);
	}

	public void A(A P_0, List<SQLProcessor.Parameter> P_1)
	{
		Wire(() =>
		{
		b _t02_field_ = P_0.__t02_field_04000925;
		b _t02_field_2 = P_0.__t02_field_04000926;
		global::W.Dm.Internal.Legacy.A.C.A(_t02_field_, P_0, P_1, a());
		A(_t02_field_, _t02_field_2, P_0.G().ConnProperty.SocketTimeout);
			global::W.Dm.Internal.Legacy.A.c.A(_t02_field_2, P_0, a(), _t02_field_.I(), executionVsPrepare: false);
		P_0.d(false);
			});
	}

	public void a(b P_0, b P_1, int P_2)
	{
		Wire(() =>
		{
		if (P_2 == 3)
		{
			A(true);
		}
		global::W.Dm.Internal.Legacy.A.C.B(P_0, P_2);
		A(P_0, P_1, __t02_field_04000ABC);
		global::W.Dm.Internal.Legacy.A.c.A(P_1, a());
			});
	}

	public void e()
	{
		A(new COMMIT(this));
		__t02_field_04000ABA.ClearTrx();
	}

	public TableInfo A(string P_0, string P_1, byte P_2)
	{
		return A(new FLDR_GET_TABLE_INFO(this, P_0, P_1, P_2));
	}

	public HorizontalTableInfo A(int P_0, int P_1, int P_2, byte P_3, object P_4, object P_5, TableInfo P_6)
	{
		return A(new FLDR_FIND_INTERVAL(this, P_0, P_1, P_2, P_3, P_4, P_5, P_6));
	}

	public List<FldrIndexInfo> A(string P_0, string P_1, int P_2, int P_3)
	{
		return A(new FLDR_GET_INDEX_INFO(this, P_0, P_1, P_2, P_3));
	}

	public FldrClusterInfo A(string P_0, string P_1)
	{
		return A(new FLDR_GET_MPP_INFO(this, P_0, P_1));
	}

	public string A(SetEnvInfo P_0, HashSet<int> P_1, List<FldrIndexInfo> P_2)
	{
		return A(new FLDR_SET2(this, P_0, P_1, P_2));
	}

	public void A(List<ColumnData> P_0, TableInfo P_1, int P_2, int P_3, int P_4, CancellationTokenSource P_5)
	{
		if (P_1.containLob)
		{
			A(P_0, P_1);
		}
		A(new FLDR_INSERT(this, P_0, P_1, P_2, P_3, P_4, P_5));
		if (__t02_field_04000AC2 != null)
		{
			A(P_3, __t02_field_04000AC2, 0, 0, 0, null);
			__t02_field_04000AC2 = null;
		}
	}

	private void A(List<ColumnData> P_0, TableInfo P_1)
	{
		List<ColumnInfo> columnInfos = P_1.getColumnInfos();
		if (columnInfos == null || columnInfos.size() == 0)
		{
			throw new Exception("不存在列定义信息,请检查TableInfo信息");
		}
		List<int> list = new List<int>();
		foreach (ColumnInfo item in columnInfos)
		{
			if (19 == item.getColumnType() || 12 == item.getColumnType())
			{
				list.add(Convert.ToInt32(item.getColumnId()));
			}
		}
		if (list == null || list.size() <= 0)
		{
			return;
		}
		__t02_field_04000AC2 = new List<LobData>();
		Dictionary<int, ColumnData> dictionary = new Dictionary<int, ColumnData>();
		foreach (ColumnData item2 in P_0)
		{
			if (19 == item2.getSqlType() || 12 == item2.getSqlType())
			{
				dictionary.put(item2.getColumnIndex(), item2);
			}
		}
		foreach (int item3 in list)
		{
			ColumnData columnData = dictionary.get(item3);
			if (columnData == null)
			{
				__t02_field_04000AC2.add(new LobData((short)item3, null, -1));
			}
			else
			{
				__t02_field_04000AC2.add(new LobData((short)item3, columnData.getData(), columnData.getSqlType()));
			}
		}
	}

	public void A(int P_0, List<LobData> P_1, int P_2, int P_3, int P_4, byte[] P_5)
	{
		for (FLDR_BLOB fLDR_BLOB = A(new FLDR_BLOB(this, P_0, P_1, P_2, P_3, P_4, P_5)); fLDR_BLOB != null; fLDR_BLOB = A(fLDR_BLOB))
		{
		}
	}

	public List<FldrIndexInfo> A(int P_0)
	{
		return A(new FLDR_CLR(this, P_0));
	}

	public void A(List<FldrIndexInfo> P_0)
	{
		A(new FLDR_RESET_INDEX_INFO(this, P_0));
	}
}
