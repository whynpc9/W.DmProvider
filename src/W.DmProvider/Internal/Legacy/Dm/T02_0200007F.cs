using System.Collections.Generic;
using W.Dm.Internal.Legacy.A;

namespace W.Dm;

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
		access.__t02_field_04000AB9.a(schemaName, access.a().ServerEncoding);
		access.__t02_field_04000AB9.a(tableName, access.a().ServerEncoding);
		access.__t02_field_04000AB9.A((byte)0);
		access.__t02_field_04000AB9.A((byte)parallelFlag);
		access.__t02_field_04000AB9.A((byte)indexOption);
	}

	protected override List<FldrIndexInfo> doDecode()
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
			list.Add(fldrIndexInfo);
		}
		return list;
	}
}
