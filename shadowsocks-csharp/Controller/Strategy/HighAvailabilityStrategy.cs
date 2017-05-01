﻿using System;
using System.Collections.Generic;
using System.Net;
using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    internal class HighAvailabilityStrategy : IStrategy
    {
        private readonly ShadowsocksController _controller;
        protected ServerStatus _currentServer;
        protected Dictionary<Server, ServerStatus> _serverStatus;

        public HighAvailabilityStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            _serverStatus = new Dictionary<Server, ServerStatus>();
        }

        public string Name => "High Availability";

        public string ID => "com.shadowsocks.strategy.ha";

        public void ReloadServers()
        {
            // make a copy to avoid locking
            var newServerStatus = new Dictionary<Server, ServerStatus>(_serverStatus);

            foreach (var server in _controller.GetCurrentConfiguration().RemoteServers)
                if (!newServerStatus.ContainsKey(server))
                {
                    var status = new ServerStatus
                    {
                        server = server,
                        lastFailure = DateTime.MinValue,
                        lastRead = DateTime.Now,
                        lastWrite = DateTime.Now,
                        latency = new TimeSpan(0, 0, 0, 0, 10),
                        lastTimeDetectLatency = DateTime.Now
                    };
                    newServerStatus[server] = status;
                }
                else
                {
                    // update settings for existing ServerIp
                    newServerStatus[server].server = server;
                }
            _serverStatus = newServerStatus;

            ChooseNewServer();
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint, EndPoint destEndPoint)
        {
            if (type == IStrategyCallerType.TCP)
                ChooseNewServer();
            return _currentServer?.server;
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            Logging.Debug($"latency: {server.FriendlyName()} {latency}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
            {
                status.latency = latency;
                status.lastTimeDetectLatency = DateTime.Now;
            }
        }

        public void UpdateLastRead(Server server)
        {
            Logging.Debug($"last read: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
                status.lastRead = DateTime.Now;
        }

        public void UpdateLastWrite(Server server)
        {
            Logging.Debug($"last write: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
                status.lastWrite = DateTime.Now;
        }

        public void SetFailure(Server server)
        {
            Logging.Debug($"failure: {server.FriendlyName()}");

            ServerStatus status;
            if (_serverStatus.TryGetValue(server, out status))
                status.lastFailure = DateTime.Now;
        }

        /**
         * once failed, try after 5 min
         * and (last write - last read) < 5s
         * and (now - last read) <  5s  // means not stuck
         * and latency < 200ms, try after 30s
         */

        public void ChooseNewServer()
        {
            var servers = new List<ServerStatus>(_serverStatus.Values);
            var now = DateTime.Now;
            foreach (var status in servers)
            {
                // all of failure, latency, (lastread - lastwrite) normalized to 1000, then
                // 100 * failure - 2 * latency - 0.5 * (lastread - lastwrite)
                status.score =
                    100*1000*Math.Min(5*60, (now - status.lastFailure).TotalSeconds)
                    -
                    2*5*
                    (Math.Min(2000, status.latency.TotalMilliseconds)/
                     (1 + (now - status.lastTimeDetectLatency).TotalSeconds/30/10) +
                     -0.5*200*Math.Min(5, (status.lastRead - status.lastWrite).TotalSeconds));
                Logging.Debug(string.Format("ServerIp: {0} latency:{1} score: {2}", status.server.FriendlyName(),
                    status.latency, status.score));
            }
            ServerStatus max = null;
            foreach (var status in servers)
                if (max == null)
                {
                    max = status;
                }
                else
                {
                    if (status.score >= max.score)
                        max = status;
                }
            if (max != null)
                if ((_currentServer == null) || (max.score - _currentServer.score > 200))
                {
                    _currentServer = max;
                    Logging.Info($"HA switching to ServerIp: {_currentServer.server.FriendlyName()}");
                }
        }

        public class ServerStatus
        {
            // connection refused or closed before anything received
            public DateTime lastFailure;

            // last time anything received
            public DateTime lastRead;
            public DateTime lastTimeDetectLatency;

            // last time anything sent
            public DateTime lastWrite;
            // time interval between SYN and SYN+ACK
            public TimeSpan latency;

            public double score;

            public Server server;
        }
    }
}