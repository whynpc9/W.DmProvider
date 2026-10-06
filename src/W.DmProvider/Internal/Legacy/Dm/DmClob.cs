using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Types;
using W.Dm.Internal.Lobs;
using W.Dm.util;

namespace W.Dm;

public class DmClob : AbstractLob
{
	public string data = "";

	public string serverEncoding;
	private AbstractLob readTemplate;

	private void RebaseReadTemplate(bool knownLength)
	{
		if (!knownLength) m_length = -1;
		bytesLength = knownLength ? m_length : -1;
		readTemplate = SnapshotForRead();
		readTemplate.curFileId = readTemplate.fileId;
		readTemplate.curPageNo = readTemplate.pageNo;
		readTemplate.totalOffset = 0;
		readTemplate.curOffset = 0;
		readTemplate.readOver = false;
	}

	internal DmClob(byte[] value, DmConnInstance connInstance, DmField column, bool fetchAll)
		: base(value, 1, connInstance, column)
	{
		serverEncoding = connInstance.ConnProperty.ServerEncoding;
		readTemplate = SnapshotForRead();
		if (storageType == 1)
		{
			int headSize = getHeadSize();
			// Inline lengths are encoded bytes. Counting decoded chars does not allocate
			// a string and avoids rejecting valid multibyte text by its wire size.
			if (bytesLength < 0 || bytesLength > value.LongLength - headSize)
				DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			int byteCount = checked((int)bytesLength);
			var encoding = DmTextCodec.CreateStrictEncoding(serverEncoding);
			DmLobMaterialization.Characters(encoding.GetCharCount(value, headSize, byteCount));
			data = DmTextCodec.DecodeStrict(value, headSize, byteCount, serverEncoding);
			m_length = data.length();
		}
		else if (fetchAll)
		{
			LoadAllDataOwned();
		}
	}

	internal DmClob(string data, DmConnInstance connInstance)
		: base(1, connInstance)
	{
		serverEncoding = connInstance.ConnProperty.ServerEncoding;
		this.data = data;
		m_length = this.data.length();
	}

	internal static DmClob newInstance(byte[] data, DmConnInstance conn, DmField column, bool fetchAll)
	{
		return new DmClob(data, conn, column, fetchAll);
	}

	public static DmClob newInstance(string data, DmConnection connection)
	{
		return new DmClob(data, connection.m_ConnInst);
	}

	public string getSubString(long pos, int len)
	{
		using var invocation = BeginPublicOperation();
		return GetSubStringOwned(pos + 1, len);
	}

	public string do_getSubString(long pos, int len)
	{
		using var invocation = BeginPublicOperation();
		return GetSubStringOwned(pos, len);
	}

	private DmLobReadCursor NewReadCursor()
	{
		ValidateReadOwner();
		return DmLobReadCursor.Create(readTemplate ?? SnapshotForRead(), default, false, ReadLease,
			ValidateReadOwner, true, serverEncoding);
	}

	private string GetSubStringOwned(long pos, int len)
	{
		using var invocation = BeginInternalOperation();
		if (pos < 1 || len < 0) throw new ArgumentOutOfRangeException();
		pos--;
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{
			if (pos > data.Length) throw new ArgumentOutOfRangeException(nameof(pos));
			int returned = DmLobMaterialization.Characters(Math.Min((long)len, data.Length - pos));
			return data.Substring(checked((int)pos), returned);
		}
		if (pos == 0 && len == 0) return "";
		using var cursor = NewReadCursor();
		char[] chunk = new char[8192];
		while (cursor.Position < pos)
			if (cursor.ReadChars(chunk.AsSpan(0, (int)Math.Min(chunk.Length, pos - cursor.Position))) == 0)
				throw new ArgumentOutOfRangeException(nameof(pos));
		if (len == 0) return "";
		var result = new StringBuilder();
		while (result.Length < len)
		{
			int count = cursor.ReadChars(chunk.AsSpan(0, Math.Min(chunk.Length, len - result.Length)));
			if (count == 0) break;
			DmLobMaterialization.Characters(checked((long)result.Length + count));
			result.Append(chunk, 0, count);
		}
		return result.ToString();
	}

