using System;
using System.IO;
using System.Threading;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;

namespace W.Dm;

internal class GET_LOB_DATA : MSG<Data>
{
	// Fixed encoded-byte client policy; opaque request units do not establish a charset expansion ratio.
	internal const int TextPayloadBudget = 4 * DmConnectionSettings.DefaultLobChunkSize;
	internal const int MaxSuccessBodyOverhead = 19 + 4 + 4;
	internal override int MaxResponseBodyLength => (int)Math.Min(DmFrameReader.MaxFrameSize - DmFrameReader.HeaderSize,
		Math.Max((long)TextPayloadBudget, lob.lobFlag == AbstractLob.LOB_FLAG_BYTE ? length : TextPayloadBudget) + MaxSuccessBodyOverhead);

	internal static readonly AsyncLocal<Action<int>> PayloadAllocationObserver = new();

	private const int REQ_GET_LOB_DATA_TYPE = 20;

	private const int REQ_GET_LOB_LONG_FLAG = 21;

	private AbstractLob lob;

	private long offset;

	private int length;

	public GET_LOB_DATA(B access, AbstractLob lob, long offset, int length)
		: base(access, (short)32)
	{
		if (offset < 0 || length < 0) throw new ArgumentOutOfRangeException();
		if (!access.a().LongLobFlag && (offset > int.MaxValue || lob.totalOffset > int.MaxValue || lob.totalOffset < 0))
			throw new NotSupportedException("LOB offset exceeds this protocol version.");
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
		var buffer = access.__t02_field_04000AB9;
		int prefix = access.a().LongLobFlag ? 19 : 15;
		if (buffer.a(false) < prefix) throw new InvalidDataException("Truncated LOB response metadata.");
		byte end = buffer.__t02_method_06000ABD();
		if (end > 1) throw new InvalidDataException("Invalid LOB end marker.");
		long count = buffer.E();
		int file = buffer.C();
		int page = buffer.d();
		long total = access.a().LongLobFlag ? buffer.e() : buffer.d();
		if (count > int.MaxValue || count > DmConnectionSettings.DefaultMaxMessageSize || count > buffer.a(false))
			throw new InvalidDataException("LOB payload length exceeds the response bounds.");
		if (lob.lobFlag == AbstractLob.LOB_FLAG_CHAR && count > TextPayloadBudget)
			throw new InvalidDataException("Text LOB payload exceeds the fixed encoded-byte client budget.");
		// Binary request units are bytes. Text units are opaque and have no guessed multiplier.
		if (lob.lobFlag == AbstractLob.LOB_FLAG_BYTE && count > length)
			throw new InvalidDataException("Binary LOB response exceeds the requested bytes.");
		int trailing = buffer.a(false) - checked((int)count);
		if (trailing != 0 && trailing != 4) throw new InvalidDataException("Invalid LOB response suffix.");
		if (total < 0) throw new InvalidDataException("Negative LOB locator offset.");
		PayloadAllocationObserver.Value?.Invoke(checked((int)count));
		byte[] value = buffer.F(checked((int)count));
		long units = trailing == 4 ? buffer.E() : -1;
		lob.readOver = end == 1;
		lob.curFileId = file;
		lob.curPageNo = page;
		lob.totalOffset = total;
		return new Data(units, value);
	}
}
