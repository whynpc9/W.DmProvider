using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Diagnostics;

namespace W.Dm.Internal.Legacy.A;

internal partial class B
{
    // The upload ACK success body is exactly 21 bytes; the budget keeps bounded
    // room for a complete server error body instead of the global 64 MiB frame.
    private const int UploadAckSuccessBodyBytes = 21;
    private const int UploadAckMaxBodyLength = UploadAckSuccessBodyBytes + 4096;

    private static int StreamingInputChunkSize(A statement)
    {
        var settings = statement.G().ConnProperty;
        if (settings.msgVersion < 10) throw new NotSupportedException("Streaming LOB parameters require the modern upload protocol.");
        if (settings.MaxLobDataLenPerMsg <= 0) throw new InvalidDataException("Invalid negotiated parameter upload chunk limit.");
        return Math.Min(statement.G().Conn.Settings.LobChunkSize, settings.MaxLobDataLenPerMsg);
    }

    internal byte[] UploadStreamingParameter(A statement, int index, DmLobInput input)
    {
        CancellationToken token = DmInvocation.Current?.CancellationToken ?? throw new InvalidOperationException("Input upload has no invocation.");
        using var cursor = OpenStreamingParameterCursor(statement, input);
        return UploadStreamingParameter(statement, index, input, cursor, cursor.ReadChunk(token));
    }

    internal DmLobInput.Cursor OpenStreamingParameterCursor(A statement, DmLobInput input) =>
        input.OpenCursor(StreamingInputChunkSize(statement), a().ServerEncoding);

    internal byte[] UploadStreamingParameter(A statement, int index, DmLobInput input, DmLobInput.Cursor cursor, int firstCount)
    {
        CancellationToken token = DmInvocation.Current?.CancellationToken ?? throw new InvalidOperationException("Input upload has no invocation.");
        byte[] acknowledgement = global::W.Dm.Internal.Legacy.A.C.A();
        var send = new b(); var receive = new b();
        int count = firstCount;
        while (true)
        {
            acknowledgement = Wire(() =>
            {
                global::W.Dm.Internal.Legacy.A.C.A(send, statement.g(), index, cursor.Buffer, count, a(), acknowledgement);
                A(send, receive, statement.G().ConnProperty.SocketTimeout, UploadAckMaxBodyLength);
                return AcceptParameterUploadAck(receive, statement);
            });
            // A positive short caller read is not EOF. Cursor fills until capacity
            // or actual EOF; an exact-full final chunk retains a zero-byte tail.
            bool terminal = count < cursor.Buffer.Length && cursor.EndOfInput;
            DmDiagnosticsCore.LobChunk(true, input.IsText, terminal);
            if (terminal) return acknowledgement;
            count = cursor.ReadChunk(token);
        }
    }

    internal async Task<byte[]> UploadStreamingParameterAsync(A statement, int index, DmLobInput input, CancellationToken cancellationToken)
    {
        cancellationToken = AsyncCancellationToken(cancellationToken);
        using var cursor = OpenStreamingParameterCursor(statement, input);
        int count = await cursor.ReadChunkAsync(cancellationToken).ConfigureAwait(false);
        return await UploadStreamingParameterAsync(statement, index, input, cursor, count, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<byte[]> UploadStreamingParameterAsync(A statement, int index, DmLobInput input, DmLobInput.Cursor cursor, int firstCount, CancellationToken cancellationToken)
    {
        cancellationToken = AsyncCancellationToken(cancellationToken);
        byte[] acknowledgement = global::W.Dm.Internal.Legacy.A.C.A();
        var send = new b(); var receive = new b();
        int count = firstCount;
        while (true)
        {
            CheckAsyncTermination(cancellationToken);
            acknowledgement = await WireAsync(async () =>
            {
                global::W.Dm.Internal.Legacy.A.C.A(send, statement.g(), index, cursor.Buffer, count, a(), acknowledgement);
                await ExchangeAsync(send, receive, statement.G().ConnProperty.SocketTimeout, cancellationToken, UploadAckMaxBodyLength).ConfigureAwait(false);
                DmResultProtocolTrace.RecordFrame(send.I(), receive);
                DmWireTestHooks.ResponseReady();
                decodeOwner = this;
                return AcceptParameterUploadAck(receive, statement);
            }, cancellationToken).ConfigureAwait(false);
            bool terminal = count < cursor.Buffer.Length && cursor.EndOfInput;
            DmDiagnosticsCore.LobChunk(true, input.IsText, terminal);
            if (terminal) return acknowledgement;
            count = await cursor.ReadChunkAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static byte[] AcceptParameterUploadAck(b response, A statement)
    {
        DmInvocation invocation = DmInvocation.Current ?? throw new InvalidOperationException("Parameter ACK has no invocation.");
        invocation.ThrowIfTerminated();
        statement.G().Session.RequireActiveWireExchange();
        if (!ReferenceEquals(invocation.Lease.Session, statement.G().Session))
            throw new InvalidOperationException("Parameter ACK belongs to another session.");
        // Actual opcode26 ACK headers were independently observed as261 on the
        // UTF8/msg21 and GB18030/msg11 TEST profiles. Other replies fail closed.
        if (response.I() != 261 || response.a() != DmFrameReader.HeaderSize || response.a(false) != response.k())
            throw new InvalidDataException("Parameter upload ACK has an invalid reply or frame boundary.");
        if (response.L() < 0)
            global::W.Dm.Internal.Legacy.A.c.ThrowOwnedStatementServerError(response, statement, statement.G().ConnProperty, 26, allowPreservation: false);
        if (response.L() != 0 || response.a() != DmFrameReader.HeaderSize ||
            response.k() != 21 || response.a(false) != 21)
            throw new InvalidDataException("Parameter upload ACK has an invalid status or token boundary.");
        byte[] acknowledgement = response.F(21);
        invocation.ThrowIfTerminated();
        return acknowledgement;
    }
}
