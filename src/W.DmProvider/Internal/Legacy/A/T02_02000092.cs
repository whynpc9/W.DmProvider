using System;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.util;

namespace W.Dm.Internal.Legacy.A;

internal class b
{
	private byte[] __t02_field_04000AC3;

	private int __t02_field_04000AC4;

	private int __t02_field_04000AC5;

	[SpecialName]
	internal byte[] A()
	{
		return __t02_field_04000AC3;
	}

	[SpecialName]
	internal int a()
	{
		return __t02_field_04000AC4;
	}

	[SpecialName]
	internal int B()
	{
		return __t02_field_04000AC5;
	}

	[SpecialName]
	internal void A(int P_0)
	{
		__t02_field_04000AC5 = P_0;
	}

	internal b()
	{
		__t02_field_04000AC3 = new byte[32640];
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
		__t02_field_04000AC3 = P_0;
	}

	internal void a(int P_0)
	{
		__t02_field_04000AC5 = P_0;
		__t02_field_04000AC4 = 0;
		Array.Clear(__t02_field_04000AC3, 0, P_0);
	}

	internal void B(int P_0)
	{
		if (__t02_field_04000AC5 + P_0 > __t02_field_04000AC3.Length)
		{
			byte[] array = new byte[__t02_field_04000AC3.Length * 2 + P_0];
			Array.Copy(__t02_field_04000AC3, 0, array, 0, __t02_field_04000AC5);
			__t02_field_04000AC3 = array;
		}
	}

	internal byte __t02_method_06000AB4(int P_0)
	{
		return __t02_field_04000AC3[P_0];
	}

	internal short C(int P_0)
	{
		int num = 0xFF & __t02_field_04000AC3[P_0];
		P_0++;
		int num2 = 0xFF & __t02_field_04000AC3[P_0];
		num2 = num | (num2 << 8);
		return (short)(0xFFFF & num2);
	}

	internal ushort c(int P_0)
	{
		int num = 0xFF & __t02_field_04000AC3[P_0];
		P_0++;
		int num2 = 0xFF & __t02_field_04000AC3[P_0];
		num2 = num | (num2 << 8);
		return (ushort)(0xFFFF & num2);
	}

	internal int D(int P_0)
	{
		int num = 0xFF & __t02_field_04000AC3[P_0];
		P_0++;
		int num2 = 0xFF & __t02_field_04000AC3[P_0];
		return num | (num2 << 8);
	}

	internal int d(int P_0)
	{
		int num = 4;
		int num2 = P_0 + num;
		long num3 = 0xFF & __t02_field_04000AC3[--num2];
		num3 = (0xFF & __t02_field_04000AC3[--num2]) | (num3 << 8);
		num3 = (0xFF & __t02_field_04000AC3[--num2]) | (num3 << 8);
		num3 = (0xFF & __t02_field_04000AC3[--num2]) | (num3 << 8);
		return (int)(0xFFFFFFFFu & num3);
	}

	internal long E(int P_0)
	{
		return (__t02_field_04000AC3[P_0++] & 0xFF) | ((long)(__t02_field_04000AC3[P_0++] & 0xFF) << 8) | ((long)(__t02_field_04000AC3[P_0++] & 0xFF) << 16) | ((long)(__t02_field_04000AC3[P_0++] & 0xFF) << 24);
	}

	internal long e(int P_0)
	{
		long num = 0L;
		int num2 = P_0 + 8;
		for (int i = 0; i < 8; i++)
		{
			num = (0xFF & __t02_field_04000AC3[--num2]) | (num << 8);
		}
		return num;
	}

	internal byte[] A(int P_0, int P_1)
	{
		return DmConvertion.GetBytes(__t02_field_04000AC3, P_0, P_1);
	}

	internal string A(int P_0, int P_1, string P_2)
	{
		return DmConvertion.GetString(__t02_field_04000AC3, P_0, P_1, P_2);
	}

	internal byte __t02_method_06000ABD()
	{
		byte result = __t02_method_06000AB4(__t02_field_04000AC4);
		__t02_field_04000AC4++;
		return result;
	}

	internal short C()
	{
		short result = C(__t02_field_04000AC4);
		__t02_field_04000AC4 += 2;
		return result;
	}

	internal ushort c()
	{
		ushort result = c(__t02_field_04000AC4);
		__t02_field_04000AC4 += 2;
		return result;
	}

	internal int D()
	{
		int result = D(__t02_field_04000AC4);
		__t02_field_04000AC4 += 2;
		return result;
	}

	internal int d()
	{
		int result = d(__t02_field_04000AC4);
		__t02_field_04000AC4 += 4;
		return result;
	}

	internal long E()
	{
		long result = E(__t02_field_04000AC4);
		__t02_field_04000AC4 += 4;
		return result;
	}

	internal long e()
	{
		long result = e(__t02_field_04000AC4);
		__t02_field_04000AC4 += 8;
		return result;
	}

	internal float F()
	{
		float result = ByteUtil.toFloat(A(__t02_field_04000AC4, 4));
		__t02_field_04000AC4 += 4;
		return result;
	}

	internal double f()
	{
		double result = ByteUtil.toDouble(A(__t02_field_04000AC4, 8));
		__t02_field_04000AC4 += 8;
		return result;
	}

	internal byte[] F(int P_0)
	{
		byte[] result = A(__t02_field_04000AC4, P_0);
		__t02_field_04000AC4 += P_0;
		return result;
	}

	internal byte[] A(byte[] P_0, int P_1, int P_2)
	{
		byte[] sourceArray = A(__t02_field_04000AC4, P_2);
		__t02_field_04000AC4 += P_2;
		Array.Copy(sourceArray, 0, P_0, P_1, P_2);
		return P_0;
	}

