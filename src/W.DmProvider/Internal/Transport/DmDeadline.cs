using System;
using System.Threading;
using W.Dm.Internal.Sessions;

namespace W.Dm.Internal.Transport;

/// <summary>A single monotonic budget shared by every stage of one operation.</summary>
internal readonly struct DmDeadline
{
    private readonly TimeProvider clock;
    private readonly long startedAt;
    private readonly TimeSpan budget;

    private DmDeadline(TimeProvider clock, TimeSpan budget)
    {
        this.clock = clock;
        this.budget = budget;
        startedAt = clock.GetTimestamp();
    }

    internal static DmDeadline Infinite => default;
    internal bool IsInfinite => clock == null || budget == TimeSpan.Zero;
    internal TimeProvider Clock => clock ?? TimeProvider.System;

    internal static DmDeadline Start(TimeSpan timeout, TimeProvider provider = null)
    {
        if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        // An unlimited command budget still owns its explicitly selected clock.
        // Its finite cleanup children must use that clock without inheriting an
        // unlimited timeout. The default infinite value continues to use System.
        return timeout == TimeSpan.Zero && provider == null
            ? Infinite
            : new DmDeadline(provider ?? TimeProvider.System, timeout);
    }

    internal static DmDeadline FromMilliseconds(int milliseconds, TimeProvider provider = null)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return Start(TimeSpan.FromMilliseconds(milliseconds), provider);
    }

    internal static DmDeadline FromSeconds(int seconds, TimeProvider provider = null)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        return Start(TimeSpan.FromSeconds(seconds), provider);
    }

    internal DmDeadline RenewBudget(TimeSpan timeout) => Start(timeout, Clock);

    /// <summary>Milliseconds left, rounded up to avoid converting a live budget to zero.</summary>
    internal int RemainingMilliseconds
    {
        get
        {
            if (IsInfinite) return 0; // Socket timeout convention: zero means infinite.
            TimeSpan remaining = budget - clock.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("The operation deadline has expired.");
            double milliseconds = Math.Ceiling(remaining.TotalMilliseconds);
            return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
        }
    }

    internal TimeSpan RemainingTime
    {
        get
        {
            if (IsInfinite) return Timeout.InfiniteTimeSpan;
            TimeSpan remaining = budget - Clock.GetElapsedTime(startedAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    internal void ThrowIfExpired()
    {
        if (!IsInfinite && budget - clock.GetElapsedTime(startedAt) <= TimeSpan.Zero)
            throw new TimeoutException("The operation deadline has expired.");
    }
}

/// <summary>Schedules bounded timer slices while preserving an arbitrarily long absolute budget.</summary>
internal sealed class DmDeadlineTimer : IDisposable
{
    private readonly DmDeadline deadline;
    private readonly Action expired;
    private readonly ITimer timer;
    private int disposed;

    internal DmDeadlineTimer(DmDeadline deadline, Action expired)
    {
        if (deadline.IsInfinite) throw new ArgumentException("An infinite deadline has no timer.", nameof(deadline));
        this.deadline = deadline;
        this.expired = expired ?? throw new ArgumentNullException(nameof(expired));
        // Construct inertly: an immediate callback must never observe an
        // unassigned timer field when it tries to rearm an early timer tick.
        timer = deadline.Clock.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try { timer.Change(Slice(), Timeout.InfiniteTimeSpan); }
        catch { timer.Dispose(); throw; }
    }

    private TimeSpan Slice()
    {
        TimeSpan remaining = deadline.RemainingTime;
        if (remaining == TimeSpan.Zero) return TimeSpan.Zero;
        // System timers have millisecond granularity. A positive sub-ms budget
        // must not become a repeating zero-due callback before its true deadline.
        double milliseconds = Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds));
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, int.MaxValue));
    }

    private void Tick()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (deadline.RemainingTime == TimeSpan.Zero) expired();
        else
        {
            // A timer's dueTime has a much smaller range than a public timeout.
            // Reaching a bounded slice is progress, never an early expiration.
            try { timer.Change(Slice(), Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) timer.Dispose();
    }
}

/// <summary>Owns a timer on the deadline's clock and links operation/close cancellation.</summary>
internal sealed class DmIoCancellation : IDisposable
{
    private readonly CancellationTokenSource signal = new();
    private readonly CancellationTokenRegistration callerRegistration;
    private readonly CancellationTokenRegistration closedRegistration;
    private readonly DmDeadlineTimer deadlineTimer;
    private readonly ITimer idleTimer;
    private readonly CancellationToken caller;
    private readonly DmInvocation invocation;
    // CAS records the event that actually won. Catch-time token state is never
    // used to guess whether a caller cancellation or a timeout happened first.
    private int cause;
    internal CancellationToken Token => signal.Token;

    internal DmIoCancellation(DmDeadline deadline, CancellationToken caller, CancellationToken closed,
        int idleMilliseconds = 0)
    {
        if (idleMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(idleMilliseconds));
        invocation = DmInvocation.Current;
        invocation?.ThrowIfTerminated();
        caller.ThrowIfCancellationRequested();
        deadline.ThrowIfExpired();
        this.caller = caller;
        callerRegistration = caller.UnsafeRegister(_ => Trigger(1), null);
        closedRegistration = closed.UnsafeRegister(_ => Trigger(2), null);
        if (!deadline.IsInfinite)
            deadlineTimer = new DmDeadlineTimer(deadline, () => Trigger(3));
        if (idleMilliseconds > 0)
            idleTimer = deadline.Clock.CreateTimer(_ => Trigger(4), null,
                TimeSpan.FromMilliseconds(idleMilliseconds), Timeout.InfiniteTimeSpan);
    }

    private void Trigger(int value)
    {
        if (Interlocked.CompareExchange(ref cause, value, 0) != 0) return;
        if (value is 3 or 4)
            invocation?.Lease.Session.TerminateInvocation(invocation,
                value == 3 ? DmCancelSource.TotalDeadline : DmCancelSource.IdleTimeout);
        try { signal.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal void RethrowCancellation(OperationCanceledException error)
    {
        if (invocation?.IsTerminated == true) throw invocation.TranslateFailure(error);
        switch (Volatile.Read(ref cause))
        {
            case 1: throw new OperationCanceledException("The operation was canceled.", error, caller);
            case 3: case 4: throw new TimeoutException("The operation I/O deadline expired.", error);
            default: throw error;
        }
    }

    public void Dispose()
    {
        callerRegistration.Dispose();
        closedRegistration.Dispose();
        deadlineTimer?.Dispose();
        idleTimer?.Dispose();
        signal.Dispose();
    }
}
