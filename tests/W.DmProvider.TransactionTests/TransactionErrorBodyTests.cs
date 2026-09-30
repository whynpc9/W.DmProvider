using System.IO;
using System.Text;
using W.Dm.Internal.Legacy.A;
using Xunit;

namespace W.DmProvider.TransactionTests;

public sealed class TransactionErrorBodyTests
{
    [Fact]
    public void CompleteVerifiedErrorBodyPreservesAllFieldsAndConsumesTheFrame()
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            foreach (string text in new[] { "SYNTHETIC", "TABLE", "COL", "test_error" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        var response = Response(body.ToArray());
        var error = c.ReadCompleteErrorBody(response, "UTF-8");
        Assert.Equal(-2106, error.State);
        Assert.Equal("SYNTHETIC", error.Schema);
        Assert.Equal("TABLE", error.Table);
        Assert.Equal("COL", error.Col);
        Assert.Equal("test_error", error.Message);
        Assert.Equal(0, response.a(false));
    }

    [Fact]
    public void NegativeDiagnosticLengthCannotClaimVerifiedServerError()
        => Assert.Throws<InvalidDataException>(() =>
            c.ReadCompleteErrorBody(Response(BitConverter.GetBytes(-1)), "UTF-8"));

    [Fact]
    public void TruncatedDiagnosticCannotClaimVerifiedServerError()
        => Assert.Throws<InvalidDataException>(() =>
            c.ReadCompleteErrorBody(Response([6, 0, 0, 0, 65]), "UTF-8"));

    [Fact]
    public void ExtraByteAfterFourStringsCannotClaimVerifiedServerError()
        => Assert.Throws<InvalidDataException>(() =>
            c.ReadCompleteErrorBody(Response(new byte[17]), "UTF-8"));

    private static b Response(byte[] body)
    {
        byte[] frame = new byte[64 + body.Length];
        BitConverter.GetBytes(body.Length).CopyTo(frame, 6);
        BitConverter.GetBytes(-2106).CopyTo(frame, 10);
        body.CopyTo(frame, 64);
        var response = new b(frame);
        response.G(64);
        return response;
    }
}
