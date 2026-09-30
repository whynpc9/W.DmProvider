using System.Collections.Generic;

namespace W.Dm;

internal class RsLRUCache
{
	private LinkedList<KeyValuePair<RsKey, DmResultSetCache>> list;

	private Dictionary<RsKey, LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>>> map;

	private int capacity;

	private int count;

	internal RsLRUCache(int capacity)
	{
		this.capacity = capacity;
		list = new LinkedList<KeyValuePair<RsKey, DmResultSetCache>>();
		map = new Dictionary<RsKey, LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>>>(64);
	}

	internal bool Add(RsKey key, DmResultSetCache value)
	{
		if (value.BytesCount > capacity)
		{
			return false;
		}
		lock (map)
		{
			LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>> value2 = null;
			if (map.TryGetValue(key, out value2))
			{
				list.Remove(value2);
				count -= value2.Value.Value.BytesCount;
			}
			while (count + value.BytesCount > capacity)
			{
				LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>> last = list.Last;
				list.Remove(last);
				count -= last.Value.Value.BytesCount;
				map.Remove(last.Value.Key);
			}
			LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>> linkedListNode = new LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>>(new KeyValuePair<RsKey, DmResultSetCache>(key, value));
			list.AddFirst(linkedListNode);
			count += linkedListNode.Value.Value.BytesCount;
			map[key] = linkedListNode;
			return true;
		}
	}

	internal DmResultSetCache Find(RsKey key)
	{
		lock (map)
		{
			LinkedListNode<KeyValuePair<RsKey, DmResultSetCache>> value = null;
			if (!map.TryGetValue(key, out value))
			{
				return null;
			}
			list.Remove(value);
			list.AddFirst(value);
			return value.Value.Value;
		}
	}
}
