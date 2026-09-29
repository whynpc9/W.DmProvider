using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Transactions;

namespace W.Dm.filter;

internal interface IFilter
{
	string getServerVersion(DmConnection conn);

	string getDataSource(DmConnection conn);

	string getDatabase(DmConnection conn);

	int getConnectionTimeout(DmConnection conn);

	string getConnectionString(DmConnection conn);

	DbProviderFactory getDbProviderFactory(DmConnection conn);

	ConnectionState getState(DmConnection conn);

	DmTransaction BeginDbTransaction(DmConnection conn, System.Data.IsolationLevel isolationLevel);

	void ChangeDatabase(DmConnection conn, string databaseName);

	void Close(DmConnection conn);

	void ForceClose(DmConnection conn);

	DmCommand CreateDbCommand(DmConnection conn);

	void EnlistTransaction(DmConnection conn, Transaction transaction);

	DataTable GetSchema(DmConnection conn);

	DataTable GetSchema(DmConnection conn, string collectionName, string[] restrictionValues);

	DataTable GetSchema(DmConnection conn, string collectionName);

	void Open(DmConnection conn);

	FldrStatement Connection_fldrStatement(DmConnection conn, FldrConfig config);

	bool getDesignTimeVisible(DmCommand command);

	void setDesignTimeVisible(DmCommand command, bool value);

	CommandType getCommandType(DmCommand command);

	void setCommandType(DmCommand command, CommandType value);

	int getCommandTimeout(DmCommand command);

	void setCommandTimeout(DmCommand command, int value);

	string getCommandText(DmCommand command);

	void setCommandText(DmCommand command, string value);

	UpdateRowSource getUpdatedRowSource(DmCommand command);

	void setUpdatedRowSource(DmCommand command, UpdateRowSource value);

	DmConnection getDbConnection(DmCommand command);

	void setDbConnection(DmCommand command, DmConnection value);

	DmParameterCollection getDbParameterCollection(DmCommand command);

	DmTransaction getDbTransaction(DmCommand command);

	void setDbTransaction(DmCommand command, DmTransaction value);

	void Cancel(DmCommand command);

	int ExecuteNonQuery(DmCommand command);

	object ExecuteScalar(DmCommand command);

	void Prepare(DmCommand command);

	DmParameter CreateDbParameter(DmCommand command);

	DmDataReader ExecuteDbDataReader(DmCommand command, CommandBehavior behavior);

	long getExecuteId(DmCommand command);

	System.Data.IsolationLevel getIsolationLevel(DmTransaction transaction);

	DmConnection getDbConnection(DmTransaction transaction);

	void Commit(DmTransaction transaction);

	void Rollback(DmTransaction transaction);

	void Dispose(DmTransaction transaction, bool disposing);

	object getThis(DmDataReader dataReader, int index);

	object getThis(DmDataReader dataReader, string name);

	int getDepth(DmDataReader dataReader);

	bool getHasRows(DmDataReader dataReader);

	int getVisibleFieldCount(DmDataReader dataReader);

	int getRecordsAffected(DmDataReader dataReader);

	bool getIsClosed(DmDataReader dataReader);

	int getFieldCount(DmDataReader dataReader);

	void Close(DmDataReader dataReader);

	bool GetBoolean(DmDataReader dataReader, int ordinal);

	byte GetByte(DmDataReader dataReader, int ordinal);

	long GetBytes(DmDataReader dataReader, int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length);

	char GetChar(DmDataReader dataReader, int ordinal);

	long GetChars(DmDataReader dataReader, int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length);

	string GetDataTypeName(DmDataReader dataReader, int ordinal);

	DateTime GetDateTime(DmDataReader dataReader, int ordinal);

	decimal GetDecimal(DmDataReader dataReader, int ordinal);

	double GetDouble(DmDataReader dataReader, int ordinal);

	IEnumerator GetEnumerator(DmDataReader dataReader);

	Type GetFieldType(DmDataReader dataReader, int ordinal);

	float GetFloat(DmDataReader dataReader, int ordinal);

	Guid GetGuid(DmDataReader dataReader, int ordinal);

	short GetInt16(DmDataReader dataReader, int ordinal);

	int GetInt32(DmDataReader dataReader, int ordinal);

	long GetInt64(DmDataReader dataReader, int ordinal);

	string GetName(DmDataReader dataReader, int ordinal);

	int GetOrdinal(DmDataReader dataReader, string name);

	Type GetProviderSpecificFieldType(DmDataReader dataReader, int ordinal);

