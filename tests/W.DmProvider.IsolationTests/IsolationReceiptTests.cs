using System.IO;
using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.IsolationTests;

public sealed class IsolationReceiptTests
{
    private static readonly OperationIdentity Identity = new(1, 1, 1, 1);

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)3)]
    public void VerifiedTransactionReceiptConfirmsOnlyItsRequestedLevel(short level)
    {
        var info = TransactionResponse();
        info.RecordTransactionIsolationReceipt(level, 5, 0, Identity);
        info.RequireTransactionIsolationReceipt(level, Identity);
    }

    [Fact]
    public void MissingReceiptCannotConfirmIsolation()
        => Assert.Throws<InvalidDataException>(() => TransactionResponse().RequireTransactionIsolationReceipt(0, Identity));

    [Fact]
    public void AcknowledgedDifferentLevelCannotConfirmTheRequest()
    {
        var info = TransactionResponse();
        info.RecordTransactionIsolationReceipt(0, 5, 0, Identity);
        Assert.Throws<InvalidDataException>(() => info.RequireTransactionIsolationReceipt(3, Identity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(166)]
    public void OtherStatementTypeIsNotATransactionReceipt(int resultType)
        => Assert.Throws<InvalidDataException>(() =>
        {
            var info = new DmInfo();
            info.SetRetStmtType(resultType);
            info.RecordTransactionIsolationReceipt(0, 5, 0, Identity);
            info.RequireTransactionIsolationReceipt(0, Identity);
        });

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)2)]
    [InlineData((short)4)]
    public void UnknownReturnedLevelCannotConfirmIsolation(short level)
        => Assert.Throws<InvalidDataException>(() =>
        {
            var info = TransactionResponse();
            info.RecordTransactionIsolationReceipt(level, 5, 0, Identity);
            info.RequireTransactionIsolationReceipt(0, Identity);
        });

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)2)]
    [InlineData((short)4)]
    public void UnknownRequestedLevelCannotBeConfirmed(short level)
    {
        var info = TransactionResponse();
        info.RecordTransactionIsolationReceipt(0, 5, 0, Identity);
        Assert.Throws<InvalidDataException>(() => info.RequireTransactionIsolationReceipt(level, Identity));
    }

    [Theory]
    [InlineData((short)3, 0)]
    [InlineData((short)5, -2106)]
    [InlineData((short)5, 1)]
    public void WrongControlEnvelopeCannotConfirmIsolation(short request, int code)
        => Assert.Throws<InvalidDataException>(() =>
        {
            var info = TransactionResponse();
            info.RecordTransactionIsolationReceipt(0, request, code, Identity);
            info.RequireTransactionIsolationReceipt(0, Identity);
        });

    [Theory]
    [InlineData(2L, 1L, 1L, 1L)]
    [InlineData(1L, 2L, 1L, 1L)]
    [InlineData(1L, 1L, 2L, 1L)]
    [InlineData(1L, 1L, 1L, 2L)]
    public void ReceiptFromAnotherExecutionCannotConfirmThisInvocation(long session, long generation, long execution, long invocation)
        => Assert.Throws<InvalidDataException>(() =>
        {
            var info = TransactionResponse();
            info.RecordTransactionIsolationReceipt(0, 5, 0, new OperationIdentity(session, generation, execution, invocation));
            info.RequireTransactionIsolationReceipt(0, Identity);
        });

    [Theory]
    [InlineData(0L, 1L, 1L, 1L)]
    [InlineData(1L, 0L, 1L, 1L)]
    [InlineData(1L, 1L, 0L, 1L)]
    [InlineData(1L, 1L, 1L, 0L)]
    public void NonpositiveIdentityCannotConfirmIsolation(long session, long generation, long execution, long invocation)
        => Assert.Throws<InvalidDataException>(() =>
        {
            var identity = new OperationIdentity(session, generation, execution, invocation);
            var info = TransactionResponse();
            info.RecordTransactionIsolationReceipt(0, 5, 0, identity);
            info.RequireTransactionIsolationReceipt(0, identity);
        });

    private static DmInfo TransactionResponse()
    {
        var info = new DmInfo();
        info.SetRetStmtType(150);
        return info;
    }
}
