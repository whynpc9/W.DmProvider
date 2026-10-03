using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.parser;

namespace W.Dm.Internal.Legacy.A;

internal partial class A
{
	private int __t02_field_04000922;

	private DmConnInstance __t02_field_04000923;

	private B __t02_field_04000924;

	internal b __t02_field_04000925 = new b();

	internal b __t02_field_04000926 = new b();

	private DmInfo __t02_field_04000927;

	private DmResultSetCache __t02_field_04000928;

	internal DmDataReader __t02_field_04000929;

	private string __t02_field_0400092A;

	private string __t02_field_0400092B;

	private bool __t02_field_0400092C;

	private string __t02_field_0400092D;

	private bool __t02_field_0400092E;

	private byte __t02_field_0400092F = 1;

	private bool __t02_field_04000930;

	internal bool __t02_field_04000931;

	private bool __t02_field_04000932;

	private DmCommand __t02_field_04000933;

	private long __t02_field_04000934;

	private bool __t02_field_04000935;

	private bool __t02_field_04000936;

	private bool __t02_field_04000937;

	private bool __t02_field_04000938;

	private int __t02_field_04000939;

	[SpecialName]
	internal int __t02_method_06000883()
	{
		return __t02_field_04000922;
	}

	[SpecialName]
	internal void __t02_method_06000884(int P_0)
	{
		__t02_field_04000922 = P_0;
	}

	[SpecialName]
	internal string a()
	{
		return __t02_field_0400092B;
	}

	[SpecialName]
	internal void __t02_method_06000886(string P_0)
	{
		__t02_field_0400092B = P_0;
	}

	[SpecialName]
	internal string B()
	{
		return a();
	}

	[SpecialName]
	internal void a(string P_0)
	{
		__t02_method_06000886(P_0);
	}

	[SpecialName]
	public bool b()
	{
		return __t02_field_0400092C;
	}

	[SpecialName]
	public void __t02_method_0600088A(bool P_0)
	{
		__t02_field_0400092C = P_0;
	}

	[SpecialName]
	public string C()
	{
		return __t02_field_0400092D;
	}

	[SpecialName]
	public void B(string P_0)
	{
		__t02_field_0400092D = P_0;
	}

	[SpecialName]
	internal bool c()
	{
		return __t02_field_0400092E;
	}

	[SpecialName]
	internal void a(bool P_0)
	{
		__t02_field_0400092E = P_0;
	}

	[SpecialName]
	public byte D()
	{
		return __t02_field_0400092F;
	}

	[SpecialName]
	public void __t02_method_06000890(byte P_0)
	{
		__t02_field_0400092F = P_0;
	}

	[SpecialName]
	public void B(bool P_0)
	{
		__t02_field_04000932 = P_0;
	}

	[SpecialName]
	public bool d()
	{
		return __t02_field_04000932;
	}

	[SpecialName]
	internal bool E()
	{
		return __t02_field_04000936;
	}

	[SpecialName]
	internal void b(bool P_0)
	{
		__t02_field_04000936 = P_0;
	}

	[SpecialName]
	internal bool e()
	{
		return __t02_field_04000935;
	}

	[SpecialName]
	internal void C(bool P_0)
	{
		__t02_field_04000935 = false;
	}

	[SpecialName]
	internal DmInfo F()
	{
		return __t02_field_04000927;
	}

	[SpecialName]
	internal void __t02_method_06000898(DmInfo P_0)
	{
		__t02_field_04000927 = P_0;
	}

	[SpecialName]
	internal DmCommand f()
	{
		return __t02_field_04000933;
	}

	[SpecialName]
	internal void __t02_method_0600089A(DmCommand P_0)
	{
		__t02_field_04000933 = P_0;
	}

	[SpecialName]
	internal DmConnInstance G()
	{
		return __t02_field_04000923;
	}

	[SpecialName]
	internal int g()
	{
		return __t02_method_06000883();
	}

	[SpecialName]
	internal void a(int P_0)
	{
		__t02_method_06000884(g());
	}

	[SpecialName]
	internal DmInfo H()
	{
		return __t02_field_04000927;
	}

	[SpecialName]
	internal B h()
	{
		return __t02_field_04000924;
	}

	[SpecialName]
	internal int I()
	{
		return __t02_field_04000939;
	}

	[SpecialName]
	internal void B(int P_0)
	{
		__t02_field_04000939 = P_0;
	}

	[SpecialName]
	internal bool i()
	{
		return __t02_field_04000937;
	}

	[SpecialName]
	internal void c(bool P_0)
	{
		__t02_field_04000937 = P_0;
	}

