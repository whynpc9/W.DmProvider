using System;
using System.Text;

namespace W.Dm.Internal.Types;

internal static class DmTextCodec
{
    internal static Encoding CreateStrictEncoding(string serverEncoding)
    {
        string name = serverEncoding ?? "default";
        if (!DmConnProperty.encodingMap.TryGetValue(name, out Encoding encoding))
            throw new ArgumentException("Server charset is unsupported.", nameof(serverEncoding));
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return strict;
    }

    internal static byte[] EncodeStrict(string text, string serverEncoding)
    {
        if (text == null) return null;
        try { return CreateStrictEncoding(serverEncoding).GetBytes(text); }
        catch (EncoderFallbackException)
        {
            throw new EncoderFallbackException("Text cannot be represented in the declared server charset.");
        }
    }

    internal static string DecodeStrict(byte[] data, int offset, int length, string serverEncoding)
    {
        if (data == null) return null;
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(length));
        try { return CreateStrictEncoding(serverEncoding).GetString(data, offset, length); }
        catch (DecoderFallbackException)
        {
            throw new DecoderFallbackException("Server text contains invalid bytes for its declared charset.");
        }
    }
}