	internal byte[] G()
	{
		return F(d());
	}

	internal string A(int P_0, string P_1)
	{
		string result = A(__t02_field_04000AC4, P_0, P_1);
		__t02_field_04000AC4 += P_0;
		return result;
	}

	public string A(string P_0)
	{
		return ByteUtil.toString(G(), P_0);
	}

	internal string a(string P_0)
	{
		return A(__t02_method_06000ABD(), P_0);
	}

	internal string B(string P_0)
	{
		return A(C(), P_0);
	}

	internal string __t02_method_06000ACD(string P_0)
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
			return __t02_field_04000AC5 - __t02_field_04000AC4;
		}
		return __t02_field_04000AC3.Length - __t02_field_04000AC5;
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
		__t02_field_04000AC4 += P_0;
	}

	internal int g()
	{
		return __t02_field_04000AC5;
	}

	internal void G(int P_0)
	{
		__t02_field_04000AC4 = P_0;
	}

	internal void g(int P_0)
	{
		B(P_0);
		__t02_field_04000AC5 += P_0;
	}

	public void A(int P_0, byte P_1)
	{
		__t02_field_04000AC3[P_0] = P_1;
	}

	public void A(int P_0, short P_1)
	{
		__t02_field_04000AC3[P_0] = (byte)(P_1 & 0xFF);
		P_0++;
		P_1 >>= 8;
		__t02_field_04000AC3[P_0] = (byte)(P_1 & 0xFF);
	}

	public void a(int P_0, int P_1)
	{
		__t02_field_04000AC3[P_0] = (byte)(P_1 & 0xFF);
		P_0++;
		P_1 >>= 8;
		__t02_field_04000AC3[P_0] = (byte)(P_1 & 0xFF);
	}

	public void B(int P_0, int P_1)
	{
		__t02_field_04000AC3[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		__t02_field_04000AC3[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		__t02_field_04000AC3[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
		__t02_field_04000AC3[P_0++] = (byte)(P_1 & 0xFF);
		P_1 >>= 8;
	}

	public void A(int P_0, long P_1)
	{
		int num = 8;
		int num2 = P_0;
		while (num-- > 0)
		{
			__t02_field_04000AC3[num2++] = (byte)(P_1 & 0xFF);
			P_1 >>= 8;
		}
	}

	public void A(int P_0, byte[] P_1, int P_2)
	{
		A(P_0, P_1, 0, P_2);
	}

	public void A(int P_0, byte[] P_1, int P_2, int P_3)
	{
		DmConvertion.SetBytes(__t02_field_04000AC3, P_0, P_1, P_2, P_3);
	}

	internal void A(byte P_0)
	{
		int num = 1;
		B(num);
		A(__t02_field_04000AC5, P_0);
		__t02_field_04000AC5 += num;
	}

	internal void A(short P_0)
	{
		int num = 2;
		B(num);
		A(__t02_field_04000AC5, P_0);
		__t02_field_04000AC5 += num;
	}

	internal void H(int P_0)
	{
		int num = 4;
		B(num);
		B(__t02_field_04000AC5, P_0);
		__t02_field_04000AC5 += num;
	}

	internal void A(long P_0)
	{
		int num = 8;
		B(num);
		A(__t02_field_04000AC5, P_0);
		__t02_field_04000AC5 += num;
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
		A(__t02_field_04000AC5, (short)P_0);
		__t02_field_04000AC5 += num;
	}

	internal void a(long P_0)
	{
		int num = 4;
		B(num);
		B(__t02_field_04000AC5, (int)P_0);
		__t02_field_04000AC5 += num;
	}

	internal void a(byte[] P_0, int P_1, int P_2)
	{
		B(P_2);
		A(__t02_field_04000AC5, P_0, P_1, P_2);
		__t02_field_04000AC5 += P_2;
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
		byte b2 = __t02_field_04000AC3[0];
		byte b3 = 19;
		byte b4 = __t02_field_04000AC3[1];
		for (byte b5 = 1; b5 < b3; b5++)
		{
			b4 = __t02_field_04000AC3[b5];
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

	public void __t02_method_06000AF8(int P_0, int P_1)
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
		B(6, __t02_field_04000AC5 - 64);
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
		return __t02_method_06000AB4(19);
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

	public void __t02_method_06000B0B(byte P_0)
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
		return __t02_method_06000AB4(40);
	}

	public byte p()
	{
		return __t02_method_06000AB4(41);
	}

	public byte Q()
	{
		return __t02_method_06000AB4(42);
	}

	public byte q()
	{
		return __t02_method_06000AB4(45);
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
		return __t02_method_06000AB4(28);
	}

	public int T()
	{
		return d(29);
	}

	public byte t()
	{
		return __t02_method_06000AB4(33);
	}

	public byte U()
	{
		return __t02_method_06000AB4(34);
	}

	public byte u()
	{
		return __t02_method_06000AB4(39);
	}

	public short V()
	{
		return C(40);
	}

	public byte v()
	{
		return __t02_method_06000AB4(42);
	}

	public byte W()
	{
		return __t02_method_06000AB4(43);
	}

	public int w()
	{
		return d(44);
	}

	public byte X()
	{
		return __t02_method_06000AB4(50);
	}

	public byte x()
	{
		return __t02_method_06000AB4(51);
	}

	public byte Y()
	{
		return __t02_method_06000AB4(53);
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
		return __t02_method_06000AB4(20);
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

	public void __t02_method_06000B3B(short P_0)
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

	public void __t02_method_06000B47(long P_0)
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
		return __t02_method_06000AB4(34);
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
		return __t02_method_06000AB4(43);
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
