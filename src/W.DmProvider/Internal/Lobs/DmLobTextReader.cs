using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Lobs;

internal sealed class DmLobTextReader(DmLobReadCursor cursor) : TextReader
{
    public override int Read(char[] buffer, int index, int count)
    { DmLobReadStream.CheckBuffer(buffer, index, count); return cursor.ReadChars(buffer.AsSpan(index, count)); }
    public override int Read(Span<char> buffer) => cursor.ReadChars(buffer);
    public override int Peek() => throw new NotSupportedException("LOB text readers are forward-only and do not support Peek.");
    public override int Read() { Span<char> one = stackalloc char[1]; return Read(one) == 0 ? -1 : one[0]; }
    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    { DmLobReadStream.CheckBuffer(buffer, index, count); return cursor.ReadCharsAsync(buffer.AsMemory(index, count), default).AsTask(); }
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default) => cursor.ReadCharsAsync(buffer, token);
    protected override void Dispose(bool disposing) { if (disposing) cursor.Dispose(); base.Dispose(disposing); }
}
