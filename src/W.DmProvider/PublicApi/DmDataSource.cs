using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Pooling;

namespace W.Dm;

/// <summary>Immutable connection configuration and an independently owned capacity domain.
/// Returned physical sessions are always discarded; no verified session reset is currently available.</summary>
public sealed class DmDataSource : DbDataSource
{
    private readonly DmConnectionSettings settings;
    private readonly object publicationGate = new();
    private int disposed;
    internal DmPoolOwner Owner { get; }
    internal DmPoolSnapshot Snapshot => Owner.Snapshot;
    public override string ConnectionString => settings.ToConnectionString(includeSecrets: false);

    public DmDataSource(string connectionString)
    {
        settings = DmConnectionSettings.Parse(connectionString);
        Owner = new DmPoolOwner(settings.MaxPoolSize, settings.MaxPoolWaiters);
    }

    public static DmDataSource Create(string connectionString) => new(connectionString);
    public new DmConnection CreateConnection() => (DmConnection)base.CreateConnection();
    public new DmConnection OpenConnection() => (DmConnection)base.OpenConnection();
    public new async ValueTask<DmConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        => (DmConnection)await base.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    protected override DbConnection CreateDbConnection()
    {
        ThrowIfDisposed();
        return new DmConnection(settings, this);
    }
    protected override DbCommand CreateDbCommand(string commandText = null)
    {
        ThrowIfDisposed();
        return new DmDataSourceCommand(this, commandText ?? string.Empty);
    }
    internal void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DmDataSource));
    }
    internal void Publish(Action publish)
    {
        lock (publicationGate)
        {
            ThrowIfDisposed();
            publish();
        }
    }
    public void ClearPool()
    {
        ThrowIfDisposed();
        Owner.Clear();
    }
    protected override void Dispose(bool disposing)
    {
        bool stop;
        lock (publicationGate) stop = Interlocked.Exchange(ref disposed, 1) == 0;
        if (stop) Owner.Stop(); // Waiter continuations/cancellation cleanup stay outside publicationGate.
        base.Dispose(disposing);
    }
    protected override ValueTask DisposeAsyncCore()
    {
        Dispose(true);
        return ValueTask.CompletedTask;
    }
}
