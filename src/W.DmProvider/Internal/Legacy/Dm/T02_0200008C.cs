using W.Dm.Internal.Legacy.A;

namespace W.Dm;

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
		access.__t02_field_04000AB9.A(lob.lobFlag);
		access.__t02_field_04000AB9.A(lob.id);
		access.__t02_field_04000AB9.h(lob.groupId);
		access.__t02_field_04000AB9.h(lob.fileId);
		access.__t02_field_04000AB9.H(lob.pageNo);
		access.__t02_field_04000AB9.H(lob.tabId);
		access.__t02_field_04000AB9.h(lob.colId);
		access.__t02_field_04000AB9.A(lob.rowId);
		if (access.a().LongLobFlag)
		{
			access.__t02_field_04000AB9.A(tlen);
			return;
		}
		access.__t02_field_04000AB9.a(tlen);
		access.__t02_field_04000AB9.h(-1);
		access.__t02_field_04000AB9.h(-1);
		access.__t02_field_04000AB9.H(-1);
	}

	protected override long doDecode()
	{
		long num = (access.a().LongLobFlag ? access.__t02_field_04000AB9.e() : access.__t02_field_04000AB9.E());
		lob.m_length = num;
		lob.id = access.__t02_field_04000AB9.e();
		lob.curFileId = lob.fileId;
		lob.curPageNo = lob.pageNo;
		lob.curOffset = 0;
		lob.totalOffset = 0L;
		return num;
	}
}
