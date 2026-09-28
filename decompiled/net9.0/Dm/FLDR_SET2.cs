using System.Collections.Generic;
using System.Linq;
using A;

namespace Dm;

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
		access.A.B(0, (command != null) ? command.Statement.g() : 0);
		access.A.A(4, cmd);
		access.A.B(6, access.A.g() - 64);
		access.A.B(20, setIdentity);
		access.A.B(24, 1);
		access.A.B(28, sorted);
		access.A.B(32, bdtaSize);
		access.A.B(36, indexOption);
		access.A.B(40, noMpp);
		access.A.B(44, charset);
		access.A.B(48, lobFromMsg);
		access.A.B(52, ignoreConflict);
		access.A.B(56, 0);
		access.A.A(60, setId);
		access.A.A(62, bldrNumber);
	}

	protected override void doEncode()
	{
		access.A.a(schemaName, access.a().ServerEncoding);
		access.A.a(tableName, access.a().ServerEncoding);
		access.A.a(lobDirectoryName, access.a().ServerEncoding);
		access.A.A(flushFlag);
		access.A.A(parallelFlag);
		access.A.A(sequenceCount);
		if (sequenceCount > 0)
		{
			foreach (SequenceNumInfo sequenceNumInfo in sequenceNumInfoList)
			{
				access.A.A(sequenceNumInfo.getColumnId());
				access.A.H(sequenceNumInfo.getSequenceId());
			}
		}
		if (msgVersion >= 1)
		{
			access.A.a((long)indexInfos.Count);
			foreach (FldrIndexInfo indexInfo in indexInfos)
			{
				access.A.H(indexInfo.getIndexId());
				access.A.H(indexInfo.getNth());
				access.A.A(indexInfo.getValidFlag());
			}
		}
		if (msgVersion >= 2 && defaultColumns != null && defaultColumns.Count > 0)
		{
			processColumnFunc();
		}
	}

	private void processColumnFunc()
	{
		access.A.a((long)(6 + defaultColumns.Count * 4));
		access.A.h(defaultColumns.Count);
		int[] array = defaultColumns.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			access.A.h(array[i]);
			access.A.h(5);
		}
	}

	protected override void beforeDecode()
	{
		access.A.G(64);
		sqlCode = access.A.d(10);
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
		int num = access.A.d();
		access.A.A(num, false, true);
		int num2 = access.A.d();
		access.A.A(num2, access.a().ServerEncoding);
		int num3 = access.A.d();
		access.A.A(num3, access.a().ServerEncoding);
		int num4 = access.A.d();
		if (num4 > 0)
		{
			return access.A.A(num4, access.a().ServerEncoding);
		}
		return null;
	}
}
