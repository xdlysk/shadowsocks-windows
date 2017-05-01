using System;

namespace Shadowsocks.Model
{
    [Serializable]
    public class ProxyConfig
    {
        
        private const int DefaultProxyTimeoutSec = 3;
        public int ProxyPort { get; set; }
        public string ProxyServer { get; set; }
        /// <summary>
        /// sec
        /// </summary>
        public int ProxyTimeout { get; set; }
        public ProxyType ProxyType { get; set; }

        public bool Enabled { get; set; }

        public ProxyConfig()
        {
            Enabled = false;
            ProxyType = ProxyType.None;
            ProxyServer = "";
            ProxyPort = 0;
            ProxyTimeout = DefaultProxyTimeoutSec;
        }
    }

    public enum ProxyType
    {
        None=0,
        Socks5,
        Http
    }
}