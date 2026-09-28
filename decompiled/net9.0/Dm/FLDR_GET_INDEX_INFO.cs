using System.Collections.Generic;
using A;

namespace Dm;

internal class FLDR_GET_INDEX_INFO : MSG<List<FldrIndexInfo>>
{
	private string schemaName;

	private string tableName;

	private int parallelFlag;

	private int indexOption;

	public FLDR_GET_INDEX_INFO(B access, string schemaName, string tableName, int parallelFlag, int indexOption)
		: base(access, (short)122)
	{
		this.schemaName = schemaName;
		this.tableName = tableName;
		this.parallelFlag = parallelFlag;
		this.indexOption = indexOption;
	}

	protected override void doEncode()
	{
		access.A.a(schemaName, access.a().ServerEncoding);
		access.A.a(tableName, access.a().ServerEncoding);
		access.A.A((byte)0);
		access.A.A((byte)parallelFlag);
		access.A.A((byte)indexOption);
	}

	protected override List<FldrIndexInfo> doDecode()
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
			list.Add(fldrIndexInfo);
		}
		return list;
	}
}
