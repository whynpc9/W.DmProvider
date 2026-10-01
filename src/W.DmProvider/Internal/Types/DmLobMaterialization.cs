using System;

namespace W.Dm.Internal.Types;

/// <summary>Bounds returned payload bytes, rather than CLR object overhead or wire size.</summary>
internal static class DmLobMaterialization
{
    internal static int Bytes(long length) => CheckedLength(length, 1);
    internal static int Characters(long length) => CheckedLength(length, sizeof(char));
    internal static int HexInput(long length) => CheckedLength(length, 2 * sizeof(char));

    private static int CheckedLength(long length, int bytesPerUnit)
    {
        long payload;
        try { payload = checked(length * bytesPerUnit); }
        catch (OverflowException) { throw TooLarge(); }
        if (length < 0 || payload > DmConnectionSettings.DefaultMaxMaterializedLobSize)
            throw TooLarge();
        return checked((int)length);
    }

    private static NotSupportedException TooLarge() => new(
        "LOB materialization exceeds the fixed 64 MiB returned-payload limit.");
}
