using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Shadowsocks.Proxy;

namespace ClientClientTester
{
    public class IProxyWrapper
    {
        private readonly IProxy _proxy;
        public IProxyWrapper(IProxy proxy,EndPoint proxyEndPoint,EndPoint destEndPoint)
        {
            _proxy = proxy;
        }

        
    }
}
