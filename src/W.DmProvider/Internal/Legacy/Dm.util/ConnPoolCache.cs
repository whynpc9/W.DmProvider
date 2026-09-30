using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading;
using W.Dm.filter;

namespace W.Dm.util;

internal class ConnPoolCache
{
	private class ConnPool : BlockingQueue2<DmConnInstance>
	{
		private readonly object newConnLock = new object();

		private long totalCount;

		private int limit;

		private int timeout;

		private int expiredTime;

		internal ConnPool(int limit, int timeout, int expiredTime)
			: base(limit)
		{
			this.limit = limit;
			if (limit == 3)
			{
				Console.WriteLine("limit=3,call stack trace:");
				Console.WriteLine(new StackTrace());
			}
			this.timeout = timeout;
			this.expiredTime = expiredTime;
		}

		internal DmConnInstance Get(BaseFilter filter, DmConnection conn)
		{
			string text = "{get}";
			text += "->{TryDequeue()}";
			if (TryDequeue(out var item))
			{
				return item;
			}
			text += "->{TryDequeue() failed}";
			lock (newConnLock)
			{
				if (Interlocked.Read(in totalCount) < conn.ConnProperty.ConnPoolSize)
				{
					text += "->{TotalCount + 1 <= ConnPoolSize}";
					if (filter.next != null)
					{
						filter.next.Open(conn);
					}
					else
					{
						conn.Connect();
					}
					if (ConnectionState.Open == conn.State)
					{
						Interlocked.Increment(ref totalCount);
						DmConnInstance connInst = conn.m_ConnInst;
						connInst.CleanDmConnection();
						return connInst;
					}
					text += "->{generate failed}";
				}
			}
			text += "->{DequeueTimeout(timeout)}";
			if (DequeueTimeout(out item, timeout))
			{
				return item;
			}
			text += "->{DequeueTimeout(timeout) failed}";
			text = text + "->{totalCount=" + Interlocked.Read(in totalCount) + "}";
			text = text + "->{limit=" + limit + "}";
			text = text + "->{ConnPoolSize=" + conn.ConnProperty.ConnPoolSize + "}";
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNPOOL_TIMEOUT, text);
			return null;
		}

		internal void Put(DmConnInstance connInstance, bool pool)
		{
			if (!pool)
			{
				CloseConnInstance(connInstance);
				return;
			}
			if (!connInstance.getTransFinish() && !connInstance.RollbackWithoutAutoCommitCheck())
			{
				CloseConnInstance(connInstance);
				return;
			}
			connInstance.CleanDmConnection();
			connInstance.connPoolPutTime = DateTime.Now;
			if (!TryEnqueue(connInstance))
			{
				CloseConnInstance(connInstance);
			}
		}

		internal void Clear()
		{
			List<DmConnInstance> list = new List<DmConnInstance>();
			List<DmConnInstance> list2 = new List<DmConnInstance>();
			List<DmConnInstance> list3 = new List<DmConnInstance>();
			DmConnInstance item;
			while (TryDequeue(out item))
			{
				list.Add(item);
			}
			foreach (DmConnInstance item2 in list)
			{
				if (!CheckConnectionSurvival(item2))
				{
					list2.Add(item2);
				}
				else if (expiredTime > 0 && (DateTime.Now - item2.connPoolPutTime).TotalMilliseconds > (double)expiredTime)
				{
					list2.Add(item2);
				}
				else
				{
					list3.Add(item2);
				}
			}
			foreach (DmConnInstance item3 in list3)
			{
				if (!TryEnqueue(item3))
				{
					CloseConnInstance(item3);
				}
			}
			foreach (DmConnInstance item4 in list2)
			{
				CloseConnInstance(item4);
			}
		}

		private void CloseConnInstance(DmConnInstance connInstance)
		{
			connInstance.Close(keep_tcp: false);
			Interlocked.Decrement(ref totalCount);
		}
	}

	private Dictionary<string, ConnPool> _connPoolMap = new Dictionary<string, ConnPool>();

	private int _limit;

	private int _timeout;

	private int _expiredTime;

	private int _clearInterval;

	private Thread clearThread;

	internal int Limit
	{
		set
		{
			_limit = value;
		}
	}

	internal int Timeout
	{
		set
		{
			_timeout = value;
		}
	}

	internal int IdleExpiredTime
	{
		set
		{
			_expiredTime = value;
		}
	}

	internal int IdleClearInterval
	{
		set
		{
			_clearInterval = value;
		}
	}

	internal ConnPoolCache()
	{
		clearThread = new Thread(Clear);
		clearThread.Name = "DmProvider-connPoolClear";
		clearThread.IsBackground = true;
		clearThread.Start();
	}

	internal DmConnInstance Get(BaseFilter filter, DmConnection conn)
	{
		string connPoolKey = conn.getConnPoolKey();
		if (!_connPoolMap.ContainsKey(connPoolKey))
		{
			lock (_connPoolMap)
			{
				if (!_connPoolMap.ContainsKey(connPoolKey))
				{
					_connPoolMap[connPoolKey] = new ConnPool(_limit, _timeout, _expiredTime);
				}
			}
		}
		return _connPoolMap[connPoolKey].Get(filter, conn);
	}

	internal void Put(DmConnInstance connInstance, string key, bool pool)
	{
		_connPoolMap[key].Put(connInstance, pool);
	}

	private void Clear()
	{
		while (true)
		{
			try
			{
				Thread.Sleep(_clearInterval);
				foreach (ConnPool value in _connPoolMap.Values)
				{
					value.Clear();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.StackTrace);
			}
		}
	}

	internal static bool CheckConnectionSurvival(DmConnInstance connInstance)
	{
		bool flag = true;
		try
		{
			DmConnection dmConnection = new DmConnection();
			dmConnection.m_ConnInst = connInstance;
			connInstance.SetDmConnection(dmConnection);
			connInstance.SetAutoCommit(autoCommit: true);
			dmConnection.ConnProperty = connInstance.ConnProperty;
			dmConnection.do_State = ConnectionState.Open;
			DmCommand dmCommand = dmConnection.do_CreateDbCommand();
			dmCommand.do_CommandText = "select 1";
			DmDataReader dmDataReader = dmCommand.do_ExecuteDbDataReader(CommandBehavior.Default);
			flag = dmDataReader.do_Read() && dmDataReader.do_GetInt32(0) == 1;
			dmDataReader.do_Close();
			dmCommand.Close();
			connInstance.ConnProperty.ClearAutoCommit();
			dmConnection.ReleaseUnmanagedResource(pooled: true);
			dmConnection.do_State = ConnectionState.Closed;
			connInstance.CleanDmConnection();
		}
		catch (Exception)
		{
			flag = false;
		}
		return flag;
	}
}
