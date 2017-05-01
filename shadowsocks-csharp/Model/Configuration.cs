using System;
using System.Collections.Generic;
using Shadowsocks.Controller.Strategy;

namespace Shadowsocks.Model
{
    [Serializable]
    public class Configuration
    {
        public bool EnableHttp { get; set; }
        public List<Server> RemoteServers { get; set; }
        /// <summary>
        /// 本地server端口
        /// </summary>
        public int LocalPort { get; set; }

        /// <summary>
        /// 代理配置
        /// </summary>
        public ProxyConfig Proxy { get; set; }

        /// <summary>
        /// 是否允许从局域网连接
        /// </summary>
        public bool ShareOverLan { get; set; }

        // when strategy is set, index is ignored
        public IStrategy Strategy { get; set; }

        public Server GetCurrentServer()
        {
            return new Server();
        }

        public static void CheckServer(Server server)
        {
            CheckPort(server.ServerPort);
            CheckPassword(server.Password);
            CheckServer(server.ServerIp);
            CheckTimeout(server.Timeout, Server.MaxServerTimeoutSec);
        }


        public static void CheckPort(int port)
        {
            if ((port <= 0) || (port > 65535))
                throw new ArgumentException("Port out of range");
        }

        public static void CheckLocalPort(int port)
        {
            CheckPort(port);
            if (port == 8123)
                throw new ArgumentException("Port can't be 8123");
        }

        private static void CheckPassword(string password)
        {
            if (password.IsNullOrEmpty())
                throw new ArgumentException("Password can not be blank");
        }

        public static void CheckServer(string server)
        {
            if (server.IsNullOrEmpty())
                throw new ArgumentException("Server IP can not be blank");
        }

        public static void CheckTimeout(int timeout, int maxTimeout)
        {
            if ((timeout <= 0) || (timeout > maxTimeout))
                throw new ArgumentException($"Timeout is invalid, it should not exceed {maxTimeout}");
        }
    }
}