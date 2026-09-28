using A;

namespace Dm;

internal class GET_LOB_LEN : MSG<long>
{
	private AbstractLob lob;

	public GET_LOB_LEN(B access, AbstractLob lob)
		: base(access, (short)29)
	{
		this.lob = lob;
	}

	protected override void doEncode()
	{
		access.A.A(lob.lobFlag);
		access.A.A(lob.id);
		access.A.h(lob.groupId);
		access.A.h(lob.fileId);
		access.A.H(lob.pageNo);
		if (access.a().NewLobFlag)
		{
			access.A.H(lob.tabId);
			access.A.h(lob.colId);
			access.A.A(lob.rowId);
			if (!access.a().LongLobFlag)
			{
				access.A.h(-1);
				access.A.h(-1);
				access.A.H(-1);
			}
		}
	}

	protected override long doDecode()
	{
		if (!access.a().LongLobFlag)
		{
			return access.A.E();
		}
		return access.A.e();
	}
}
