using System.Text;
using System.Text.Json;
using W.Dm;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class EncodingBoundaryTests
{
    [Fact]
    public async Task EveryIndependentUtf8AndGb18030CutDecodesWithOneCharOutput()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("small_fixed_text_vectors").EnumerateArray())
        {
            string encoding = vector.GetProperty("reference_encoding_name").GetString()!;
            string expected = vector.GetProperty("text").GetString()!;
            byte[] bytes = Convert.FromHexString(vector.GetProperty("encoded_bytes_hex").GetString()!);
            for (int cut = 1; cut < bytes.Length; cut++)
            {
                var locator = new AbstractLob(1, null);
                int fetchCount = 0;
                Data Fetch(long offset, int count)
                {
                    Assert.Equal(fetchCount == 0 ? 0 : 97, offset);
                    fetchCount++;
                    locator.readOver = fetchCount == 2;
                    return fetchCount == 1 ? new Data(97, bytes[..cut]) : new Data(-1, bytes[cut..]);
                }
                using var cursor = new DmLobReadCursor(locator, default, false, null, null, 4093,
                    Fetch, (p, n, t) => Task.FromResult(Fetch(p, n)), true, encoding);
                using var reader = new DmLobTextReader(cursor);
                var result = new StringBuilder(); char[] one = new char[1];
                while (await reader.ReadAsync(one) != 0) result.Append(one[0]);
                Assert.Equal(expected, result.ToString());
                Assert.Equal(2, fetchCount);
            }
        }
    }

    [Theory]
    [InlineData("UTF-8", "F09F")]
    [InlineData("GB18030", "95308B")]
    public async Task TruncatedLastCharacterIsStrictlyRejected(string encoding, string hex)
    {
        var locator = new AbstractLob(1, null) { readOver = true };
        byte[] bytes = Convert.FromHexString(hex);
        using var cursor = new DmLobReadCursor(locator, bytes, true, null, null, 2,
            (p, n) => throw new Exception("Unexpected network"),
            (p, n, t) => throw new Exception("Unexpected network"), true, encoding);
        await Assert.ThrowsAsync<DecoderFallbackException>(async () => await cursor.ReadCharsAsync(new char[8], default));
    }

    [Fact]
    public void StrictCodecAndLocatorSnapshotsDoNotShareMutableState()
    {
        var first = DmTextCodec.CreateStrictEncoding("UTF-8");
        var second = DmTextCodec.CreateStrictEncoding("UTF-8");
        Assert.NotSame(first, second);
        first.DecoderFallback = DecoderFallback.ReplacementFallback;
        Assert.IsType<DecoderExceptionFallback>(second.DecoderFallback);
        var original = new AbstractLob(1, null) { rowId = [7, 8], curFileId = 12, totalOffset = 21 };
        var copy = original.SnapshotForRead();
        copy.rowId[0] = 99; copy.curFileId = 77; copy.totalOffset = 88;
        Assert.Equal(7, original.rowId[0]); Assert.Equal(12, original.curFileId); Assert.Equal(21, original.totalOffset);
    }
}
