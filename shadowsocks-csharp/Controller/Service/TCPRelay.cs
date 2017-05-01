using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Encryption.AEAD;
using Shadowsocks.Encryption.Exception;
using Shadowsocks.Model;
using Shadowsocks.Proxy;
using Shadowsocks.Util.Sockets;

namespace Shadowsocks.Controller.Service
{
    internal class TCPRelay : Listener.Service
    {
        private readonly Configuration _config;
        private readonly ShadowsocksController _controller;
        private DateTime _lastSweepTime;

        public TCPRelay(ShadowsocksController controller, Configuration conf)
        {
            _controller = controller;
            _config = conf;
            Handlers = new HashSet<TCPHandler>();
            _lastSweepTime = DateTime.Now;
        }

        public ISet<TCPHandler> Handlers { get; set; }

        public override bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if ((socket.ProtocolType != ProtocolType.Tcp) || (length < 2) || (firstPacket[0] != 5))
                return false;
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            var handler = new TCPHandler(_controller, _config, this, socket);

            IList<TCPHandler> handlersToClose = new List<TCPHandler>();
            lock (Handlers)
            {
                Handlers.Add(handler);
                var now = DateTime.Now;
                if (now - _lastSweepTime > TimeSpan.FromSeconds(1))
                {
                    _lastSweepTime = now;
                    foreach (var handler1 in Handlers)
                        if (now - handler1.LastActivity > TimeSpan.FromSeconds(900))
                            handlersToClose.Add(handler1);
                }
            }
            foreach (var handler1 in handlersToClose)
            {
                Logging.Debug("Closing timed out TCP connection.");
                handler1.Close();
            }

            /*
             * Start after we put it into Handlers set. Otherwise if it failed in handler.Start()
             * then it will call handler.Close() before we add it into the set.
             * Then the handler will never release until the next Handle call. Sometimes it will
             * cause odd problems (especially during memory profiling).
             */
            handler.Start(firstPacket, length);

            return true;
        }

