using System.Buffers.Binary;
using System.Text;
using System.Data;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class LobLifecycleTests
{
    [Fact]
    public async Task DisposedPartialBlobCannotRestartFromZeroUnderSequentialAccess()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(17, [1, 2, 3], CommandBehavior.SequentialAccess);
        var stream = reader.GetStream(0); Assert.Equal(1, stream.ReadByte()); stream.Dispose();
        Assert.Throws<InvalidOperationException>(() => reader.GetStream(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Equal(6097, Assert.Throws<DmException>(() => reader.GetBytes(0, 0, new byte[1], 0, 1)).Number);
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData(DmTransactionOutcome.Committed)]
    [InlineData(DmTransactionOutcome.RolledBack)]
    [InlineData(DmTransactionOutcome.OutcomeUnknown)]
    [InlineData(DmTransactionOutcome.CompletedExternally)]
    public async Task EndedTransactionInvalidatesZeroAndCachedInlineReadsWithoutIo(DmTransactionOutcome outcome)
    {
        await using var fixture = new OutputLobFixture();
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        transaction.SetOutcomeFromSession(DmTransactionOutcome.Active);
        OutputLobFixture.SetField(fixture.Session, "activeTransaction", transaction);
        using var stream = fixture.Reader(17, [1, 2, 3]).GetStream(0);
        byte[] one = new byte[1];
        Assert.Equal(1, stream.Read(one)); Assert.Equal(1, one[0]);
        transaction.SetOutcomeFromSession(outcome);
        Assert.Throws<InvalidOperationException>(() => stream.Read(one, one.Length, 0));
        Assert.Throws<InvalidOperationException>(() => stream.Read(one));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task EndedTransactionRejectsAlreadyDecodedClobCacheAndAttachedLob()
    {
        await using var fixture = new OutputLobFixture();
        var transaction = (DmTransaction)RuntimeHelpers.GetUninitializedObject(typeof(DmTransaction));
        transaction.SetOutcomeFromSession(DmTransactionOutcome.Active);
        OutputLobFixture.SetField(fixture.Session, "activeTransaction", transaction);
        var reader = fixture.Reader(19);
        var attached = reader.GetClob(0);
        using var text = reader.GetTextReader(0);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂Z"), 5, true); fixture.ReleaseAll();
        char[] one = new char[1];
        Assert.Equal(1, await text.ReadAsync(one)); Assert.Equal('A', one[0]);
        Assert.Single(fixture.Channel.Commands);
        transaction.SetOutcomeFromSession(DmTransactionOutcome.Committed);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await text.ReadAsync(one));
        Assert.Throws<InvalidOperationException>(() => text.Read(one, one.Length, 0));
        Assert.Throws<InvalidOperationException>(() => attached.length());
        Assert.Single(fixture.Channel.Commands);
    }

    [Fact]
    public async Task ConfirmedBlobWriteRebasesThePrivateReadLocatorAndQueriesFreshLength()
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        var blob = fixture.Blob(5);
        Assert.Equal(12, blob.rowId.Length);
        Assert.True(fixture.Instance.ConnProperty.NewLobFlag);
        byte[] ack = new byte[30];
        BinaryPrimitives.WriteInt32LittleEndian(ack, 1);
        BinaryPrimitives.WriteInt64LittleEndian(ack.AsSpan(4), 42);
        BinaryPrimitives.WriteInt16LittleEndian(ack.AsSpan(14), 8);
        BinaryPrimitives.WriteInt32LittleEndian(ack.AsSpan(16), 13);
        BinaryPrimitives.WriteInt16LittleEndian(ack.AsSpan(20), 19);
        BinaryPrimitives.WriteUInt32LittleEndian(ack.AsSpan(26), 99);
        fixture.Channel.AddRawReply(30, ack);
        fixture.Channel.AddLength(5);
        fixture.Channel.AddData([9, 2], -1); fixture.ReleaseAll();
        Assert.Equal(1, blob.SetBytes(0, [9]));
        Assert.Equal(new byte[] { 9 }, blob.GetBytes(0, 1));
        Assert.Equal(new short[] { 30, 29, 32 }, fixture.Channel.Commands);
        Assert.Equal(new long[] { 42 }, fixture.Channel.LocatorIds);
        Assert.Equal(new int[] { 8 }, fixture.Channel.CursorFiles);
        Assert.Equal(new long[] { 0 }, fixture.Channel.CursorTotals);
        Assert.Equal(new long[] { 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task ConfirmedClobTruncateReadsTheUpdatedLocatorAtZero()
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        var clob = fixture.Clob(5);
        Assert.Equal(12, clob.rowId.Length);
        Assert.True(fixture.Instance.ConnProperty.NewLobFlag);
        byte[] ack = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(ack, 3);
        BinaryPrimitives.WriteInt64LittleEndian(ack.AsSpan(8), 77);
        fixture.Channel.AddRawReply(31, ack);
        fixture.Channel.AddData(Encoding.UTF8.GetBytes("A🙂"), 3, true); fixture.ReleaseAll();
        clob.Truncate(3);
        Assert.Equal("A🙂", clob.MaterializeStringUnderOwner());
        Assert.Equal(new long[] { 77 }, fixture.Channel.LocatorIds);
        Assert.Equal(new long[] { 0 }, fixture.Channel.ReadPositions);
    }

    [Fact]
    public async Task NextRowInvalidatesStreamsAndExplicitAttachedLobsBeforeIo()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12, rows: 2);
        var oldLob = reader.GetBlob(0);
        using var stream = reader.GetStream(0);
        Assert.True(reader.Read());
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Throws<InvalidOperationException>(() => oldLob.Length());
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task FailedConcurrentParentAdvanceDoesNotInvalidateAnOwnedRead()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12, rows: 2);
        using var stream = reader.GetStream(0);
        fixture.Channel.AddData([1, 2], -1);
        Task<int> pending = stream.ReadAsync(new byte[2], 0, 2);
        await fixture.Channel.Replies[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => reader.Read());
        await Assert.ThrowsAsync<InvalidOperationException>(() => stream.ReadAsync(new byte[1], 0, 1));
        fixture.ReleaseAll();
        Assert.Equal(2, await pending);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public async Task OnlyOneActiveFlowAndDisposedZeroLengthStillFails()
    {
        await using var fixture = new OutputLobFixture();
        var reader = fixture.Reader(12);
        var stream = reader.GetStream(0);
        Assert.Throws<InvalidOperationException>(() => reader.GetStream(0));
        Assert.Equal(0, stream.Read(new byte[2], 2, 0));
        Assert.Empty(fixture.Channel.Commands);
        stream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[2], 2, 0));
        using var second = reader.GetStream(0);
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task OldGenerationAndPreCancellationCannotReadOrBreakCurrentOwner()
    {
        await using var fixture = new OutputLobFixture();
        using var stream = fixture.Reader(12).GetStream(0);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[2], 0, 2, cancel.Token));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        fixture.Lease.Dispose();
        using var next = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        await Assert.ThrowsAsync<InvalidOperationException>(() => stream.ReadAsync(new byte[2], 0, 2));
        Assert.Throws<InvalidOperationException>(() => stream.Read(new byte[2], 2, 0));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Empty(fixture.Channel.Commands);
    }
}
