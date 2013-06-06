using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CoAP;

namespace Coap.Proxy
{
    class ServerListerner
    {
        private int listenPort;
        private string coapPortName;
        private TcpListener listener;
        private WebSocketService coapWebSocektService;        
        /// <summary>
        /// proxy port
        /// </summary>
        /// <param name="port">proxy port</param>
        public ServerListerner(int port)
        {
            this.listenPort = port;
            this.listener = new TcpListener(IPAddress.Any, this.listenPort);
            //this.listener = new TcpListener(IPAddress.IPv6Any, port);
        }
        /// <summary>
        /// open proxy
        /// </summary>
        /// <param name="port">webSocket port</param>
        /// <param name="portName">ipv6 gateway port</param>
        public void StartServer(int port,string portName)
        {
            coapWebSocektService = new WebSocketService(port);
            coapPortName = portName;
            this.listener.Start();
            this.coapWebSocektService.StartSuperWebSocketByProgramming();
        }
        // new connection
        public void AcceptConnection()
        {
            Socket newClient = this.listener.AcceptSocket();
            ClientConnection client = new ClientConnection(newClient, coapWebSocektService, coapPortName);
            client.StartHandling();
        }


    }
}
