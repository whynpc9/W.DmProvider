using System.Collections.Generic;
using System.Linq;
using W.Dm.Internal.Legacy.A;

namespace W.Dm;

internal class FLDR_SET2 : MSG<string>
{
	private int setIdentity;

	private const int generateLog = 1;

	private int sorted;

	private int bdtaSize;

	private int indexOption;

	private int noMpp;

	private int charset;

	private int lobFromMsg = 1;

	private int ignoreConflict;

	private const int bufferNumber = 0;

	private short setId;

	private short bldrNumber = 64;

	private string schemaName;

	private string tableName;

	private string lobDirectoryName = "";

	private byte flushFlag;

	private byte parallelFlag;

	private short sequenceCount;

	private bool decodeFlag;

	private List<SequenceNumInfo> sequenceNumInfoList;

	private const short COLUMN_DEFAULT_VALUE = 5;

	private HashSet<int> defaultColumns;

	private int msgVersion;

	private List<FldrIndexInfo> indexInfos;

	public FLDR_SET2(B access, SetEnvInfo setTableInfo, HashSet<int> defaultColumns, List<FldrIndexInfo> indexInfos)
		: base(access, (short)111)
	{
		setIdentity = setTableInfo.getSetIdentity();
		sorted = setTableInfo.getSorted();
		bdtaSize = setTableInfo.getBdtaSize();
		indexOption = setTableInfo.getIndexOption();
		noMpp = setTableInfo.getNoMpp();
		charset = setTableInfo.getCharset();
		ignoreConflict = setTableInfo.getIgnoreConflict();
		setId = setTableInfo.getSetId();
		bldrNumber = setTableInfo.getBldrNumber();
		schemaName = setTableInfo.getSchemaName();
		tableName = setTableInfo.getTableName();
		flushFlag = setTableInfo.getFlushFlag();
		parallelFlag = setTableInfo.getParallelFlag();
		sequenceNumInfoList = setTableInfo.getSequenceNumInfos();
		if (sequenceNumInfoList != null && sequenceNumInfoList.Count > 0)
		{
			sequenceCount = (short)sequenceNumInfoList.Count;
		}
		this.defaultColumns = defaultColumns;
		msgVersion = setTableInfo.getMsgVersion();
		this.indexInfos = indexInfos;
	}

	protected override void afterEncode()
	{
		access.__t02_field_04000AB9.B(0, (command != null) ? command.Statement.g() : 0);
		access.__t02_field_04000AB9.A(4, cmd);
		access.__t02_field_04000AB9.B(6, access.__t02_field_04000AB9.g() - 64);
		access.__t02_field_04000AB9.B(20, setIdentity);
		access.__t02_field_04000AB9.B(24, 1);
		access.__t02_field_04000AB9.B(28, sorted);
		access.__t02_field_04000AB9.B(32, bdtaSize);
		access.__t02_field_04000AB9.B(36, indexOption);
		access.__t02_field_04000AB9.B(40, noMpp);
		access.__t02_field_04000AB9.B(44, charset);
		access.__t02_field_04000AB9.B(48, lobFromMsg);
		access.__t02_field_04000AB9.B(52, ignoreConflict);
		access.__t02_field_04000AB9.B(56, 0);
		access.__t02_field_04000AB9.A(60, setId);
		access.__t02_field_04000AB9.A(62, bldrNumber);
	}

	protected override void doEncode()
	{
		access.__t02_field_04000AB9.a(schemaName, access.a().ServerEncoding);
		access.__t02_field_04000AB9.a(tableName, access.a().ServerEncoding);
		access.__t02_field_04000AB9.a(lobDirectoryName, access.a().ServerEncoding);
		access.__t02_field_04000AB9.A(flushFlag);
		access.__t02_field_04000AB9.A(parallelFlag);
		access.__t02_field_04000AB9.A(sequenceCount);
		if (sequenceCount > 0)
		{
			foreach (SequenceNumInfo sequenceNumInfo in sequenceNumInfoList)
			{
				access.__t02_field_04000AB9.A(sequenceNumInfo.getColumnId());
				access.__t02_field_04000AB9.H(sequenceNumInfo.getSequenceId());
			}
		}
		if (msgVersion >= 1)
		{
			access.__t02_field_04000AB9.a((long)indexInfos.Count);
			foreach (FldrIndexInfo indexInfo in indexInfos)
			{
				access.__t02_field_04000AB9.H(indexInfo.getIndexId());
				access.__t02_field_04000AB9.H(indexInfo.getNth());
				access.__t02_field_04000AB9.A(indexInfo.getValidFlag());
			}
		}
		if (msgVersion >= 2 && defaultColumns != null && defaultColumns.Count > 0)
		{
			processColumnFunc();
		}
	}

	private void processColumnFunc()
	{
		access.__t02_field_04000AB9.a((long)(6 + defaultColumns.Count * 4));
		access.__t02_field_04000AB9.h(defaultColumns.Count);
		int[] array = defaultColumns.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			access.__t02_field_04000AB9.h(array[i]);
			access.__t02_field_04000AB9.h(5);
		}
	}

	protected override void beforeDecode()
	{
		access.__t02_field_04000AB9.G(64);
		sqlCode = access.__t02_field_04000AB9.d(10);
		if (sqlCode < 0)
		{
			decodeFlag = true;
		}
	}

	protected override string doDecode()
	{
		if (!decodeFlag)
		{
			return "SUCCESS";
		}
		int num = access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.A(num, false, true);
		int num2 = access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.A(num2, access.a().ServerEncoding);
		int num3 = access.__t02_field_04000AB9.d();
		access.__t02_field_04000AB9.A(num3, access.a().ServerEncoding);
		int num4 = access.__t02_field_04000AB9.d();
		if (num4 > 0)
		{
			return access.__t02_field_04000AB9.A(num4, access.a().ServerEncoding);
		}
		return null;
	}
}
