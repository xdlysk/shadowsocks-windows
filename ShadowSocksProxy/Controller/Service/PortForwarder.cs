using System;
using System.Net.Sockets;
using ShadowSocksProxy.Util.Sockets;

namespace ShadowSocksProxy.Controller.Service
{
    /// <summary>
    /// 端口转发，这里将http请求转发到ssprivoxy，由此处理
    /// </summary>
    internal class PortForwarder : Listener.Service
    {
        private readonly int _targetPort;

        public PortForwarder(int targetPort)
        {
            _targetPort = targetPort;
        }

        public override bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            //http[s]协议基于tcp协议
            if (socket.ProtocolType != ProtocolType.Tcp)
                return false;
            new Handler().Start(firstPacket, length, socket, _targetPort);
            return true;
        }

        private class Handler
        {
            private const int RecvSize = 2048;

            // instance-based lock
            private readonly object _lock = new object();
            private bool _closed;
            private byte[] _firstPacket;
            private int _firstPacketLength;
            private Socket _local;
            private bool _localShutdown;
            private WrappedSocket _remote;
            private bool _remoteShutdown;
            // connection receive buffer
            private readonly byte[] _connetionRecvBuffer = new byte[RecvSize];
            // remote receive buffer
            private readonly byte[] _remoteRecvBuffer = new byte[RecvSize];

            /// <summary>
            /// 将数据转发到Privoxy
            /// </summary>
            /// <param name="firstPacket"></param>
            /// <param name="length"></param>
            /// <param name="socket"></param>
            /// <param name="targetPort">Privoxy的监听端口</param>
            public void Start(byte[] firstPacket, int length, Socket socket, int targetPort)
            {
                _firstPacket = firstPacket;
                _firstPacketLength = length;
                _local = socket;
                try
                {
                    var remoteEp = SocketUtil.GetEndPoint("127.0.0.1", targetPort);

                    // Connect to the remote endpoint.
                    _remote = new WrappedSocket();
                    _remote.BeginConnect(remoteEp, ConnectCallback, null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void ConnectCallback(IAsyncResult ar)
            {
                if (_closed)
                    return;
                try
                {
                    _remote.EndConnect(ar);
                    _remote.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                    HandshakeReceive();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void HandshakeReceive()
            {
                if (_closed)
                    return;
                try
                {
                    _remote.BeginSend(_firstPacket, 0, _firstPacketLength, 0, StartPipe, null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void StartPipe(IAsyncResult ar)
            {
                if (_closed)
                    return;
                try
                {
                    _remote.EndSend(ar);
                    _remote.BeginReceive(_remoteRecvBuffer, 0, RecvSize, 0,
                        PipeRemoteReceiveCallback, null);
                    _local.BeginReceive(_connetionRecvBuffer, 0, RecvSize, 0,
                        PipeConnectionReceiveCallback, null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void PipeRemoteReceiveCallback(IAsyncResult ar)
            {
                if (_closed)
                    return;
                try
                {
                    var bytesRead = _remote.EndReceive(ar);
                    if (bytesRead > 0)
                    {
                        _local.BeginSend(_remoteRecvBuffer, 0, bytesRead, 0, PipeConnectionSendCallback, null);
                    }
                    else
                    {
                        _local.Shutdown(SocketShutdown.Send);
                        _localShutdown = true;
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
                if (_closed)
                    return;
                try
                {
                    var bytesRead = _local.EndReceive(ar);
                    if (bytesRead > 0)
                    {
                        _remote.BeginSend(_connetionRecvBuffer, 0, bytesRead, 0, PipeRemoteSendCallback, null);
                    }
                    else
                    {
                        _remote.Shutdown(SocketShutdown.Send);
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

            private void PipeRemoteSendCallback(IAsyncResult ar)
            {
                if (_closed)
                    return;
                try
                {
                    _remote.EndSend(ar);
                    _local.BeginReceive(_connetionRecvBuffer, 0, RecvSize, 0,
                        PipeConnectionReceiveCallback, null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void PipeConnectionSendCallback(IAsyncResult ar)
            {
                if (_closed)
                    return;
                try
                {
                    _local.EndSend(ar);
                    _remote.BeginReceive(_remoteRecvBuffer, 0, RecvSize, 0,
                        PipeRemoteReceiveCallback, null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    Close();
                }
            }

            private void CheckClose()
            {
                if (_localShutdown && _remoteShutdown)
                    Close();
            }

            private void Close()
            {
                lock (_lock)
                {
                    if (_closed)
                        return;
                    _closed = true;
                }
                if (_local != null)
                    try
                    {
                        _local.Shutdown(SocketShutdown.Both);
                        _local.Close();
                    }
                    catch (Exception e)
                    {
                        Logging.LogUsefulException(e);
                    }
                if (_remote != null)
                    try
                    {
                        _remote.Shutdown(SocketShutdown.Both);
                        _remote.Dispose();
                    }
                    catch (SocketException e)
                    {
                        Logging.LogUsefulException(e);
                    }
            }
        }
    }
}