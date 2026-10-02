using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.util;
using W.Dm.Internal.Types;

namespace W.Dm;

public class DmBlob : AbstractLob
{
	private byte[] data;

	internal DmBlob(byte[] value, DmConnInstance connInstance, DmField column, bool fetchAll, bool hexPayload = false)
		: base(value, 0, connInstance, column)
	{
		m_length = bytesLength;
		if (hexPayload && m_length != -1) DmLobMaterialization.HexInput(m_length);
		if (storageType == 1)
		{
			int headSize = getHeadSize();
			int length = DmLobMaterialization.Bytes(m_length);
			if (m_length > value.LongLength - headSize)
				DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			data = new byte[length];
			ByteUtil.setBytes(data, 0, value, headSize, data.Length);
		}
		else if (fetchAll)
		{
			LoadAllDataOwned();
		}
	}

	internal DmBlob(byte[] data, DmConnInstance connInstance)
		: base(0, connInstance)
	{
		this.data = data;
		m_length = this.data.Length;
	}

	internal static DmBlob newInstanceFromDB(byte[] value, DmConnInstance connInstance, DmField column, bool fetchAll)
	{
		return new DmBlob(value, connInstance, column, fetchAll);
	}

	public static DmBlob newInstanceOfLocal(byte[] data, DmConnection connection)
	{
		return new DmBlob(data, connection.m_ConnInst);
	}

	public byte[] GetBytes(long pos, int len)
	{
		using var invocation = BeginPublicOperation();
		return do_getBytes(pos + 1, len);
	}

	internal byte[] do_getBytes(long pos, int len)
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
		len = DmLobMaterialization.Bytes(Math.Min((long)len, num));
		if (local || storageType == 1 || fetchAll)
		{
			byte[] array = new byte[len];
			ByteUtil.setBytes(array, 0, data, checked((int)pos), array.Length);
			return array;
		}
		return ConnInstance.GetCsi().A(this, pos, len);
	}

	internal async Task<byte[]> do_getBytesAsync(long pos, int len, CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (pos < 1 || len < 0)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		pos--;
		long remaining = await do_lengthAsync(cancellationToken).ConfigureAwait(false) - pos;
		if (remaining < 0)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		len = DmLobMaterialization.Bytes(Math.Min((long)len, remaining));
		if (local || storageType == STORAGE_IN_ROW || fetchAll)
		{
			byte[] result = new byte[len];
			ByteUtil.setBytes(result, 0, data, checked((int)pos), result.Length);
			return result;
		}
		return await ConnInstance.GetCsi().ReadLobAsync(this, pos, len, cancellationToken).ConfigureAwait(false);
	}

	public int SetBytes(long pos, byte[] bytes)
	{
		using var invocation = BeginPublicOperation();
		if (bytes == null)
		{
			return do_setBytes(pos + 1, new byte[0], 0, 0);
		}
		return do_setBytes(pos + 1, bytes, 0, bytes.Length);
	}

	public int SetBytes(long pos, ref byte[] bytes, int offset, int len)
	{
		using var invocation = BeginPublicOperation();
		return do_setBytes(pos + 1, bytes, offset, len);
	}

	internal int do_setBytes(long pos, byte[] bytes, int offset, int len)
	{
		using var invocation = BeginInternalOperation();
		if (pos < 1 || len < 0 || offset < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		if (!updateable)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_UPDATE_READONLY_CURSOR);
		}
		len = ((offset + len > bytes.Length) ? (bytes.Length - offset) : len);
		pos--;
		int num = 0;
		if (local || fetchAll)
		{
			if (pos > do_length())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
			}
			setLocalData((int)pos, bytes, offset, len);
			return len;
		}
		int num2 = ConnInstance.GetCsi().A(this, pos, bytes, offset, len);
		if (storageType == 1)
		{
			setLocalData((int)pos, bytes, offset, num2);
		}
		return num2;
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
			if (len < data.Length)
			{
				byte[] array = new byte[(int)len];
				ByteUtil.setBytes(array, 0, data, 0, array.Length);
				data = array;
				m_length = array.Length;
			}
		}
		else
		{
			m_length = ConnInstance.GetCsi().A(this, (int)len);
			if (storageType == 1)
			{
				byte[] array2 = new byte[(int)do_length()];
				ByteUtil.setBytes(array2, 0, data, 0, array2.Length);
				data = array2;
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
			data = do_getBytes(1L, DmLobMaterialization.Bytes(do_length()));
			fetchAll = true;
		}
	}

	internal async Task LoadAllDataUnderOwnerAsync(CancellationToken cancellationToken)
	{
		using var invocation = BeginInternalOperation(cancellationToken);
		if (!local && storageType != STORAGE_IN_ROW && !fetchAll)
		{
			// A known locator is rejected before issuing even a length query.
			if (bytesLength >= 0) DmLobMaterialization.Bytes(bytesLength);
			int length = DmLobMaterialization.Bytes(await do_lengthAsync(cancellationToken).ConfigureAwait(false));
			data = await do_getBytesAsync(1L, length, cancellationToken).ConfigureAwait(false);
			fetchAll = true;
		}
	}

	private void setLocalData(int pos, byte[] bytes, int offset, int len)
	{
		if (pos + len > m_length)
		{
			byte[] bytes2 = new byte[pos + len];
			ByteUtil.setBytes(bytes2, 0, data, 0, pos);
			ByteUtil.setBytes(bytes2, pos, bytes, offset, len);
			data = bytes2;
		}
		else
		{
			ByteUtil.setBytes(data, pos, bytes, offset, len);
		}
		m_length = data.Length;
	}

	public Stream GetStream()
	{
		return new DmBLobStream(this);
	}

	public long Length()
	{
		using var invocation = BeginPublicOperation();
		return do_length();
	}
}