	object GetProviderSpecificValue(DmDataReader dataReader, int ordinal);

	int GetProviderSpecificValues(DmDataReader dataReader, object[] values);

	DataTable GetSchemaTable(DmDataReader dataReader);

	string GetString(DmDataReader dataReader, int ordinal);

	object GetValue(DmDataReader dataReader, int ordinal);

	int GetValues(DmDataReader dataReader, object[] values);

	bool IsDBNull(DmDataReader dataReader, int ordinal);

	bool NextResult(DmDataReader dataReader);

	bool Read(DmDataReader dataReader);

	void Dispose(DmDataReader dataReader, bool disposing);

	DmDataReader GetDbDataReader(DmDataReader dataReader, int ordinal);

	object getSyncRoot(DmParameterCollection parameterCollection);

	bool getIsSynchronized(DmParameterCollection parameterCollection);

	bool getIsReadOnly(DmParameterCollection parameterCollection);

	bool getIsFixedSize(DmParameterCollection parameterCollection);

	int getCount(DmParameterCollection parameterCollection);

	int Add(DmParameterCollection parameterCollection, object value);

	void AddRange(DmParameterCollection parameterCollection, Array values);

	void Clear(DmParameterCollection parameterCollection);

	bool Contains(DmParameterCollection parameterCollection, object value);

	bool Contains(DmParameterCollection parameterCollection, string value);

	void CopyTo(DmParameterCollection parameterCollection, Array array, int index);

	IEnumerator GetEnumerator(DmParameterCollection parameterCollection);

	int IndexOf(DmParameterCollection parameterCollection, object value);

	int IndexOf(DmParameterCollection parameterCollection, string parameterName);

	void Insert(DmParameterCollection parameterCollection, int index, object value);

	void Remove(DmParameterCollection parameterCollection, object value);

	void RemoveAt(DmParameterCollection parameterCollection, string parameterName);

	void RemoveAt(DmParameterCollection parameterCollection, int index);

	DmParameter GetParameter(DmParameterCollection parameterCollection, string parameterName);

	DmParameter GetParameter(DmParameterCollection parameterCollection, int index);

	void SetParameter(DmParameterCollection parameterCollection, int index, DmParameter value);

	void SetParameter(DmParameterCollection parameterCollection, string parameterName, DmParameter value);

	DbType getDbType(DmParameter parameter);

	void setDbType(DmParameter parameter, DbType value);

	ParameterDirection getDirection(DmParameter parameter);

	void setDirection(DmParameter parameter, ParameterDirection value);

	bool getIsNullable(DmParameter parameter);

	void setIsNullable(DmParameter parameter, bool value);

	string getParameterName(DmParameter parameter);

	void setParameterName(DmParameter parameter, string value);

	byte getPrecision(DmParameter parameter);

	void setPrecision(DmParameter parameter, byte value);

	byte getScale(DmParameter parameter);

	void setScale(DmParameter parameter, byte value);

	int getSize(DmParameter parameter);

	void setSize(DmParameter parameter, int value);

	string getSourceColumn(DmParameter parameter);

	void setSourceColumn(DmParameter parameter, string value);

	bool getSourceColumnNullMapping(DmParameter parameter);

	void setSourceColumnNullMapping(DmParameter parameter, bool value);

	DataRowVersion getSourceVersion(DmParameter parameter);

	void setSourceVersion(DmParameter parameter, DataRowVersion value);

	object getValue(DmParameter parameter);

	void setValue(DmParameter parameter, object value);

	void ResetDbType(DmParameter parameter);

	int getUpdateBatchSize(DmDataAdapter dataAdapter);

	void setUpdateBatchSize(DmDataAdapter dataAdapter, int value);

	int AddToBatch(DmDataAdapter dataAdapter, DmCommand command);

	void ClearBatch(DmDataAdapter dataAdapter);

