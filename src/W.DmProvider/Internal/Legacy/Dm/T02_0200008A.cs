using W.Dm.Internal.Legacy.A;

namespace W.Dm;

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
		access.__t02_field_04000AB9.A(21, (byte)1);
		access.__t02_field_04000AB9.A(lob.lobFlag);
		access.__t02_field_04000AB9.H(lob.tabId);
		access.__t02_field_04000AB9.h(lob.colId);
		access.__t02_field_04000AB9.A(lob.id);
		access.__t02_field_04000AB9.h(lob.groupId);
		access.__t02_field_04000AB9.h(lob.fileId);
		access.__t02_field_04000AB9.H(lob.pageNo);
		access.__t02_field_04000AB9.h(lob.curFileId);
		access.__t02_field_04000AB9.H(lob.curPageNo);
		if (access.a().LongLobFlag)
		{
			access.__t02_field_04000AB9.A(lob.totalOffset);
			access.__t02_field_04000AB9.A(offset);
		}
		else
		{
			access.__t02_field_04000AB9.a(lob.totalOffset);
			access.__t02_field_04000AB9.H((int)offset);
		}
		access.__t02_field_04000AB9.H(length);
		if (access.a().NewLobFlag)
		{
			access.__t02_field_04000AB9.A(lob.rowId);
			if (!access.a().LongLobFlag)
			{
				access.__t02_field_04000AB9.h(-1);
				access.__t02_field_04000AB9.h(-1);
				access.__t02_field_04000AB9.H(-1);
			}
		}
	}

	protected override Data doDecode()
	{
		lob.readOver = access.__t02_field_04000AB9.__t02_method_06000ABD() == 1;
		long num = access.__t02_field_04000AB9.E();
		lob.curFileId = access.__t02_field_04000AB9.C();
		lob.curPageNo = access.__t02_field_04000AB9.d();
		lob.totalOffset = (access.a().LongLobFlag ? access.__t02_field_04000AB9.e() : access.__t02_field_04000AB9.d());
		if (num <= 0)
		{
			return new Data(0L, new byte[0]);
		}
		byte[] value = access.__t02_field_04000AB9.F((int)num);
		long len = -1L;
		if (access.__t02_field_04000AB9.a(false) > 0)
		{
			len = access.__t02_field_04000AB9.E();
		}
		return new Data(len, value);
	}
}
