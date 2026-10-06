using System;
using System.Collections;
using System.IO;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Legacy.A;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Types;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Transport;

namespace W.Dm;

public class DmDataReader : DbDataReader, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmDataReader";

	private DmConnInstance m_Conn;

	private DmResultSetCache m_RsCache;

	internal global::W.Dm.Internal.Legacy.A.A m_Statement;

	private DmInfo m_DbInfo;

	private DmColumn[] m_ColInfo;

	private DmGetValue m_GetVal;

	internal long m_RowCount;

	private CommandBehavior m_Behavior;

	private int m_is_single_row;

	internal long m_StartRow;

	protected long m_CurrentRow = -1L;

	private readonly DmResultCursor resultCursor = new();

	protected volatile bool m_IsClosed;

	private bool is_SequentialAccess;

	private int m_SequentialSeq = -1;

	private long m_StreamPos;

	private long lobRowVersion;
	private DmLobReadCursor activeLobFlow;
	private int activeLobOrdinal = -1;
	private bool activeLobText;
	private int sequentialLobUnitOrdinal = -1;
	private bool sequentialLobBytes;

	private void InvalidateLobFlows()
	{
		Interlocked.Increment(ref lobRowVersion);
		m_StreamPos = 0;
		sequentialLobUnitOrdinal = -1;
		activeLobFlow?.Dispose();
		activeLobFlow = null;
		activeLobOrdinal = -1;
	}

	private Action CaptureLobOwner(int ordinal = -1, bool? bytes = null)
	{
		long version = Volatile.Read(ref lobRowVersion);
		var lease = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
		var transaction = lease?.Session.ActiveTransaction ?? Volatile.Read(ref commandPlan)?.Transaction;
		return () =>
		{
			if (m_IsClosed || version != Volatile.Read(ref lobRowVersion) || lease == null ||
				!ReferenceEquals(lease, Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease) ||
				!ReferenceEquals(m_Conn?.Session, lease.Session) || (lease.IsDisposed || !lease.Session.IsCurrentExecution(lease.Identity)))
				throw new InvalidOperationException("LOB row or execution owner is stale.");
			if (is_SequentialAccess && ordinal >= 0 && m_SequentialSeq > ordinal)
				throw new InvalidOperationException("Sequential access moved past this LOB field.");
			if (bytes.HasValue) GuardSequentialLobUnit(ordinal, bytes.Value);
			if (transaction != null && (transaction.Outcome != DmTransactionOutcome.Active ||
				!ReferenceEquals(transaction, lease.Session.ActiveTransaction)))
				throw new InvalidOperationException("LOB transaction owner has ended.");
		};
	}

	private DmLobReadCursor CreateLobCursor(int ordinal, bool text, bool lengthScan = false)
	{
		checkClosed(); CheckIndex(ordinal);
		int type = m_ColInfo[ordinal].GetCType();
		if (text ? type is not (0 or 1 or 2 or 19 or 54) : type is not (3 or 12 or 17 or 18 or 54))
			throw new InvalidCastException("Column does not support the requested LOB flow.");
		byte[] value = null;
		GetByteArrayValue(ordinal, ref value, lobPartial: true, lengthScan: lengthScan);
		if (value == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		bool isLob = type is 12 or 19;
		var template = isLob ? new AbstractLob(value, text ? (byte)1 : (byte)0, m_Conn, m_ColInfo[ordinal])
			: new AbstractLob(text ? (byte)1 : (byte)0, m_Conn);
		ReadOnlyMemory<byte> inline = value;
		bool hasInline = !isLob || template.storageType == AbstractLob.STORAGE_IN_ROW;
		if (isLob && hasInline)
		{
			int head = template.getHeadSize();
			if (template.bytesLength < 0 || head > value.Length || template.bytesLength > value.Length - head)
				throw new InvalidDataException("Inline LOB exceeds its result frame.");
			inline = value.AsMemory(head, checked((int)template.bytesLength));
		}
		return DmLobReadCursor.Create(template, inline, hasInline, ReaderLease, CaptureLobOwner(ordinal, bytes: lengthScan ? null : !text), text,
			m_Conn.ConnProperty.ServerEncoding);
	}

	private void RefreshSequentialLobPosition()
	{
		if (is_SequentialAccess && activeLobFlow != null && activeLobOrdinal == m_SequentialSeq)
		{
			m_StreamPos = Math.Max(m_StreamPos, activeLobFlow.Position);
			if (activeLobFlow.Position > 0)
			{ sequentialLobUnitOrdinal = activeLobOrdinal; sequentialLobBytes = !activeLobText; }
		}
	}

	private void GuardSequentialLobUnit(int ordinal, bool bytes)
	{
		RefreshSequentialLobPosition();
		if (is_SequentialAccess && sequentialLobUnitOrdinal == ordinal && sequentialLobBytes != bytes)
			throw new InvalidOperationException("Sequential LOB access cannot switch byte and UTF-16 units.");
	}

	private void GuardSequentialRange(int ordinal, long offset)
	{
		RefreshSequentialLobPosition();
		if (is_SequentialAccess && (m_SequentialSeq > ordinal ||
			(m_SequentialSeq == ordinal && offset < m_StreamPos)))
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
	}

	private DmLobReadCursor ActivateLobCursor(int ordinal, bool text, bool rangeResume = false)
	{
		GuardSequentialLobUnit(ordinal, bytes: !text);
		if (is_SequentialAccess && (m_SequentialSeq > ordinal ||
			(!rangeResume && m_SequentialSeq == ordinal && m_StreamPos > 0)))
			throw new InvalidOperationException("Sequential access already consumed this LOB field.");
		if (activeLobFlow != null && !activeLobFlow.IsDisposed)
			throw new InvalidOperationException("Close the active LOB flow before opening another.");
		activeLobFlow = CreateLobCursor(ordinal, text);
		activeLobOrdinal = ordinal;
		activeLobText = text;
		return activeLobFlow;
	}

	public override Stream GetStream(int ordinal)
	{
		using var invocation = BeginReaderInvocation();
		return new DmLobReadStream(ActivateLobCursor(ordinal, false));
	}

	public override TextReader GetTextReader(int ordinal)
	{
		using var invocation = BeginReaderInvocation();
		return new DmLobTextReader(ActivateLobCursor(ordinal, true));
	}

	private static void ValidateLobRange<T>(long offset, T[] buffer, int bufferOffset, int length)
	{
		if (offset < 0) throw new IndexOutOfRangeException("Field offset must be nonnegative.");
		if (bufferOffset < 0 || length < 0) throw new ArgumentOutOfRangeException();
		if (buffer != null) DmLobReadStream.CheckBuffer(buffer, bufferOffset, length);
		if (offset > long.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(offset));
	}

	private static void ValidateCharRange(long offset, char[] buffer, int bufferOffset, int length)
	{
		if (offset < 0) DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		if (buffer != null && (bufferOffset < 0 || bufferOffset > buffer.Length))
			throw new IndexOutOfRangeException("Buffer index must be a valid index in buffer.");
		if (length < 0 || (buffer != null && length > buffer.Length - bufferOffset))
			throw new ArgumentException("Buffer is not large enough to hold the requested data.");
		if (offset > long.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(offset));
	}

	private DmLobReadCursor RangeCursor(int ordinal, bool text, out bool temporary)
	{
		if (is_SequentialAccess && activeLobFlow != null && !activeLobFlow.IsDisposed && activeLobOrdinal == ordinal)
		{
			if (text != activeLobText) throw new InvalidCastException();
			temporary = false; return activeLobFlow;
		}
		if (is_SequentialAccess)
		{
			if (m_SequentialSeq > ordinal) DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
			activeLobFlow?.Dispose(); activeLobFlow = null;
			temporary = false; return ActivateLobCursor(ordinal, text, rangeResume: true);
		}
		temporary = true; return CreateLobCursor(ordinal, text);
	}


	private ArrayList m_Clobs = new ArrayList();

	private DmExecutionLease executionLease;
	private DmCommandPlan commandPlan;
	private Action<DmCommandPlan> releaseCommandPlan;
	private bool ownsExecutionLease;
	private bool registeredReader;
	private int closeStarted;
	private readonly object asyncCloseGate = new();
	private Task asyncCloseTask;
	internal static Action<OperationIdentity> AfterInvocationEntered;

	internal void AttachExecutionLease(DmExecutionLease lease, bool ownsLease = true)
	{
		if (lease == null || !ReferenceEquals(lease.Session, m_Conn.Session))
			throw new InvalidOperationException("Reader session does not match its execution lease.");
		if (Interlocked.CompareExchange(ref executionLease, lease, null) != null)
			throw new InvalidOperationException("Reader already owns an execution lease.");
		ownsExecutionLease = ownsLease;
		if (ownsLease && lease.Purpose == DmOperationPurpose.Reader)
		{
			try { lease.Session.RegisterReader(lease, this); registeredReader = true; }
			catch
			{
				Interlocked.CompareExchange(ref executionLease, null, lease);
				ownsExecutionLease = false;
				throw;
			}
		}
	}

	internal void AttachCommandPlan(DmCommandPlan plan, Action<DmCommandPlan> release)
	{
		if (plan == null || release == null) throw new ArgumentNullException();
		if (Interlocked.CompareExchange(ref commandPlan, plan, null) != null)
			throw new InvalidOperationException("Reader already owns a command plan.");
		releaseCommandPlan = release;
	}

	private DmExecutionLease ReaderLease => Volatile.Read(ref executionLease) ??
		throw new InvalidOperationException("Reader has no execution lease.");

	private static T CompleteReaderInvocation<T>(DmInvocation invocation, T value)
	{
		invocation.ThrowIfTerminated();
		invocation.Complete();
		return value;
	}

	// A getter that reached the wire and returned successfully completes its
	// invocation; cached no-I/O getters deliberately record no span. Remote LOB
	// reads reuse the ambient reader invocation, so nothing else completes it.
	private static T CompleteReaderInvocationIfSent<T>(DmInvocation invocation, T value)
	{
		if (invocation != null && invocation.SendAttempted) return CompleteReaderInvocation(invocation, value);
		return value;
	}

	private DmInvocation BeginReaderInvocation()
	{
		if (m_IsClosed) throw new InvalidOperationException("Reader is closed.");
		return ReaderLease.BeginInvocation();
	}

	private DmInvocation BeginInternalInvocation()
	{
		var lease = Volatile.Read(ref executionLease);
		if (DmInvocation.Current?.Lease == lease && lease != null) return null;
		// ExecuteScalar owns a private reader for the duration of its command invocation.
		if (lease == null && DmInvocation.Current?.Lease.Session == m_Conn?.Session) return null;
		return BeginReaderInvocation();
	}

	public bool bdta;

	public long ID
	{
		get
		{
			if (id < 0)
			{
				id = Interlocked.Increment(ref idGenerator);
			}
			return id;
		}
	}

	public BaseFilter filterHead
	{
		get => null;
		set
		{
			if (value != null)
				throw new NotSupportedException("Legacy filter injection is unsupported.");
		}
	}

	public LogInfo LogInfo { get; set; }

	public RWInfo RWInfo { get; set; }

	public RecoverInfo RecoverInfo { get; set; }

	internal long RowCount => m_RowCount;

	internal bool FetchedAll => m_RowCount == long.MaxValue;

	internal int do_Depth => 0;

	internal bool do_HasRows
	{
		get
		{
			if (m_RowCount > 0)
			{
				return true;
			}
			return false;
		}
	}

	internal int do_VisibleFieldCount => base.VisibleFieldCount;

	internal int do_RecordsAffected
	{
		get
		{
			return resultCursor.HasReadableRowset ? -1 : resultCursor.RecordsAffected;
		}
	}

	internal bool do_IsClosed
	{
		get
		{
			var lease = Volatile.Read(ref executionLease);
			return m_IsClosed || (lease != null && !lease.Session.IsCurrent(lease.Identity.SessionId, lease.Identity.LeaseGeneration));
		}
	}

	internal int do_FieldCount { get { checkClosed(); return m_DbInfo.GetColumnCount(); } }

	public override object this[int number]
	{
		get
		{
			using var invocation = BeginReaderInvocation();
			if (filterHead == null)
			{
				return CompleteReaderInvocationIfSent(invocation, do_this(number));
			}
			return filterHead.getThis(this, number);
		}
	}

	public override object this[string name]
	{
		get
		{
			using var invocation = BeginReaderInvocation();
			if (filterHead == null)
			{
				return CompleteReaderInvocationIfSent(invocation, do_this(name));
			}
			return filterHead.getThis(this, name);
		}
	}

	public override int Depth
	{
		get
		{
			if (filterHead == null)
			{
				return do_Depth;
			}
			return filterHead.getDepth(this);
		}
	}

	public override bool HasRows
	{
		get
		{
			if (filterHead == null)
			{
				return do_HasRows;
			}
			return filterHead.getHasRows(this);
		}
	}

	public override int VisibleFieldCount
	{
		get
		{
			if (filterHead == null)
			{
				return do_VisibleFieldCount;
			}
			return filterHead.getVisibleFieldCount(this);
		}
	}

	public override int RecordsAffected
	{
		get
		{
			if (filterHead == null)
			{
				return do_RecordsAffected;
			}
			return filterHead.getRecordsAffected(this);
		}
	}

	public override bool IsClosed
	{
		get
		{
			if (filterHead == null)
			{
				return do_IsClosed;
			}
			return filterHead.getIsClosed(this);
		}
	}

	public override int FieldCount
	{
		get
		{
			if (filterHead == null)
			{
				return do_FieldCount;
			}
			return filterHead.getFieldCount(this);
		}
	}

	internal DmDataReader(DmResultSetCache cache, DmInfo info, CommandBehavior behavior)
	{
		BaseFilter.CreateFilterChain(this);
		m_RsCache = cache;
		m_DbInfo = info;
		m_ColInfo = info.GetColumnsInfo();
		bdta = info.rsBdta;
		DmColumn[] colInfo = m_ColInfo;
		for (int i = 0; i < colInfo.Length; i++)
		{
			colInfo[i].isBdta = bdta;
		}
		m_RowCount = m_DbInfo.GetRowCount();
		resultCursor.Observe(m_DbInfo);
		m_Statement = m_RsCache.statement;
		m_Conn = m_Statement.G();
		string serverEncoding = m_Conn.ConnProperty.ServerEncoding;
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		bool newLobFlag = m_Conn.ConnProperty.NewLobFlag;
		DmField[] colInfo2 = m_ColInfo;
		m_GetVal = new DmGetValue(serverEncoding, statement, newLobFlag, colInfo2);
		m_Behavior = behavior;
		if ((m_Behavior & CommandBehavior.SequentialAccess) != 0)
		{
			is_SequentialAccess = true;
		}
	}

	internal DmDataReader(DmInfo info, CommandBehavior behavior, global::W.Dm.Internal.Legacy.A.A stmt)
	{
		BaseFilter.CreateFilterChain(this);
		m_RsCache = null;
		m_DbInfo = info;
		m_ColInfo = info.GetColumnsInfo();
		bdta = info.rsBdta;
		if (m_ColInfo != null)
		{
			DmColumn[] colInfo = m_ColInfo;
			for (int i = 0; i < colInfo.Length; i++)
			{
				colInfo[i].isBdta = bdta;
			}
		}
		m_RowCount = m_DbInfo.GetRowCount();
		resultCursor.Observe(m_DbInfo);
		m_Statement = stmt;
		m_Conn = m_Statement.G();
		string serverEncoding = m_Conn.ConnProperty.ServerEncoding;
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		bool newLobFlag = m_Conn.ConnProperty.NewLobFlag;
		DmField[] colInfo2 = m_ColInfo;
		m_GetVal = new DmGetValue(serverEncoding, statement, newLobFlag, colInfo2);
		m_Behavior = behavior;
		if ((m_Behavior & CommandBehavior.SequentialAccess) != 0)
		{
			is_SequentialAccess = true;
		}
	}

	internal object do_this(int number)
	{
		return do_GetValue(number);
	}

	internal object do_this(string name)
	{
		int number = do_GetOrdinal(name);
		return do_this(number);
	}

	internal void do_Close()
	{
		CloseCore(allowOwnedInvocation: true);
	}

	internal void CloseForTransactionCleanup(DmDeadline deadline)
	{
		CloseCore(allowOwnedInvocation: false, cleanupDeadlineOverride: deadline);
	}

	private void CloseCore(bool allowOwnedInvocation, DmDeadline? cleanupDeadlineOverride = null)
	{
		if (Interlocked.CompareExchange(ref closeStarted, 1, 0) != 0) return;
		var lease = Volatile.Read(ref executionLease);
		var active = DmInvocation.Current;
		DmInvocation invocation = null;
		bool ownedInvocation = allowOwnedInvocation && active != null && active.Lease == lease;
		if (lease != null && !ownedInvocation &&
			lease.Session.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
		{
			TimeSpan cleanupTimeout = commandPlan?.CleanupTimeout ?? m_Conn.Conn.Settings.CleanupTimeout;
			try
			{
				invocation = cleanupDeadlineOverride is { } deadline
					? lease.BeginCleanupInvocation(deadline)
					: lease.BeginCleanupInvocation(cleanupTimeout);
			}
			catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or TimeoutException)
			{
				// Cleanup has no safe wire owner. Detach only the captured session.
				lease.Session.Detach(lease.Identity)?.AbortTransport();
			}
		}
		try
		{
			if (CloseOwned()) invocation?.RecordDiagnosticCleanupSuccess();
		}
		catch
		{
			lease?.Session.Detach(lease.Identity)?.AbortTransport();
			throw;
		}
		finally
		{
			invocation?.Dispose();
			var released = Interlocked.Exchange(ref executionLease, null);
			try
			{
				if (registeredReader) released?.Session.UnregisterReader(released, this);
				if (ownsExecutionLease) released?.Dispose();
			}
			finally
			{
				var plan = Interlocked.Exchange(ref commandPlan, null);
				if (plan != null) releaseCommandPlan?.Invoke(plan);
			}
		}
	}

	internal bool CloseOwned()
	{
		if (m_IsClosed) return false;
		InvalidateLobFlows();
		m_IsClosed = true;
		m_DbInfo = null;
		m_ColInfo = null;
		m_CurrentRow = -1L;
		m_RsCache = null;
		var statement = m_Statement;
		m_Statement = null;
		var lease = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
		bool sameSession = lease != null && lease.Session.IsCurrent(lease.Identity.SessionId, lease.Identity.LeaseGeneration)
			&& ReferenceEquals(m_Conn?.Session, lease.Session);
		bool closedStatement = sameSession && statement != null && !statement.P();
		if (closedStatement) statement.p();
		else statement?.o();
		if (sameSession && (m_Behavior & CommandBehavior.CloseConnection) != 0)
			m_Conn.Conn?.CloseExpectedSession(lease.Session);
		return closedStatement;
	}

	internal bool do_GetBoolean(int i)
	{
		object obj = do_GetValue(i);
		if (obj is bool)
		{
			return (bool)obj;
		}
		return Convert.ToBoolean(obj);
	}

	internal byte do_GetByte(int i)
	{
		using var invocation = BeginInternalInvocation();
		byte[] value = null;
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetByte(i, value, cType, precision, scale);
	}

	internal long do_GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
	{
		ValidateLobRange(fieldOffset, buffer, bufferoffset, length);
		using var invocation = BeginInternalInvocation();
		checkClosed(); CheckIndex(i);
		int type = m_ColInfo[i].GetCType();
		if (type is not (3 or 12 or 17 or 18 or 19 or 54))
			throw new InvalidCastException("Column does not support a binary value.");
		if (buffer != null) GuardSequentialLobUnit(i, bytes: true);
		if (buffer != null) GuardSequentialRange(i, fieldOffset);
		if (buffer != null && length == 0)
		{
			byte[] zero = null; GetByteArrayValue(i, ref zero, lobPartial: true, lengthScan: true);
			if (zero == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			return CompleteReaderInvocationIfSent(invocation, 0L);
		}
		if (type == 19)
		{
			// Compatibility: CLOB bytes retain the bounded full-materialization contract.
			byte[] raw = null; GetByteArrayValue(i, ref raw, lobPartial: true, lengthScan: buffer == null);
			byte[] encoded = m_GetVal.GetBytes(i, raw, type, m_ColInfo[i].GetPrecision(), m_ColInfo[i].GetScale());
			if (buffer == null) return CompleteReaderInvocationIfSent(invocation, encoded.LongLength);
			int count = fieldOffset >= encoded.LongLength ? 0 : (int)Math.Min(length, encoded.LongLength - fieldOffset);
			encoded.AsSpan(checked((int)Math.Min(fieldOffset, encoded.LongLength)), count).CopyTo(buffer.AsSpan(bufferoffset, count));
			if (is_SequentialAccess && count > 0)
			{
				m_StreamPos = checked(fieldOffset + count);
				sequentialLobUnitOrdinal = i; sequentialLobBytes = true;
			}
			return CompleteReaderInvocationIfSent(invocation, (long)count);
		}
		if (buffer == null)
		{
			byte[] raw = null; GetByteArrayValue(i, ref raw, lobPartial: true, lengthScan: true);
			if (raw == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			if (type != 12) return CompleteReaderInvocationIfSent(invocation, raw.LongLength);
			var locator = new AbstractLob(raw, 0, m_Conn, m_ColInfo[i]);
			if (locator.storageType == AbstractLob.STORAGE_IN_ROW)
			{
				int head = locator.getHeadSize();
				if (locator.bytesLength < 0 || locator.bytesLength > raw.LongLength - head)
					DmError.ThrowDmException(DmErrorDefinition.ECNET_LOB_LENGTH_ERROR);
			}
			if (locator.bytesLength >= 0) return CompleteReaderInvocationIfSent(invocation, locator.bytesLength);
			return CompleteReaderInvocationIfSent(invocation, m_Conn.GetCsi().A(locator));
		}
		var cursor = RangeCursor(i, false, out bool temporary);
		try
		{
			if (fieldOffset < cursor.Position) DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
			byte[] discard = new byte[DmConnectionSettings.DefaultLobChunkSize];
			while (cursor.Position < fieldOffset)
				if (cursor.ReadBytes(discard.AsSpan(0, (int)Math.Min(discard.Length, fieldOffset - cursor.Position))) == 0)
					return CompleteReaderInvocationIfSent(invocation, 0L);
			int total = 0;
			while (total < length)
			{
				int count = cursor.ReadBytes(buffer.AsSpan(bufferoffset + total, length - total));
				if (count == 0) break;
				total += count;
			}
			m_StreamPos = cursor.Position; return CompleteReaderInvocationIfSent(invocation, (long)total);
		}
		finally { if (temporary) cursor.Dispose(); }
	}

	internal char do_GetChar(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetChar(int i)");
		CheckIndex(i);
		checkClosed();
		char[] array = new char[1];
		do_GetChars(i, 0L, array, 0, 1);
		return array[0];
	}

	internal long do_GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
	{
		ValidateCharRange(fieldoffset, buffer, bufferoffset, length);
		using var invocation = BeginInternalInvocation();
		checkClosed(); CheckIndex(i);
		int type = m_ColInfo[i].GetCType();
		if (type is not (0 or 1 or 2 or 19 or 28 or 54))
			throw new InvalidCastException("Column does not support a text value.");
		if (buffer != null) GuardSequentialLobUnit(i, bytes: false);
		if (buffer != null) GuardSequentialRange(i, fieldoffset);
		if (type == 28)
		{
			// ROWID is a fixed binary value rendered by its established local
			// converter, never server-charset text or a network LOB locator.
			byte[] raw = null;
			GetByteArrayValue(i, ref raw, lobPartial: true, lengthScan: buffer == null);
			if (raw == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			if (raw.Length is not (8 or 12)) throw new InvalidDataException("ROWID payload length is invalid.");
			string rendered = m_GetVal.GetString(i, raw, type, m_ColInfo[i].GetPrecision(), m_ColInfo[i].GetScale());
			if (buffer == null) return CompleteReaderInvocationIfSent(invocation, (long)rendered.Length);
			int count = fieldoffset >= rendered.Length ? 0 : (int)Math.Min(length, rendered.Length - fieldoffset);
			rendered.AsSpan(checked((int)Math.Min(fieldoffset, rendered.Length)), count).CopyTo(buffer.AsSpan(bufferoffset, count));
			if (is_SequentialAccess && count > 0)
			{
				m_StreamPos = checked(fieldoffset + count);
				sequentialLobUnitOrdinal = i; sequentialLobBytes = false;
			}
			return CompleteReaderInvocationIfSent(invocation, (long)count);
		}
		if (buffer != null && length == 0)
		{
			byte[] zero = null; GetByteArrayValue(i, ref zero, lobPartial: true, lengthScan: true);
			if (zero == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			return CompleteReaderInvocationIfSent(invocation, 0L);
		}
		bool temporary = false;
		var cursor = buffer == null ? CreateLobCursor(i, true, lengthScan: true) : RangeCursor(i, true, out temporary);
		if (buffer == null) temporary = true;
		try
		{
			char[] discard = new char[8192];
			if (buffer == null)
			{
				while (cursor.ReadChars(discard) != 0) { }
				return CompleteReaderInvocationIfSent(invocation, cursor.Position);
			}
			if (fieldoffset < cursor.Position) DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
			while (cursor.Position < fieldoffset)
				if (cursor.ReadChars(discard.AsSpan(0, (int)Math.Min(discard.Length, fieldoffset - cursor.Position))) == 0)
					return CompleteReaderInvocationIfSent(invocation, 0L);
			int total = 0;
			while (total < length)
			{
				int count = cursor.ReadChars(buffer.AsSpan(bufferoffset + total, length - total));
				if (count == 0) break;
				total += count;
			}
			m_StreamPos = cursor.Position; return CompleteReaderInvocationIfSent(invocation, (long)total);
		}
		finally { if (temporary) cursor.Dispose(); }
	}

	internal string do_GetDataTypeName(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDataTypeName(int i)");
		checkClosed();
		return m_ColInfo[i].GetTypeName();
	}

	internal DateTime do_GetDateTime(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDateTime(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetTimestamp(i, value, cType, precision, scale);
	}

	internal decimal do_GetDecimal(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDecimal(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetBigDecimal(i, value, cType, precision, scale);
	}

	internal double do_GetDouble(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDouble(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetDouble(i, value, cType, precision, scale);
	}

	internal IEnumerator do_GetEnumerator()
	{
		if ((m_Behavior & CommandBehavior.CloseConnection) != 0)
		{
			return new DbEnumerator(this, closeReader: true);
		}
		return new DbEnumerator(this, closeReader: false);
	}

	internal Type do_GetFieldType(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFieldType(int i)");
		CheckIndex(i);
		return DmSqlType.CTypeToSystemType(m_ColInfo[i].GetCType(), m_ColInfo[i].GetPrecision(), m_ColInfo[i].GetScale(), m_Conn.ConnProperty);
	}

	internal float do_GetFloat(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFloat(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetFloat(i, value, cType, precision, scale);
	}

	internal Guid do_GetGuid(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetGuid(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		if (value == null) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		int cType = m_ColInfo[i].GetCType();
		if (cType is 0 or 1 or 2 or 54)
		{
			string text = m_GetVal.GetString(i, value, cType, m_ColInfo[i].GetPrecision(), m_ColInfo[i].GetScale());
			if (text != null && Guid.TryParseExact(text.TrimEnd(), "D", out Guid parsed)) return parsed;
			throw new FormatException("GUID text is not in the declared 36-character format.");
		}
		if (cType is 17 or 18)
		{
			if (value.Length != 16) throw new FormatException("Binary GUID must contain exactly 16 bytes.");
			return new Guid(value);
		}
		throw new InvalidCastException("Column storage is not a supported GUID format.");
	}

	internal short do_GetInt16(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt16(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetShort(i, value, cType, precision, scale);
	}

	internal int do_GetInt32(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt32(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetInt(i, value, cType, precision, scale);
	}

	internal long do_GetInt64(int i)
	{
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetInt64(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetLong(i, value, cType, precision, scale);
	}

	internal string do_GetName(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetName(int i)");
		string name = m_ColInfo[i].GetName();
		if (m_Conn.ConnProperty.ColumnNameUpperCase)
		{
			return name.ToUpper();
		}
		if (m_Conn.ConnProperty.ColumnNameCase == ColumnNameCase.UPPER)
		{
			return name.ToUpper();
		}
		if (m_Conn.ConnProperty.ColumnNameCase == ColumnNameCase.LOWER)
		{
			return name.ToLower();
		}
		return name;
	}

	internal int do_GetOrdinal(string name)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetOrdinal(string name)");
		checkClosed();
		int i;
		for (i = 0; i < m_DbInfo.GetColumnCount() && !m_ColInfo[i].GetName().Equals(name); i++)
		{
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int j = 0; j < m_DbInfo.GetColumnCount(); j++)
			{
				if ((m_ColInfo[j].GetTable() + "." + m_ColInfo[j].GetName()).Equals(name))
				{
					i = j;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int k = 0; k < m_DbInfo.GetColumnCount(); k++)
			{
				if ((m_ColInfo[k].GetSchema() + "." + m_ColInfo[k].GetTable() + "." + m_ColInfo[k].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = k;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (i = 0; i < m_DbInfo.GetColumnCount() && !m_ColInfo[i].GetName().ToUpper().Equals(name.ToUpper()); i++)
			{
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int l = 0; l < m_DbInfo.GetColumnCount(); l++)
			{
				if ((m_ColInfo[l].GetTable() + "." + m_ColInfo[l].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = l;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			for (int m = 0; m < m_DbInfo.GetColumnCount(); m++)
			{
				if ((m_ColInfo[m].GetSchema() + "." + m_ColInfo[m].GetTable() + "." + m_ColInfo[m].GetName()).ToUpper().Equals(name.ToUpper()))
				{
					i = m;
					break;
				}
			}
		}
		if (i >= m_DbInfo.GetColumnCount())
		{
			throw new IndexOutOfRangeException();
		}
		return i;
	}

	internal Type do_GetProviderSpecificFieldType(int ordinal)
	{
		CheckIndex(ordinal);
		if (m_ColInfo[ordinal].GetCType() is 9 or 24) return typeof(DmDecimal);
		return do_GetFieldType(ordinal);
	}

	internal object do_GetProviderSpecificValue(int ordinal)
	{
		using var invocation = BeginInternalInvocation();
		CheckIndex(ordinal);
		int cType = m_ColInfo[ordinal].GetCType();
		if (cType is not (9 or 24)) return CompleteReaderInvocationIfSent(invocation, GetValueOwned(ordinal));
		byte[] value = null;
		GetByteArrayValue(ordinal, ref value);
		if (value == null) return CompleteReaderInvocationIfSent(invocation, (object)DBNull.Value);
		int precision = m_ColInfo[ordinal].GetPrecision();
		int scale = m_ColInfo[ordinal].GetScale();
		return CompleteReaderInvocationIfSent(invocation, cType == 9
			? (object)DmNumericCodec.DecodeDecimal(value, precision > 0 && scale >= 0 ? scale : null)
			: DmNumericCodec.DecodeScaledInt64(value, scale));
	}

	internal int do_GetProviderSpecificValues(object[] values)
	{
		using var invocation = BeginInternalInvocation();
		checkClosed();
		if (values == null) return 0;
		int count = Math.Min(values.Length, m_DbInfo.GetColumnCount());
		for (int i = 0; i < count; i++) values[i] = do_GetProviderSpecificValue(i);
		return CompleteReaderInvocationIfSent(invocation, count);
	}

	internal DataTable do_GetSchemaTable()
	{
		using var invocation = BeginInternalInvocation();
		return GetSchemaTableOwned();
	}

	private DataTable GetSchemaTableOwned()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetSchemaTable()");
		checkClosed();
		if (m_ColInfo == null)
		{
			return null;
		}
		DataTable dataTable = new DataTable("SchemaTable");
		BuildSchemaColumns(dataTable);
		FillSchemaTable(dataTable);
		return dataTable;
	}

	internal DataTable do_GetDataTable()
	{
		using var invocation = BeginInternalInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "do_GetDataTable()");
		checkClosed();
		DataTable dataTable = new DataTable();
		new DmDataAdapter().do_Fill(dataTable, this);
		return dataTable;
	}

	internal string do_GetString(int i)
	{
		using var invocation = BeginInternalInvocation();
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetString(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (value == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		}
		return CompleteReaderInvocationIfSent(invocation, m_GetVal.GetString(i, value, cType, precision, scale));
	}

	internal object do_GetValue(int i)
	{
		using var invocation = BeginInternalInvocation();
		return CompleteReaderInvocationIfSent(invocation, GetValueOwned(i));
	}

	internal object GetValueOwned(int i)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetValue(int i)");
		CheckIndex(i);
		checkClosed();
		byte[] value = null;
		GetByteArrayValue(i, ref value);
		if (value == null)
		{
			return DBNull.Value;
		}
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		if (cType == 1)
		{
			int num = ((value != null) ? value.Length : 0);
			int precision2 = m_ColInfo[i].GetPrecision();
			if (num < precision2)
			{
				byte[] array = new byte[precision2];
				if (num != 0)
				{
					Array.Copy(value, 0, array, 0, num);
				}
				for (int j = num; j < precision2; j++)
				{
					array[j] = 32;
				}
				value = array;
			}
		}
		if (DmSqlType.isComplexType(cType, scale))
		{
			throw new NotSupportedException("Complex type value decoding is not supported by this provider version.");
		}
		return BindLob(m_GetVal.GetObject(i, value, cType, precision, scale));
	}

	internal int do_GetValues(object[] values)
	{
		using var invocation = BeginInternalInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetValues(object[] values)");
		checkClosed();
		if (values == null)
		{
			return 0;
		}
		int num = m_DbInfo.GetColumnCount();
		if (values.Length < num)
		{
			num = values.Length;
		}
		for (int i = 0; i < num; i++)
		{
			values[i] = do_GetValue(i);
		}
		return CompleteReaderInvocationIfSent(invocation, num);
	}

	internal bool do_IsDBNull(int i)
	{
		using var invocation = BeginInternalInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "IsDBNull(int i)");
		checkClosed();
		byte[] value = null;
		GetByteArrayValue(i, ref value);
		if (value == null)
		{
			return true;
		}
		return false;
	}

	internal bool do_NextResult()
	{
		using var invocation = BeginInternalInvocation();
		return NextResultOwned();
	}

	private bool NextResultOwned() => AdvanceToReadableResultOwned(initialSeek: false);

	internal void SeekFirstReadableResultOwned()
	{
		if (!resultCursor.HasReadableRowset && !resultCursor.IsTerminal)
			AdvanceToReadableResultOwned(initialSeek: true);
	}

	private bool AdvanceToReadableResultOwned(bool initialSeek)
	{
		InvalidateLobFlows();
		DmInvocation.Current?.ThrowIfTerminated();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "NextResult()");
		checkClosed();
		if ((!initialSeek && (m_Behavior & CommandBehavior.SingleResult) != 0) || resultCursor.IsTerminal)
			return false;
		global::W.Dm.Internal.Legacy.A.A statement = m_Statement;
		if (statement?.f() == null) return false;
		if (statement.f().RefCursorStmtArr != null &&
			statement.f().RefCursorStmtArr.Count > statement.f().RefCursorStmtArr_cur)
			throw new NotSupportedException("Reference cursor results are not supported by this provider version.");

		for (int step = 0; step < 4096; step++)
		{
			DmInfo next = statement.h().A(statement, m_DbInfo, 0);
			resultCursor.Observe(next);
			ApplyResult(next, statement);
			if (resultCursor.IsTerminal) return false;
			if (resultCursor.HasReadableRowset) return true;
		}
		var owner = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
		owner?.Session.Detach(owner.Identity)?.AbortTransport();
		throw new System.IO.InvalidDataException("Result sequence exceeds the supported bound.");
	}

	internal bool NextResultForCommandOwned() => NextResultOwned();

	private void ApplyResult(DmInfo info, global::W.Dm.Internal.Legacy.A.A statement)
	{
		m_DbInfo = info;
		m_Statement = statement;
		m_ColInfo = info.GetColumnsInfo();
		bdta = info.rsBdta;
		if (m_ColInfo != null)
			foreach (DmColumn column in m_ColInfo) column.isBdta = bdta;
		m_RsCache = info.GetHasResultSet() ? statement.l() : null;
		m_RowCount = info.GetRowCount();
		m_StartRow = 0L;
		m_CurrentRow = -1L;
		m_is_single_row = 0;
		m_SequentialSeq = -1;
		m_StreamPos = 0L;
		m_Clobs.Clear();
		m_GetVal = new DmGetValue(m_Conn.ConnProperty.ServerEncoding, statement,
			m_Conn.ConnProperty.NewLobFlag, m_ColInfo);
	}

	internal bool do_Read()
	{
		using var invocation = BeginInternalInvocation();
		return ReadOwned();
	}

	internal bool ReadOwned()
	{
		DmInvocation.Current?.ThrowIfTerminated();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Read()");
		checkClosed();
		ClearClobs();
		if ((m_Behavior & CommandBehavior.SequentialAccess) != 0)
		{
			m_SequentialSeq = -1;
		}
		if ((m_Behavior & CommandBehavior.SingleRow) != 0 && m_CurrentRow != -1)
		{
			m_is_single_row = 1;
			return false;
		}
		if ((m_Behavior & CommandBehavior.SchemaOnly) != 0)
		{
			m_RowCount = 0L;
			return false;
		}
		if (!do_HasRows || m_RsCache == null)
		{
			return false;
		}
		bool result = m_RsCache.do_next();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	internal void do_Dispose(bool disposing)
	{
		base.Dispose(disposing);
	}

	internal DmDataReader do_GetDbDataReader(int ordinal)
	{
		return (DmDataReader)base.GetDbDataReader(ordinal);
	}

	public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var invocation = ReaderLease.BeginInvocation(cancellationToken);
		try
		{
			invocation.ThrowIfTerminated();
			AfterInvocationEntered?.Invoke(invocation.Identity);
			return CompleteReaderInvocation(invocation, await ReadOwnedAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	internal async Task<bool> ReadOwnedAsync(CancellationToken cancellationToken)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		cancellationToken.ThrowIfCancellationRequested();
		checkClosed();
		ClearClobs();
		if (is_SequentialAccess) m_SequentialSeq = -1;
		if ((m_Behavior & CommandBehavior.SingleRow) != 0 && m_CurrentRow != -1)
		{ m_is_single_row = 1; return false; }
		if (!do_HasRows || m_RsCache == null) return false;
		bool result = await m_RsCache.do_nextAsync(cancellationToken).ConfigureAwait(false);
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var invocation = ReaderLease.BeginInvocation(cancellationToken);
		try
		{
			invocation.ThrowIfTerminated();
			return CompleteReaderInvocation(invocation, await AdvanceToReadableResultOwnedAsync(false, cancellationToken).ConfigureAwait(false));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	internal Task<bool> NextResultForCommandOwnedAsync(CancellationToken token)
		=> AdvanceToReadableResultOwnedAsync(false, token);

	internal async Task SeekFirstReadableResultOwnedAsync(CancellationToken token)
	{
		if (!resultCursor.HasReadableRowset && !resultCursor.IsTerminal)
			await AdvanceToReadableResultOwnedAsync(true, token).ConfigureAwait(false);
	}

	private async Task<bool> AdvanceToReadableResultOwnedAsync(bool initialSeek, CancellationToken token)
	{
		InvalidateLobFlows();
		DmInvocation.Current?.ThrowIfTerminated();
		token.ThrowIfCancellationRequested();
		checkClosed();
		if ((!initialSeek && (m_Behavior & CommandBehavior.SingleResult) != 0) || resultCursor.IsTerminal) return false;
		var statement = m_Statement;
		if (statement?.f() == null) return false;
		if (statement.f().RefCursorStmtArr != null && statement.f().RefCursorStmtArr.Count > statement.f().RefCursorStmtArr_cur)
			throw new NotSupportedException("Reference cursor results are not supported by this provider version.");
		for (int step = 0; step < 4096; step++)
		{
			DmInfo next = await statement.h().MoreResultsAsync(statement, m_DbInfo, 0, token).ConfigureAwait(false);
			resultCursor.Observe(next);
			ApplyResult(next, statement);
			if (resultCursor.IsTerminal) return false;
			if (resultCursor.HasReadableRowset) return true;
		}
		var owner = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
		owner?.Session.Detach(owner.Identity)?.AbortTransport();
		throw new System.IO.InvalidDataException("Result sequence exceeds the supported bound.");
	}

	public override Task CloseAsync() => BeginAsyncClose(null);

	internal Task CloseForTransactionCleanupAsync(DmDeadline deadline) => BeginAsyncClose(deadline);

	private Task BeginAsyncClose(DmDeadline? deadline)
	{
		TaskCompletionSource completion;
		lock (asyncCloseGate)
		{
			if (asyncCloseTask != null) return asyncCloseTask;
			completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			asyncCloseTask = ObserveCloseCompletionAsync(completion.Task);
		}
		// Publish ownership before running hooks or touching the wire. Reentrant and
		// concurrent callers must observe the same still-pending cleanup task.
		_ = CompleteAsyncClose(completion, deadline);
		return asyncCloseTask;
	}

	private static async Task ObserveCloseCompletionAsync(Task completion)
		=> await completion.ConfigureAwait(false);

	private async Task CompleteAsyncClose(TaskCompletionSource completion, DmDeadline? deadline)
	{
		try
		{
			await CloseCoreAsync(deadline).ConfigureAwait(false);
			completion.TrySetResult();
		}
		catch (Exception error) { completion.TrySetException(error); }
	}

	private async Task CloseCoreAsync(DmDeadline? cleanupDeadlineOverride)
	{
		if (Interlocked.CompareExchange(ref closeStarted, 1, 0) != 0) return;
		var lease = Volatile.Read(ref executionLease);
		DmInvocation invocation = null;
		try
		{
			if (lease != null &&
				lease.Session.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
			{
				TimeSpan timeout = commandPlan?.CleanupTimeout ?? m_Conn.Conn.Settings.CleanupTimeout;
				try { invocation = cleanupDeadlineOverride is { } deadline
					? lease.BeginCleanupInvocation(deadline) : lease.BeginCleanupInvocation(timeout); }
				catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException or TimeoutException)
				{ lease.Session.Detach(lease.Identity)?.AbortTransport(); }
			}
			if (m_IsClosed) return;
			InvalidateLobFlows();
		m_IsClosed = true;
			m_DbInfo = null;
			m_ColInfo = null;
			m_CurrentRow = -1L;
			m_RsCache = null;
			var statement = m_Statement;
			m_Statement = null;
			bool sameSession = lease != null && lease.Session.IsCurrent(lease.Identity.SessionId, lease.Identity.LeaseGeneration)
				&& ReferenceEquals(m_Conn?.Session, lease.Session);
			bool closedStatement = sameSession && invocation != null && statement != null && !statement.P();
			if (closedStatement)
				await statement.CloseAsync(CancellationToken.None).ConfigureAwait(false);
			else statement?.o();
			if (sameSession && (m_Behavior & CommandBehavior.CloseConnection) != 0)
				await m_Conn.Conn.CloseExpectedSessionAsync(lease.Session).ConfigureAwait(false);
			if (closedStatement) invocation.RecordDiagnosticCleanupSuccess();
		}
		catch { lease?.Session.Detach(lease.Identity)?.AbortTransport(); throw; }
		finally
		{
			invocation?.Dispose();
			var released = Interlocked.Exchange(ref executionLease, null);
			try
			{
				if (registeredReader) released?.Session.UnregisterReader(released, this);
				if (ownsExecutionLease) released?.Dispose();
			}
			finally
			{
				var plan = Interlocked.Exchange(ref commandPlan, null);
				if (plan != null) releaseCommandPlan?.Invoke(plan);
			}
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await CloseAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	public override Task<DataTable> GetSchemaTableAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var invocation = ReaderLease.BeginInvocation(cancellationToken);
		try
		{
			invocation.ThrowIfTerminated();
			return CompleteReaderInvocation(invocation, Task.FromResult(GetSchemaTableOwned()));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var invocation = ReaderLease.BeginInvocation(cancellationToken);
		try
		{
			invocation.ThrowIfTerminated();
			CheckIndex(ordinal);
			checkClosed();
			byte[] value = null;
			GetByteArrayValue(ordinal, ref value); // Already fetched row bytes; no network access.
			return CompleteReaderInvocation(invocation, Task.FromResult(value == null));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var invocation = ReaderLease.BeginInvocation(cancellationToken);
		try
		{
			invocation.ThrowIfTerminated();
			CheckIndex(ordinal);
			checkClosed();
			int type = m_ColInfo[ordinal].GetCType();
			if (type is not (12 or 19)) return CompleteReaderInvocation(invocation, GetFieldValueOwned<T>(ordinal));
			byte[] bytes = null;
			GetByteArrayValue(ordinal, ref bytes);
			if (bytes == null)
			{
				if (typeof(T) == typeof(object) || typeof(T) == typeof(DBNull)) return CompleteReaderInvocation(invocation, (T)(object)DBNull.Value);
				DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			}
			object value;
			if (typeof(T) == typeof(string))
				value = await m_GetVal.GetStringAsync(ordinal, bytes, type, m_ColInfo[ordinal].GetPrecision(), m_ColInfo[ordinal].GetScale(), cancellationToken).ConfigureAwait(false);
			else if (typeof(T) == typeof(byte[]))
				value = await m_GetVal.GetBytesAsync(ordinal, bytes, type, m_ColInfo[ordinal].GetPrecision(), m_ColInfo[ordinal].GetScale(), cancellationToken).ConfigureAwait(false);
			else if (typeof(T) == typeof(object))
				value = await m_GetVal.GetObjectAsync(ordinal, bytes, type, m_ColInfo[ordinal].GetPrecision(), m_ColInfo[ordinal].GetScale(), cancellationToken).ConfigureAwait(false);
			else throw new NotSupportedException("Asynchronous LOB field conversion supports object, string and byte array values.");
			return CompleteReaderInvocation(invocation, (T)value);
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	internal async Task<object> GetValueOwnedAsync(int ordinal, CancellationToken token)
	{
		DmInvocation.Current?.ThrowIfTerminated();
		token.ThrowIfCancellationRequested();
		CheckIndex(ordinal);
		checkClosed();
		if (m_ColInfo[ordinal].GetCType() is not (12 or 19)) return GetValueOwned(ordinal);
		byte[] bytes = null;
		GetByteArrayValue(ordinal, ref bytes);
		if (bytes == null) return DBNull.Value;
		return await m_GetVal.GetObjectAsync(ordinal, bytes, m_ColInfo[ordinal].GetCType(),
			m_ColInfo[ordinal].GetPrecision(), m_ColInfo[ordinal].GetScale(), token).ConfigureAwait(false);
	}

	public override void Close()
	{
		if (filterHead == null)
		{
			CloseCore(allowOwnedInvocation: false);
		}
		else
		{
			filterHead.Close(this);
		}
	}

	public override bool GetBoolean(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetBoolean(i));
		}
		return filterHead.GetBoolean(this, i);
	}

	public override byte GetByte(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetByte(i);
		}
		return filterHead.GetByte(this, i);
	}

	public override long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetBytes(i, fieldOffset, buffer, bufferoffset, length));
		}
		return filterHead.GetBytes(this, i, fieldOffset, buffer, bufferoffset, length);
	}

	public override char GetChar(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetChar(i));
		}
		return filterHead.GetChar(this, i);
	}

	public override long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetChars(i, fieldoffset, buffer, bufferoffset, length));
		}
		return filterHead.GetChars(this, i, fieldoffset, buffer, bufferoffset, length);
	}

	public override string GetDataTypeName(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetDataTypeName(i);
		}
		return filterHead.GetDataTypeName(this, i);
	}

	public override DateTime GetDateTime(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetDateTime(i);
		}
		return filterHead.GetDateTime(this, i);
	}

	public override decimal GetDecimal(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetDecimal(i);
		}
		return filterHead.GetDecimal(this, i);
	}

	public override double GetDouble(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetDouble(i);
		}
		return filterHead.GetDouble(this, i);
	}

	public override IEnumerator GetEnumerator()
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetEnumerator();
		}
		return filterHead.GetEnumerator(this);
	}

	public override Type GetFieldType(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetFieldType(i);
		}
		return filterHead.GetFieldType(this, i);
	}

	public override float GetFloat(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetFloat(i);
		}
		return filterHead.GetFloat(this, i);
	}

	public override Guid GetGuid(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetGuid(i);
		}
		return filterHead.GetGuid(this, i);
	}

	public override short GetInt16(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetInt16(i);
		}
		return filterHead.GetInt16(this, i);
	}

	public override int GetInt32(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetInt32(i);
		}
		return filterHead.GetInt32(this, i);
	}

	public override long GetInt64(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetInt64(i);
		}
		return filterHead.GetInt64(this, i);
	}

	public override string GetName(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetName(i);
		}
		return filterHead.GetName(this, i);
	}

	public override int GetOrdinal(string name)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetOrdinal(name);
		}
		return filterHead.GetOrdinal(this, name);
	}

	public override Type GetProviderSpecificFieldType(int ordinal)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_GetProviderSpecificFieldType(ordinal);
		}
		return filterHead.GetProviderSpecificFieldType(this, ordinal);
	}

	public override object GetProviderSpecificValue(int ordinal)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetProviderSpecificValue(ordinal));
		}
		return filterHead.GetProviderSpecificValue(this, ordinal);
	}

	public override int GetProviderSpecificValues(object[] values)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetProviderSpecificValues(values));
		}
		return filterHead.GetProviderSpecificValues(this, values);
	}

	public override DataTable GetSchemaTable()
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return GetSchemaTableOwned();
		}
		return filterHead.GetSchemaTable(this);
	}

	public override string GetString(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetString(i));
		}
		return filterHead.GetString(this, i);
	}

	public override object GetValue(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetValue(i));
		}
		return filterHead.GetValue(this, i);
	}

	public override T GetFieldValue<T>(int ordinal)
	{
		using var invocation = BeginReaderInvocation();
		try
		{
			invocation.ThrowIfTerminated();
			return CompleteReaderInvocation(invocation, GetFieldValueOwned<T>(ordinal));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	private T GetFieldValueOwned<T>(int ordinal)
	{
		if (typeof(T) == typeof(Guid)) return (T)(object)do_GetGuid(ordinal);
		if (typeof(T) == typeof(DmDecimal))
		{
			object providerValue = do_GetProviderSpecificValue(ordinal);
			if (providerValue is DBNull) DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
			return (T)providerValue;
		}
		if (typeof(T) == typeof(byte)) return (T)(object)do_GetByte(ordinal);
		if (typeof(T) == typeof(short)) return (T)(object)do_GetInt16(ordinal);
		if (typeof(T) == typeof(int)) return (T)(object)do_GetInt32(ordinal);
		if (typeof(T) == typeof(long)) return (T)(object)do_GetInt64(ordinal);
		if (typeof(T) == typeof(float)) return (T)(object)do_GetFloat(ordinal);
		if (typeof(T) == typeof(double)) return (T)(object)do_GetDouble(ordinal);
		if (typeof(T) == typeof(decimal)) return (T)(object)do_GetDecimal(ordinal);
		if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(ushort) ||
			typeof(T) == typeof(uint) || typeof(T) == typeof(ulong))
			return GetAdditionalInteger<T>(ordinal);
		object value = do_GetValue(ordinal);
		if (value is DBNull && typeof(T) != typeof(object) && typeof(T) != typeof(DBNull))
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NULL_VALUE);
		if (typeof(T) == typeof(TimeSpan) && value is DmIntervalDT interval)
		{
			return (T)(object)interval.ToTimeSpanExact();
		}
		if (typeof(T) == typeof(DateOnly) && value is DateTime dateTime)
		{
			if (m_ColInfo[ordinal].GetCType() != 14 || dateTime.TimeOfDay != TimeSpan.Zero)
				throw new InvalidCastException("DateOnly requires a DATE column.");
			return (T)(object)DateOnly.FromDateTime(dateTime);
		}
		if (typeof(T) == typeof(TimeOnly) && value is DateTime dateTime2)
		{
			if (m_ColInfo[ordinal].GetCType() != 15)
				throw new InvalidCastException("TimeOnly requires a TIME column.");
			return (T)(object)TimeOnly.FromDateTime(dateTime2);
		}
		if (typeof(T) == typeof(TimeOnly) && value is TimeSpan timeSpan)
		{
			if (m_ColInfo[ordinal].GetCType() != 15)
				throw new InvalidCastException("TimeOnly requires a TIME column.");
			return (T)(object)TimeOnly.FromTimeSpan(timeSpan);
		}
		if (value is T exact) return exact;
		throw new InvalidCastException("Column cannot be read as the requested CLR type.");
	}

	private T GetAdditionalInteger<T>(int ordinal)
	{
		CheckIndex(ordinal);
		checkClosed();
		byte[] value = null;
		GetByteArrayValue(ordinal, ref value);
		int cType = m_ColInfo[ordinal].GetCType();
		int precision = m_ColInfo[ordinal].GetPrecision();
		int scale = m_ColInfo[ordinal].GetScale();
		if (typeof(T) == typeof(sbyte))
			return (T)(object)m_GetVal.GetSByte(ordinal, value, cType, precision, scale);
		if (typeof(T) == typeof(ushort))
			return (T)(object)m_GetVal.GetUshort(ordinal, value, cType, precision, scale);
		if (typeof(T) == typeof(uint))
			return (T)(object)m_GetVal.GetUint(ordinal, value, cType, precision, scale);
		return (T)(object)m_GetVal.GetUlong(ordinal, value, cType, precision, scale);
	}

	public override int GetValues(object[] values)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return CompleteReaderInvocationIfSent(invocation, do_GetValues(values));
		}
		return filterHead.GetValues(this, values);
	}

	public override bool IsDBNull(int i)
	{
		using var invocation = BeginReaderInvocation();
		if (filterHead == null)
		{
			return do_IsDBNull(i);
		}
		return filterHead.IsDBNull(this, i);
	}

	public override bool NextResult()
	{
		using var invocation = BeginReaderInvocation();
		try
		{
			invocation.ThrowIfTerminated();
			if (filterHead == null)
			{
				return CompleteReaderInvocation(invocation, NextResultOwned());
			}
			return CompleteReaderInvocation(invocation, filterHead.NextResult(this));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	public override bool Read()
	{
		using var invocation = BeginReaderInvocation();
		try
		{
			invocation.ThrowIfTerminated();
			AfterInvocationEntered?.Invoke(invocation.Identity);
			if (filterHead == null)
			{
				return CompleteReaderInvocation(invocation, ReadOwned());
			}
			return CompleteReaderInvocation(invocation, filterHead.Read(this));
		}
		catch (Exception error) { throw invocation.TranslateFailure(error); }
	}

	protected override void Dispose(bool disposing)
	{
		if (filterHead == null)
		{
			do_Dispose(disposing);
		}
		else
		{
			filterHead.Dispose(this, disposing);
		}
	}

	protected override DbDataReader GetDbDataReader(int ordinal)
	{
		if (filterHead == null)
		{
			return do_GetDbDataReader(ordinal);
		}
		return filterHead.GetDbDataReader(this, ordinal);
	}

	public new void Dispose()
	{
		Close();
	}

	private void BuildSchemaColumns(DataTable schTbl)
	{
		schTbl.Columns.Add("ColumnName", typeof(string));
		schTbl.Columns.Add("ColumnOrdinal", typeof(int));
		schTbl.Columns.Add("ColumnSize", typeof(int));
		schTbl.Columns.Add("NumericPrecision", typeof(int));
		schTbl.Columns.Add("NumericScale", typeof(int));
		schTbl.Columns.Add("IsUnique", typeof(bool));
		schTbl.Columns.Add("IsKey", typeof(bool));
		schTbl.Columns.Add("BaseServerName", typeof(string));
		schTbl.Columns.Add("BaseCatalogName", typeof(string));
		schTbl.Columns.Add("BaseSchemaName", typeof(string));
		schTbl.Columns.Add("BaseTableName", typeof(string));
		schTbl.Columns.Add("BaseColumnName", typeof(string));
		schTbl.Columns.Add("DataType", typeof(Type));
		schTbl.Columns.Add("AllowDBNull", typeof(bool));
		schTbl.Columns.Add("ProviderType", typeof(int));
		schTbl.Columns.Add("IsAliased", typeof(bool));
		schTbl.Columns.Add("IsExpression", typeof(bool));
		schTbl.Columns.Add("IsIdentity", typeof(bool));
		schTbl.Columns.Add("IsAutoIncrement", typeof(bool));
		schTbl.Columns.Add("IsRowVersion", typeof(bool));
		schTbl.Columns.Add("IsHidden", typeof(bool));
		schTbl.Columns.Add("IsLong", typeof(bool));
		schTbl.Columns.Add("IsReadOnly", typeof(bool));
	}

	private void FillSchemaColumn(DataRow row, DmColumn dmCol, int i)
	{
		row["ColumnName"] = dmCol.GetName();
		row["ColumnOrdinal"] = i;
		row["ColumnSize"] = dmCol.GetSize();
		row["NumericPrecision"] = dmCol.GetPrecision();
		row["NumericScale"] = dmCol.GetScale();
		row["IsUnique"] = DBNull.Value;
		row["IsKey"] = DBNull.Value;
		row["BaseServerName"] = DBNull.Value;
		row["BaseCatalogName"] = DBNull.Value;
		row["BaseSchemaName"] = string.IsNullOrEmpty(dmCol.GetSchema()) ? DBNull.Value : dmCol.GetSchema();
		row["BaseTableName"] = string.IsNullOrEmpty(dmCol.GetTable()) ? DBNull.Value : dmCol.GetTable();
		row["BaseColumnName"] = string.IsNullOrEmpty(dmCol.GetBaseColumn()) ? DBNull.Value : dmCol.GetBaseColumn();
		row["DataType"] = DmSqlType.CTypeToSystemType(dmCol.GetCType(), dmCol.GetPrecision(), m_Conn.ConnProperty);
		row["AllowDBNull"] = dmCol.GetNullable();
		row["ProviderType"] = dmCol.GetCType();
		row["IsAliased"] = false;
		row["IsExpression"] = false;
		row["IsIdentity"] = dmCol.GetIdentity();
		row["IsAutoIncrement"] = dmCol.GetIdentity();
		row["IsRowVersion"] = false;
		row["IsHidden"] = false;
		row["IsLong"] = dmCol.GetIsLob();
		row["IsReadOnly"] = false;
	}

	private void FillSchemaTable(DataTable schTbl)
	{
		for (int i = 0; i < m_ColInfo.Length; i++)
		{
			DataRow row = schTbl.NewRow();
			FillSchemaColumn(row, m_ColInfo[i], i);
			row["IsReadOnly"] = !m_DbInfo.GetUpdatable();
			schTbl.Rows.Add(row);
		}
	}

	public DmDataReader GetKeyCols(string schema, string table)
	{
		throw new NotSupportedException("Independent metadata readers are not supported by this session.");
	}


	public DmDataReader GetUniqueCols(string schema, string table)
	{
		throw new NotSupportedException("Independent metadata readers are not supported by this session.");
	}


	public bool Previous()
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Previous()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_previous();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool First()
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "First()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_first();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Last()
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Last()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_last();
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Absolute(int pos)
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Absolute()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_absolute(pos);
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public bool Relative(int pos)
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Relative()");
		checkClosed();
		ClearClobs();
		if (!do_HasRows)
		{
			return false;
		}
		bool result = m_RsCache.do_relative(pos);
		m_CurrentRow = m_RsCache.currentPos;
		return result;
	}

	public new DbDataReader GetData(int i)
	{
		using var invocation = BeginReaderInvocation();
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetData(int i)");
		checkClosed();
		DmError.ThrowUnsupportedException();
		return null;
	}

	public DateTimeOffset GetDateTimeOffset(int i)
	{
		using var invocation = BeginReaderInvocation();
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDateTime(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetTimestampTZ(i, value, cType, precision, scale);
	}

	public DmXDec GetDmDecimal(int i)
	{
		using var invocation = BeginReaderInvocation();
		byte[] value = null;
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetDecimal(int i)");
		checkClosed();
		GetByteArrayValue(i, ref value);
		int cType = m_ColInfo[i].GetCType();
		int precision = m_ColInfo[i].GetPrecision();
		int scale = m_ColInfo[i].GetScale();
		return m_GetVal.GetDmDecimal(i, value, cType, precision, scale);
	}

	internal Type GetFieldTypeInner(int i)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "GetFieldTypeInner(int i)");
		CheckIndex(i);
		return DmSqlType.CTypeToSystemTypeInner(m_ColInfo[i].GetCType());
	}

	public DmBlob GetBlob(short i)
	{
		using var invocation = BeginReaderInvocation();
		return CompleteReaderInvocationIfSent(invocation, GetBlobOwned(i));
	}

	private DmBlob GetBlobOwned(short i)
	{
		checkClosed();
		return BindLob(new DmBlob(GetByteArrayValue(i), m_Conn, m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2), i);
	}

	public DmClob GetClob(short i)
	{
		using var invocation = BeginReaderInvocation();
		return CompleteReaderInvocationIfSent(invocation, GetClobOwned(i));
	}

	private DmClob GetClobOwned(short i)
	{
		checkClosed();
		return BindLob(new DmClob(GetByteArrayValue(i), m_Conn, m_ColInfo[i], m_Statement.G().ConnProperty.LobMode == 2), i);
	}

	private T BindLob<T>(T value, int ordinal = -1)
	{
		if (value is AbstractLob lob)
		{
			var lease = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
			if (lease == null) throw new InvalidOperationException("LOB has no reader execution lease.");
			lob.AttachExecutionLease(lease);
			lob.AttachRowOwner(CaptureLobOwner(ordinal));
		}
		return value;
	}

	private void GetByteArrayValue(int columnIndex, ref byte[] value, bool lobPartial = false, bool lengthScan = false)
	{
		if (columnIndex < 0 || columnIndex >= m_RsCache.colNum)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		RefreshSequentialLobPosition();
		if (is_SequentialAccess && m_SequentialSeq > columnIndex)
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SEQUENTIALACCESS_ERROR);
		if (is_SequentialAccess && !lobPartial && !lengthScan && m_SequentialSeq == columnIndex && m_StreamPos > 0)
			throw new InvalidOperationException("Sequential access already consumed this field.");
		// Validate the cached row and field before changing any live flow ownership.
		m_RsCache.GetBytes((short)columnIndex, ref value);
		if (lengthScan) return;
		if (activeLobFlow != null && activeLobOrdinal != columnIndex)
		{ activeLobFlow.Dispose(); activeLobFlow = null; activeLobOrdinal = -1; }
		if (is_SequentialAccess)
		{
			if (m_SequentialSeq < columnIndex) { m_StreamPos = 0; sequentialLobUnitOrdinal = -1; }
			m_SequentialSeq = columnIndex + (lobPartial ? 0 : 1);
		}
	}

	private byte[] GetByteArrayValue(int columnIndex)
	{
		byte[] data = null;
		if ((m_Behavior & CommandBehavior.SingleRow) != 0 && m_is_single_row == 1)
		{
			return null;
		}
		GetByteArrayValue(columnIndex, ref data, lobPartial: true);
		if (m_ColInfo[columnIndex].GetCType() == 1)
		{
			int num = ((data != null) ? data.Length : 0);
			int precision = m_ColInfo[columnIndex].GetPrecision();
			if (num < precision)
			{
				byte[] array = new byte[precision];
				if (num != 0)
				{
					Array.Copy(data, 0, array, 0, num);
				}
				for (int i = num; i < precision; i++)
				{
					array[i] = 32;
				}
				return array;
			}
		}
		return data;
	}

	private void CheckIndex(int i)
	{
		if (i < 0 || i > m_DbInfo.GetColumnCount() - 1)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		}
	}

	private void checkClosed()
	{
		if (do_IsClosed || m_Statement == null || m_Statement.P())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_RESULTSET_CLOSED);
		}
	}

	private void ClearClobs()
	{
		InvalidateLobFlows();
		m_Clobs.Clear();
		for (int i = 0; i < m_DbInfo.GetColumnCount(); i++)
		{
			m_Clobs.Add(null);
		}
	}

	internal DmdbResultSetMetaData getMetaData()
	{
		checkClosed();
		return new DmdbResultSetMetaData(m_Conn.Conn, m_ColInfo, innerMeta: false);
	}
}
