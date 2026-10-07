using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class LobMaterializationAsyncTests
{
    [Fact]
    public async Task BlobMaterializationWaitsAtEveryChunkWithoutSynchronousIo()
    {
        await using var fixture = new LobAsyncFixture();
        DmBlob blob = fixture.Blob(5);
        fixture.Channel.AddData([0x00, 0xAB], 2);
        fixture.Channel.AddData([0xFF, 0x42], 2);
        fixture.Channel.AddData([0x7F], 1, readOver: true);
        Task<byte[]> read = blob.do_getBytesAsync(1, 5, default);
        for (int chunk = 0; chunk < 3; chunk++)
        {
            await fixture.Channel.Replies[chunk].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(read.IsCompleted);
            fixture.Channel.Replies[chunk].Release.SetResult(true);
        }
        Assert.Equal(new byte[] { 0, 0xAB, 0xFF, 0x42, 0x7F }, await read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new long[] { 0, 2, 4 }, fixture.Channel.ReadPositions);
        Assert.Equal(new int[] { 2, 2, 1 }, fixture.Channel.ReadLengths);
        Assert.Single(fixture.Channel.Identities.Distinct());
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Fact]
    public async Task ClobLengthQueryAndChunksKeepServerUnitsAndReturnFullUtf16()
    {
        await using var fixture = new LobAsyncFixture();
        DmClob clob = fixture.Clob(5);
        fixture.Channel.AddLength(5);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂"), 2);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("中e"), 2);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("\u0301"), 1, readOver: true);
        Task<string> read = clob.MaterializeStringUnderOwnerAsync(default);
        for (int reply = 0; reply < 4; reply++)
        {
            await fixture.Channel.Replies[reply].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(read.IsCompleted);
            fixture.Channel.Replies[reply].Release.SetResult(true);
        }
        string result = await read.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("A🙂中e\u0301", result);
        Assert.Equal(6, result.Length);
        Assert.Equal(new short[] { 29, 32, 32, 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 0, 2, 4 }, fixture.Channel.ReadPositions);
        Assert.Equal(new int[] { 2, 2, 1 }, fixture.Channel.ReadLengths);
        Assert.Single(fixture.Channel.Identities.Distinct());
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Fact]
    public async Task AClobMaterializationSharesOneDeadlineAcrossLengthAndAllChunks()
    {
        var clock = new LobManualClock();
        await using var fixture = new LobAsyncFixture(clock);
        DmClob clob = fixture.Clob(5);
        fixture.Channel.AddLength(5);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("ab"), 2);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("cd"), 2);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("e"), 1, readOver: true);
        fixture.Channel.AfterHeader = () => clock.Advance(TimeSpan.FromSeconds(3));
        foreach (var reply in fixture.Channel.Replies) reply.Release.SetResult(true);
        var error = await Assert.ThrowsAsync<DmTimeoutException>(() => clob.MaterializeStringUnderOwnerAsync(default));
        Assert.Equal(DmErrorKind.Timeout, error.ErrorKind);
        Assert.Equal("WDM_TIMEOUT", error.DriverErrorCode);
        Assert.Equal(0, error.Number);
        Assert.Equal(DmCancelSource.TotalDeadline, error.FailureInfo.CancelSource);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(new int[] { 10000, 7000, 4000, 1000 }, fixture.Channel.RemainingBudgets);
        Assert.Single(fixture.Channel.Identities.Distinct());
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Fact]
    public async Task OversizedKnownBlobLocatorRejectsBeforeLengthQueryOrChunk()
    {
        await using var fixture = new LobAsyncFixture();
        var blob = fixture.Blob((long)DmConnectionSettings.DefaultMaxMaterializedLobSize + 1);
        await Assert.ThrowsAsync<NotSupportedException>(() => blob.LoadAllDataUnderOwnerAsync(default));
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Fact]
    public async Task PreCancellationDoesNotQueryReadOrBreakTheOwner()
    {
        await using var fixture = new LobAsyncFixture();
        var blob = fixture.Blob(5);
        var clob = fixture.Clob(5);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blob.do_getBytesAsync(1, 5, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clob.MaterializeStringUnderOwnerAsync(cancellation.Token));
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        using var available = fixture.Lease.BeginInvocation();
    }

    [Fact]
    public async Task LocalAndInlinePayloadsCompleteWithoutProtocolReads()
    {
        await using var fixture = new LobAsyncFixture();
        var blob = new DmBlob(new byte[] { 1, 2, 3 }, fixture.Instance);
        Task<byte[]> bytes = blob.do_getBytesAsync(2, 2, default);
        Assert.True(bytes.IsCompletedSuccessfully);
        Assert.Equal(new byte[] { 2, 3 }, await bytes);
        var clob = new DmClob("中🙂e\u0301", fixture.Instance);
        Task<string> text = clob.MaterializeStringUnderOwnerAsync(default);
        Assert.True(text.IsCompletedSuccessfully);
        Assert.Equal("中🙂e\u0301", await text);
        Assert.Equal(Encoding.UTF8.GetBytes("中🙂e\u0301"), await clob.MaterializeBytesUnderOwnerAsync(default));
        byte[] payload = Encoding.UTF8.GetBytes("A中🙂");
        var inline = fixture.Clob(payload.Length, payload);
        Assert.Equal("A中🙂", await inline.MaterializeStringUnderOwnerAsync(default));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task RemoteLocatorWithoutOwnerRejectsBeforeNetworkAccess()
    {
        await using var fixture = new LobAsyncFixture();
        var blob = new DmBlob(LobAsyncFixture.Locator(5), fixture.Instance, new DmColumn(fixture.Instance), false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => blob.do_getBytesAsync(1, 5, default));
        Assert.Empty(fixture.Channel.Commands);
    }
}

