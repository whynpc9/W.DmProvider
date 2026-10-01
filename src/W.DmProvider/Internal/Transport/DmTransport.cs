using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;

namespace W.Dm.Internal.Transport;

internal static class DmTransportTestHooks
{
    internal static Action BeforeTlsRead;
    internal static Action<OperationIdentity, DmTransactionControlKind> BeforeControlSendAttempt;
    internal static Action<OperationIdentity, DmTransactionControlKind> AfterControlAttemptMarked;
    internal static Action<OperationIdentity, DmTransactionControlKind, short, short, int> AfterControlFrameValidated;
    internal static Action<OperationIdentity, DmTransactionControlKind> BeforeControlAck;
    private static long successfulTcpConnections;
    private static long attemptedTcpConnections;
    private static long createdTcpSockets;
    private static long disposedTcpSockets;
    private static long successfulTlsUpgrades;
    private static long failedTlsUpgrades;
    private static int lastNegotiatedTlsProtocol;
    internal static long SuccessfulTcpConnections => Interlocked.Read(ref successfulTcpConnections);
    internal static long AttemptedTcpConnections => Interlocked.Read(ref attemptedTcpConnections);
    internal static long CreatedTcpSockets => Interlocked.Read(ref createdTcpSockets);
    internal static long DisposedTcpSockets => Interlocked.Read(ref disposedTcpSockets);
    internal static long SuccessfulTlsUpgrades => Interlocked.Read(ref successfulTlsUpgrades);
    internal static long FailedTlsUpgrades => Interlocked.Read(ref failedTlsUpgrades);
    internal static SslProtocols LastNegotiatedTlsProtocol => (SslProtocols)Volatile.Read(ref lastNegotiatedTlsProtocol);
    internal static void Reset()
    {
        Interlocked.Exchange(ref successfulTcpConnections, 0);
        Interlocked.Exchange(ref attemptedTcpConnections, 0);
        Interlocked.Exchange(ref createdTcpSockets, 0);
        Interlocked.Exchange(ref disposedTcpSockets, 0);
        Interlocked.Exchange(ref successfulTlsUpgrades, 0);
        Interlocked.Exchange(ref failedTlsUpgrades, 0);
        Volatile.Write(ref lastNegotiatedTlsProtocol, 0);
        Volatile.Write(ref BeforeTlsRead, null);
        Volatile.Write(ref BeforeControlSendAttempt, null);
        Volatile.Write(ref AfterControlAttemptMarked, null);
        Volatile.Write(ref AfterControlFrameValidated, null);
        Volatile.Write(ref BeforeControlAck, null);
    }
    internal static void Attempted() => Interlocked.Increment(ref attemptedTcpConnections);
    internal static void Connected() => Interlocked.Increment(ref successfulTcpConnections);
    internal static void Created() => Interlocked.Increment(ref createdTcpSockets);
    internal static void Disposed() => Interlocked.Increment(ref disposedTcpSockets);
    internal static void TlsUpgraded(SslProtocols protocol)
    {
        Volatile.Write(ref lastNegotiatedTlsProtocol, (int)protocol);
        Interlocked.Increment(ref successfulTlsUpgrades);
    }
    internal static void TlsFailed() => Interlocked.Increment(ref failedTlsUpgrades);
}

// The channel boundary permits deterministic short-I/O tests without a database.
internal interface IDmByteChannel : IDisposable
{
    int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds);
    int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds);
    bool IsClosed { get; }
}

internal sealed class DmTransport : IDisposable
{
    private readonly object gate = new();
    private readonly string host;
    private readonly int port;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses;
    private readonly Func<Socket, IPEndPoint, CancellationToken, ValueTask> connectSocket;
    private readonly Action<IPAddress, int> beforeAttempt;
    private readonly CancellationTokenSource closeSource = new();
    private IDmByteChannel channel;
    private Socket connectingSocket;
    private SslStreamByteChannel pendingTls;
    private bool closed;
    private bool openStarted;
    private bool tlsUpgradeStarted;

