using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using W.Dm.Internal.Diagnostics;

namespace W.Dm.Internal.Lobs;

// A private locator and decoder belong to one forward-only consumer. Server offsets
// and public UTF-16/byte positions deliberately remain different quantities.
internal sealed class DmLobReadCursor : IDisposable
{
    private readonly AbstractLob locator;
    private readonly DmExecutionLease lease;
    private readonly Action validateOwner;
    private readonly Func<long, int, Data> fetch;
    private readonly Func<long, int, CancellationToken, Task<Data>> fetchAsync;
    private readonly int chunkSize;
    private readonly Decoder decoder;
    private readonly bool text;
    private long knownWireLength = -1;
    private Func<long> getBinaryLength;
    private Func<CancellationToken, Task<long>> getBinaryLengthAsync;
    private ReadOnlyMemory<byte> bytes;
    private int bytePosition;
    private bool wireEnd;
    private readonly bool inlineOnly;
    private bool decoderEnd;
    private readonly char[] chars;
    private int charPosition;
    private int charLength;
    private volatile bool disposed;
    private int entered;
    private long wirePosition;
    internal long Position { get; private set; }
    internal bool IsDisposed => disposed;
    internal int BufferedBytes => bytes.Length + (text ? chars.Length * sizeof(char) : 0);
    internal bool IsRemoteBinary => !text && !inlineOnly;
    internal long KnownWireLength => knownWireLength;

    // Binary wire units are bytes, so a remote range read can start directly at
    // the requested offset instead of transferring and discarding the prefix.
    internal void SeekBinaryTo(long offset)
    {
        if (text) throw new InvalidOperationException("Text LOB wire units are opaque.");
        if (inlineOnly) throw new InvalidOperationException("An inline LOB has no remote wire offset.");
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (Position != 0 || wirePosition != 0 || bytePosition != 0 || wireEnd)
            throw new InvalidOperationException("The LOB flow has already started.");
        wirePosition = offset;
        Position = offset;
    }

    // A seek must compare against a validated length: resolve the deferred
    // length query before the offset is checked or sent as a GET_LOB_DATA.
    internal long EnsureBinaryWireLength()
    {
        if (text) throw new InvalidOperationException("Text LOB wire units are opaque.");
        if (knownWireLength < 0 && getBinaryLength != null) SetKnownWireLength(getBinaryLength());
        return knownWireLength;
    }

    // A validated read at or past the end consumes the flow without a payload
    // request: a retained sequential cursor must report the resolved end so a
    // later lower-offset read is still rejected.
    internal void MarkConsumedToEnd()
    {
        if (text) throw new InvalidOperationException("Text LOB wire units are opaque.");
        if (knownWireLength < 0) throw new InvalidOperationException("The LOB length is not resolved.");
        bytes = default;
        bytePosition = 0;
        wirePosition = knownWireLength;
        Position = knownWireLength;
        wireEnd = true;
    }

    // Test injection still exercises this cursor's unit/decoder/lifetime contract.
    internal DmLobReadCursor(AbstractLob locator, ReadOnlyMemory<byte> inline, bool hasInline,
        DmExecutionLease lease, Action validateOwner, int chunkSize, Func<long, int, Data> fetch,
        Func<long, int, CancellationToken, Task<Data>> fetchAsync, bool text, string encoding)
    {
        this.locator = locator;
        this.lease = lease;
        this.validateOwner = validateOwner;
        if (chunkSize <= 0) throw new InvalidDataException("Invalid negotiated LOB chunk limit.");
        this.chunkSize = chunkSize;
        this.fetch = fetch;
        this.fetchAsync = fetchAsync;
        bytes = hasInline ? inline : default;
        wireEnd = hasInline;
        inlineOnly = hasInline;
        this.text = text;
        chars = text ? new char[8192] : Array.Empty<char>();
        decoder = text ? DmTextCodec.CreateStrictEncoding(encoding).GetDecoder() : null;
    }

