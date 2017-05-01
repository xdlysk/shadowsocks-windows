using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ShadowSocksProxy.Model;

namespace ShadowSocksProxy.Controller.Service
{
    public class Listener
    {
        private readonly List<IService> _services;
        private Configuration _config;
        private bool _shareOverLan;
        private Socket _tcpSocket;
        private Socket _udpSocket;

        public Listener(List<IService> services)
        {
            _services = services;
        }

        private bool CheckIfPortInUse(int port)
        {
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var ipEndPoints = ipProperties.GetActiveTcpListeners();

            foreach (var endPoint in ipEndPoints)
                if (endPoint.Port == port)
                    return true;
            return false;
        }

        public void Start(Configuration config)
        {
            _config = config;
            _shareOverLan = config.ShareOverLan;

            if (CheckIfPortInUse(_config.LocalPort))
                throw new Exception("Port already in use");

            try
            {
                // Create a TCP/IP socket.
                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _tcpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                //若接受局域网连接，则绑定ipaddress.any否则绑定loopback
                var localEndPoint = _shareOverLan
                    ? new IPEndPoint(IPAddress.Any, _config.LocalPort)
                    : new IPEndPoint(IPAddress.Loopback, _config.LocalPort);

                // Bind the socket to the local endpoint and listen for incoming connections.
                _tcpSocket.Bind(localEndPoint);
                _udpSocket.Bind(localEndPoint);
                _tcpSocket.Listen(1024);

                // Start an asynchronous socket to listen for connections.
                Logging.Info("Shadowsocks started");
                _tcpSocket.BeginAccept(AcceptCallback, _tcpSocket);
                var udpState = new UDPState {socket = _udpSocket};
                _udpSocket.BeginReceiveFrom(udpState.buffer, 0, udpState.buffer.Length, 0, ref udpState.remoteEndPoint,
                    RecvFromCallback, udpState);
            }
            catch (SocketException)
            {
                _tcpSocket.Close();
                throw;
            }
        }

        public void Stop()
        {
            if (_tcpSocket != null)
            {
                _tcpSocket.Close();
                _tcpSocket = null;
            }
            if (_udpSocket != null)
            {
                _udpSocket.Close();
                _udpSocket = null;
            }

            _services.ForEach(s => s.Stop());
        }

        public void RecvFromCallback(IAsyncResult ar)
        {
            var state = (UDPState) ar.AsyncState;
            var socket = state.socket;
            try
            {
                var bytesRead = socket.EndReceiveFrom(ar, ref state.remoteEndPoint);
                foreach (var service in _services)
                    if (service.Handle(state.buffer, bytesRead, socket, state))
                        break;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Logging.Debug(ex);
            }
            finally
            {
                try
                {
                    socket.BeginReceiveFrom(state.buffer, 0, state.buffer.Length, 0, ref state.remoteEndPoint,
                        RecvFromCallback, state);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception)
                {
                }
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            var listener = (Socket) ar.AsyncState;
            try
            {
                var conn = listener.EndAccept(ar);

                var buf = new byte[4096];
                object[] state =
                {
                    conn,
                    buf
                };

                conn.BeginReceive(buf, 0, buf.Length, 0,
                    ReceiveCallback, state);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
            finally
            {
                try
                {
                    listener.BeginAccept(
                        AcceptCallback,
                        listener);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            var state = (object[]) ar.AsyncState;

            var conn = (Socket) state[0];
            var buf = (byte[]) state[1];
            try
            {
                var bytesRead = conn.EndReceive(ar);
                foreach (var service in _services)
                    if (service.Handle(buf, bytesRead, conn, null))
                        return;
                // no service found for this
                if (conn.ProtocolType == ProtocolType.Tcp)
                    conn.Close();
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                conn.Close();
            }
        }

        public interface IService
        {
            bool Handle(byte[] firstPacket, int length, Socket socket, object state);

            void Stop();
        }

        public abstract class Service : IService
        {
            public abstract bool Handle(byte[] firstPacket, int length, Socket socket, object state);

            public virtual void Stop()
            {
            }
        }

        public class UDPState
        {
            public byte[] buffer = new byte[4096];
            public EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            public Socket socket;
        }
    }
}