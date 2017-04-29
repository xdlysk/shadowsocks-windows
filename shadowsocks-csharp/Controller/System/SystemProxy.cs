using System;
using Shadowsocks.Model;
using Shadowsocks.Util.SystemProxy;

namespace Shadowsocks.Controller
{
    public static class SystemProxy
    {
        private static string GetTimestamp(DateTime value)
        {
            return value.ToString("yyyyMMddHHmmssfff");
        }

        public static void Update(Configuration config, bool forceDisable)
        {
            bool global = config.global;
            bool enabled = config.enabled;

            if (forceDisable)
            {
                enabled = false;
            }

            try
            {
                if (enabled)
                {
                    Sysproxy.SetIEProxy(true, true, "127.0.0.1:" + config.localPort.ToString(), null);
                }
                else
                {
                    Sysproxy.SetIEProxy(false, false, null, null);
                }
            }
            catch (ProxyException ex)
            {
                Logging.LogUsefulException(ex);
            }
        }
    }
}