    internal static DmLobReadCursor Create(AbstractLob template, ReadOnlyMemory<byte> inline, bool hasInline,
        DmExecutionLease lease, Action validateOwner, bool text, string encoding)
    {
        AbstractLob copy = template.SnapshotForRead();
        var cursor = new DmLobReadCursor(copy, inline, hasInline, lease, validateOwner,
            Math.Min(DmConnectionSettings.DefaultLobChunkSize, template.ConnInstance.ConnProperty.MaxLobDataLenPerMsg),
            (pos, count) => copy.ConnInstance.GetCsi().A(copy, pos, count),
            (pos, count, token) => copy.ConnInstance.GetCsi().ReadLobAsync(copy, pos, count, token), text, encoding);
        if (!text)
        {
            cursor.knownWireLength = hasInline ? inline.Length : copy.bytesLength;
            if (!hasInline && cursor.knownWireLength < 0)
            {
                cursor.getBinaryLength = () => copy.ConnInstance.GetCsi().A(copy);
                cursor.getBinaryLengthAsync = token => copy.ConnInstance.GetCsi().GetLobLengthAsync(copy, token);
            }
        }
        return cursor;
    }

    internal void Validate()
    {
        if (disposed) throw new ObjectDisposedException(nameof(DmLobReadCursor));
        validateOwner?.Invoke();
        if (lease != null && (lease.IsDisposed || !lease.Session.IsCurrentExecution(lease.Identity)))
            throw new InvalidOperationException("LOB reader lease is stale.");
    }

