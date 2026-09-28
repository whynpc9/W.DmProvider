using System.Collections.Generic;
using System.Linq;
using A;
using Dm.util;

namespace Dm;

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
		access.A.B(0, (command != null) ? command.Statement.g() : 0);
		access.A.A(4, cmd);
		access.A.B(6, access.A.g() - 64);
		access.A.B(20, commitFlag);
	}

	protected override void doEncode()
	{
	}

	protected override List<FldrIndexInfo> doDecode()
	{
		if (TableInfo.msgVersion > 0)
		{
			access.A.G(20);
			int num = access.A.d();
			List<FldrIndexInfo> list = new List<FldrIndexInfo>();
			for (int i = 0; i < num; i++)
			{
				FldrIndexInfo fldrIndexInfo = new FldrIndexInfo();
				fldrIndexInfo.setIndexId(access.A.d());
				fldrIndexInfo.setNth(access.A.d());
				fldrIndexInfo.setValidFlag(access.A.b());
				list.add(fldrIndexInfo);
			}
			return list;
		}
		return Enumerable.Empty<FldrIndexInfo>().ToList();
	}
}
