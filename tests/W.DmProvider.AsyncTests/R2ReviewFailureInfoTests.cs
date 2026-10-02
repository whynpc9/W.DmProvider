using W.Dm;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class R2ReviewFailureInfoTests
{
    [Theory]
    [InlineData(DmCancelSource.None, -12345)]
    [InlineData(DmCancelSource.User, 12345)]
    [InlineData(DmCancelSource.Command, 0)]
    [InlineData(DmCancelSource.TotalDeadline, -12345)]
    [InlineData(DmCancelSource.IdleTimeout, 12345)]
    public void CommitUnknownPreservesSuppliedServerNumberAndCancelSource(DmCancelSource cancelSource, int serverNumber)
    {
        // Synthetic unit metadata, not a characterized live server response or
        // a declaration that any numeric server error permits session recovery.
        var original = new DmFailureInfo(DmErrorKind.Server, DmFailurePhase.Receive, "WDM_SERVER",
            DmOperationOutcome.ServerReported, DmTransactionOutcome.Committing, true, cancelSource, serverNumber);

        var unknown = original.WithCommitUnknown();

        AssertCommitUnknown(unknown);
        Assert.Equal(serverNumber, unknown.ServerErrorNumber);
        Assert.Equal(cancelSource, unknown.CancelSource);
        Assert.Equal(DmErrorKind.Server, original.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, original.OperationOutcome);
        Assert.True(original.ConnectionReusable);
        Assert.Equal(serverNumber, original.ServerErrorNumber);
    }

    [Theory]
    [InlineData(DmCancelSource.None)]
    [InlineData(DmCancelSource.User)]
    [InlineData(DmCancelSource.Command)]
    [InlineData(DmCancelSource.TotalDeadline)]
    [InlineData(DmCancelSource.IdleTimeout)]
    public void CommitUnknownDoesNotInventMissingServerNumber(DmCancelSource cancelSource)
    {
        var original = new DmFailureInfo(DmErrorKind.Transport, DmFailurePhase.Send, "WDM_TRANSPORT",
            DmOperationOutcome.Unknown, null, false, cancelSource);

        var unknown = original.WithCommitUnknown();

        AssertCommitUnknown(unknown);
        Assert.Equal(cancelSource, unknown.CancelSource);
        Assert.Null(unknown.ServerErrorNumber);
        Assert.Null(original.ServerErrorNumber);
        Assert.Null(original.TransactionOutcome);
    }

    private static void AssertCommitUnknown(DmFailureInfo info)
    {
        Assert.Equal(DmErrorKind.OutcomeUnknown, info.ErrorKind);
        Assert.Equal(DmFailurePhase.Commit, info.Phase);
        Assert.Equal("WDM_COMMIT_UNKNOWN", info.ErrorCode);
        Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, info.TransactionOutcome);
        Assert.False(info.ConnectionReusable);
    }
}
