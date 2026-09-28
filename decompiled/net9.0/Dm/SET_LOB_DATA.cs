using A;

namespace Dm;

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
		access.A.A(lob.lobFlag);
		access.A.A(firstOrLast);
		access.A.A(lob.id);
		access.A.h(lob.groupId);
		access.A.h(lob.fileId);
		access.A.H(lob.pageNo);
		access.A.h(lob.curFileId);
		access.A.H(lob.curPageNo);
		if (access.a().LongLobFlag)
		{
			access.A.A(lob.totalOffset);
			access.A.H(lob.tabId);
			access.A.h(lob.colId);
			access.A.A(lob.rowId);
			access.A.A(pos);
		}
		else
		{
			access.A.a(lob.totalOffset);
			access.A.H(lob.tabId);
			access.A.h(lob.colId);
			access.A.A(lob.rowId);
			access.A.a(pos);
		}
		access.A.H(length);
		access.A.a(data, dataOffset, length);
		if (!access.a().LongLobFlag)
		{
			access.A.h(-1);
			access.A.h(-1);
			access.A.H(-1);
		}
	}

	protected override int doDecode()
	{
		int result = access.A.d();
		lob.id = access.A.e();
		lob.groupId = access.A.C();
		lob.fileId = access.A.C();
		lob.pageNo = access.A.d();
		lob.curFileId = access.A.C();
		lob.curPageNo = access.A.d();
		lob.totalOffset = access.A.E();
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
