using System.Collections.Concurrent;

namespace W.Dm.util;

public class BlockingQueue2<T>
{
	public readonly BlockingCollection<T> _queue;

	public int Count => _queue.Count;

	public BlockingQueue2(int limit = -1)
	{
		_queue = ((limit > 0) ? new BlockingCollection<T>(new ConcurrentQueue<T>(), limit) : new BlockingCollection<T>(new ConcurrentQueue<T>()));
	}

	public bool TryDequeue(out T item)
	{
		return _queue.TryTake(out item);
	}

	public bool TryEnqueue(T item)
	{
		return _queue.TryAdd(item);
	}

	public void Enqueue(T item)
	{
		_queue.Add(item);
	}

	public T Dequeue()
	{
		return _queue.Take();
	}

	public bool DequeueTimeout(out T item, int timeout)
	{
		return _queue.TryTake(out item, timeout);
	}
}
