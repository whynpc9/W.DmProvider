using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.parser;
namespace W.Dm.Internal.Legacy.A;
internal partial class A
{
	private A(DmConnInstance owner,DmCommand command,bool deferAllocation)
	{
		__t02_field_04000927 = new DmInfo(owner);
		__t02_field_04000923 = owner;
		__t02_field_04000933 = command;
		__t02_field_04000924 = owner.GetCsi();
		K();
		__t02_field_04000931 = command != null ? command.GetStmtSerial() : owner.ConnProperty.IsolationLevel == IsolationLevel.Serializable;
	}
	internal static async Task<A> CreateAsync(DmConnInstance owner,DmCommand command,CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var statement = new A(owner,command,true);
		statement.__t02_field_0400092E = await statement.h().AllocateStatementAsync(statement,statement.__t02_field_04000925,statement.__t02_field_04000926,statement.__t02_field_0400092E,cancellationToken).ConfigureAwait(false);
		long id = statement.__t02_method_06000883();
		if(id < 0) id = 0xFFFFFFFFu & id;
		statement.__t02_method_06000886("DM_CURSOR_" + id);
		owner.AddStmt(statement);
		return statement;
	}
	private async Task CheckReadyAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		b(false);
		if(__t02_field_04000930) DmError.ThrowDmException(DmErrorDefinition.ECNET_STATEMENT_HANDLE_CLOSED);
		if(__t02_field_04000932)
		{
			await CloseAsync(cancellationToken).ConfigureAwait(false);
			DmError.ThrowDmException("执行了主备切换，句柄无效",DmErrorDefinition.ERROR_MASTER_SLAVE_SWITCHED);
		}
	}
	private void SetSql(string sql)
	{
		B(sql);
		if(G().ConnProperty.EscapeProcess)
		{
			try { B(SQLProcessor.escape(C(),G().ConnProperty.ResveredList)); } catch { }
		}
	}
	private async Task CreateReaderAsync(CommandBehavior behavior)
	{
		if(__t02_field_04000929 != null)
		{
			await __t02_field_04000929.CloseAsync().ConfigureAwait(false);
			__t02_field_04000929 = null;
		}
		__t02_method_060008AE(behavior);
	}
	internal async Task<DmDataReader> ExecuteReaderAsync(string sql,CommandBehavior behavior,CancellationToken cancellationToken = default)
	{
		await CheckReadyAsync(cancellationToken).ConfigureAwait(false);
		SetSql(sql); K();
		await h().AAsync(__t02_field_04000925,__t02_field_04000926,this,C(),true,0,cancellationToken).ConfigureAwait(false);
		await CreateReaderAsync(behavior).ConfigureAwait(false);
		return __t02_field_04000929;
	}
	internal async Task<int> ExecuteNonQueryAsync(string sql,CancellationToken cancellationToken = default)
	{
		await CheckReadyAsync(cancellationToken).ConfigureAwait(false);
		SetSql(sql); K();
		await h().AAsync(__t02_field_04000925,__t02_field_04000926,this,C(),true,0,cancellationToken).ConfigureAwait(false);
		return F().GetHasResultSet() ? -1 : (int)F().GetRowCount();
	}
	internal async Task PrepareAsync(string sql,CancellationToken cancellationToken = default)
	{
		await CheckReadyAsync(cancellationToken).ConfigureAwait(false);
		if(sql.Length==0) return;
		SetSql(sql); K();
		await h().AAsync(__t02_field_04000925,__t02_field_04000926,this,C(),false,0,cancellationToken).ConfigureAwait(false);
	}
	internal async Task<DmDataReader> ExecutePreparedReaderAsync(CommandBehavior behavior,CancellationToken cancellationToken = default)
	{
		await CheckReadyAsync(cancellationToken).ConfigureAwait(false); N(); K();
		await h().AAsync(this,F(),cancellationToken).ConfigureAwait(false);
		await CreateReaderAsync(behavior).ConfigureAwait(false);
		return __t02_field_04000929;
	}
	internal async Task<int> ExecutePreparedNonQueryAsync(CancellationToken cancellationToken = default)
	{
		await CheckReadyAsync(cancellationToken).ConfigureAwait(false); N(); K();
		await h().AAsync(this,F(),cancellationToken).ConfigureAwait(false);
		return F().GetHasResultSet() ? -1 : (int)F().GetRowCount();
	}
	internal Task ResetAsync(CancellationToken cancellationToken = default) { K(); return h().AAsync(this,cancellationToken); }
	internal Task CloseAsync(CancellationToken cancellationToken = default) => CloseAsync(true,true,cancellationToken);
	internal async Task CloseAsync(bool remove,bool pool,CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(__t02_field_04000930) return;
		if(pool && G()!=null && (G().ConnProperty.PreparePooling || G().ConnProperty.StmtPooling))
		{
			if(remove) G().RemoveStmt(this);
			if(!G().ReUsedStmt(this)) return;
		}
		if(remove) G()?.RemoveStmt(this);
		try
		{
			K();
			await h().AAsync(__t02_field_04000925,__t02_field_04000926,this,cancellationToken).ConfigureAwait(false);
		}
		finally { o(); }
	}
	internal Task pAsync(CancellationToken cancellationToken = default) => CloseAsync(cancellationToken);
}
