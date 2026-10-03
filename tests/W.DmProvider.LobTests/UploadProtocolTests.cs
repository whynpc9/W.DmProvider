using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class UploadProtocolTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 19)]
    [InlineData(true, 19)]
    public async Task EmptyExactFullAndFinalShortHaveSourceEofSequenceAndLatestAckToken(bool asynchronous, int length)
    {
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var channel = new UploadRecordingChannel(asyncOnly: asynchronous);
        using var fixture = new InputSessionFixture(channel);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var source = new UnknownInputStream(data, 1, asynchronous);
        var input = new DmLobInput(source, DmDbType.Blob);
        byte[] token = asynchronous ? await fixture.Instance.GetCsi().UploadStreamingParameterAsync(fixture.Statement, 0, input, CancellationToken.None) :
            fixture.Instance.GetCsi().UploadStreamingParameter(fixture.Statement, 0, input);
        int[] expected = length switch { 0 => [0], 8 => [8, 0], _ => [8, 8, 3] };
        Assert.Equal(expected, channel.ChunkLengths);
        Assert.Equal(data, channel.Payload);
        Assert.True(channel.AllPreviousTokensMatched);
        Assert.Equal(channel.LastAck, token);
        Assert.Equal(asynchronous ? 0 : channel.Sends, channel.SyncSends);
        Assert.False(source.Disposed);
        invocation.Complete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualParameterEncoderAlwaysOffRowsUnknownInputAndPlacesLastAckInExecuteFrame(bool asynchronous)
    {
        var channel = new UploadRecordingChannel(asyncOnly: asynchronous);
        using var fixture = new InputSessionFixture(channel);
        var source = new UnknownInputStream([1, 2, 3, 4, 5, 6, 7, 8, 9], 1, asynchronous);
        var input = new DmLobInput(source, DmDbType.Blob);
        InputEncoding.SetParameter(fixture, input);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var outer = fixture.Session.BeginWireExchange();
        var frame = new b();
        if (asynchronous) await InputEncoding.EncodeAsync(fixture, frame);
        else InputEncoding.Encode(fixture, frame);
        Assert.Equal(new[] { 8, 1 }, channel.ChunkLengths);
        Assert.Equal((ushort)65529, BinaryPrimitives.ReadUInt16LittleEndian(frame.A().AsSpan(77)));
        Assert.Equal(channel.LastAck, frame.A().AsSpan(79, 21).ToArray());
        Assert.Equal(36, frame.k());
        outer.Complete(); outer.Dispose(); invocation.Complete();
        Assert.False(source.Disposed);
    }

    [Fact]
    [Trait("Evidence", "SyntheticBoundedMemory")]
    public async Task OneGiBProtocolUploadRetainsOnlyCountersOneTokenAndIncrementalHash()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        var vector = document.RootElement.GetProperty("large_logical_stream_descriptor");
        var generator = vector.GetProperty("generator");
        long length = vector.GetProperty("length_bytes").GetInt64();
        var source = new GeneratedInputStream(length, generator.GetProperty("seed").GetInt32(), generator.GetProperty("stride").GetInt32());
        var channel = new UploadRecordingChannel(recordDetails: false, asyncOnly: true);
        using var fixture = new InputSessionFixture(channel, chunk: 16384);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        int peak = 0;
        DmLobInputTestHooks.AfterChunk = (_, _, _, bytes) => peak = Math.Max(peak, bytes);
        byte[] token = await fixture.Instance.GetCsi().UploadStreamingParameterAsync(fixture.Statement, 0,
            new DmLobInput(source, DmDbType.Blob), CancellationToken.None);
        Assert.Equal(length, channel.TotalPayload);
        Assert.Equal(vector.GetProperty("expected_incremental_hash").GetProperty("expected_sha256_hex").GetString(), channel.Digest());
        Assert.Equal(65537, channel.Sends);
        Assert.Equal(0, channel.RetainedPayloadBytes);
        Assert.Empty(channel.ChunkLengths);
        Assert.Equal(16384, peak);
        Assert.True(channel.AllPreviousTokensMatched);
        Assert.Equal(channel.LastAck, token);
        Assert.False(source.Disposed);
        invocation.Complete();
    }
}

