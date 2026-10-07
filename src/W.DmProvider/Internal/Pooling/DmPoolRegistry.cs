using System;
using System.Collections.Generic;
using System.Threading;
using W.Dm.Internal.Diagnostics;

namespace W.Dm.Internal.Pooling;

/// <summary>Access-driven, strictly bounded ownership registry; no permanent timer or background thread.</summary>
internal sealed class DmPoolRegistry
{
    internal const int DefaultMaximumOwners = 128;
    internal static DmPoolRegistry Shared { get; } = new();
    private readonly object gate = new();
    private readonly Dictionary<DmPoolKey, Entry> owners;
    private readonly TimeProvider clock;
    private readonly TimeSpan expiration;
    private readonly int maximumOwners;

    internal DmPoolRegistry(int maximumOwners = DefaultMaximumOwners, TimeProvider clock = null,
        TimeSpan? expiration = null, IEqualityComparer<DmPoolKey> comparer = null)
    {
        if (maximumOwners < 1) throw new ArgumentOutOfRangeException(nameof(maximumOwners));
        this.maximumOwners = maximumOwners;
        this.clock = clock ?? TimeProvider.System;
        this.expiration = expiration ?? TimeSpan.FromMinutes(2);
        if (this.expiration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(expiration));
        owners = new Dictionary<DmPoolKey, Entry>(comparer);
    }

    internal int Count { get { lock (gate) return owners.Count; } }

    internal DmPoolOwnerReference GetOrCreate(DmConnectionSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (settings.TlsCaCertificatePath.Length != 0 || settings.TlsClientCertificatePath.Length != 0 ||
            settings.TlsClientPrivateKeyPath.Length != 0)
            throw new NotSupportedException("Direct pooled connections with explicit TLS certificate files require a DmDataSource.");
        var key = new DmPoolKey(settings);
        List<DmPoolOwner> retired;
        DmPoolOwnerReference reference = null;
        Exception error = null;
        lock (gate)
        {
            retired = RetireExpired();
            if (!owners.TryGetValue(key, out Entry entry))
            {
                if (owners.Count >= maximumOwners)
                    error = DmPoolOwner.PoolFailure("WDM_POOL_REGISTRY_FULL", "The connection pool registry is full.");
                else
                {
                    entry = new Entry(new DmPoolOwner(settings.MaxPoolSize, settings.MaxPoolWaiters), clock.GetTimestamp());
                    Entry captured = entry;
                    entry.Owner.Quiescent += () => ObserveQuiescence(captured);
                    owners.Add(key, entry);
                    if (ReferenceEquals(this, Shared)) DmDiagnosticsCore.RegistryDelta(1);
                }
            }
            if (error == null)
            {
                entry.References++;
                reference = new DmPoolOwnerReference(entry.Owner, () => ReleaseReference(entry));
            }
        }
        foreach (DmPoolOwner owner in retired) owner.Stop();
        if (error != null) throw error;
        return reference;
    }

    private List<DmPoolOwner> RetireExpired()
    {
        var expired = new List<DmPoolKey>();
        foreach (var pair in owners)
        {
            Entry entry = pair.Value;
            if (entry.References != 0 || !entry.Owner.Snapshot.IsQuiescent)
            { entry.QuiescentSince = null; continue; }
            // Returning transports can complete after the acquisition reference is disposed.
            // Start their idle interval only when quiescence is actually observed.
            entry.QuiescentSince ??= clock.GetTimestamp();
            if (clock.GetElapsedTime(entry.QuiescentSince.Value) >= expiration) expired.Add(pair.Key);
        }
        var retired = new List<DmPoolOwner>(expired.Count);
        foreach (DmPoolKey key in expired)
        {
            retired.Add(owners[key].Owner); owners.Remove(key);
            if (ReferenceEquals(this, Shared)) DmDiagnosticsCore.RegistryDelta(-1);
        }
        return retired;
    }

    private void ReleaseReference(Entry entry)
    {
        lock (gate)
        {
            entry.References--;
            entry.QuiescentSince = entry.References == 0 && entry.Owner.Snapshot.IsQuiescent ? clock.GetTimestamp() : null;
        }
    }

    private void ObserveQuiescence(Entry entry)
    {
        lock (gate)
        {
            if (entry.References == 0 && entry.Owner.Snapshot.IsQuiescent)
                entry.QuiescentSince ??= clock.GetTimestamp();
        }
    }

    internal void Clear(DmConnectionSettings settings)
    {
        lock (gate) if (owners.TryGetValue(new DmPoolKey(settings), out Entry entry)) entry.Owner.Clear();
    }

    internal void ClearAll()
    {
        lock (gate) foreach (Entry entry in owners.Values) entry.Owner.Clear();
    }

    private sealed class Entry
    {
        internal readonly DmPoolOwner Owner;
        internal int References;
        internal long? QuiescentSince;
        internal Entry(DmPoolOwner owner, long timestamp) { Owner = owner; QuiescentSince = timestamp; }
    }
}

/// <summary>Prevents eviction between registry lookup and acquisition commit/failure.</summary>
internal sealed class DmPoolOwnerReference : IDisposable
{
    private Action release;
    internal DmPoolOwner Owner { get; }
    internal DmPoolOwnerReference(DmPoolOwner owner, Action release) { Owner = owner; this.release = release; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
