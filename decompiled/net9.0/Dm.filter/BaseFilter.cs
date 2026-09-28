using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Transactions;
using Dm.Config;
using Dm.filter.log;
using Dm.filter.reconnect;
using Dm.filter.rw;

namespace Dm.filter;

public class BaseFilter : IFilter
{
	public BaseFilter next;

	internal static void CreateFilterChain(IFilterInfo filterInfo, DmConnProperty connProperty = null)
	{
		BaseFilter baseFilter = null;
		IList<BaseFilter> list = null;
		if (DmSvcConfig.logLevel != LogLevel.OFF)
		{
			list = list ?? new List<BaseFilter>();
			list.Add(LogFilter.Instance);
			filterInfo.LogInfo = new LogInfo();
		}
		if (connProperty != null)
		{
			if (connProperty.ConnPooling)
			{
				list = list ?? new List<BaseFilter>();
				ConnPoolFilter instance = ConnPoolFilter.Instance;
				instance.ConnPoolSize = connProperty.ConnPoolSize;
				instance.ConnPoolTimeout = connProperty.ConnPoolTimeout;
				instance.ConnPoolIdleExpiredTime = connProperty.ConnPoolIdleExpiredTime;
				instance.ConnPoolIdleClearInterval = connProperty.ConnPoolIdleClearInterval;
				list.Add(instance);
			}
			if (connProperty.DoSwitch != DoSwitch.OFF)
			{
				list = list ?? new List<BaseFilter>();
				list.Add(ReconnectFilter.Instance);
				filterInfo.RecoverInfo = new RecoverInfo();
			}
			if (connProperty.RwSeparate > 0)
			{
				list = list ?? new List<BaseFilter>();
				list.Add(RWFilter2.Instance);
				filterInfo.RWInfo = new RWInfo();
			}
		}
		if (list != null && list.Count > 0)
		{
			baseFilter = list[0];
			BaseFilter baseFilter2 = baseFilter;
			for (int i = 1; i < list.Count; i++)
			{
				baseFilter2.next = list[i];
				baseFilter2 = baseFilter2.next;
			}
		}
		filterInfo.filterHead = baseFilter;
	}

	public virtual string getServerVersion(DmConnection conn)
	{
		if (next != null)
		{
			return next.getServerVersion(conn);
		}
		return conn.do_ServerVersion;
	}

	public virtual string getDataSource(DmConnection conn)
	{
		if (next != null)
		{
			return next.getDataSource(conn);
		}
		return conn.do_DataSource;
	}

	public virtual string getDatabase(DmConnection conn)
	{
		if (next != null)
		{
			return next.getDatabase(conn);
		}
		return conn.do_Database;
	}

	public virtual int getConnectionTimeout(DmConnection conn)
	{
		if (next != null)
		{
			return next.getConnectionTimeout(conn);
		}
		return conn.do_ConnectionTimeout;
	}

	public virtual string getConnectionString(DmConnection conn)
	{
		if (next != null)
		{
			return next.getConnectionString(conn);
		}
		return conn.do_ConnectionString;
	}

	public virtual DbProviderFactory getDbProviderFactory(DmConnection conn)
	{
		if (next != null)
		{
			return next.getDbProviderFactory(conn);
		}
		return conn.do_DbProviderFactory;
	}

	public virtual ConnectionState getState(DmConnection conn)
	{
		if (next != null)
		{
			return next.getState(conn);
		}
		return conn.do_State;
	}

	public virtual void ChangeDatabase(DmConnection conn, string databaseName)
	{
		if (next != null)
		{
			next.ChangeDatabase(conn, databaseName);
		}
		else
		{
			conn.do_ChangeDatabase(databaseName);
		}
	}

	public virtual DmTransaction BeginDbTransaction(DmConnection conn, System.Data.IsolationLevel isolationLevel)
	{
		if (next != null)
		{
			return next.BeginDbTransaction(conn, isolationLevel);
		}
		return conn.do_BeginDbTransaction(isolationLevel);
	}

	public virtual DmCommand CreateDbCommand(DmConnection conn)
	{
		if (next != null)
		{
			return next.CreateDbCommand(conn);
		}
		return conn.do_CreateDbCommand();
	}

	public virtual void Close(DmConnection conn)
	{
		if (next != null)
		{
			next.Close(conn);
		}
		else
		{
			conn.do_Close();
		}
	}

	public virtual void ForceClose(DmConnection conn)
	{
		if (next != null)
		{
			next.ForceClose(conn);
		}
		else
		{
			conn.do_Close();
		}
	}

	public virtual void EnlistTransaction(DmConnection conn, Transaction transaction)
	{
		if (next != null)
		{
			next.EnlistTransaction(conn, transaction);
		}
		else
		{
			conn.do_EnlistTransaction(transaction);
		}
	}

	public virtual DataTable GetSchema(DmConnection conn)
	{
		if (next != null)
		{
			return next.GetSchema(conn);
		}
		return conn.do_GetSchema();
	}

	public virtual DataTable GetSchema(DmConnection conn, string collectionName, string[] restrictionValues)
	{
		if (next != null)
		{
			return next.GetSchema(conn, collectionName, restrictionValues);
		}
		return conn.do_GetSchema(collectionName, restrictionValues);
	}

	public virtual DataTable GetSchema(DmConnection conn, string collectionName)
	{
		if (next != null)
		{
			return next.GetSchema(conn, collectionName);
		}
		return conn.do_GetSchema(collectionName);
	}

	public virtual void Open(DmConnection conn)
	{
		if (next != null)
		{
			next.Open(conn);
		}
		else
		{
			conn.Connect();
		}
	}

