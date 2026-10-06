using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using System.IO;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Pooling;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

public sealed class DmConnection : DbConnection, ICloneable, IFilterInfo
{
	public enum ConnectionStatusInTransactionScope
	{
		Open,
		Closed,
		Disposed
	}

	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static readonly string ClassName = "DmConnection";

	internal DmConnInstance m_ConnInst;

	private volatile bool m_AlreadyDisposed;

	private DmSchema m_Schema;

	private bool forEFCore;

	private volatile ConnectionState connectionState;
	// Tracks the last state actually published through a StateChange event. A
	// deferred close notification must chain from it, never from an unpublished
	// intermediate value such as a deliberately suppressed Broken.
	private ConnectionState lastPublishedState = ConnectionState.Closed;

	private readonly object settingsGate = new object();
	private volatile DmConnectionSettings settings;
	private volatile bool redactCredentials;
	private bool hasExplicitSettings;
	private volatile bool openingInProgress;
	private DmSession session;
	// Shared by all public operation budgets; retained TransactionClock aliases the
	// same provider so existing transaction fixtures do not select a separate clock.
	internal TimeProvider OperationClock { get; set; } = TimeProvider.System;
	internal TimeProvider TransactionClock { get => OperationClock; set => OperationClock = value; }
	private DmInvocation handshakeInvocation;
	private DmPendingOpen pendingOpen;
	private long openGeneration;
	private DmPoolLease poolLease;
	private DmDetachedTransport physicalClose;
	private readonly Dictionary<DmPendingOpen, Action> pendingCloseNotifications = new();
	private DmDataSource dataSourceOwner;
	internal DmPoolOwner PoolOwner { get; private set; }
	internal DmSession Session => Volatile.Read(ref session);
	private void OnSessionBroken(DmSession broken)
	{
		ConnectionState prior;
		lock (settingsGate)
		{
			if (!ReferenceEquals(session, broken) || connectionState is ConnectionState.Broken or ConnectionState.Closed) return;
			prior = connectionState;
			connectionState = ConnectionState.Broken;
			// A creating permit belongs to the outer pending workflow. Notify after
			// its cleanup returns that permit, so a callback can synchronously reopen.
			if (pendingOpen != null && ReferenceEquals(pendingOpen.Session, broken)) return;
		}
		lastPublishedState = ConnectionState.Broken;
		try { OnStateChange(new StateChangeEventArgs(prior, ConnectionState.Broken)); }
		catch { /* A callback must not resurrect a broken transport or hide the protocol failure. */ }
	}
	internal DmExecutionLease BeginExecution(DmOperationPurpose purpose)
		=> BeginExecution(purpose, default, 0);
	internal DmExecutionLease BeginExecution(DmOperationPurpose purpose, DmDeadline deadline, int timeoutSeconds)
	{
		DmSession current = Session;
		if (current == null || do_State != ConnectionState.Open)
			throw new InvalidOperationException("Connection is not open.");
		return current.BeginExecution(purpose, deadline, timeoutSeconds);
	}
	internal DmExecutionLease BeginInternalExecution(DmOperationPurpose purpose)
	{
		if (purpose != DmOperationPurpose.Query || !openingInProgress || Session == null)
			throw new InvalidOperationException("Internal handshake execution is unavailable.");
		return Session.BorrowHandshakeExecution();
	}

	internal DmConnectionSettings Settings => settings;

	private void EnsureConfigurationMutable()
	{
		if (connectionState != ConnectionState.Closed || openingInProgress || m_AlreadyDisposed)
			throw new InvalidOperationException("Connection settings can only be changed while closed.");
	}


	internal static RsLRUCache rsLRUCache;

	internal static object obj = new object();

	internal ConnectionStatusInTransactionScope StatusInTransactionScope;

	public static Assembly DmSkyWalkingAgentAssembly;

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

