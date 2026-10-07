using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;

namespace W.Dm.Internal.Lobs;

/// <summary>Frozen caller identity and kind; each upload creates its own bounded encoder/cursor.</summary>
internal sealed class DmLobInput
{
    internal object SourceIdentity { get; }
    internal bool IsText { get; }
    internal int WireType => IsText ? 19 : 12;
    internal DmLobInput(object value, DmDbType type)
    {
        if (value is Stream && type == DmDbType.Blob) IsText = false;
        else if (value is TextReader && type == DmDbType.Clob) IsText = true;
        else throw new NotSupportedException("Streaming input requires Stream with Blob or TextReader with Clob.");
        SourceIdentity = value;
    }
    internal Cursor OpenCursor(int chunkSize, string serverEncoding) => new(this, chunkSize, serverEncoding);

    internal sealed class Cursor : IDisposable
    {
        private readonly DmLobInput input;
        private readonly char[] characters;
        private readonly byte[] encoded;
        private readonly Encoder encoder;
        private int characterOffset, characterCount, encodedOffset, encodedCount;
        private bool sourceEof, encoderEof, disposed;
        internal byte[] Buffer { get; }
        internal long ConsumedBytes { get; private set; }
        internal long ConsumedCharacters { get; private set; }
        internal int PeakOwnedBufferBytes { get; }
        internal bool EndOfInput => sourceEof && (!input.IsText || encoderEof && encodedOffset == encodedCount);

        internal Cursor(DmLobInput input, int chunkSize, string serverEncoding)
        {
            if (chunkSize <= 0 || chunkSize > DmConnectionSettings.DefaultLobChunkSize)
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            this.input = input;
            Buffer = new byte[chunkSize];
            if (input.IsText)
            {
                Encoding encoding = DmTextCodec.CreateStrictEncoding(serverEncoding);
                encoder = encoding.GetEncoder();
                characters = new char[Math.Max(2, Math.Min(4096, chunkSize / 2))];
                encoded = new byte[encoding.GetMaxByteCount(checked(characters.Length + 1))];
            }
            PeakOwnedBufferBytes = checked(Buffer.Length + (characters?.Length ?? 0) * sizeof(char) + (encoded?.Length ?? 0));
        }

        internal int ReadChunk(CancellationToken token = default) => ReadChunkCoreAsync(false, token).GetAwaiter().GetResult();
        internal ValueTask<int> ReadChunkAsync(CancellationToken token = default) => ReadChunkCoreAsync(true, token);
        private static void Poll(CancellationToken token)
        {
            DmInvocation.Current?.ThrowIfTerminated();
            token.ThrowIfCancellationRequested();
        }
        private async ValueTask<int> ReadChunkCoreAsync(bool asynchronous, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Cursor));
            Poll(token);
            int count = 0;
            if (!input.IsText)
            {
                var stream = (Stream)input.SourceIdentity;
                while (count < Buffer.Length && !sourceEof)
                {
                    Poll(token);
                    DmInvocation invocation = DmInvocation.Current;
                    DmWireExchange wire = DmWireExchange.Current;
                    int read;
                    try
                    {
                        read = asynchronous
                            ? await stream.ReadAsync(Buffer.AsMemory(count), token).ConfigureAwait(false)
                            : stream.Read(Buffer, count, Buffer.Length - count);
                    }
                    catch (Exception error)
                    {
                        invocation?.Lease.Session.TryAcceptLocalInputFailure(invocation, wire, error);
                        throw;
                    }
                    Poll(token);
                    if (read < 0 || read > Buffer.Length - count) throw new IOException("Input returned an invalid byte count.");
                    if (read == 0) { sourceEof = true; break; }
                    count += read;
                    ConsumedBytes = checked(ConsumedBytes + read);
                }
            }
            else
            {
                while (count < Buffer.Length)
                {
                    Poll(token);
                    if (encodedOffset < encodedCount)
                    {
                        int copy = Math.Min(Buffer.Length - count, encodedCount - encodedOffset);
                        encoded.AsSpan(encodedOffset, copy).CopyTo(Buffer.AsSpan(count));
                        encodedOffset += copy; count += copy;
                        continue;
                    }
                    if (encoderEof) break;
                    if (characterOffset == characterCount && !sourceEof)
                    {
                        var reader = (TextReader)input.SourceIdentity;
                        DmInvocation invocation = DmInvocation.Current;
                        DmWireExchange wire = DmWireExchange.Current;
                        int read;
                        try
                        {
                            read = asynchronous
                                ? await reader.ReadAsync(characters.AsMemory(), token).ConfigureAwait(false)
                                : reader.Read(characters, 0, characters.Length);
                        }
                        catch (Exception error)
                        {
                            invocation?.Lease.Session.TryAcceptLocalInputFailure(invocation, wire, error);
                            throw;
                        }
                        Poll(token);
                        if (read < 0 || read > characters.Length) throw new IOException("Input returned an invalid character count.");
                        characterOffset = 0; characterCount = read;
                        ConsumedCharacters = checked(ConsumedCharacters + read);
                        if (read == 0) sourceEof = true;
                    }
                    DmInvocation encoderInvocation = DmInvocation.Current;
                    DmWireExchange encoderWire = DmWireExchange.Current;
                    int usedCharacters, usedBytes;
                    bool completed;
                    try
                    {
                        encoder.Convert(characters.AsSpan(characterOffset, characterCount - characterOffset), encoded.AsSpan(), sourceEof,
                            out usedCharacters, out usedBytes, out completed);
                    }
                    catch (EncoderFallbackException error)
                    {
                        encoderInvocation?.Lease.Session.TryAcceptLocalInputFailure(encoderInvocation, encoderWire, error);
                        throw;
                    }
                    characterOffset += usedCharacters;
                    encodedOffset = 0; encodedCount = usedBytes;
                    ConsumedBytes = checked(ConsumedBytes + usedBytes);
                    encoderEof = sourceEof && completed && characterOffset == characterCount;
                    if (usedCharacters == 0 && usedBytes == 0 && !encoderEof && characterOffset < characterCount)
                        throw new IOException("Strict input encoder made no progress.");
                }
            }
            Poll(token);
            DmLobInputTestHooks.AfterChunk?.Invoke(ConsumedBytes, ConsumedCharacters, count, PeakOwnedBufferBytes);
            return count;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // The caller owns its stream/reader. This cursor owns only buffers and encoder state.
            Array.Clear(Buffer);
            if (characters != null) Array.Clear(characters);
            if (encoded != null) Array.Clear(encoded);
        }
    }
}

internal static class DmLobInputTestHooks
{
    // Numeric test/probe observation only; never caller objects, data, SQL or tokens.
    internal static Action<long, long, int, int> AfterChunk;
}
