using System;
using System.Threading;

namespace W.Dm;

/// <summary>Stable, credential-free information about an operation failure.</summary>
public sealed class DmFailureInfo
{
    public DmErrorKind ErrorKind { get; }
    public DmFailurePhase Phase { get; }
    public string ErrorCode { get; }
    public DmOperationOutcome OperationOutcome { get; }
    public DmTransactionOutcome? TransactionOutcome { get; }
    public bool ConnectionReusable { get; }
    public DmCancelSource CancelSource { get; }
    public int? ServerErrorNumber { get; }
    internal DmFailureInfo WithCommitUnknown() => new(DmErrorKind.OutcomeUnknown, DmFailurePhase.Commit,
        "WDM_COMMIT_UNKNOWN", DmOperationOutcome.Unknown, DmTransactionOutcome.OutcomeUnknown, false, CancelSource, ServerErrorNumber);

    internal DmFailureInfo(DmErrorKind errorKind, DmFailurePhase phase, string errorCode,
        DmOperationOutcome outcome, DmTransactionOutcome? transactionOutcome, bool reusable,
        DmCancelSource cancelSource = DmCancelSource.None, int? serverErrorNumber = null)
    {
        ErrorKind = errorKind; Phase = phase; ErrorCode = errorCode; OperationOutcome = outcome;
        TransactionOutcome = transactionOutcome; ConnectionReusable = reusable;
        CancelSource = cancelSource; ServerErrorNumber = serverErrorNumber;
    }
}

public enum DmErrorKind { Unknown, Timeout, Canceled, Transport, Server, OutcomeUnknown }
public enum DmFailurePhase { Unknown, Dns, Connect, Authenticate, Prepare, Send, Receive, Fetch, Commit, Rollback, Cleanup, PoolWait }
public enum DmOperationOutcome { NotSent, ServerReported, Unknown }
public enum DmCancelSource { None, User, Command, TotalDeadline, IdleTimeout }

/// <summary>A client deadline expired. Number remains zero because no server error is invented.</summary>
public sealed class DmTimeoutException : DmException
{
    internal DmTimeoutException(DmFailureInfo info, Exception inner = null)
        : base("The operation deadline expired.", inner) { SetFailureInfo(info); }
    public override bool IsTransient => false;
}

/// <summary>A canceled operation with its original token and stable failure metadata.</summary>
public sealed class DmOperationCanceledException : OperationCanceledException
{
    private DmFailureInfo failureInfo;
    public DmFailureInfo FailureInfo => Volatile.Read(ref failureInfo);
    internal void SetFailureInfo(DmFailureInfo info) => Volatile.Write(ref failureInfo, info);
    internal DmOperationCanceledException(DmFailureInfo info, CancellationToken token, Exception inner = null)
        : base("The operation was canceled.", inner, token) { failureInfo = info; }
}
