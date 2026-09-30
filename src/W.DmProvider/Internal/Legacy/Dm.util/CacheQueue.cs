using System.Collections.Generic;

namespace W.Dm.util;

public class CacheQueue<T>
{
	private BlockingQueue<T> queue;

	public int maxSize;

	private bool enableLRU = true;

	public object lockObject = new object();

	public CacheQueue(int maxSize)
	{
		queue = new BlockingQueue<T>(maxSize);
		this.maxSize = maxSize;
	}

	public CacheQueue(int maxSize, bool enableLRU)
	{
		queue = new BlockingQueue<T>(maxSize);
		this.maxSize = maxSize;
		this.enableLRU = enableLRU;
	}

	protected bool needRemove(T element)
	{
		return false;
	}

	protected void beforeRemove(T element)
	{
	}

	public T get()
	{
		lock (lockObject)
		{
			T val = default(T);
			while ((val = queue.Poll()) != null && needRemove(val))
			{
				beforeRemove(val);
			}
			return val;
		}
	}

	public T peek()
	{
		if (queue.Count > 0)
		{
			return queue.Peek();
		}
		return default(T);
	}

	public bool put(T value)
	{
		lock (lockObject)
		{
			if (queue.Contains(value))
			{
				return true;
			}
			if (queue.Count < maxSize)
			{
				queue.Enqueue(value);
				return true;
			}
			if (enableLRU)
			{
				T element = get();
				beforeRemove(element);
				queue.Enqueue(value);
				return true;
			}
			beforeRemove(value);
			return false;
		}
	}

	public int size()
	{
		return queue.Count;
	}

	public void clear()
	{
		lock (lockObject)
		{
			queue.Clear();
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return queue.GetEnumerator();
	}

	public object clone()
	{
		CacheQueue<T> cacheQueue = new CacheQueue<T>(maxSize, enableLRU);
		IEnumerator<T> enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			cacheQueue.put(enumerator.Current);
		}
		return cacheQueue;
	}
}
