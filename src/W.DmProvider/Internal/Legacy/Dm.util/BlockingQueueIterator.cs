using System;
using System.Collections;
using System.Collections.Generic;

namespace W.Dm.util;

internal class BlockingQueueIterator<T> : IEnumerator<T>, IEnumerator, IDisposable
{
	internal BlockingQueue<T> queue;

	internal T current;

	internal bool disposed;

	public T Current => current;

	object IEnumerator.Current => current;

	public BlockingQueueIterator(BlockingQueue<T> queue)
	{
		this.queue = queue;
		Reset();
	}

	public void Dispose()
	{
		disposed = true;
	}

	public bool MoveNext()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("BlockingQueueIterator");
		}
		lock (queue.lockObject)
		{
			try
			{
				current = queue._queue.Dequeue();
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	public void Reset()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("BlockingQueueIterator");
		}
		lock (queue.lockObject)
		{
			current = default(T);
		}
	}
}
