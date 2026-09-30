using W.Dm.Internal.Legacy.A;

namespace W.Dm;

internal class SET_LOB_DATA : MSG<int>
{
	private AbstractLob lob;

	private byte firstOrLast;

	private long pos;

	private byte[] data;

	private int dataOffset;

	private int length;

	public SET_LOB_DATA(B access, AbstractLob lob, byte firstOrLast, long pos, byte[] data, int dataOffset, int length)
		: base(access, (short)30)
	{
		this.lob = lob;
		this.firstOrLast = firstOrLast;
		this.pos = pos;
		this.data = data;
		this.dataOffset = dataOffset;
		this.length = length;
	}

	protected override void doEncode()
	{
		access.__t02_field_04000AB9.A(lob.lobFlag);
		access.__t02_field_04000AB9.A(firstOrLast);
		access.__t02_field_04000AB9.A(lob.id);
		access.__t02_field_04000AB9.h(lob.groupId);
		access.__t02_field_04000AB9.h(lob.fileId);
		access.__t02_field_04000AB9.H(lob.pageNo);
		access.__t02_field_04000AB9.h(lob.curFileId);
		access.__t02_field_04000AB9.H(lob.curPageNo);
		if (access.a().LongLobFlag)
		{
			access.__t02_field_04000AB9.A(lob.totalOffset);
			access.__t02_field_04000AB9.H(lob.tabId);
			access.__t02_field_04000AB9.h(lob.colId);
			access.__t02_field_04000AB9.A(lob.rowId);
			access.__t02_field_04000AB9.A(pos);
		}
		else
		{
			access.__t02_field_04000AB9.a(lob.totalOffset);
			access.__t02_field_04000AB9.H(lob.tabId);
			access.__t02_field_04000AB9.h(lob.colId);
			access.__t02_field_04000AB9.A(lob.rowId);
			access.__t02_field_04000AB9.a(pos);
		}
		access.__t02_field_04000AB9.H(length);
		access.__t02_field_04000AB9.a(data, dataOffset, length);
		if (!access.a().LongLobFlag)
		{
			access.__t02_field_04000AB9.h(-1);
			access.__t02_field_04000AB9.h(-1);
			access.__t02_field_04000AB9.H(-1);
		}
	}

	protected override int doDecode()
	{
		int result = access.__t02_field_04000AB9.d();
		lob.id = access.__t02_field_04000AB9.e();
		lob.groupId = access.__t02_field_04000AB9.C();
		lob.fileId = access.__t02_field_04000AB9.C();
		lob.pageNo = access.__t02_field_04000AB9.d();
		lob.curFileId = access.__t02_field_04000AB9.C();
		lob.curPageNo = access.__t02_field_04000AB9.d();
		lob.totalOffset = access.__t02_field_04000AB9.E();
		if (lob.groupId == -1)
		{
			lob.storageType = 1;
		}
		else
		{
			lob.storageType = 2;
			lob.m_length = -1L;
		}
		lob.curOffset = 0;
		return result;
	}
}
