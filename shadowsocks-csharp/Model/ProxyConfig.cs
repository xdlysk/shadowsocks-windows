using System;

namespace Shadowsocks.Model
{
    [Serializable]
    public class ProxyConfig
    {
        public const int PROXY_SOCKS5 = 0;
        public const int PROXY_HTTP = 1;

        public const int MaxProxyTimeoutSec = 10;
        private const int DefaultProxyTimeoutSec = 3;
        public int proxyPort;
        public string proxyServer;
        public int proxyTimeout;
        public int proxyType;

        public bool useProxy;

        public ProxyConfig()
        {
            useProxy = true;
            proxyType = PROXY_SOCKS5;
            proxyServer = "";
            proxyPort = 0;
            proxyTimeout = DefaultProxyTimeoutSec;
        }

        public void CheckConfig()
        {
            if ((proxyType < PROXY_SOCKS5) || (proxyType > PROXY_HTTP))
                proxyType = PROXY_SOCKS5;
        }
    }
}