        public override void Stop()
        {
            var handlersToClose = new List<TCPHandler>();
            lock (Handlers)
            {
                handlersToClose.AddRange(Handlers);
            }
            handlersToClose.ForEach(h => h.Close());
        }
    }

    internal class TCPHandler
    {
        // each recv size.
        public const int RecvSize = 2048;

        // overhead of one chunk, reserved for AEAD ciphers
        public const int ChunkOverheadSize = 16*2 /* two tags */+ AEADEncryptor.CHUNK_LEN_BYTES;

        // max chunk size
        public const uint MaxChunkSize = AEADEncryptor.CHUNK_LEN_MASK + AEADEncryptor.CHUNK_LEN_BYTES + 16*2;

        // In general, the ciphertext length, we should take overhead into account
        public const int BufferSize = RecvSize + (int) MaxChunkSize + 32 /* max salt len */;

        private const int CMD_CONNECT = 0x01;
        private const int CMD_UDP_ASSOC = 0x03;
        private readonly object _closeConnLock = new object();

        private readonly object _decryptionLock = new object();

        // instance-based lock without static
        private readonly object _encryptionLock = new object();
        private readonly int _proxyTimeout;

        private readonly int _serverTimeout;

        private int _addrBufLength = -1;
        private bool _closed;

        private byte _command;
        private readonly Configuration _config;
        private readonly Socket _connection;

        private bool _connectionShutdown;

        // client -> local proxy (plaintext, before encrypt)
        private readonly byte[] _connetionRecvBuffer = new byte[BufferSize];

        // local proxy -> client (ciphertext, before decrypt)
        private readonly byte[] _connetionSendBuffer = new byte[BufferSize];

        private readonly ShadowsocksController _controller;

        private AsyncSession _currentRemoteSession;
        private bool _destConnected;

        private EndPoint _destEndPoint;

        private IEncryptor _encryptor;
        private byte[] _firstPacket;
        private int _firstPacketLength;

        private bool _proxyConnected;

        // remote -> local proxy (ciphertext, before decrypt)
        private readonly byte[] _remoteRecvBuffer = new byte[BufferSize];

        // local proxy -> remote (plaintext, after decrypt)
        private readonly byte[] _remoteSendBuffer = new byte[BufferSize];
        private bool _remoteShutdown;
        private Server _server;

        private DateTime _startConnectTime;
        private readonly TCPRelay _tcprelay;

        public DateTime LastActivity;

        public TCPHandler(ShadowsocksController controller, Configuration config, TCPRelay tcprelay, Socket socket)
        {
            _controller = controller;
            _config = config;
            _tcprelay = tcprelay;
            _connection = socket;
            _proxyTimeout = config.Proxy.ProxyTimeout*1000;
            _serverTimeout = config.GetCurrentServer().Timeout*1000;

            LastActivity = DateTime.Now;
        }

        public void CreateRemote()
        {
            var server = _controller.GetAServer(IStrategyCallerType.TCP, (IPEndPoint) _connection.RemoteEndPoint,
                _destEndPoint);
            if ((server == null) || (server.ServerIp == ""))
                throw new ArgumentException("No ServerIp configured");

            _encryptor = EncryptorFactory.GetEncryptor(server.Method, server.Password);

            _server = server;

            /* prepare address buffer length for AEAD */
            Logging.Debug($"_addrBufLength={_addrBufLength}");
            _encryptor.AddrBufLength = _addrBufLength;
        }

        public void Start(byte[] firstPacket, int length)
        {
            _firstPacket = firstPacket;
            _firstPacketLength = length;
            HandshakeReceive();
        }

        private void CheckClose()
        {
            if (_connectionShutdown && _remoteShutdown)
                Close();
        }

        public void Close()
        {
            lock (_closeConnLock)
            {
                if (_closed) return;
                _closed = true;
            }
            lock (_tcprelay.Handlers)
            {
                _tcprelay.Handlers.Remove(this);
            }
            try
            {
                _connection.Shutdown(SocketShutdown.Both);
                _connection.Close();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }

            if (_currentRemoteSession != null)
                try
                {
                    var remote = _currentRemoteSession.Remote;
                    remote.Shutdown(SocketShutdown.Both);
                    remote.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }

            lock (_encryptionLock)
            {
                lock (_decryptionLock)
                {
                    _encryptor?.Dispose();
                }
            }
        }

        private void HandshakeReceive()
        {
            if (_closed) return;
            try
            {
                var bytesRead = _firstPacketLength;
                if (bytesRead > 1)
                {
                    byte[] response = {5, 0};
                    if (_firstPacket[0] != 5)
                    {
                        // reject socks 4
                        response = new byte[] {0, 91};
                        Logging.Error("socks 5 protocol error");
                    }
                    _connection.BeginSend(response, 0, response.Length, SocketFlags.None,
                        HandshakeSendCallback, null);
                }
                else
                    Close();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandshakeSendCallback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                _connection.EndSend(ar);

                // +-----+-----+-------+------+----------+----------+
                // | VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
                // +-----+-----+-------+------+----------+----------+
                // |  1  |  1  | X'00' |  1   | Variable |    2     |
                // +-----+-----+-------+------+----------+----------+
                // Skip first 3 bytes, and read 2 more bytes to analysis the address.
                // 2 more bytes is designed if address is domain then we don't need to read once more to get the addr length.
                // TODO validate
                _connection.BeginReceive(_connetionRecvBuffer, 0, 3 + EncryptorBase.ADDR_ATYP_LEN + 1, SocketFlags.None,
                    HandshakeReceive2Callback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandshakeReceive2Callback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var bytesRead = _connection.EndReceive(ar);
                if (bytesRead >= 5)
                {
                    _command = _connetionRecvBuffer[1];
                    if ((_command != CMD_CONNECT) && (_command != CMD_UDP_ASSOC))
                    {
                        Logging.Debug("Unsupported CMD=" + _command);
                        Close();
                    }
                    else
                    {
                        if (_command == CMD_CONNECT)
                        {
                            byte[] response = {5, 0, 0, 1, 0, 0, 0, 0, 0, 0};
                            _connection.BeginSend(response, 0, response.Length, SocketFlags.None,
                                ResponseCallback, null);
                        }
                        else if (_command == CMD_UDP_ASSOC)
                        {
                            ReadAddress(HandleUdpAssociate);
                        }
                    }
                }
                else
                {
                    Logging.Debug(
                        "failed to recv data in Shadowsocks.Controller.TCPHandler.handshakeReceive2Callback()");
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ResponseCallback(IAsyncResult ar)
        {
            try
            {
                _connection.EndSend(ar);

                ReadAddress(StartConnect);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ReadAddress(Action onSuccess)
        {
            int atyp = _connetionRecvBuffer[3];

            switch (atyp)
            {
                case EncryptorBase.ATYP_IPv4: // IPv4 address, 4 bytes
                    ReadAddress(4 + EncryptorBase.ADDR_PORT_LEN - 1, onSuccess);
                    break;
                case EncryptorBase.ATYP_DOMAIN: // domain name, length + str
                    int len = _connetionRecvBuffer[4];
                    ReadAddress(len + EncryptorBase.ADDR_PORT_LEN, onSuccess);
                    break;
                case EncryptorBase.ATYP_IPv6: // IPv6 address, 16 bytes
                    ReadAddress(16 + EncryptorBase.ADDR_PORT_LEN - 1, onSuccess);
                    break;
                default:
                    Logging.Debug("Unsupported ATYP=" + atyp);
                    Close();
                    break;
            }
        }

        private void ReadAddress(int bytesRemain, Action onSuccess)
        {
            // drop [ VER | CMD |  RSV  ]
            Array.Copy(_connetionRecvBuffer, 3, _connetionRecvBuffer, 0, EncryptorBase.ADDR_ATYP_LEN + 1);

            // Read the remain address bytes
            _connection.BeginReceive(_connetionRecvBuffer, 2, RecvSize - 2, SocketFlags.None, OnAddressFullyRead,
                new object[] {bytesRemain, onSuccess});
        }

        private void OnAddressFullyRead(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var bytesRead = _connection.EndReceive(ar);

                var states = (object[]) ar.AsyncState;

                var bytesRemain = (int) states[0];
                var onSuccess = (Action) states[1];

                if (bytesRead >= bytesRemain)
                {
                    _firstPacketLength = bytesRead + 2;

                    int atyp = _connetionRecvBuffer[0];

                    var dstAddr = "Unknown";
                    var dstPort = -1;
                    switch (atyp)
                    {
                        case EncryptorBase.ATYP_IPv4: // IPv4 address, 4 bytes
                            dstAddr = new IPAddress(_connetionRecvBuffer.Skip(1).Take(4).ToArray()).ToString();
                            dstPort = (_connetionRecvBuffer[5] << 8) + _connetionRecvBuffer[6];

                            _addrBufLength = EncryptorBase.ADDR_ATYP_LEN + 4 + EncryptorBase.ADDR_PORT_LEN;
                            break;
                        case EncryptorBase.ATYP_DOMAIN: // domain name, length + str
                            int len = _connetionRecvBuffer[1];
                            dstAddr = Encoding.UTF8.GetString(_connetionRecvBuffer, 2, len);
                            dstPort = (_connetionRecvBuffer[len + 2] << 8) + _connetionRecvBuffer[len + 3];

                            _addrBufLength = EncryptorBase.ADDR_ATYP_LEN + 1 + len + EncryptorBase.ADDR_PORT_LEN;
                            break;
                        case EncryptorBase.ATYP_IPv6: // IPv6 address, 16 bytes
                            dstAddr = $"[{new IPAddress(_connetionRecvBuffer.Skip(1).Take(16).ToArray())}]";
                            dstPort = (_connetionRecvBuffer[17] << 8) + _connetionRecvBuffer[18];

                            _addrBufLength = EncryptorBase.ADDR_ATYP_LEN + 16 + EncryptorBase.ADDR_PORT_LEN;
                            break;
                    }
                    Logging.Info($"connect to {dstAddr}:{dstPort}");


                    _destEndPoint = SocketUtil.GetEndPoint(dstAddr, dstPort);

                    onSuccess.Invoke(); /* StartConnect() */
                }
                else
                {
                    Logging.Debug("failed to recv data in Shadowsocks.Controller.TCPHandler.OnAddressFullyRead()");
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandleUdpAssociate()
        {
            var endPoint = (IPEndPoint) _connection.LocalEndPoint;
            var address = endPoint.Address.GetAddressBytes();
            var port = endPoint.Port;
            var response = new byte[4 + address.Length + EncryptorBase.ADDR_PORT_LEN];
            response[0] = 5;
            switch (endPoint.AddressFamily)
            {
                case AddressFamily.InterNetwork:
                    response[3] = EncryptorBase.ATYP_IPv4;
                    break;
                case AddressFamily.InterNetworkV6:
                    response[3] = EncryptorBase.ATYP_IPv6;
                    break;
            }
            address.CopyTo(response, 4);
            response[response.Length - 1] = (byte) (port & 0xFF);
            response[response.Length - 2] = (byte) ((port >> 8) & 0xFF);
            _connection.BeginSend(response, 0, response.Length, SocketFlags.None, ReadAll, true);
        }

        private void ReadAll(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                if (ar.AsyncState != null)
                {
                    _connection.EndSend(ar);
                    _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None,
                        ReadAll, null);
                }
                else
                {
                    var bytesRead = _connection.EndReceive(ar);
                    if (bytesRead > 0)
                        _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None,
                            ReadAll, null);
                    else
                        Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void StartConnect()
        {
            try
            {
                CreateRemote();

                // Setting up proxy
                IProxy remote;
                EndPoint proxyEp = null;
                if (_config.Proxy.Enabled)
                {
                    switch (_config.Proxy.ProxyType)
                    {
                        case ProxyType.Socks5:
                            remote = new Socks5Proxy();
                            break;
                        case ProxyType.Http:
                            remote = new HttpProxy();
                            break;
                        default:
                            throw new NotSupportedException("Unknown forward proxy.");
                    }
                    proxyEp = SocketUtil.GetEndPoint(_config.Proxy.ProxyServer, _config.Proxy.ProxyPort);
                }
                else
                {
                    remote = new DirectConnect();
                }

                var session = new AsyncSession(remote);
                lock (_closeConnLock)
                {
                    if (_closed)
                    {
                        remote.Close();
                        return;
                    }

                    _currentRemoteSession = session;
                }

                var proxyTimer = new ProxyTimer(_proxyTimeout) {AutoReset = false};
                proxyTimer.Elapsed += ProxyConnectTimer_Elapsed;
                proxyTimer.Enabled = true;

                proxyTimer.Session = session;
                proxyTimer.DestEndPoint = SocketUtil.GetEndPoint(_server.ServerIp, _server.ServerPort);
                proxyTimer.Server = _server;

                _proxyConnected = false;

                // Connect to the proxy ServerIp.
                //连接代理服务器
                remote.BeginConnectProxy(proxyEp, ProxyConnectCallback,
                    new AsyncSession<ProxyTimer>(remote, proxyTimer));
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ProxyConnectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var timer = (ProxyTimer) sender;
            timer.Elapsed -= ProxyConnectTimer_Elapsed;
            timer.Enabled = false;
            timer.Dispose();


            if (_proxyConnected || _destConnected || _closed)
                return;
            var proxy = timer.Session.Remote;

            Logging.Info($"Proxy {proxy.ProxyEndPoint} timed out");
            proxy.Close();
            Close();
        }

        private void ProxyConnectCallback(IAsyncResult ar)
        {
            if (_closed)
                return;
            try
            {
                var session = (AsyncSession<ProxyTimer>) ar.AsyncState;
                var timer = session.State;
                var destEndPoint = timer.DestEndPoint;
                var server = timer.Server;
                timer.Elapsed -= ProxyConnectTimer_Elapsed;
                timer.Enabled = false;
                timer.Dispose();

                var remote = session.Remote;

                // Complete the connection.
                remote.EndConnectProxy(ar);

                _proxyConnected = true;
                
                if (!(remote is DirectConnect))
                    Logging.Info($"Socket connected to proxy {remote.ProxyEndPoint}");

                _startConnectTime = DateTime.Now;
                var connectTimer = new ServerTimer(_serverTimeout) {AutoReset = false};
                connectTimer.Elapsed += DestConnectTimer_Elapsed;
                connectTimer.Enabled = true;
                connectTimer.Session = session;
                connectTimer.Server = server;

                _destConnected = false;
                // Connect to the remote endpoint.
                remote.BeginConnectDest(destEndPoint, ConnectCallback,
                    new AsyncSession<ServerTimer>(session, connectTimer));
            }
            catch (ArgumentException)
            {
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void DestConnectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var timer = (ServerTimer) sender;
            timer.Elapsed -= DestConnectTimer_Elapsed;
            timer.Enabled = false;
            timer.Dispose();

            if (_destConnected || _closed)
                return;

            var session = timer.Session;
            var server = timer.Server;
            var strategy = _controller.GetCurrentStrategy();
            strategy?.SetFailure(server);
            Logging.Info($"{server.FriendlyName()} timed out");
            session.Remote.Close();
            Close();
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var session = (AsyncSession<ServerTimer>) ar.AsyncState;
                var timer = session.State;
                _server = timer.Server;
                timer.Elapsed -= DestConnectTimer_Elapsed;
                timer.Enabled = false;
                timer.Dispose();

                var remote = session.Remote;
                // Complete the connection.
                remote.EndConnectDest(ar);

                _destConnected = true;
                
                Logging.Info($"Socket connected to ss ServerIp: {_server.FriendlyName()}");

                var latency = DateTime.Now - _startConnectTime;
                var strategy = _controller.GetCurrentStrategy();
                strategy?.UpdateLatency(_server, latency);

                StartPipe(session);
            }
            catch (ArgumentException)
            {
            }
            catch (Exception e)
            {
                if (_server != null)
                {
                    var strategy = _controller.GetCurrentStrategy();
                    strategy?.SetFailure(_server);
                }
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void TryReadAvailableData()
        {
            var available = Math.Min(_connection.Available, RecvSize - _firstPacketLength);
            if (available > 0)
            {
                var size = _connection.Receive(_connetionRecvBuffer, _firstPacketLength, available,
                    SocketFlags.None);

                _firstPacketLength += size;
            }
        }

        private void StartPipe(AsyncSession session)
        {
            if (_closed) return;
            try
            {
                session.Remote.BeginReceive(_remoteRecvBuffer, 0, RecvSize, SocketFlags.None,
                    PipeRemoteReceiveCallback, session);

                TryReadAvailableData();
                Logging.Debug($"_firstPacketLength = {_firstPacketLength}");
                SendToServer(_firstPacketLength, session);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeRemoteReceiveCallback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var session = (AsyncSession) ar.AsyncState;
                var bytesRead = session.Remote.EndReceive(ar);
                if (bytesRead > 0)
                {
                    LastActivity = DateTime.Now;
                    int bytesToSend;
                    lock (_decryptionLock)
                    {
                        try
                        {
                            _encryptor.Decrypt(_remoteRecvBuffer, bytesRead, _remoteSendBuffer, out bytesToSend);
                        }
                        catch (CryptoErrorException)
                        {
                            Logging.Error("decryption error");
                            Close();
                            return;
                        }
                    }
                    if (bytesToSend == 0)
                    {
                        // need more to decrypt
                        Logging.Debug("Need more to decrypt");
                        session.Remote.BeginReceive(_remoteRecvBuffer, 0, RecvSize, SocketFlags.None,
                            PipeRemoteReceiveCallback, session);
                        return;
                    }
                    Logging.Debug($"start sending {bytesToSend}");
                    _connection.BeginSend(_remoteSendBuffer, 0, bytesToSend, SocketFlags.None,
                        PipeConnectionSendCallback, new object[] {session, bytesToSend});
                    var strategy = _controller.GetCurrentStrategy();
                    strategy?.UpdateLastRead(_server);
                }
                else
                {
                    _connection.Shutdown(SocketShutdown.Send);
                    _connectionShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeConnectionReceiveCallback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var bytesRead = _connection.EndReceive(ar);

                var session = (AsyncSession) ar.AsyncState;
                var remote = session.Remote;

                if (bytesRead > 0)
                {
                    SendToServer(bytesRead, session);
                }
                else
                {
                    remote.Shutdown(SocketShutdown.Send);
                    _remoteShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void SendToServer(int length, AsyncSession session)
        {
            int bytesToSend;
            lock (_encryptionLock)
            {
                try
                {
                    _encryptor.Encrypt(_connetionRecvBuffer, length, _connetionSendBuffer, out bytesToSend);
                }
                catch (CryptoErrorException)
                {
                    Logging.Debug("encryption error");
                    Close();
                    return;
                }
            }
            session.Remote.BeginSend(_connetionSendBuffer, 0, bytesToSend, SocketFlags.None,
                PipeRemoteSendCallback, new object[] {session, bytesToSend});
            var strategy = _controller.GetCurrentStrategy();
            strategy?.UpdateLastWrite(_server);
        }

        private void PipeRemoteSendCallback(IAsyncResult ar)
        {
            if (_closed) return;
            try
            {
                var container = (object[]) ar.AsyncState;
                var session = (AsyncSession) container[0];
                var bytesShouldSend = (int) container[1];
                var bytesSent = session.Remote.EndSend(ar);
                var bytesRemaining = bytesShouldSend - bytesSent;
                if (bytesRemaining > 0)
                {
                    Logging.Info("reconstruct _connetionSendBuffer to re-send");
                    Buffer.BlockCopy(_connetionSendBuffer, bytesSent, _connetionSendBuffer, 0, bytesRemaining);
                    session.Remote.BeginSend(_connetionSendBuffer, 0, bytesRemaining, SocketFlags.None,
                        PipeRemoteSendCallback, new object[] {session, bytesRemaining});
                    return;
                }
                _connection.BeginReceive(_connetionRecvBuffer, 0, RecvSize, SocketFlags.None,
                    PipeConnectionReceiveCallback, session);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        // In general, we assume there is no delay between local proxy and client, add this for sanity
        private void PipeConnectionSendCallback(IAsyncResult ar)
        {
            try
            {
                var container = (object[]) ar.AsyncState;
                var session = (AsyncSession) container[0];
                var bytesShouldSend = (int) container[1];
                var bytesSent = _connection.EndSend(ar);
                var bytesRemaining = bytesShouldSend - bytesSent;
                if (bytesRemaining > 0)
                {
                    Logging.Info("reconstruct _remoteSendBuffer to re-send");
                    Buffer.BlockCopy(_remoteSendBuffer, bytesSent, _remoteSendBuffer, 0, bytesRemaining);
                    _connection.BeginSend(_remoteSendBuffer, 0, bytesRemaining, SocketFlags.None,
                        PipeConnectionSendCallback, new object[] {session, bytesRemaining});
                    return;
                }
                session.Remote.BeginReceive(_remoteRecvBuffer, 0, RecvSize, SocketFlags.None,
                    PipeRemoteReceiveCallback, session);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private class AsyncSession
        {
            public AsyncSession(IProxy remote)
            {
                Remote = remote;
            }

            public IProxy Remote { get; }
        }

        private class AsyncSession<T> : AsyncSession
        {
            public AsyncSession(IProxy remote, T state) : base(remote)
            {
                State = state;
            }

            public AsyncSession(AsyncSession session, T state) : base(session.Remote)
            {
                State = state;
            }

            public T State { get; }
        }

        // inner class
        private class ProxyTimer : Timer
        {
            public EndPoint DestEndPoint;
            public Server Server;
            public AsyncSession Session;

            public ProxyTimer(int p) : base(p)
            {
            }
        }

        private class ServerTimer : Timer
        {
            public Server Server;
            public AsyncSession Session;

            public ServerTimer(int p) : base(p)
            {
            }
        }
    }
}