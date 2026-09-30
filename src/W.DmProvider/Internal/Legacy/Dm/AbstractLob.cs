using System;
using W.Dm.Internal.Sessions;

namespace W.Dm;

public class AbstractLob
{
	public const byte LOB_FLAG_BYTE = 0;

	public const byte LOB_FLAG_CHAR = 1;

	public const byte STORAGE_IN_ROW = 1;

	public const byte STORAGE_OUT_ROW = 2;

	public const byte STORAGE_LONG_ROW = 4;

	public const int NBLOB_HEAD_SIZE_INROW = 13;

	public const int NBLOB_HEAD_SIZE_OUTROW = 21;

	public const int NBLOB_HEAD_SIZE_EX = 43;

	public const int NBLOB_HEAD_SIZE_EX_ROWID_12B = 47;

	public long id = -1L;

	public int storageType;

	public short groupId = -1;

	public short fileId = -1;

	public int pageNo = -1;

	public int tabId;

	public short colId;

	public byte[] rowId;

	public int curFileId;

	public int curPageNo;

	public int curOffset;

	public long totalOffset;

	public bool readOver;

	internal DmConnInstance ConnInstance;
	private DmExecutionLease executionLease;

	internal void AttachExecutionLease(DmExecutionLease lease)
	{
		if (lease == null || !ReferenceEquals(lease.Session, ConnInstance?.Session))
			throw new InvalidOperationException("LOB session does not match its reader.");
		executionLease = lease;
	}

	internal DmInvocation BeginPublicOperation()
	{
		if (local) return null;
		if (executionLease != null) return executionLease.BeginInvocation();
		// A freshly decoded LOB can be consumed by the command that is still decoding it.
		if (DmInvocation.Current?.Lease.Session == ConnInstance?.Session) return null;
		throw new InvalidOperationException("LOB has no active reader lease.");
	}

	internal DmInvocation BeginInternalOperation()
	{
		if (local) return null;
		if (executionLease != null)
		{
			if (DmInvocation.Current?.Lease == executionLease) return null;
			return executionLease.BeginInvocation();
		}
		if (DmInvocation.Current?.Lease.Session == ConnInstance?.Session) return null;
		throw new InvalidOperationException("LOB has no current session owner.");
	}

	public bool local = true;

	public bool updateable = true;

	public byte lobFlag;

	public long bytesLength = -1L;

	public long m_length = -1L;

	public bool fetchAll;

	internal AbstractLob(byte[] value, byte lobFlag, DmConnInstance connInstance, DmField column)
		: this(lobFlag, connInstance)
	{
		this.lobFlag = lobFlag;
		local = false;
		updateable = !column.Readonly;
		tabId = column.GetTableID();
		colId = column.GetColID();
		int num = 0;
		storageType = DmConvertion.GetByte(value, num);
		num++;
		id = DmConvertion.GetLong(value, num);
		num += 8;
		bytesLength = DmConvertion.GetInt(value, num);
		num += 4;
		if (num == value.Length)
		{
			return;
		}
		groupId = DmConvertion.GetShort(value, num);
		num += 2;
		fileId = DmConvertion.GetShort(value, num);
		num += 2;
		pageNo = DmConvertion.GetInt(value, num);
		num += 4;
		curFileId = fileId;
		curPageNo = pageNo;
		curOffset = 0;
		totalOffset = 0L;
		if (num != value.Length)
		{
			tabId = DmConvertion.GetInt(value, num);
			num += 4;
			colId = DmConvertion.GetShort(value, num);
			num += 2;
			if (ConnInstance.ConnProperty.msgVersion < 9)
			{
				rowId = DmConvertion.GetBytes(value, num, 8);
				num += 8;
			}
			else
			{
				rowId = DmConvertion.GetBytes(value, num, 12);
				num += 12;
			}
			if (num != value.Length && storageType == 4)
			{
				bytesLength = DmConvertion.GetLong(value, num);
				num += 8;
			}
		}
	}

	internal AbstractLob(byte lobFlag, DmConnInstance connInstance)
	{
		ConnInstance = connInstance;
		this.lobFlag = lobFlag;
	}

	internal long do_length()
	{
		using var invocation = BeginInternalOperation();
		if (m_length == -1)
		{
			m_length = ConnInstance.GetCsi().A(this);
		}
		return m_length;
	}

	public int getHeadSize()
	{
		if (!ConnInstance.ConnProperty.NewLobFlag)
		{
			if (storageType != 1)
			{
				return 21;
			}
			return 13;
		}
		if (ConnInstance.ConnProperty.msgVersion < 9)
		{
			return 43;
		}
		return 47;
	}
}
