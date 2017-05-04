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
using ShadowSocksProxy.Controller;
using ShadowSocksProxy.Controller.Strategy;
using ShadowSocksProxy.Model;
using ShadowSocksProxy.Proxy;

namespace ClientClientTester
{
    public partial class Form1 : Form
    {
        private Socks5Proxy s5;
        private ShadowsocksController mc;
        public Form1()
        {
            mc = new ShadowsocksController(new Configuration
            {
                EnableHttp = true,
                LocalPort = 1080,
                ShareOverLan = true,
                Strategy = new FixedStrategy(new Server
                {
                    Method = "aes-256-cfb",
                    Password = "jeqee",
                    ServerIp = "60.169.115.17",
                    ServerPort = 15420,
                    Timeout = 600
                })
            });
            mc.Start();
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
            var bys = Encoding.UTF8.GetBytes(textBox1.Text);
            s5.BeginSend(bys, 0, bys.Length, SocketFlags.None, ar2 =>
            {
                //s5.BeginReceive();
            }, null);

            
            
        }
    }
}
