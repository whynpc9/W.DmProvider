namespace W.Dm;

/// <summary>The last known result of one local transaction, retained after disposal.</summary>
public enum DmTransactionOutcome
{
    Starting,
    Active,
    Committing,
    Committed,
    RollingBack,
    RolledBack,
    CompletedExternally,
    OutcomeUnknown
}
