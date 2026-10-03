using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.LobTests;

[CollectionDefinition("Input LOB hooks", DisableParallelization = true)]
public sealed class InputLobCollection { }

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class InputStreamTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    public async Task PositiveShortReadFillsChunkWithoutLengthSeekOrCallerDisposal(bool asynchronous, int shortRead)
    {
        byte[] expected = Enumerable.Range(0, 29).Select(i => (byte)i).ToArray();
        var source = new UnknownInputStream(expected, shortRead, asynchronous);
        var input = new DmLobInput(source, DmDbType.Blob);
        using var cursor = input.OpenCursor(8, "UTF-8");
        var actual = new List<byte>();
        var chunks = new List<int>();
        while (true)
        {
            int count = asynchronous ? await cursor.ReadChunkAsync() : cursor.ReadChunk();
            chunks.Add(count); actual.AddRange(cursor.Buffer.AsSpan(0, count).ToArray());
            if (count < 8 && cursor.EndOfInput) break;
        }
        Assert.Equal(new[] { 8, 8, 8, 5 }, chunks);
        Assert.Equal(expected, actual);
        Assert.Equal(29, cursor.ConsumedBytes);
        Assert.Equal(8, cursor.PeakOwnedBufferBytes);
        cursor.Dispose();
        Assert.False(source.Disposed);
        Assert.Equal(asynchronous ? 0 : source.ReadCalls, source.SyncReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryIndependentTextVectorAndEveryUtf16ReadBoundaryHasStrictExpectedBytes(bool asynchronous)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        foreach (var vector in document.RootElement.GetProperty("small_fixed_text_vectors").EnumerateArray())
        {
            string text = vector.GetProperty("text").GetString()!;
            string charset = vector.GetProperty("reference_encoding_name").GetString()!;
            byte[] expected = Convert.FromHexString(vector.GetProperty("encoded_bytes_hex").GetString()!);
            for (int split = 1; split <= text.Length; split++)
            {
                var source = new UnknownTextInput(text, split, asynchronous);
                using var cursor = new DmLobInput(source, DmDbType.Clob).OpenCursor(3, charset);
                var actual = new List<byte>();
                while (true)
                {
                    int count = asynchronous ? await cursor.ReadChunkAsync() : cursor.ReadChunk();
                    actual.AddRange(cursor.Buffer.AsSpan(0, count).ToArray());
                    if (count < 3 && cursor.EndOfInput) break;
                }
                Assert.Equal(expected, actual);
                Assert.Equal(text.Length, cursor.ConsumedCharacters);
                Assert.Equal(expected.Length, cursor.ConsumedBytes);
                Assert.False(source.Disposed);
                Assert.Equal(asynchronous ? 0 : source.ReadCalls, source.SyncReads);
                Assert.True(cursor.PeakOwnedBufferBytes < 128);
            }
        }
    }

    [Theory]
    [InlineData("UTF-8", false)]
    [InlineData("UTF-8", true)]
    [InlineData("GB18030", false)]
    [InlineData("GB18030", true)]
    public async Task LoneSurrogateAtFinalFlushFailsWithoutReplacementOrDisposal(string charset, bool asynchronous)
    {
        var source = new UnknownTextInput("prefix\ud83d", 1, asynchronous);
        using var cursor = new DmLobInput(source, DmDbType.Clob).OpenCursor(4, charset);
        if (asynchronous)
            await Assert.ThrowsAsync<System.Text.EncoderFallbackException>(async () =>
            { while (true) { int count = await cursor.ReadChunkAsync(); if (count < 4 && cursor.EndOfInput) break; } });
        else Assert.Throws<System.Text.EncoderFallbackException>(() =>
            { while (true) { int count = cursor.ReadChunk(); if (count < 4 && cursor.EndOfInput) break; } });
        Assert.False(source.Disposed);
    }

    [Fact]
    [Trait("Evidence", "SyntheticBoundedMemory")]
    public async Task OneGiBPlusOneUsesOneControlledBufferAndIndependentIncrementalHash()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        var vector = document.RootElement.GetProperty("large_logical_stream_descriptor");
        long length = vector.GetProperty("length_bytes").GetInt64();
        var generator = vector.GetProperty("generator");
        int seed = generator.GetProperty("seed").GetInt32(), stride = generator.GetProperty("stride").GetInt32();
        var source = new GeneratedInputStream(length, seed, stride);
        using var cursor = new DmLobInput(source, DmDbType.Blob).OpenCursor(16384, "UTF-8");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        while (true)
        {
            int count = await cursor.ReadChunkAsync();
            hash.AppendData(cursor.Buffer.AsSpan(0, count)); total = checked(total + count);
            Assert.Equal(16384, cursor.PeakOwnedBufferBytes);
            if (count < 16384 && cursor.EndOfInput) break;
        }
        Assert.Equal(length, total);
        Assert.Equal(length, cursor.ConsumedBytes);
        Assert.Equal(vector.GetProperty("expected_incremental_hash").GetProperty("expected_sha256_hex").GetString(),
            Convert.ToHexStringLower(hash.GetHashAndReset()));
        Assert.False(source.Disposed);
        Assert.Equal(0, source.SyncReads);
    }
}

