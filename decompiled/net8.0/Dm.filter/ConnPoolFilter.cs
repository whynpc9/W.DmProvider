using System.Data;
using Dm.filter.log;
using Dm.util;

namespace Dm.filter;

internal class ConnPoolFilter : BaseFilter
{
	private readonly ILogger LOG = LogFactory.getLog(typeof(ConnPoolFilter));

	private ConnPoolCache _connPoolCache;

	internal static ConnPoolFilter Instance = new ConnPoolFilter();

	private ConnPoolCache PoolCache
	{
		get
		{
			if (_connPoolCache != null)
			{
				return _connPoolCache;
			}
			lock (this)
			{
				if (_connPoolCache != null)
				{
					return _connPoolCache;
				}
				_connPoolCache = new ConnPoolCache();
				return _connPoolCache;
			}
		}
	}

	internal int ConnPoolSize
	{
		set
		{
			PoolCache.Limit = value;
		}
	}

	internal int ConnPoolTimeout
	{
		set
		{
			PoolCache.Timeout = value;
		}
	}

	internal int ConnPoolIdleExpiredTime
	{
		set
		{
			PoolCache.IdleExpiredTime = value;
		}
	}

	internal int ConnPoolIdleClearInterval
	{
		set
		{
			PoolCache.IdleClearInterval = value;
		}
	}

	private ConnPoolFilter()
	{
	}

	public override void Open(DmConnection conn)
	{
		if (conn.do_State == ConnectionState.Open)
		{
			if (next != null)
			{
				next.Open(conn);
			}
			else
			{
				conn.Connect();
			}
			return;
		}
		conn.CheckProperty();
		conn.do_State = ConnectionState.Connecting;
		int num = 0;
		while (true)
		{
			num++;
			if (num % 100 == 0)
			{
				LOG.Error("[cnt]:" + num);
			}
			DmConnInstance dmConnInstance = PoolCache.Get(this, conn);
			if (dmConnInstance == null)
			{
				break;
			}
			if (ConnPoolCache.CheckConnectionSurvival(dmConnInstance))
			{
				dmConnInstance.ConnProperty.ClearAutoCommit();
				dmConnInstance.SetDmConnection(conn);
				conn.m_ConnInst = dmConnInstance;
				conn.RWInfo = dmConnInstance.RWInfo;
				conn.RecoverInfo = dmConnInstance.RecoverInfo;
				conn.do_State = ConnectionState.Open;
				if (((conn.ConnProperty.Schema == "") ? conn.ConnProperty.User : conn.ConnProperty.Schema) != dmConnInstance.ConnProperty.CurrentSchema)
				{
					DriverUtil.executeSetSchema(conn);
				}
				break;
			}
			PoolCache.Put(dmConnInstance, conn.getConnPoolKey(), pool: false);
		}
	}

	public override void Close(DmConnection conn)
	{
		DmConnInstance connInst = conn.m_ConnInst;
		if (connInst != null)
		{
			string connPoolKey = conn.getConnPoolKey();
			if (conn.RWInfo != null)
			{
				connInst.RWInfo = conn.RWInfo.init();
			}
			if (conn.RecoverInfo != null)
			{
				connInst.RecoverInfo = conn.RecoverInfo;
			}
			conn.RWInfo = null;
			conn.RecoverInfo = null;
			conn.ReleaseUnmanagedResource(pooled: true);
			conn.do_State = ConnectionState.Closed;
			PoolCache.Put(connInst, connPoolKey, pool: true);
		}
		base.Close(conn);
	}

	public override void ForceClose(DmConnection conn)
	{
		PoolCache.Put(conn.m_ConnInst, conn.getConnPoolKey(), pool: false);
		base.ForceClose(conn);
	}
}
