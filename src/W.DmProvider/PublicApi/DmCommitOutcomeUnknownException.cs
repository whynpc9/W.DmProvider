using System;

namespace W.Dm;

/// <summary>The Commit request may have reached the server, but its result was not confirmed.</summary>
public sealed class DmCommitOutcomeUnknownException : DmException
{
    internal DmCommitOutcomeUnknownException(Exception cause)
        : base("Commit outcome is unknown. Do not retry this transaction or replay its work automatically.", cause)
    {
    }

    public override bool IsTransient => false;
}
