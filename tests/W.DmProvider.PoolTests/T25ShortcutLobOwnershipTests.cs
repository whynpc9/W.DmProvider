using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "T25CallerBoundary")]
public sealed class T25ShortcutLobOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualShortcutLobFlowKeepsCapacityUntilPhysicalCloseAndCannotReadCachedDataAfterNewLease(bool text)
    {
        using var handshake = new Handshake(text);
        await using var source = new DmDataSource(new DmConnectionStringBuilder
        {
            Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only", Pooling = true,
            MaxPoolSize = 1, MaxPoolWaiters = 4, CommandTimeout = 15, TransportSecurity = DmTransportSecurity.PlaintextAllowed
        }.ConnectionString);
        await using var command = source.CreateCommand("SELECT VALUE FROM SYNTHETIC_TABLE");
        await using DbDataReader wrapper = await command.ExecuteReaderAsync();
        Assert.Equal("DmDataSourceReader", wrapper.GetType().Name);
        Assert.True(await wrapper.ReadAsync());
        Channel first = Assert.Single(handshake.Channels); DmSession oldSession = Assert.Single(handshake.Sessions);
        Assert.Equal(new short[] { 3, 5 }, first.Opcodes);
        Stream? blob = null; TextReader? clob = null;
        if (text)
        {
            clob = wrapper.GetTextReader(0); Assert.Equal("DmLobTextReader", clob.GetType().Name);
            Assert.Equal(new short[] { 3, 5 }, first.Opcodes);
            char[] prefix = new char[1]; Assert.Equal(1, await clob.ReadAsync(prefix)); Assert.Equal('A', prefix[0]);
        }
        else
        {
            blob = wrapper.GetStream(0); Assert.IsNotType<MemoryStream>(blob); Assert.Equal("DmLobReadStream", blob.GetType().Name);
            Assert.False(blob.CanSeek); Assert.Equal(new short[] { 3, 5 }, first.Opcodes);
            byte[] prefix = new byte[1]; Assert.Equal(1, await blob.ReadAsync(prefix)); Assert.Equal(17, prefix[0]);
        }
        Assert.Equal(new short[] { 3, 5, 32 }, first.Opcodes); // One frame leaves unread cursor/decoder cache.
        Assert.Equal(1, source.Snapshot.Leased);
        await using var second = source.CreateConnection(); long sockets = DmTransportTestHooks.CreatedTcpSockets;
        Task secondOpen = second.OpenAsync(); Assert.False(secondOpen.IsCompleted); Assert.Equal(1, source.Snapshot.Waiting);
        Assert.Single(handshake.Channels); Assert.Equal(sockets, DmTransportTestHooks.CreatedTcpSockets);
        first.PausePhysicalClose = true;
        // Dispose's physical channel boundary is synchronous; schedule the API so the test can observe its blocked close.
        Task dispose = Task.Run(async () => await wrapper.DisposeAsync());
        try
        {
            await first.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(first.IsClosed); Assert.False(dispose.IsCompleted); Assert.False(secondOpen.IsCompleted);
            Assert.Equal(1, source.Snapshot.Closing); Assert.Equal(1, source.Snapshot.PhysicalCount); Assert.Equal(1, source.Snapshot.Waiting);
            Assert.Single(handshake.Channels); Assert.Equal(sockets, DmTransportTestHooks.CreatedTcpSockets);
            await wrapper.DisposeAsync(); await command.DisposeAsync(); await wrapper.DisposeAsync(); await command.DisposeAsync();
            Assert.False(secondOpen.IsCompleted); Assert.Equal(1, source.Snapshot.PhysicalCount); Assert.False(first.IsClosed);
            Assert.Equal(new short[] { 3, 5, 32, 4 }, first.Opcodes);
        }
        finally { first.ReleasePhysicalClose.TrySetResult(); }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5)); await secondOpen.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.IsClosed); Assert.Equal(1, first.DisposeCalls); Assert.Equal(2, handshake.Channels.Count);
        Assert.NotEqual(oldSession.SessionId, second.Session.SessionId);
        Assert.Equal(1, source.Snapshot.Leased); Assert.Equal(0, source.Snapshot.Creating); Assert.Equal(0, source.Snapshot.Closing); Assert.Equal(0, source.Snapshot.Waiting);
        Channel current = handshake.Channels[1]; int oldFrames = first.Opcodes.Count, newFrames = current.Opcodes.Count;
        if (text)
        {
            Assert.ThrowsAny<InvalidOperationException>(() => clob!.Read(new char[1], 0, 1));
            Assert.ThrowsAny<InvalidOperationException>(() => clob!.Read(Array.Empty<char>(), 0, 0));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => clob!.ReadAsync(new char[1]).AsTask());
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => clob!.ReadAsync(Memory<char>.Empty).AsTask());
        }
        else
        {
            Assert.ThrowsAny<InvalidOperationException>(() => blob!.Read(new byte[1], 0, 1));
            Assert.ThrowsAny<InvalidOperationException>(() => blob!.Read(Array.Empty<byte>(), 0, 0));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => blob!.ReadAsync(new byte[1]).AsTask());
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => blob!.ReadAsync(Memory<byte>.Empty).AsTask());
        }
        Assert.Equal(oldFrames, first.Opcodes.Count); Assert.Equal(newFrames, current.Opcodes.Count);
        Assert.False(current.IsClosed); Assert.Equal(ConnectionState.Open, second.State);
        await using (var healthy = new DmCommand("SELECT VALUE FROM SYNTHETIC_TABLE", second))
        await using (var reader = await healthy.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            if (text)
            { using var fresh = reader.GetTextReader(0); char[] value = new char[16]; int read = await fresh.ReadAsync(value); Assert.Equal("A🚀e\u0301Z", new string(value, 0, read)); }
            else
            { await using var fresh = reader.GetStream(0); byte[] value = new byte[16]; int read = await fresh.ReadAsync(value); Assert.Equal(new byte[] { 17, 148, 23, 154, 29 }, value.AsSpan(0, read).ToArray()); }
        }
        Assert.Equal(new short[] { 3, 5, 32, 4 }, current.Opcodes);
        await second.CloseAsync(); await second.DisposeAsync(); await command.DisposeAsync(); await wrapper.DisposeAsync();
        blob?.Dispose(); clob?.Dispose();
        Assert.Equal(1, current.DisposeCalls); Assert.True(source.Snapshot.IsQuiescent);
        Assert.All(handshake.Channels, channel => Assert.Equal(0, channel.SyncCalls));
    }

    private sealed class Handshake : IDisposable
    {
        private readonly bool text;
        internal readonly List<Channel> Channels = [];
        internal readonly List<DmSession> Sessions = [];
        internal Handshake(bool text)
        {
            this.text = text;
            DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
            {
                var instance = new DmConnInstance(candidate); candidate.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60"; instance.ConnProperty.ServerEncoding = "UTF-8";
                instance.ConnProperty.msgVersion = 21; instance.ConnProperty.NewLobFlag = true; instance.ConnProperty.LongLobFlag = true;
                var protocol = instance.GetCsi(); object wire = protocol.A();
                ((DmTransport)Field(wire, "transport")!).Dispose();
                var channel = new Channel(this.text); Channels.Add(channel); Sessions.Add(candidate.Session);
                Set(wire, "transport", new DmTransport(channel)); Set(wire, "__t02_field_04000AAD", false); Set(protocol, "__t02_field_04000ABD", false);
                candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open; return ValueTask.CompletedTask;
            };
        }
        private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value);
        private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(value, field);
        public void Dispose() => DmPendingOpenTestHooks.Handshake = null;
    }
    private sealed class Channel(bool text) : IDmByteChannel
    {
        private byte[] reply = [];
        private int position;
        internal readonly List<short> Opcodes = [];
        internal readonly TaskCompletionSource CloseEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleasePhysicalClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool PausePhysicalClose;
        internal int DisposeCalls, SyncCalls;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync test I/O forbidden."); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync test I/O forbidden."); }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); short opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4)); Opcodes.Add(opcode);
            reply = opcode switch { 3 => Allocation(), 5 => Rowset(text), 32 => Lob(text), 4 => Frame(0, []), _ => throw new InvalidOperationException("Unexpected fixed-golden request.") };
            position = 0; return ValueTask.FromResult(count);
        }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); int read = Math.Min(count, reply.Length - position); reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read)); position += read; return ValueTask.FromResult(read); }
        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCalls); CloseEntered.TrySetResult();
            if (PausePhysicalClose && !ReleasePhysicalClose.Task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test physical-close barrier timed out.");
            IsClosed = true;
        }
        private static byte[] Allocation()
        { byte[] frame = Frame(0, []); BinaryPrimitives.WriteInt32LittleEndian(frame, 41); Checksum(frame); return frame; }
        private static byte[] Rowset(bool text)
        {
            // One plain, non-BDTA column and one cached row. Independent 32-byte metadata descriptor,
            // 6-byte LOB column identity, 2-byte row prefix/12-byte rowid/2-byte ordinal/2-byte value length/47-byte locator.
            byte[] body = new byte[38 + 2 + 12 + 2 + 2 + 47];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0), text ? 19 : 12);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 5);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(16), 2);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(32), 41); BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(36), 1);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(54), 47);
            body[56] = 2; BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(65), 5);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(77), 41); BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(81), 1);
            byte[] frame = Frame(0, body); BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 1); BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1); Checksum(frame); return frame;
        }
        private static byte[] Lob(bool text)
        {
            byte[] payload = text ? Encoding.UTF8.GetBytes("A🚀e\u0301Z") : [17, 148, 23, 154, 29];
            byte[] body = new byte[19 + payload.Length + 4]; body[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), payload.Length); BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(11), 5);
            payload.CopyTo(body, 19); BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(19 + payload.Length), 5);
            return Frame(32, body);
        }
        private static byte[] Frame(short opcode, byte[] body)
        { byte[] frame = new byte[64 + body.Length]; BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode); BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length); body.CopyTo(frame, 64); Checksum(frame); return frame; }
        private static void Checksum(byte[] frame)
        { frame[19] = 0; for (int i = 0; i < 19; i++) frame[19] ^= frame[i]; }
    }
}
