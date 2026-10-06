using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.LobTests;

[CollectionDefinition("R3 opaque CLOB materialization", DisableParallelization = true)]
public sealed class R3ClobOpaqueLengthMaterializationCollection { }

[Collection("R3 opaque CLOB materialization")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class R3ClobOpaqueLengthMaterializationTests
{
    private const string Text = "A🚂中Z";

    [Theory]
    [InlineData("UTF-8", false, 33554433L)]
    [InlineData("UTF-8", true, 33554433L)]
    [InlineData("GB18030", false, 33554433L)]
    [InlineData("GB18030", true, 33554433L)]
    [InlineData("UTF-8", false, 2147483648L)]
    [InlineData("UTF-8", true, 2147483648L)]
    [InlineData("GB18030", false, 2147483648L)]
    [InlineData("GB18030", true, 2147483648L)]
    [InlineData("UTF-8", false, long.MaxValue)]
    [InlineData("UTF-8", true, long.MaxValue)]
    [InlineData("GB18030", false, long.MaxValue)]
    [InlineData("GB18030", true, long.MaxValue)]
    [InlineData("UTF-8", false, -1L)]
    [InlineData("UTF-8", true, -1L)]
    [InlineData("GB18030", false, -1L)]
    [InlineData("GB18030", true, -1L)]
    public async Task WholeStringBytesAndFetchAllBoundDecodedPayloadInsteadOfOpaqueLocator(string charset, bool asynchronous, long length)
    {
        foreach (string operation in new[] { "string", "bytes", "fetchAll" })
        {
            await using var fixture = new OpaqueFixture(charset);
            var clob = fixture.Clob(length);
            Assert.Equal(length, clob.bytesLength);
            byte[] expected = fixture.ScriptText(length);
            if (operation == "string")
                Assert.Equal(Text, asynchronous ? await clob.MaterializeStringUnderOwnerAsync(default) : clob.MaterializeStringUnderOwner());
            else if (operation == "bytes")
                Assert.Equal(expected, asynchronous ? await clob.MaterializeBytesUnderOwnerAsync(default) : clob.MaterializeBytesUnderOwner());
            else
            {
                if (asynchronous) await clob.LoadAllDataUnderOwnerAsync(default);
                else clob.loadAllData();
                Assert.True(clob.fetchAll);
                Assert.Equal(Text, clob.data);
                Assert.Equal(Text.Length, clob.length());
                Assert.Equal(Text, clob.MaterializeStringUnderOwner());
            }
            Assert.Equal(new short[] { 29, 32, 32 }, fixture.Channel.Commands);
            Assert.Equal(new long[] { 0, 37 }, fixture.Channel.ReadPositions);
            Assert.Single(fixture.Channel.Identities.Distinct());
            Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
            if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
            // The completed private cursor must leave the caller's execution owner available.
            using var available = fixture.Lease.BeginInvocation();
        }
    }

    [Theory]
    [InlineData("UTF-8", 33554433L)]
    [InlineData("GB18030", 33554433L)]
    [InlineData("UTF-8", long.MaxValue)]
    [InlineData("GB18030", long.MaxValue)]
    public async Task PublicFullGettersReturnCompleteTextForHugeOpaqueLocators(string charset, long length)
    {
        foreach (string getter in new[] { "GetString", "GetValue", "GetFieldValue", "GetFieldValueAsync", "GetBytesLength" })
        {
            await using var fixture = new OpaqueFixture(charset);
            var reader = fixture.Reader(length);
            byte[] expected = fixture.ScriptText(length);
            switch (getter)
            {
                case "GetString": Assert.Equal(Text, reader.GetString(0)); break;
                case "GetValue": Assert.Equal(Text, reader.GetValue(0)); break;
                case "GetFieldValue": Assert.Equal(Text, reader.GetFieldValue<string>(0)); break;
                case "GetFieldValueAsync": Assert.Equal(Text, await reader.GetFieldValueAsync<string>(0, default)); break;
                case "GetBytesLength": Assert.Equal(expected.Length, reader.GetBytes(0, 0, null!, 0, 0)); break;
            }
            Assert.Equal(new short[] { 29, 32, 32 }, fixture.Channel.Commands);
            Assert.Equal(new long[] { 0, 37 }, fixture.Channel.ReadPositions);
            if (getter == "GetFieldValueAsync") Assert.Equal(0, fixture.Channel.SynchronousCalls);
        }
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("GB18030")]
    public async Task ConstructorFetchAllUsesTheCurrentCallerInvocationAndCachesOnlyDecodedText(string charset)
    {
        await using var fixture = new OpaqueFixture(charset);
        fixture.ScriptText(long.MaxValue);
        using var owner = fixture.Lease.BeginInvocation();
        var clob = new DmClob(OpaqueFixture.Locator(long.MaxValue), fixture.Instance, new DmColumn(fixture.Instance), true);
        Assert.True(clob.fetchAll);
        Assert.Equal(Text, clob.data);
        Assert.Equal(Text.Length, clob.m_length);
        Assert.Equal(long.MaxValue, clob.bytesLength);
        Assert.Equal(new short[] { 29, 32, 32 }, fixture.Channel.Commands);
        Assert.All(fixture.Channel.Identities, identity => Assert.Equal(owner.Identity, identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedWholeMaterializationRejectsAnOldReaderRowBeforeIo(bool asynchronous)
    {
        await using var fixture = new OpaqueFixture();
        var reader = fixture.Reader(long.MaxValue, rows: 2);
        var clob = reader.GetClob(0);
        fixture.ScriptText(long.MaxValue);
        if (asynchronous) await clob.LoadAllDataUnderOwnerAsync(default);
        else clob.loadAllData();
        Assert.True(reader.Read());
        int requests = fixture.Channel.Commands.Count;
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => clob.MaterializeStringUnderOwnerAsync(default));
        else Assert.Throws<InvalidOperationException>(() => clob.MaterializeStringUnderOwner());
        Assert.Equal(requests, fixture.Channel.Commands.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnownedWholeMaterializationRejectsHugeLocatorBeforeIo(bool asynchronous)
    {
        await using var fixture = new OpaqueFixture();
        var clob = new DmClob(OpaqueFixture.Locator(long.MaxValue), fixture.Instance, new DmColumn(fixture.Instance), false);
        if (asynchronous) await Assert.ThrowsAsync<InvalidOperationException>(() => clob.MaterializeStringUnderOwnerAsync(default));
        else Assert.Throws<InvalidOperationException>(() => clob.MaterializeStringUnderOwner());
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task PreCancellationPreservesTokenOwnerAndEmptyCacheBeforeLengthQuery()
    {
        await using var fixture = new OpaqueFixture();
        var clob = fixture.Clob(long.MaxValue);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clob.LoadAllDataUnderOwnerAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(clob.fetchAll);
        Assert.Equal("", clob.data);
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        using var available = fixture.Lease.BeginInvocation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NegativeGetLengthStillRejectsBeforePayloadReadOrCacheSuccess(bool asynchronous)
    {
        await using var fixture = new OpaqueFixture();
        var clob = fixture.Clob(long.MaxValue);
        fixture.Channel.AddLength(-1);
        if (asynchronous) await Assert.ThrowsAsync<InvalidDataException>(() => clob.LoadAllDataUnderOwnerAsync(default));
        else Assert.Throws<InvalidDataException>(() => clob.loadAllData());
        Assert.Equal(new short[] { 29 }, fixture.Channel.Commands);
        Assert.False(clob.fetchAll);
        Assert.Equal("", clob.data);
        if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDecodedPayloadAtTheInclusiveCapSucceedsWithBoundedSyntheticFrames(bool asynchronous)
    {
        int cap = DmConnectionSettings.DefaultMaxMaterializedLobSize / sizeof(char);
        await using var fixture = new OpaqueFixture();
        fixture.Channel.LogicalCharacters = cap;
        var clob = fixture.Clob(long.MaxValue);
        string actual = asynchronous ? await clob.MaterializeStringUnderOwnerAsync(default) : clob.MaterializeStringUnderOwner();
        Assert.Equal(cap, actual.Length);
        Assert.Equal('x', actual[0]);
        Assert.Equal('x', actual[^1]);
        Assert.Equal(cap, fixture.Channel.GeneratedCharacters);
        Assert.Equal(4096, fixture.Channel.DataRequests);
        Assert.Equal(1, fixture.Channel.LengthRequests);
        Assert.True(fixture.Channel.PeakFrameBytes <= 64 + 19 + 8192 + 4);
        if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData((short)29)]
    [InlineData((short)32)]
    public async Task InFlightCancellationNeverCachesPartialTextOrReleasesTheCallerOwner(short blockedCommand)
    {
        await using var fixture = new OpaqueFixture();
        fixture.Channel.BlockedCommand = blockedCommand;
        var clob = fixture.Clob(long.MaxValue);
        fixture.ScriptText(long.MaxValue);
        using var cancellation = new CancellationTokenSource();
        Task load = clob.LoadAllDataUnderOwnerAsync(cancellation.Token);
        await fixture.Channel.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(load.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(clob.fetchAll);
        Assert.Equal("", clob.data);
        Assert.Equal(blockedCommand == 29 ? new short[] { 29 } : new short[] { 29, 32 }, fixture.Channel.Commands);
        Assert.False(fixture.Lease.IsDisposed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Channel.IsClosed);
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDecodedPayloadOverCapRejectsBeforeTheNextFrameAndDoesNotCacheSuccess(bool asynchronous)
    {
        int cap = DmConnectionSettings.DefaultMaxMaterializedLobSize / sizeof(char);
        await using var fixture = new OpaqueFixture();
        fixture.Channel.LogicalCharacters = cap + 8193;
        var clob = fixture.Clob(long.MaxValue);
        if (asynchronous) await Assert.ThrowsAsync<NotSupportedException>(() => clob.LoadAllDataUnderOwnerAsync(default));
        else Assert.Throws<NotSupportedException>(() => clob.loadAllData());
        // The frame after the inclusive cap contains one character. Its remaining
        // logical tail must never be requested after the pre-append cap rejects it.
        Assert.Equal(cap + 1, fixture.Channel.GeneratedCharacters);
        Assert.Equal(4097, fixture.Channel.DataRequests);
        Assert.Equal(1, fixture.Channel.LengthRequests);
        Assert.True(fixture.Channel.PeakFrameBytes <= 64 + 19 + 8192 + 4);
        Assert.False(clob.fetchAll);
        Assert.Equal("", clob.data);
        if (asynchronous) Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    private sealed class OpaqueFixture : IAsyncDisposable
    {
        private readonly DmConnection connection = new(new DmConnectionStringBuilder
        { Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only" }.ConnectionString);
        internal DmSession Session { get; } = new();
        internal DmConnInstance Instance { get; }
        internal DmExecutionLease Lease { get; }
        internal ScriptChannel Channel { get; } = new();

        internal OpaqueFixture(string charset = "UTF-8")
        {
            SetField(connection, "session", Session);
            Session.BeginConnecting();
            using (var handshake = Session.BeginExecution(DmOperationPurpose.Handshake))
            using (var invocation = handshake.BeginInvocation())
            {
                Instance = new DmConnInstance(connection);
                connection.m_ConnInst = Instance;
                Instance.ConnProperty.ServerEncoding = charset;
                Instance.ConnProperty.msgVersion = 21;
                Instance.ConnProperty.NewLobFlag = true;
                Instance.ConnProperty.LongLobFlag = true;
                Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = 8192;
                var protocol = Instance.GetCsi();
                var wire = protocol.A();
                var field = wire.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((DmTransport)field.GetValue(wire)!).Dispose();
                field.SetValue(wire, new DmTransport(Channel));
                SetField(wire, "__t02_field_04000AAD", false);
                SetField(protocol, "__t02_field_04000ABD", false);
                Session.CompleteHandshake();
            }
            connection.do_State = ConnectionState.Open;
            Lease = Session.BeginExecution(DmOperationPurpose.Reader);
        }

        internal DmClob Clob(long length)
        {
            var clob = new DmClob(Locator(length), Instance, new DmColumn(Instance), false);
            clob.AttachExecutionLease(Lease);
            return clob;
        }

        internal byte[] ScriptText(long locatorLength)
        {
            byte[] bytes = DmTextCodec.CreateStrictEncoding(Instance.ConnProperty.ServerEncoding).GetBytes(Text);
            Channel.AddLength(locatorLength < 0 ? 48 : locatorLength);
            // Split the supplementary character's multibyte sequence in both charsets.
            Channel.AddData(bytes[..2], 37, false);
            Channel.AddData(bytes[2..], 11, true);
            return bytes;
        }

        internal DmDataReader Reader(long length, int rows = 1)
        {
            var command = new DmCommand("synthetic", connection);
            var statement = (W.Dm.Internal.Legacy.A.A)RuntimeHelpers.GetUninitializedObject(typeof(W.Dm.Internal.Legacy.A.A));
            SetField(statement, "__t02_field_04000923", Instance);
            SetField(statement, "__t02_field_04000924", Instance.GetCsi());
            SetField(statement, "__t02_field_04000933", command);
            SetField(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
            SetField(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
            var info = new DmInfo(Instance);
            info.SetColumnsInfo([new DmColumn(Instance) { type = 19, name = "VALUE" }]);
            info.SetHasResultSet(true);
            info.SetRowCount(rows);
            SetField(statement, "__t02_field_04000927", info);
            var data = new byte[rows][][];
            for (int row = 0; row < rows; row++) data[row] = [[], Locator(length)];
            var cache = new DmResultSetCache(statement, 1, rows) { datas = data, datasStartPos = 0 };
            SetField(statement, "__t02_field_04000928", cache);
            var reader = new DmDataReader(cache, info, CommandBehavior.Default);
            reader.AttachExecutionLease(Lease, ownsLease: false);
            Assert.True(reader.Read());
            return reader;
        }

        internal static byte[] Locator(long length)
        {
            byte[] bytes = new byte[47];
            bool longRow = length > int.MaxValue;
            bytes[0] = longRow ? (byte)4 : (byte)2;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), longRow ? 0 : checked((int)length));
            if (longRow) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(39), length);
            return bytes;
        }

        public async ValueTask DisposeAsync()
        {
            Lease.Dispose();
            await connection.DisposeAsync();
        }

        private static void SetField(object target, string name, object? value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
    }

    // Real protocol/strict decoder; only the transport bytes are scripted. Large
    // input is generated per request, so fixture retention stays under one frame.
    private sealed class ScriptChannel : IDmByteChannel
    {
        private readonly Queue<byte[]> replies = new();
        private byte[] response = [];
        private int offset;
        private long wirePosition;
        internal readonly List<short> Commands = [];
        internal readonly List<long> ReadPositions = [];
        internal readonly List<OperationIdentity> Identities = [];
        internal int SynchronousCalls, LogicalCharacters, GeneratedCharacters, DataRequests, LengthRequests, PeakFrameBytes;
        internal short BlockedCommand;
        internal readonly TaskCompletionSource<bool> Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private short command;
        public bool IsClosed { get; private set; }

        internal void AddLength(long length)
        {
            byte[] body = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(body, length);
            replies.Enqueue(Frame(29, body));
        }

        internal void AddData(byte[] payload, uint advance, bool end) => replies.Enqueue(DataFrame(payload, advance, end));

        public int Send(byte[] bytes, int start, int count, int timeoutMilliseconds)
        { SynchronousCalls++; return SendCore(bytes, start, count); }
        public int Receive(byte[] bytes, int start, int count, int timeoutMilliseconds)
        { SynchronousCalls++; return ReceiveCore(bytes, start, count); }
        public ValueTask<int> SendAsync(byte[] bytes, int start, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(bytes, start, count)); }
        public async ValueTask<int> ReceiveAsync(byte[] bytes, int start, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (BlockedCommand == command)
            {
                Blocked.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }
            return ReceiveCore(bytes, start, count);
        }

        private int SendCore(byte[] bytes, int start, int count)
        {
            short operation = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(start + 4));
            command = operation;
            Commands.Add(operation);
            Identities.Add(DmInvocation.Current.Identity);
            if (operation == 29) LengthRequests++;
            else
            {
                Assert.Equal((short)32, operation);
                long position = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(start + 101));
                ReadPositions.Add(position);
                DataRequests++;
                if (LogicalCharacters > 0) Assert.Equal(wirePosition, position);
            }
            if (LogicalCharacters == 0) response = replies.Dequeue();
            else if (operation == 29)
            {
                byte[] body = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(body, long.MaxValue);
                response = Frame(29, body);
            }
            else
            {
                int cap = DmConnectionSettings.DefaultMaxMaterializedLobSize / sizeof(char);
                int emitted = GeneratedCharacters == cap ? 1 : Math.Min(8192, LogicalCharacters - GeneratedCharacters);
                byte[] payload = new byte[emitted];
                payload.AsSpan().Fill((byte)'x');
                GeneratedCharacters += emitted;
                response = DataFrame(payload, 37, GeneratedCharacters == LogicalCharacters);
                wirePosition += 37;
            }
            offset = 0;
            PeakFrameBytes = Math.Max(PeakFrameBytes, response.Length);
            return count;
        }

        private int ReceiveCore(byte[] bytes, int start, int count)
        {
            int copied = Math.Min(count, response.Length - offset);
            response.AsSpan(offset, copied).CopyTo(bytes.AsSpan(start, copied));
            offset += copied;
            return copied;
        }

        private static byte[] DataFrame(byte[] payload, uint advance, bool end)
        {
            byte[] body = new byte[19 + payload.Length + 4];
            body[0] = end ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), payload.Length);
            payload.CopyTo(body, 19);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(19 + payload.Length), advance);
            return Frame(32, body);
        }

        private static byte[] Frame(short operation, byte[] body)
        {
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), operation);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
            body.CopyTo(frame, 64);
            return frame;
        }

        public void Dispose() { IsClosed = true; response = []; replies.Clear(); release.TrySetResult(true); }
    }
}
