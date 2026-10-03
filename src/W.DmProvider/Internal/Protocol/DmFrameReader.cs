using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Protocol;

internal static class DmFrameReader
{
    internal const int HeaderSize = 64;
    internal const int MaxFrameSize = 64 * 1024 * 1024;
    internal const short HeartbeatCommand = 269;

    // The two legacy codecs both use little-endian command at 4 and body size at 6.
    internal static short Command(byte[] header) => (short)(header[4] | (header[5] << 8));
    internal static int BodyLength(byte[] header) => header[6] | (header[7] << 8) | (header[8] << 16) | (header[9] << 24);

    internal static bool ValidateHeaderChecksum(byte[] header)
    {
        byte value = header[0];
        for (int index = 1; index < 19; index++) value ^= header[index];
        return value == header[19];
    }

    internal static int ValidateLength(int bodyLength, int maxResponseBodyLength = MaxFrameSize - HeaderSize)
    {
        if (maxResponseBodyLength < 0 || maxResponseBodyLength > MaxFrameSize - HeaderSize)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBodyLength));
        if (bodyLength < 0) throw new InvalidDataException("Negative frame body length.");
        int total;
        try { total = checked(HeaderSize + bodyLength); }
        catch (OverflowException ex) { throw new InvalidDataException("Frame length overflow.", ex); }
        if (total > MaxFrameSize) throw new InvalidDataException("Frame exceeds the configured limit.");
        if (bodyLength > maxResponseBodyLength) throw new InvalidDataException("Frame exceeds the current message response budget.");
        return total;
    }

    internal static void ValidateCount(int count, int minimumWidth, int remaining)
    {
        if (count < 0 || minimumWidth < 1 || remaining < 0)
            throw new InvalidDataException("Invalid protocol element count.");
        int bytes;
        try { bytes = checked(count * minimumWidth); }
        catch (OverflowException ex) { throw new InvalidDataException("Protocol element count overflow.", ex); }
        if (bytes > remaining) throw new InvalidDataException("Protocol element count exceeds frame data.");
    }

    internal static void ValidateRows(int rows, int columns, int remaining)
    {
        ValidateCount(rows, 1, remaining);
        if (columns < 0) throw new InvalidDataException("Negative column count.");
        int cells;
        try { cells = checked(rows * checked(columns + 1)); }
        catch (OverflowException ex) { throw new InvalidDataException("Result size overflow.", ex); }
        // Outer array and one row reference array per row. Cell byte[] copies
        // are separately bounded by the received frame and are not counted here.
        long allocation = checked(24L + (24L + IntPtr.Size) * rows + (long)IntPtr.Size * cells);
        if (allocation > MaxFrameSize)
            throw new InvalidDataException("Result frame exceeds row allocation budget.");
    }

    // Shared by all BDTA packages decoded from one FillRows frame. This limits
    // server-requested padding, which need not be present on the wire.
    internal static void ReserveDecodedValueBytes(ref long used, int next)
    {
        if (used < 0 || next < 0) throw new InvalidDataException("Invalid decoded value length.");
        long total;
        try { total = checked(used + next); }
        catch (OverflowException ex) { throw new InvalidDataException("Decoded value length overflow.", ex); }
        if (total > MaxFrameSize) throw new InvalidDataException("Decoded value bytes exceed the per-frame budget.");
        used = total;
    }

    // The supplied checksum callback is the negotiated legacy algorithm. It is also
    // called for heartbeat frames, before they can be skipped.
    internal static int Read(Action<byte[], int, int> readExactly, b destination,
        Func<b, int, bool> validateFrame, DmDeadline deadline, Func<byte[], bool> validateHeader = null,
        int maxResponseBodyLength = MaxFrameSize - HeaderSize)
    {
        if (readExactly == null) throw new ArgumentNullException(nameof(readExactly));
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (validateFrame == null) throw new ArgumentNullException(nameof(validateFrame));
        while (true)
        {
            deadline.ThrowIfExpired();
            destination.a(0);
            destination.B(HeaderSize);
            readExactly(destination.A(), 0, HeaderSize);
            destination.A(HeaderSize);
            int total = ValidateLength(BodyLength(destination.A()), maxResponseBodyLength);
            if (destination.A()[18] != 0) throw new NotSupportedException("Compressed protocol frames are not supported.");
            if (validateHeader != null && !validateHeader(destination.A()))
                throw new InvalidDataException("Frame header checksum failed.");
            int bodyLength = total - HeaderSize;
            destination.B(bodyLength);
            if (bodyLength != 0) readExactly(destination.A(), HeaderSize, bodyLength);
            destination.A(total);
            if (!validateFrame(destination, total)) throw new InvalidDataException("Frame checksum failed.");
            if (Command(destination.A()) != HeartbeatCommand)
            {
                destination.G(HeaderSize);
                return total;
            }
        }
    }

    internal static async ValueTask<int> ReadAsync(Func<byte[], int, int, CancellationToken, ValueTask> readExactly,
        b destination, Func<b, int, bool> validateFrame, DmDeadline deadline,
        CancellationToken cancellationToken = default, Func<byte[], bool> validateHeader = null,
        int maxResponseBodyLength = MaxFrameSize - HeaderSize)
    {
        ArgumentNullException.ThrowIfNull(readExactly);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(validateFrame);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            deadline.ThrowIfExpired();
            destination.a(0);
            destination.B(HeaderSize);
            await readExactly(destination.A(), 0, HeaderSize, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            deadline.ThrowIfExpired();
            destination.A(HeaderSize);
            int total = ValidateLength(BodyLength(destination.A()), maxResponseBodyLength);
            if (destination.A()[18] != 0) throw new NotSupportedException("Compressed protocol frames are not supported.");
            if (validateHeader != null && !validateHeader(destination.A()))
                throw new InvalidDataException("Frame header checksum failed.");
            int bodyLength = total - HeaderSize;
            destination.B(bodyLength);
            if (bodyLength != 0)
                await readExactly(destination.A(), HeaderSize, bodyLength, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            deadline.ThrowIfExpired();
            destination.A(total);
            if (!validateFrame(destination, total)) throw new InvalidDataException("Frame checksum failed.");
            if (Command(destination.A()) != HeartbeatCommand)
            {
                destination.G(HeaderSize);
                return total;
            }
        }
    }

    internal static ValueTask<int> ReadAsync(Stream stream, b destination, bool crcBody, DmDeadline deadline,
        CancellationToken cancellationToken = default, int maxResponseBodyLength = MaxFrameSize - HeaderSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadAsync(async (buffer, offset, count, token) =>
        {
            int read = 0;
            using var budget = new DmIoCancellation(deadline, token, CancellationToken.None);
            while (read < count)
            {
                int progress;
                try { progress = await stream.ReadAsync(buffer.AsMemory(offset + read, count - read), budget.Token).ConfigureAwait(false); }
                catch (OperationCanceledException ex) { budget.RethrowCancellation(ex); throw; }
                deadline.ThrowIfExpired();
                if (progress <= 0) throw new EndOfStreamException("Truncated protocol frame.");
                read += progress;
            }
        }, destination, (buffer, total) => ValidateChecksum(buffer, total, crcBody), deadline, cancellationToken,
           header => (crcBody && Command(header) != 200) || ValidateHeaderChecksum(header), maxResponseBodyLength);
    }

    internal static int Read(Stream stream, b destination, bool crcBody, DmDeadline deadline,
        int maxResponseBodyLength = MaxFrameSize - HeaderSize)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        return Read((buffer, offset, count) =>
        {
            int read = 0;
            while (read < count)
            {
                deadline.ThrowIfExpired();
                int current = stream.Read(buffer, offset + read, count - read);
                if (current <= 0) throw new EndOfStreamException("Truncated protocol frame.");
                read += current;
            }
        }, destination, (buffer, total) => ValidateChecksum(buffer, total, crcBody), deadline,
           header => (crcBody && Command(header) != 200) || ValidateHeaderChecksum(header), maxResponseBodyLength);
    }

    internal static bool ValidateChecksum(b buffer, int total, bool crcBody)
    {
        if (!crcBody || Command(buffer.A()) == 200)
            return ValidateHeaderChecksum(buffer.A());
        if (total < HeaderSize + 4) return false;
        uint crc = 0xffffffff;
        for (int index = 0; index < total - 4; index++)
        {
            crc ^= buffer.__t02_method_06000AB4(index);
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return unchecked((int)~crc) == buffer.d(total - 4);
    }
}