	[SpecialName]
	internal bool J()
	{
		return __t02_field_04000938;
	}

	[SpecialName]
	internal void D(bool P_0)
	{
		__t02_field_04000938 = P_0;
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
		if (__t02_field_04000933 != null)
		{
			__t02_field_04000939 = __t02_field_04000933.do_CommandTimeout;
		}
	}

	internal void b(int P_0)
	{
		__t02_field_04000939 = P_0;
	}

	private void k()
	{
		__t02_field_04000924 = __t02_field_04000923.GetCsi();
		__t02_field_04000930 = false;
		__t02_field_04000932 = false;
		K();
		__t02_field_04000924.A(this, __t02_field_04000925, __t02_field_04000926, ref __t02_field_0400092E);
		long num = __t02_method_06000883();
		if (__t02_method_06000883() < 0)
		{
			num = 0xFFFFFFFFu & num;
		}
		__t02_method_06000886("DM_CURSOR_" + num);
	}

	public A(DmConnInstance P_0, DmCommand P_1)
	{
		__t02_field_04000927 = new DmInfo(P_0);
		__t02_field_04000923 = P_0;
		__t02_field_04000933 = P_1;
		k();
		P_0.AddStmt(this);
		__t02_field_04000939 = P_1.do_CommandTimeout;
		K();
		if (P_1 != null)
		{
			__t02_field_04000931 = P_1.GetStmtSerial();
		}
		else
		{
			__t02_field_04000931 = __t02_field_04000923.ConnProperty.IsolationLevel == IsolationLevel.Serializable;
		}
	}

	internal void b(string P_0)
	{
		__t02_field_0400092A = P_0;
	}

	internal string L()
	{
		return __t02_field_0400092A;
	}

	internal void __t02_method_060008AE(CommandBehavior P_0)
	{
		if (__t02_field_04000929 != null)
		{
			__t02_field_04000929.do_Close();
		}
		if (f() != null)
		{
			if (f().RetRefCursorStmt != null)
			{
				__t02_field_04000929 = new DmDataReader(f().RetRefCursorStmt.__t02_field_04000928, f().RetRefCursorStmt.__t02_field_04000927, P_0);
				__t02_field_04000929.m_StartRow = 0L;
				return;
			}
			if (f().RefCursorStmtArr != null && f().RefCursorStmtArr.Count > 0)
			{
				A a2 = (A)f().RefCursorStmtArr[f().RefCursorStmtArr_cur];
				f().IncRefCur();
				if (a2 != null && a2.__t02_field_04000928 != null && a2.__t02_field_04000927 != null)
				{
					__t02_field_04000929 = new DmDataReader(a2.__t02_field_04000928, a2.__t02_field_04000927, P_0);
					__t02_field_04000929.m_StartRow = 0L;
					return;
				}
			}
		}
		if (!__t02_field_04000927.GetHasResultSet())
		{
			__t02_field_04000929 = new DmDataReader(__t02_field_04000927, P_0, this);
			__t02_field_04000929.m_StartRow = 0L;
			return;
		}
		if (__t02_field_04000928 == null)
		{
			__t02_field_04000928 = new DmResultSetCache(this, __t02_field_04000927.GetColumnsInfo().Length, __t02_field_04000927.GetRowCount());
		}
		__t02_field_04000929 = new DmDataReader(__t02_field_04000928, __t02_field_04000927, P_0);
		__t02_field_04000929.m_StartRow = 0L;
	}

	[SpecialName]
	public DmResultSetCache l()
	{
		return __t02_field_04000928;
	}

	[SpecialName]
	public void __t02_method_060008B0(DmResultSetCache P_0)
	{
		__t02_field_04000928 = P_0;
	}

