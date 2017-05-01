using System;
using System.Net;
using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    public enum IStrategyCallerType
    {
        TCP,
        UDP
    }

    /*
     * IStrategy
     *
     * Subclasses must be thread-safe
     */

    public interface IStrategy
    {
        string Name { get; }

        string ID { get; }

        /*
         * Called when servers need to be reloaded, i.e. new configuration saved
         */
        void ReloadServers();

        /*
         * Get a new ServerIp to use in TCPRelay or UDPRelay
         */
        Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint, EndPoint destEndPoint);

        /*
         * TCPRelay will call this when latency of a ServerIp detected
         */
        void UpdateLatency(Server server, TimeSpan latency);

        /*
         * TCPRelay will call this when reading from a ServerIp
         */
        void UpdateLastRead(Server server);

        /*
         * TCPRelay will call this when writing to a ServerIp
         */
        void UpdateLastWrite(Server server);

        /*
         * TCPRelay will call this when fatal failure detected
         */
        void SetFailure(Server server);
    }
}