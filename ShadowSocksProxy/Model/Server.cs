using System;

namespace ShadowSocksProxy.Model
{
    [Serializable]
    public class Server
    {
        private const int DefaultServerTimeoutSec = 5;
        public const int MaxServerTimeoutSec = 20;

        public string Method { get; set; }
        public string Password { get; set; }

        public string ServerIp { get; set; }
        public int ServerPort { get; set; }
        public int Timeout { get; set; }

        public Server()
        {
            ServerIp = "";
            ServerPort = 8388;
            Method = "aes-256-cfb";
            Password = "";
            Timeout = DefaultServerTimeoutSec;
        }

        public string FriendlyName()
        {
            if (ServerIp.IsNullOrEmpty())
                return "New ServerIp";
            string serverStr;
            // CheckHostName() won't do a real DNS lookup
            var hostType = Uri.CheckHostName(ServerIp);

            switch (hostType)
            {
                case UriHostNameType.IPv6:
                    serverStr = $"[{ServerIp}]:{ServerPort}";
                    break;
                default:
                    // IPv4 and domain name
                    serverStr = $"{ServerIp}:{ServerPort}";
                    break;
            }
            return serverStr;
        }

        public string Identifier()
        {
            return ServerIp + ':' + ServerPort;
        }
    }
}