	public DmDataReader __t02_method_060008B1(string P_0, CommandBehavior P_1)
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
                // Parsing above is local and may fall back. Once this execution
                // enters the wire, its failure must propagate without replay.
                __t02_field_04000924.A(this, list);
			}
			else
			{
				C(P_0);
				K();
				__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, C(), true, 0);
			}
		}
		else
		{
			K();
			__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, C(), true, 0);
		}
		__t02_method_060008AE(P_1);
		return __t02_field_04000929;
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
                // Parsing above is local and may fall back. Once this execution
                // enters the wire, its failure must propagate without replay.
                __t02_field_04000924.A(this, list);
			}
			else
			{
				B(P_0);
				K();
				__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, C(), true, 0);
			}
		}
		else
		{
			K();
			__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, C(), true, 0);
		}
		if (__t02_field_04000927.GetHasResultSet())
		{
			return -1;
		}
		return (int)__t02_field_04000927.GetRowCount();
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
		__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, C(), false, 0);
	}

	public DmDataReader a(CommandBehavior P_0)
	{
		Q();
		N();
		K();
		__t02_field_04000924.A(this, __t02_field_04000927);
		__t02_method_060008AE(P_0);
		return __t02_field_04000929;
	}

	public int M()
	{
		Q();
		N();
		K();
		__t02_field_04000924.A(this, __t02_field_04000927);
		if (__t02_field_04000927.GetHasResultSet())
		{
			return -1;
		}
		return (int)__t02_field_04000927.GetRowCount();
	}

	internal DmInfo m()
	{
		return __t02_field_04000927;
	}

	private void N()
	{
		DmParameterInternal[] ParamsInfo = null;
		__t02_field_04000927.GetParamsInfo(out ParamsInfo);
		if (ParamsInfo == null)
		{
			return;
		}
		if (__t02_field_04000933.CommandType == CommandType.StoredProcedure && !__t02_field_04000923.ConnProperty.BatchNotOnCall && (ParamsInfo.Length > 1 || ParamsInfo[0].GetParamValue().Count > 1))
		{
			throw new InvalidOperationException("ERROR: Binding multi-rows is not allowed since batchNotOnCall is false");
		}
		for (int i = 0; i < __t02_field_04000927.GetParameterCount(); i++)
		{
			if (ParamsInfo[i].GetInOutType() != 1 && !ParamsInfo[i].GetInDataBound())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_UNBINDED_PARAMETER);
			}
		}
	}

	public void n()
	{
		__t02_field_04000927 = null;
		__t02_field_04000928 = null;
		__t02_field_04000935 = false;
		__t02_field_04000934 = 0L;
		__t02_field_04000936 = false;
		__t02_field_04000932 = false;
	}

	public void O()
	{
		K();
		__t02_field_04000924.A(this);
	}

	public void o()
	{
		if (__t02_field_04000933 != null)
		{
			__t02_field_04000933.Statement = null;
			__t02_field_04000933.RefCursorStmtArr.Clear();
			__t02_field_04000933.RetRefCursorStmt = null;
		}
		__t02_field_04000923 = null;
		__t02_field_04000924 = null;
		__t02_field_04000929 = null;
		__t02_field_04000930 = true;
		__t02_field_04000932 = false;
		__t02_field_04000927 = null;
	}

	public bool P()
	{
		return __t02_field_04000930;
	}

	public void p()
	{
		if (__t02_field_04000930)
		{
			return;
		}
		if (__t02_field_04000923 != null)
		{
			__t02_field_04000923.RemoveStmt(this);
			if ((__t02_field_04000923.ConnProperty.PreparePooling || __t02_field_04000923.ConnProperty.StmtPooling) && !__t02_field_04000923.ReUsedStmt(this))
			{
				return;
			}
		}
		try
		{
			if (__t02_field_04000933 != null)
			{
				K();
			}
			__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this);
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

	public void __t02_method_060008BE(bool P_0, bool P_1)
	{
		if (__t02_field_04000930)
		{
			return;
		}
		if (P_1 && __t02_field_04000923 != null && (__t02_field_04000923.ConnProperty.PreparePooling || __t02_field_04000923.ConnProperty.StmtPooling))
		{
			if (P_0)
			{
				__t02_field_04000923.RemoveStmt(this);
			}
			if (!__t02_field_04000923.ReUsedStmt(this))
			{
				return;
			}
		}
		if (P_0)
		{
			__t02_field_04000923.RemoveStmt(this);
		}
		try
		{
			if (__t02_field_04000933 != null)
			{
				K();
			}
			__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this);
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
		if (__t02_field_04000930)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_STATEMENT_HANDLE_CLOSED);
		}
		if (__t02_field_04000932)
		{
			p();
			DmError.ThrowDmException("执行了主备切换，句柄无效", DmErrorDefinition.ERROR_MASTER_SLAVE_SWITCHED);
		}
	}

	internal void d(string P_0)
	{
		if (P_0.Length == 0 || P_0.Equals(" ") || __t02_field_0400092B.Equals(P_0))
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
		}
		K();
		__t02_field_04000924.A(__t02_field_04000925, __t02_field_04000926, this, P_0);
		__t02_method_06000886(P_0);
	}

	internal string q()
	{
		return B();
	}

	internal long R()
	{
		if (l() == null)
		{
			__t02_field_04000934 = -1L;
		}
		else
		{
			__t02_field_04000934 = l().CursorUpdateRow();
		}
		return __t02_field_04000934;
	}
}
