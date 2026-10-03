using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Diagnostics;

namespace W.Dm.Internal.Transport;

internal static class DmTransportTestHooks
{
    internal static Action<bool, string> BeforeNetworkIo;
    internal static Action BeforeTlsRead;
    internal static Action<OperationIdentity> BeforeSendAttempt;
    internal static Action<OperationIdentity> AfterSendAttempt;
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
        Volatile.Write(ref BeforeNetworkIo, null);
        Interlocked.Exchange(ref successfulTcpConnections, 0);
        Interlocked.Exchange(ref attemptedTcpConnections, 0);
        Interlocked.Exchange(ref createdTcpSockets, 0);
        Interlocked.Exchange(ref disposedTcpSockets, 0);
        Interlocked.Exchange(ref successfulTlsUpgrades, 0);
        Interlocked.Exchange(ref failedTlsUpgrades, 0);
        Volatile.Write(ref lastNegotiatedTlsProtocol, 0);
        Volatile.Write(ref BeforeTlsRead, null);
        Volatile.Write(ref BeforeSendAttempt, null);
        Volatile.Write(ref AfterSendAttempt, null);
        Volatile.Write(ref BeforeControlSendAttempt, null);
        Volatile.Write(ref AfterControlAttemptMarked, null);
        Volatile.Write(ref AfterControlFrameValidated, null);
        Volatile.Write(ref BeforeControlAck, null);
    }
    internal static void Attempted() => Interlocked.Increment(ref attemptedTcpConnections);
    internal static void Connected() => Interlocked.Increment(ref successfulTcpConnections);
    internal static void Created() { Interlocked.Increment(ref createdTcpSockets); DmDiagnosticsCore.ConnectionCreated(); }
    internal static void Disposed() { Interlocked.Increment(ref disposedTcpSockets); DmDiagnosticsCore.ConnectionClosed(); }
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
    ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ValueTask.FromException<int>(new NotSupportedException("This byte channel does not support asynchronous sends."));
    ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ValueTask.FromException<int>(new NotSupportedException("This byte channel does not support asynchronous receives."));
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
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(false, "connect");
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

    internal async Task OpenAsync(DmDeadline deadline, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(true, "connect");
        lock (gate)
        {
            if (closed) throw new ObjectDisposedException(nameof(DmTransport));
            if (channel != null || openStarted) throw new InvalidOperationException("Transport has already opened.");
            openStarted = true;
        }

        try
        {
            IPAddress[] addresses;
            using (var dnsDeadline = new DmIoCancellation(deadline, cancellationToken, closeSource.Token))
            {
                try { addresses = await resolveAddresses(host, dnsDeadline.Token).WaitAsync(dnsDeadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException ex) { dnsDeadline.RethrowCancellation(ex); throw; }
            }
            deadline.ThrowIfExpired();
            if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);

            Exception lastFailure = null;
            foreach (IPAddress address in addresses)
            {
                deadline.ThrowIfExpired();
                cancellationToken.ThrowIfCancellationRequested();
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
                    using var attemptDeadline = new DmIoCancellation(deadline, cancellationToken, closeSource.Token);
                    DmTransportTestHooks.Attempted();
                    try { await connectSocket(candidate, new IPEndPoint(address, port), attemptDeadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException ex) { attemptDeadline.RethrowCancellation(ex); throw; }
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
        catch
        {
            Close();
            throw;
        }
    }

    private DmIoCancellation LinkedDeadline(DmDeadline deadline) =>
        new(deadline, DmInvocation.Current?.CancellationToken ?? CancellationToken.None, closeSource.Token);

    internal void UpgradeTls(DmTlsOptions options, DmDeadline deadline)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(false, "tls");
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

    internal async Task UpgradeTlsAsync(DmTlsOptions options, DmDeadline deadline, CancellationToken cancellationToken = default)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(true, "tls");
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
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

            using (var handshakeDeadline = new DmIoCancellation(deadline, cancellationToken, closeSource.Token))
            {
                try { await ssl.AuthenticateAsClientAsync(tls.Authentication, handshakeDeadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException ex) { handshakeDeadline.RethrowCancellation(ex); throw; }
            }
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

    private static void CheckOperationDeadline(DmDeadline deadline)
    {
        DmInvocation.Current?.ThrowIfTerminated();
        deadline.ThrowIfExpired();
    }

    private static int Timeout(DmDeadline deadline, int limitMilliseconds)
    {
        CheckOperationDeadline(deadline);
        int remaining = deadline.IsInfinite ? 0 : Math.Max(1, deadline.RemainingMilliseconds);
        if (limitMilliseconds <= 0) return remaining;
        return remaining == 0 ? limitMilliseconds : Math.Min(remaining, limitMilliseconds);
    }

    private static void PrepareSendAttempt(DmInvocation invocation, bool first)
    {
        if (invocation == null) return;
        DmTransactionControlKind kind = default;
        bool control = first && invocation.Lease.Session.IsCurrentTransactionControl(invocation, out kind);
        if (control)
        {
            try { Volatile.Read(ref DmTransportTestHooks.BeforeControlSendAttempt)?.Invoke(invocation.Identity, kind); }
            catch { invocation.Lease.Session.MarkRecoverablePreSendFailure(invocation); throw; }
        }
        Volatile.Read(ref DmTransportTestHooks.BeforeSendAttempt)?.Invoke(invocation.Identity);
        invocation.Lease.Session.TryBeginSendAttempt(invocation);
        if (control) Volatile.Read(ref DmTransportTestHooks.AfterControlAttemptMarked)?.Invoke(invocation.Identity, kind);
        Volatile.Read(ref DmTransportTestHooks.AfterSendAttempt)?.Invoke(invocation.Identity);
    }

    internal void SendAll(byte[] buffer, int offset, int count, DmDeadline deadline, int timeoutMilliseconds, Action<int> onSent = null)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(false, "send");
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
                invocation?.Lease.Session.TerminateInvocation(invocation, DmCancelSource.TotalDeadline);
                invocation?.Lease.Session.MarkRecoverablePreSendFailure(invocation);
                throw;
            }
            PrepareSendAttempt(invocation, sent == 0);
            int progress = current.Send(buffer, offset + sent, count - sent, timeout);
            if (progress > 0 && progress <= count - sent) DmDiagnosticsCore.NetworkBytes(progress, true);
            CheckOperationDeadline(deadline);
            if (progress <= 0 || progress > count - sent) throw new IOException("Socket send made no valid progress.");
            sent += progress;
            onSent?.Invoke(progress);
        }
    }

    internal int ReadSome(byte[] buffer, int offset, int count, DmDeadline deadline, int timeoutMilliseconds)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(false, "receive");
        ValidateRange(buffer, offset, count);
        if (count == 0) return 0;
        DmInvocation.Current?.ThrowIfTerminated();
        if (DmInvocation.Current is { } invocation) invocation.Phase = DmFailurePhase.Receive;
        int read = CurrentChannel().Receive(buffer, offset, count, Timeout(deadline, timeoutMilliseconds));
        if (read > 0 && read <= count) DmDiagnosticsCore.NetworkBytes(read, false);
        CheckOperationDeadline(deadline);
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

    internal async ValueTask SendAllAsync(byte[] buffer, int offset, int count, DmDeadline deadline,
        int timeoutMilliseconds, CancellationToken cancellationToken = default, Action<int> onSent = null)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(true, "send");
        ValidateRange(buffer, offset, count);
        DmInvocation.Current?.ThrowIfTerminated();
        cancellationToken.ThrowIfCancellationRequested();
        IDmByteChannel current = CurrentChannel();
        int sent = 0;
        while (sent < count)
        {
            DmInvocation invocation = DmInvocation.Current;
            DmIoCancellation budget;
            try { budget = new DmIoCancellation(deadline, cancellationToken, closeSource.Token, timeoutMilliseconds); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                if (ex is TimeoutException)
                    invocation?.Lease.Session.TerminateInvocation(invocation, DmCancelSource.TotalDeadline);
                if (sent == 0) invocation?.Lease.Session.MarkRecoverablePreSendFailure(invocation);
                throw;
            }
            using (budget)
            {
                PrepareSendAttempt(invocation, sent == 0);
                int progress;
                try { progress = await current.SendAsync(buffer, offset + sent, count - sent, budget.Token).ConfigureAwait(false); }
                catch (OperationCanceledException ex) { budget.RethrowCancellation(ex); throw; }
                if (progress > 0 && progress <= count - sent) DmDiagnosticsCore.NetworkBytes(progress, true);
                CheckOperationDeadline(deadline);
                if (progress <= 0 || progress > count - sent) throw new IOException("Socket send made no valid progress.");
                sent += progress;
                onSent?.Invoke(progress);
            }
        }
    }

    internal async ValueTask<int> ReadSomeAsync(byte[] buffer, int offset, int count, DmDeadline deadline,
        int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        DmTransportTestHooks.BeforeNetworkIo?.Invoke(true, "receive");
        ValidateRange(buffer, offset, count);
        DmInvocation.Current?.ThrowIfTerminated();
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0) return 0;
        if (DmInvocation.Current is { } invocation) invocation.Phase = DmFailurePhase.Receive;
        using var budget = new DmIoCancellation(deadline, cancellationToken, closeSource.Token, timeoutMilliseconds);
        int read;
        try { read = await CurrentChannel().ReceiveAsync(buffer, offset, count, budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) { budget.RethrowCancellation(ex); throw; }
        if (read > 0 && read <= count) DmDiagnosticsCore.NetworkBytes(read, false);
        CheckOperationDeadline(deadline);
        if (read <= 0) throw new EndOfStreamException("Socket closed before the expected bytes arrived.");
        if (read > count) throw new IOException("Socket returned an invalid byte count.");
        return read;
    }

    internal async ValueTask ReadExactlyAsync(byte[] buffer, int offset, int count, DmDeadline deadline,
        int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        ValidateRange(buffer, offset, count);
        cancellationToken.ThrowIfCancellationRequested();
        int read = 0;
        while (read < count)
            read += await ReadSomeAsync(buffer, offset + read, count - read, deadline,
                timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
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
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Socket.SendAsync(buffer.AsMemory(offset, count), SocketFlags.None, cancellationToken);
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Socket.ReceiveAsync(buffer.AsMemory(offset, count), SocketFlags.None, cancellationToken);
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
        public async ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            return count;
        }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Volatile.Read(ref DmTransportTestHooks.BeforeTlsRead)?.Invoke();
            return stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
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
