using System.Threading;
using Dm.util;

namespace Dm;

public class DmSavePoint
{
	public DmSavePoint standby;

	private const string NAME_PREFIX_DEFAULT = "DMDB_SVPT_";

	private static int SEED;

	private int id;

	public string name;

	public bool released;

	private DmConnection conn;

	public DmSavePoint(DmConnection conn, string name)
	{
		lock (conn)
		{
			this.conn = conn;
			if (StringUtil.isEmpty(name))
			{
				id = Interlocked.Increment(ref SEED);
				this.name = "DMDB_SVPT_" + id;
			}
			else
			{
				id = -1;
				this.name = name;
			}
			string sql = "SAVEPOINT \"" + StringUtil.processDoubleQuoteOfName(this.name) + "\"";
			DriverUtil.executeNonQuery(this.conn, sql, null);
		}
	}

	public int getSavepointId()
	{
		if (id < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CANNOT_GET_SAVEPOINT_ID);
		}
		return id;
	}

	public string getSavepointName()
	{
		if (id >= 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CANNOT_GET_SAVEPOINT_NAME);
		}
		return name;
	}

	public void release()
	{
		lock (conn)
		{
			if (!released)
			{
				string sql = "RELEASE_SAVEPOINT('" + StringUtil.processSingleQuoteOfName(name) + "')";
				DriverUtil.executeNonQuery(conn, sql, null);
				released = true;
			}
		}
	}
}
