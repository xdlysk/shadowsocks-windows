using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Shadowsocks.Proxy;

namespace ClientClientTester
{
    public partial class Form1 : Form
    {
        private Socks5Proxy s5;
        public Form1()
        {
            s5 = new Socks5Proxy();
            var socks5Endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1080);
            var destEndpoint = new IPEndPoint(IPAddress.Parse("47.93.52.50"), 12345);
            s5.BeginConnectProxy(socks5Endpoint, ar =>
            {
                s5.BeginConnectDest(destEndpoint, ar1 =>
                {

                }, null);
            }, null);
            InitializeComponent();
        }


        private void button1_Click(object sender, EventArgs e)
        {
            
            s5.BeginSend(new byte[] { 1, 2, 3 }, 0, 3, SocketFlags.None, ar2 =>
            {
                //s5.BeginReceive();
            }, null);

            
            
        }
    }
}