	public virtual DmStruct CreateStruct(DmConnection conn, string typeName, object[] attributes)
	{
		if (next != null)
		{
			return next.CreateStruct(conn, typeName, attributes);
		}
		return conn.do_CreateStruct(typeName, attributes);
	}

	public virtual DmArray CreateArray(DmConnection conn, string typeName, object[] elements)
	{
		if (next != null)
		{
			return next.CreateArray(conn, typeName, elements);
		}
		return conn.do_CreateArray(typeName, elements);
	}

	public virtual DmStruct CreateIndexTable(DmConnection conn, string typeName, Dictionary<string, object> dictionary)
	{
		if (next != null)
		{
			return next.CreateIndexTable(conn, typeName, dictionary);
		}
		return conn.do_CreateIndexTable(typeName, dictionary);
	}

	public virtual FldrStatement Connection_fldrStatement(DmConnection conn, FldrConfig config)
	{
		if (next != null)
		{
			return next.Connection_fldrStatement(conn, config);
		}
		return conn.do_fldrStatement(config);
	}

	public virtual bool getDesignTimeVisible(DmCommand command)
	{
		if (next != null)
		{
			return next.getDesignTimeVisible(command);
		}
		return command.do_DesignTimeVisible;
	}

	public virtual void setDesignTimeVisible(DmCommand command, bool value)
	{
		if (next != null)
		{
			next.setDesignTimeVisible(command, value);
		}
		else
		{
			command.do_DesignTimeVisible = value;
		}
	}

	public virtual CommandType getCommandType(DmCommand command)
	{
		if (next != null)
		{
			return next.getCommandType(command);
		}
		return command.do_CommandType;
	}

	public virtual void setCommandType(DmCommand command, CommandType value)
	{
		if (next != null)
		{
			next.setCommandType(command, value);
		}
		else
		{
			command.do_CommandType = value;
		}
	}

	public virtual int getCommandTimeout(DmCommand command)
	{
		if (next != null)
		{
			return next.getCommandTimeout(command);
		}
		return command.do_CommandTimeout;
	}

	public virtual void setCommandTimeout(DmCommand command, int value)
	{
		if (next != null)
		{
			next.setCommandTimeout(command, value);
		}
		else
		{
			command.do_CommandTimeout = value;
		}
	}

	public virtual string getCommandText(DmCommand command)
	{
		if (next != null)
		{
			return next.getCommandText(command);
		}
		return command.do_CommandText;
	}

	public virtual void setCommandText(DmCommand command, string value)
	{
		if (next != null)
		{
			next.setCommandText(command, value);
		}
		else
		{
			command.do_CommandText = value;
		}
	}

	public virtual UpdateRowSource getUpdatedRowSource(DmCommand command)
	{
		if (next != null)
		{
			return next.getUpdatedRowSource(command);
		}
		return command.do_UpdatedRowSource;
	}

	public virtual void setUpdatedRowSource(DmCommand command, UpdateRowSource value)
	{
		if (next != null)
		{
			next.setUpdatedRowSource(command, value);
		}
		else
		{
			command.do_UpdatedRowSource = value;
		}
	}

	public virtual DmConnection getDbConnection(DmCommand command)
	{
		if (next != null)
		{
			return next.getDbConnection(command);
		}
		return command.do_DbConnection;
	}

	public virtual void setDbConnection(DmCommand command, DmConnection value)
	{
		if (next != null)
		{
			next.setDbConnection(command, value);
		}
		else
		{
			command.do_DbConnection = value;
		}
	}

	public virtual DmParameterCollection getDbParameterCollection(DmCommand command)
	{
		if (next != null)
		{
			return next.getDbParameterCollection(command);
		}
		return command.do_DbParameterCollection;
	}

	public virtual DmTransaction getDbTransaction(DmCommand command)
	{
		if (next != null)
		{
			return next.getDbTransaction(command);
		}
		return command.do_DbTransaction;
	}

	public virtual void setDbTransaction(DmCommand command, DmTransaction value)
	{
		if (next != null)
		{
			next.setDbTransaction(command, value);
		}
		else
		{
			command.do_DbTransaction = value;
		}
	}

	public virtual void Cancel(DmCommand command)
	{
		if (next != null)
		{
			next.Cancel(command);
		}
		else
		{
			command.do_Cancel();
		}
	}

	public virtual int ExecuteNonQuery(DmCommand command)
	{
		if (next != null)
		{
			return next.ExecuteNonQuery(command);
		}
		return command.do_ExecuteNonQuery();
	}

	public virtual object ExecuteScalar(DmCommand command)
	{
		if (next != null)
		{
			return next.ExecuteScalar(command);
		}
		return command.do_ExecuteScalar();
	}

	public virtual void Prepare(DmCommand command)
	{
		if (next != null)
		{
			next.Prepare(command);
		}
		else
		{
			command.do_Prepare();
		}
	}

	public virtual DmParameter CreateDbParameter(DmCommand command)
	{
		if (next != null)
		{
			return next.CreateDbParameter(command);
		}
		return command.do_CreateDbParameter();
	}

