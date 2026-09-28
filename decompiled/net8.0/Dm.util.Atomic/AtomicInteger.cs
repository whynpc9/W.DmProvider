using System.Threading;

namespace Dm.util.Atomic;

public class AtomicInteger
{
	private int value;

	public AtomicInteger(int initialValue)
	{
		value = initialValue;
	}

	public int incrementAndGet()
	{
		return Interlocked.Increment(ref value);
	}

	public int decrementAndGet()
	{
		return Interlocked.Decrement(ref value);
	}

	public int get()
	{
		return value;
	}

	public void set(int newValue)
	{
		Interlocked.Exchange(ref value, newValue);
	}

	public int addAndGet(int delta)
	{
		return Interlocked.Add(ref value, delta);
	}
}
