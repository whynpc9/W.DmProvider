using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Dm;
using Dm.parser;

namespace A;

internal class A
{
	private int m_A;

	private DmConnInstance m_A;

	private B m_A;

	internal b A = new b();

	internal b a = new b();

	private DmInfo m_A;

	private DmResultSetCache m_A;

	internal DmDataReader A;

	private string m_A;

	private string m_a;

	private bool m_A;

	private string m_B;

	private bool m_a;

	private byte m_A = 1;

	private bool m_B;

	internal bool b;

	private bool m_C;

	private DmCommand m_A;

	private long m_A;

	private bool m_c;

	private bool m_D;

	private bool m_d;

	private bool m_E;

	private int m_a;

	[SpecialName]
	internal int A()
	{
		return this.m_A;
	}

	[SpecialName]
	internal void A(int P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	internal string a()
	{
		return this.m_a;
	}

	[SpecialName]
	internal void A(string P_0)
	{
		this.m_a = P_0;
	}

	[SpecialName]
	internal string B()
	{
		return a();
	}

	[SpecialName]
	internal void a(string P_0)
	{
		A(P_0);
	}

	[SpecialName]
	public bool b()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(bool P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	public string C()
	{
		return this.m_B;
	}

	[SpecialName]
	public void B(string P_0)
	{
		this.m_B = P_0;
	}

	[SpecialName]
	internal bool c()
	{
		return this.m_a;
	}

	[SpecialName]
	internal void a(bool P_0)
	{
		this.m_a = P_0;
	}

	[SpecialName]
	public byte D()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(byte P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	public void B(bool P_0)
	{
		this.m_C = P_0;
	}

	[SpecialName]
	public bool d()
	{
		return this.m_C;
	}

	[SpecialName]
	internal bool E()
	{
		return this.m_D;
	}

	[SpecialName]
	internal void b(bool P_0)
	{
		this.m_D = P_0;
	}

	[SpecialName]
	internal bool e()
	{
		return this.m_c;
	}

	[SpecialName]
	internal void C(bool P_0)
	{
		this.m_c = false;
	}

	[SpecialName]
	internal DmInfo F()
	{
		return this.m_A;
	}

	[SpecialName]
	internal void A(DmInfo P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	internal DmCommand f()
	{
		return this.m_A;
	}

	[SpecialName]
	internal void A(DmCommand P_0)
	{
		this.m_A = P_0;
	}

	[SpecialName]
	internal DmConnInstance G()
	{
		return this.m_A;
	}

	[SpecialName]
	internal int g()
	{
		return A();
	}

	[SpecialName]
	internal void a(int P_0)
	{
		A(g());
	}

	[SpecialName]
	internal DmInfo H()
	{
		return this.m_A;
	}

	[SpecialName]
	internal B h()
	{
		return this.m_A;
	}

	[SpecialName]
	internal int I()
	{
		return this.m_a;
	}

	[SpecialName]
	internal void B(int P_0)
	{
		this.m_a = P_0;
	}

	[SpecialName]
	internal bool i()
	{
		return this.m_d;
	}

	[SpecialName]
	internal void c(bool P_0)
	{
		this.m_d = P_0;
	}

	[SpecialName]
	internal bool J()
	{
		return this.m_E;
	}

	[SpecialName]
	internal void D(bool P_0)
	{
		this.m_E = P_0;
	}

	[SpecialName]
	public bool j()
	{
		return i();
	}

	[SpecialName]
	public void d(bool P_0)
	{
		c(P_0);
	}

	internal void K()
	{
		if (this.m_A != null)
		{
			this.m_a = this.m_A.do_CommandTimeout;
		}
	}

	internal void b(int P_0)
	{
		this.m_a = P_0;
	}

	private void k()
	{
		this.m_A = this.m_A.GetCsi();
		this.m_B = false;
		this.m_C = false;
		K();
		this.m_A.A(this, this.A, this.a, ref this.m_a);
		long num = A();
		if (A() < 0)
		{
			num = 0xFFFFFFFFu & num;
		}
		A("DM_CURSOR_" + num);
	}

	public A(DmConnInstance P_0, DmCommand P_1)
	{
		this.m_A = new DmInfo(P_0);
		this.m_A = P_0;
		this.m_A = P_1;
		k();
		P_0.AddStmt(this);
		this.m_a = P_1.do_CommandTimeout;
		K();
		if (P_1 != null)
		{
			this.b = P_1.GetStmtSerial();
		}
		else
		{
			this.b = this.m_A.ConnProperty.IsolationLevel == IsolationLevel.Serializable;
		}
	}

	internal void b(string P_0)
	{
		this.m_A = P_0;
	}

	internal string L()
	{
		return this.m_A;
	}

	internal void A(CommandBehavior P_0)
	{
		if (this.A != null)
		{
			this.A.do_Close();
		}
		if (f() != null)
		{
			if (f().RetRefCursorStmt != null)
			{
				this.A = new DmDataReader(f().RetRefCursorStmt.m_A, f().RetRefCursorStmt.m_A, P_0);
				this.A.m_StartRow = 0L;
				return;
			}
			if (f().RefCursorStmtArr != null && f().RefCursorStmtArr.Count > 0)
			{
				A a2 = (A)f().RefCursorStmtArr[f().RefCursorStmtArr_cur];
				f().IncRefCur();
				if (a2 != null && a2.m_A != null && a2.m_A != null)
				{
					this.A = new DmDataReader(a2.m_A, a2.m_A, P_0);
					this.A.m_StartRow = 0L;
					return;
				}
			}
		}
		if (!this.m_A.GetHasResultSet())
		{
			this.A = new DmDataReader(this.m_A, P_0, this);
			this.A.m_StartRow = 0L;
			return;
		}
		if (this.m_A == null)
		{
			this.m_A = new DmResultSetCache(this, this.m_A.GetColumnsInfo().Length, this.m_A.GetRowCount());
		}
		this.A = new DmDataReader(this.m_A, this.m_A, P_0);
		this.A.m_StartRow = 0L;
	}

	[SpecialName]
	public DmResultSetCache l()
	{
		return this.m_A;
	}

	[SpecialName]
	public void A(DmResultSetCache P_0)
	{
		this.m_A = P_0;
	}

	public DmDataReader A(string P_0, CommandBehavior P_1)
	{
		Q();
		B(P_0);
		if (G().ConnProperty.EscapeProcess)
		{
			try
			{
				B(SQLProcessor.escape(C(), G().ConnProperty.ResveredList));
			}
			catch (Exception)
			{
			}
		}
		if (G().ConnProperty.C2p == 1)
		{
			List<SQLProcessor.Parameter> list = new List<SQLProcessor.Parameter>();
			try
			{
				B(SQLProcessor.execOpt(C(), list, G().ConnProperty.ServerEncoding));
			}
			catch (Exception)
			{
			}
			if (list.Count > 0)
			{
				try
				{
					this.m_A.A(this, list);
				}
				catch (Exception)
				{
					C(P_0);
					K();
					this.m_A.A(this.A, this.a, this, C(), true, 0);
				}
			}
			else
			{
				C(P_0);
				K();
				this.m_A.A(this.A, this.a, this, C(), true, 0);
			}
		}
		else
		{
			K();
			this.m_A.A(this.A, this.a, this, C(), true, 0);
		}
		A(P_1);
		return this.A;
	}

	private void C(string P_0)
	{
		if (!G().ConnProperty.EscapeProcess)
		{
			B(P_0);
		}
	}

	public int c(string P_0)
	{
		Q();
		B(P_0);
		if (G().ConnProperty.EscapeProcess)
		{
			try
			{
				B(SQLProcessor.escape(C(), G().ConnProperty.ResveredList));
			}
			catch (Exception)
			{
			}
		}
		if (G().ConnProperty.C2p == 1)
		{
			List<SQLProcessor.Parameter> list = new List<SQLProcessor.Parameter>();
			try
			{
				B(SQLProcessor.execOpt(C(), list, G().ConnProperty.ServerEncoding));
			}
			catch (Exception)
			{
			}
			if (list.Count > 0)
			{
				try
				{
					this.m_A.A(this, list);
				}
				catch (Exception)
				{
					B(P_0);
					K();
					this.m_A.A(this.A, this.a, this, C(), true, 0);
				}
			}
			else
			{
				B(P_0);
				K();
				this.m_A.A(this.A, this.a, this, C(), true, 0);
			}
		}
		else
		{
			K();
			this.m_A.A(this.A, this.a, this, C(), true, 0);
		}
		if (this.m_A.GetHasResultSet())
		{
			return -1;
		}
		return (int)this.m_A.GetRowCount();
	}

	public void D(string P_0)
	{
		Q();
		if (P_0.Equals(""))
		{
			return;
		}
		K();
		B(P_0);
		if (G().ConnProperty.EscapeProcess)
		{
			try
			{
				B(SQLProcessor.escape(C(), G().ConnProperty.ResveredList));
			}
			catch (Exception)
			{
			}
		}
		this.m_A.A(this.A, this.a, this, C(), false, 0);
	}

	public DmDataReader a(CommandBehavior P_0)
	{
		Q();
		N();
		K();
		this.m_A.A(this, this.m_A);
		A(P_0);
		return this.A;
	}

	public int M()
	{
		Q();
		N();
		K();
		this.m_A.A(this, this.m_A);
		if (this.m_A.GetHasResultSet())
		{
			return -1;
		}
		return (int)this.m_A.GetRowCount();
	}

	internal DmInfo m()
	{
		return this.m_A;
	}

	private void N()
	{
		DmParameterInternal[] ParamsInfo = null;
		this.m_A.GetParamsInfo(out ParamsInfo);
		if (ParamsInfo == null)
		{
			return;
		}
		if (this.m_A.CommandType == CommandType.StoredProcedure && !this.m_A.ConnProperty.BatchNotOnCall && (ParamsInfo.Length > 1 || ParamsInfo[0].GetParamValue().Count > 1))
		{
			throw new InvalidOperationException("ERROR: Binding multi-rows is not allowed since batchNotOnCall is false");
		}
		for (int i = 0; i < this.m_A.GetParameterCount(); i++)
		{
			if (ParamsInfo[i].GetInOutType() != 1 && !ParamsInfo[i].GetInDataBound())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_UNBINDED_PARAMETER);
			}
		}
	}

	public void n()
	{
		this.m_A = null;
		this.m_A = null;
		this.m_c = false;
		this.m_A = 0L;
		this.m_D = false;
		this.m_C = false;
	}

	public void O()
	{
		K();
		this.m_A.A(this);
	}

	public void o()
	{
		if (this.m_A != null)
		{
			this.m_A.Statement = null;
			this.m_A.RefCursorStmtArr.Clear();
			this.m_A.RetRefCursorStmt = null;
		}
		this.m_A = null;
		this.m_A = null;
		this.A = null;
		this.m_B = true;
		this.m_C = false;
		this.m_A = null;
	}

	public bool P()
	{
		return this.m_B;
	}

	public void p()
	{
		if (this.m_B)
		{
			return;
		}
		if (this.m_A != null)
		{
			this.m_A.RemoveStmt(this);
			if ((this.m_A.ConnProperty.PreparePooling || this.m_A.ConnProperty.StmtPooling) && !this.m_A.ReUsedStmt(this))
			{
				return;
			}
		}
		try
		{
			if (this.m_A != null)
			{
				K();
			}
			this.m_A.A(this.A, this.a, this);
		}
		catch (Exception ex)
		{
			throw ex;
		}
		finally
		{
			o();
		}
	}

	public void A(bool P_0, bool P_1)
	{
		if (this.m_B)
		{
			return;
		}
		if (P_1 && this.m_A != null && (this.m_A.ConnProperty.PreparePooling || this.m_A.ConnProperty.StmtPooling))
		{
			if (P_0)
			{
				this.m_A.RemoveStmt(this);
			}
			if (!this.m_A.ReUsedStmt(this))
			{
				return;
			}
		}
		if (P_0)
		{
			this.m_A.RemoveStmt(this);
		}
		try
		{
			if (this.m_A != null)
			{
				K();
			}
			this.m_A.A(this.A, this.a, this);
		}
		catch (Exception ex)
		{
			throw ex;
		}
		finally
		{
			o();
		}
	}

	private void Q()
	{
		b(false);
		if (this.m_B)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_STATEMENT_HANDLE_CLOSED);
		}
		if (this.m_C)
		{
			p();
			DmError.ThrowDmException("执行了主备切换，句柄无效", DmErrorDefinition.ERROR_MASTER_SLAVE_SWITCHED);
		}
	}

	internal void d(string P_0)
	{
		if (P_0.Length == 0 || P_0.Equals(" ") || this.m_a.Equals(P_0))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		K();
		this.m_A.A(this.A, this.a, this, P_0);
		A(P_0);
	}

	internal string q()
	{
		return B();
	}

	internal long R()
	{
		if (l() == null)
		{
			this.m_A = -1L;
		}
		else
		{
			this.m_A = l().CursorUpdateRow();
		}
		return this.m_A;
	}
}
internal class a
{
	internal virtual void A(b P_0, int P_1, bool P_2, bool P_3)
	{
	}

	internal virtual b a(b P_0, int P_1, bool P_2, bool P_3)
	{
		return null;
	}
}
