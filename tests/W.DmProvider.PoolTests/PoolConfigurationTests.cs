using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class PoolConfigurationTests
{
    [Fact]
    public void DefaultAndAliasRoundtripKeepOwnedPoolingOutOfLegacyFilters()
    {
        var defaults = new DmConnectionStringBuilder();
        Assert.False(defaults.Pooling);
        Assert.Equal(100, defaults.MaxPoolSize);
        Assert.Equal(1024, defaults.MaxPoolWaiters);
        var builder = new DmConnectionStringBuilder("Pooling=true;ConnPooling=true;MaxPoolSize=7;ConnPoolSize=7;MaxPoolWaiters=3");
        Assert.True(builder.Pooling);
        Assert.True(builder.ConnPooling);
        Assert.Equal(7, builder.ConnPoolSize);
        var settings = builder.ToSettings();
        var roundtrip = DmConnectionSettings.Parse(settings.ToConnectionString(true));
        Assert.True(roundtrip.Pooling);
        Assert.Equal(7, roundtrip.MaxPoolSize);
        Assert.Equal(3, roundtrip.MaxPoolWaiters);
        Assert.False((bool)settings.ToLegacyProperties()[DmConst.PROP_KEY_CONN_POOLING]);
        builder.Remove("Pooling"); builder.Remove("MaxPoolSize"); builder.Remove("MaxPoolWaiters");
        Assert.False(builder.Pooling);
        Assert.Equal(100, builder.MaxPoolSize);
        Assert.Equal(1024, builder.MaxPoolWaiters);
    }
    [Theory]
    [InlineData("Pooling=true;ConnPooling=false")]
    [InlineData("MaxPoolSize=2;ConnPoolSize=3")]
    [InlineData("MaxPoolSize=0")]
    [InlineData("MaxPoolWaiters=-1")]
    public void InvalidAndConflictingLimitsReject(string raw) => Assert.Throws<ArgumentException>(() => new DmConnectionStringBuilder(raw));
    [Theory]
    [InlineData("StmtPooling=true")]
    [InlineData("PreparePooling=true")]
    public void StatementPoolingRemainsUnsupported(string raw) => Assert.Throws<NotSupportedException>(() => new DmConnectionStringBuilder(raw));
    [Fact]
    public async Task DirectExplicitCertificatePoolingRejectsWithoutCreatingSocket()
    {
        long sockets = DmTransportTestHooks.CreatedTcpSockets;
        using var connection = new DmConnection(PoolApiSettings.Text + ";TlsCaCertificatePath=/tmp/synthetic-ca.pem");
        await Assert.ThrowsAsync<NotSupportedException>(() => connection.OpenAsync());
        Assert.Equal(sockets, DmTransportTestHooks.CreatedTcpSockets);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }
    [Fact]
    public void AbsoluteEarlierDeadlineDoesNotRenewOriginOrTurnExpiredFiniteBudgetInfinite()
    {
        var clock = new PoolClock();
        var pool = DmDeadline.Start(TimeSpan.FromSeconds(1), clock);
        clock.Advance(TimeSpan.FromMilliseconds(900));
        var connect = DmDeadline.Start(TimeSpan.FromSeconds(10), clock);
        var earliest = pool.EarlierOf(connect);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(earliest.IsInfinite);
        Assert.Throws<TimeoutException>(() => earliest.ThrowIfExpired());
        Assert.Throws<ArgumentException>(() => pool.EarlierOf(DmDeadline.Start(TimeSpan.FromSeconds(1), new PoolClock())));
        Assert.Equal(TimeSpan.Zero, DmDeadline.Infinite.EarlierOf(pool).RemainingTime);
    }
}
