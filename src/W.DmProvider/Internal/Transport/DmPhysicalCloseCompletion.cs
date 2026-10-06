using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace W.Dm.Internal.Transport;

// Logical close rejects I/O immediately; physical close finishes only after all
// owned disposal attempts. Completion callbacks never run under this gate.
internal sealed class DmPhysicalCloseCompletion
{
    private readonly object gate = new();
    private bool closed;
    private List<Action> callbacks;

    internal void RunAfterClosed(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            if (!closed)
            {
                (callbacks ??= new List<Action>()).Add(callback);
                return;
            }
        }
        callback();
    }

    internal void Complete()
    {
        Action[] captured;
        lock (gate)
        {
            if (closed) return;
            closed = true;
            captured = callbacks?.ToArray() ?? Array.Empty<Action>();
            callbacks = null;
        }
        ExceptionDispatchInfo failure = null;
        foreach (Action callback in captured)
        {
            try { callback(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        }
        failure?.Throw();
    }
}
