using System;

namespace W.Dm.Internal.Sessions;

internal readonly record struct OperationIdentity(long SessionId, long LeaseGeneration, long ExecutionId, long InvocationId);

internal enum DmOperationPurpose { Handshake, Query, Reader, TransactionControl, Metadata, Lob }
internal enum DmPhysicalSessionState { New, Connecting, Authenticating, Ready, Busy, Resetting, Broken, Closed }
internal enum DmLocalTransactionState { None, Starting, Active, Committing, Committed, RollingBack, RolledBack, CompletedExternally, OutcomeUnknown }
internal enum DmTransactionControlKind { Commit, Rollback }