	public virtual DmDataReader ExecuteDbDataReader(DmCommand command, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.ExecuteDbDataReader(command, behavior);
		}
		return command.do_ExecuteDbDataReader(behavior);
	}

	public virtual long getExecuteId(DmCommand command)
	{
		if (next != null)
		{
			return next.getExecuteId(command);
		}
		return command.do_GetExecuteId();
	}

	public virtual System.Data.IsolationLevel getIsolationLevel(DmTransaction transaction)
	{
		if (next != null)
		{
			return next.getIsolationLevel(transaction);
		}
		return transaction.do_IsolationLevel;
	}

	public virtual DmConnection getDbConnection(DmTransaction transaction)
	{
		if (next != null)
		{
			return next.getDbConnection(transaction);
		}
		return transaction.do_DbConnection;
	}

	public virtual void Commit(DmTransaction transaction)
	{
		if (next != null)
		{
			next.Commit(transaction);
		}
		else
		{
			transaction.do_Commit();
		}
	}

	public virtual void Rollback(DmTransaction transaction)
	{
		if (next != null)
		{
			next.Rollback(transaction);
		}
		else
		{
			transaction.do_Rollback();
		}
	}

	public virtual void Dispose(DmTransaction transaction, bool disposing)
	{
		if (next != null)
		{
			next.Dispose(transaction, disposing);
		}
		else
		{
			transaction.do_Dispose(disposing);
		}
	}

	public virtual void Save(DmTransaction transaction, string savepointName)
	{
		if (next != null)
		{
			next.Save(transaction, savepointName);
		}
		else
		{
			transaction.do_Save(savepointName);
		}
	}

	public virtual void Rollback(DmTransaction transaction, string savepointName)
	{
		if (next != null)
		{
			next.Rollback(transaction, savepointName);
		}
		else
		{
			transaction.do_Rollback(savepointName);
		}
	}

	public virtual void Release(DmTransaction transaction, string savepointName)
	{
		if (next != null)
		{
			next.Release(transaction, savepointName);
		}
		else
		{
			transaction.do_Release(savepointName);
		}
	}

	public virtual object getThis(DmDataReader dataReader, int index)
	{
		if (next != null)
		{
			return next.getThis(dataReader, index);
		}
		return dataReader.do_this(index);
	}

	public virtual object getThis(DmDataReader dataReader, string name)
	{
		if (next != null)
		{
			return next.getThis(dataReader, name);
		}
		return dataReader.do_this(name);
	}

	public virtual int getDepth(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getDepth(dataReader);
		}
		return dataReader.do_Depth;
	}

	public virtual bool getHasRows(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getHasRows(dataReader);
		}
		return dataReader.do_HasRows;
	}

	public virtual int getVisibleFieldCount(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getVisibleFieldCount(dataReader);
		}
		return dataReader.do_VisibleFieldCount;
	}

	public virtual int getRecordsAffected(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getRecordsAffected(dataReader);
		}
		return dataReader.do_RecordsAffected;
	}

	public virtual bool getIsClosed(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getIsClosed(dataReader);
		}
		return dataReader.do_IsClosed;
	}

	public virtual int getFieldCount(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.getFieldCount(dataReader);
		}
		return dataReader.do_FieldCount;
	}

	public virtual void Close(DmDataReader dataReader)
	{
		if (next != null)
		{
			next.Close(dataReader);
		}
		else
		{
			dataReader.do_Close();
		}
	}

	public virtual bool GetBoolean(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetBoolean(dataReader, ordinal);
		}
		return dataReader.do_GetBoolean(ordinal);
	}

	public virtual byte GetByte(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetByte(dataReader, ordinal);
		}
		return dataReader.do_GetByte(ordinal);
	}

	public virtual long GetBytes(DmDataReader dataReader, int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length)
	{
		if (next != null)
		{
			return next.GetBytes(dataReader, ordinal, dataOffset, buffer, bufferOffset, length);
		}
		return dataReader.do_GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
	}

	public virtual char GetChar(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetChar(dataReader, ordinal);
		}
		return dataReader.do_GetChar(ordinal);
	}

	public virtual long GetChars(DmDataReader dataReader, int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length)
	{
		if (next != null)
		{
			return next.GetChars(dataReader, ordinal, dataOffset, buffer, bufferOffset, length);
		}
		return dataReader.do_GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
	}

	public virtual string GetDataTypeName(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetDataTypeName(dataReader, ordinal);
		}
		return dataReader.do_GetDataTypeName(ordinal);
	}

	public virtual DateTime GetDateTime(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetDateTime(dataReader, ordinal);
		}
		return dataReader.do_GetDateTime(ordinal);
	}

	public virtual decimal GetDecimal(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetDecimal(dataReader, ordinal);
		}
		return dataReader.do_GetDecimal(ordinal);
	}

	public virtual double GetDouble(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetDouble(dataReader, ordinal);
		}
		return dataReader.do_GetDouble(ordinal);
	}

	public virtual IEnumerator GetEnumerator(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.GetEnumerator(dataReader);
		}
		return dataReader.do_GetEnumerator();
	}

	public virtual Type GetFieldType(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetFieldType(dataReader, ordinal);
		}
		return dataReader.do_GetFieldType(ordinal);
	}

	public virtual float GetFloat(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetFloat(dataReader, ordinal);
		}
		return dataReader.do_GetFloat(ordinal);
	}

	public virtual Guid GetGuid(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetGuid(dataReader, ordinal);
		}
		return dataReader.do_GetGuid(ordinal);
	}

	public virtual short GetInt16(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetInt16(dataReader, ordinal);
		}
		return dataReader.do_GetInt16(ordinal);
	}

	public virtual int GetInt32(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetInt32(dataReader, ordinal);
		}
		return dataReader.do_GetInt32(ordinal);
	}

	public virtual long GetInt64(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetInt64(dataReader, ordinal);
		}
		return dataReader.do_GetInt64(ordinal);
	}

	public virtual string GetName(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetName(dataReader, ordinal);
		}
		return dataReader.do_GetName(ordinal);
	}

	public virtual int GetOrdinal(DmDataReader dataReader, string name)
	{
		if (next != null)
		{
			return next.GetOrdinal(dataReader, name);
		}
		return dataReader.do_GetOrdinal(name);
	}

	public virtual Type GetProviderSpecificFieldType(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetProviderSpecificFieldType(dataReader, ordinal);
		}
		return dataReader.do_GetProviderSpecificFieldType(ordinal);
	}

	public virtual object GetProviderSpecificValue(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetProviderSpecificValue(dataReader, ordinal);
		}
		return dataReader.do_GetProviderSpecificValue(ordinal);
	}

	public virtual int GetProviderSpecificValues(DmDataReader dataReader, object[] values)
	{
		if (next != null)
		{
			return next.GetProviderSpecificValues(dataReader, values);
		}
		return dataReader.do_GetProviderSpecificValues(values);
	}

	public virtual DataTable GetSchemaTable(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.GetSchemaTable(dataReader);
		}
		return dataReader.do_GetSchemaTable();
	}

	public virtual string GetString(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetString(dataReader, ordinal);
		}
		return dataReader.do_GetString(ordinal);
	}

	public virtual object GetValue(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetValue(dataReader, ordinal);
		}
		return dataReader.do_GetValue(ordinal);
	}

	public virtual int GetValues(DmDataReader dataReader, object[] values)
	{
		if (next != null)
		{
			return next.GetValues(dataReader, values);
		}
		return dataReader.do_GetValues(values);
	}

	public virtual bool IsDBNull(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.IsDBNull(dataReader, ordinal);
		}
		return dataReader.do_IsDBNull(ordinal);
	}

	public virtual bool NextResult(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.NextResult(dataReader);
		}
		return dataReader.do_NextResult();
	}

	public virtual bool Read(DmDataReader dataReader)
	{
		if (next != null)
		{
			return next.Read(dataReader);
		}
		return dataReader.do_Read();
	}

	public virtual void Dispose(DmDataReader dataReader, bool disposing)
	{
		if (next != null)
		{
			next.Dispose(dataReader, disposing);
		}
		else
		{
			dataReader.do_Dispose(disposing);
		}
	}

	public virtual DmDataReader GetDbDataReader(DmDataReader dataReader, int ordinal)
	{
		if (next != null)
		{
			return next.GetDbDataReader(dataReader, ordinal);
		}
		return dataReader.do_GetDbDataReader(ordinal);
	}

	public virtual object getSyncRoot(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.getSyncRoot(parameterCollection);
		}
		return parameterCollection.do_SyncRoot;
	}

	public virtual bool getIsSynchronized(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.getIsSynchronized(parameterCollection);
		}
		return parameterCollection.do_IsSynchronized;
	}

	public virtual bool getIsReadOnly(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.getIsReadOnly(parameterCollection);
		}
		return parameterCollection.do_IsReadOnly;
	}

	public virtual bool getIsFixedSize(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.getIsFixedSize(parameterCollection);
		}
		return parameterCollection.do_IsFixedSize;
	}

	public virtual int getCount(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.getCount(parameterCollection);
		}
		return parameterCollection.do_Count;
	}

	public virtual int Add(DmParameterCollection parameterCollection, object value)
	{
		if (next != null)
		{
			return next.Add(parameterCollection, value);
		}
		return parameterCollection.do_Add(value);
	}

	public virtual void AddRange(DmParameterCollection parameterCollection, Array values)
	{
		if (next != null)
		{
			next.AddRange(parameterCollection, values);
		}
		else
		{
			parameterCollection.do_AddRange(values);
		}
	}

	public virtual void Clear(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			next.Clear(parameterCollection);
		}
		else
		{
			parameterCollection.do_Clear();
		}
	}

	public virtual bool Contains(DmParameterCollection parameterCollection, object value)
	{
		if (next != null)
		{
			return next.Contains(parameterCollection, value);
		}
		return parameterCollection.do_Contains(value);
	}

	public virtual bool Contains(DmParameterCollection parameterCollection, string value)
	{
		if (next != null)
		{
			return next.Contains(parameterCollection, value);
		}
		return parameterCollection.do_Contains(value);
	}

	public virtual void CopyTo(DmParameterCollection parameterCollection, Array array, int index)
	{
		if (next != null)
		{
			next.CopyTo(parameterCollection, array, index);
		}
		else
		{
			parameterCollection.do_CopyTo(array, index);
		}
	}

	public virtual IEnumerator GetEnumerator(DmParameterCollection parameterCollection)
	{
		if (next != null)
		{
			return next.GetEnumerator(parameterCollection);
		}
		return parameterCollection.do_GetEnumerator();
	}

	public virtual int IndexOf(DmParameterCollection parameterCollection, object value)
	{
		if (next != null)
		{
			return next.IndexOf(parameterCollection, value);
		}
		return parameterCollection.do_IndexOf(value);
	}

	public virtual int IndexOf(DmParameterCollection parameterCollection, string parameterName)
	{
		if (next != null)
		{
			return next.IndexOf(parameterCollection, parameterName);
		}
		return parameterCollection.do_IndexOf(parameterName);
	}

	public virtual void Insert(DmParameterCollection parameterCollection, int index, object value)
	{
		if (next != null)
		{
			next.Insert(parameterCollection, index, value);
		}
		else
		{
			parameterCollection.do_Insert(index, value);
		}
	}

	public virtual void Remove(DmParameterCollection parameterCollection, object value)
	{
		if (next != null)
		{
			next.Remove(parameterCollection, value);
		}
		else
		{
			parameterCollection.do_Remove(value);
		}
	}

	public virtual void RemoveAt(DmParameterCollection parameterCollection, string parameterName)
	{
		if (next != null)
		{
			next.RemoveAt(parameterCollection, parameterName);
		}
		else
		{
			parameterCollection.do_RemoveAt(parameterName);
		}
	}

	public virtual void RemoveAt(DmParameterCollection parameterCollection, int index)
	{
		if (next != null)
		{
			next.RemoveAt(parameterCollection, index);
		}
		else
		{
			parameterCollection.do_RemoveAt(index);
		}
	}

	public virtual DmParameter GetParameter(DmParameterCollection parameterCollection, string parameterName)
	{
		if (next != null)
		{
			return next.GetParameter(parameterCollection, parameterName);
		}
		return parameterCollection.do_GetParameter(parameterName);
	}

	public virtual DmParameter GetParameter(DmParameterCollection parameterCollection, int index)
	{
		if (next != null)
		{
			return next.GetParameter(parameterCollection, index);
		}
		return parameterCollection.do_GetParameter(index);
	}

	public virtual void SetParameter(DmParameterCollection parameterCollection, int index, DmParameter value)
	{
		if (next != null)
		{
			next.SetParameter(parameterCollection, index, value);
		}
		else
		{
			parameterCollection.do_SetParameter(index, value);
		}
	}

	public virtual void SetParameter(DmParameterCollection parameterCollection, string parameterName, DmParameter value)
	{
		if (next != null)
		{
			next.SetParameter(parameterCollection, parameterName, value);
		}
		else
		{
			parameterCollection.do_SetParameter(parameterName, value);
		}
	}

	public virtual DbType getDbType(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getDbType(parameter);
		}
		return parameter.do_DbType;
	}

	public virtual void setDbType(DmParameter parameter, DbType value)
	{
		if (next != null)
		{
			next.setDbType(parameter, value);
		}
		else
		{
			parameter.do_DbType = value;
		}
	}

	public virtual ParameterDirection getDirection(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getDirection(parameter);
		}
		return parameter.do_Direction;
	}

	public virtual void setDirection(DmParameter parameter, ParameterDirection value)
	{
		if (next != null)
		{
			next.setDirection(parameter, value);
		}
		else
		{
			parameter.do_Direction = value;
		}
	}

	public virtual bool getIsNullable(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getIsNullable(parameter);
		}
		return parameter.do_IsNullable;
	}

	public virtual void setIsNullable(DmParameter parameter, bool value)
	{
		if (next != null)
		{
			next.setIsNullable(parameter, value);
		}
		else
		{
			parameter.do_IsNullable = value;
		}
	}

	public virtual string getParameterName(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getParameterName(parameter);
		}
		return parameter.do_ParameterName;
	}

	public virtual void setParameterName(DmParameter parameter, string value)
	{
		if (next != null)
		{
			next.setParameterName(parameter, value);
		}
		else
		{
			parameter.do_ParameterName = value;
		}
	}

	public virtual byte getPrecision(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getPrecision(parameter);
		}
		return parameter.do_Precision;
	}

	public virtual void setPrecision(DmParameter parameter, byte value)
	{
		if (next != null)
		{
			next.setPrecision(parameter, value);
		}
		else
		{
			parameter.do_Precision = value;
		}
	}

	public virtual byte getScale(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getScale(parameter);
		}
		return parameter.do_Scale;
	}

	public virtual void setScale(DmParameter parameter, byte value)
	{
		if (next != null)
		{
			next.setScale(parameter, value);
		}
		else
		{
			parameter.do_Scale = value;
		}
	}

	public virtual int getSize(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getSize(parameter);
		}
		return parameter.do_Size;
	}

	public virtual void setSize(DmParameter parameter, int value)
	{
		if (next != null)
		{
			next.setSize(parameter, value);
		}
		else
		{
			parameter.do_Size = value;
		}
	}

	public virtual string getSourceColumn(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getSourceColumn(parameter);
		}
		return parameter.do_SourceColumn;
	}

	public virtual void setSourceColumn(DmParameter parameter, string value)
	{
		if (next != null)
		{
			next.setSourceColumn(parameter, value);
		}
		else
		{
			parameter.do_SourceColumn = value;
		}
	}

	public virtual bool getSourceColumnNullMapping(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getSourceColumnNullMapping(parameter);
		}
		return parameter.do_SourceColumnNullMapping;
	}

	public virtual void setSourceColumnNullMapping(DmParameter parameter, bool value)
	{
		if (next != null)
		{
			next.setSourceColumnNullMapping(parameter, value);
		}
		else
		{
			parameter.do_SourceColumnNullMapping = value;
		}
	}

	public virtual DataRowVersion getSourceVersion(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getSourceVersion(parameter);
		}
		return parameter.do_SourceVersion;
	}

	public virtual void setSourceVersion(DmParameter parameter, DataRowVersion value)
	{
		if (next != null)
		{
			next.setSourceVersion(parameter, value);
		}
		else
		{
			parameter.do_SourceVersion = value;
		}
	}

	public virtual object getValue(DmParameter parameter)
	{
		if (next != null)
		{
			return next.getValue(parameter);
		}
		return parameter.do_Value;
	}

	public virtual void setValue(DmParameter parameter, object value)
	{
		if (next != null)
		{
			next.setValue(parameter, value);
		}
		else
		{
			parameter.do_Value = value;
		}
	}

	public virtual void ResetDbType(DmParameter parameter)
	{
		if (next != null)
		{
			next.ResetDbType(parameter);
		}
		else
		{
			parameter.do_ResetDbType();
		}
	}

	public virtual int getUpdateBatchSize(DmDataAdapter dataAdapter)
	{
		if (next != null)
		{
			return next.getUpdateBatchSize(dataAdapter);
		}
		return dataAdapter.do_UpdateBatchSize;
	}

	public virtual void setUpdateBatchSize(DmDataAdapter dataAdapter, int value)
	{
		if (next != null)
		{
			next.setUpdateBatchSize(dataAdapter, value);
		}
		else
		{
			dataAdapter.do_UpdateBatchSize = value;
		}
	}

	public virtual int AddToBatch(DmDataAdapter dataAdapter, DmCommand command)
	{
		if (next != null)
		{
			return next.AddToBatch(dataAdapter, command);
		}
		return dataAdapter.do_AddToBatch(command);
	}

	public virtual void ClearBatch(DmDataAdapter dataAdapter)
	{
		if (next != null)
		{
			next.ClearBatch(dataAdapter);
		}
		else
		{
			dataAdapter.do_ClearBatch();
		}
	}

	public virtual RowUpdatedEventArgs CreateRowUpdatedEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping)
	{
		if (next != null)
		{
			return next.CreateRowUpdatedEvent(dataAdapter, dataRow, command, statementType, tableMapping);
		}
		return dataAdapter.do_CreateRowUpdatedEvent(dataRow, command, statementType, tableMapping);
	}

	public virtual RowUpdatingEventArgs CreateRowUpdatingEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping)
	{
		if (next != null)
		{
			return next.CreateRowUpdatingEvent(dataAdapter, dataRow, command, statementType, tableMapping);
		}
		return dataAdapter.do_CreateRowUpdatingEvent(dataRow, command, statementType, tableMapping);
	}

	public virtual int ExecuteBatch(DmDataAdapter dataAdapter)
	{
		if (next != null)
		{
			return next.ExecuteBatch(dataAdapter);
		}
		return dataAdapter.do_ExecuteBatch();
	}

	public virtual int Fill(DmDataAdapter dataAdapter, DataTable[] dataTables, int startRecord, int maxRecords, DmCommand command, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.Fill(dataAdapter, dataTables, startRecord, maxRecords, command, behavior);
		}
		return dataAdapter.do_Fill(dataTables, startRecord, maxRecords, command, behavior);
	}

	public virtual int Fill(DmDataAdapter dataAdapter, DataTable dataTable, DmCommand command, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.Fill(dataAdapter, dataTable, command, behavior);
		}
		return dataAdapter.do_Fill(dataTable, command, behavior);
	}

	public virtual int Fill(DmDataAdapter dataAdapter, DataSet dataSet, int startRecord, int maxRecords, string srcTable, DmCommand command, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.Fill(dataAdapter, dataSet, startRecord, maxRecords, srcTable, command, behavior);
		}
		return dataAdapter.do_Fill(dataSet, startRecord, maxRecords, srcTable, command, behavior);
	}

	public virtual DataTable FillSchema(DmDataAdapter dataAdapter, DataTable dataTable, SchemaType schemaType, DmCommand command, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.FillSchema(dataAdapter, dataTable, schemaType, command, behavior);
		}
		return dataAdapter.do_FillSchema(dataTable, schemaType, command, behavior);
	}

	public virtual DataTable[] FillSchema(DmDataAdapter dataAdapter, DataSet dataSet, SchemaType schemaType, DmCommand command, string srcTable, CommandBehavior behavior)
	{
		if (next != null)
		{
			return next.FillSchema(dataAdapter, dataSet, schemaType, command, srcTable, behavior);
		}
		return dataAdapter.do_FillSchema(dataSet, schemaType, command, srcTable, behavior);
	}

	public virtual IDataParameter GetBatchedParameter(DmDataAdapter dataAdapter, int commandIdentifier, int parameterIndex)
	{
		if (next != null)
		{
			return next.GetBatchedParameter(dataAdapter, commandIdentifier, parameterIndex);
		}
		return dataAdapter.do_GetBatchedParameter(commandIdentifier, parameterIndex);
	}

	public virtual bool GetBatchedRecordsAffected(DmDataAdapter dataAdapter, int commandIdentifier, out int recordsAffected, out Exception error)
	{
		if (next != null)
		{
			return next.GetBatchedRecordsAffected(dataAdapter, commandIdentifier, out recordsAffected, out error);
		}
		return dataAdapter.do_GetBatchedRecordsAffected(commandIdentifier, out recordsAffected, out error);
	}

	public virtual void InitializeBatching(DmDataAdapter dataAdapter)
	{
		if (next != null)
		{
			next.InitializeBatching(dataAdapter);
		}
		else
		{
			dataAdapter.do_InitializeBatching();
		}
	}

	public virtual void OnRowUpdated(DmDataAdapter dataAdapter, RowUpdatedEventArgs value)
	{
		if (next != null)
		{
			next.OnRowUpdated(dataAdapter, value);
		}
		else
		{
			dataAdapter.do_OnRowUpdated(value);
		}
	}

	public virtual void OnRowUpdating(DmDataAdapter dataAdapter, RowUpdatingEventArgs value)
	{
		if (next != null)
		{
			next.OnRowUpdating(dataAdapter, value);
		}
		else
		{
			dataAdapter.do_OnRowUpdating(value);
		}
	}

	public virtual void TerminateBatching(DmDataAdapter dataAdapter)
	{
		if (next != null)
		{
			next.TerminateBatching(dataAdapter);
		}
		else
		{
			dataAdapter.do_TerminateBatching();
		}
	}

	public virtual int Update(DmDataAdapter dataAdapter, DataRow[] dataRows, DataTableMapping tableMapping)
	{
		if (next != null)
		{
			return next.Update(dataAdapter, dataRows, tableMapping);
		}
		return dataAdapter.do_Update(dataRows, tableMapping);
	}

	public virtual string getQuoteSuffix(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getQuoteSuffix(commandBuilder);
		}
		return commandBuilder.do_QuoteSuffix;
	}

	public virtual void setQuoteSuffix(DmCommandBuilder commandBuilder, string value)
	{
		if (next != null)
		{
			next.setQuoteSuffix(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_QuoteSuffix = value;
		}
	}

	public virtual string getQuotePrefix(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getQuotePrefix(commandBuilder);
		}
		return commandBuilder.do_QuotePrefix;
	}

	public virtual void setQuotePrefix(DmCommandBuilder commandBuilder, string value)
	{
		if (next != null)
		{
			next.setQuotePrefix(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_QuotePrefix = value;
		}
	}

	public virtual string getCatalogSeparator(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getCatalogSeparator(commandBuilder);
		}
		return commandBuilder.do_CatalogSeparator;
	}

	public virtual void setCatalogSeparator(DmCommandBuilder commandBuilder, string value)
	{
		if (next != null)
		{
			next.setCatalogSeparator(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_CatalogSeparator = value;
		}
	}

	public virtual CatalogLocation getCatalogLocation(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getCatalogLocation(commandBuilder);
		}
		return commandBuilder.do_CatalogLocation;
	}

	public virtual void setCatalogLocation(DmCommandBuilder commandBuilder, CatalogLocation value)
	{
		if (next != null)
		{
			next.setCatalogLocation(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_CatalogLocation = value;
		}
	}

	public virtual ConflictOption getConflictOption(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getConflictOption(commandBuilder);
		}
		return commandBuilder.do_ConflictOption;
	}

	public virtual void setConflictOption(DmCommandBuilder commandBuilder, ConflictOption value)
	{
		if (next != null)
		{
			next.setConflictOption(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_ConflictOption = value;
		}
	}

	public virtual string getSchemaSeparator(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			return next.getSchemaSeparator(commandBuilder);
		}
		return commandBuilder.do_SchemaSeparator;
	}

	public virtual void setSchemaSeparator(DmCommandBuilder commandBuilder, string value)
	{
		if (next != null)
		{
			next.setSchemaSeparator(commandBuilder, value);
		}
		else
		{
			commandBuilder.do_SchemaSeparator = value;
		}
	}

	public virtual string QuoteIdentifier(DmCommandBuilder commandBuilder, string unquotedIdentifier)
	{
		if (next != null)
		{
			return next.QuoteIdentifier(commandBuilder, unquotedIdentifier);
		}
		return commandBuilder.do_QuoteIdentifier(unquotedIdentifier);
	}

	public virtual void RefreshSchema(DmCommandBuilder commandBuilder)
	{
		if (next != null)
		{
			next.RefreshSchema(commandBuilder);
		}
		else
		{
			commandBuilder.do_RefreshSchema();
		}
	}

	public virtual string UnquoteIdentifier(DmCommandBuilder commandBuilder, string quotedIdentifier)
	{
		if (next != null)
		{
			return next.UnquoteIdentifier(commandBuilder, quotedIdentifier);
		}
		return commandBuilder.do_UnquoteIdentifier(quotedIdentifier);
	}

	public virtual void ApplyParameterInfo(DmCommandBuilder commandBuilder, DmParameter parameter, DataRow row, StatementType statementType, bool whereClause)
	{
		if (next != null)
		{
			next.ApplyParameterInfo(commandBuilder, parameter, row, statementType, whereClause);
		}
		else
		{
			commandBuilder.do_ApplyParameterInfo(parameter, row, statementType, whereClause);
		}
	}

	public virtual string GetParameterName(DmCommandBuilder commandBuilder, int parameterOrdinal)
	{
		if (next != null)
		{
			return next.GetParameterName(commandBuilder, parameterOrdinal);
		}
		return commandBuilder.do_GetParameterName(parameterOrdinal);
	}

	public virtual string GetParameterName(DmCommandBuilder commandBuilder, string parameterName)
	{
		if (next != null)
		{
			return next.GetParameterName(commandBuilder, parameterName);
		}
		return commandBuilder.do_GetParameterName(parameterName);
	}

	public virtual string GetParameterPlaceholder(DmCommandBuilder commandBuilder, int parameterOrdinal)
	{
		if (next != null)
		{
			return next.GetParameterPlaceholder(commandBuilder, parameterOrdinal);
		}
		return commandBuilder.do_GetParameterPlaceholder(parameterOrdinal);
	}

	public virtual DataTable GetSchemaTable(DmCommandBuilder commandBuilder, DmCommand sourceCommand)
	{
		if (next != null)
		{
			return next.GetSchemaTable(commandBuilder, sourceCommand);
		}
		return commandBuilder.do_GetSchemaTable(sourceCommand);
	}

	public virtual DmCommand InitializeCommand(DmCommandBuilder commandBuilder, DmCommand command)
	{
		if (next != null)
		{
			return next.InitializeCommand(commandBuilder, command);
		}
		return commandBuilder.do_InitializeCommand(command);
	}

	public virtual void SetRowUpdatingHandler(DmCommandBuilder commandBuilder, DmDataAdapter adapter)
	{
		if (next != null)
		{
			next.SetRowUpdatingHandler(commandBuilder, adapter);
		}
		else
		{
			commandBuilder.do_SetRowUpdatingHandler(adapter);
		}
	}

	public virtual object getThis(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		if (next != null)
		{
			return next.getThis(connectionStringBuilder, keyword);
		}
		return connectionStringBuilder.do_getThis(keyword);
	}

	public virtual void setThis(DmConnectionStringBuilder connectionStringBuilder, string keyword, object value)
	{
		if (next != null)
		{
			next.setThis(connectionStringBuilder, keyword, value);
		}
		else
		{
			connectionStringBuilder.do_setThis(keyword, value);
		}
	}

	public virtual bool getIsFixedSize(DmConnectionStringBuilder connectionStringBuilder)
	{
		if (next != null)
		{
			return next.getIsFixedSize(connectionStringBuilder);
		}
		return connectionStringBuilder.do_IsFixedSize;
	}

	public virtual int getCount(DmConnectionStringBuilder connectionStringBuilder)
	{
		if (next != null)
		{
			return next.getCount(connectionStringBuilder);
		}
		return connectionStringBuilder.do_Count;
	}

	public virtual ICollection getKeys(DmConnectionStringBuilder connectionStringBuilder)
	{
		if (next != null)
		{
			return next.getKeys(connectionStringBuilder);
		}
		return connectionStringBuilder.do_Keys;
	}

	public virtual ICollection getValues(DmConnectionStringBuilder connectionStringBuilder)
	{
		if (next != null)
		{
			return next.getValues(connectionStringBuilder);
		}
		return connectionStringBuilder.do_Values;
	}

	public virtual void Clear(DmConnectionStringBuilder connectionStringBuilder)
	{
		if (next != null)
		{
			next.Clear(connectionStringBuilder);
		}
		else
		{
			connectionStringBuilder.do_Clear();
		}
	}

	public virtual bool ContainsKey(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		if (next != null)
		{
			return next.ContainsKey(connectionStringBuilder, keyword);
		}
		return connectionStringBuilder.do_ContainsKey(keyword);
	}

	public virtual bool EquivalentTo(DmConnectionStringBuilder sourceConnectionStringBuilder, DmConnectionStringBuilder destConnectionStringBuilder)
	{
		if (next != null)
		{
			return next.EquivalentTo(sourceConnectionStringBuilder, destConnectionStringBuilder);
		}
		return sourceConnectionStringBuilder.do_EquivalentTo(destConnectionStringBuilder);
	}

	public virtual bool Remove(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		if (next != null)
		{
			return next.Remove(connectionStringBuilder, keyword);
		}
		return connectionStringBuilder.do_Remove(keyword);
	}

	public virtual bool ShouldSerialize(DmConnectionStringBuilder connectionStringBuilder, string keyword)
	{
		if (next != null)
		{
			return next.ShouldSerialize(connectionStringBuilder, keyword);
		}
		return connectionStringBuilder.do_ShouldSerialize(keyword);
	}

	public virtual bool TryGetValue(DmConnectionStringBuilder connectionStringBuilder, string keyword, out object value)
	{
		if (next != null)
		{
			return next.TryGetValue(connectionStringBuilder, keyword, out value);
		}
		return connectionStringBuilder.do_TryGetValue(keyword, out value);
	}

	public virtual void GetProperties(DmConnectionStringBuilder connectionStringBuilder, Hashtable propertyDescriptors)
	{
		if (next != null)
		{
			next.GetProperties(connectionStringBuilder, propertyDescriptors);
		}
		else
		{
			connectionStringBuilder.do_GetProperties(propertyDescriptors);
		}
	}

	internal int ResultSetMetaData_getColumnCount(DmdbResultSetMetaData resultSetMetaData)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnCount(resultSetMetaData);
		}
		return resultSetMetaData.do_getColumnCount();
	}

	internal bool ResultSetMetaData_isAutoIncrement(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isAutoIncrement(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isAutoIncrement(column);
	}

	internal bool ResultSetMetaData_isCaseSensitive(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isCaseSensitive(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isCaseSensitive(column);
	}

	internal bool ResultSetMetaData_isSearchable(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isSearchable(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isSearchable(column);
	}

	internal bool ResultSetMetaData_isCurrency(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isCurrency(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isCurrency(column);
	}

	internal int ResultSetMetaData_isNullable(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isNullable(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isNullable(column);
	}

	internal bool ResultSetMetaData_isSigned(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isSigned(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isSigned(column);
	}

	internal int ResultSetMetaData_getColumnDisplaySize(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnDisplaySize(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnDisplaySize(column);
	}

	internal string ResultSetMetaData_getColumnLabel(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnLabel(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnLabel(column);
	}

	internal string ResultSetMetaData_getColumnName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnName(column);
	}

	internal string ResultSetMetaData_getSchemaName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getSchemaName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getSchemaName(column);
	}

	internal int ResultSetMetaData_getPrecision(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getPrecision(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getPrecision(column);
	}

	internal int ResultSetMetaData_getScale(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getScale(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getScale(column);
	}

	internal string ResultSetMetaData_getTableName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getTableName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getTableName(column);
	}

	internal string ResultSetMetaData_getCatalogName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getCatalogName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getCatalogName(column);
	}

	internal int ResultSetMetaData_getColumnType(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnType(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnType(column);
	}

	internal string ResultSetMetaData_getColumnTypeName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnTypeName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnTypeName(column);
	}

	internal bool ResultSetMetaData_isReadOnly(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isReadOnly(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isReadOnly(column);
	}

	internal bool ResultSetMetaData_isWritable(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isWritable(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isWritable(column);
	}

	internal bool ResultSetMetaData_isDefinitelyWritable(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_isDefinitelyWritable(resultSetMetaData, column);
		}
		return resultSetMetaData.do_isDefinitelyWritable(column);
	}

	internal string ResultSetMetaData_getColumnClassName(DmdbResultSetMetaData resultSetMetaData, int column)
	{
		if (next != null)
		{
			return next.ResultSetMetaData_getColumnClassName(resultSetMetaData, column);
		}
		return resultSetMetaData.do_getColumnClassName(column);
	}
}
