using System;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Execution;

/// <summary>Cancellation belongs to one shortcut call, including its open and reader lifetime.</summary>
internal sealed class DmDataSourceExecution : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource openCancellation = new();
    private readonly CancellationToken callerToken;
    private readonly CancellationToken openToken;
    private CancellationTokenRegistration callerRegistration;
    private DmCommandPlan plan;
    private bool commandCanceled, retired;
    private int cancellationCalls;
    private int openCancelSource;
    internal DmConnection Connection { get; set; }

    internal DmDataSourceExecution(CancellationToken callerToken)
    {
        this.callerToken = callerToken;
        openToken = openCancellation.Token;
        callerRegistration = callerToken.Register(() => RequestCancellation(user: true));
    }

    internal void RequestCancellation() => RequestCancellation(user: false);

    private void RequestCancellation(bool user)
    {
        DmCommandPlan captured;
        lock (gate)
        {
            if (retired) return;
            if (!user) commandCanceled = true;
            Interlocked.CompareExchange(ref openCancelSource, (int)(user ? DmCancelSource.User : DmCancelSource.Command), 0);
            captured = user ? null : plan;
            cancellationCalls++;
        }
        try
        {
            // CTS callbacks and a plan's transport abort may block. Neither runs
            // under a lifecycle/state lock, nor can either target a later call.
            try { openCancellation.Cancel(); }
            finally { captured?.RequestCancellation(); }
        }
        finally
        {
            bool dispose;
            lock (gate) dispose = --cancellationCalls == 0 && retired;
            if (dispose) openCancellation.Dispose();
        }
    }

    internal void BindPlan(DmCommandPlan captured)
    {
        bool cancel;
        lock (gate)
        {
            if (retired) return;
            plan = captured;
            cancel = commandCanceled;
        }
        if (cancel) captured.RequestCancellation();
    }

    internal void Open()
    {
        try { Connection.OpenForDataSource(openToken); }
        catch (Exception error) { throw TranslateOpenFailure(error); }
    }

    internal async Task OpenAsync()
    {
        try { await Connection.OpenAsync(openToken).ConfigureAwait(false); }
        catch (Exception error) { throw TranslateOpenFailure(error); }
    }

    private Exception TranslateOpenFailure(Exception error)
    {
        DmCancelSource source = (DmCancelSource)Volatile.Read(ref openCancelSource);
        if (source == DmCancelSource.None) return error;
        CancellationToken token = source == DmCancelSource.User ? callerToken : openToken;
        if (error is DmOperationCanceledException canceled && canceled.CancellationToken == openToken &&
            canceled.FailureInfo.CancelSource == DmCancelSource.User)
        {
            DmFailureInfo old = canceled.FailureInfo;
            var info = new DmFailureInfo(old.ErrorKind, old.Phase, old.ErrorCode, old.OperationOutcome,
                old.TransactionOutcome, old.ConnectionReusable, source, old.ServerErrorNumber);
            return new DmOperationCanceledException(info, token, canceled);
        }
        // Cancellation before InstallPending has no physical operation or phase.
        if (error is OperationCanceledException early && error is not DmOperationCanceledException &&
            early.CancellationToken == openToken)
            return new DmOperationCanceledException(new DmFailureInfo(DmErrorKind.Canceled, DmFailurePhase.PoolWait,
                "WDM_POOL_WAIT_CANCELED", DmOperationOutcome.NotSent, null, false, source), token, early);
        return error;
    }

    public void Dispose()
    {
        bool dispose;
        lock (gate)
        {
            if (retired) return;
            retired = true;
            plan = null;
            dispose = cancellationCalls == 0;
        }
        // Unregister waits for an already-running caller callback outside gate.
        callerRegistration.Dispose();
        if (dispose) openCancellation.Dispose();
    }
}
