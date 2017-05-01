using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Shadowsocks.Controller.Service;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public class ShadowsocksController
    {
        private readonly Configuration _config;

        private Listener _listener;
        // controller:
        // handle user actions
        // manipulates UI
        // interacts with low level logic

        private Thread _ramThread;
        private readonly PrivoxyRunner _privoxyRunner;
        private readonly List<Listener.IService> _services;
        private bool _stopped;

        public ShadowsocksController(Configuration config)
        {
            _config = config;

            RNG.Reload();
            var strategy = GetCurrentStrategy();
            strategy?.ReloadServers();
            var tcpRelay = new TCPRelay(this, _config);
            var udpRelay = new UDPRelay(this);
            _services = new List<Listener.IService>
            {
                tcpRelay,
                udpRelay
            };
            //启用http协议，则启动privoxy进程
            if (_config.EnableHttp)
            {
                _privoxyRunner = new PrivoxyRunner();
                _privoxyRunner.Start(_config);
                _services.Add(new PortForwarder(_privoxyRunner.RunningPort));
            }
            
            //StartReleasingMemory();
        }

        public void Start()
        {
            try
            {
                _listener = new Listener(_services);
                _listener.Start(_config);
            }
            catch (SocketException se)
            {
                // translate Microsoft language into human language
                // i.e. An attempt was made to access a socket in a way forbidden by its access permissions => Port already in use
                if (se.SocketErrorCode == SocketError.AccessDenied)
                {
                    Logging.LogUsefulException(se);
                }
                throw;
            }

            Utils.ReleaseMemory(true);
        }

        public Server GetCurrentServer()
        {
            return _config.GetCurrentServer();
        }

        // always return current instance
        public Configuration GetCurrentConfiguration()
        {
            return _config;
        }

        public IStrategy GetCurrentStrategy()
        {
            return _config.Strategy;
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIpEndPoint, EndPoint destEndPoint)
        {
            var strategy = GetCurrentStrategy();
            if (strategy != null)
                return strategy.GetAServer(type, localIpEndPoint, destEndPoint);
            return GetCurrentServer();
        }

        public void Stop()
        {
            if (_stopped)
                return;
            _stopped = true;
            _listener?.Stop();
            _privoxyRunner?.Stop();
            RNG.Close();
        }


        #region Memory Management

        private void StartReleasingMemory()
        {
            _ramThread = new Thread(ReleaseMemory) {IsBackground = true};
            _ramThread.Start();
        }

        private void ReleaseMemory()
        {
            while (true)
            {
                Utils.ReleaseMemory(false);
                Thread.Sleep(30*1000);
            }
        }

        #endregion
    }
}