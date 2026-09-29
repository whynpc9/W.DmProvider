using System;
using System.Collections.Generic;
using W.Dm.Internal.Legacy.A;
using W.Dm.util;

namespace W.Dm;

internal class FldrBuffer
{
	private bool hTabFlag;

	private int seqNo;

	private b buffer;

	private List<LobData> lobList;

	private Dictionary<string, object[]> htabInfo;

	private Dictionary<int, List<object[]>> bpData;

	private Dictionary<int, Exception> errRowMap;

	public FldrBuffer()
	{
	}

	public FldrBuffer(int seqNo, b buffer, List<LobData> lobList, Dictionary<int, Exception> errRowMap)
	{
		this.seqNo = seqNo;
		this.buffer = buffer;
		this.lobList = lobList;
		this.errRowMap = errRowMap;
	}

	public FldrBuffer(int seqNo, string subTableName, b buffer, List<LobData> lobList, Dictionary<int, Exception> errRowMap)
	{
		htabInfo = new Dictionary<string, object[]>();
		this.seqNo = seqNo;
		hTabFlag = true;
		htabInfo.put(subTableName, new object[3] { buffer, lobList, errRowMap });
		this.errRowMap = errRowMap;
	}

	public int getRowNum()
	{
		return buffer.d(64);
	}

	public bool gethTabFlag()
	{
		return hTabFlag;
	}

	public void sethTabFlag(bool hTabFlag)
	{
		this.hTabFlag = hTabFlag;
	}

	public int getSeqNo()
	{
		return seqNo;
	}

	public void setSeqNo(int seqNo)
	{
		this.seqNo = seqNo;
	}

	public b getBuffer()
	{
		return buffer;
	}

	public void setBuffer(b buffer)
	{
		this.buffer = buffer;
	}

	public List<LobData> getLobList()
	{
		return lobList;
	}

	public void setLobList(List<LobData> lobList)
	{
		this.lobList = lobList;
	}

	public Dictionary<string, object[]> getHtabInfo()
	{
		return htabInfo;
	}

	public void setHtabInfo(Dictionary<string, object[]> htabInfo)
	{
		this.htabInfo = htabInfo;
	}

	public FldrBuffer setTabDetail(string tableName, object[] tabInfo)
	{
		if (htabInfo == null)
		{
			htabInfo = new Dictionary<string, object[]>();
		}
		htabInfo.put(tableName, tabInfo);
		return this;
	}

	public Dictionary<int, List<object[]>> getBpData()
	{
		return bpData;
	}

	public FldrBuffer setBpData(int bpId, object[] data)
	{
		if (bpData == null)
		{
			bpData = new Dictionary<int, List<object[]>>();
		}
		List<object[]> list = bpData.get(bpId);
		if (list == null)
		{
			list = new List<object[]>();
		}
		list.add(data);
		bpData.put(bpId, list);
		return this;
	}

	public Dictionary<int, Exception> getErrRowMap()
	{
		if (errRowMap != null)
		{
			return errRowMap;
		}
		return new Dictionary<int, Exception>(0);
	}

	public override string ToString()
	{
		return "FldrBuffer{hTabFlag=" + hTabFlag + ", seqNo=" + seqNo + ", buffer=" + buffer?.ToString() + ", lobList=" + lobList?.ToString() + "}";
	}
}
