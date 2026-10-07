using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "T25CallerBoundary")]
public sealed class T25LateInputCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCallerInputReturnCannotSendOrCleanUpAReplacementLogicalConnection(bool text)
    {
        await using var fixture = new CallerFixture(text ? 19 : 12);
        var barrier = new InputBarrier();
        object input = text ? new LateTextReader(barrier) : new LateByteStream(barrier);
        using var caller = new CancellationTokenSource();
        await using var oldCommand = new DmCommand("INSERT INTO SYNTHETIC VALUES (:p)", fixture.Connection);
        oldCommand.Parameters.Add(new DmParameter("p", text ? DmDbType.Clob : DmDbType.Blob) { Value = input });
        await fixture.Connection.OpenAsync();
        DmSession oldSession = fixture.Connection.Session;
        Task<int> old = oldCommand.ExecuteNonQueryAsync(caller.Token);
        try
        {
            await barrier.SecondReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fixture.Channels[0].PutSends);
            Assert.Equal(1, fixture.Channels[0].PutAcknowledgementsRead);
            Assert.False(old.IsCompleted);

            caller.Cancel();
            Assert.Equal(DmPhysicalSessionState.Broken, oldSession.State);
            Assert.True(fixture.Channels[0].IsClosed);
            int oldSends = fixture.Channels[0].Sends;
            await fixture.Connection.CloseAsync();
            Assert.Equal(0, fixture.Source.Snapshot.PhysicalCount);
            await fixture.Connection.OpenAsync();
            DmSession replacement = fixture.Connection.Session;
            Assert.NotSame(oldSession, replacement);
            Assert.Equal(1, fixture.Source.Snapshot.Leased);
            Assert.Equal(1, await UpdateAsync(fixture.Connection));

            barrier.ReleaseSecondRead.TrySetResult();
            var canceled = await Assert.ThrowsAsync<DmOperationCanceledException>(() => old.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(caller.Token, canceled.CancellationToken);
            Assert.Equal(DmCancelSource.User, canceled.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, canceled.FailureInfo.OperationOutcome);
            Assert.False(canceled.FailureInfo.ConnectionReusable);
            Assert.Equal(oldSends, fixture.Channels[0].Sends);
            Assert.Equal(1, fixture.Channels[0].PutSends);
            Assert.Equal(0, fixture.Channels[0].FinalExecuteSends);
            Assert.Equal(new short[] { 3, 5, 90, 26 }, fixture.Channels[0].Opcodes);

            await oldCommand.DisposeAsync();
            caller.Cancel(); // Old token cleanup must remain confined to its retired execution.
            Assert.Same(replacement, fixture.Connection.Session);
            Assert.Equal(ConnectionState.Open, fixture.Connection.State);
            Assert.Equal(DmPhysicalSessionState.Ready, replacement.State);
            Assert.False(fixture.Channels[1].IsClosed);
            Assert.Equal(1, await UpdateAsync(fixture.Connection));
            Assert.Equal(2, fixture.Channels[1].CompletedUpdates);
            Assert.Equal(0, barrier.Disposed);
            Assert.Equal(0, barrier.ForbiddenAccesses);
            Assert.Equal(0, barrier.SyncReads);
            await fixture.Connection.CloseAsync();
            Assert.Equal(0, fixture.Source.Snapshot.PhysicalCount);
            Assert.Equal(0, fixture.Source.Snapshot.Waiting);
            Assert.All(fixture.Channels, channel => Assert.True(channel.IsClosed));
            Assert.All(fixture.Channels, channel => Assert.Equal(0, channel.SyncCalls));
        }
        finally
        {
            // Finite source release even when a strict assertion fails. This never
            // asks the provider to interrupt arbitrary nonreturning caller code.
            barrier.ReleaseSecondRead.TrySetResult();
            try { await old.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static async Task<int> UpdateAsync(DmConnection connection)
    {
        await using var command = new DmCommand("UPDATE SYNTHETIC SET ID=ID", connection);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed class InputBarrier
    {
        internal readonly TaskCompletionSource SecondReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSecondRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads, SyncReads, Disposed, ForbiddenAccesses;
        internal Exception Forbidden() { ForbiddenAccesses++; return new InvalidOperationException("Caller input seek/length access forbidden."); }
    }
    private sealed class LateByteStream(InputBarrier barrier) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => throw barrier.Forbidden();
        public override bool CanWrite => false;
        public override long Length => throw barrier.Forbidden();
        public override long Position { get => throw barrier.Forbidden(); set => throw barrier.Forbidden(); }
        public override int Read(byte[] buffer, int offset, int count) { barrier.SyncReads++; throw new InvalidOperationException("Sync caller read forbidden."); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            int read = ++barrier.Reads;
            if (read == 1) { new byte[] { 1, 2, 3, 4 }.AsMemory().CopyTo(buffer); return 4; }
            if (read == 2)
            {
                barrier.SecondReadEntered.TrySetResult();
                await barrier.ReleaseSecondRead.Task.ConfigureAwait(false); // Intentionally ignores the supplied token.
                new byte[] { 5, 6, 7, 8 }.AsMemory().CopyTo(buffer); return 4;
            }
            return 0;
        }
        protected override void Dispose(bool disposing) { barrier.Disposed++; }
        public override long Seek(long offset, SeekOrigin origin) => throw barrier.Forbidden();
        public override void SetLength(long length) => throw barrier.Forbidden();
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class LateTextReader(InputBarrier barrier) : TextReader
    {
        public override int Read(char[] buffer, int offset, int count) { barrier.SyncReads++; throw new InvalidOperationException("Sync caller read forbidden."); }
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        {
            int read = ++barrier.Reads;
            if (read == 1) { "\ud83d\ude80".AsMemory().CopyTo(buffer); return 2; } // Independent UTF8 F0 9F 9A 80 fills the four-byte chunk.
            if (read == 2)
            {
                barrier.SecondReadEntered.TrySetResult();
                await barrier.ReleaseSecondRead.Task.ConfigureAwait(false);
                "\ud83d\ude80".AsMemory().CopyTo(buffer); return 2;
            }
            return 0;
        }
        protected override void Dispose(bool disposing) { barrier.Disposed++; }
        public override string ReadToEnd() => throw barrier.Forbidden();
    }

    private sealed class CallerFixture : IAsyncDisposable
    {
        internal readonly DmDataSource Source;
        internal readonly DmConnection Connection;
        internal readonly List<GoldenChannel> Channels = [];
        internal CallerFixture(int parameterType)
        {
            Source = new DmDataSource(new DmConnectionStringBuilder
            {
                Server = "127.0.0.1", User = "T25_SYNTH", Password = "synthetic_only", Pooling = true,
                MaxPoolSize = 1, MaxPoolWaiters = 4, CommandTimeout = 10, TransportSecurity = DmTransportSecurity.PlaintextAllowed
            }.ConnectionString);
            Connection = Source.CreateConnection();
            DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
            {
                var channel = new GoldenChannel(Channels.Count == 0, parameterType);
                Channels.Add(channel);
                var instance = new DmConnInstance(candidate);
                candidate.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60";
                instance.ConnProperty.ServerEncoding = "UTF-8";
                instance.ConnProperty.msgVersion = 21;
                instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = 4;
                var protocol = instance.GetCsi(); object wire = protocol.A();
                ((DmTransport)Get(wire, "transport")!).Dispose();
                Set(wire, "transport", new DmTransport(channel));
                Set(wire, "__t02_field_04000AAD", false); Set(protocol, "__t02_field_04000ABD", false);
                candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
                return ValueTask.CompletedTask;
            };
        }
        public async ValueTask DisposeAsync()
        {
            try { await Connection.DisposeAsync(); await Source.DisposeAsync(); }
            finally { DmPendingOpenTestHooks.Handshake = null; }
        }
        private static object? Get(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner);
        private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(owner, value);
    }

    // Fixed synthetic reply layouts copied independently from documented legacy
    // fields/previous characterization. Expected frames never call the product encoder.
    private sealed class GoldenChannel(bool oldParameterized, int parameterType) : IDmByteChannel
    {
        private byte[] response = [];
        private int position;
        private short request;
        internal readonly List<short> Opcodes = [];
        internal int Sends, PutSends, PutAcknowledgementsRead, FinalExecuteSends, CompletedUpdates, SyncCalls;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync network forbidden."); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync network forbidden."); }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosed) throw new InvalidOperationException("Retired channel cannot send.");
            Sends++; request = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4)); Opcodes.Add(request);
            if (request == 26) PutSends++;
            if (oldParameterized && request is 6 or 13) FinalExecuteSends++;
            response = request switch
            {
                3 => Reply(3, []),
                5 when oldParameterized => Prepare(parameterType),
                5 => Updated(),
                90 => Reply(90, []),
                26 => Reply(261, Convert.FromHexString("020102030405060708090A0B0C0D0E0F1011121314")),
                4 => Reply(4, []),
                // Source-pinned NO_MORE_RESULTS contract: request44, reply0,
                // EC_RESULT_SET_EMPTY111, zero metadata/no rowset (parsed count-1).
                44 => Reply(0, [], 111),
                _ => throw new InvalidOperationException("Unexpected synthetic request opcode.")
            };
            position = 0; return ValueTask.FromResult(count);
        }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read)); position += read;
            if (request == 26 && position == response.Length) PutAcknowledgementsRead++;
            return ValueTask.FromResult(read);
        }
        private static byte[] Prepare(int type)
        {
            byte[] body = new byte[33];
            BinaryPrimitives.WriteInt32LittleEndian(body, type);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 1); // nullable, fixed typeFlag1, Input direction0.
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(24), 1); body[32] = (byte)'p';
            byte[] frame = Reply(5, body);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 1); // one prepared parameter.
            return frame;
        }
        private byte[] Updated()
        {
            CompletedUpdates++;
            byte[] frame = Reply(5, []);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 158); // UPDATE, no generated ROWID body.
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1);
            return frame;
        }
        private static byte[] Reply(short opcode, byte[] body, int status = 0)
        {
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, 41);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), status);
            body.CopyTo(frame, 64);
            for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
            return frame;
        }
        public void Dispose() => IsClosed = true;
    }
}
