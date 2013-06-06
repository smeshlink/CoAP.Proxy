using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SuperSocket.Common;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Command;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketEngine;
using SuperSocket.SocketEngine.Configuration;
using SuperWebSocket;
using CoAP;

namespace Coap.Proxy
{
    class WebSocketService
    {
        private List<WebSocketSession> m_Sessions = new List<WebSocketSession>();
        private object m_SessionSyncRoot = new object();
        private IBootstrap m_Bootstrap;
        private int port=0;

        public List<WebSocketSession> SessionList
        {
            get { return m_Sessions; }
        }

        public WebSocketService(int port)
        {
            this.port = port;
            //this.listener = new TcpListener(IPAddress.IPv6Any, port);
        }

        void socketServer_NewMessageReceived(WebSocketSession session, string e)
        {
            SendToAll(session.Cookies["name"] + ": " + e);
        }


        public void StartSuperWebSocketByProgramming()
        {
            var socketServer = new WebSocketServer();

            socketServer.NewMessageReceived += new SessionHandler<WebSocketSession, string>(socketServer_NewMessageReceived);
            socketServer.NewSessionConnected += socketServer_NewSessionConnected;
            socketServer.SessionClosed += socketServer_SessionClosed;

            socketServer.Setup(port);

            m_Bootstrap = new DefaultBootstrap(new RootConfig(), new IWorkItem[] { socketServer });

            m_Bootstrap.Start();
        }

        void socketServer_NewSessionConnected(WebSocketSession session)
        {
            lock (m_SessionSyncRoot)
                m_Sessions.Add(session);

            SendToObserved("System: observe connected",session);
        }

        public void socketServer_SessionClosed(WebSocketSession session, CloseReason reason)
        {
            lock (m_SessionSyncRoot)
                m_Sessions.Remove(session);

            if (reason == CloseReason.ServerShutdown)
                return;
            Console.WriteLine(session.ToString()+"connect disposed");
            //SendToAll("System: " + session.Cookies["name"] + " disconnected");
        }

        public void SendToAll(string message)
        {
            lock (m_SessionSyncRoot)
            {
                foreach (var s in m_Sessions)
                {
                    s.Send(message);
                }
            }
        }

        public void SendToObserved(string message,WebSocketSession s)
        {
            s.Send(message);
        }

        void Application_End(object sender, EventArgs e)
        {

            if (m_Bootstrap != null)
                m_Bootstrap.Stop();
        }

        void Application_Error(object sender, EventArgs e)
        {
            // Code that runs when an unhandled error occurs

        }

        void Session_Start(object sender, EventArgs e)
        {
            // Code that runs when a new session is started

        }

        public void Session_End()
        {
            // Code that runs when a session ends. 
            // Note: The Session_End event is raised only when the sessionstate mode
            // is set to InProc in the Web.config file. If session mode is set to StateServer 
            // or SQLServer, the event is not raised.
            lock (m_SessionSyncRoot)
                m_Sessions.Clear();
        }
        
    }
}
