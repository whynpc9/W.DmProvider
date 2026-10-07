using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class ReadStreamTests
{
    [Fact]
    public async Task UnknownBlobLengthRejectsNegativeGetLengthBeforeReturningMetadata()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12, OutputLobFixture.Locator(-1));
        fixture.Channel.AddLength(-1); fixture.ReleaseAll();
        Assert.Throws<InvalidDataException>(() => reader.GetBytes(0, 0, null!, 0, 0));
        Assert.Equal(new short[] { 29 }, fixture.Channel.Commands);
    }

    [Fact]
    public async Task LazyBlobUsesSingleFramesAndMissingAdvanceIsBytes()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12);
        using var stream = reader.GetStream(0);
        Assert.Empty(fixture.Channel.Commands);
        Assert.False(stream.CanSeek); Assert.False(stream.CanWrite);
        fixture.Channel.AddData([1, 2], -1);
        fixture.Channel.AddData([3, 4], -1);
        fixture.Channel.AddData([5], -1, true);
        fixture.ReleaseAll();
        byte[] buffer = new byte[8];
        Assert.Equal(2, await stream.ReadAsync(buffer));
        Assert.Equal(new byte[] { 1, 2 }, buffer[..2]);
        Assert.Single(fixture.Channel.Commands);
        Assert.Equal(2, await stream.ReadAsync(buffer));
        Assert.Equal(1, await stream.ReadAsync(buffer));
        Assert.Equal(0, await stream.ReadAsync(buffer));
        Assert.Equal(new long[] { 0, 2, 4 }, fixture.Channel.ReadPositions);
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public async Task PrematureBinaryEofFailsWithoutPadding()
    {
        await using var fixture = new OutputLobFixture();
        using var stream = fixture.Reader(12).GetStream(0);
        fixture.Channel.AddData([1, 2], -1, true); fixture.ReleaseAll();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await stream.ReadAsync(new byte[5]));
    }

    [Fact]
    public async Task NegativeUpdatedLocatorIsRejectedBeforePayloadAllocation()
    {
        await using var fixture = new OutputLobFixture();
        using var stream = fixture.Reader(12).GetStream(0);
        byte[] body = new byte[21];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), 2);
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(11), -1);
        body[19] = 1; body[20] = 2;
        fixture.Channel.AddRawDataBody(body); fixture.ReleaseAll();
        int allocations = 0;
        GET_LOB_DATA.PayloadAllocationObserver.Value = _ => allocations++;
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await stream.ReadAsync(new byte[2]));
            Assert.Equal(0, allocations);
        }
        finally { GET_LOB_DATA.PayloadAllocationObserver.Value = null; }
    }

    [Theory]
    [InlineData(0xffffffffu, 19)]
    [InlineData(4u, 20)]
    [InlineData(1u, 18)]
    public async Task DeclaredResponseLengthIsValidatedBeforePayloadAllocation(uint declared, int bodySize)
    {
        await using var fixture = new OutputLobFixture();
        using var stream = fixture.Reader(12).GetStream(0);
        byte[] body = new byte[bodySize];
        if (bodySize >= 5) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), declared);
        fixture.Channel.AddRawDataBody(body); fixture.ReleaseAll();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await stream.ReadAsync(new byte[5]));
    }

    [Fact]
    public async Task LocatorUpdatesArePreservedOnThePrivateCursor()
    {
        await using var fixture = new OutputLobFixture();
        using var stream = fixture.Reader(12).GetStream(0);
        fixture.Channel.AddData([1, 2], -1, nextFile: 9, nextTotal: 73);
        fixture.Channel.AddData([3, 4], -1, nextFile: 12, nextTotal: 89);
        fixture.Channel.AddData([5], -1, true); fixture.ReleaseAll();
        byte[] buffer = new byte[2];
        await stream.ReadAsync(buffer); await stream.ReadAsync(buffer); await stream.ReadAsync(buffer);
        Assert.Equal(new int[] { 0, 9, 12 }, fixture.Channel.CursorFiles);
        Assert.Equal(new long[] { 0, 73, 89 }, fixture.Channel.CursorTotals);
        Assert.Equal(new long[] { 0, 2, 4 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task HugeLogicalBlobKeepsOneFrameAndReturnsFirstChunkEarly()
    {
        const long total = (1L << 30) + 1;
        var locator = new AbstractLob(0, null) { bytesLength = total };
        byte[] frame = new byte[16384]; Array.Fill(frame, (byte)0xA7);
        long generated = 0; int calls = 0;
        Data Fetch(long position, int requested)
        {
            Assert.Equal(generated, position); calls++;
            int count = (int)Math.Min(frame.Length, total - generated);
            generated += count; locator.readOver = generated == total;
            return new Data(-1, count == frame.Length ? frame : new byte[] { 0xA7 });
        }
        using var cursor = new DmLobReadCursor(locator, default, false, null, null, 16384,
            Fetch, (p, n, t) => Task.FromResult(Fetch(p, n)), false, null);
        cursor.SetKnownWireLength(total);
        using var stream = new DmLobReadStream(cursor);
        byte[] buffer = new byte[65536];
        Assert.Equal(16384, await stream.ReadAsync(buffer));
        Assert.Equal(16384, generated); Assert.Equal(1, calls);
        long read = 16384;
        while (true)
        {
            int count = await stream.ReadAsync(buffer);
            if (count == 0) break;
            Assert.Equal(0xA7, buffer[0]); Assert.Equal(0xA7, buffer[count - 1]);
            Assert.True(cursor.BufferedBytes <= 16384);
            read += count;
        }
        Assert.Equal(total, read); Assert.Equal(65537, calls);
    }
}

// Uses the real LOB codec and protocol path, replacing only the byte channel.
internal sealed class OutputLobFixture : IAsyncDisposable
{
    internal DmConnection Connection { get; } = new(new DmConnectionStringBuilder
    {
        Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only"
    }.ConnectionString);
    internal DmSession Session { get; } = new();
    internal DmConnInstance Instance { get; }
    internal DmExecutionLease Lease { get; }
    internal OutputLobChannel Channel { get; } = new();

    private readonly bool newLobProfile;

    internal OutputLobFixture(TimeProvider? clock = null, bool newLobProfile = false)
    {
        this.newLobProfile = newLobProfile;
        SetField(Connection, "session", Session);
        Session.BeginConnecting();
        using (var handshake = Session.BeginExecution(DmOperationPurpose.Handshake))
        using (var invocation = handshake.BeginInvocation())
        {
            Instance = new DmConnInstance(Connection);
            Connection.m_ConnInst = Instance;
            Instance.ConnProperty.ServerEncoding = "UTF-8";
            Instance.ConnProperty.msgVersion = 21;
            Instance.ConnProperty.NewLobFlag = newLobProfile;
            Instance.ConnProperty.LongLobFlag = true;
            Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = 2;
            var protocol = Instance.GetCsi();
            var transport = protocol.A();
            var field = transport.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((DmTransport)field.GetValue(transport)!).Dispose();
            field.SetValue(transport, new DmTransport(Channel));
            SetField(transport, "__t02_field_04000AAD", false);
            SetField(protocol, "__t02_field_04000ABD", false);
            Session.CompleteHandshake();
        }
        Connection.do_State = ConnectionState.Open;
        Lease = Session.BeginExecution(DmOperationPurpose.Reader,
            clock == null ? DmDeadline.Infinite : DmDeadline.Start(TimeSpan.FromSeconds(10), clock), timeoutSeconds: 10);
    }

    internal DmDataReader Reader(int type, byte[]? value = null, CommandBehavior behavior = CommandBehavior.Default, int rows = 1)
    {
        var command = new DmCommand("synthetic", Connection);
        var statement = (W.Dm.Internal.Legacy.A.A)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(W.Dm.Internal.Legacy.A.A));
        SetField(statement, "__t02_field_04000923", Instance);
        SetField(statement, "__t02_field_04000924", Instance.GetCsi());
        SetField(statement, "__t02_field_04000933", command);
        SetField(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
        SetField(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
        var info = new DmInfo(Instance);
        info.SetColumnsInfo([new DmColumn(Instance) { type = type, name = "VALUE" }]);
        info.SetHasResultSet(true); info.SetRowCount(rows);
        SetField(statement, "__t02_field_04000927", info);
        var data = new byte[rows][][];
        for (int row = 0; row < rows; row++) data[row] = [[], value ?? Locator(5, newLob: newLobProfile)];
        var cache = new DmResultSetCache(statement, 1, rows) { datas = data, datasStartPos = 0 };
        SetField(statement, "__t02_field_04000928", cache);
        var reader = new DmDataReader(cache, info, behavior);
        reader.AttachExecutionLease(Lease, ownsLease: false);
        Assert.True(reader.Read());
        return reader;
    }

    internal void ReleaseAll() { foreach (var reply in Channel.Replies) reply.Release.TrySetResult(true); }

    internal DmBlob Blob(long length)
    {
        var result = new DmBlob(Locator(length, newLob: newLobProfile), Instance, new DmColumn(Instance), false);
        result.AttachExecutionLease(Lease);
        return result;
    }

    internal DmClob Clob(long length, byte[]? inline = null)
    {
        // The current inline decoder has the full new-locator metadata prefix.
        Instance.ConnProperty.NewLobFlag = inline != null || newLobProfile;
        var result = new DmClob(Locator(length, inline, newLob: newLobProfile), Instance, new DmColumn(Instance), false);
        Instance.ConnProperty.NewLobFlag = newLobProfile;
        result.AttachExecutionLease(Lease);
        return result;
    }

    internal static byte[] Locator(long length, byte[]? inline = null, bool newLob = false)
    {
        int header = inline != null || newLob ? 47 : 21;
        byte[] result = new byte[header + (inline?.Length ?? 0)];
        result[0] = inline == null ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(9), checked((int)length));
        if (header == 47)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(21), 101);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(25), 3);
            // msg21's extended locator contains a complete 12-byte row identifier.
            for (int index = 0; index < 12; index++) result[27 + index] = (byte)(index + 1);
        }
        inline?.CopyTo(result, header);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        Lease.Dispose();
        await Connection.DisposeAsync();
    }

    internal static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}

