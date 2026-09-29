using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Transactions;

namespace W.Dm.filter.log;

internal class LogFilter : BaseFilter
{
	private readonly ILogger LOG = LogFactory.getLog(typeof(LogFilter));

	internal static LogFilter Instance = new LogFilter();

	private LogFilter()
	{
	}

	private void doLog(LogRecord logRecord)
	{
		if (logRecord == null)
		{
			return;
		}
		try
		{
			if (logRecord.Throwable != null)
			{
				LOG.Error(logRecord.ToString(), logRecord.Throwable);
			}
			else if (logRecord.Sql != null && LOG.SqlEnabled)
			{
				LOG.Sql(logRecord.ToString());
			}
			else
			{
				LOG.Info(logRecord.ToString());
			}
		}
		catch (Exception t)
		{
			LOG.Error("Log failed!", t);
		}
	}

	public override string getServerVersion(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getServerVersion");
		try
		{
			return (string)(logRecord.ReturnValue = base.getServerVersion(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getDataSource(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getDataSource");
		try
		{
			return (string)(logRecord.ReturnValue = base.getDataSource(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getDatabase(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getDatabase");
		try
		{
			return (string)(logRecord.ReturnValue = base.getDatabase(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getConnectionTimeout(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getConnectionTimeout");
		try
		{
			int connectionTimeout = base.getConnectionTimeout(conn);
			logRecord.ReturnValue = connectionTimeout;
			return connectionTimeout;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getConnectionString(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getConnectionString");
		try
		{
			return (string)(logRecord.ReturnValue = base.getConnectionString(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DbProviderFactory getDbProviderFactory(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getDbProviderFactory");
		try
		{
			return (DbProviderFactory)(logRecord.ReturnValue = base.getDbProviderFactory(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override ConnectionState getState(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "getState");
		try
		{
			ConnectionState state = base.getState(conn);
			logRecord.ReturnValue = state;
			return state;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmTransaction BeginDbTransaction(DmConnection conn, System.Data.IsolationLevel isolationLevel)
	{
		LogRecord logRecord = new LogRecord(conn, "BeginTransaction", isolationLevel);
		try
		{
			return (DmTransaction)(logRecord.ReturnValue = base.BeginDbTransaction(conn, isolationLevel));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void ChangeDatabase(DmConnection conn, string databaseName)
	{
		LogRecord logRecord = new LogRecord(conn, "ChangeDatabase", databaseName);
		try
		{
			base.ChangeDatabase(conn, databaseName);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Close(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "Close");
		try
		{
			base.Close(conn);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void ForceClose(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "ForceClose");
		try
		{
			base.ForceClose(conn);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmCommand CreateDbCommand(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "CreateDbCommand");
		try
		{
			return (DmCommand)(logRecord.ReturnValue = base.CreateDbCommand(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void EnlistTransaction(DmConnection conn, Transaction transaction)
	{
		LogRecord logRecord = new LogRecord(conn, "EnlistTransaction");
		try
		{
			base.EnlistTransaction(conn, transaction);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable GetSchema(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "GetSchema");
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.GetSchema(conn));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable GetSchema(DmConnection conn, string collectionName, string[] restrictionValues)
	{
		LogRecord logRecord = new LogRecord(conn, "GetSchema", collectionName, restrictionValues);
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.GetSchema(conn, collectionName, restrictionValues));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable GetSchema(DmConnection conn, string collectionName)
	{
		LogRecord logRecord = new LogRecord(conn, "GetSchema", collectionName);
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.GetSchema(conn, collectionName));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Open(DmConnection conn)
	{
		LogRecord logRecord = new LogRecord(conn, "Open");
		try
		{
			base.Open(conn);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override FldrStatement Connection_fldrStatement(DmConnection conn, FldrConfig config)
	{
		LogRecord logRecord = new LogRecord(conn, "Connection_fldrStatement");
		try
		{
			return base.Connection_fldrStatement(conn, config);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getDesignTimeVisible(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getDesignTimeVisible");
		try
		{
			bool designTimeVisible = base.getDesignTimeVisible(command);
			logRecord.ReturnValue = designTimeVisible;
			return designTimeVisible;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setDesignTimeVisible(DmCommand command, bool value)
	{
		LogRecord logRecord = new LogRecord(command, "setDesignTimeVisible", value);
		try
		{
			base.setDesignTimeVisible(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override CommandType getCommandType(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getCommandType");
		try
		{
			CommandType commandType = base.getCommandType(command);
			logRecord.ReturnValue = commandType;
			return commandType;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setCommandType(DmCommand command, CommandType value)
	{
		LogRecord logRecord = new LogRecord(command, "setCommandType", value);
		try
		{
			base.setCommandType(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getCommandTimeout(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getCommandTimeout");
		try
		{
			int commandTimeout = base.getCommandTimeout(command);
			logRecord.ReturnValue = commandTimeout;
			return commandTimeout;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setCommandTimeout(DmCommand command, int value)
	{
		LogRecord logRecord = new LogRecord(command, "setCommandTimeout", value);
		try
		{
			base.setCommandTimeout(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getCommandText(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getCommandText");
		try
		{
			return (string)(logRecord.ReturnValue = base.getCommandText(command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setCommandText(DmCommand command, string value)
	{
		LogRecord logRecord = new LogRecord(command, "setCommandText", value);
		try
		{
			base.setCommandText(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override UpdateRowSource getUpdatedRowSource(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getUpdatedRowSource");
		try
		{
			UpdateRowSource updatedRowSource = base.getUpdatedRowSource(command);
			logRecord.ReturnValue = updatedRowSource;
			return updatedRowSource;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setUpdatedRowSource(DmCommand command, UpdateRowSource value)
	{
		LogRecord logRecord = new LogRecord(command, "setUpdatedRowSource", value);
		try
		{
			base.setUpdatedRowSource(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmConnection getDbConnection(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getDbConnection");
		try
		{
			return (DmConnection)(logRecord.ReturnValue = base.getDbConnection(command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setDbConnection(DmCommand command, DmConnection value)
	{
		LogRecord logRecord = new LogRecord(command, "setDbConnection", value);
		try
		{
			base.setDbConnection(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmParameterCollection getDbParameterCollection(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getDbParameterCollection");
		try
		{
			return (DmParameterCollection)(logRecord.ReturnValue = base.getDbParameterCollection(command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmTransaction getDbTransaction(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getDbTransaction");
		try
		{
			return (DmTransaction)(logRecord.ReturnValue = base.getDbTransaction(command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setDbTransaction(DmCommand command, DmTransaction value)
	{
		LogRecord logRecord = new LogRecord(command, "setDbTransaction", value);
		try
		{
			base.setDbTransaction(command, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Cancel(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "Cancel");
		try
		{
			base.Cancel(command);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int ExecuteNonQuery(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "ExecuteNonQuery");
		try
		{
			logRecord.Sql = command.do_CommandText;
			int num = base.ExecuteNonQuery(command);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			SetExecuteId(command, logRecord);
			doLog(logRecord);
		}
	}

	public override object ExecuteScalar(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "ExecuteScalar");
		try
		{
			logRecord.Sql = command.do_CommandText;
			return logRecord.ReturnValue = base.ExecuteScalar(command);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			SetExecuteId(command, logRecord);
			doLog(logRecord);
		}
	}

	public override void Prepare(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "Prepare");
		try
		{
			logRecord.Sql = command.do_CommandText;
			base.Prepare(command);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			SetExecuteId(command, logRecord);
			doLog(logRecord);
		}
	}

	public override DmParameter CreateDbParameter(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "CreateDbParameter");
		try
		{
			return (DmParameter)(logRecord.ReturnValue = base.CreateDbParameter(command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmDataReader ExecuteDbDataReader(DmCommand command, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(command, "ExecuteDbDataReader", behavior);
		try
		{
			logRecord.Sql = command.do_CommandText;
			return (DmDataReader)(logRecord.ReturnValue = base.ExecuteDbDataReader(command, behavior));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			SetExecuteId(command, logRecord);
			doLog(logRecord);
		}
	}

	private void SetExecuteId(DmCommand command, LogRecord logRecord)
	{
		logRecord.ExecuteId = command.GetExecuteId();
	}

	public override long getExecuteId(DmCommand command)
	{
		LogRecord logRecord = new LogRecord(command, "getExecuteId");
		try
		{
			return base.getExecuteId(command);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override System.Data.IsolationLevel getIsolationLevel(DmTransaction transaction)
	{
		LogRecord logRecord = new LogRecord(transaction, "getIsolationLevel");
		try
		{
			System.Data.IsolationLevel isolationLevel = base.getIsolationLevel(transaction);
			logRecord.ReturnValue = isolationLevel;
			return isolationLevel;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmConnection getDbConnection(DmTransaction transaction)
	{
		LogRecord logRecord = new LogRecord(transaction, "getDbConnection");
		try
		{
			return (DmConnection)(logRecord.ReturnValue = base.getDbConnection(transaction));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Commit(DmTransaction transaction)
	{
		LogRecord logRecord = new LogRecord(transaction, "Commit");
		try
		{
			base.Commit(transaction);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Rollback(DmTransaction transaction)
	{
		LogRecord logRecord = new LogRecord(transaction, "Rollback");
		try
		{
			base.Rollback(transaction);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Dispose(DmTransaction transaction, bool disposing)
	{
		LogRecord logRecord = new LogRecord(transaction, "Dispose", disposing);
		try
		{
			base.Dispose(transaction, disposing);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Save(DmTransaction transaction, string savepointName)
	{
		LogRecord logRecord = new LogRecord(transaction, "SetSavepoint", savepointName);
		try
		{
			base.Save(transaction, savepointName);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Rollback(DmTransaction transaction, string savepointName)
	{
		LogRecord logRecord = new LogRecord(transaction, "Rollback", savepointName);
		try
		{
			base.Rollback(transaction, savepointName);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Release(DmTransaction transaction, string savepointName)
	{
		LogRecord logRecord = new LogRecord(transaction, "Release", savepointName);
		try
		{
			base.Release(transaction, savepointName);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object getThis(DmDataReader dataReader, int index)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getThis", index);
		try
		{
			return logRecord.ReturnValue = base.getThis(dataReader, index);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object getThis(DmDataReader dataReader, string name)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getThis", name);
		try
		{
			return logRecord.ReturnValue = base.getThis(dataReader, name);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getDepth(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getDepth");
		try
		{
			int depth = base.getDepth(dataReader);
			logRecord.ReturnValue = depth;
			return depth;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getHasRows(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getHasRows");
		try
		{
			bool hasRows = base.getHasRows(dataReader);
			logRecord.ReturnValue = hasRows;
			return hasRows;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getVisibleFieldCount(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getVisibleFieldCount");
		try
		{
			int visibleFieldCount = base.getVisibleFieldCount(dataReader);
			logRecord.ReturnValue = visibleFieldCount;
			return visibleFieldCount;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getRecordsAffected(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getRecordsAffected");
		try
		{
			int recordsAffected = base.getRecordsAffected(dataReader);
			logRecord.ReturnValue = recordsAffected;
			return recordsAffected;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsClosed(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getIsClosed");
		try
		{
			bool isClosed = base.getIsClosed(dataReader);
			logRecord.ReturnValue = isClosed;
			return isClosed;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getFieldCount(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "getFieldCount");
		try
		{
			int fieldCount = base.getFieldCount(dataReader);
			logRecord.ReturnValue = fieldCount;
			return fieldCount;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Close(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "Close");
		try
		{
			base.Close(dataReader);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool GetBoolean(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetBoolean", ordinal);
		try
		{
			bool boolean = base.GetBoolean(dataReader, ordinal);
			logRecord.ReturnValue = boolean;
			return boolean;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override byte GetByte(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetByte", ordinal);
		try
		{
			byte b = base.GetByte(dataReader, ordinal);
			logRecord.ReturnValue = b;
			return b;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override long GetBytes(DmDataReader dataReader, int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetBytes", ordinal, dataOffset, buffer, bufferOffset, length);
		try
		{
			long bytes = base.GetBytes(dataReader, ordinal, dataOffset, buffer, bufferOffset, length);
			logRecord.ReturnValue = bytes;
			return bytes;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override char GetChar(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetChar", ordinal);
		try
		{
			char c = base.GetChar(dataReader, ordinal);
			logRecord.ReturnValue = c;
			return c;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override long GetChars(DmDataReader dataReader, int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetChars", ordinal, dataOffset, buffer, bufferOffset, length);
		try
		{
			long chars = base.GetChars(dataReader, ordinal, dataOffset, buffer, bufferOffset, length);
			logRecord.ReturnValue = chars;
			return chars;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetDataTypeName(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetDataTypeName", ordinal);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetDataTypeName(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DateTime GetDateTime(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetDateTime", ordinal);
		try
		{
			DateTime dateTime = base.GetDateTime(dataReader, ordinal);
			logRecord.ReturnValue = dateTime;
			return dateTime;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override decimal GetDecimal(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetDecimal", ordinal);
		try
		{
			decimal num = base.GetDecimal(dataReader, ordinal);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override double GetDouble(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetDouble", ordinal);
		try
		{
			double num = base.GetDouble(dataReader, ordinal);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override IEnumerator GetEnumerator(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetEnumerator");
		try
		{
			return (IEnumerator)(logRecord.ReturnValue = base.GetEnumerator(dataReader));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override Type GetFieldType(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetFieldType", ordinal);
		try
		{
			return (Type)(logRecord.ReturnValue = base.GetFieldType(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override float GetFloat(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetFloat", ordinal);
		try
		{
			float num = base.GetFloat(dataReader, ordinal);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override Guid GetGuid(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetGuid", ordinal);
		try
		{
			Guid guid = base.GetGuid(dataReader, ordinal);
			logRecord.ReturnValue = guid;
			return guid;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override short GetInt16(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetInt16", ordinal);
		try
		{
			short @int = base.GetInt16(dataReader, ordinal);
			logRecord.ReturnValue = @int;
			return @int;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int GetInt32(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetInt32", ordinal);
		try
		{
			int @int = base.GetInt32(dataReader, ordinal);
			logRecord.ReturnValue = @int;
			return @int;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override long GetInt64(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetInt64", ordinal);
		try
		{
			long @int = base.GetInt64(dataReader, ordinal);
			logRecord.ReturnValue = @int;
			return @int;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetName(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetName", ordinal);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetName(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int GetOrdinal(DmDataReader dataReader, string name)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetOrdinal", name);
		try
		{
			int ordinal = base.GetOrdinal(dataReader, name);
			logRecord.ReturnValue = ordinal;
			return ordinal;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override Type GetProviderSpecificFieldType(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetProviderSpecificFieldType", ordinal);
		try
		{
			return (Type)(logRecord.ReturnValue = base.GetProviderSpecificFieldType(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object GetProviderSpecificValue(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetProviderSpecificValue", ordinal);
		try
		{
			return logRecord.ReturnValue = base.GetProviderSpecificValue(dataReader, ordinal);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int GetProviderSpecificValues(DmDataReader dataReader, object[] values)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetProviderSpecificValues", values);
		try
		{
			int providerSpecificValues = base.GetProviderSpecificValues(dataReader, values);
			logRecord.ReturnValue = providerSpecificValues;
			return providerSpecificValues;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable GetSchemaTable(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetSchemaTable");
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.GetSchemaTable(dataReader));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetString(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetString", ordinal);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetString(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object GetValue(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetValue", ordinal);
		try
		{
			return logRecord.ReturnValue = base.GetValue(dataReader, ordinal);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int GetValues(DmDataReader dataReader, object[] values)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetValues", values);
		try
		{
			int values2 = base.GetValues(dataReader, values);
			logRecord.ReturnValue = values2;
			return values2;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool IsDBNull(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "IsDBNull", ordinal);
		try
		{
			bool flag = base.IsDBNull(dataReader, ordinal);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool NextResult(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "NextResult");
		try
		{
			bool flag = base.NextResult(dataReader);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool Read(DmDataReader dataReader)
	{
		LogRecord logRecord = new LogRecord(dataReader, "Read");
		try
		{
			bool flag = base.Read(dataReader);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Dispose(DmDataReader dataReader, bool disposing)
	{
		LogRecord logRecord = new LogRecord(dataReader, "Dispose", disposing);
		try
		{
			base.Dispose(dataReader, disposing);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmDataReader GetDbDataReader(DmDataReader dataReader, int ordinal)
	{
		LogRecord logRecord = new LogRecord(dataReader, "GetDbDataReader", ordinal);
		try
		{
			return (DmDataReader)(logRecord.ReturnValue = base.GetDbDataReader(dataReader, ordinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object getSyncRoot(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "getSyncRoot");
		try
		{
			return logRecord.ReturnValue = base.getSyncRoot(parameterCollection);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsSynchronized(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "getIsSynchronized");
		try
		{
			bool isSynchronized = base.getIsSynchronized(parameterCollection);
			logRecord.ReturnValue = isSynchronized;
			return isSynchronized;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsReadOnly(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "getIsReadOnly");
		try
		{
			bool isReadOnly = base.getIsReadOnly(parameterCollection);
			logRecord.ReturnValue = isReadOnly;
			return isReadOnly;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsFixedSize(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "getIsFixedSize");
		try
		{
			bool isFixedSize = base.getIsFixedSize(parameterCollection);
			logRecord.ReturnValue = isFixedSize;
			return isFixedSize;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getCount(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "getCount");
		try
		{
			int count = base.getCount(parameterCollection);
			logRecord.ReturnValue = count;
			return count;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int Add(DmParameterCollection parameterCollection, object value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Add", value);
		try
		{
			int num = base.Add(parameterCollection, value);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void AddRange(DmParameterCollection parameterCollection, Array values)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "AddRange", values);
		try
		{
			base.AddRange(parameterCollection, values);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Clear(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Clear");
		try
		{
			base.Clear(parameterCollection);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool Contains(DmParameterCollection parameterCollection, object value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Contains", value);
		try
		{
			bool flag = base.Contains(parameterCollection, value);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool Contains(DmParameterCollection parameterCollection, string value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Contains", value);
		try
		{
			bool flag = base.Contains(parameterCollection, value);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void CopyTo(DmParameterCollection parameterCollection, Array array, int index)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "CopyTo", array, index);
		try
		{
			base.CopyTo(parameterCollection, array, index);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override IEnumerator GetEnumerator(DmParameterCollection parameterCollection)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "GetEnumerator");
		try
		{
			return (IEnumerator)(logRecord.ReturnValue = base.GetEnumerator(parameterCollection));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int IndexOf(DmParameterCollection parameterCollection, object value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "IndexOf", value);
		try
		{
			int num = base.IndexOf(parameterCollection, value);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int IndexOf(DmParameterCollection parameterCollection, string parameterName)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "IndexOf", parameterName);
		try
		{
			int num = base.IndexOf(parameterCollection, parameterName);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Insert(DmParameterCollection parameterCollection, int index, object value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Insert", index, value);
		try
		{
			base.Insert(parameterCollection, index, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Remove(DmParameterCollection parameterCollection, object value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "Remove", value);
		try
		{
			base.Remove(parameterCollection, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void RemoveAt(DmParameterCollection parameterCollection, string parameterName)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "RemoveAt", parameterName);
		try
		{
			base.RemoveAt(parameterCollection, parameterName);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void RemoveAt(DmParameterCollection parameterCollection, int index)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "RemoveAt", index);
		try
		{
			base.RemoveAt(parameterCollection, index);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmParameter GetParameter(DmParameterCollection parameterCollection, string parameterName)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "GetParameter", parameterName);
		try
		{
			return (DmParameter)(logRecord.ReturnValue = base.GetParameter(parameterCollection, parameterName));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmParameter GetParameter(DmParameterCollection parameterCollection, int index)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "GetParameter", index);
		try
		{
			return (DmParameter)(logRecord.ReturnValue = base.GetParameter(parameterCollection, index));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void SetParameter(DmParameterCollection parameterCollection, int index, DmParameter value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "SetParameter", index, value);
		try
		{
			base.SetParameter(parameterCollection, index, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void SetParameter(DmParameterCollection parameterCollection, string parameterName, DmParameter value)
	{
		LogRecord logRecord = new LogRecord(parameterCollection, "SetParameter", parameterName, value);
		try
		{
			base.SetParameter(parameterCollection, parameterName, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DbType getDbType(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getDbType");
		try
		{
			DbType dbType = base.getDbType(parameter);
			logRecord.ReturnValue = dbType;
			return dbType;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setDbType(DmParameter parameter, DbType value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setDbType", value);
		try
		{
			base.setDbType(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override ParameterDirection getDirection(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getDirection");
		try
		{
			ParameterDirection direction = base.getDirection(parameter);
			logRecord.ReturnValue = direction;
			return direction;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setDirection(DmParameter parameter, ParameterDirection value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setDirection", value);
		try
		{
			base.setDirection(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsNullable(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getIsNullable");
		try
		{
			bool isNullable = base.getIsNullable(parameter);
			logRecord.ReturnValue = isNullable;
			return isNullable;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setIsNullable(DmParameter parameter, bool value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setIsNullable", value);
		try
		{
			base.setIsNullable(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getParameterName(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getParameterName");
		try
		{
			return (string)(logRecord.ReturnValue = base.getParameterName(parameter));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setParameterName(DmParameter parameter, string value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setParameterName", value);
		try
		{
			base.setParameterName(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override byte getPrecision(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getPrecision");
		try
		{
			byte precision = base.getPrecision(parameter);
			logRecord.ReturnValue = precision;
			return precision;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setPrecision(DmParameter parameter, byte value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setPrecision", value);
		try
		{
			base.setPrecision(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override byte getScale(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getScale");
		try
		{
			byte scale = base.getScale(parameter);
			logRecord.ReturnValue = scale;
			return scale;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setScale(DmParameter parameter, byte value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setScale", value);
		try
		{
			base.setScale(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getSize(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getSize");
		try
		{
			int size = base.getSize(parameter);
			logRecord.ReturnValue = size;
			return size;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setSize(DmParameter parameter, int value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setSize", value);
		try
		{
			base.setSize(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getSourceColumn(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getSourceColumn");
		try
		{
			return (string)(logRecord.ReturnValue = base.getSourceColumn(parameter));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setSourceColumn(DmParameter parameter, string value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setSourceColumn", value);
		try
		{
			base.setSourceColumn(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getSourceColumnNullMapping(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getSourceColumnNullMapping");
		try
		{
			bool sourceColumnNullMapping = base.getSourceColumnNullMapping(parameter);
			logRecord.ReturnValue = sourceColumnNullMapping;
			return sourceColumnNullMapping;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setSourceColumnNullMapping(DmParameter parameter, bool value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setSourceColumnNullMapping", value);
		try
		{
			base.setSourceColumnNullMapping(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataRowVersion getSourceVersion(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getSourceVersion");
		try
		{
			DataRowVersion sourceVersion = base.getSourceVersion(parameter);
			logRecord.ReturnValue = sourceVersion;
			return sourceVersion;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setSourceVersion(DmParameter parameter, DataRowVersion value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setSourceVersion", value);
		try
		{
			base.setSourceVersion(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object getValue(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "getValue");
		try
		{
			return logRecord.ReturnValue = base.getValue(parameter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setValue(DmParameter parameter, object value)
	{
		LogRecord logRecord = new LogRecord(parameter, "setValue", value);
		try
		{
			base.setValue(parameter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void ResetDbType(DmParameter parameter)
	{
		LogRecord logRecord = new LogRecord(parameter, "ResetDbType");
		try
		{
			base.ResetDbType(parameter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getUpdateBatchSize(DmDataAdapter dataAdapter)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "getUpdateBatchSize");
		try
		{
			int updateBatchSize = base.getUpdateBatchSize(dataAdapter);
			logRecord.ReturnValue = updateBatchSize;
			return updateBatchSize;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setUpdateBatchSize(DmDataAdapter dataAdapter, int value)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "setUpdateBatchSize", value);
		try
		{
			base.setUpdateBatchSize(dataAdapter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int AddToBatch(DmDataAdapter dataAdapter, DmCommand command)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "AddToBatch", command);
		try
		{
			int num = base.AddToBatch(dataAdapter, command);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void ClearBatch(DmDataAdapter dataAdapter)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "ClearBatch");
		try
		{
			base.ClearBatch(dataAdapter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override RowUpdatedEventArgs CreateRowUpdatedEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "CreateRowUpdatedEvent", dataRow, command, statementType, tableMapping);
		try
		{
			return (RowUpdatedEventArgs)(logRecord.ReturnValue = base.CreateRowUpdatedEvent(dataAdapter, dataRow, command, statementType, tableMapping));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override RowUpdatingEventArgs CreateRowUpdatingEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "CreateRowUpdatingEvent", dataRow, command, statementType, tableMapping);
		try
		{
			return (RowUpdatingEventArgs)(logRecord.ReturnValue = base.CreateRowUpdatingEvent(dataAdapter, dataRow, command, statementType, tableMapping));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int ExecuteBatch(DmDataAdapter dataAdapter)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "ExecuteBatch");
		try
		{
			int num = base.ExecuteBatch(dataAdapter);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int Fill(DmDataAdapter dataAdapter, DataTable[] dataTables, int startRecord, int maxRecords, DmCommand command, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "Fill", dataTables, startRecord, maxRecords, command, behavior);
		try
		{
			int num = base.Fill(dataAdapter, dataTables, startRecord, maxRecords, command, behavior);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int Fill(DmDataAdapter dataAdapter, DataTable dataTable, DmCommand command, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "Fill", dataTable, command, behavior);
		try
		{
			int num = base.Fill(dataAdapter, dataTable, command, behavior);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int Fill(DmDataAdapter dataAdapter, DataSet dataSet, int startRecord, int maxRecords, string srcTable, DmCommand command, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "Fill", dataSet, startRecord, maxRecords, srcTable, command, behavior);
		try
		{
			int num = base.Fill(dataAdapter, dataSet, startRecord, maxRecords, srcTable, command, behavior);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable FillSchema(DmDataAdapter dataAdapter, DataTable dataTable, SchemaType schemaType, DmCommand command, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "FillSchema", dataTable, schemaType, command, behavior);
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.FillSchema(dataAdapter, dataTable, schemaType, command, behavior));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable[] FillSchema(DmDataAdapter dataAdapter, DataSet dataSet, SchemaType schemaType, DmCommand command, string srcTable, CommandBehavior behavior)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "FillSchema", dataSet, schemaType, command, srcTable, behavior);
		try
		{
			return (DataTable[])(logRecord.ReturnValue = base.FillSchema(dataAdapter, dataSet, schemaType, command, srcTable, behavior));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override IDataParameter GetBatchedParameter(DmDataAdapter dataAdapter, int commandIdentifier, int parameterIndex)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "GetBatchedParameter", commandIdentifier, parameterIndex);
		try
		{
			return (IDataParameter)(logRecord.ReturnValue = base.GetBatchedParameter(dataAdapter, commandIdentifier, parameterIndex));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool GetBatchedRecordsAffected(DmDataAdapter dataAdapter, int commandIdentifier, out int recordsAffected, out Exception error)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "GetBatchedRecordsAffected", commandIdentifier);
		try
		{
			bool batchedRecordsAffected = base.GetBatchedRecordsAffected(dataAdapter, commandIdentifier, out recordsAffected, out error);
			logRecord.ReturnValue = batchedRecordsAffected;
			return batchedRecordsAffected;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void InitializeBatching(DmDataAdapter dataAdapter)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "InitializeBatching");
		try
		{
			base.InitializeBatching(dataAdapter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void OnRowUpdated(DmDataAdapter dataAdapter, RowUpdatedEventArgs value)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "OnRowUpdated", value);
		try
		{
			base.OnRowUpdated(dataAdapter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void OnRowUpdating(DmDataAdapter dataAdapter, RowUpdatingEventArgs value)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "OnRowUpdating", value);
		try
		{
			base.OnRowUpdating(dataAdapter, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void TerminateBatching(DmDataAdapter dataAdapter)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "TerminateBatching");
		try
		{
			base.TerminateBatching(dataAdapter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int Update(DmDataAdapter dataAdapter, DataRow[] dataRows, DataTableMapping tableMapping)
	{
		LogRecord logRecord = new LogRecord(dataAdapter, "Update", dataRows, tableMapping);
		try
		{
			int num = base.Update(dataAdapter, dataRows, tableMapping);
			logRecord.ReturnValue = num;
			return num;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getQuoteSuffix(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getQuoteSuffix");
		try
		{
			return (string)(logRecord.ReturnValue = base.getQuoteSuffix(commandBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setQuoteSuffix(DmCommandBuilder commandBuilder, string value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setQuoteSuffix", value);
		try
		{
			base.setQuoteSuffix(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getQuotePrefix(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getQuotePrefix");
		try
		{
			return (string)(logRecord.ReturnValue = base.getQuotePrefix(commandBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setQuotePrefix(DmCommandBuilder commandBuilder, string value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setQuotePrefix", value);
		try
		{
			base.setQuotePrefix(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getCatalogSeparator(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getCatalogSeparator");
		try
		{
			return (string)(logRecord.ReturnValue = base.getCatalogSeparator(commandBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setCatalogSeparator(DmCommandBuilder commandBuilder, string value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setCatalogSeparator", value);
		try
		{
			base.setCatalogSeparator(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override CatalogLocation getCatalogLocation(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getCatalogLocation");
		try
		{
			CatalogLocation catalogLocation = base.getCatalogLocation(commandBuilder);
			logRecord.ReturnValue = catalogLocation;
			return catalogLocation;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setCatalogLocation(DmCommandBuilder commandBuilder, CatalogLocation value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setCatalogLocation", value);
		try
		{
			base.setCatalogLocation(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override ConflictOption getConflictOption(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getConflictOption");
		try
		{
			ConflictOption conflictOption = base.getConflictOption(commandBuilder);
			logRecord.ReturnValue = conflictOption;
			return conflictOption;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setConflictOption(DmCommandBuilder commandBuilder, ConflictOption value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setConflictOption", value);
		try
		{
			base.setConflictOption(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string getSchemaSeparator(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "getSchemaSeparator");
		try
		{
			return (string)(logRecord.ReturnValue = base.getSchemaSeparator(commandBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setSchemaSeparator(DmCommandBuilder commandBuilder, string value)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "setSchemaSeparator", value);
		try
		{
			base.setSchemaSeparator(commandBuilder, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string QuoteIdentifier(DmCommandBuilder commandBuilder, string unquotedIdentifier)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "QuoteIdentifier", unquotedIdentifier);
		try
		{
			return (string)(logRecord.ReturnValue = base.QuoteIdentifier(commandBuilder, unquotedIdentifier));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void RefreshSchema(DmCommandBuilder commandBuilder)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "RefreshSchema");
		try
		{
			base.RefreshSchema(commandBuilder);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string UnquoteIdentifier(DmCommandBuilder commandBuilder, string quotedIdentifier)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "UnquoteIdentifier", quotedIdentifier);
		try
		{
			return (string)(logRecord.ReturnValue = base.UnquoteIdentifier(commandBuilder, quotedIdentifier));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void ApplyParameterInfo(DmCommandBuilder commandBuilder, DmParameter parameter, DataRow row, StatementType statementType, bool whereClause)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "ApplyParameterInfo", parameter, row, statementType, whereClause);
		try
		{
			base.ApplyParameterInfo(commandBuilder, parameter, row, statementType, whereClause);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetParameterName(DmCommandBuilder commandBuilder, int parameterOrdinal)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "GetParameterName", parameterOrdinal);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetParameterName(commandBuilder, parameterOrdinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetParameterName(DmCommandBuilder commandBuilder, string parameterName)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "GetParameterName", parameterName);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetParameterName(commandBuilder, parameterName));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override string GetParameterPlaceholder(DmCommandBuilder commandBuilder, int parameterOrdinal)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "GetParameterPlaceholder", parameterOrdinal);
		try
		{
			return (string)(logRecord.ReturnValue = base.GetParameterPlaceholder(commandBuilder, parameterOrdinal));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DataTable GetSchemaTable(DmCommandBuilder commandBuilder, DmCommand sourceCommand)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "GetSchemaTable", sourceCommand);
		try
		{
			return (DataTable)(logRecord.ReturnValue = base.GetSchemaTable(commandBuilder, sourceCommand));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override DmCommand InitializeCommand(DmCommandBuilder commandBuilder, DmCommand command)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "InitializeCommand", command);
		try
		{
			return (DmCommand)(logRecord.ReturnValue = base.InitializeCommand(commandBuilder, command));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void SetRowUpdatingHandler(DmCommandBuilder commandBuilder, DmDataAdapter adapter)
	{
		LogRecord logRecord = new LogRecord(commandBuilder, "SetRowUpdatingHandler", adapter);
		try
		{
			base.SetRowUpdatingHandler(commandBuilder, adapter);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override object getThis(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "geThis");
		try
		{
			return logRecord.ReturnValue = base.getThis(connectionStringBuilder, keyword);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void setThis(DmConnectionStringBuilder connectionStringBuilder, string keyword, object value)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "setThis", value);
		try
		{
			base.setThis(connectionStringBuilder, keyword, value);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool getIsFixedSize(DmConnectionStringBuilder connectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "getIsFixedSize");
		try
		{
			bool isFixedSize = base.getIsFixedSize(connectionStringBuilder);
			logRecord.ReturnValue = isFixedSize;
			return isFixedSize;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override int getCount(DmConnectionStringBuilder connectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "getCount");
		try
		{
			int count = base.getCount(connectionStringBuilder);
			logRecord.ReturnValue = count;
			return count;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override ICollection getKeys(DmConnectionStringBuilder connectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "getKeys");
		try
		{
			return (ICollection)(logRecord.ReturnValue = base.getKeys(connectionStringBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override ICollection getValues(DmConnectionStringBuilder connectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "getValues");
		try
		{
			return (ICollection)(logRecord.ReturnValue = base.getValues(connectionStringBuilder));
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void Clear(DmConnectionStringBuilder connectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "Clear");
		try
		{
			base.Clear(connectionStringBuilder);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool ContainsKey(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "ContainsKey", keyword);
		try
		{
			bool flag = base.ContainsKey(connectionStringBuilder, keyword);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool EquivalentTo(DmConnectionStringBuilder sourceConnectionStringBuilder, DmConnectionStringBuilder destConnectionStringBuilder)
	{
		LogRecord logRecord = new LogRecord(sourceConnectionStringBuilder, "EquivalentTo", destConnectionStringBuilder);
		try
		{
			bool flag = base.EquivalentTo(sourceConnectionStringBuilder, destConnectionStringBuilder);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool Remove(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "Remove", keyword);
		try
		{
			bool flag = base.Remove(connectionStringBuilder, keyword);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool ShouldSerialize(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "ShouldSerialize", keyword);
		try
		{
			bool flag = base.ShouldSerialize(connectionStringBuilder, keyword);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override bool TryGetValue(DmConnectionStringBuilder connectionStringBuilder, string keyword, out object value)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "TryGetValue", keyword);
		try
		{
			bool flag = base.TryGetValue(connectionStringBuilder, keyword, out value);
			logRecord.ReturnValue = flag;
			return flag;
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}

	public override void GetProperties(DmConnectionStringBuilder connectionStringBuilder, Hashtable propertyDescriptors)
	{
		LogRecord logRecord = new LogRecord(connectionStringBuilder, "GetProperties", propertyDescriptors);
		try
		{
			base.GetProperties(connectionStringBuilder, propertyDescriptors);
		}
		catch (Exception ex)
		{
			Exception ex2 = (logRecord.Throwable = ex);
			throw (ex2 is DmException) ? ((DmException)ex2).CreateCopy() : new DmException(ex2.ToString());
		}
		finally
		{
			doLog(logRecord);
		}
	}
}