    private DmInvocation Enter(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref entered, 1, 0) != 0)
            throw new InvalidOperationException("Another read owns this LOB flow.");
        try
        {
            Validate();
            var invocation = lease != null && DmInvocation.Current?.Lease != lease ? lease.BeginInvocation(token) : null;
            try { Validate(); invocation?.ThrowIfTerminated(); return invocation; }
            catch { invocation?.Dispose(); throw; }
        }
        catch { Volatile.Write(ref entered, 0); throw; }
    }

    private void Accept(Data chunk)
    {
        if (chunk?.value == null) throw new InvalidDataException("Missing LOB payload.");
        if (chunk.value.Length == 0 && !locator.readOver)
            throw new InvalidDataException("LOB response made no progress before EOF.");
        if (text)
        {
            if (!locator.readOver && chunk.len <= 0)
                throw new NotSupportedException("LOB profile does not provide positive server advance units.");
            if (chunk.len < -1) throw new InvalidDataException("Invalid LOB advance units.");
            if (chunk.len >= 0) wirePosition = checked(wirePosition + chunk.len);
        }
        else
        {
            if (chunk.len != -1 && chunk.len != chunk.value.Length)
                throw new InvalidDataException("Binary LOB advance differs from its payload bytes.");
            wirePosition = checked(wirePosition + chunk.value.Length);
            if (knownWireLength >= 0 && (wirePosition > knownWireLength || (locator.readOver && wirePosition != knownWireLength)))
                throw new InvalidDataException("Binary LOB response contradicts its declared length.");
        }
        bytes = chunk.value;
        bytePosition = 0;
        wireEnd = locator.readOver;
        DmDiagnosticsCore.LobChunk(false, text, wireEnd);
    }

    private bool NeedChunk => bytePosition == bytes.Length && !wireEnd && charPosition == charLength;
    internal void SetKnownWireLength(long length)
    {
        if (length < 0) throw new InvalidDataException("Negative LOB length.");
        knownWireLength = length;
        // Explicit validated metadata supersedes the deferred length query.
        getBinaryLength = null;
        getBinaryLengthAsync = null;
    }
    private int RequestSize => knownWireLength < 0 ? chunkSize :
        (int)Math.Min(chunkSize, Math.Max(1, knownWireLength - wirePosition));
    private void Fetch()
    {
        if (!NeedChunk) return;
        if (getBinaryLength != null) { SetKnownWireLength(getBinaryLength()); getBinaryLength = null; getBinaryLengthAsync = null; }
        Accept(fetch(wirePosition, RequestSize));
    }
    private async Task FetchAsync(CancellationToken token)
    {
        if (!NeedChunk) return;
        if (getBinaryLengthAsync != null)
        { SetKnownWireLength(await getBinaryLengthAsync(token).ConfigureAwait(false)); getBinaryLength = null; getBinaryLengthAsync = null; }
        Accept(await fetchAsync(wirePosition, RequestSize, token).ConfigureAwait(false));
    }

    private int CopyBytes(Span<byte> destination)
    {
        int count = Math.Min(destination.Length, bytes.Length - bytePosition);
        bytes.Span.Slice(bytePosition, count).CopyTo(destination);
        bytePosition += count;
        Position = checked(Position + count);
        return count;
    }

    private int Decode(Span<char> destination)
    {
        if (charPosition == charLength && !decoderEnd)
        {
            decoder.Convert(bytes.Span.Slice(bytePosition), chars.AsSpan(), wireEnd,
                out int consumed, out int produced, out bool complete);
            bytePosition += consumed;
            charPosition = 0;
            charLength = produced;
            decoderEnd = wireEnd && complete;
            if (consumed == 0 && produced == 0 && !complete)
                throw new InvalidDataException("LOB decoder made no progress.");
        }
        int count = Math.Min(destination.Length, charLength - charPosition);
        chars.AsSpan(charPosition, count).CopyTo(destination);
        charPosition += count;
        Position = checked(Position + count);
        return count;
    }

    private Exception Fail(DmInvocation invocation, Exception error)
    {
        DmInvocation owner = invocation ?? DmInvocation.Current;
        Dispose();
        if (owner != null && owner.ShouldAbortAfterFailure(error))
            owner.Lease.Session.Detach(owner.Identity)?.AbortTransport();
        return owner == null ? error : owner.TranslateFailure(error);
    }

    private void ValidateZero(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Validate();
        if (Volatile.Read(ref entered) != 0) throw new InvalidOperationException("Another read owns this LOB flow.");
    }

    internal int ReadBytes(Span<byte> destination) => Read(destination, default);
    private int Read(Span<byte> destination, CancellationToken token)
    {
        if (destination.IsEmpty) { ValidateZero(token); return 0; }
        using var invocation = Enter(token);
        try
        {
            if (destination.IsEmpty) { invocation?.Complete(); return 0; }
            Fetch();
            int count = CopyBytes(destination);
            Validate(); invocation?.ThrowIfTerminated(); invocation?.Complete(); return count;
        }
        catch (Exception error) { throw Fail(invocation, error); }
        finally { Volatile.Write(ref entered, 0); if (disposed) bytes = default; }
    }

    internal async ValueTask<int> ReadBytesAsync(Memory<byte> destination, CancellationToken token)
    {
        if (destination.IsEmpty) { ValidateZero(token); return 0; }
        using var invocation = Enter(token);
        try
        {
            if (destination.IsEmpty) { invocation?.Complete(); return 0; }
            await FetchAsync(token).ConfigureAwait(false);
            int count = CopyBytes(destination.Span);
            Validate(); invocation?.ThrowIfTerminated(); invocation?.Complete(); return count;
        }
        catch (Exception error) { throw Fail(invocation, error); }
        finally { Volatile.Write(ref entered, 0); if (disposed) bytes = default; }
    }

    internal int ReadChars(Span<char> destination)
    {
        if (destination.IsEmpty) { ValidateZero(default); return 0; }
        using var invocation = Enter(default);
        try
        {
            if (destination.IsEmpty) { invocation?.Complete(); return 0; }
            int count;
            do { Fetch(); count = Decode(destination); } while (count == 0 && !decoderEnd);
            Validate(); invocation?.ThrowIfTerminated(); invocation?.Complete(); return count;
        }
        catch (Exception error) { throw Fail(invocation, error); }
        finally { Volatile.Write(ref entered, 0); if (disposed) bytes = default; }
    }

    internal async ValueTask<int> ReadCharsAsync(Memory<char> destination, CancellationToken token)
    {
        if (destination.IsEmpty) { ValidateZero(token); return 0; }
        using var invocation = Enter(token);
        try
        {
            if (destination.IsEmpty) { invocation?.Complete(); return 0; }
            int count;
            do { await FetchAsync(token).ConfigureAwait(false); count = Decode(destination.Span); }
            while (count == 0 && !decoderEnd);
            Validate(); invocation?.ThrowIfTerminated(); invocation?.Complete(); return count;
        }
        catch (Exception error) { throw Fail(invocation, error); }
        finally { Volatile.Write(ref entered, 0); if (disposed) bytes = default; }
    }

    public void Dispose() { disposed = true; if (Volatile.Read(ref entered) == 0) bytes = default; }
}
