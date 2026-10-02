using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Types;
using W.Dm.util;

namespace W.Dm;

public class DmClob : AbstractLob
{
	public string data = "";

	public string serverEncoding;

	internal DmClob(byte[] value, DmConnInstance connInstance, DmField column, bool fetchAll)
		: base(value, 1, connInstance, column)
	{
		serverEncoding = connInstance.ConnProperty.ServerEncoding;
		if (storageType == 1)
		{
			int headSize = getHeadSize();
			// Inline lengths are encoded bytes. Counting decoded chars does not allocate
			// a string and avoids rejecting valid multibyte text by its wire size.
			if (bytesLength < 0 || bytesLength > value.LongLength - headSize)
				DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			int byteCount = checked((int)bytesLength);
			var encoding = Encoding.GetEncoding(serverEncoding);
			DmLobMaterialization.Characters(encoding.GetCharCount(value, headSize, byteCount));
			data = ByteUtil.getString(value, headSize, byteCount, serverEncoding);
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

	private string GetSubStringOwned(long pos, int len)
	{
		using var invocation = BeginInternalOperation();
		if (pos < 1 || len < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		pos--;
		long num = do_length() - pos;
		if (num < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		len = DmLobMaterialization.Characters(Math.Min((long)len, num));
		if (local || storageType == 1 || fetchAll)
		{
			if (pos > do_length())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
			}
			int offset = checked((int)pos);
			return data.substring(offset, checked(offset + len));
		}
		// The server counts character positions (Data.len); supplementary characters
		// can occupy two CLR chars. Check decoded UTF-16 size before each allocation.
		var builder = new StringBuilder();
		var encoding = Encoding.GetEncoding(serverEncoding);
		long consumed = 0;
		while (consumed < len)
		{
			int chunkLimit = Math.Min(DmConnectionSettings.DefaultLobChunkSize, ConnInstance.ConnProperty.MaxLobDataLenPerMsg);
			if (chunkLimit <= 0) throw new InvalidOperationException("LOB chunk limit must be positive.");
			int requested = (int)Math.Min(len - consumed, chunkLimit);
			Data chunk = ConnInstance.GetCsi().A((AbstractLob)this, checked(pos + consumed), requested);
			if (chunk.value == null || chunk.value.Length == 0) break;
			int chars = encoding.GetCharCount(chunk.value);
			DmLobMaterialization.Characters(checked((long)builder.Length + chars));
			string decoded = encoding.GetString(chunk.value);
			builder.Append(decoded);
			long advanced = chunk.len == -1 ? decoded.Length : chunk.len;
			if (advanced <= 0) throw new InvalidOperationException("LOB read did not advance.");
			consumed = checked(consumed + advanced);
			if (readOver) break;
		}
		return builder.ToString();
	}

	internal string GetSubStringUnderOwner(long pos, int len) => GetSubStringOwned(checked(pos + 1), len);

	internal Task<string> GetSubStringUnderOwnerAsync(long pos, int len, CancellationToken cancellationToken) =>
		GetSubStringOwnedAsync(checked(pos + 1), len, cancellationToken);

	private async Task<string> GetSubStringOwnedAsync(long pos, int len, CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (pos < 1 || len < 0)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		pos--;
		long remaining = await do_lengthAsync(cancellationToken).ConfigureAwait(false) - pos;
		if (remaining < 0)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		len = DmLobMaterialization.Characters(Math.Min((long)len, remaining));
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{
			int offset = checked((int)pos);
			return data.substring(offset, checked(offset + len));
		}
		// The locator and server positions count encoded/server units; guard the
		// returned UTF-16 payload separately, including supplementary characters.
		var builder = new StringBuilder();
		var encoding = Encoding.GetEncoding(serverEncoding);
		long consumed = 0;
		while (consumed < len)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int chunkLimit = Math.Min(DmConnectionSettings.DefaultLobChunkSize, ConnInstance.ConnProperty.MaxLobDataLenPerMsg);
			if (chunkLimit <= 0) throw new InvalidOperationException("LOB chunk limit must be positive.");
			int requested = (int)Math.Min(len - consumed, chunkLimit);
			Data chunk = await ConnInstance.GetCsi().ReadLobAsync((AbstractLob)this, checked(pos + consumed), requested, cancellationToken).ConfigureAwait(false);
			if (chunk.value == null || chunk.value.Length == 0) break;
			int chars = encoding.GetCharCount(chunk.value);
			DmLobMaterialization.Characters(checked((long)builder.Length + chars));
			string decoded = encoding.GetString(chunk.value);
			builder.Append(decoded);
			long advanced = chunk.len == -1 ? decoded.Length : chunk.len;
			if (advanced <= 0) throw new InvalidOperationException("LOB read did not advance.");
			consumed = checked(consumed + advanced);
			if (readOver) break;
		}
		return builder.ToString();
	}

	internal string MaterializeStringUnderOwner()
	{
		// A locator may expose encoded bytes while GET_LOB_LEN uses server character
		// units. Reject an oversized known locator before any length query or read.
		if (!local && storageType != 1 && bytesLength >= 0)
			DmLobMaterialization.Characters(bytesLength);
		int length = DmLobMaterialization.Characters(do_length());
		string result = GetSubStringOwned(1L, length);
		DmLobMaterialization.Characters(result.Length);
		return result;
	}

	internal async Task<string> MaterializeStringUnderOwnerAsync(CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (!local && storageType != STORAGE_IN_ROW && bytesLength >= 0)
			DmLobMaterialization.Characters(bytesLength);
		int length = DmLobMaterialization.Characters(await do_lengthAsync(cancellationToken).ConfigureAwait(false));
		string result = await GetSubStringOwnedAsync(1L, length, cancellationToken).ConfigureAwait(false);
		DmLobMaterialization.Characters(result.Length);
		return result;
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

	internal byte[] MaterializeBytesUnderOwner() => EncodeBounded(MaterializeStringUnderOwner());

	internal async Task<byte[]> MaterializeBytesUnderOwnerAsync(CancellationToken cancellationToken) =>
		EncodeBounded(await MaterializeStringUnderOwnerAsync(cancellationToken).ConfigureAwait(false));

	private byte[] EncodeBounded(string text)
	{
		var encoding = Encoding.GetEncoding(serverEncoding);
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
