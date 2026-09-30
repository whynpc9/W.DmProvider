using System.IO;
using System.Text.Json;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Transport;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;

namespace W.DmProvider.TransportTests;

public sealed class FixtureVectorTests
{
    [Fact]
    public void SyntheticManifestHasUniqueContractsAndUsesVerifiedHeaderFields()
    {
        using var document = Load();
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(DmFrameReader.MaxFrameSize,
            root.GetProperty("product_policy").GetProperty("default_max_total_frame_bytes").GetInt32());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(15, cases.Length);
        Assert.Equal(cases.Length, cases.Select(c => c.GetProperty("case_id").GetString()).Distinct().Count());
        Assert.All(cases, c =>
        {
            Assert.Equal("not_run", c.GetProperty("execution_status").GetString());
            Assert.Contains(c.GetProperty("contract").GetString(), new[] { "NET-01", "NET-02", "NET-03", "NET-04" });
        });
        byte[] header = Hex(Case(root, "net01_body_fragmented_reads").GetProperty("input").GetProperty("header_hex"));
        Assert.Equal(64, header.Length);
        Assert.Equal(6, DmFrameReader.Command(header));
        Assert.Equal(4, DmFrameReader.BodyLength(header));
    }

    [Fact]
    public void ManifestFragmentAndCoalescedVectorsExecuteAgainstFrameReader()
    {
        using var document = Load();
        JsonElement root = document.RootElement;
        JsonElement fragment = Case(root, "net01_body_fragmented_reads").GetProperty("input");
        byte[] fragmentFrame = [.. Hex(fragment.GetProperty("header_hex")), .. Hex(fragment.GetProperty("body_hex"))];
        using (var stream = new OneByteStream(fragmentFrame))
        {
            var destination = new LegacyBuffer();
            Assert.Equal(fragmentFrame.Length, DmFrameReader.Read(stream, destination, false, DmDeadline.Infinite));
            Assert.Equal(fragmentFrame, destination.A()[..fragmentFrame.Length]);
        }
        JsonElement coalesced = Case(root, "net01_coalesced_two_frames_are_separated").GetProperty("input");
        byte[] first = [.. Hex(coalesced.GetProperty("frame_1_header_hex")), .. Hex(coalesced.GetProperty("frame_1_body_hex"))];
        byte[] second = [.. Hex(coalesced.GetProperty("frame_2_header_hex")), .. Hex(coalesced.GetProperty("frame_2_body_hex"))];
        using var combined = new MemoryStream([.. first, .. second]);
        var target = new LegacyBuffer();
        Assert.Equal(first.Length, DmFrameReader.Read(combined, target, false, DmDeadline.Infinite));
        Assert.Equal(first.Length, combined.Position);
        Assert.Equal(second.Length, DmFrameReader.Read(combined, target, false, DmDeadline.Infinite));
        Assert.Equal(first.Length + second.Length, combined.Position);
    }

    [Fact]
    public void ManifestCrcAndLimitVectorsRejectWithoutUnboundedRead()
    {
        using var document = Load();
        JsonElement root = document.RootElement;
        JsonElement crc = Case(root, "net02_body_crc32_mismatch_rejected").GetProperty("input");
        byte[] corrupt = [.. Hex(crc.GetProperty("header_hex")), .. Hex(crc.GetProperty("body_hex")),
            .. Hex(crc.GetProperty("received_crc_trailer_hex"))];
        using (var stream = new MemoryStream(corrupt))
            Assert.Throws<InvalidDataException>(() => DmFrameReader.Read(stream, new LegacyBuffer(), true, DmDeadline.Infinite));
        JsonElement limit = Case(root, "net02_total_frame_product_limit_rejected_without_large_allocation").GetProperty("input");
        byte[] header = Hex(limit.GetProperty("header_hex"));
        using var headerOnly = new MemoryStream(header);
        var destination = new LegacyBuffer();
        int before = destination.A().Length;
        Assert.Throws<InvalidDataException>(() => DmFrameReader.Read(headerOnly, destination, false, DmDeadline.Infinite));
        Assert.Equal(64, headerOnly.Position);
        Assert.Equal(before, destination.A().Length);
    }

    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "scenarios.json")));

    private static JsonElement Case(JsonElement root, string id) => root.GetProperty("cases").EnumerateArray()
        .Single(c => c.GetProperty("case_id").GetString() == id);

    private static byte[] Hex(JsonElement value) => Convert.FromHexString(value.GetString()!);

    private sealed class OneByteStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override int Read(byte[] destination, int offset, int count) =>
            base.Read(destination, offset, Math.Min(count, 1));
    }
}
