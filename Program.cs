using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CoAP;
using System.Configuration;

namespace Coap.Proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            ServerListerner coapProxyServer = new ServerListerner(Int32.Parse(GetAppConfig("listenPort")));
            coapProxyServer.StartServer(Int32.Parse(GetAppConfig("webSocetPort")),GetAppConfig("portName"));
            while (true)
            {
                coapProxyServer.AcceptConnection();
            }

        }

        private static string GetAppConfig(string strKey)
        {
            foreach (string key in ConfigurationManager.AppSettings)
            {
                if (key == strKey)
                {
                    return ConfigurationManager.AppSettings[strKey];
                }
            }
            return null;
        }
    }

    
}
