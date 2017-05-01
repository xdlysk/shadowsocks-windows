using System;
using System.Net;
using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    public class FixedStrategy : IStrategy
    {
        private readonly Server _server;
        public FixedStrategy(Server server)
        {
            _server = server;
        }
        public string Name { get; }
        public string ID { get; }
        public void ReloadServers()
        {
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint, EndPoint destEndPoint)
        {
            return _server;
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
        }

        public void UpdateLastRead(Server server)
        {
        }

        public void UpdateLastWrite(Server server)
        {
        }

        public void SetFailure(Server server)
        {
        }
    }
}
