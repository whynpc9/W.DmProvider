using System.Threading;
using W.Dm.Internal.Sessions;
using W.Dm.util;

namespace W.Dm;

public class DmSavePoint
{
	public DmSavePoint standby;

	private const string NAME_PREFIX_DEFAULT = "DMDB_SVPT_";

	private static int SEED;

	private int id;

	public string name;

	public bool released;

	private DmConnection conn;

	private readonly DmTransaction transaction;

	private readonly DmSession boundSession;

	public DmSavePoint(DmConnection conn, string name)
	{
		this.conn = conn ?? throw new System.ArgumentNullException(nameof(conn));
		transaction = conn.m_ConnInst?.Transaction ?? throw new System.InvalidOperationException("Savepoint requires an active transaction.");
		boundSession = conn.Session;
		InitializeName(name);
		transaction.do_Save(this.name);
	}

	private DmSavePoint(DmConnection conn, DmTransaction transaction, string name)
	{
		this.conn = conn;
		this.transaction = transaction;
		boundSession = conn.Session;
		InitializeName(name);
	}

	internal static DmSavePoint CreateHandle(DmConnection conn, DmTransaction transaction, string name) =>
		new DmSavePoint(conn, transaction, name);

	private void InitializeName(string value)
	{
		if (StringUtil.isEmpty(value))
		{
			id = Interlocked.Increment(ref SEED);
			name = NAME_PREFIX_DEFAULT + id;
		}
		else
		{
			id = -1;
			name = value;
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
		if (released) return;
		transaction.CheckBoundSession();
		if (!object.ReferenceEquals(conn.Session, boundSession))
			throw new System.InvalidOperationException("Savepoint belongs to a closed or replaced session.");
		transaction.do_Release(name);
		released = true;
	}
}
