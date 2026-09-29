using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net.Sockets;
using System.Transactions;
using W.Dm.Config;
using W.Dm.filter.rw;
using W.Dm.util;

namespace W.Dm.filter.reconnect;

public class ReconnectFilter : BaseFilter
{
	internal static ReconnectFilter Instance = new ReconnectFilter();

	private ReconnectFilter()
	{
	}

	private void autoReconnect(DmConnection connection, Exception e)
	{
		if (e is DmException)
		{
			int number = ((DmException)e).Number;
			if (number == DmErrorDefinition.ECNET_COMMUNITION_ERROR || number == DmErrorDefinition.ECNET_NO_SOCKET_DATA)
			{
				reconnect(connection, e.Message);
			}
		}
		else
		{
			if (!(e is SocketException))
			{
				throw e;
			}
			reconnect(connection, e.Message);
		}
	}

	public static void reconnect(DmConnection connection, string reasons)
	{
		try
		{
			if (connection.ConnProperty.RwSeparate > 0)
			{
				RWUtil2.reconnect(connection);
			}
			else
			{
				connection.Reconnect();
			}
		}
		catch (Exception)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNECTION_SWITCH_FAILED);
		}
		DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNECTION_SWITCHED);
	}

	public void checkAndRecover(DmConnection connection, DmTransaction transaction)
	{
		if (connection.ConnProperty.DoSwitch != DoSwitch.EP_RECOVER)
		{
			return;
		}
		int num = 0;
		if ((transaction != null && transaction.Valid) || (num = connection.getIndexOnDBGroup()) == 0 || (DateTime.Now - connection.RecoverInfo.checkEpRecoverTs).TotalMilliseconds < (double)connection.ConnProperty.SwitchInterval)
		{
			return;
		}
		List<EP> list = ((connection.ConnProperty.Cluster == CLUSTER.DSC) ? DriverUtil.loadDscEpSites(connection) : null);
		if (list == null || list.Count == 0)
		{
			return;
		}
		bool flag = false;
		foreach (EP item in list)
		{
			if (item.epStatus != 1)
			{
				continue;
			}
			for (int i = 0; i < num; i++)
			{
				EP eP = connection.ConnProperty.EPGroup.epList[i];
				if (item.host.Equals(eP.host, StringComparison.OrdinalIgnoreCase) && item.port == eP.port)
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				break;
			}
		}
		connection.RecoverInfo.checkEpRecoverTs = DateTime.Now;
		if (flag)
		{
			connection.Reconnect();
		}
	}

	public override void Open(DmConnection conn)
	{
		if (conn.do_State == ConnectionState.Open)
		{
			return;
		}
		try
		{
			base.Open(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
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
			base.Close(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
		}
	}

	public override void ForceClose(DmConnection conn)
	{
		if (conn.do_State == ConnectionState.Closed)
		{
			return;
		}
		try
		{
			base.ForceClose(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
		}
	}

	public override string getServerVersion(DmConnection conn)
	{
		try
		{
			return base.getServerVersion(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return string.Empty;
		}
	}

	public override string getDataSource(DmConnection conn)
	{
		try
		{
			return base.getDataSource(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return string.Empty;
		}
	}

	public override string getDatabase(DmConnection conn)
	{
		try
		{
			return base.getDatabase(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return string.Empty;
		}
	}

	public override int getConnectionTimeout(DmConnection conn)
	{
		try
		{
			return base.getConnectionTimeout(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return 0;
		}
	}

	public override string getConnectionString(DmConnection conn)
	{
		try
		{
			return base.getConnectionString(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return string.Empty;
		}
	}

	public override DbProviderFactory getDbProviderFactory(DmConnection conn)
	{
		try
		{
			return base.getDbProviderFactory(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override ConnectionState getState(DmConnection conn)
	{
		try
		{
			return base.getState(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return ConnectionState.Open;
		}
	}

	public override DmTransaction BeginDbTransaction(DmConnection conn, System.Data.IsolationLevel isolationLevel)
	{
		if (conn.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		try
		{
			return base.BeginDbTransaction(conn, isolationLevel);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override void ChangeDatabase(DmConnection conn, string databaseName)
	{
		try
		{
			base.ChangeDatabase(conn, databaseName);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
		}
	}

	public override DmCommand CreateDbCommand(DmConnection conn)
	{
		try
		{
			return base.CreateDbCommand(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override void EnlistTransaction(DmConnection conn, Transaction transaction)
	{
		try
		{
			base.EnlistTransaction(conn, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
		}
	}

	public override DataTable GetSchema(DmConnection conn)
	{
		try
		{
			return base.GetSchema(conn);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override DataTable GetSchema(DmConnection conn, string collectionName, string[] restrictionValues)
	{
		try
		{
			return base.GetSchema(conn, collectionName, restrictionValues);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override DataTable GetSchema(DmConnection conn, string collectionName)
	{
		try
		{
			return base.GetSchema(conn, collectionName);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override FldrStatement Connection_fldrStatement(DmConnection conn, FldrConfig config)
	{
		try
		{
			return base.Connection_fldrStatement(conn, config);
		}
		catch (Exception e)
		{
			autoReconnect(conn, e);
			return null;
		}
	}

	public override bool getDesignTimeVisible(DmCommand command)
	{
		try
		{
			return base.getDesignTimeVisible(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return false;
		}
	}

	public override void setDesignTimeVisible(DmCommand command, bool value)
	{
		try
		{
			base.setDesignTimeVisible(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override CommandType getCommandType(DmCommand command)
	{
		try
		{
			return base.getCommandType(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return CommandType.Text;
		}
	}

	public override void setCommandType(DmCommand command, CommandType value)
	{
		try
		{
			base.setCommandType(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override int getCommandTimeout(DmCommand command)
	{
		try
		{
			return base.getCommandTimeout(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return 0;
		}
	}

	public override void setCommandTimeout(DmCommand command, int value)
	{
		try
		{
			base.setCommandTimeout(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override string getCommandText(DmCommand command)
	{
		try
		{
			return base.getCommandText(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override void setCommandText(DmCommand command, string value)
	{
		try
		{
			base.setCommandText(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override UpdateRowSource getUpdatedRowSource(DmCommand command)
	{
		try
		{
			return base.getUpdatedRowSource(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return UpdateRowSource.None;
		}
	}

	public override void setUpdatedRowSource(DmCommand command, UpdateRowSource value)
	{
		try
		{
			base.setUpdatedRowSource(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override DmConnection getDbConnection(DmCommand command)
	{
		try
		{
			return base.getDbConnection(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override void setDbConnection(DmCommand command, DmConnection value)
	{
		try
		{
			base.setDbConnection(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override DmParameterCollection getDbParameterCollection(DmCommand command)
	{
		try
		{
			return base.getDbParameterCollection(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override DmTransaction getDbTransaction(DmCommand command)
	{
		try
		{
			return base.getDbTransaction(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override void setDbTransaction(DmCommand command, DmTransaction value)
	{
		try
		{
			base.setDbTransaction(command, value);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override void Cancel(DmCommand command)
	{
		try
		{
			base.Cancel(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override int ExecuteNonQuery(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		try
		{
			checkAndRecover(command.do_DbConnection, command.do_DbTransaction);
			return base.ExecuteNonQuery(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return 0;
		}
	}

	public override object ExecuteScalar(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		try
		{
			checkAndRecover(command.do_DbConnection, command.do_DbTransaction);
			return base.ExecuteScalar(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override void Prepare(DmCommand command)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		try
		{
			checkAndRecover(command.do_DbConnection, command.do_DbTransaction);
			base.Prepare(command);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
		}
	}

	public override DmDataReader ExecuteDbDataReader(DmCommand command, CommandBehavior behavior)
	{
		if (command.do_DbConnection == null || command.do_DbConnection.do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		try
		{
			checkAndRecover(command.do_DbConnection, command.do_DbTransaction);
			return base.ExecuteDbDataReader(command, behavior);
		}
		catch (Exception e)
		{
			autoReconnect(command.do_DbConnection, e);
			return null;
		}
	}

	public override System.Data.IsolationLevel getIsolationLevel(DmTransaction transaction)
	{
		try
		{
			return base.getIsolationLevel(transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
			return System.Data.IsolationLevel.Unspecified;
		}
	}

	public override void Commit(DmTransaction transaction)
	{
		try
		{
			base.Commit(transaction);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}

	public override void Rollback(DmTransaction transaction)
	{
		try
		{
			base.Rollback(transaction);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}

	public override void Dispose(DmTransaction transaction, bool disposing)
	{
		try
		{
			base.Dispose(transaction, disposing);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}

	public override void Save(DmTransaction transaction, string savepointName)
	{
		try
		{
			base.Save(transaction, savepointName);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}

	public override void Rollback(DmTransaction transaction, string savepointName)
	{
		try
		{
			base.Rollback(transaction, savepointName);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}

	public override void Release(DmTransaction transaction, string savepointName)
	{
		try
		{
			base.Release(transaction, savepointName);
			checkAndRecover(transaction.do_DbConnection, transaction);
		}
		catch (Exception e)
		{
			autoReconnect(transaction.do_DbConnection, e);
		}
	}
}
