using A;

namespace Dm;

internal class LOB_TRUNCATE : MSG<long>
{
	private long tlen;

	private AbstractLob lob;

	public LOB_TRUNCATE(B access, AbstractLob lob, long tlen)
		: base(access, (short)31)
	{
		this.lob = lob;
		this.tlen = tlen;
	}

	protected override void doEncode()
	{
		access.A.A(lob.lobFlag);
		access.A.A(lob.id);
		access.A.h(lob.groupId);
		access.A.h(lob.fileId);
		access.A.H(lob.pageNo);
		access.A.H(lob.tabId);
		access.A.h(lob.colId);
		access.A.A(lob.rowId);
		if (access.a().LongLobFlag)
		{
			access.A.A(tlen);
			return;
		}
		access.A.a(tlen);
		access.A.h(-1);
		access.A.h(-1);
		access.A.H(-1);
	}

	protected override long doDecode()
	{
		long num = (access.a().LongLobFlag ? access.A.e() : access.A.E());
		lob.m_length = num;
		lob.id = access.A.e();
		lob.curFileId = lob.fileId;
		lob.curPageNo = lob.pageNo;
		lob.curOffset = 0;
		lob.totalOffset = 0L;
		return num;
	}
}