internal static class InputEncoding
{
    internal static void SetParameter(InputSessionFixture fixture, DmLobInput input)
    {
        var parameter = new DmParameterInternal(fixture.Instance);
        parameter.SetCType(input.WireType); parameter.SetTypeFlag(1); parameter.SetInOutType(0);
        parameter.GetParamValue()[0].SetStreamingInput(input);
        fixture.Statement.F().SetParaNum(1);
        fixture.Statement.F().SetParamsInfo([parameter]);
        fixture.Statement.b(true); // Describe already established, as in an existing prepared plan.
    }
    internal static void Encode(InputSessionFixture fixture, b frame)
    {
        Action action = () => C.A(frame, fixture.Statement, fixture.Statement.F(), fixture.Instance.ConnProperty);
        var method = typeof(B).GetMethod("EncodeWithParameterUploads", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { method.Invoke(fixture.Instance.GetCsi(), [action]); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
    internal static async Task EncodeAsync(InputSessionFixture fixture, b frame)
    {
        Func<Task> action = () => C.AAsync(frame, fixture.Statement, fixture.Statement.F(), fixture.Instance.ConnProperty);
        var method = typeof(B).GetMethod("EncodeWithParameterUploadsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(fixture.Instance.GetCsi(), [action])!;
    }
}

/// <summary>Independent synthetic payloads with reply261 from the numeric TEST profile observation; no captured tokens.</summary>
internal sealed class UploadRecordingChannel(bool recordDetails = true, bool asyncOnly = false) : IDmByteChannel
{
    private byte[] response = [];
    private int responseOffset;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    internal readonly List<int> ChunkLengths = [];
    internal readonly List<byte> Payload = [];
    // Independent field literal from the source-pinned modern initialization:
    // flag2, Int64 -1, Int32 0, Int16 -1, Int16 -1, Int32 -1.
    // Do not derive this expected token by calling the production encoder.
    internal byte[] LastAck = Convert.FromHexString("02FFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF");
    internal bool AllPreviousTokensMatched = true;
    internal int Sends, SyncSends;
    internal long TotalPayload;
    internal int RetainedPayloadBytes => Payload.Count;
    internal Func<int, byte[]>? ReplyOverride;
    internal Action<int>? AfterSend;
    public bool IsClosed { get; private set; }
    private int SendCore(byte[] buffer, int offset, int count)
    {
        Sends++;
        Assert.Equal((short)26, BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4)));
        AllPreviousTokensMatched &= buffer.AsSpan(offset + 64, 21).SequenceEqual(LastAck);
        int length = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset + 85));
        ReadOnlySpan<byte> data = buffer.AsSpan(offset + count - length, length);
        hash.AppendData(data); TotalPayload = checked(TotalPayload + length);
        if (recordDetails) { ChunkLengths.Add(length); Payload.AddRange(data.ToArray()); }
        LastAck = Enumerable.Range(0, 21).Select(index => unchecked((byte)(Sends * 7 + index))).ToArray();
        response = ReplyOverride?.Invoke(Sends) ?? Frame(0, LastAck);
        responseOffset = 0;
        AfterSend?.Invoke(Sends);
        return count;
    }
    internal static byte[] Frame(int status, byte[] body, short opcode = 261)
    {
        byte[] frame = new byte[64 + body.Length];
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), status);
        body.CopyTo(frame, 64);
        for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
        return frame;
    }
    private int ReadCore(byte[] buffer, int offset, int count)
    { int read = Math.Min(count, response.Length - responseOffset); response.AsSpan(responseOffset, read).CopyTo(buffer.AsSpan(offset, read)); responseOffset += read; return read; }
    public int Send(byte[] buffer, int offset, int count, int timeout)
    { SyncSends++; if (asyncOnly) throw new InvalidOperationException("Sync network send forbidden."); return SendCore(buffer, offset, count); }
    public int Receive(byte[] buffer, int offset, int count, int timeout)
    { if (asyncOnly) throw new InvalidOperationException("Sync network receive forbidden."); return ReadCore(buffer, offset, count); }
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, offset, count)); }
    public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReadCore(buffer, offset, count)); }
    internal string Digest() => Convert.ToHexStringLower(hash.GetHashAndReset());
    public void Dispose() { IsClosed = true; hash.Dispose(); }
}
