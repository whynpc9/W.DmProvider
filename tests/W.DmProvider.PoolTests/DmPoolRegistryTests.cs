using W.Dm;
using W.Dm.Internal.Pooling;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class DmPoolRegistryTests
{
    [Fact]
    public void BoundedBusyRegistryFailsNewKeyWithoutSplittingExistingOwner()
    {
        var registry = new DmPoolRegistry(1, expiration: TimeSpan.Zero);
        using var reference = registry.GetOrCreate(Settings("a"));
        var lease = reference.Owner.Acquire(DmDeadline.Infinite);
        using var equivalent = registry.GetOrCreate(Settings("a"));
        Assert.Same(reference.Owner, equivalent.Owner);
        var error = Assert.Throws<DmException>(() => registry.GetOrCreate(Settings("b")));
        Assert.Equal("WDM_POOL_REGISTRY_FULL", error.FailureInfo.ErrorCode);
        Assert.Equal(1, registry.Count);
        lease.CompleteAfterTransportClosed();
    }

    [Fact]
    public void LookupReferenceProtectsGapBeforeAcquisitionAndIsDisposedExactlyOnce()
    {
        var registry = new DmPoolRegistry(1, expiration: TimeSpan.Zero);
        var reference = registry.GetOrCreate(Settings("a"));
        Assert.Throws<DmException>(() => registry.GetOrCreate(Settings("b")));
        reference.Dispose();
        reference.Dispose();
        using var next = registry.GetOrCreate(Settings("b"));
        Assert.NotSame(reference.Owner, next.Owner);
        Assert.True(reference.Owner.Snapshot.Stopped);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public async Task ClearAllPreservesSameCapacityDomainWhilePhysicalLeaseOrWaiterExists()
    {
        var registry = new DmPoolRegistry(1, expiration: TimeSpan.Zero);
        var firstRef = registry.GetOrCreate(Settings("a"));
        var originalOwner = firstRef.Owner;
        var held = originalOwner.Acquire(DmDeadline.Infinite);
        firstRef.Dispose();
        using var secondRef = registry.GetOrCreate(Settings("a"));
        var waiting = secondRef.Owner.AcquireAsync(DmDeadline.Infinite).AsTask();
        registry.Clear(Settings("a"));
        registry.ClearAll();
        using var thirdRef = registry.GetOrCreate(Settings("a"));
        Assert.Same(originalOwner, thirdRef.Owner);
        Assert.False(waiting.IsCompleted);
        Assert.Equal(1, originalOwner.Snapshot.PhysicalCount);
        Assert.Throws<DmException>(() => registry.GetOrCreate(Settings("b")));
        held.CompleteAfterTransportClosed();
        var next = await waiting;
        Assert.Equal(2, next.Epoch);
        next.CompleteAfterTransportClosed();
    }

    [Fact]
    public void PhysicalCompletionStartsActualIdleExpiryEvenAfterAcquisitionReferenceReleased()
    {
        var clock = new PoolClock();
        var registry = new DmPoolRegistry(1, clock, TimeSpan.FromMinutes(2));
        var reference = registry.GetOrCreate(Settings("a"));
        var lease = reference.Owner.Acquire(DmDeadline.Infinite);
        reference.Dispose();
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Throws<DmException>(() => registry.GetOrCreate(Settings("b")));
        lease.CompleteAfterTransportClosed();
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Throws<DmException>(() => registry.GetOrCreate(Settings("b")));
        clock.Advance(TimeSpan.FromSeconds(1));
        using var next = registry.GetOrCreate(Settings("b"));
        Assert.NotSame(reference.Owner, next.Owner);
        Assert.True(reference.Owner.Snapshot.Stopped);
    }

    [Fact]
    public void RepeatedKeyChurnRemainsStrictlyBoundedAndRetiresOnlyQuiescentOwners()
    {
        var clock = new PoolClock();
        var registry = new DmPoolRegistry(4, clock, TimeSpan.FromSeconds(1));
        for (int i = 0; i < 100; i++)
        {
            using (var reference = registry.GetOrCreate(Settings(i.ToString())))
            {
                var lease = reference.Owner.Acquire(DmDeadline.Infinite);
                lease.MarkLeased();
                lease.BeginClosing();
                lease.CompleteAfterTransportClosed();
                Assert.InRange(registry.Count, 1, 4);
            }
            clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void RegistryCollisionSeparatesSecretsAndAllEffectiveConfig()
    {
        var registry = new DmPoolRegistry(4, comparer: new DmPoolIdentityTests.ConstantHashComparer());
        using var first = registry.GetOrCreate(Settings("a"));
        using var second = registry.GetOrCreate(Settings("b"));
        using var equivalent = registry.GetOrCreate(Settings("a"));
        Assert.NotSame(first.Owner, second.Owner);
        Assert.Same(first.Owner, equivalent.Owner);
        Assert.Equal(2, registry.Count);
    }

    [Theory]
    [InlineData("ca")]
    [InlineData("client")]
    [InlineData("key")]
    public void DirectExplicitTlsFilesAreRejectedBeforePoolCreation(string kind)
    {
        var builder = Builder("a");
        if (kind == "ca") builder.TlsCaCertificatePath = "/synthetic/ca.pem";
        else
        {
            builder.TlsClientCertificatePath = "/synthetic/client.pem";
            if (kind == "key") builder.TlsClientPrivateKeyPath = "/synthetic/key.pem";
        }
        var registry = new DmPoolRegistry();
        var error = Assert.Throws<NotSupportedException>(() => registry.GetOrCreate(builder.ToSettings()));
        Assert.Contains("DmDataSource", error.Message);
        Assert.DoesNotContain("synthetic", error.Message);
        Assert.Equal(0, registry.Count);
    }

    private static DmConnectionSettings Settings(string secret) => Builder(secret).ToSettings();
    private static DmConnectionStringBuilder Builder(string secret) => new()
    {
        Server = "registry.invalid", User = "SYNTHETIC_USER", Password = secret,
        Pooling = true, MaxPoolSize = 1, MaxPoolWaiters = 4
    };
}
