using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Dm;
using Dm.filter.log;
using Dm.parser;
using Dm.util;

namespace A;

internal class B
{
	private ILogger m_A = (Logger)LogFactory.getLog(typeof(B));

	public b A = new b();

	public DmConnInstance A;

	private volatile bool m_A;

	private int m_A;

	private volatile bool m_a;

	private object m_A = new object();

	private bool m_A;

	private DmConnProperty m_A;

	private D m_A;

	private List<LobData> m_A;

	[SpecialName]
	public D A()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(D P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	public DmConnProperty a()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(DmConnProperty P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	public bool B()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(bool P_0)
	{
		this.m_A = P_0;
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
		try
		{
			this.m_a = true;
			this.A = P_2;
			A(P_2.ConnProperty);
			A(a().Server);
			A(new D(a().Server, a().Port, a().ConnectionTimeout));
			A(P_0, P_1);
			if (a().Compress > 0)
			{
				this.m_A = DriverUtil.isLocalHost(a().Server);
			}
			if (a().encryptMsg || a().encryptPwd)
			{
				byte[] array = MsgSecurity.ComputeSessionKey(A().b(), a().serverPubKey);
				int num = ((a().hashType == -1) ? 4352 : a().hashType);
				if (a().encryptPwd)
				{
					int num2 = ((a().msgVersion >= 15 && a().msgVersion < 17) ? ((a().encryptType == -1) ? 2052 : a().encryptType) : ((a().msgVersion < 17) ? ((a().encryptType == -1) ? 132 : a().encryptType) : ((a().algorithm == 0) ? 132 : 2052)));
					A().A(num2, array, a().CipherPath, num, false);
				}
				if (a().encryptMsg)
				{
					A().A((a().encryptType == -1) ? 132 : a().encryptType, array, a().CipherPath, num, true);
				}
			}
			c();
			a(P_0, P_1);
			this.m_a = false;
			this.m_A = P_2.ConnProperty.SocketTimeout;
			if (P_2.ConnProperty.msgVersion < 10)
			{
				P_2.ConnProperty.lobOffRowLen = 2048;
			}
		}
		catch (Exception ex)
		{
			this.m_A?.C();
			throw ex;
		}
	}

	private void c()
	{
		if (a().property.TryGetValue(DmConst.PROP_KEY_SSL_KEY_PASS, out var value))
		{
			this.m_A.A(Convert.ToString(value));
		}
		if (a().property.TryGetValue(DmConst.PROP_KEY_SSL_FILE_PATH, out value))
		{
			this.m_A.a(Convert.ToString(value));
		}
		if (a().Encrypt == 2)
		{
			this.m_A.A(a().User, true);
		}
		else if (a().Encrypt == 1 || a().Encrypt == 4)
		{
			this.m_A.A(a().User, false);
		}
		else if (a().Encrypt == 3)
		{
			DmError.ThrowUnsupportedException();
		}
	}

	protected A A<A>(MSG<A> P_0)
	{
		try
		{
			P_0.encode();
			a(P_0);
			P_0.setCRC();
			if (this.A.B() > 536870912)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_MSG_LEN_TOO_LONG);
			}
			B(P_0);
			b(P_0);
			P_0.checkCRC();
			C(P_0);
			return P_0.decode();
		}
		catch (IOException)
		{
			E();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR);
			return default(A);
		}
	}

	protected void a<A>(MSG<A> P_0)
	{
		int length = P_0.getLength();
		if (length > 0 && P_0.cmd != 200 && a().encryptMsg)
		{
			length = P_0.getLength();
			byte[] array = this.A().A.Encrypt(this.A.A(64, length - 64), genDigest: true);
			this.A.a(64);
			this.A.A(array);
			P_0.setLength(array.Length);
		}
	}

	protected void B<A>(MSG<A> P_0)
	{
		byte[] array = P_0.access.A.A();
		int connectionTimeout = a().ConnectionTimeout;
		int num = P_0.getLength() + 64;
		this.A().A(array, connectionTimeout, num);
	}

	protected void b<A>(MSG<A> P_0)
	{
		int num = 0;
		this.A().A.Blocking = true;
		this.A().A.ReceiveTimeout = a().ConnectionTimeout;
		int num2;
		do
		{
			P_0.access.A.a(0);
			P_0.access.A.f(64);
			num2 = this.A().a(P_0.access.A.A(), 0, 32640);
			num = P_0.access.A.K();
		}
		while (269 == num);
		if (num2 == 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NO_SOCKET_DATA, "DmCommTcpip.Recv");
		}
		P_0.access.A.g(num2);
		int num3 = P_0.access.A.k();
		num3 += 64;
		if (num2 < num3 && num2 != -1)
		{
			P_0.access.A.g(num3 - num2);
			this.A().B(P_0.access.A.A(), num2, num3 - num2);
		}
	}

	protected void C<A>(MSG<A> P_0)
	{
		int length = P_0.getLength();
		if (length > 0 && P_0.cmd != 200 && a().encryptMsg)
		{
			length = P_0.getLength();
			byte[] ciphertext = this.A.A(64, length);
			byte[] array = this.A().A.Decrypt(ciphertext, checkDigest: true);
			this.A.a(64);
			this.A.A(array);
			P_0.setLength(array.Length);
		}
	}

	private b A(b P_0, b P_1, int P_2)
	{
		lock (this.m_A)
		{
			try
			{
				C();
				LogRecord logRecord = null;
				if (this.m_A.InfoEnabled)
				{
					logRecord = new LogRecord(this, a().RWStandby ? ("accessStandby Cmd:" + P_0.I()) : ("access Cmd:" + P_0.I()));
				}
				A().A(P_0, P_2, a().crcBody, a().encryptMsg);
				A().a(P_1, P_2, a().crcBody, a().encryptMsg);
				if (this.m_A.InfoEnabled)
				{
					this.m_A.Info(logRecord.ToString());
				}
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
		}
		return P_1;
	}

	internal void A(b P_0, b P_1)
	{
		global::A.C.A(P_0, a(), A());
		A(P_0, P_1, a().ConnectionTimeout);
		global::A.c.a(P_1, a());
	}

	public b a(b P_0, b P_1)
	{
		global::A.C.a(P_0, a(), A());
		A(P_0, P_1, a().ConnectionTimeout);
		global::A.c.B(P_1, a());
		return P_1;
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
		global::A.C.A(P_1, (short)3, 0);
		A(P_1, P_2, this.m_A);
		P_0.A(global::A.c.A(P_2, a(), ref P_3));
	}

	public void A(b P_0, b P_1, A P_2)
	{
		if (!this.m_a)
		{
			global::A.C.A(P_0, P_2.A());
			A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
			global::A.c.A(P_1, a());
		}
	}

	public void A(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5)
	{
		global::A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		if (P_4)
		{
			global::A.c.A(P_1, P_2, a());
			P_2.d(false);
		}
		else
		{
			global::A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.A(true);
		}
	}

	public void A(b P_0, b P_1, A P_2, string P_3, bool P_4, int P_5, bool P_6)
	{
		global::A.C.A(P_0, P_2.g(), a(), P_3, P_4, P_5, P_2.D(), P_2.I());
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		if (P_4)
		{
			global::A.c.A(P_1, P_2, a());
			P_2.d(false);
		}
		else
		{
			global::A.c.A(P_1, P_2);
			P_2.d(true);
			P_2.A(true);
		}
	}

	public void A(A P_0, DmInfo P_1)
	{
		b b2 = P_0.A;
		b b3 = P_0.a;
		if (a().msgVersion >= 26 || !A(b2, b3, P_0, P_1))
		{
			global::A.C.A(b2, P_0, P_1, a());
			A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
			global::A.c.A(b3, P_0, a());
			P_0.d(false);
		}
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
				global::A.C.A(k, P_0, P_2, P_3, a());
				A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
				global::A.c.A(P_1, P_2, a());
			}
			P_2.d(false);
			return true;
		}
		return false;
	}

	public void A(int P_0, A P_1, DmParameterInternal[] P_2)
	{
		b b2 = new b();
		b b3 = new b();
		global::A.C.A(P_0, b2, P_1.g(), P_2);
		A(b2, b3, P_1.G().ConnProperty.SocketTimeout);
		global::A.c.A(b3, a());
	}

	public bool A(A P_0, DmResultSetCache P_1, short P_2, long P_3, long P_4)
	{
		b b2 = P_0.A;
		b b3 = P_0.a;
		global::A.C.A(b2, P_0.g(), P_3, P_2, P_4, P_0.G().ConnProperty.BufPrefetch);
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
		return global::A.c.A(b3, P_3, P_1);
	}

	public void B(b P_0, b P_1)
	{
		global::A.C.a(P_0, 8, 0);
		A(P_0, P_1, this.m_A);
		global::A.c.A(P_1, a());
	}

	public void b(b P_0, b P_1)
	{
		global::A.C.a(P_0, 9, 0);
		A(P_0, P_1, this.m_A);
		global::A.c.A(P_1, a());
	}

	public void A(A P_0)
	{
		global::A.C.a(P_0.A, P_0.A());
		A(P_0.A, P_0.a, P_0.G().ConnProperty.SocketTimeout);
		global::A.c.A(P_0.a, a());
	}

	public void A(b P_0, b P_1, A P_2, string P_3)
	{
		global::A.C.A(P_0, P_2, P_3, a());
		A(P_2.A, P_2.a, P_2.G().ConnProperty.SocketTimeout);
		global::A.c.A(P_2.a, a());
	}

	public void A(b P_0, b P_1, A P_2, short P_3, byte[] P_4, int P_5, int P_6)
	{
		global::A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		global::A.c.A(P_1, a());
	}

	public byte[] A(b P_0, b P_1, A P_2, int P_3, byte[] P_4, int P_5, byte[] P_6)
	{
		global::A.C.A(P_0, P_2.g(), P_3, P_4, P_5, a(), P_6);
		A(P_0, P_1, P_2.G().ConnProperty.SocketTimeout);
		return global::A.c.A(P_1);
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
		b b2 = new b();
		b b3 = new b();
		global::A.C.A(b2, P_0.g(), P_1);
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
		global::A.c.a(b3, P_0, a());
	}

	public long[] A(DmResultSetCache P_0)
	{
		b b2 = new b();
		b b3 = new b();
		global::A.C.A(b2, P_0.ids);
		A(b2, b3, this.m_A);
		return global::A.c.b(b3, a());
	}

	public long a(DmResultSetCache P_0)
	{
		A statement = P_0.statement;
		long num = long.MaxValue;
		int num2 = 1;
		b b2 = statement.A;
		b b3 = statement.a;
		global::A.C.A(b2, statement.g(), num, 0, num2, statement.G().ConnProperty.BufPrefetch);
		A(b2, b3, statement.G().ConnProperty.SocketTimeout);
		return global::A.c.a(b3, num, P_0);
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
				P_1.A(new DmResultSetCache(P_1, P_3, num));
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
		b b2 = new b();
		b b3 = new b();
		global::A.C.A(b2, P_0.g(), P_2);
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
		global::A.c.A(b3, P_0, a());
		return P_0.F();
	}

	public bool D()
	{
		return this.m_a;
	}

	internal void d()
	{
		if (!this.m_a)
		{
			if (A() != null)
			{
				A().C();
			}
			this.A = null;
			this.m_a = true;
		}
	}

	public void E()
	{
		if (this.m_a)
		{
			return;
		}
		lock (this.m_A)
		{
			if (A() != null)
			{
				A().C();
			}
		}
		this.A = null;
		this.m_a = true;
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

	public void B(A P_0, int P_1, DmParameterInternal P_2, int P_3)
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
		byte[] array = global::A.C.A();
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
			P_2.G().ConnProperty.Database = P_1.A(num, num2, this.A.ConnProperty.ServerEncoding);
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
		P_0.A(dmError, this.A.ConnProperty.ServerEncoding);
		DmError.ThrowDmException(dmError);
	}

	public void A(A P_0, List<SQLProcessor.Parameter> P_1)
	{
		b b2 = P_0.A;
		b b3 = P_0.a;
		global::A.C.A(b2, P_0, P_1, a());
		A(b2, b3, P_0.G().ConnProperty.SocketTimeout);
		global::A.c.A(b3, P_0, a());
		P_0.d(false);
	}

	public void a(b P_0, b P_1, int P_2)
	{
		if (P_2 == 3)
		{
			A(true);
		}
		global::A.C.B(P_0, P_2);
		A(P_0, P_1, this.m_A);
		global::A.c.A(P_1, a());
	}

	public void e()
	{
		A(new COMMIT(this));
		this.A.ClearTrx();
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
		if (this.m_A != null)
		{
			A(P_3, this.m_A, 0, 0, 0, null);
			this.m_A = null;
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
		this.m_A = new List<LobData>();
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
				this.m_A.add(new LobData((short)item3, null, -1));
			}
			else
			{
				this.m_A.add(new LobData((short)item3, columnData.getData(), columnData.getSqlType()));
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
internal class b
{
	private byte[] m_A;

	private int m_A;

	private int m_a;

	[SpecialName]
	internal byte[] A()
	{
		return this.m_A;
	}

	[SpecialName]
	internal int a()
	{
		return this.m_A;
	}

	[SpecialName]
	internal int B()
	{
		return this.m_a;
	}

	[SpecialName]
	internal void A(int P_0)
	{
		this.m_a = P_0;
	}

	internal b()
	{
		this.m_A = new byte[32640];
	}

	private static void A(string[] P_0)
	{
		b b2 = new b();
		for (int i = 0; i < 32640; i++)
		{
			try
			{
				b2.A(new byte[1] { 1 });
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
		}
		b2.A(new byte[0]);
	}

	internal b(byte[] P_0)
	{
		this.m_A = P_0;
	}

	internal void a(int P_0)
	{
		this.m_a = P_0;
		this.m_A = 0;
		Array.Clear(this.m_A, 0, P_0);
	}

	internal void B(int P_0)
	{
		if (this.m_a + P_0 > this.m_A.Length)
		{
			byte[] destinationArray = new byte[this.m_A.Length * 2 + P_0];
			Array.Copy(this.m_A, 0, destinationArray, 0, this.m_a);
			this.m_A = destinationArray;
		}
	}

	internal byte b(int P_0)
	{
		return this.m_A[P_0];
	}

	internal short C(int P_0)
	{
		int num = 0xFF & this.m_A[P_0];
		P_0++;
		int num2 = 0xFF & this.m_A[P_0];
		num2 = num | (num2 << 8);
		return (short)(0xFFFF & num2);
	}

	internal ushort c(int P_0)
	{
		int num = 0xFF & this.m_A[P_0];
		P_0++;
		int num2 = 0xFF & this.m_A[P_0];
		num2 = num | (num2 << 8);
		return (ushort)(0xFFFF & num2);
	}

	internal int D(int P_0)
	{
		int num = 0xFF & this.m_A[P_0];
		P_0++;
		int num2 = 0xFF & this.m_A[P_0];
		return num | (num2 << 8);
	}

	internal int d(int P_0)
	{
		int num = 4;
		int num2 = P_0 + num;
		long num3 = 0xFF & this.m_A[--num2];
		num3 = (0xFF & this.m_A[--num2]) | (num3 << 8);
		num3 = (0xFF & this.m_A[--num2]) | (num3 << 8);
		num3 = (0xFF & this.m_A[--num2]) | (num3 << 8);
		return (int)(0xFFFFFFFFu & num3);
	}

	internal long E(int P_0)
	{
		return (this.m_A[P_0++] & 0xFF) | ((long)(this.m_A[P_0++] & 0xFF) << 8) | ((long)(this.m_A[P_0++] & 0xFF) << 16) | ((long)(this.m_A[P_0++] & 0xFF) << 24);
	}

	internal long e(int P_0)
	{
		long num = 0L;
		int num2 = P_0 + 8;
		for (int i = 0; i < 8; i++)
		{
			num = (0xFF & this.m_A[--num2]) | (num << 8);
		}
		return num;
	}

	internal byte[] A(int P_0, int P_1)
	{
		return DmConvertion.GetBytes(this.m_A, P_0, P_1);
	}

	internal string A(int P_0, int P_1, string P_2)
	{
		return DmConvertion.GetString(this.m_A, P_0, P_1, P_2);
	}

	internal byte b()
	{
		byte result = b(this.m_A);
		this.m_A++;
		return result;
	}

	internal short C()
	{
		short result = C(this.m_A);
		this.m_A += 2;
		return result;
	}

	internal ushort c()
	{
		ushort result = c(this.m_A);
		this.m_A += 2;
		return result;
	}

	internal int D()
	{
		int result = D(this.m_A);
		this.m_A += 2;
		return result;
	}

	internal int d()
	{
		int result = d(this.m_A);
		this.m_A += 4;
		return result;
	}

	internal long E()
	{
		long result = E(this.m_A);
		this.m_A += 4;
		return result;
	}

	internal long e()
	{
		long result = e(this.m_A);
		this.m_A += 8;
		return result;
	}

	internal float F()
	{
		float result = ByteUtil.toFloat(A(this.m_A, 4));
		this.m_A += 4;
		return result;
	}

	internal double f()
	{
		double result = ByteUtil.toDouble(A(this.m_A, 8));
		this.m_A += 8;
		return result;
	}

	internal byte[] F(int P_0)
	{
		byte[] result = A(this.m_A, P_0);
		this.m_A += P_0;
		return result;
	}

	internal byte[] A(byte[] P_0, int P_1, int P_2)
	{
		byte[] sourceArray = A(this.m_A, P_2);
		this.m_A += P_2;
		Array.Copy(sourceArray, 0, P_0, P_1, P_2);
		return P_0;
	}

	internal byte[] G()
	{
		return F(d());
	}

	internal string A(int P_0, string P_1)
	{
		string result = A(this.m_A, P_0, P_1);
		this.m_A += P_0;
		return result;
	}

	public string A(string P_0)
	{
		return ByteUtil.toString(G(), P_0);
	}

	internal string a(string P_0)
	{
		return A(b(), P_0);
	}

	internal string B(string P_0)
	{
		return A(C(), P_0);
	}

	internal string b(string P_0)
	{
		return A(d(), P_0);
	}

	public int A(bool P_0)
	{
		if (P_0)
		{
			return B();
		}
		return a();
	}

	public int a(bool P_0)
	{
		if (!P_0)
		{
			return this.m_a - this.m_A;
		}
		return this.m_A.Length - this.m_a;
	}

	internal void A(int P_0, bool P_1, bool P_2)
	{
		if (P_1)
		{
			g(P_0);
		}
		else
		{
			f(P_0);
		}
	}

	internal void f(int P_0)
	{
		this.m_A += P_0;
	}

	internal int g()
	{
		return this.m_a;
	}

	internal void G(int P_0)
	{
		this.m_A = P_0;
	}

	internal void g(int P_0)
	{
		B(P_0);
		this.m_a += P_0;
	}

	public void A(int P_0, byte P_1)
	{
		this.m_A[P_0] = P_1;
	}

	public void A(int P_0, short P_1)
	{
		this.m_A[P_0] = (byte)(P_1 & 0xFF);
		P_0++;
		P_1 >>= 8;
		this.m_A[P_0] = (byte)(P_1 & 0xFF);
	}

	public void a(int P_0, int P_1)
	{
		this.m_A[P_0] = (byte)(P_1 & 0xFF);
		P_0++;
		P_1 >>= 8;
		this.m_A[P_0] = (byte)(P_1 & 0xFF);
	}

	public void B(int P_0, int P_1)
	{
		this.m_A[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		this.m_A[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		this.m_A[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		this.m_A[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
	}

	public void A(int P_0, long P_1)
	{
		int num = 8;
		int num2 = P_0;
		while (num-- > 0)
		{
			this.m_A[num2++] = (byte)(P_1 & 0xFF);
			P_1 >>= 8;
		}
	}

	public void A(int P_0, byte[] P_1, int P_2)
	{
		A(P_0, P_1, 0, P_2);
	}

	public void A(int P_0, byte[] P_1, int P_2, int P_3)
	{
		DmConvertion.SetBytes(this.m_A, P_0, P_1, P_2, P_3);
	}

	internal void A(byte P_0)
	{
		int num = 1;
		B(num);
		A(this.m_a, P_0);
		this.m_a += num;
	}

	internal void A(short P_0)
	{
		int num = 2;
		B(num);
		A(this.m_a, P_0);
		this.m_a += num;
	}

	internal void H(int P_0)
	{
		int num = 4;
		B(num);
		B(this.m_a, P_0);
		this.m_a += num;
	}

	internal void A(long P_0)
	{
		int num = 8;
		B(num);
		A(this.m_a, P_0);
		this.m_a += num;
	}

	internal void A(float P_0)
	{
		byte[] array = ByteUtil.fromFloat(P_0);
		A(array);
	}

	internal void A(double P_0)
	{
		byte[] array = ByteUtil.fromDouble(P_0);
		A(array);
	}

	internal void h(int P_0)
	{
		int num = 2;
		B(num);
		A(this.m_a, (short)P_0);
		this.m_a += num;
	}

	internal void a(long P_0)
	{
		int num = 4;
		B(num);
		B(this.m_a, (int)P_0);
		this.m_a += num;
	}

	internal void a(byte[] P_0, int P_1, int P_2)
	{
		B(P_2);
		A(this.m_a, P_0, P_1, P_2);
		this.m_a += P_2;
	}

	internal void A(byte[] P_0, int P_1)
	{
		a(P_0, 0, P_1);
	}

	internal void A(byte[] P_0)
	{
		a(P_0, 0, P_0.Length);
	}

	internal void a(byte[] P_0)
	{
		A((short)P_0.Length);
		A(P_0);
	}

	internal void B(byte[] P_0)
	{
		H(P_0.Length);
		A(P_0);
	}

	public void A(string P_0, string P_1)
	{
		byte[] bytes = DmConvertion.GetBytes(P_0, P_1);
		B(bytes);
	}

	internal void a(string P_0, string P_1)
	{
		a(DmConvertion.GetBytes(P_0, P_1));
	}

	internal void B(string P_0, string P_1)
	{
		byte[] bytesWithNTS = DmConvertion.GetBytesWithNTS(P_0, P_1);
		A(bytesWithNTS);
	}

	public byte H()
	{
		byte b2 = this.m_A[0];
		byte b3 = 19;
		byte b4 = this.m_A[1];
		for (byte b5 = 1; b5 < b3; b5++)
		{
			b4 = this.m_A[b5];
			b2 ^= b4;
		}
		return b2;
	}

	public bool h()
	{
		return true;
	}

	public short I(int P_0)
	{
		return C(P_0 + 22);
	}

	public bool i(int P_0)
	{
		return d(P_0 + 12) != 0;
	}

	public short J(int P_0)
	{
		return C(P_0 + 16);
	}

	public short j(int P_0)
	{
		return C(P_0 + 24);
	}

	public short K(int P_0)
	{
		return C(P_0 + 26);
	}

	public short k(int P_0)
	{
		return C(P_0 + 28);
	}

	public short L(int P_0)
	{
		return C(P_0 + 30);
	}

	public int l(int P_0)
	{
		return d(P_0);
	}

	public int M(int P_0)
	{
		return d(P_0 + 4);
	}

	public long m(int P_0)
	{
		return E(P_0 + 8);
	}

	public void b(int P_0, int P_1)
	{
		A(P_1, (short)P_0);
	}

	public void A(int P_0, byte[] P_1)
	{
		A(P_0, P_1, 0, P_1.Length);
	}

	public void A(DmError P_0, string P_1)
	{
		int num = d();
		if (num > 0)
		{
			P_0.Schema = A(num, P_1);
		}
		num = d();
		if (num > 0)
		{
			P_0.Table = A(num, P_1);
		}
		num = d();
		if (num > 0)
		{
			P_0.Col = A(num, P_1);
		}
		num = d();
		if (num > 0)
		{
			P_0.Message = A(num, P_1);
		}
	}

	public void N(int P_0)
	{
		B(0, P_0);
	}

	public void a(short P_0)
	{
		A(4, P_0);
	}

	public short I()
	{
		return C(4);
	}

	public void i()
	{
		B(6, this.m_a - 64);
	}

	public int J()
	{
		return d(6);
	}

	public void a(byte P_0)
	{
		A(19, P_0);
	}

	public int j()
	{
		return d(0);
	}

	public short K()
	{
		return C(4);
	}

	public int k()
	{
		return d(6);
	}

	public void n(int P_0)
	{
		B(6, P_0);
	}

	public int L()
	{
		return d(10);
	}

	public int l()
	{
		return C(14);
	}

	public byte M()
	{
		return b(19);
	}

	public void O(int P_0)
	{
		B(20, P_0);
	}

	public void o(int P_0)
	{
		B(24, P_0);
	}

	public void B(byte P_0)
	{
		A(28, P_0);
	}

	public void b(byte P_0)
	{
		A(29, P_0);
	}

	public void C(byte P_0)
	{
		A(30, P_0);
	}

	public void P(int P_0)
	{
		A(35, (short)P_0);
	}

	public int m()
	{
		return d(20);
	}

	public int N()
	{
		return d(24);
	}

	public int n()
	{
		return d(28);
	}

	public int O()
	{
		return d(32);
	}

	public int o()
	{
		return d(36);
	}

	public byte P()
	{
		return b(40);
	}

	public byte p()
	{
		return b(41);
	}

	public byte Q()
	{
		return b(42);
	}

	public byte q()
	{
		return b(45);
	}

	public short R()
	{
		return C(48);
	}

	public void p(int P_0)
	{
		B(20, P_0);
	}

	public void Q(int P_0)
	{
		B(24, P_0);
	}

	public void q(int P_0)
	{
		B(28, P_0);
	}

	public void c(byte P_0)
	{
		A(40, P_0);
	}

	public void D(byte P_0)
	{
		A(32, P_0);
	}

	public void B(short P_0)
	{
		A(33, P_0);
	}

	public void R(int P_0)
	{
		B(35, P_0);
	}

	public void d(byte P_0)
	{
		A(39, P_0);
	}

	public void E(byte P_0)
	{
		A(41, P_0);
	}

	public void e(byte P_0)
	{
		A(42, P_0);
	}

	public void F(byte P_0)
	{
		A(43, P_0);
	}

	public void f(byte P_0)
	{
		A(44, P_0);
	}

	public int r()
	{
		return d(20);
	}

	public int S()
	{
		return d(24);
	}

	public byte s()
	{
		return b(28);
	}

	public int T()
	{
		return d(29);
	}

	public byte t()
	{
		return b(33);
	}

	public byte U()
	{
		return b(34);
	}

	public byte u()
	{
		return b(39);
	}

	public short V()
	{
		return C(40);
	}

	public byte v()
	{
		return b(42);
	}

	public byte W()
	{
		return b(43);
	}

	public int w()
	{
		return d(44);
	}

	public byte X()
	{
		return b(50);
	}

	public byte x()
	{
		return b(51);
	}

	public byte Y()
	{
		return b(53);
	}

	public int y()
	{
		return C(37);
	}

	public int Z()
	{
		return C(35);
	}

	public void G(byte P_0)
	{
		A(20, P_0);
	}

	public byte z()
	{
		return b(20);
	}

	public void g(byte P_0)
	{
		A(20, P_0);
	}

	public void H(byte P_0)
	{
		A(21, P_0);
	}

	public void h(byte P_0)
	{
		A(22, P_0);
	}

	public void I(byte P_0)
	{
		A(23, P_0);
	}

	public void i(byte P_0)
	{
		A(24, P_0);
	}

	public void b(short P_0)
	{
		A(25, P_0);
	}

	public void B(long P_0)
	{
		A(27, P_0);
	}

	public void J(byte P_0)
	{
		A(35, P_0);
	}

	public void C(short P_0)
	{
		A(36, P_0);
	}

	public void r(int P_0)
	{
		B(41, P_0);
	}

	public short aA()
	{
		return C(20);
	}

	public int aa()
	{
		return D(22);
	}

	public short aB()
	{
		return C(24);
	}

	public int ab()
	{
		return d(34);
	}

	public void j(byte P_0)
	{
		A(20, P_0);
	}

	public void S(int P_0)
	{
		a(21, P_0);
	}

	public void K(byte P_0)
	{
		A(23, P_0);
	}

	public void b(long P_0)
	{
		A(24, P_0);
	}

	public void C(long P_0)
	{
		A(32, P_0);
	}

	public void c(long P_0)
	{
		A(40, P_0);
	}

	public void B(bool P_0)
	{
		A(49, P_0 ? ((byte)1) : ((byte)0));
	}

	public void s(int P_0)
	{
		B(52, P_0);
	}

	public void T(int P_0)
	{
		B(56, P_0);
	}

	public short aC()
	{
		return C(20);
	}

	public short ac()
	{
		return C(22);
	}

	public long aD()
	{
		return e(24);
	}

	public int ad()
	{
		return D(32);
	}

	public byte aE()
	{
		return b(34);
	}

	public int ae()
	{
		return d(35);
	}

	public int aF()
	{
		return d(39);
	}

	public long af()
	{
		return e(43);
	}

	public byte aG()
	{
		return b(43);
	}

	public short ag()
	{
		return C(44);
	}

	public int aH()
	{
		return d(51);
	}

	public int ah()
	{
		return d(55);
	}

	public int aI()
	{
		return d(60);
	}

	public void c(short P_0)
	{
		A(20, P_0);
	}

	public void t(int P_0)
	{
		a(20, P_0);
	}

	public void D(long P_0)
	{
		A(20, P_0);
	}

	public void d(long P_0)
	{
		A(28, P_0);
	}

	public void D(short P_0)
	{
		A(36, P_0);
	}

	public void U(int P_0)
	{
		B(38, P_0);
	}

	public long ai()
	{
		return e(20);
	}

	public int aJ()
	{
		return d(28);
	}

	public void d(short P_0)
	{
		A(20, P_0);
	}

	public void u(int P_0)
	{
		B(20, P_0);
	}

	public void V(int P_0)
	{
		B(24, P_0);
	}

	public void v(int P_0)
	{
		B(28, P_0);
	}

	public void W(int P_0)
	{
		B(32, P_0);
	}

	public void w(int P_0)
	{
		B(20, P_0);
	}

	public void X(int P_0)
	{
		B(20, P_0);
	}

	public void E(short P_0)
	{
		A(20, P_0);
	}

	public short aj()
	{
		return C(20);
	}
}
