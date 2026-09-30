using System;
using System.Collections.Generic;
using System.Linq;
using W.Dm.Internal.Legacy.A;
using W.Dm.util;

namespace W.Dm;

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
		access.__t02_field_04000AB9.H(indexInfos.size());
		foreach (FldrIndexInfo indexInfo in indexInfos)
		{
			access.__t02_field_04000AB9.H(indexInfo.getIndexId());
			access.__t02_field_04000AB9.H(indexInfo.getNth());
			access.__t02_field_04000AB9.A(indexInfo.getValidFlag());
		}
	}

	protected override int doDecode()
	{
		if (access.__t02_field_04000AB9.g() > 64)
		{
			access.__t02_field_04000AB9.A(access.a().ServerEncoding);
			access.__t02_field_04000AB9.A(access.a().ServerEncoding);
			access.__t02_field_04000AB9.A(access.a().ServerEncoding);
			string text = access.__t02_field_04000AB9.A(access.a().ServerEncoding);
			if (StringUtil.isNotEmpty(text))
			{
				throw new Exception("reset index info: " + text);
			}
		}
		return 0;
	}
}