	RowUpdatedEventArgs CreateRowUpdatedEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping);

	RowUpdatingEventArgs CreateRowUpdatingEvent(DmDataAdapter dataAdapter, DataRow dataRow, DmCommand command, StatementType statementType, DataTableMapping tableMapping);

	int ExecuteBatch(DmDataAdapter dataAdapter);

	int Fill(DmDataAdapter dataAdapter, DataTable[] dataTables, int startRecord, int maxRecords, DmCommand command, CommandBehavior behavior);

	int Fill(DmDataAdapter dataAdapter, DataTable dataTable, DmCommand command, CommandBehavior behavior);

	int Fill(DmDataAdapter dataAdapter, DataSet dataSet, int startRecord, int maxRecords, string srcTable, DmCommand command, CommandBehavior behavior);

	DataTable FillSchema(DmDataAdapter dataAdapter, DataTable dataTable, SchemaType schemaType, DmCommand command, CommandBehavior behavior);

	DataTable[] FillSchema(DmDataAdapter dataAdapter, DataSet dataSet, SchemaType schemaType, DmCommand command, string srcTable, CommandBehavior behavior);

	IDataParameter GetBatchedParameter(DmDataAdapter dataAdapter, int commandIdentifier, int parameterIndex);

	bool GetBatchedRecordsAffected(DmDataAdapter dataAdapter, int commandIdentifier, out int recordsAffected, out Exception error);

	void InitializeBatching(DmDataAdapter dataAdapter);

	void OnRowUpdated(DmDataAdapter dataAdapter, RowUpdatedEventArgs value);

	void OnRowUpdating(DmDataAdapter dataAdapter, RowUpdatingEventArgs value);

	void TerminateBatching(DmDataAdapter dataAdapter);

	int Update(DmDataAdapter dataAdapter, DataRow[] dataRows, DataTableMapping tableMapping);

	string getQuoteSuffix(DmCommandBuilder commandBuilder);

	void setQuoteSuffix(DmCommandBuilder commandBuilder, string value);

	string getQuotePrefix(DmCommandBuilder commandBuilder);

	void setQuotePrefix(DmCommandBuilder commandBuilder, string value);

	string getCatalogSeparator(DmCommandBuilder commandBuilder);

	void setCatalogSeparator(DmCommandBuilder commandBuilder, string value);

	CatalogLocation getCatalogLocation(DmCommandBuilder commandBuilder);

	void setCatalogLocation(DmCommandBuilder commandBuilder, CatalogLocation value);

	ConflictOption getConflictOption(DmCommandBuilder commandBuilder);

	void setConflictOption(DmCommandBuilder commandBuilder, ConflictOption value);

	string getSchemaSeparator(DmCommandBuilder commandBuilder);

	void setSchemaSeparator(DmCommandBuilder commandBuilder, string value);

	string QuoteIdentifier(DmCommandBuilder commandBuilder, string unquotedIdentifier);

	void RefreshSchema(DmCommandBuilder commandBuilder);

	string UnquoteIdentifier(DmCommandBuilder commandBuilder, string quotedIdentifier);

	void ApplyParameterInfo(DmCommandBuilder commandBuilder, DmParameter parameter, DataRow row, StatementType statementType, bool whereClause);

	string GetParameterName(DmCommandBuilder commandBuilder, int parameterOrdinal);

	string GetParameterName(DmCommandBuilder commandBuilder, string parameterName);

	string GetParameterPlaceholder(DmCommandBuilder commandBuilder, int parameterOrdinal);

	DataTable GetSchemaTable(DmCommandBuilder commandBuilder, DmCommand sourceCommand);

	DmCommand InitializeCommand(DmCommandBuilder commandBuilder, DmCommand command);

	void SetRowUpdatingHandler(DmCommandBuilder commandBuilder, DmDataAdapter adapter);

	object getThis(DmConnectionStringBuilder connectionStringBuilder, string keyword);

	void setThis(DmConnectionStringBuilder connectionStringBuilder, string keyword, object value);

	bool getIsFixedSize(DmConnectionStringBuilder connectionStringBuilder);

	int getCount(DmConnectionStringBuilder connectionStringBuilder);

	ICollection getKeys(DmConnectionStringBuilder connectionStringBuilder);

	ICollection getValues(DmConnectionStringBuilder connectionStringBuilder);

	void Clear(DmConnectionStringBuilder connectionStringBuilder);

	bool ContainsKey(DmConnectionStringBuilder connectionStringBuilder, string keyword);

	bool EquivalentTo(DmConnectionStringBuilder sourceConnectionStringBuilder, DmConnectionStringBuilder destConnectionStringBuilder);

	bool Remove(DmConnectionStringBuilder connectionStringBuilder, string keyword);

	bool ShouldSerialize(DmConnectionStringBuilder connectionStringBuilder, string keyword);

	bool TryGetValue(DmConnectionStringBuilder connectionStringBuilder, string keyword, out object value);

	void GetProperties(DmConnectionStringBuilder connectionStringBuilder, Hashtable propertyDescriptors);
}
