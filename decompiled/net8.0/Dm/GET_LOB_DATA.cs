using A;

namespace Dm;

internal class GET_LOB_DATA : MSG<Data>
{
	private const int REQ_GET_LOB_DATA_TYPE = 20;

	private const int REQ_GET_LOB_LONG_FLAG = 21;

	private AbstractLob lob;

	private long offset;

	private int length;

	public GET_LOB_DATA(B access, AbstractLob lob, long offset, int length)
		: base(access, (short)32)
	{
		this.lob = lob;
		this.offset = offset;
		this.length = length;
	}

	protected override void doEncode()
	{
		access.A.A(21, (byte)1);
		access.A.A(lob.lobFlag);
		access.A.H(lob.tabId);
		access.A.h(lob.colId);
		access.A.A(lob.id);
		access.A.h(lob.groupId);
		access.A.h(lob.fileId);
		access.A.H(lob.pageNo);
		access.A.h(lob.curFileId);
		access.A.H(lob.curPageNo);
		if (access.a().LongLobFlag)
		{
			access.A.A(lob.totalOffset);
			access.A.A(offset);
		}
		else
		{
			access.A.a(lob.totalOffset);
			access.A.H((int)offset);
		}
		access.A.H(length);
		if (access.a().NewLobFlag)
		{
			access.A.A(lob.rowId);
			if (!access.a().LongLobFlag)
			{
				access.A.h(-1);
				access.A.h(-1);
				access.A.H(-1);
			}
		}
	}

	protected override Data doDecode()
	{
		lob.readOver = access.A.b() == 1;
		long num = access.A.E();
		lob.curFileId = access.A.C();
		lob.curPageNo = access.A.d();
		lob.totalOffset = (access.a().LongLobFlag ? access.A.e() : access.A.d());
		if (num <= 0)
		{
			return new Data(0L, new byte[0]);
		}
		byte[] value = access.A.F((int)num);
		long len = -1L;
		if (access.A.a(false) > 0)
		{
			len = access.A.E();
		}
		return new Data(len, value);
	}
}
