using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using CoAP;
using System.Net;
using System.IO;
using CoAP.EndPoint;
using SuperWebSocket;
using System.Collections;


namespace Coap.Proxy
{
    class ClientConnection
    {
        private Socket clientSocket;
        private Request _currentRequest;
        private Request _observingRequest;
        private readonly String _tempImagePath = Path.GetTempFileName();
        private WebSocketService coapWebSocektService;
        private WebSocketSession observedSession=null;
        private string portName;
        private string _payload="";

        public String Payload
        {
            set { _payload = value; }
            get { return _payload; }
        }

        public ClientConnection(Socket client,WebSocketService wss,string port)
        {
            this.clientSocket = client;
            coapWebSocektService = wss;
            portName = port;
        }

        public void StartHandling()
        {
            Thread handler = new Thread(Handler);
            handler.Priority = ThreadPriority.AboveNormal;
            handler.Start();
        }
        //proxy Segregation
        private void Handler()
        {
            bool recvRequest = true;
            string EOL = "\r\n";
            string requestPayload = "";
            string requestTempLine = "";
            List<string> requestLines = new List<string>();
            byte[] requestBuffer = new byte[1];
            byte[] responseBuffer = new byte[1];

            requestLines.Clear();

            try
            {
                //State 0: Handle Request from Client
                while (recvRequest)
                {
                    this.clientSocket.Receive(requestBuffer);
                    string fromByte = ASCIIEncoding.ASCII.GetString(requestBuffer);
                    requestPayload += fromByte;
                    requestTempLine += fromByte;

                    if (requestTempLine.EndsWith(EOL))
                    {
                        requestLines.Add(requestTempLine.Trim());
                        requestTempLine = "";
                    }

                    if (requestPayload.EndsWith(EOL + EOL))
                    {
                        recvRequest = false;
                    }
                }
                //received http request
                Console.WriteLine("Raw Request Received...");
                Console.WriteLine(requestPayload);

                //State 1:Segregation
                //Nomal coap request
                if (requestLines[0].Contains(portName) & !requestLines[0].Contains("?observehost"))
                {
                    
                    //get the URL
                    string remoteHost = requestLines[0].Split(' ')[1].Replace("http://", "coap://").Split(' ')[0];
                    Uri uri = new Uri(remoteHost);

                    //creat coap request
                    Request.Method method;
                    string requestMethod = requestLines[0].Substring(0, requestLines[0].IndexOf(' ')).ToLower();
                    if (requestMethod.Trim() == "get")
                        method = Request.Method.GET;
                    else if (requestMethod.Trim() == "post")
                    {
                        method = Request.Method.POST;
                        requestPayload += requestLines[requestLines.Count-1];
                    }
                    else if (requestMethod.Trim() == "put")
                    {
                        method = Request.Method.PUT;
                        requestPayload += requestLines[requestLines.Count - 1];
                    }
                    else if (requestMethod.Trim() == "delete")
                        method = Request.Method.DELETE;
                    else
                        method = Request.Method.GET;

                    //get the payLoad
                    Payload = requestPayload;

                    //Resource discovery request
                    if (remoteHost.Contains("index"))
                    {
                        remoteHost = remoteHost.Replace("index", ".well-known/core");
                        Uri discoverUri = new Uri(remoteHost);
                        Request request = PerformCoAP(Request.Method.GET, discoverUri, false, false);
                        Action<Request> discoverHandler = new Action<Request>(HandleDiscover);
                        discoverHandler.BeginInvoke(request, null, null);
                    }
                    //nomal CoAP request
                    else
                    {
                        Request requestOnce = PerformCoAP(method, uri, true, false);
                    }
                }
                //observe request
                else if (requestLines[0].Contains(portName) & requestLines[0].Contains("?observehost"))
                {
                    //get URL
                    string remoteHost = requestLines[0].Split(' ')[1].Replace("http://", "coap://").Split(' ')[0];
                    remoteHost = remoteHost.Replace("?observehost", "");
                    Uri discoverUri = new Uri(remoteHost);

                    PerformObserve(Request.Method.GET, discoverUri);
                }
                //Http Conncet request
                else if (requestLines[0].ToLower().Contains("connect"))
                {
                    string remoteHost = requestLines[0].Split(' ')[1].Replace("http://", "").Split('/')[0];
                    int port = Int32.Parse(remoteHost.Split(':')[1]);
                    remoteHost = remoteHost.Split(':')[0];
                    Socket destServerSocket = null;
                    //Conncet to real Server
                    try
                    {
                        destServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        destServerSocket.Connect(remoteHost, port);
                    }

                    catch (Exception e)
                    {
                        Console.WriteLine("Bad connect to real Server: " + e.Message);
                    }

                    // Conncet to real Server success
                    if (destServerSocket.Connected)
                    {
                        Console.WriteLine("Websocket connect success ");
                        //exchange message
                        ForwardTcpData(clientSocket, destServerSocket);
                    }
                    else
                    {
                        clientSocket.Shutdown(SocketShutdown.Both);
                        clientSocket.Close();
                    }
                }
                //nomal Http request
                else
                {
                    //State 1: Rebuilding Request Information and Create Connection to Destination Server

                    string remoteHost = requestLines[0].Split(' ')[1].Replace("http://", "").Split('/')[0];
                    string requestFile = requestLines[0].Replace("http://", "").Replace(remoteHost, "");
                    requestLines[0] = requestFile;

                    requestPayload = "";
                    foreach (string line in requestLines)
                    {
                        requestPayload += line;
                        requestPayload += EOL;
                    }

                    Socket destServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    destServerSocket.Connect(remoteHost, 80);

                    //State 2: Sending New Request Information to Destination Server and Relay Response to Client            
                    destServerSocket.Send(ASCIIEncoding.ASCII.GetBytes(requestPayload));

                    //Console.WriteLine("Begin Receiving Response...");
                    while (destServerSocket.Receive(responseBuffer) != 0)
                    {
                        //Console.Write(ASCIIEncoding.ASCII.GetString(responseBuffer));
                        this.clientSocket.Send(responseBuffer);
                    }

                    destServerSocket.Disconnect(false);
                    destServerSocket.Dispose();

                    this.clientSocket.Disconnect(false);
                    this.clientSocket.Dispose();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("Error Occured: " + e.Message);
                //Console.WriteLine(e.StackTrace);
            }
        }
         /// <summary>
         /// Exchange Connect message from client and server
         /// </summary>
         /// <param name="client">client socket</param> 
         /// <param name="server">server socket</param>
        private void ForwardTcpData(Socket client, Socket server)
            {
            ArrayList ReadList = new ArrayList(2);
            client.Send(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"));
            while (true)
            {
            ReadList.Clear();
            ReadList.Add(client);
            ReadList.Add(server);
            try
            {
                //Console.WriteLine("test socket");
                Socket.Select(ReadList, null, null, 1 * 1000 * 1000);
            } 
            catch (SocketException e)
            { 
                Console.WriteLine("Select error: " + e.Message);
                break;
            } 
            // time out
            if (ReadList.Count == 0)
            {
                //Console.WriteLine("Time out");
                if (client.Connected && server.Connected)
                    continue;
                else
                    break;
            }

            // client send message 
            if (ReadList.Contains(client))
            {
                byte[] Recv = new byte[1024];
            int Length = 0;

            try
            {
                Length = client.Receive(Recv, Recv.Length,0); 
                 if (Length == 0)
                {
                Console.WriteLine("Client is disconnect.");
                break;
                 }
                 Console.WriteLine(" Recv bytes from client" + Encoding.ASCII.GetString(Recv), Length);
            }
             catch (Exception e)
             { 
            Console.WriteLine("Read from client error: " + e.Message);
            break;
            }

            try 
            {
                Length = server.Send(Recv, Length,0); 
                Console.WriteLine(" Write bytes to server{0}", Length); 
            }
             catch (Exception e) 
            { 
                 Console.WriteLine("Write data to server error: " + e.Message); 
            break; 
            } 
            }
            // real server send messgae
            if (ReadList.Contains(server)) 
            {
            byte[] Recv = new byte[1024]; 
            int Length = 0; 
             try
             { 
            Length = server.Receive(Recv, Recv.Length, 0); 
            if (Length == 0)
             {
             Console.WriteLine("Server is disconnect"); 
            break; 
            } 
            Console.WriteLine("Recv bytes from server", Length); 
            } 
            catch (Exception e) 
            { 
            Console.WriteLine("Read from server error: " + e.Message);
             break;
             }
            try
            {
             Length = client.Send(Recv, Length, 0);
             Console.WriteLine(" Write bytes to client", Length);
             }
             catch (Exception e)
             {
             Console.WriteLine("Write data to client error: " + e.Message);
             break;
             }
             }

             }
             try
            {
             client.Shutdown(SocketShutdown.Both);
             server.Shutdown(SocketShutdown.Both);
             client.Close();
             server.Close();
            } 
            catch (Exception e)
            {
            Console.WriteLine(e.Message);
            }
            finally
            {
            Console.WriteLine("connect close");
             }
        }
        //Resource discovery Handle
        private void HandleDiscover(Request request)
        {
            Response response = request.ReceiveResponse();
            string responseCoap="";
            if (null == response)
            {
                // timeout
                responseCoap = "Timeout";
            }
            else
            {
                Resource root = RemoteResource.NewRoot(response.PayloadString);
                responseCoap = "HTTP/1.1 200 OK\r\n";
                responseCoap += "Content-Type: text/html; charset=UTF-8\r\n\r\n";
                responseCoap += "<html><head><title>resource list</title></head><body>resource list:<ul>";
                responseCoap+=PopulateResources(root, GetAbsolutePath(request.URI));
                responseCoap += "</ul></body></html>";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(responseCoap);
            this.clientSocket.Send(bytes);
            this.clientSocket.Disconnect(false);
            this.clientSocket.Dispose();
        }

        //get resouce path
        private static String GetAbsolutePath(Uri uri)
        {
            if ("/".Equals(uri.AbsolutePath) && !uri.OriginalString.EndsWith("/"))
                return uri.OriginalString;
            else
                return uri.OriginalString.Substring(0, uri.OriginalString.Length - uri.PathAndQuery.Length);
        }
        //make a html index
        private string PopulateResources(Resource currentRoot, String baseUri)
        {
            Resource[] resources = currentRoot.GetSubResources();
            string returnLi="";
            foreach (Resource res in resources)
            {
                if (res.SubResourceCount > 0)
                {
                    returnLi += PopulateResources(res, baseUri);
                }
                else
                {
                    if (res.ResourceType != null && res.ResourceType.ToLower() == "observe")
                    {
                        returnLi += String.Format("<li><a href=\"{0}\">{1}</a><br/><span>*can be observed*</span><br/><span>*resource title:{2}</span></li>", baseUri.Replace("coap", "http") + res.Path, res.Path.Substring(1, res.Path.Length - 1), res.Title);
                    }
                    else
                    {
                        returnLi += String.Format("<li><a href=\"{0}\">{1}</a><br/><span>*resource title:{2}</span></li>", baseUri.Replace("coap", "http") + res.Path, res.Path.Substring(1, res.Path.Length - 1), res.Title);
                    }
                }
            }
            return returnLi;
        }

        //send nomal coap request
        private Request PerformCoAP(Request.Method method, Uri uri, Boolean messageVisible, Boolean observe)
        {

            if (null != _currentRequest)
                _currentRequest.Cancel();

            Request request = Request.Create(method);
            _currentRequest = request;
            request.URI = uri;

            if (method == Request.Method.POST || method == Request.Method.PUT)
            {
                request.SetPayload(this.Payload, MediaType.TextPlain);
            }
            if (messageVisible)
            {
                request.Responding += new EventHandler<ResponseEventArgs>(request_Responding);
                request.Responded += new EventHandler<ResponseEventArgs>(request_Responded);
            }
            request.ResponseQueueEnabled = true;
            request.Execute();
            return request;
        }

        //send observe request
        private void PerformObserve(Request.Method method, Uri uri)
        {
            if (null != _currentRequest)
                _currentRequest.Cancel();

            Request request = Request.Create(method);
            _currentRequest = request;
            request.URI = uri;
            request.AddOption(Option.Create(OptionType.Observe, 60));
            request.Token = TokenManager.Instance.AcquireToken(false);
            request.Responded += new EventHandler<ResponseEventArgs>(observe_Responded);
            _observingRequest = request;
            
            //try Websocket connect
            try
            {
                //wait for websocket connect
                Thread.Sleep(5000);
                observedSession = coapWebSocektService.SessionList[coapWebSocektService.SessionList.Count - 1];
                request.ResponseQueueEnabled = true;
                request.Execute();
            }
            catch
            {
                _observingRequest.RemoveOptions(OptionType.Observe);
                _observingRequest.Execute();
                _observingRequest = null;
                Console.WriteLine("Bad websocket connect");
                this.clientSocket.Send(Encoding.UTF8.GetBytes("Bad websocket connect"));
                this.clientSocket.Disconnect(false);
                this.clientSocket.Dispose();
            }            
        }
        //on receive observe response
        void observe_Responded(Object sender, ResponseEventArgs e)
        {
            if (coapWebSocektService.SessionList.Contains(observedSession))
               coapWebSocektService.SendToObserved(e.Response.PayloadString, observedSession);
            //when the client is closed,cancel the observe
            else
              {
                PerformCoAP(Request.Method.GET, _observingRequest.URI, false, false);
                Console.WriteLine("Websocket链接关闭");
                this.clientSocket.Disconnect(false);
                this.clientSocket.Dispose();
                }
        }

        //on receiving nomal coap response
        void request_Responding(Object sender, ResponseEventArgs e)
        {
            DisplayMessage(e.Response);
        }
        //on receive nomal coap response
        void request_Responded(Object sender, ResponseEventArgs e)
        {
            DisplayMessage(e.Response);         
        }

        //show messgae
        private void DisplayMessage(Message msg)
        {
            try
            {

                IList<Option> options = msg.GetOptions();
                Boolean ifObserve = false;
                string responseCoap;

                responseCoap = "HTTP/1.1 200 OK\r\n";
                responseCoap += "Content-Type: text/html; charset=UTF-8\r\n\r\n";
                //Judge if the resouce can be Observed
                foreach (Option o in options)
                {
                    if (o.Type.Equals(OptionType.MaxAge))
                    {
                        ifObserve = true;
                        break;
                    }
                }
                //if can be Observed
                if (ifObserve)
                {
                    responseCoap += "<!DOCTYPE html PUBLIC \" -//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">"
                        + "<html><head><title>Observe</title>"
                        +"<script type=\"text/javascript\"> var ws; function connectSocketServer() {"
                        + "if (document.getElementById('btnSend').value == \"Observe\"){"
                        + " document.getElementById('messageBoard').innerHTML = \"* Connecting to server ..<br/>\"; document.getElementById('btnSend').value =\"Cancel\";"
                        + " ws = new WebSocket('ws://59.64.38.126:912');"
                        + " var script_el=document.createElement(\"script\"); script_el.type=\"text/javascript\";"
                        + " script_el.src=window.location.href + \"?observehost\";"
                        + " document.getElementById('messageBoard').appendChild(script_el);"
                        +  " ws.onmessage = function (evt) { document.getElementById('messageBoard').innerHTML+=\"# \" + evt.data + \"<br />\"; };"
                        + " ws.onopen = function () { document.getElementById('messageBoard').innerHTML +=\"* Connection open<br/>\";};"
                        + " ws.onclose = function (){ document.getElementById('messageBoard').innerHTML += \"* Connection closed<br/>\"; };"
                        + "} else{ window.location.reload(); }"
                        + "}</script>"
                        +"</head><body><div id=\"messageBoard\">"
                        + msg.PayloadString
                        + "</div><br/><input type=\"button\" id=\"btnSend\" value=\"Observe\" onclick=\"connectSocketServer();\" /></body></html>";
                }
                else
                {
                    responseCoap += msg.PayloadString;
                }
                Byte[] payloadBytes = msg.Payload;

                if (MediaType.IsImage(msg.ContentType))
                {
                    // show image

                    // doesn't work :(
                    //webBrowserPayload.DocumentStream = new MemoryStream(payloadBytes);

                    // save to a temp file
                    File.WriteAllBytes(_tempImagePath, payloadBytes);
                    String path = "file:///" + _tempImagePath.Replace('\\', '/');
                    responseCoap = String.Format("<html><head><title></title></head><body><img src=\"{0}\" alt=\"\"></body></html>", path);
                }
                byte[] bytes = Encoding.UTF8.GetBytes(responseCoap);

                this.clientSocket.Send(bytes);
                this.clientSocket.Disconnect(false);
                this.clientSocket.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
