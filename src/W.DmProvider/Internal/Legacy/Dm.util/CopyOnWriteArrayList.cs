using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace W.Dm.util;

public class CopyOnWriteArrayList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable, ICollection, ICloneable
{
	private List<T> items = new List<T>();

	private readonly object objLock = new object();

	private readonly object syncRoot = new object();

	int ICollection.Count => items.Count;

	int ICollection<T>.Count => items.Count;

	public bool IsReadOnly => false;

	public bool IsSynchronized => true;

	public object SyncRoot => syncRoot;

	public T this[int index]
	{
		get
		{
			lock (objLock)
			{
				return items[index];
			}
		}
		set
		{
			lock (objLock)
			{
				List<T> list = items.ToList();
				list[index] = value;
				items = list;
			}
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		lock (objLock)
		{
			return items.ToList().GetEnumerator();
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		lock (objLock)
		{
			return items.ToList().GetEnumerator();
		}
	}

	public void add(T item)
	{
		Add(item);
	}

	public void Add(T item)
	{
		lock (objLock)
		{
			List<T> list = items.ToList();
			list.Add(item);
			items = list;
		}
	}

	public void Clear()
	{
		lock (objLock)
		{
			items = new List<T>();
		}
	}

	public bool Contains(T item)
	{
		lock (objLock)
		{
			return items.Contains(item);
		}
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		lock (objLock)
		{
			items.CopyTo(array, arrayIndex);
		}
	}

	public void CopyTo(Array array, int index)
	{
		if (array == null)
		{
			throw new ArgumentNullException("array");
		}
		if (array.Rank != 1)
		{
			throw new ArgumentException("array must be one-dimensional", "array");
		}
		if (array.GetLowerBound(0) != 0)
		{
			throw new ArgumentException("array must be a lower bound of zero", "array");
		}
		if (index < 0 || index > array.Length)
		{
			throw new ArgumentOutOfRangeException("array");
		}
		if (items.Count > array.Length - index)
		{
			throw new ArgumentException("array is to small");
		}
		lock (objLock)
		{
			T[] array2 = items.ToArray();
			for (int i = 0; i < items.Count; i++)
			{
				array.SetValue(array2[i], index + i);
			}
		}
	}

	public int IndexOf(T item)
	{
		lock (objLock)
		{
			return items.IndexOf(item);
		}
	}

	public void Insert(int index, T item)
	{
		lock (objLock)
		{
			List<T> list = items.ToList();
			list.Insert(index, item);
			items = list;
		}
	}

	public bool Remove(T item)
	{
		lock (objLock)
		{
			List<T> list = items.ToList();
			bool num = list.Remove(item);
			if (num)
			{
				items = list;
			}
			return num;
		}
	}

	public void RemoveAt(int index)
	{
		lock (objLock)
		{
			List<T> list = items.ToList();
			list.RemoveAt(index);
			items = list;
		}
	}

	public object Clone()
	{
		lock (objLock)
		{
			return items.ToList();
		}
	}
}
