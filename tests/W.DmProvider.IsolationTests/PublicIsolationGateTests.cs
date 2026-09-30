using System.Data;
using W.Dm;
using Xunit;

namespace W.DmProvider.IsolationTests;

public sealed class PublicIsolationGateTests
{
    [Theory]
    [InlineData(IsolationLevel.ReadCommitted, null)]
    [InlineData(IsolationLevel.Unspecified, null)]
    [InlineData(IsolationLevel.ReadCommitted, "")]
    [InlineData(IsolationLevel.Unspecified, "8.1.5.600")]
    [InlineData(IsolationLevel.ReadCommitted, "9.0.0.0")]
    public void ExistingDefaultIsolationDoesNotDependOnThePromotedProfile(IsolationLevel level, string? version)
        => Assert.True(DmConnection.IsPublicTransactionIsolationSupported(level, version!));

    [Theory]
    [InlineData(IsolationLevel.ReadUncommitted)]
    [InlineData(IsolationLevel.Serializable)]
    public void PromotedLevelsRequireTheExactVerifiedServerProfile(IsolationLevel level)
        => Assert.True(DmConnection.IsPublicTransactionIsolationSupported(level, "8.1.5.60"));

    [Theory]
    [InlineData(IsolationLevel.ReadUncommitted, null)]
    [InlineData(IsolationLevel.Serializable, null)]
    [InlineData(IsolationLevel.ReadUncommitted, "")]
    [InlineData(IsolationLevel.ReadUncommitted, "8.1.5.600")]
    [InlineData(IsolationLevel.Serializable, "8.1.5.600")]
    [InlineData(IsolationLevel.ReadUncommitted, " 8.1.5.60")]
    [InlineData(IsolationLevel.Serializable, "8.1.5.60 ")]
    [InlineData(IsolationLevel.ReadUncommitted, "8.1.5.60-extra")]
    [InlineData(IsolationLevel.Serializable, "8.1.5.6")]
    [InlineData(IsolationLevel.ReadUncommitted, "8.1.6.60")]
    [InlineData(IsolationLevel.RepeatableRead, "8.1.5.60")]
    [InlineData(IsolationLevel.Snapshot, "8.1.5.60")]
    [InlineData(IsolationLevel.Chaos, "8.1.5.60")]
    [InlineData((IsolationLevel)(-999), "8.1.5.60")]
    public void UnsupportedLevelsAndUnverifiedVersionShapesRemainRejected(IsolationLevel level, string? version)
        => Assert.False(DmConnection.IsPublicTransactionIsolationSupported(level, version!));
}
