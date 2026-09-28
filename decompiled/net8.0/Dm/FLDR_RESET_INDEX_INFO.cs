using System;
using System.Collections.Generic;
using System.Linq;
using A;
using Dm.util;

namespace Dm;

internal class FLDR_RESET_INDEX_INFO : MSG<int>
{
	private List<FldrIndexInfo> indexInfos = Enumerable.Empty<FldrIndexInfo>().ToList();

	public FLDR_RESET_INDEX_INFO(B access, List<FldrIndexInfo> indexInfos)
		: base(access, (short)123)
	{
		this.indexInfos = indexInfos;
	}

	protected override void doEncode()
	{
		access.A.H(indexInfos.size());
		foreach (FldrIndexInfo indexInfo in indexInfos)
		{
			access.A.H(indexInfo.getIndexId());
			access.A.H(indexInfo.getNth());
			access.A.A(indexInfo.getValidFlag());
		}
	}

	protected override int doDecode()
	{
		if (access.A.g() > 64)
		{
			access.A.A(access.a().ServerEncoding);
			access.A.A(access.a().ServerEncoding);
			access.A.A(access.a().ServerEncoding);
			string text = access.A.A(access.a().ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				throw new Exception("reset index info: " + text);
			}
		}
		return 0;
	}
}
