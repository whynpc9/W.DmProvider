using System;
using System.Diagnostics;
using System.Text;

namespace W.Dm.filter.log;

internal class LogRecord
{
	internal class NullData
	{
	}

	public static readonly NullData NULL = new NullData();

	private string source;

	private string method;

	private object[] @params;

	private object returnValue;

	private Exception e;

	private Stopwatch startTime;

	private string sql;

	private long executeId = -1L;

	public virtual Exception Throwable
	{
		get
		{
			return e;
		}
		set
		{
			e = value;
		}
	}

	public virtual object ReturnValue
	{
		get
		{
			return returnValue;
		}
		set
		{
			if (value == null)
			{
				returnValue = NULL;
			}
			else
			{
				returnValue = value;
			}
		}
	}

	public virtual string Sql
	{
		get
		{
			return sql;
		}
		set
		{
			sql = value;
		}
	}

	public virtual long ExecuteId
	{
		get
		{
			return executeId;
		}
		set
		{
			executeId = value;
		}
	}

	public LogRecord(object source, string method, params object[] @params)
	{
		this.source = Logger.FormatSource(source);
		this.method = method;
		this.@params = @params;
		startTime = Stopwatch.StartNew();
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder(128);
		stringBuilder.Append(Logger.FormatTrace(source, method, returnValue, @params));
		if (sql != null)
		{
			stringBuilder.Append(formatSql(sql));
		}
		startTime.Stop();
		stringBuilder.Append(formatUsedTime((long)((double)startTime.ElapsedMilliseconds * Math.Pow(10.0, 6.0))));
		if (executeId != -1)
		{
			stringBuilder.Append(formatExecuteId(executeId));
		}
		return stringBuilder.ToString();
	}

	private string formatUsedTime(long nanosecond)
	{
		if ((double)nanosecond < Math.Pow(10.0, 6.0))
		{
			return " [USED TIME]: " + nanosecond + "ns;";
		}
		if ((double)nanosecond < Math.Pow(10.0, 9.0))
		{
			return " [USED TIME]: " + (double)nanosecond / Math.Pow(10.0, 6.0) + "ms;";
		}
		return " [USED TIME]: " + (double)nanosecond / Math.Pow(10.0, 9.0) + "s;";
	}

	private string formatSql(string sql)
	{
		return "[SQL]: " + sql;
	}

	private string formatExecuteId(long executeId)
	{
		return " [EXEC_ID]: " + executeId + ";";
	}
}