    internal DmTransport(string host, int port,
        Func<string, CancellationToken, Task<IPAddress[]>> resolver = null,
        Func<Socket, IPEndPoint, CancellationToken, ValueTask> connector = null,
        Action<IPAddress, int> beforeAttempt = null)
    {
        this.host = !string.IsNullOrWhiteSpace(host) ? host : throw new ArgumentException("Host is required.", nameof(host));
        this.port = port is > 0 and <= 65535 ? port : throw new ArgumentOutOfRangeException(nameof(port));
        resolveAddresses = resolver ?? Dns.GetHostAddressesAsync;
        connectSocket = connector ?? ((socket, endpoint, cancellationToken) => socket.ConnectAsync(endpoint, cancellationToken));
        this.beforeAttempt = beforeAttempt;
    }

    internal DmTransport(IDmByteChannel connectedChannel) =>
        channel = connectedChannel ?? throw new ArgumentNullException(nameof(connectedChannel));

    internal bool IsClosed
    {
        get { lock (gate) return closed; }
    }

    internal void Open(DmDeadline deadline)
    {
        lock (gate)
        {
            if (closed) throw new ObjectDisposedException(nameof(DmTransport));
            if (channel != null || openStarted) throw new InvalidOperationException("Transport has already opened.");
            openStarted = true;
        }

        try
        {
            IPAddress[] addresses;
            using (var dnsDeadline = LinkedDeadline(deadline))
                addresses = resolveAddresses(host, dnsDeadline.Token).GetAwaiter().GetResult();
            deadline.ThrowIfExpired();
            if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);

            Exception lastFailure = null;
            foreach (IPAddress address in addresses)
            {
                deadline.ThrowIfExpired();
                closeSource.Token.ThrowIfCancellationRequested();
                beforeAttempt?.Invoke(address, deadline.RemainingMilliseconds);
                deadline.ThrowIfExpired();
                Socket candidate;
                try { candidate = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp); }
                catch (SocketException ex) { lastFailure = ex; continue; }
                DmTransportTestHooks.Created();
                bool rejected;
                lock (gate)
                {
                    rejected = closed;
                    if (!rejected) connectingSocket = candidate;
                }
                if (rejected) { DisposeSocket(candidate); throw new ObjectDisposedException(nameof(DmTransport)); }
                bool transferred = false;
                try
                {
                    using var attemptDeadline = LinkedDeadline(deadline);
                    DmTransportTestHooks.Attempted();
                    connectSocket(candidate, new IPEndPoint(address, port), attemptDeadline.Token).AsTask().GetAwaiter().GetResult();
                    deadline.ThrowIfExpired();
                    lock (gate)
                    {
                        if (closed) throw new ObjectDisposedException(nameof(DmTransport));
                        deadline.ThrowIfExpired();
                        channel = new SocketByteChannel(candidate);
                        connectingSocket = null;
                        transferred = true;
                        DmTransportTestHooks.Connected();
                    }
                    return;
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    lastFailure = ex;
                }
                catch (OperationCanceledException) when (!closeSource.IsCancellationRequested)
                {
                    deadline.ThrowIfExpired();
                    throw new TimeoutException("TCP connection deadline expired.");
                }
                finally
                {
                    bool dispose = false;
                    lock (gate)
                    {
                        if (!transferred && ReferenceEquals(connectingSocket, candidate))
                        {
                            connectingSocket = null;
                            dispose = true;
                        }
                    }
                    if (dispose) DisposeSocket(candidate);
                }
            }
            deadline.ThrowIfExpired();
            throw lastFailure ?? new SocketException((int)SocketError.NotConnected);
        }
        catch (OperationCanceledException) when (!closeSource.IsCancellationRequested)
        {
            Close();
            deadline.ThrowIfExpired();
            throw new TimeoutException("Connection deadline expired.");
        }
        catch
        {
            Close();
            throw;
        }
    }

    private CancellationTokenSource LinkedDeadline(DmDeadline deadline)
    {
        deadline.ThrowIfExpired();
        var source = CancellationTokenSource.CreateLinkedTokenSource(closeSource.Token);
        if (!deadline.IsInfinite) source.CancelAfter(deadline.RemainingMilliseconds);
        return source;
    }

    internal void UpgradeTls(DmTlsOptions options, DmDeadline deadline)
    {
        ArgumentNullException.ThrowIfNull(options);
        SocketByteChannel raw;
        lock (gate)
        {
            if (closed) throw new ObjectDisposedException(nameof(DmTransport));
            raw = channel as SocketByteChannel ?? throw new InvalidOperationException("TLS requires an open raw TCP channel.");
            if (tlsUpgradeStarted) throw new InvalidOperationException("TLS authentication has already started.");
            tlsUpgradeStarted = true;
        }

        DmTlsOptionsScope scope = null;
        SslStreamByteChannel tls = null;
        bool upgraded = false;
        try
        {
            deadline.ThrowIfExpired();
            scope = options.CreateAuthenticationScope(deadline);
            var network = new NetworkStream(raw.Socket, ownsSocket: false);
            SslStream ssl;
            try { ssl = new SslStream(network, leaveInnerStreamOpen: false); }
            catch { network.Dispose(); throw; }
            tls = new SslStreamByteChannel(ssl, raw.Socket, scope);
            scope = null;
            lock (gate)
            {
                if (closed || !ReferenceEquals(channel, raw)) throw new ObjectDisposedException(nameof(DmTransport));
                pendingTls = tls;
            }

            using (var handshakeDeadline = LinkedDeadline(deadline))
                ssl.AuthenticateAsClientAsync(tls.Authentication, handshakeDeadline.Token).GetAwaiter().GetResult();
            deadline.ThrowIfExpired();
            if (!ssl.IsAuthenticated || !ssl.IsEncrypted || ssl.SslProtocol is not (SslProtocols.Tls12 or SslProtocols.Tls13))
                throw new AuthenticationException("TLS transport did not negotiate an accepted protocol.");
            lock (gate)
            {
                if (closed || !ReferenceEquals(channel, raw) || !ReferenceEquals(pendingTls, tls))
                    throw new ObjectDisposedException(nameof(DmTransport));
                deadline.ThrowIfExpired();
                raw.RelinquishSocket();
                tls.ActivateSocketOwnership();
                channel = tls;
                pendingTls = null;
            }
            DmTransportTestHooks.TlsUpgraded(ssl.SslProtocol);
            upgraded = true;
        }
        catch (OperationCanceledException) when (!closeSource.IsCancellationRequested)
        {
            deadline.ThrowIfExpired();
            throw new TimeoutException("TLS authentication deadline expired.");
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
            throw new AuthenticationException("TLS authentication failed.", ex);
        }
        finally
        {
            if (!upgraded)
            {
                DmTransportTestHooks.TlsFailed();
                try { Close(); }
                finally
                {
                    tls?.Dispose();
                    scope?.Dispose();
                }
            }
        }
    }

    private IDmByteChannel CurrentChannel()
    {
        lock (gate)
        {
            if (closed) throw new ObjectDisposedException(nameof(DmTransport));
            if (tlsUpgradeStarted && channel is SocketByteChannel)
                throw new InvalidOperationException("Raw I/O is unavailable during TLS upgrade.");
            return channel ?? throw new InvalidOperationException("Transport is not open.");
        }
    }

    private static int Timeout(DmDeadline deadline, int limitMilliseconds)
    {
        deadline.ThrowIfExpired();
        int remaining = deadline.IsInfinite ? 0 : Math.Max(1, deadline.RemainingMilliseconds);
        if (limitMilliseconds <= 0) return remaining;
        return remaining == 0 ? limitMilliseconds : Math.Min(remaining, limitMilliseconds);
    }

    internal void SendAll(byte[] buffer, int offset, int count, DmDeadline deadline, int timeoutMilliseconds, Action<int> onSent = null)
    {
        ValidateRange(buffer, offset, count);
        IDmByteChannel current = CurrentChannel();
        int sent = 0;
        while (sent < count)
        {
            DmInvocation invocation = DmInvocation.Current;
            int timeout;
            try { timeout = Timeout(deadline, timeoutMilliseconds); }
            catch (TimeoutException)
            {
                invocation?.Lease.Session.MarkRecoverablePreSendFailure(invocation);
                throw;
            }
            if (sent == 0 && invocation != null &&
                invocation.Lease.Session.IsCurrentTransactionControl(invocation, out DmTransactionControlKind kind))
            {
                try { Volatile.Read(ref DmTransportTestHooks.BeforeControlSendAttempt)?.Invoke(invocation.Identity, kind); }
                catch
                {
                    invocation.Lease.Session.MarkRecoverablePreSendFailure(invocation);
                    throw;
                }
                invocation.Lease.Session.MarkTransactionSendAttempt(invocation);
                Volatile.Read(ref DmTransportTestHooks.AfterControlAttemptMarked)?.Invoke(invocation.Identity, kind);
            }
            int progress = current.Send(buffer, offset + sent, count - sent, timeout);
            deadline.ThrowIfExpired();
            if (progress <= 0 || progress > count - sent) throw new IOException("Socket send made no valid progress.");
            sent += progress;
            onSent?.Invoke(progress);
        }
    }

    internal int ReadSome(byte[] buffer, int offset, int count, DmDeadline deadline, int timeoutMilliseconds)
    {
        ValidateRange(buffer, offset, count);
        if (count == 0) return 0;
        int read = CurrentChannel().Receive(buffer, offset, count, Timeout(deadline, timeoutMilliseconds));
        deadline.ThrowIfExpired();
        if (read <= 0) throw new EndOfStreamException("Socket closed before the expected bytes arrived.");
        if (read > count) throw new IOException("Socket returned an invalid byte count.");
        return read;
    }

    internal void ReadExactly(byte[] buffer, int offset, int count, DmDeadline deadline, int timeoutMilliseconds)
    {
        ValidateRange(buffer, offset, count);
        int read = 0;
        while (read < count)
            read += ReadSome(buffer, offset + read, count - read, deadline, timeoutMilliseconds);
    }

    internal bool IsPeerClosed()
    {
        var current = CurrentChannel();
        return current.IsClosed;
    }

    private static void ValidateRange(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(count));
    }

    private static void DisposeSocket(Socket socket)
    {
        try { socket.Dispose(); }
        finally { DmTransportTestHooks.Disposed(); }
    }

    internal void Close()
    {
        IDmByteChannel captured;
        Socket pending;
        SslStreamByteChannel upgrading;
        lock (gate)
        {
            if (closed) return;
            closed = true;
            captured = channel;
            channel = null;
            pending = connectingSocket;
            connectingSocket = null;
            upgrading = pendingTls;
            pendingTls = null;
        }
        try { closeSource.Cancel(); }
        finally
        {
            try { if (pending != null) DisposeSocket(pending); }
            finally
            {
                try { captured?.Dispose(); }
                finally { upgrading?.Dispose(); }
            }
        }
    }

    public void Dispose() => Close();

    private sealed class SocketByteChannel : IDmByteChannel
    {
        internal Socket Socket { get; }
        private int disposed;
        private bool ownsSocket = true;
        internal SocketByteChannel(Socket socket) => Socket = socket;
        internal void RelinquishSocket() => ownsSocket = false;
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            Socket.SendTimeout = timeoutMilliseconds;
            return Socket.Send(buffer, offset, count, SocketFlags.None);
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            Socket.ReceiveTimeout = timeoutMilliseconds;
            return Socket.Receive(buffer, offset, count, SocketFlags.None);
        }
        public bool IsClosed => Volatile.Read(ref disposed) != 0;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (ownsSocket) DisposeSocket(Socket);
        }
    }

    private sealed class SslStreamByteChannel : IDmByteChannel
    {
        private readonly SslStream stream;
        private readonly Socket socket;
        private readonly DmTlsOptionsScope scope;
        private int disposed;
        private bool ownsSocket;

        internal SslClientAuthenticationOptions Authentication => scope.Authentication;
        internal SslStreamByteChannel(SslStream stream, Socket socket, DmTlsOptionsScope scope)
        {
            this.stream = stream;
            this.socket = socket;
            this.scope = scope;
        }
        internal void ActivateSocketOwnership() => ownsSocket = true;
        public bool IsClosed => Volatile.Read(ref disposed) != 0;
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            stream.WriteTimeout = timeoutMilliseconds == 0 ? System.Threading.Timeout.Infinite : timeoutMilliseconds;
            stream.Write(buffer, offset, count);
            return count;
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            stream.ReadTimeout = timeoutMilliseconds == 0 ? System.Threading.Timeout.Infinite : timeoutMilliseconds;
            Volatile.Read(ref DmTransportTestHooks.BeforeTlsRead)?.Invoke();
            return stream.Read(buffer, offset, count);
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { if (ownsSocket) DisposeSocket(socket); }
            finally
            {
                try { stream.Dispose(); }
                finally { scope.Dispose(); }
            }
        }
    }
}