internal sealed class OutputLobChannel : IDmByteChannel
{
    internal sealed class Reply(byte[] frame)
    {
        internal byte[] Frame { get; } = frame;
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal List<Reply> Replies { get; } = [];
    internal List<short> Commands { get; } = [];
    internal List<long> ReadPositions { get; } = [];
    internal List<int> CursorFiles { get; } = [];
    internal List<long> LocatorIds { get; } = [];
    internal List<long> CursorTotals { get; } = [];
    internal List<int> ReadLengths { get; } = [];
    internal List<OperationIdentity> Identities { get; } = [];
    internal List<int> RemainingBudgets { get; } = [];
    internal Action? AfterHeader { get; set; }
    internal int SynchronousCalls { get; private set; }
    private int received;
    private int frameOffset;
    public bool IsClosed { get; private set; }

    internal void AddLength(long length)
    {
        byte[] body = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(body, length);
        Replies.Add(new Reply(Frame(29, body)));
    }

    internal void AddData(byte[] payload, long serverCharacters, bool readOver = false, short nextFile = 0, long nextTotal = 0)
    {
        // readOver, byte count, file/page/total locator update, bytes, server units.
        byte[] body = new byte[19 + payload.Length + (serverCharacters < 0 ? 0 : 4)];
        body[0] = readOver ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), payload.Length);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(5), nextFile);
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(11), nextTotal);
        payload.CopyTo(body, 19);
        if (serverCharacters >= 0) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(19 + payload.Length), checked((uint)serverCharacters));
        Replies.Add(new Reply(Frame(32, body)));
    }

    internal void AddRawDataBody(byte[] body) => AddRawReply(32, body);
    internal void AddRawReply(short command, byte[] body) => Replies.Add(new Reply(Frame(command, body)));

    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        SynchronousCalls++;
        return SendAsync(buffer, offset, count, default).GetAwaiter().GetResult();
    }
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        SynchronousCalls++;
        return ReceiveAsync(buffer, offset, count, default).GetAwaiter().GetResult();
    }
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        short command = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
        Commands.Add(command);
        Identities.Add(DmInvocation.Current.Identity);
        RemainingBudgets.Add(DmInvocation.Current.Deadline.RemainingMilliseconds);
        if (command == 32)
        {
            LocatorIds.Add(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 71)));
            CursorFiles.Add(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 87)));
            CursorTotals.Add(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 93)));
            ReadPositions.Add(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 101)));
            ReadLengths.Add(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset + 109)));
        }
        return ValueTask.FromResult(count);
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Reply reply = Replies[received];
        reply.Entered.TrySetResult(true);
        bool released = await reply.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!released) return 0;
        int copied = Math.Min(count, reply.Frame.Length - frameOffset);
        reply.Frame.AsSpan(frameOffset, copied).CopyTo(buffer.AsSpan(offset, copied));
        frameOffset += copied;
        if (frameOffset == 64) AfterHeader?.Invoke();
        if (frameOffset == reply.Frame.Length) { received++; frameOffset = 0; }
        return copied;
    }
    public void Dispose()
    {
        IsClosed = true;
        foreach (var reply in Replies) reply.Release.TrySetResult(false);
    }

    private static byte[] Frame(short command, byte[] body)
    {
        byte[] frame = new byte[64 + body.Length];
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), command);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
        for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
        body.CopyTo(frame, 64);
        return frame;
    }
}
