using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Dm.util;

internal class BlockingQueue<T> : IEnumerable
{
	internal Queue<T> _queue = new Queue<T>();

	private int _limit = -1;

	public readonly object lockObject = new object();

	internal int Count
	{
		get
		{
			int num = 0;
			lock (lockObject)
			{
				return _queue.Count;
			}
		}
	}

	internal BlockingQueue()
		: this(-1)
	{
	}

	internal BlockingQueue(int limit)
	{
		_limit = limit;
	}

	internal void Enqueue(T item)
	{
		lock (lockObject)
		{
			while (_limit != -1 && _queue.Count > _limit)
			{
				Monitor.Wait(lockObject, 1000);
			}
			_queue.Enqueue(item);
			Monitor.PulseAll(lockObject);
		}
	}

	internal List<T> Dequeue(int batchSize)
	{
		List<T> list = new List<T>();
		lock (lockObject)
		{
			while (_queue.Count == 0)
			{
				Monitor.Wait(lockObject, 1000);
			}
			int num = Math.Min(batchSize, _queue.Count);
			while (num-- > 0)
			{
				try
				{
					T item = _queue.Dequeue();
					list.Add(item);
				}
				catch (Exception)
				{
				}
			}
			Monitor.PulseAll(lockObject);
			return list;
		}
	}

	internal void Enqueue(T item, out bool succ)
	{
		succ = false;
		lock (lockObject)
		{
			if (_limit == -1 || _queue.Count < _limit)
			{
				_queue.Enqueue(item);
				succ = true;
			}
		}
	}

	internal T Dequeue()
	{
		T result = default(T);
		lock (lockObject)
		{
			if (_queue.Count > 0)
			{
				try
				{
					result = _queue.Dequeue();
					return result;
				}
				catch (Exception)
				{
				}
			}
		}
		return result;
	}

	internal T DequeueTimeout(int timeout)
	{
		T val = default(T);
		DateTime now = DateTime.Now;
		lock (lockObject)
		{
			while (_queue.Count == 0 && timeout > (int)(DateTime.Now - now).TotalMilliseconds)
			{
				Monitor.Wait(lockObject, 1000);
			}
			if (_queue.Count > 0)
			{
				try
				{
					val = _queue.Dequeue();
				}
				catch (Exception)
				{
				}
				AfterDequeueTimeout(val);
				Monitor.PulseAll(lockObject);
			}
		}
		return val;
	}

	internal T Poll()
	{
		lock (lockObject)
		{
			T result = default(T);
			try
			{
				result = _queue.Dequeue();
				return result;
			}
			catch (Exception)
			{
			}
			return result;
		}
	}

	internal T Peek()
	{
		lock (lockObject)
		{
			T result = default(T);
			try
			{
				result = _queue.Peek();
				return result;
			}
			catch (Exception)
			{
			}
			return result;
		}
	}

	internal bool Contains(T item)
	{
		lock (lockObject)
		{
			return _queue.Contains(item);
		}
	}

	internal void Clear()
	{
		lock (lockObject)
		{
			_queue.Clear();
		}
	}

	internal T[] ToArray()
	{
		return _queue.ToArray();
	}

	internal IEnumerator<T> GetEnumerator()
	{
		return new BlockingQueueIterator<T>(this);
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	internal virtual void AfterDequeueTimeout(T item)
	{
	}
}
