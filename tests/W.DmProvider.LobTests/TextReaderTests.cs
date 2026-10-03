using System.Text;
using W.Dm;
using W.Dm.Internal.Lobs;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class TextReaderTests
{
    [Fact]
    public async Task OneCharReadsPreserveSupplementaryCharacterAndOpaqueServerUnits()
    {
        await using var fixture = new OutputLobFixture();
        using var text = fixture.Reader(19).GetTextReader(0);
        Assert.Empty(fixture.Channel.Commands);
        fixture.Channel.AddData([0x41, 0xF0, 0x9F], 37);
        fixture.Channel.AddData([0x99, 0x82, 0, 0xE4], 11);
        fixture.Channel.AddData([0xB8, 0xAD], 1, true);
        fixture.ReleaseAll();
        var result = new StringBuilder();
        char[] one = new char[1];
        while (await text.ReadAsync(one) != 0) result.Append(one[0]);
        Assert.Equal("A🙂\0中", result.ToString());
        Assert.Equal(new long[] { 0, 37, 48 }, fixture.Channel.ReadPositions);
        Assert.Equal(0, fixture.Channel.SynchronousCalls);
    }

    [Fact]
    public async Task MissingTextAdvanceBeforeEofIsNotGuessed()
    {
        await using var fixture = new OutputLobFixture();
        using var text = fixture.Reader(19).GetTextReader(0);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("中"), -1); fixture.ReleaseAll();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await text.ReadAsync(new char[5]));
    }

    [Fact]
    public async Task EofWithoutAdvanceFlushesAndRejectsInvalidTail()
    {
        await using var fixture = new OutputLobFixture();
        using var text = fixture.Reader(19).GetTextReader(0);
        fixture.Channel.AddData([0xF0, 0x9F], -1, true); fixture.ReleaseAll();
        await Assert.ThrowsAsync<DecoderFallbackException>(async () => await text.ReadAsync(new char[5]));
    }

    [Fact]
    public async Task EmptyNonEofDoesNotBecomeEof()
    {
        await using var fixture = new OutputLobFixture();
        using var text = fixture.Reader(19).GetTextReader(0);
        fixture.Channel.AddData([], 0); fixture.ReleaseAll();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await text.ReadAsync(new char[5]));
    }
}
