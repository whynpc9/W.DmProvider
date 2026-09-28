using System;
using System.Collections.Generic;
using A;
using Dm.util;

namespace Dm;

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
		access.A.a(array);
		access.A.a(array2);
		byte b2 = 0;
		access.A.A(b2);
	}

	protected override void beforeDecode()
	{
	}

	protected override FldrClusterInfo doDecode()
	{
		FldrClusterInfo fldrClusterInfo = new FldrClusterInfo();
		access.A.G(20);
		fldrClusterInfo.raftId = access.A.C();
		access.A.A(42, false, true);
		checkError();
		access.A.A(4, false, true);
		short num = access.A.C();
		num = (short)((num <= 0) ? 1 : num);
		int i = 0;
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		for (; i < num; i++)
		{
			int value = access.A.C();
			int key = access.A.d();
			dictionary.Add(key, value);
		}
		fldrClusterInfo.tabIdToBpIdMap = dictionary;
		short num2 = access.A.C();
		List<object[]> list = new List<object[]>(num2);
		for (int j = 0; j < num2; j++)
		{
			int num3 = access.A.C();
			int num4 = access.A.C();
			object[] item = new object[3]
			{
				num3,
				access.A.A(num4, access.a().ServerEncoding),
				Convert.ToString(access.A.c())
			};
			list.Add(item);
		}
		fldrClusterInfo.ipInfoList = list;
		return fldrClusterInfo;
	}
}
