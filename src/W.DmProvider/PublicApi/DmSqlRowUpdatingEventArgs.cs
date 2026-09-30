using System.Data;
using System.Data.Common;

namespace W.Dm;

public class DmSqlRowUpdatingEventArgs : RowUpdatingEventArgs
{
	public DmSqlRowUpdatingEventArgs(DataRow dataRow, IDbCommand command, StatementType statementType, DataTableMapping tableMapping)
		: base(dataRow, command, statementType, tableMapping)
	{
	}
}
