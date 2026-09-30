using System.Data;
using System.Data.Common;
using W.Dm.Config;

namespace W.Dm.filter.rw;

internal class RWFilter2 : BaseFilter
{
	internal static RWFilter2 Instance = new RWFilter2();

	private RWFilter2()
	{
	}

	public override void Open(DmConnection conn)
	{
		if (conn.do_State != ConnectionState.Open)
		{
			conn.ConnProperty.LoginMode = LoginModeFlag.onlyprimary;
			base.Open(conn);
			conn.RWInfo.rwCounter = RWCounter.getInstance(conn);
			RWUtil2.connectStandby(conn);
		}
	}

	public override void Close(DmConnection conn)
	{
		if (conn.do_State == ConnectionState.Closed)
		{
			return;
		}
		try
		{
			if (RWUtil2.isStandbyAlive(conn))
			{
				conn.RWInfo.connStandby.do_Close();
			}
		}
		catch (DbException)
		{
		}
		base.Close(conn);
	}

	public override void ForceClose(DmConnection conn)
	{
		if (conn.do_State == ConnectionState.Closed)
		{
			return;
		}
		try
		{
			if (RWUtil2.isStandbyAlive(conn))
			{
				conn.RWInfo.connStandby.do_Close();
			}
		}
		catch (DbException)
		{
		}
		base.ForceClose(conn);
	}

	public override DmCommand CreateDbCommand(DmConnection conn)
	{
		DmCommand dmCommand = base.CreateDbCommand(conn);
		dmCommand.RWInfo.cmdCurrent = dmCommand;
		dmCommand.RWInfo.readOnly = RWUtil2.checkReadonly(dmCommand, null);
		try
		{
			if (RWUtil2.isCreateStandbyStmt(dmCommand))
			{
				dmCommand.RWInfo.cmdStandby = conn.RWInfo.connStandby.do_CreateDbCommand();
			}
		}
		catch (DbException e)
		{
			RWUtil2.afterExceptionOnStandby(conn, e);
		}
		return dmCommand;
	}

	public override void setDesignTimeVisible(DmCommand command, bool value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_DesignTimeVisible = value;
			}
		}
		catch (DbException)
		{
		}
		base.setDesignTimeVisible(command, value);
	}

	public override void setCommandType(DmCommand command, CommandType value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_CommandType = value;
			}
		}
		catch (DbException)
		{
		}
		base.setCommandType(command, value);
	}

	public override void setCommandTimeout(DmCommand command, int value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_CommandTimeout = value;
			}
		}
		catch (DbException)
		{
		}
		base.setCommandTimeout(command, value);
	}

	public override void setCommandText(DmCommand command, string value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_CommandText = value;
			}
		}
		catch (DbException)
		{
		}
		base.setCommandText(command, value);
	}

	public override void setUpdatedRowSource(DmCommand command, UpdateRowSource value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_UpdatedRowSource = value;
			}
		}
		catch (DbException)
		{
		}
		base.setUpdatedRowSource(command, value);
	}

	public override void setDbConnection(DmCommand command, DmConnection value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null && value != null)
			{
				command.RWInfo.cmdStandby.do_DbConnection = value.RWInfo.connStandby;
			}
		}
		catch (DbException)
		{
		}
		base.setDbConnection(command, value);
	}

	public override void setDbTransaction(DmCommand command, DmTransaction value)
	{
		try
		{
			if (command.RWInfo.cmdStandby != null)
			{
				command.RWInfo.cmdStandby.do_DbTransaction = command.do_DbConnection.RWInfo.transStandby;
			}
		}
		catch (DbException)
		{
		}
		base.setDbTransaction(command, value);
	}

	public override void Cancel(DmCommand command)
	{
		try
		{
			if (RWUtil2.isStandbyCmdValid(command))
			{
				command.RWInfo.cmdStandby.do_Cancel();
			}
		}
		catch (DbException e)
		{
			RWUtil2.afterExceptionOnStandby(command.do_DbConnection, e);
		}
		base.Cancel(command);
	}

	public override int ExecuteNonQuery(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		return RWUtil2.execute(command, () => command.RWInfo.cmdCurrent.do_ExecuteNonQuery(), delegate
		{
		}, (DmCommand otherCmd) => otherCmd.do_ExecuteNonQuery());
	}

	public override object ExecuteScalar(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		return RWUtil2.execute(command, () => command.RWInfo.cmdCurrent.do_ExecuteScalar(), delegate
		{
		}, (DmCommand otherCmd) => otherCmd.do_ExecuteScalar());
	}

	public override void Prepare(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		RWUtil2.execute(command, delegate
		{
			command.RWInfo.cmdCurrent.do_Prepare();
			return (object)null;
		}, delegate
		{
		}, delegate(DmCommand otherCmd)
		{
			otherCmd.do_Prepare();
			return (object)null;
		});
	}

	public override DmParameter CreateDbParameter(DmCommand command)
	{
		if (command.RWInfo.cmdCurrent != null)
		{
			return base.CreateDbParameter(command.RWInfo.cmdCurrent);
		}
		return base.CreateDbParameter(command);
	}

	public override DmDataReader ExecuteDbDataReader(DmCommand command, CommandBehavior behavior)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		return RWUtil2.execute(command, () => command.RWInfo.cmdCurrent.do_ExecuteDbDataReader(behavior), delegate(DmDataReader dmDataReader)
		{
			dmDataReader?.do_Close();
		}, (DmCommand otherCmd) => otherCmd.do_ExecuteDbDataReader(behavior));
	}

	public override void Save(DmTransaction transaction, string savepointName)
	{
		DmSavePoint dmSavePoint = transaction.do_Save(savepointName);
		DmConnection do_DbConnection = transaction.do_DbConnection;
		try
		{
			if (RWUtil2.isStandbyAlive(do_DbConnection))
			{
				dmSavePoint.standby = do_DbConnection.RWInfo.connStandby.m_ConnInst.Save(savepointName);
			}
		}
		catch (DbException e)
		{
			RWUtil2.afterExceptionOnStandby(do_DbConnection, e);
		}
	}

	public override void Rollback(DmTransaction transaction, string savepointName)
	{
		DmConnection do_DbConnection = transaction.do_DbConnection;
		try
		{
			if (RWUtil2.isStandbyAlive(do_DbConnection))
			{
				do_DbConnection.RWInfo.connStandby.m_ConnInst.Rollback(savepointName);
			}
		}
		catch (DbException e)
		{
			RWUtil2.afterExceptionOnStandby(do_DbConnection, e);
		}
		transaction.do_Rollback(savepointName);
	}

	public override void Release(DmTransaction transaction, string savepointName)
	{
		DmConnection do_DbConnection = transaction.do_DbConnection;
		try
		{
			if (RWUtil2.isStandbyAlive(do_DbConnection))
			{
				do_DbConnection.RWInfo.connStandby.m_ConnInst.Release(savepointName);
			}
		}
		catch (DbException e)
		{
			RWUtil2.afterExceptionOnStandby(do_DbConnection, e);
		}
		transaction.do_Release(savepointName);
	}
}
