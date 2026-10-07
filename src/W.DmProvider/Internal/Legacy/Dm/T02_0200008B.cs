using System.IO;
using W.Dm.Internal.Legacy.A;

namespace W.Dm;

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
		access.__t02_field_04000AB9.A(lob.lobFlag);
		access.__t02_field_04000AB9.A(lob.id);
		access.__t02_field_04000AB9.h(lob.groupId);
		access.__t02_field_04000AB9.h(lob.fileId);
		access.__t02_field_04000AB9.H(lob.pageNo);
		if (access.a().NewLobFlag)
		{
			access.__t02_field_04000AB9.H(lob.tabId);
			access.__t02_field_04000AB9.h(lob.colId);
			access.__t02_field_04000AB9.A(lob.rowId);
			if (!access.a().LongLobFlag)
			{
				access.__t02_field_04000AB9.h(-1);
				access.__t02_field_04000AB9.h(-1);
				access.__t02_field_04000AB9.H(-1);
			}
		}
	}

	protected override long doDecode()
	{
		var buffer = access.__t02_field_04000AB9;
		if (buffer.a(false) != (access.a().LongLobFlag ? 8 : 4))
			throw new InvalidDataException("Invalid LOB length response.");
		long length = access.a().LongLobFlag ? buffer.e() : buffer.E();
		if (length < 0) throw new InvalidDataException("Negative LOB length.");
		return length;
	}
}
