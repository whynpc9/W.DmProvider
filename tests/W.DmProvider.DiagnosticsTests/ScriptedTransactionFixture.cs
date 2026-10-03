using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;

namespace W.DmProvider.DiagnosticsTesting;

// Minimal independently copied allocation/SET TRANSACTION/close/commit golden frames.
// Public Open/BeginTransaction/Commit run normally; only the byte transport is scripted.
internal sealed class ScriptedTransactionFixture : IAsyncDisposable
{
    internal readonly DmDataSource Source;
    internal readonly ScriptedControlChannel Channel = new();
    internal DmConnection Connection = null!;
    internal ScriptedTransactionFixture()
    {
        Source = new DmDataSource(new DmConnectionStringBuilder
        {
            Server = "127.0.0.1", User = "SYNTHETIC_USER_SECRET", Password = "SYNTHETIC_PASSWORD_SECRET",
            Pooling = true, MaxPoolSize = 1, MaxPoolWaiters = 8, CommandTimeout = 10,
            TransportSecurity = DmTransportSecurity.PlaintextAllowed
        }.ConnectionString);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            var instance = new DmConnInstance(candidate);
            candidate.m_ConnInst = instance; instance.ConnProperty.ServerVersion = "8.1.5.60";
            var protocol = instance.GetCsi(); var wire = protocol.A();
            ((DmTransport)Get(wire, "transport")!).Dispose();
            Set(wire, "transport", new DmTransport(Channel)); Set(wire, "__t02_field_04000AAD", false);
            Set(protocol, "__t02_field_04000ABD", false);
            candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
            return ValueTask.CompletedTask;
        };
    }
    internal async Task<DmTransaction> BeginAsync()
    {
        Connection = await Source.OpenConnectionAsync();
        return (DmTransaction)await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    }
    internal bool Quiescent => Source.Snapshot.IsQuiescent;
    public async ValueTask DisposeAsync()
    {
        try { if (Connection != null) await Connection.DisposeAsync(); }
        finally { await Source.DisposeAsync(); DmPendingOpenTestHooks.Handshake = null; }
    }
    private static object? Get(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
    private static void Set(object value, string name, object? field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
}

internal sealed class ScriptedControlChannel : IDmByteChannel
{
    private byte[] response = [];
    private int offset;
    private short opcode;
    internal int Sends, CommitSends, SyncCalls;
    internal bool EofAtCommit, BlockCommit;
    internal readonly TaskCompletionSource<bool> CommitReceiveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<bool> ReleaseCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds) { SyncCalls++; throw new InvalidOperationException("Sync scripted I/O forbidden."); }
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) { SyncCalls++; throw new InvalidOperationException("Sync scripted I/O forbidden."); }
    public ValueTask<int> SendAsync(byte[] buffer, int start, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Sends++; opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(start + 4, 2));
        if (opcode == 8) CommitSends++;
        response = new byte[opcode == 5 ? 67 : 64]; offset = 0;
        if (opcode == 3) BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(0, 4), 41);
        if (opcode == 5)
        {
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(6, 4), 3);
            BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(20, 2), 150);
            BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(64, 2), 3);
        }
        for (int i = 0; i < 19; i++) response[19] ^= response[i];
        return ValueTask.FromResult(count);
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int start, int count, CancellationToken token)
    {
        if (opcode == 8)
        {
            CommitReceiveEntered.TrySetResult(true);
            if (BlockCommit) await ReleaseCommit.Task.WaitAsync(token).ConfigureAwait(false);
            if (EofAtCommit) return 0;
        }
        token.ThrowIfCancellationRequested(); int copy = Math.Min(count, response.Length - offset);
        response.AsSpan(offset, copy).CopyTo(buffer.AsSpan(start, copy)); offset += copy; return copy;
    }
    public void Dispose() { IsClosed = true; ReleaseCommit.TrySetResult(true); }
}
