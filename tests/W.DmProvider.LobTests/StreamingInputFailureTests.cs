using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Collection("Input LOB hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class StreamingInputFailureTests
{
    [Theory]
    [InlineData(false, "negative")]
    [InlineData(true, "negative")]
    [InlineData(false, "token20")]
    [InlineData(true, "token20")]
    [InlineData(false, "token22")]
    [InlineData(true, "token22")]
    [InlineData(false, "truncated")]
    [InlineData(true, "truncated")]
    [InlineData(false, "positive_status")]
    [InlineData(true, "positive_status")]
    [InlineData(false, "reply0")]
    [InlineData(true, "reply0")]
    [InlineData(false, "reply26")]
    [InlineData(true, "reply26")]
    [InlineData(false, "reply_other")]
    [InlineData(true, "reply_other")]
    public async Task BadUploadAcknowledgementStopsAfterFirstSendAndNeverReplays(bool asynchronous, string failure)
    {
        var channel = new UploadRecordingChannel(asyncOnly: asynchronous);
        channel.ReplyOverride = _ => failure switch
        {
            "negative" => UploadRecordingChannel.Frame(-6602, new byte[16]),
            "token20" => UploadRecordingChannel.Frame(0, new byte[20]),
            "token22" => UploadRecordingChannel.Frame(0, new byte[22]),
            "truncated" => UploadRecordingChannel.Frame(0, new byte[21])[..^1],
            "positive_status" => UploadRecordingChannel.Frame(111, new byte[21]),
            "reply0" => UploadRecordingChannel.Frame(0, new byte[21], 0),
            "reply26" => UploadRecordingChannel.Frame(0, new byte[21], 26),
            _ => UploadRecordingChannel.Frame(0, new byte[21], 999)
        };
        using var fixture = new InputSessionFixture(channel);
        var source = new UnknownInputStream(new byte[24], 1, asynchronous);
        InputEncoding.SetParameter(fixture, new DmLobInput(source, DmDbType.Blob));
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var outer = fixture.Session.BeginWireExchange();
        Exception error;
        if (asynchronous) error = await Record.ExceptionAsync(() => InputEncoding.EncodeAsync(fixture, new b()));
        else error = Record.Exception(() => InputEncoding.Encode(fixture, new b()))!;
        Assert.NotNull(error);
        outer.Dispose();
        error = invocation.TranslateFailure(error);
        var info = invocation.CreateFailureInfo(error);
        Assert.Equal(1, channel.Sends);
        Assert.True(channel.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.False(info.ConnectionReusable);
        Assert.False(source.Disposed);
        if (failure == "negative")
        {
            var server = Assert.IsType<DmException>(error);
            Assert.Equal(-6602, server.Number);
            Assert.True(server.HasVerifiedServerResponse);
            Assert.False(server.CanPreserveSessionAfterServerError);
            Assert.Equal(DmErrorKind.Server, info.ErrorKind);
            Assert.Equal(DmOperationOutcome.ServerReported, info.OperationOutcome);
        }
        else
        {
            Assert.Equal(DmErrorKind.Transport, info.ErrorKind);
            Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
            Assert.Null(info.ServerErrorNumber);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InputExceptionAfterOneAcknowledgedChunkKeepsOriginalFailureAndDoesNotReplay(bool asynchronous)
    {
        var channel = new UploadRecordingChannel(asyncOnly: asynchronous);
        using var fixture = new InputSessionFixture(channel);
        var source = new ThrowingInputStream(8, asynchronous);
        InputEncoding.SetParameter(fixture, new DmLobInput(source, DmDbType.Blob));
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var outer = fixture.Session.BeginWireExchange();
        IOException error = asynchronous ? await Assert.ThrowsAsync<IOException>(() => InputEncoding.EncodeAsync(fixture, new b())) :
            Assert.Throws<IOException>(() => InputEncoding.Encode(fixture, new b()));
        outer.Dispose();
        Assert.Same(source.Error, error);
        Assert.Equal(1, channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(channel.IsClosed);
        Assert.Equal(DmOperationOutcome.Unknown, invocation.CreateFailureInfo(error).OperationOutcome);
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("command")]
    [InlineData("deadline")]
    public async Task CancellationAfterUploadSendRetainsFirstCauseAndCapturedIdentity(string cause)
    {
        var channel = new UploadRecordingChannel(asyncOnly: true);
        using var fixture = new InputSessionFixture(channel);
        var source = new UnknownInputStream(new byte[24]);
        InputEncoding.SetParameter(fixture, new DmLobInput(source, DmDbType.Blob));
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(caller.Token);
        var outer = fixture.Session.BeginWireExchange();
        channel.AfterSend = _ =>
        {
            if (cause == "user") caller.Cancel();
            else if (cause == "command") lease.Cancel();
            else fixture.Session.TerminateInvocation(invocation, DmCancelSource.TotalDeadline);
        };
        Exception error = (await Record.ExceptionAsync(() => InputEncoding.EncodeAsync(fixture, new b())))!;
        outer.Dispose();
        error = invocation.TranslateFailure(error);
        if (cause == "deadline")
        {
            var timeout = Assert.IsType<DmTimeoutException>(error);
            Assert.Equal(DmCancelSource.TotalDeadline, timeout.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.Unknown, timeout.FailureInfo.OperationOutcome);
        }
        else
        {
            var canceled = Assert.IsType<DmOperationCanceledException>(error);
            Assert.Equal(cause == "user" ? caller.Token : lease.CommandCancellationToken, canceled.CancellationToken);
            Assert.Equal(DmOperationOutcome.Unknown, canceled.FailureInfo.OperationOutcome);
        }
        Assert.Equal(1, channel.Sends);
        Assert.True(channel.IsClosed);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task PreCanceledCursorDoesNotReadAndHasNoWireSend()
    {
        var source = new UnknownInputStream([1]);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        using var cursor = new DmLobInput(source, DmDbType.Blob).OpenCursor(8, "UTF-8");
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cursor.ReadChunkAsync(caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(0, source.ReadCalls);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task CancellationBeforeAnySendIsNotSentAndDoesNotConsumeInput()
    {
        var channel = new UploadRecordingChannel(asyncOnly: true);
        using var fixture = new InputSessionFixture(channel);
        var source = new UnknownInputStream([1]);
        InputEncoding.SetParameter(fixture, new DmLobInput(source, DmDbType.Blob));
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(caller.Token);
        var outer = fixture.Session.BeginWireExchange();
        caller.Cancel();
        Exception error = (await Record.ExceptionAsync(() => InputEncoding.EncodeAsync(fixture, new b())))!;
        outer.Dispose();
        var canceled = Assert.IsType<DmOperationCanceledException>(invocation.TranslateFailure(error));
        Assert.Equal(caller.Token, canceled.CancellationToken);
        Assert.Equal(DmOperationOutcome.NotSent, canceled.FailureInfo.OperationOutcome);
        Assert.Equal(0, channel.Sends);
        Assert.Equal(0, source.ReadCalls);
        Assert.False(channel.IsClosed);
    }

    private sealed class ThrowingInputStream(int prefix, bool asyncOnly) : Stream
    {
        private int remaining = prefix;
        internal readonly IOException Error = new("Synthetic caller input failure.");
        internal bool Disposed;
        private int Next(Span<byte> buffer)
        {
            if (remaining == 0) throw Error;
            int count = Math.Min(buffer.Length, remaining); buffer[..count].Fill(7); remaining -= count; return count;
        }
        public override int Read(byte[] buffer, int offset, int count)
        { if (asyncOnly) throw new InvalidOperationException("Sync input read forbidden."); return Next(buffer.AsSpan(offset, count)); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Next(buffer.Span)); }
        protected override void Dispose(bool disposing) { Disposed = true; throw new InvalidOperationException("Caller owns input."); }
        public override bool CanRead => true;
        public override bool CanSeek => throw new InvalidOperationException("CanSeek forbidden.");
        public override bool CanWrite => false;
        public override long Length => throw new InvalidOperationException("Length forbidden.");
        public override long Position { get => throw new InvalidOperationException("Position forbidden."); set => throw new InvalidOperationException("Position forbidden."); }
        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Seek forbidden.");
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
