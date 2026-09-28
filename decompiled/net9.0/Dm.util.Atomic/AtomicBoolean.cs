using System.Threading;

namespace Dm.util.Atomic;

public class AtomicBoolean
{
	private int value;

	public AtomicBoolean(bool initialValue)
	{
		value = (initialValue ? 1 : 0);
	}

	public bool compareAndSet(bool expect, bool update)
	{
		int num = (expect ? 1 : 0);
		int num2 = (update ? 1 : 0);
		return Interlocked.CompareExchange(ref value, num2, num) == num;
	}

	public bool get()
	{
		return value != 0;
	}

	public void set(bool newValue)
	{
		Interlocked.Exchange(ref value, newValue ? 1 : 0);
	}
}
