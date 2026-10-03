using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Lobs;

internal sealed class DmLobReadStream(DmLobReadCursor cursor) : Stream
{
    public override bool CanRead => !cursor.IsDisposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => cursor.Position; set => throw new NotSupportedException(); }
    internal static void CheckBuffer<T>(T[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || count > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));
    }
    public override int Read(byte[] buffer, int offset, int count)
    { CheckBuffer(buffer, offset, count); return cursor.ReadBytes(buffer.AsSpan(offset, count)); }
    public override int Read(Span<byte> buffer) => cursor.ReadBytes(buffer);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { CheckBuffer(buffer, offset, count); return cursor.ReadBytesAsync(buffer.AsMemory(offset, count), token).AsTask(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => cursor.ReadBytesAsync(buffer, token);
    protected override void Dispose(bool disposing) { if (disposing) cursor.Dispose(); base.Dispose(disposing); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