// Uses the real LOB codec and protocol path, replacing only the byte channel.
internal sealed class LobAsyncFixture : IAsyncDisposable
{
    internal DmConnection Connection { get; } = new(new DmConnectionStringBuilder
    {
        Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only"
    }.ConnectionString);
    internal DmSession Session { get; } = new();
    internal DmConnInstance Instance { get; }
    internal DmExecutionLease Lease { get; }
    internal LobReplyChannel Channel { get; } = new();

    internal LobAsyncFixture(TimeProvider? clock = null)
    {
        SetField(Connection, "session", Session);
        Session.BeginConnecting();
        using (var handshake = Session.BeginExecution(DmOperationPurpose.Handshake))
        using (var invocation = handshake.BeginInvocation())
        {
            Instance = new DmConnInstance(Connection);
            Connection.m_ConnInst = Instance;
            Instance.ConnProperty.ServerEncoding = "UTF-8";
            Instance.ConnProperty.msgVersion = 21;
            Instance.ConnProperty.NewLobFlag = false;
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

    internal DmBlob Blob(long length)
    {
        var result = new DmBlob(Locator(length), Instance, new DmColumn(Instance), false);
        result.AttachExecutionLease(Lease);
        return result;
    }

    internal DmClob Clob(long length, byte[]? inline = null)
    {
        // The current inline decoder has the full new-locator metadata prefix.
        Instance.ConnProperty.NewLobFlag = inline != null;
        var result = new DmClob(Locator(length, inline), Instance, new DmColumn(Instance), false);
        Instance.ConnProperty.NewLobFlag = false;
        result.AttachExecutionLease(Lease);
        return result;
    }

    internal static byte[] Locator(long length, byte[]? inline = null)
    {
        int header = inline == null ? 21 : 47;
        byte[] result = new byte[header + (inline?.Length ?? 0)];
        result[0] = inline == null ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(9), checked((int)length));
        inline?.CopyTo(result, header);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        Lease.Dispose();
        await Connection.DisposeAsync();
    }

    private static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}

internal sealed class LobReplyChannel : IDmByteChannel
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

    internal void AddData(byte[] payload, uint serverCharacters, bool readOver = false)
    {
        // readOver, byte count, file/page/total locator update, bytes, server units.
        byte[] body = new byte[19 + payload.Length + 4];
        body[0] = readOver ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), payload.Length);
        payload.CopyTo(body, 19);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(19 + payload.Length), serverCharacters);
        Replies.Add(new Reply(Frame(32, body)));
    }

    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        SynchronousCalls++;
        throw new InvalidOperationException("Synchronous send is forbidden in LOB async tests.");
    }
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
    {
        SynchronousCalls++;
        throw new InvalidOperationException("Synchronous receive is forbidden in LOB async tests.");
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

internal sealed class LobManualClock : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => ticks;
    internal void Advance(TimeSpan elapsed) => ticks += (long)elapsed.TotalMilliseconds;
}
