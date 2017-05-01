using System;
using Shadowsocks.Model;
using Shadowsocks.Util.SystemProxy;

namespace Shadowsocks.Controller.System
{
    public static class SystemProxy
    {
        private static string GetTimestamp(DateTime value)
        {
            return value.ToString("yyyyMMddHHmmssfff");
        }

        public static void Update(Configuration config, bool forceDisable)
        {
            var global = config.global;
            var enabled = config.enabled;

            if (forceDisable)
                enabled = false;

            try
            {
                if (enabled)
                    Sysproxy.SetIEProxy(true, true, "127.0.0.1:" + config.localPort, null);
                else
                    Sysproxy.SetIEProxy(false, false, null, null);
            }
            catch (ProxyException ex)
            {
                Logging.LogUsefulException(ex);
            }
        }
    }
}