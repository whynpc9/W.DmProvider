using System;
using System.Collections.Generic;
using W.Dm.Internal.Legacy.A;
using W.Dm.util;

namespace W.Dm;

internal class FLDR_GET_MPP_INFO : MSG<FldrClusterInfo>
{
	private string schemaName;

	private string tableName;

	public FLDR_GET_MPP_INFO(B access, string schemaName, string tableName)
		: base(access, (short)112)
	{
		this.schemaName = schemaName;
		this.tableName = tableName;
	}

	protected override void doEncode()
	{
		byte[] array = ByteUtil.fromString(schemaName, access.a().ServerEncoding);
		if (array.Length > 128)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_SCHEMA_NAME);
		}
		byte[] array2 = ByteUtil.fromString(tableName, access.a().ServerEncoding);
		if (array2.Length > 128)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_DB_NAME);
		}
		access.__t02_field_04000AB9.a(array);
		access.__t02_field_04000AB9.a(array2);
		byte b2 = 0;
		access.__t02_field_04000AB9.A(b2);
	}

	protected override void beforeDecode()
	{
	}

	protected override FldrClusterInfo doDecode()
	{
		FldrClusterInfo fldrClusterInfo = new FldrClusterInfo();
		access.__t02_field_04000AB9.G(20);
		fldrClusterInfo.raftId = access.__t02_field_04000AB9.C();
		access.__t02_field_04000AB9.A(42, false, true);
		checkError();
		access.__t02_field_04000AB9.A(4, false, true);
		short num = access.__t02_field_04000AB9.C();
		num = (short)((num <= 0) ? 1 : num);
		int i = 0;
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		for (; i < num; i++)
		{
			int value = access.__t02_field_04000AB9.C();
			int key = access.__t02_field_04000AB9.d();
			dictionary.Add(key, value);
		}
		fldrClusterInfo.tabIdToBpIdMap = dictionary;
		short num2 = access.__t02_field_04000AB9.C();
		List<object[]> list = new List<object[]>(num2);
		for (int j = 0; j < num2; j++)
		{
			int num3 = access.__t02_field_04000AB9.C();
			int num4 = access.__t02_field_04000AB9.C();
			object[] item = new object[3]
			{
				num3,
				access.__t02_field_04000AB9.A(num4, access.a().ServerEncoding),
				Convert.ToString(access.__t02_field_04000AB9.c())
			};
			list.Add(item);
		}
		fldrClusterInfo.ipInfoList = list;
		return fldrClusterInfo;
	}
}
