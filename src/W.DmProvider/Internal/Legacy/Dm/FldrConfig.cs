using System.Collections.Generic;

namespace W.Dm;

public class FldrConfig
{
	public string schemaName;

	public string tableName;

	public bool setIdentity;

	public bool sorted;

	public int maxRows = 5000;

	public int indexOption = 1;

	public short bldrNum = 64;

	public bool flushFlag;

	public bool parallelFlag;

	public int maxErrorNum = 1;

	public string logFileName;

	public HashSet<int> defualtColumns;

	public override string ToString()
	{
		return "FldrConfig{schemaName='" + schemaName + "', tableName='" + tableName + "', setIdentity=" + setIdentity + ", sorted=" + sorted + ", maxRows=" + maxRows + ", indexOption=" + indexOption + ", bldrNum=" + bldrNum + ", flushFlag=" + flushFlag + ", parallelFlag=" + parallelFlag + ", maxErrorNum=" + maxErrorNum + ", logFileName='" + logFileName + "', defualtColumns=" + defualtColumns?.ToString() + "}";
	}
}
