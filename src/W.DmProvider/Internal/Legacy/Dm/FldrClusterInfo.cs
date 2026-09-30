using System.Collections.Concurrent;
using System.Collections.Generic;
using W.Dm.util;

namespace W.Dm;

internal class FldrClusterInfo
{
	public Fldr primaryFldr;

	public short raftId;

	public Dictionary<int, int> tabIdToBpIdMap;

	public List<object[]> ipInfoList;

	public CopyOnWriteArrayList<DmConnection> connections = new CopyOnWriteArrayList<DmConnection>();

	public ConcurrentDictionary<int, Fldr> fldrsMap = new ConcurrentDictionary<int, Fldr>();
}
