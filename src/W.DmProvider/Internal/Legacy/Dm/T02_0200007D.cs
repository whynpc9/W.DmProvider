using System.Collections.Generic;
using System.Linq;
using W.Dm.Internal.Legacy.A;
using W.Dm.util;

namespace W.Dm;

internal class FLDR_CLR : MSG<List<FldrIndexInfo>>
{
	private int commitFlag;

	public FLDR_CLR(B access, int commitFlag)
		: base(access, (short)56)
	{
		this.commitFlag = commitFlag;
	}

	protected override void afterEncode()
	{
		access.__t02_field_04000AB9.B(0, (command != null) ? command.Statement.g() : 0);
		access.__t02_field_04000AB9.A(4, cmd);
		access.__t02_field_04000AB9.B(6, access.__t02_field_04000AB9.g() - 64);
		access.__t02_field_04000AB9.B(20, commitFlag);
	}

	protected override void doEncode()
	{
	}

	protected override List<FldrIndexInfo> doDecode()
	{
		if (TableInfo.msgVersion > 0)
		{
			access.__t02_field_04000AB9.G(20);
			int num = access.__t02_field_04000AB9.d();
			List<FldrIndexInfo> list = new List<FldrIndexInfo>();
			for (int i = 0; i < num; i++)
			{
				FldrIndexInfo fldrIndexInfo = new FldrIndexInfo();
				fldrIndexInfo.setIndexId(access.__t02_field_04000AB9.d());
				fldrIndexInfo.setNth(access.__t02_field_04000AB9.d());
				fldrIndexInfo.setValidFlag(access.__t02_field_04000AB9.__t02_method_06000ABD());
				list.add(fldrIndexInfo);
			}
			return list;
		}
		return Enumerable.Empty<FldrIndexInfo>().ToList();
	}
}
