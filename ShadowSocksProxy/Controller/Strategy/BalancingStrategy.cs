using System;
using System.Net;
using ShadowSocksProxy.Model;

namespace ShadowSocksProxy.Controller.Strategy
{
    internal class BalancingStrategy : IStrategy
    {
        private readonly ShadowsocksController _controller;
        private readonly Random _random;

        public BalancingStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            _random = new Random();
        }

        public string Name => "Load Balance";

        public string ID => "com.shadowsocks.strategy.balancing";

        public void ReloadServers()
        {
            // do nothing
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint, EndPoint destEndPoint)
        {
            var configs = _controller.GetCurrentConfiguration().RemoteServers;
            var index = type == IStrategyCallerType.TCP ? _random.Next() : localIPEndPoint.GetHashCode();
            return configs[index%configs.Count];
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            // do nothing
        }

        public void UpdateLastRead(Server server)
        {
            // do nothing
        }

        public void UpdateLastWrite(Server server)
        {
            // do nothing
        }

        public void SetFailure(Server server)
        {
            // do nothing
        }
    }
}