internal sealed class UnknownInputStream(byte[] data, int shortRead = 1, bool asyncOnly = true) : Stream
{
    private int offset;
    internal bool Disposed;
    internal int SyncReads, AsyncReads;
    internal int ReadCalls => SyncReads + AsyncReads;
    public override bool CanRead => true;
    public override bool CanSeek => throw new InvalidOperationException("CanSeek must not be inspected.");
    public override bool CanWrite => false;
    public override long Length => throw new InvalidOperationException("Length must not be inspected.");
    public override long Position { get => throw new InvalidOperationException("Position must not be inspected."); set => throw new InvalidOperationException("Position must not be set."); }
    private int Next(Span<byte> buffer)
    { int count = Math.Min(Math.Min(shortRead, buffer.Length), data.Length - offset); data.AsSpan(offset, count).CopyTo(buffer); offset += count; return count; }
    public override int Read(byte[] buffer, int at, int count)
    { SyncReads++; if (asyncOnly) throw new InvalidOperationException("Sync input read forbidden."); return Next(buffer.AsSpan(at, count)); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); AsyncReads++; return ValueTask.FromResult(Next(buffer.Span)); }
    protected override void Dispose(bool disposing) { Disposed = true; throw new InvalidOperationException("The caller owns this input."); }
    public override long Seek(long at, SeekOrigin origin) => throw new InvalidOperationException("Seek forbidden.");
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long length) => throw new NotSupportedException();
    public override void Write(byte[] bytes, int at, int count) => throw new NotSupportedException();
}
internal sealed class UnknownTextInput(string text, int shortRead = 1, bool asyncOnly = true) : TextReader
{
    private int offset;
    internal bool Disposed;
    internal int SyncReads, AsyncReads;
    internal int ReadCalls => SyncReads + AsyncReads;
    private int Next(Span<char> buffer)
    { int count = Math.Min(Math.Min(shortRead, buffer.Length), text.Length - offset); text.AsSpan(offset, count).CopyTo(buffer); offset += count; return count; }
    public override int Read(char[] buffer, int at, int count)
    { SyncReads++; if (asyncOnly) throw new InvalidOperationException("Sync text read forbidden."); return Next(buffer.AsSpan(at, count)); }
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); AsyncReads++; return ValueTask.FromResult(Next(buffer.Span)); }
    public override string ReadToEnd() => throw new InvalidOperationException("Full text materialization forbidden.");
    protected override void Dispose(bool disposing) { Disposed = true; throw new InvalidOperationException("The caller owns this input."); }
}
internal sealed class GeneratedInputStream(long length, int seed, int stride) : Stream
{
    private long offset;
    internal bool Disposed;
    internal int SyncReads;
    public override bool CanRead => true;
    public override bool CanSeek => throw new InvalidOperationException("CanSeek forbidden.");
    public override bool CanWrite => false;
    public override long Length => throw new InvalidOperationException("Length forbidden.");
    public override long Position { get => throw new InvalidOperationException("Position forbidden."); set => throw new InvalidOperationException("Position forbidden."); }
    public override int Read(byte[] bytes, int at, int count) { SyncReads++; throw new InvalidOperationException("Sync read forbidden."); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        int count = (int)Math.Min(buffer.Length, length - offset);
        for (int index = 0; index < count; index++) buffer.Span[index] = unchecked((byte)(seed + stride * (offset + index)));
        offset += count; return ValueTask.FromResult(count);
    }
    protected override void Dispose(bool disposing) { Disposed = true; throw new InvalidOperationException("Caller owns generated input."); }
    public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Seek forbidden.");
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long length) => throw new NotSupportedException();
    public override void Write(byte[] bytes, int at, int count) => throw new NotSupportedException();
}

internal sealed class InputSessionFixture : IDisposable
{
    internal readonly DmConnection Connection;
    internal readonly DmSession Session;
    internal readonly DmConnInstance Instance;
    internal readonly DmCommand Command;
    internal readonly Statement Statement;
    internal InputSessionFixture(IDmByteChannel channel, int chunk = 8, string charset = "UTF-8", short messageVersion = 21)
    {
        Connection = new DmConnection("server=synthetic.invalid;user=LOB_SYNTH;password=synthetic_only;transport_security=PlaintextAllowed");
        Session = new DmSession();
        SetField(Connection, "session", Session);
        Session.BeginConnecting();
        using (var lease = Session.BeginExecution(DmOperationPurpose.Handshake))
        using (var invocation = lease.BeginInvocation())
        {
            Instance = new DmConnInstance(Connection);
            Connection.m_ConnInst = Instance;
            Instance.ConnProperty.ServerVersion = "8.1.4.6";
            Instance.ConnProperty.msgVersion = messageVersion;
            Instance.ConnProperty.ServerEncoding = charset;
            Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = chunk;
            object wire = Instance.GetCsi().A();
            ((DmTransport)GetField(wire, "transport")!).Dispose();
            SetField(wire, "transport", new DmTransport(channel));
            SetField(wire, "__t02_field_04000AAD", false);
            SetField(Instance.GetCsi(), "__t02_field_04000ABD", false);
            Session.CompleteHandshake();
        }
        Connection.do_State = ConnectionState.Open;
        Command = new DmCommand("INSERT INTO SYNTHETIC VALUES (:p)", Connection);
        Statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
        SetField(Statement, "__t02_field_04000923", Instance);
        SetField(Statement, "__t02_field_04000924", Instance.GetCsi());
        SetField(Statement, "__t02_field_04000933", Command);
        SetField(Statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
        SetField(Statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
        SetField(Statement, "__t02_field_04000927", new DmInfo(Instance));
    }
    internal static object? GetField(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);
    internal static void SetField(object owner, string name, object? value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
    public void Dispose()
    {
        Command.Dispose(); Connection.Dispose();
        DmLobInputTestHooks.AfterChunk = null;
    }
}
