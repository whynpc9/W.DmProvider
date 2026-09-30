using System;
using System.IO;
using W.Dm.Internal.Legacy.A;

namespace W.Dm.Internal.Protocol;

internal static class DmFrameWriter
{
    internal static int Validate(b buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.B() < DmFrameReader.HeaderSize)
            throw new InvalidDataException("Incomplete frame header.");
        int total = DmFrameReader.ValidateLength(DmFrameReader.BodyLength(buffer.A()));
        if (total != buffer.B()) throw new InvalidDataException("Encoded frame length does not match header.");
        return total;
    }
}
