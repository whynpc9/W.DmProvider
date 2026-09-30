using System;

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

    internal void ThrowIfExpired()
    {
        if (!IsInfinite && budget - clock.GetElapsedTime(startedAt) <= TimeSpan.Zero)
            throw new TimeoutException("The operation deadline has expired.");
    }
}