	internal string do_ServerVersion
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				throw new InvalidOperationException("connection is closed");
			}
			return GetConnInstance().ConnProperty.ServerVersion;
		}
	}

	internal string do_DataSource
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				return null;
			}
			return ConnProperty.Server;
		}
	}

	internal string do_Database
	{
		get
		{
			if (do_State == ConnectionState.Closed)
			{
				return "";
			}
			DmConnInstance connInstance = GetConnInstance();
			if (connInstance == null)
			{
				return ConnProperty.Database;
			}
			return connInstance.ConnProperty.Database;
		}
	}

	internal int do_ConnectionTimeout => settings == null ? 5 :
		settings.ConnectTimeout == TimeSpan.Zero ? 0 :
		checked((int)Math.Ceiling(settings.ConnectTimeout.TotalSeconds));

	internal string do_ConnectionString
	{
		get => !hasExplicitSettings ? string.Empty : settings.ToConnectionString(includeSecrets: !redactCredentials || settings.PersistSecurityInfo);
		set
		{
			lock (settingsGate)
			{
				EnsureConfigurationMutable();
				DmConnectionSettings replacement = DmConnectionSettings.Parse(value);
				if (dataSourceOwner != null && !string.Equals(settings.ToConnectionString(true), replacement.ToConnectionString(true), StringComparison.Ordinal))
					throw new InvalidOperationException("A data source connection has immutable configuration.");
				ConnProperty.BindSettings(replacement);
				settings = replacement;
				hasExplicitSettings = !string.IsNullOrEmpty(value);
				redactCredentials = false;
			}
		}
	}

	internal ConnectionState do_State
	{
		get
		{
			return connectionState;
		}
		set
		{
			ConnectionState originalState;
			lock (settingsGate)
			{
				originalState = connectionState;
				if (originalState == value) return;
				connectionState = value;
			}
			lastPublishedState = value;
			OnStateChange(new StateChangeEventArgs(originalState, value));
		}
	}

	internal DbProviderFactory do_DbProviderFactory => DmClientFactory.Instance;

	public override string ServerVersion
	{
		get
		{
			if (filterHead == null)
			{
				return do_ServerVersion;
			}
			return filterHead.getServerVersion(this);
		}
	}

	public override string DataSource
	{
		get
		{
			if (filterHead == null)
			{
				return do_DataSource;
			}
			return filterHead.getDataSource(this);
		}
	}

	public override string Database
	{
		get
		{
			if (filterHead == null)
			{
				return do_Database;
			}
			return filterHead.getDatabase(this);
		}
	}

	public override int ConnectionTimeout
	{
		get
		{
			if (filterHead == null)
			{
				return do_ConnectionTimeout;
			}
			return filterHead.getConnectionTimeout(this);
		}
	}

	public override string ConnectionString
	{
		get
		{
			if (filterHead == null)
			{
				return do_ConnectionString;
			}
			return filterHead.getConnectionString(this);
		}
		set
		{
			do_ConnectionString = value;
		}
	}

	protected override DbProviderFactory DbProviderFactory
	{
		get
		{
			if (filterHead == null)
			{
				return do_DbProviderFactory;
			}
			return filterHead.getDbProviderFactory(this);
		}
	}

	public override ConnectionState State
	{
		get
		{
			if (filterHead == null)
			{
				return do_State;
			}
			return filterHead.getState(this);
		}
	}

	public DmMppType MppType
	{
		get
		{
			if (1 == ConnProperty.MppType)
			{
				return DmMppType.LOGIN_MPP_LOCAL;
			}
			return DmMppType.LOGIN_MPP_GLOBAL;
		}
		set
		{
			lock (settingsGate) EnsureConfigurationMutable();
			throw new NotSupportedException("MPP routing is unsupported.");

		}
	}

	public string User
	{
		get
		{
			if (ConnProperty == null)
			{
				return null;
			}
			return ConnProperty.User;
		}
	}

	public string Password => redactCredentials && settings?.PersistSecurityInfo != true ? null : settings?.Password;

	internal DmConnProperty ConnProperty { get; set; }

	public string Schema
	{
		get => do_State == ConnectionState.Open ? GetConnInstance().ConnProperty.Schema : settings?.Schema ?? string.Empty;
		set
		{
			lock (settingsGate)
			{
				EnsureConfigurationMutable();
				var replacement = (settings ?? DmConnectionSettings.Parse(string.Empty)).WithSchema(value);
				if (dataSourceOwner != null && !string.Equals(settings.Schema, replacement.Schema, StringComparison.Ordinal))
					throw new InvalidOperationException("A data source connection has immutable configuration.");
				ConnProperty.BindSettings(replacement);
				settings = replacement;
				hasExplicitSettings = true;
			}
		}
	}

	public bool ForEFCore
	{
		get
		{
			return forEFCore;
		}
		set
		{
			lock (settingsGate)
			{
				EnsureConfigurationMutable();
				forEFCore = value;
			}
		}
	}

	public string getConnPoolKey()
	{
		throw new NotSupportedException("Legacy string pool keys are unsupported; use owned connection pooling.");
	}

	public DmConnection()
	{
		settings = DmConnectionSettings.Parse(string.Empty);
		ConnProperty = new DmConnProperty();
		ConnProperty.BindSettings(settings);
	}

	public DmConnection(bool forEF)
		: this()
	{
		ForEFCore = forEF;
	}

	public DmConnection(string connectionString)
		: this()
	{
		do_ConnectionString = connectionString;
	}

	public DmConnection(string connectionString, bool forEFCore)
		: this(forEFCore)
	{
		do_ConnectionString = connectionString;
	}

	internal DmTransaction do_BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
		=> BeginLocalTransactionCore(isolationLevel, profileProbe: false);

	// One explicitly requested validation call, with no persistent capability
	// switch. Public BeginTransaction remains gated by the validated contract.
	internal DmTransaction BeginProfileProbeTransaction(System.Data.IsolationLevel isolationLevel)
		=> BeginLocalTransactionCore(isolationLevel, profileProbe: true);

	internal static bool IsPublicTransactionIsolationSupported(System.Data.IsolationLevel isolationLevel,
		string serverVersion)
		=> (isolationLevel is System.Data.IsolationLevel.Unspecified or System.Data.IsolationLevel.ReadCommitted) ||
			((isolationLevel is System.Data.IsolationLevel.ReadUncommitted or System.Data.IsolationLevel.Serializable) &&
			 string.Equals(serverVersion, "8.1.5.60", StringComparison.Ordinal));

	private DmTransaction BeginLocalTransactionCore(System.Data.IsolationLevel isolationLevel, bool profileProbe)
	{
		int timeoutSeconds = Settings.CommandTimeout;
		TimeSpan cleanupTimeout = Settings.CleanupTimeout;
		using var lease = BeginExecution(DmOperationPurpose.TransactionControl,
			DmDeadline.FromSeconds(timeoutSeconds, TransactionClock), timeoutSeconds);
		DmConnInstance instance;
		lock (settingsGate)
		{
			if (!ReferenceEquals(session, lease.Session) || m_ConnInst == null)
				throw new InvalidOperationException("Connection changed during transaction start.");
			instance = m_ConnInst;
		}
		if (isolationLevel == System.Data.IsolationLevel.Unspecified)
			isolationLevel = System.Data.IsolationLevel.ReadCommitted;
		if (profileProbe)
		{
			if (isolationLevel is not (System.Data.IsolationLevel.ReadCommitted or
				System.Data.IsolationLevel.ReadUncommitted or System.Data.IsolationLevel.Serializable) ||
				!string.Equals(instance.ConnProperty.ServerVersion, "8.1.5.60", StringComparison.Ordinal))
				throw new NotSupportedException("Isolation profile validation is unavailable for this level or server profile.");
		}
		else if (!IsPublicTransactionIsolationSupported(isolationLevel, instance.ConnProperty.ServerVersion))
			throw new NotSupportedException("Local isolation level is unavailable for this server profile.");
		if (instance.Transaction?.Valid == true)
			throw new InvalidOperationException("A local transaction is already active.");
		DmLocalTransactionState priorState = lease.Session.TransactionState;
		DmTransaction priorTransaction = instance.Transaction;
		bool priorAutoCommit = instance.GetAutoCommit();
		var priorIsolation = instance.ConnProperty.IsolationLevel;
		DmCommand isolationOwner = null;
		DmTransaction transaction = null;
		DmInvocation failureInvocation = null;
		DmFailurePhase failurePhase = DmFailurePhase.Prepare;
		try
		{
			using (var configuration = failureInvocation = lease.BeginInvocation())
			{
				configuration.ThrowIfTerminated();
				lease.Session.SetTransactionState(DmLocalTransactionState.Starting);
				transaction = instance.BeginTrx(isolationLevel, out isolationOwner, out bool isolationConfirmed);
				if (!isolationConfirmed || instance.GetAutoCommit())
					throw new InvalidOperationException("Transaction configuration was not confirmed.");
				configuration.Complete();
			}
			if (isolationOwner?.Statement is { } controlStatement)
			{
				// The configuration child has ended. Close the isolated handle under
				// one finite cleanup child of the same root execution, with no new lease.
				failureInvocation = null;
				failurePhase = DmFailurePhase.Cleanup;
				using var cleanup = failureInvocation = lease.BeginCleanupInvocation(
					DmDeadline.Start(cleanupTimeout, lease.Deadline.Clock));
				controlStatement.p();
				cleanup.RecordDiagnosticCleanupSuccess();
			}
			// Cleanup has its own budget so resources can be released after timeout;
			// it must not renew Begin's budget or permit a late Active result.
			failureInvocation = null;
			failurePhase = DmFailurePhase.Prepare;
			using var activation = failureInvocation = lease.BeginInvocation();
			lease.Session.ActivateTransaction(transaction, activation.Identity);
			return transaction;
		}
		catch (Exception error)
		{
			DmTransaction failedTransaction = transaction ?? instance.Transaction;
			DmTransactionOutcome? failureTransactionOutcome;
			if (!lease.SendAttempted && lease.Session.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
			{
				// No configuration request reached its send boundary. Undo only the
				// provisional local state; the old session and transaction remain usable.
				if (!ReferenceEquals(instance.Transaction, priorTransaction))
					instance.Transaction?.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
				instance.Transaction = priorTransaction;
				instance.ConnProperty.AutoCommit = priorAutoCommit;
				instance.ConnProperty.IsolationLevel = priorIsolation;
				lease.Session.SetTransactionState(priorState);
				failureTransactionOutcome = BeginTransactionOutcome(priorTransaction, priorState);
			}
			else
			{
				failedTransaction?.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
				failedTransaction?.RecordFailure("begin_configuration_unconfirmed");
				// Physical abort clears instance.Transaction. Freeze the final result
				// while the captured transaction still belongs to this Begin attempt.
				failureTransactionOutcome = BeginTransactionOutcome(failedTransaction, lease.Session.TransactionState);
				try { CloseExpectedSession(lease.Session); } catch { }
			}
			throw TranslateBeginFailure(error, failureInvocation, failurePhase, lease, failureTransactionOutcome);
		}
		finally
		{
			// Success already closed the handle; on failure the captured session was
			// aborted. Clear any remaining local owner before Dispose, which must not
			// acquire another lease or send a second statement-close request.
			try
			{
				if (isolationOwner?.Statement is { } abandoned)
				{
					instance.RemoveStmt(abandoned);
					abandoned.o();
				}
			}
			finally { isolationOwner?.Dispose(); }
		}
	}

	private async ValueTask<DbTransaction> BeginLocalTransactionCoreAsync(System.Data.IsolationLevel isolationLevel, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		int timeoutSeconds = Settings.CommandTimeout;
		TimeSpan cleanupTimeout = Settings.CleanupTimeout;
		using var lease = BeginExecution(DmOperationPurpose.TransactionControl,
			DmDeadline.FromSeconds(timeoutSeconds, TransactionClock), timeoutSeconds);
		DmConnInstance instance;
		lock (settingsGate)
		{
			if (!ReferenceEquals(session, lease.Session) || m_ConnInst == null)
				throw new InvalidOperationException("Connection changed during transaction start.");
			instance = m_ConnInst;
		}
		if (isolationLevel == System.Data.IsolationLevel.Unspecified)
			isolationLevel = System.Data.IsolationLevel.ReadCommitted;
		if (!IsPublicTransactionIsolationSupported(isolationLevel, instance.ConnProperty.ServerVersion))
			throw new NotSupportedException("Local isolation level is unavailable for this server profile.");
		if (instance.Transaction?.Valid == true)
			throw new InvalidOperationException("A local transaction is already active.");
		DmLocalTransactionState priorState = lease.Session.TransactionState;
		DmTransaction priorTransaction = instance.Transaction;
		bool priorAutoCommit = instance.GetAutoCommit();
		var priorIsolation = instance.ConnProperty.IsolationLevel;
		DmCommand isolationOwner = null;
		DmTransaction transaction = null;
		DmInvocation failureInvocation = null;
		DmFailurePhase failurePhase = DmFailurePhase.Prepare;
		try
		{
			using (var configuration = failureInvocation = lease.BeginInvocation(cancellationToken))
			{
				configuration.ThrowIfTerminated();
				lease.Session.SetTransactionState(DmLocalTransactionState.Starting);
				var configured = await instance.BeginTrxAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
				transaction = configured.Transaction;
				isolationOwner = configured.IsolationOwner;
				bool isolationConfirmed = configured.IsolationConfirmed;
				if (!isolationConfirmed || instance.GetAutoCommit())
					throw new InvalidOperationException("Transaction configuration was not confirmed.");
				configuration.Complete();
			}
			if (isolationOwner?.Statement is { } controlStatement)
			{
				// The configuration child has ended. Close the isolated handle under
				// one finite cleanup child of the same root execution, with no new lease.
				failureInvocation = null;
				failurePhase = DmFailurePhase.Cleanup;
				using var cleanup = failureInvocation = lease.BeginCleanupInvocation(
					DmDeadline.Start(cleanupTimeout, lease.Deadline.Clock));
				await controlStatement.pAsync(CancellationToken.None).ConfigureAwait(false);
				cleanup.RecordDiagnosticCleanupSuccess();
			}
			// Cleanup has its own budget so resources can be released after timeout;
			// it must not renew Begin's budget or permit a late Active result.
			failureInvocation = null;
			failurePhase = DmFailurePhase.Prepare;
			using var activation = failureInvocation = lease.BeginInvocation(cancellationToken);
			lease.Session.ActivateTransaction(transaction, activation.Identity);
			return transaction;
		}
		catch (Exception error)
		{
			DmTransaction failedTransaction = transaction ?? instance.Transaction;
			DmTransactionOutcome? failureTransactionOutcome;
			if (!lease.SendAttempted && lease.Session.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed))
			{
				// No configuration request reached its send boundary. Undo only the
				// provisional local state; the old session and transaction remain usable.
				if (!ReferenceEquals(instance.Transaction, priorTransaction))
					instance.Transaction?.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
				instance.Transaction = priorTransaction;
				instance.ConnProperty.AutoCommit = priorAutoCommit;
				instance.ConnProperty.IsolationLevel = priorIsolation;
				lease.Session.SetTransactionState(priorState);
				failureTransactionOutcome = BeginTransactionOutcome(priorTransaction, priorState);
			}
			else
			{
				failedTransaction?.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
				failedTransaction?.RecordFailure("begin_configuration_unconfirmed");
				// Physical abort clears instance.Transaction. Freeze the final result
				// while the captured transaction still belongs to this Begin attempt.
				failureTransactionOutcome = BeginTransactionOutcome(failedTransaction, lease.Session.TransactionState);
				try { CloseExpectedSession(lease.Session); } catch { }
			}
			throw TranslateBeginFailure(error, failureInvocation, failurePhase, lease, failureTransactionOutcome);
		}
		finally
		{
			// Success already closed the handle; on failure the captured session was
			// aborted. Clear any remaining local owner before Dispose, which must not
			// acquire another lease or send a second statement-close request.
			try
			{
				if (isolationOwner?.Statement is { } abandoned)
				{
					instance.RemoveStmt(abandoned);
					abandoned.o();
				}
			}
			finally
			{
				if (isolationOwner != null) await isolationOwner.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private static DmTransactionOutcome? BeginTransactionOutcome(DmTransaction transaction,
		DmLocalTransactionState state) => transaction?.Outcome ?? (state switch
		{
			DmLocalTransactionState.Starting => DmTransactionOutcome.Starting,
			DmLocalTransactionState.Active => DmTransactionOutcome.Active,
			DmLocalTransactionState.Committing => DmTransactionOutcome.Committing,
			DmLocalTransactionState.Committed => DmTransactionOutcome.Committed,
			DmLocalTransactionState.RollingBack => DmTransactionOutcome.RollingBack,
			DmLocalTransactionState.RolledBack => DmTransactionOutcome.RolledBack,
			DmLocalTransactionState.CompletedExternally => DmTransactionOutcome.CompletedExternally,
			DmLocalTransactionState.OutcomeUnknown => DmTransactionOutcome.OutcomeUnknown,
			_ => (DmTransactionOutcome?)null
		});

	private static Exception TranslateBeginFailure(Exception error, DmInvocation invocation,
		DmFailurePhase phase, DmExecutionLease lease, DmTransactionOutcome? transactionOutcome)
	{
		// A child may already be disposed, or its factory may have failed before
		// assigning it. Never ask a completed configuration child to terminate or
		// infer the winning cause from the caller token's current state.
		DmFailureInfo existing = error switch
		{
			DmOperationCanceledException canceled => canceled.FailureInfo,
			DmException driver => driver.FailureInfo,
			_ => null
		};
		DmFailureInfo captured = invocation?.CreateFailureInfo(error);
		DmCancelSource cause = existing?.CancelSource is { } existingCause && existingCause != DmCancelSource.None
			? existingCause : captured?.CancelSource ?? DmCancelSource.None;
		if (cause == DmCancelSource.None)
			cause = error switch
			{
				TimeoutException => DmCancelSource.TotalDeadline,
				OperationCanceledException => DmCancelSource.User,
				_ => DmCancelSource.None
			};
		DmErrorKind kind = cause is DmCancelSource.TotalDeadline or DmCancelSource.IdleTimeout ? DmErrorKind.Timeout :
			cause is DmCancelSource.User or DmCancelSource.Command ? DmErrorKind.Canceled :
			existing?.ErrorKind ?? captured?.ErrorKind ?? DmErrorKind.Transport;
		// Cleanup is a semantic phase even when its close exchange reached Send
		// or Receive. For configuration, retain the actual protocol failure phase.
		DmFailurePhase actualPhase = phase == DmFailurePhase.Cleanup ? phase :
			invocation?.Phase ?? existing?.Phase ?? phase;
		var info = new DmFailureInfo(kind, actualPhase, kind switch
		{
			DmErrorKind.Timeout => "WDM_TIMEOUT",
			DmErrorKind.Canceled => "WDM_CANCELED",
			_ => existing?.ErrorCode ?? captured?.ErrorCode ?? "WDM_TRANSPORT"
		}, existing?.OperationOutcome ?? captured?.OperationOutcome ?? DmOperationOutcome.NotSent,
			transactionOutcome, lease.Session.State is not (DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed),
			cause, existing?.ServerErrorNumber ?? captured?.ServerErrorNumber);
		if (error is DmOperationCanceledException typedCanceled)
		{
			typedCanceled.SetFailureInfo(info);
			return typedCanceled;
		}
		if (error is DmTimeoutException typedTimeout)
		{
			typedTimeout.SetFailureInfo(info);
			return typedTimeout;
		}
		if (kind == DmErrorKind.Timeout) return new DmTimeoutException(info, error);
		if (kind == DmErrorKind.Canceled)
		{
			CancellationToken token = cause == DmCancelSource.Command ? lease.CommandCancellationToken :
				invocation?.TerminalCause == DmCancelSource.User ? invocation.TerminalToken :
				(error as OperationCanceledException)?.CancellationToken ?? default;
			return new DmOperationCanceledException(info, token, error);
		}
		if (error is DmException driverError) driverError.SetFailureInfo(info);
		return error;
	}

	internal void do_ChangeDatabase(string databaseName)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_DO_NOT_SUPPORT_CATALOG);
	}

	internal void do_Close() => CloseExpectedSession(null);

	internal void CloseExpectedSession(DmSession expected)
	{
		DmDetachedTransport captured;
		DmConnInstance orphan;
		DmSession oldSession;
		DmPendingOpen closingPending;
		DmPoolLease closingLease;
		ConnectionState prior;
		long closedGeneration;
		lock (settingsGate)
		{
			if (expected != null && !ReferenceEquals(session, expected)) return;
			// A repeated logical close must retain the first close's notification
			// identity while its physical abort or pending workflow is still ending.
			if (connectionState == ConnectionState.Closed && session == null && pendingOpen == null &&
				poolLease == null && m_ConnInst == null) return;
			prior = connectionState;
			oldSession = session;
			closingPending = pendingOpen;
			pendingOpen = null;
			if (openGeneration != long.MaxValue) openGeneration++;
			closedGeneration = openGeneration;
			if (closingPending != null && prior != ConnectionState.Closed)
			{
				// The deferred event chains from the last published state; a
				// suppressed unpublished Broken must not become the event's prior.
				ConnectionState published = lastPublishedState;
				pendingCloseNotifications[closingPending] = () => NotifyClosedIfCurrent(published, closedGeneration);
			}
			openingInProgress = false;
			closingLease = poolLease;
			poolLease = null;
			captured = oldSession?.Detach();
			if (captured != null) physicalClose = captured;
			orphan = oldSession == null ? m_ConnInst : null;
			m_ConnInst = null;
			session = null;
			connectionState = ConnectionState.Closed;
		}
		try
		{
			closingLease?.BeginClosing();
			try
			{
				closingPending?.CancelClose(); // Never cancel/dispose registrations under settingsGate.
				if (captured != null) DmSessionTestHooks.BeforeConnectionTransportAbort?.Invoke(oldSession.SessionId);
			}
			finally
			{
				try { captured?.AbortTransport(); }
				finally { orphan?.AbortTransport(); }
			}
		}
		finally
		{
			oldSession?.MarkClosed();
			// Pending workflow retains its own permit until its unpublished candidate
			// has stopped creating. Only an already-published lease completes here.
			if (closingPending == null)
			{
				void CompleteClosed()
				{
					closingLease?.CompleteAfterTransportClosed();
					NotifyClosedIfCurrent(prior, closedGeneration);
				}
				if (captured == null) CompleteClosed();
				else captured.RunAfterClosed(CompleteClosed);
			}
		}
	}

	private void NotifyClosedIfCurrent(ConnectionState prior, long generation)
	{
		if (prior == ConnectionState.Closed) return;
		lock (settingsGate)
		{
			if (openGeneration != generation || connectionState != ConnectionState.Closed || session != null) return;
		}
		lastPublishedState = ConnectionState.Closed;
		OnStateChange(new StateChangeEventArgs(prior, ConnectionState.Closed));
	}

	internal void RunAfterPhysicalClose(Action completion)
	{
		DmDetachedTransport captured;
		lock (settingsGate) captured = physicalClose;
		if (captured == null) completion();
		else captured.RunAfterClosed(completion);
	}

	internal DmCommand do_CreateDbCommand()
	{
		DmCommand dmCommand = CreateDmTracingCommand(this);
		if (dmCommand != null)
		{
			return dmCommand;
		}
		return new DmCommand("", this);
	}

	internal void Reconnect() => throw new NotSupportedException("Automatic reconnect is unsupported.");

	internal void do_EnlistTransaction(Transaction transaction) =>
		throw new NotSupportedException("Transaction enlistment is unsupported.");

	internal bool FindExistingEnlistedConnInstance(DmPromotableTransaction dmPromotableTransaction)
	{
		foreach (DmTransactionScope item in dmPromotableTransaction.ScopeQueue)
		{
			DmConnection connection = item.Connection;
			if (connection.ConnectionString == ConnectionString)
			{
				Close();
				m_ConnInst = connection.m_ConnInst;
				m_ConnInst.SetDmConnection(this);
				do_State = ConnectionState.Open;
				item.Connection = this;
				return true;
			}
		}
		return false;
	}

	internal DataTable do_GetSchema()
	{
		return do_GetSchema(null);
	}

	internal DataTable do_GetSchema(string collectionName)
	{
		if (collectionName == null || collectionName.Equals(""))
		{
			collectionName = "METADATACOLLECTIONS";
		}
		return do_GetSchema(collectionName, null);
	}

	internal DataTable do_GetSchema(string collectionName, string[] restrictionValues)
	{
		using var lease = BeginExecution(DmOperationPurpose.Metadata);
		if (collectionName == null || collectionName.Equals(""))
		{
			collectionName = "METADATACOLLECTIONS";
		}
		return new DmSchema(this, lease).GetSchema(collectionName, restrictionValues);
	}

	internal void do_Open()
	{
		DmSession openingSession = Session;
		if (do_State != ConnectionState.Connecting || openingSession == null)
			throw new InvalidOperationException("Connection must be connecting.");
		CheckProperty();
		ConnProperty.encryptPwd = false;
		ConnProperty.encryptMsg = false;
		ConnProperty.msgVersion = 21;
		CheckProperty();
		DmConnInstance opened = new DmConnInstance(this);
		DmDeadline handshakeDeadline = DmInvocation.Current?.Deadline ??
			throw new InvalidOperationException("Handshake invocation is missing.");
		opened.Open(handshakeDeadline);
		handshakeDeadline.ThrowIfExpired();
		bool rejected;
		lock (settingsGate)
		{
			rejected = !ReferenceEquals(session, openingSession) || connectionState != ConnectionState.Connecting;
			if (!rejected)
			{
				m_ConnInst = opened;
				redactCredentials = true;
				connectionState = ConnectionState.Open;
			}
		}
		if (rejected)
		{
			opened.AbortTransport();
			throw new InvalidOperationException("Connection was closed during handshake.");
		}
		openingSession.BeginAuthenticating();
		// Schema setup is part of the handshake ownership interval.
		handshakeInvocation?.Complete();
		handshakeInvocation?.Dispose();
		handshakeInvocation = null;
		DriverUtil.executeSetSchema(this);
	}

	internal async Task do_OpenAsync(CancellationToken cancellationToken)
	{
		DmSession openingSession = Session;
		if (do_State != ConnectionState.Connecting || openingSession == null)
			throw new InvalidOperationException("Connection must be connecting.");
		CheckProperty();
		ConnProperty.encryptPwd = false;
		ConnProperty.encryptMsg = false;
		ConnProperty.msgVersion = 21;
		CheckProperty();
		DmConnInstance opened = new DmConnInstance(this);
		DmDeadline handshakeDeadline = DmInvocation.Current?.Deadline ??
			throw new InvalidOperationException("Handshake invocation is missing.");
		await opened.OpenAsync(handshakeDeadline, cancellationToken).ConfigureAwait(false);
		DmInvocation.Current.ThrowIfTerminated();
		bool rejected;
		lock (settingsGate)
		{
			rejected = !ReferenceEquals(session, openingSession) || connectionState != ConnectionState.Connecting;
			if (!rejected)
			{
				m_ConnInst = opened;
				redactCredentials = true;
				connectionState = ConnectionState.Open;
			}
		}
		if (rejected)
		{
			opened.AbortTransport();
			throw new InvalidOperationException("Connection was closed during handshake.");
		}
		openingSession.BeginAuthenticating();
		// Schema setup is part of the handshake ownership interval.
		handshakeInvocation?.Complete();
		handshakeInvocation?.Dispose();
		handshakeInvocation = null;
		await ExecuteSetSchemaAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task ExecuteSetSchemaAsync(CancellationToken cancellationToken)
	{
		string statement = DriverUtil.FormatSchemaStatement(ConnProperty.Schema);
		if (statement == null) return;
		DmConnInstance instance = GetConnInstance();
		using var borrowed = BeginInternalExecution(DmOperationPurpose.Query);
		instance.ConnProperty.AutoCommit = true;
		try
		{
			var command = CreateCommand(statement);
			await using (command.ConfigureAwait(false))
				await command.ExecuteInternalNonQueryAsync(borrowed, cancellationToken).ConfigureAwait(false);
		}
		finally { instance.ConnProperty.ClearAutoCommit(); }
	}

	internal DmStruct do_CreateStruct(string typeName, object[] attributes)
	{
		throw new NotSupportedException("Complex type metadata is not supported by the owned session path.");
	}

	internal DmArray do_CreateArray(string typeName, object[] elements)
	{
		throw new NotSupportedException("Complex type metadata is not supported by the owned session path.");
	}

	internal DmStruct do_CreateIndexTable(string typeName, Dictionary<string, object> dictionary)
	{
		throw new NotSupportedException("Complex type metadata is not supported by the owned session path.");
	}

	protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
	{
		if (filterHead == null)
		{
			return do_BeginDbTransaction(isolationLevel);
		}
		return filterHead.BeginDbTransaction(this, isolationLevel);
	}

	public override void ChangeDatabase(string databaseName)
	{
		if (filterHead == null)
		{
			do_ChangeDatabase(databaseName);
		}
		else
		{
			filterHead.ChangeDatabase(this, databaseName);
		}
	}

	public override void Close()
	{
		do_Close();
	}

	protected override ValueTask<DbTransaction> BeginDbTransactionAsync(System.Data.IsolationLevel isolationLevel,
		CancellationToken cancellationToken)
		=> BeginLocalTransactionCoreAsync(isolationLevel, cancellationToken);

	public override Task OpenAsync(CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
		if (m_AlreadyDisposed) return Task.FromException(new ObjectDisposedException(nameof(DmConnection)));
		return ConnectAsync(cancellationToken);
	}

	// Closing is a local detach/abort. It sends no logout, rollback, or cursor I/O.
	public override Task CloseAsync() => CloseExpectedSessionAsync(null);

	internal Task CloseExpectedSessionAsync(DmSession expected)
	{
		try { CloseExpectedSession(expected); return Task.CompletedTask; }
		catch (Exception exception) { return Task.FromException(exception); }
	}

	public override async ValueTask DisposeAsync()
	{
		lock (settingsGate)
		{
			if (m_AlreadyDisposed) return;
			m_AlreadyDisposed = true;
		}
		try { await CloseAsync().ConfigureAwait(false); }
		finally
		{
			base.Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}

	public override Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
		=> cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) :
			Task.FromException(new NotSupportedException("Physical database switching is unsupported."));

	public override Task<DataTable> GetSchemaAsync(CancellationToken cancellationToken = default)
		=> UnsupportedSchemaAsync(cancellationToken);

	public override Task<DataTable> GetSchemaAsync(string collectionName, CancellationToken cancellationToken = default)
		=> UnsupportedSchemaAsync(cancellationToken);

	public override Task<DataTable> GetSchemaAsync(string collectionName, string[] restrictionValues,
		CancellationToken cancellationToken = default)
		=> UnsupportedSchemaAsync(cancellationToken);

	private static Task<DataTable> UnsupportedSchemaAsync(CancellationToken cancellationToken)
		=> cancellationToken.IsCancellationRequested ? Task.FromCanceled<DataTable>(cancellationToken) :
			Task.FromException<DataTable>(new NotSupportedException("Asynchronous schema discovery is unsupported."));

	public void ForceClose()
	{
		do_Close();
	}

	protected override DbCommand CreateDbCommand()
	{
		if (filterHead == null)
		{
			return do_CreateDbCommand();
		}
		return filterHead.CreateDbCommand(this);
	}

	public DmCommand CreateCommand(string cmdText)
	{
		DmCommand dmCommand = do_CreateDbCommand();
		dmCommand.CommandText = cmdText;
		return dmCommand;
	}

	public override void EnlistTransaction(Transaction transaction) =>
		throw new NotSupportedException("Transaction enlistment is unsupported.");

	public override DataTable GetSchema()
	{
		if (filterHead == null)
		{
			return do_GetSchema();
		}
		return filterHead.GetSchema(this);
	}

	public override DataTable GetSchema(string collectionName)
	{
		if (filterHead == null)
		{
			return do_GetSchema(collectionName);
		}
		return filterHead.GetSchema(this, collectionName);
	}

	public override DataTable GetSchema(string collectionName, string[] restrictionValues)
	{
		if (filterHead == null)
		{
			return do_GetSchema(collectionName, restrictionValues);
		}
		return filterHead.GetSchema(this, collectionName, restrictionValues);
	}

	public override void Open()
	{
		if (m_AlreadyDisposed)
			throw new ObjectDisposedException("DmConnection");
		Connect();
	}

	internal void Connect() => ConnectCoreAsync(false, CancellationToken.None).GetAwaiter().GetResult();

	internal void OpenForDataSource(CancellationToken cancellationToken)
	{
		if (m_AlreadyDisposed) throw new ObjectDisposedException(nameof(DmConnection));
		ConnectCoreAsync(false, cancellationToken).GetAwaiter().GetResult();
	}

	private Task ConnectAsync(CancellationToken cancellationToken) => ConnectCoreAsync(true, cancellationToken);

	private DmPendingOpen InstallPending(CancellationToken userToken)
	{
		lock (settingsGate)
		{
			if (m_AlreadyDisposed) throw new ObjectDisposedException(nameof(DmConnection));
			if (openingInProgress) throw new InvalidOperationException("Connection is opening.");
			if (connectionState == ConnectionState.Open) return null;
			EnsureConfigurationMutable();
			CheckProperty();
			dataSourceOwner?.ThrowIfDisposed();
			long nextGeneration = checked(openGeneration + 1);
			var physical = new DmSession(OnSessionBroken);
			physical.BeginConnecting();
			// Each opening has separate mutable handshake/codec configuration. A
			// closed old handshake cannot modify a subsequent logical connection.
			var candidate = new DmConnection
			{
				settings = settings, hasExplicitSettings = true, session = physical,
				connectionState = ConnectionState.Connecting, openingInProgress = true,
				OperationClock = OperationClock, forEFCore = forEFCore
			};
			candidate.ConnProperty.BindSettings(settings);
			var pending = new DmPendingOpen(nextGeneration, settings, physical, candidate, userToken,
				settings.Pooling ? DmDeadline.Start(settings.PoolAcquireTimeout, OperationClock) : DmDeadline.Infinite);
			pending.Owner = dataSourceOwner?.Owner;
			openGeneration = nextGeneration;
			pendingOpen = pending;
			openingInProgress = true;
			session = physical;
			connectionState = ConnectionState.Connecting;
			return pending;
		}
	}

	private void RequireCurrentPending(DmPendingOpen pending)
	{
		pending.UserToken.ThrowIfCancellationRequested();
		lock (settingsGate)
		{
			if (!ReferenceEquals(pendingOpen, pending) || openGeneration != pending.Generation ||
				connectionState != ConnectionState.Connecting || m_AlreadyDisposed || pending.LifetimeToken.IsCancellationRequested)
				throw new InvalidOperationException("Connection was closed during opening.");
		}
	}

	private void PublishPending(DmPendingOpen pending)
	{
		void Publish()
		{
			lock (settingsGate)
			{
				RequireCurrentPending(pending);
				pending.PoolDeadline.ThrowIfExpired();
				pending.PoolLease?.MarkLeased();
				DmConnInstance instance = pending.Candidate.m_ConnInst ?? throw new InvalidOperationException("Handshake did not create a physical connection.");
				instance.SetDmConnection(this);
				m_ConnInst = instance;
				ConnProperty = pending.Candidate.ConnProperty;
				poolLease = pending.PoolLease;
				PoolOwner = pending.Owner;
				redactCredentials = true;
				connectionState = ConnectionState.Open;
				openingInProgress = false;
				pendingOpen = null;
				pending.Published = true;
				// Logical ownership transfer only: disposing the context must not
				// abort a session which has just been delivered to this connection.
				pending.Candidate.m_ConnInst = null;
				pending.Candidate.session = null;
				pending.Candidate.connectionState = ConnectionState.Closed;
				pending.Candidate.openingInProgress = false;
			}
		}
		if (dataSourceOwner != null) dataSourceOwner.Publish(Publish); else Publish();
	}

	private async Task ConnectCoreAsync(bool asynchronous, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DmPendingOpen pending = InstallPending(cancellationToken);
		if (pending == null) return;
		DmExecutionLease execution = null;
		DmInvocation invocation = null;
		DmDiagnosticStamp? connectDiagnostic = null;
		DmDiagnosticResult connectResult = DmDiagnosticResult.Rejected;
		bool connectFailed = false;
		DmDetachedTransport failedTransport = null;
		ConnectionState pendingFailurePrior = ConnectionState.Closed;
		long pendingFailureGeneration = 0;
		try
		{
			OnStateChange(new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Connecting));
			lastPublishedState = ConnectionState.Connecting;
			RequireCurrentPending(pending);
			if (pending.Settings.Pooling)
			{
				if (pending.Owner == null)
				{
					pending.OwnerReference = DmPoolRegistry.Shared.GetOrCreate(pending.Settings);
					pending.Owner = pending.OwnerReference.Owner;
				}
				pending.PoolLease = asynchronous
					? await pending.Owner.AcquireAsync(pending.PoolDeadline, pending.LifetimeToken).ConfigureAwait(false)
					: pending.Owner.Acquire(pending.PoolDeadline, pending.LifetimeToken);
			}
			RequireCurrentPending(pending);
			pending.PoolDeadline.ThrowIfExpired();
			DmPendingOpenTestHooks.AfterCapacityAcquired?.Invoke(this);
			RequireCurrentPending(pending);
			connectDiagnostic = DmDiagnosticsCore.Start();
			execution = pending.Session.BeginExecution(DmOperationPurpose.Handshake, pending.HandshakeDeadline(OperationClock));
			// One logical connect owns auth, schema setup, cleanup and publication.
			// Borrowed handshake children keep all coordination but delegate only
			// their Connect diagnostics to this outer result.
			execution.OuterOwnsConnectDiagnostic = true;
			invocation = execution.BeginInvocation(pending.UserToken);
			pending.Candidate.handshakeInvocation = invocation;
			pending.HandshakeStarted = true;
			var hook = DmPendingOpenTestHooks.Handshake;
			if (hook != null)
			{
				if (asynchronous) await hook(pending.Candidate, true, pending.UserToken).ConfigureAwait(false);
				else hook(pending.Candidate, false, pending.UserToken).GetAwaiter().GetResult();
			}
			else if (asynchronous)
				await pending.Candidate.ConnProperty.EPGroup.connectAsync(pending.Candidate, pending.UserToken).ConfigureAwait(false);
			else pending.Candidate.ConnProperty.EPGroup.connect(pending.Candidate);
			pending.HandshakeSendAttempted = invocation.SendAttempted;
			invocation.Dispose();
			invocation = execution.BeginInvocation(pending.UserToken);
			invocation.ThrowIfTerminated();
			pending.Session.CompleteHandshake();
			pending.HandshakeAcknowledged = true;
			invocation.Complete();
			invocation.Dispose();
			invocation = null;
			execution.Dispose();
			execution = null;
			pending.Candidate.handshakeInvocation = null;
			DmPendingOpenTestHooks.BeforePublish?.Invoke(this);
			PublishPending(pending);
			// There are no state writes after user callbacks. Close/reopen from an
			// Open notification therefore keeps the replacement generation intact.
			OnStateChange(new StateChangeEventArgs(ConnectionState.Connecting, ConnectionState.Open));
			lastPublishedState = ConnectionState.Open;
			connectResult = DmDiagnosticResult.Success;
		}
		catch (Exception error)
		{
			// A raw canceled handshake may win its continuation before the
			// invocation's cancellation callback. Preserve actual send evidence
			// before translation or cleanup can retire the captured invocation.
			pending.HandshakeSendAttempted |= invocation?.SendAttempted == true || execution?.SendAttempted == true;
			if (error is OperationCanceledException rawCancellation && error is not DmOperationCanceledException &&
				(rawCancellation.CancellationToken == pending.UserToken || pending.UserToken.IsCancellationRequested))
				pending.ObserveUserCancellation();
			Exception translated = invocation?.TranslateFailure(error) ?? error;
			if (translated is DmOperationCanceledException canceled && canceled.CancellationToken == pending.LifetimeToken)
			{
				translated = pending.UserCancellationWon
					? new DmOperationCanceledException(canceled.FailureInfo, pending.UserToken, canceled)
					: new InvalidOperationException("Connection was closed during opening.");
			}
			else if (translated is OperationCanceledException && translated is not DmOperationCanceledException)
			{
				if (pending.CloseCancellationWon)
					translated = new InvalidOperationException("Connection was closed during opening.");
				else if (pending.UserCancellationWon)
					translated = new DmOperationCanceledException(new DmFailureInfo(DmErrorKind.Canceled,
						pending.HandshakeStarted ? DmFailurePhase.Connect : DmFailurePhase.PoolWait,
						pending.HandshakeStarted ? "WDM_CONNECT_CANCELED" : "WDM_POOL_WAIT_CANCELED",
						pending.HandshakeAcknowledged ? DmOperationOutcome.ServerReported :
						pending.HandshakeSendAttempted ? DmOperationOutcome.Unknown : DmOperationOutcome.NotSent,
						null, false, DmCancelSource.User), pending.UserToken, error);
			}
			else if (error is TimeoutException && translated is not DmTimeoutException)
				translated = new DmTimeoutException(new DmFailureInfo(DmErrorKind.Timeout,
					pending.HandshakeStarted ? DmFailurePhase.Connect : DmFailurePhase.PoolWait,
					pending.HandshakeStarted ? "WDM_CONNECT_TIMEOUT" : "WDM_POOL_WAIT_TIMEOUT",
					pending.HandshakeAcknowledged ? DmOperationOutcome.ServerReported :
					pending.HandshakeSendAttempted ? DmOperationOutcome.Unknown : DmOperationOutcome.NotSent,
					null, false, DmCancelSource.TotalDeadline), error);
			connectFailed = true;
			connectResult = ClassifyConnectDiagnostic(translated);
			try { invocation?.Dispose(); } catch { }
			try { execution?.Dispose(); } catch { }
			if (pending.Published)
			{
				try { CloseExpectedSession(pending.Session); } catch { }
			}
			else
			{
				failedTransport = pending.Session.Detach();
				lock (settingsGate)
				{
					if (ReferenceEquals(pendingOpen, pending))
					{
						pendingOpen = null;
						openingInProgress = false;
						pendingFailurePrior = lastPublishedState;
						pendingFailureGeneration = openGeneration;
						connectionState = ConnectionState.Closed;
						physicalClose = failedTransport;
						session = null;
					}
				}
				pending.PoolLease?.BeginClosing();
				try { pending.Candidate.CloseExpectedSession(pending.Session); } catch { }
			}
			throw translated;
		}
		finally
		{
			try
			{
				Exception cleanupFailure = null;
				try { pending.Dispose(); } catch (Exception error) { cleanupFailure = error; }
				try { pending.Candidate.Dispose(); } catch (Exception error) { cleanupFailure ??= error; }
				if (connectFailed && !pending.Published)
				{
					Action publicCloseNotification;
					lock (settingsGate)
					{
						pendingCloseNotifications.Remove(pending, out publicCloseNotification);
					}
					void CompletePendingFailure()
					{
						pending.PoolLease?.CompleteAfterTransportClosed();
						try
						{
							if (publicCloseNotification != null) publicCloseNotification();
							else NotifyClosedIfCurrent(pendingFailurePrior, pendingFailureGeneration);
						}
						catch { /* Preserve the original opening failure after notification. */ }
					}
					if (failedTransport == null) CompletePendingFailure();
					else failedTransport.RunAfterClosed(CompletePendingFailure);
				}
				// Preserve the original translated failure, while still attempting
				// both releases. A standalone cleanup failure remains observable.
				if (!connectFailed && cleanupFailure != null)
				{
					connectResult = ClassifyConnectDiagnostic(cleanupFailure);
					ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
				}
			}
			finally
			{
				if (connectDiagnostic.HasValue)
					DmDiagnosticsCore.CompleteOperation(connectDiagnostic.Value, DmDiagnosticOperation.Connect, connectResult);
			}
		}
	}

	private static DmDiagnosticResult ClassifyConnectDiagnostic(Exception error) =>
		error is IOException or SocketException ? DmDiagnosticResult.TransportError : DmDiagnosticsCore.Classify(error);

	internal DmConnection(DmConnectionSettings frozen, DmDataSource source) : this()
	{
		settings = frozen;
		hasExplicitSettings = true;
		redactCredentials = true;
		dataSourceOwner = source;
		PoolOwner = source.Owner;
		ConnProperty.BindSettings(frozen);
	}

	internal int getIndexOnDBGroup()
	{
		if (ConnProperty.EPGroup == null || ConnProperty.EPGroup.epList == null)
		{
			return -1;
		}
		EPGroup ePGroup = ConnProperty.EPGroup;
		for (int i = 0; i < ePGroup.epList.Count; i++)
		{
			EP eP = ePGroup.epList[i];
			if (ConnProperty.Server.Equals(eP.host, StringComparison.OrdinalIgnoreCase) && ConnProperty.Port == eP.port)
			{
				return i;
			}
		}
		return -1;
	}

	public bool getConnPooling()
	{
		return settings.Pooling;
	}

	public void ClearAllPools(bool pooled) => ClearAllPools();

	public static void ClearAllPools() => DmPoolRegistry.Shared.ClearAll();

	public static void ClearPool(DmConnection connection)
	{
		if (connection == null) throw new ArgumentNullException(nameof(connection));
		if (connection.dataSourceOwner != null) connection.dataSourceOwner.ClearPool();
		else DmPoolRegistry.Shared.Clear(connection.settings);
	}

	internal void ReleaseUnmanagedResource(bool pooled)
	{
		do_Close();
	}

	internal void ReleaseUnmanagedResource(bool pooled, StringBuilder msg)
	{
		do_Close();
	}

	protected override void Dispose(bool disposing)
	{
		lock (settingsGate)
		{
			if (m_AlreadyDisposed) return;
			m_AlreadyDisposed = true;
		}
		try
		{
			Close();
		}
		finally
		{
			base.Dispose(disposing);
		}
	}

	protected void ForceDispose()
	{
		lock (settingsGate)
		{
			if (m_AlreadyDisposed) return;
			m_AlreadyDisposed = true;
		}
		try
		{
			ForceClose();
		}
		finally
		{
			base.Dispose(disposing: false);
		}
	}

	public void SetDatabase(string db) =>
		throw new NotSupportedException("Physical database switching is unsupported.");

	public DmTransaction BeginTransaction(System.Data.IsolationLevel il, bool for_ef)
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "BeginTransaction(IsolationLevel il)");
		if (do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNCTION_NOT_OPENED);
		}
		return do_BeginDbTransaction(il);
	}

	internal DmConnInstance GetConnInstance()
	{
		return m_ConnInst;
	}

	internal void CheckProperty()
	{
		if (settings == null || string.IsNullOrWhiteSpace(settings.Host))
			throw new InvalidOperationException("A server is required.");
		if (string.IsNullOrWhiteSpace(settings.User) || string.IsNullOrEmpty(settings.Password))
			throw new InvalidOperationException("User and password are required.");
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	public DmConnection Clone()
	{
		DmTrace.TraceMethodEnter(TraceLevel.Debug, ClassName, "Clone()");
		var clone = dataSourceOwner != null ? dataSourceOwner.CreateConnection() : hasExplicitSettings ?
			new DmConnection(settings.ToConnectionString(includeSecrets: true), forEFCore) :
			new DmConnection(forEFCore);
		clone.redactCredentials = redactCredentials;
		return clone;
	}

	public bool compatibleOracle()
	{
		return (ConnProperty.CompatibleMode & CompatibleMode.ORACLE) != 0;
	}

	public bool compatibleMysql()
	{
		return ConnProperty.CompatibleMode == CompatibleMode.MYSQL;
	}

	public void checkClosed()
	{
		if (do_State == ConnectionState.Closed)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_CONNECTION_CLOSED);
		}
	}

	public DmStruct CreateStruct(string typeName, object[] attributes)
	{
		if (filterHead == null)
		{
			return do_CreateStruct(typeName, attributes);
		}
		return filterHead.CreateStruct(this, typeName, attributes);
	}

	public DmArray CreateArray(string typeName, object[] elements)
	{
		if (filterHead == null)
		{
			return do_CreateArray(typeName, elements);
		}
		return filterHead.CreateArray(this, typeName, elements);
	}

	public DmStruct CreateIndexTable(string typeName, Dictionary<string, object> dictionary)
	{
		if (filterHead == null)
		{
			return do_CreateIndexTable(typeName, dictionary);
		}
		return filterHead.CreateIndexTable(this, typeName, dictionary);
	}

	public static DmCommand CreateDmTracingCommand(DmConnection conn)
	{
		if (!conn.ConnProperty.UseSkyWalking)
		{
			return null;
		}
		if (DmSkyWalkingAgentAssembly == null)
		{
			DmSkyWalkingAgentAssembly = Assembly.Load("DmSkyWalkingAgent");
		}
		return (DmCommand)DmSkyWalkingAgentAssembly.CreateInstance("DmSkyWalkingAgent.DmTracingCommand", ignoreCase: false, BindingFlags.Instance | BindingFlags.Public, null, new object[2] { "", conn }, null, null);
	}

	internal string getFormat(DmField column)
	{
		switch (column.GetCType())
		{
		case 14:
			return GetConnInstance().ConnProperty.oracleDateFormat;
		case 15:
			return GetConnInstance().ConnProperty.oracleTimeFormat;
		case 22:
			return GetConnInstance().ConnProperty.oracleTimeTZFormat;
		case 16:
		case 26:
			return GetConnInstance().ConnProperty.oracleTimestampFormat;
		case 23:
		case 27:
			return GetConnInstance().ConnProperty.oracleTimestampTZFormat;
		default:
			return "";
		}
	}

	public bool getClobAsString()
	{
		return ConnProperty.ClobAsString;
	}

	public bool isColumnNameUpperCase()
	{
		return ConnProperty.ColumnNameCase == ColumnNameCase.UPPER;
	}

	public bool isColumnNameLowerCase()
	{
		return ConnProperty.ColumnNameCase == ColumnNameCase.LOWER;
	}

	public bool lobFetchAll()
	{
		return ConnProperty.LobMode == 2;
	}

	internal Fldr getFldrInstance() => throw new NotSupportedException("FLDR is unsupported.");

	public FldrStatement fldrStatement(FldrConfig config) =>
		throw new NotSupportedException("FLDR is unsupported.");


	public FldrStatement do_fldrStatement(FldrConfig config) =>
		throw new NotSupportedException("FLDR is unsupported.");

	public bool ExistDmPromotableTransaction()
	{
		return m_ConnInst?.CurrentDmPromotableTransaction != null;
	}
}