	internal string GetSubStringUnderOwner(long pos, int len) => GetSubStringOwned(checked(pos + 1), len);

	internal Task<string> GetSubStringUnderOwnerAsync(long pos, int len, CancellationToken cancellationToken) =>
		GetSubStringOwnedAsync(checked(pos + 1), len, cancellationToken);

	private async Task<string> GetSubStringOwnedAsync(long pos, int len, CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (pos < 1 || len < 0) throw new ArgumentOutOfRangeException();
		pos--;
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{
			if (pos > data.Length) throw new ArgumentOutOfRangeException(nameof(pos));
			int returned = DmLobMaterialization.Characters(Math.Min((long)len, data.Length - pos));
			return data.Substring(checked((int)pos), returned);
		}
		if (pos == 0 && len == 0) return "";
		using var cursor = NewReadCursor();
		char[] chunk = new char[8192];
		while (cursor.Position < pos)
			if (await cursor.ReadCharsAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, pos - cursor.Position)), cancellationToken).ConfigureAwait(false) == 0)
				throw new ArgumentOutOfRangeException(nameof(pos));
		if (len == 0) return "";
		var result = new StringBuilder();
		while (result.Length < len)
		{
			int count = await cursor.ReadCharsAsync(chunk.AsMemory(0, Math.Min(chunk.Length, len - result.Length)), cancellationToken).ConfigureAwait(false);
			if (count == 0) break;
			DmLobMaterialization.Characters(checked((long)result.Length + count));
			result.Append(chunk, 0, count);
		}
		return result.ToString();
	}

	internal string MaterializeStringUnderOwner()
	{
		using var invocation = BeginInternalOperation();
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{ DmLobMaterialization.Characters(data.Length); return data; }
		// Remote locator lengths are opaque wire units; bound the decoded UTF-16 below.
		using var cursor = NewReadCursor();
		cursor.SetKnownWireLength(do_length());
		char[] chunk = new char[8192];
		var result = new StringBuilder();
		while (true)
		{
			int count = cursor.ReadChars(chunk);
			if (count == 0) break;
			DmLobMaterialization.Characters(checked((long)result.Length + count));
			result.Append(chunk, 0, count);
		}
		return result.ToString();
	}

	internal async Task<string> MaterializeStringUnderOwnerAsync(CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{ DmLobMaterialization.Characters(data.Length); return data; }
		// Remote locator lengths are opaque wire units; bound the decoded UTF-16 below.
		using var cursor = NewReadCursor();
		cursor.SetKnownWireLength(await do_lengthAsync(cancellationToken).ConfigureAwait(false));
		char[] chunk = new char[8192];
		var result = new StringBuilder();
		while (true)
		{
			int count = await cursor.ReadCharsAsync(chunk, cancellationToken).ConfigureAwait(false);
			if (count == 0) break;
			DmLobMaterialization.Characters(checked((long)result.Length + count));
			result.Append(chunk, 0, count);
		}
		return result.ToString();
	}

	public int SetString(long pos, string str)
	{
		using var invocation = BeginPublicOperation();
		if (str == null)
		{
			return SetStringOwned(pos + 1, "", 0, 0);
		}
		return SetStringOwned(pos + 1, str, 0, str.length());
	}

	public int SetString(long pos, string str, int offset, int len)
	{
		using var invocation = BeginPublicOperation();
		return SetStringOwned(pos + 1, str, offset, len);
	}

	public int do_setString(long pos, string str, int offset, int len)
	{
		using var invocation = BeginPublicOperation();
		return SetStringOwned(pos, str, offset, len);
	}

	private int SetStringOwned(long pos, string str, int offset, int len)
	{
		using var invocation = BeginInternalOperation();
		if (pos < 1 || offset < 0 || len < 0 || offset + len > str.length())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		if (!updateable)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_UPDATE_READONLY_CURSOR);
		}
		str = str.Substring(offset, len);
		pos--;
		int num = 0;
		if (local || fetchAll)
		{
			if (pos > do_length())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
			}
			setLocalData((int)pos, str);
			return str.length();
		}
		int result = ConnInstance.GetCsi().A(this, pos, str, serverEncoding);
		if (storageType != STORAGE_IN_ROW) RebaseReadTemplate(knownLength: false);
		if (storageType == 1)
		{
			setLocalData((int)pos, str);
		}
		return result;
	}

	public void Truncate(long len)
	{
		using var invocation = BeginPublicOperation();
		TruncateOwned(len);
	}

	public void do_truncate(long len)
	{
		using var invocation = BeginPublicOperation();
		TruncateOwned(len);
	}

	private void TruncateOwned(long len)
	{
		using var invocation = BeginInternalOperation();
		if (len < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		if (!updateable)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_UPDATE_READONLY_CURSOR);
		}
		if (local || fetchAll)
		{
			if (len <= do_length())
			{
				data = data.substring(0, (int)len);
				m_length = data.length();
			}
		}
		else
		{
			m_length = ConnInstance.GetCsi().A(this, (int)len);
			RebaseReadTemplate(knownLength: true);
			if (storageType == 1)
			{
				data = data.Substring(0, (int)m_length);
			}
		}
	}

	public void loadAllData()
	{
		using var invocation = BeginPublicOperation();
		LoadAllDataOwned();
	}

	private void LoadAllDataOwned()
	{
		using var invocation = BeginInternalOperation();
		if (!local && storageType != 1 && !fetchAll)
		{
			data = MaterializeStringUnderOwner();
			m_length = data.length();
			fetchAll = true;
		}
	}

	internal async Task LoadAllDataUnderOwnerAsync(CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (!local && storageType != STORAGE_IN_ROW && !fetchAll)
		{
			data = await MaterializeStringUnderOwnerAsync(cancellationToken).ConfigureAwait(false);
			m_length = data.length();
			fetchAll = true;
		}
	}

	private void setLocalData(int pos, string str)
	{
		if (pos + str.length() >= m_length)
		{
			data = data.substring(0, pos) + str;
		}
		else
		{
			data = data.substring(0, pos) + str + data.substring(pos + str.length(), data.length());
		}
		m_length = data.length();
	}

	public byte[] GetBytes(long pos, int len)
	{
		using var invocation = BeginPublicOperation();
		return EncodeBounded(GetSubStringOwned(checked(pos + 1), len));
	}

	internal byte[] MaterializeBytesUnderOwner() => MaterializeBytesUnderOwner(serverEncoding);

	internal byte[] MaterializeBytesUnderOwner(string targetEncoding) => EncodeBounded(MaterializeStringUnderOwner(), targetEncoding);

	internal async Task<byte[]> MaterializeBytesUnderOwnerAsync(CancellationToken cancellationToken) =>
		EncodeBounded(await MaterializeStringUnderOwnerAsync(cancellationToken).ConfigureAwait(false));

	private byte[] EncodeBounded(string text) => EncodeBounded(text, serverEncoding);

	private static byte[] EncodeBounded(string text, string targetEncoding)
	{
		var encoding = DmTextCodec.CreateStrictEncoding(targetEncoding);
		DmLobMaterialization.Bytes(encoding.GetByteCount(text));
		return encoding.GetBytes(text);
	}

	internal Stream GetStream() => new MemoryStream(MaterializeBytesUnderOwner(), writable: false);

	public string GetString(long pos, int length)
	{
		using var invocation = BeginPublicOperation();
		return GetSubStringOwned(pos + 1, length);
	}

	public long length()
	{
		using var invocation = BeginPublicOperation();
		return do_length();
	}
}
