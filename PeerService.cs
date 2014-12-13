using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Squared.Task;
using Squared.Task.IO;
using Squared.Task.Http;
using System.Windows.Forms;
using System.Web;
using System.IO;
using System.Net.Sockets;
using System.Net;
using Newtonsoft.Json;

namespace Tsunagaro {
    public delegate IEnumerator<object> MessageHandler (PeerService.Connection sender, Dictionary<string, object> message);

    public class PeerService {
        public class PendingConnection : IDisposable {
            public readonly IPEndPoint RemoteEndPoint;
            public readonly int        Port;

            public readonly TcpListener       Listener;
            public readonly Future<TcpClient> Future;

            public PendingConnection (IPEndPoint remoteEndPoint) {
                RemoteEndPoint = remoteEndPoint;

                Listener = new TcpListener(0);
                Listener.Start();

                Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
                Future = Listener.AcceptIncomingConnection();
            }

            public void Dispose () {
                Listener.Stop();
            }
        }

        public class Connection : IDisposable {
            public struct PendingResponse {
                public string Message;
                public IFuture Future;
            }

            public readonly string     HostName;
            public readonly IPEndPoint RemoteEndPoint;
            public readonly int        Port;

            public readonly SocketDataAdapter Channel;
            public readonly AsyncTextReader   Input;
            public readonly AsyncTextWriter   Output;
            public readonly TcpClient         TcpClient;

            private int NextResponseToken;
            public readonly Dictionary<int, PendingResponse> PendingResponses = new Dictionary<int, PendingResponse>();

            public Connection (TcpClient tcpClient, IPEndPoint remoteEndPoint) {
                TcpClient = tcpClient;
                Channel = new SocketDataAdapter(tcpClient.Client, false) {
                    ThrowOnDisconnect = true,
                    ThrowOnFullSendBuffer = false
                };
                RemoteEndPoint = remoteEndPoint;
                HostName = Dns.GetHostByAddress(RemoteEndPoint.Address).HostName;
                Input = new AsyncTextReader(Channel, false);
                Output = new AsyncTextWriter(Channel, false) {
                    AutoFlush = true
                };
            }

            private SignalFuture WriteMessage (Dictionary<string, object> body) {
                if (body == null)
                    throw new ArgumentNullException("body");

                return Output.WriteLine(JsonConvert.SerializeObject(body, Formatting.None));
            }

            // Does not wait for a response
            public SignalFuture PostMessage (string message, Dictionary<string, object> payload = null) {
                if (payload == null)
                    payload = new Dictionary<string, object>();
                
                payload["_Message_"] = message;
                return WriteMessage(payload);
            }

            // Waits for a response
            public Future<TResult> SendMessage<TResult> (string message, Dictionary<string, object> payload = null) {
                if (payload == null)
                    payload = new Dictionary<string, object>();

                var result = new Future<TResult>();

                int token = NextResponseToken++;
                payload["_Message_"] = message;
                payload["_Token_"] = token;
                PendingResponses[token] = new PendingResponse {
                    Message = message,
                    Future = result
                };

                // Wait?
                WriteMessage(payload);

                return result;
            }

            public void Dispose () {
                TcpClient.Close();
                Channel.Dispose();
            }
        }

        public readonly HashSet<IPEndPoint>                Pending = new HashSet<IPEndPoint>();
        public readonly Dictionary<IPEndPoint, Connection> Peers   = new Dictionary<IPEndPoint, Connection>();

        public readonly Dictionary<string, MessageHandler> MessageHandlers = new Dictionary<string, MessageHandler>();

        public readonly TaskScheduler Scheduler;

        public PeerService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            Program.Control.Handlers.Add("/connect", ServeConnect);

            yield break;
        }

        public IEnumerator<object> ServeConnect (HttpServer.Request request) {
            if (
                !request.QueryString.ContainsKey("myAddress") ||
                !request.QueryString.ContainsKey("myPort")
            ) {
                yield return ControlService.ServeError(request, 501, "argument missing");
                Console.WriteLine("Rejected connection attempt from {0} with missing arguments", request.RemoteEndPoint);
                yield break;
            }

            var fRemoteEndPoint = Future.RunInThread(() => 
                new IPEndPoint(
                    Dns.GetHostByName(request.QueryString["myAddress"]).AddressList.First(),
                    int.Parse(request.QueryString["myPort"])
                )
            );
            yield return fRemoteEndPoint;

            if (!fRemoteEndPoint.Result.Address.Equals(((IPEndPoint)request.RemoteEndPoint).Address)) {
                yield return ControlService.ServeError(request, 501, "address mismatch");
                Console.WriteLine("Rejected mismatched connection attempt from {0}", request.RemoteEndPoint);
                yield break;
            }

            if (
                Pending.Contains(fRemoteEndPoint.Result) ||
                Peers.ContainsKey(fRemoteEndPoint.Result)
            ) {
                yield return ControlService.ServeError(request, 501, "already connecting or connected");
                Console.WriteLine("Rejected duplicate connection attempt from {0}", request.RemoteEndPoint);
                yield break;
            }

            var pc = new PendingConnection(fRemoteEndPoint.Result);
            Console.WriteLine("Establishing connection with {0}", fRemoteEndPoint.Result);
            Scheduler.Start(AwaitConnection(pc), TaskExecutionPolicy.RunAsBackgroundTask);

            request.Response.ContentType = "text/plain";

            var address = String.Format("{0}:{1}", Program.Control.HostName, pc.Port);
            yield return ControlService.WriteResponseBody(request, address);
        }

        private IEnumerator<object> AwaitConnection (PendingConnection pc) {
            if (Pending.Contains(pc.RemoteEndPoint))
                throw new InvalidOperationException(String.Format("Already connecting to {0}", pc.RemoteEndPoint));

            Pending.Add(pc.RemoteEndPoint);
            try {
                var wwt = new WaitWithTimeout(pc.Future, 5);
                var f = Scheduler.Start(wwt);

                yield return f;

                if (f.Failed)
                    Console.WriteLine("Connection request from {0} timed out", pc.RemoteEndPoint);
                else
                    Console.WriteLine("Connection established with {0}", pc.RemoteEndPoint);

                var conn = new Connection(pc.Future.Result, pc.RemoteEndPoint);
                Scheduler.Start(HandleConnection(conn), TaskExecutionPolicy.RunAsBackgroundTask);
            } finally {
                Pending.Remove(pc.RemoteEndPoint);
                pc.Dispose();
            }
        }

        private IEnumerator<object> ProcessMessage (Connection conn, string messageJson) {
            var fParsedMessage = Future.RunInThread(() => JsonConvert.DeserializeObject<Dictionary<string, object>>(messageJson));
            yield return fParsedMessage;

            var msg = fParsedMessage.Result;

            MessageHandler handler;
            var messageName = Convert.ToString(msg["_Message_"]);

            if (messageName == "_Result_") {
                int token = Convert.ToInt32(msg["Token"]);
                Connection.PendingResponse pr;

                if (conn.PendingResponses.TryGetValue(token, out pr)) {
                    conn.PendingResponses.Remove(token);

                    if (msg.ContainsKey("Result")) {
                        // Console.WriteLine("{0} <- {1}[{2}] = {3}", conn.RemoteEndPoint, pr.Message, token, msg["Result"]);

                        pr.Future.Complete(msg["Result"]);
                    } else if (msg.ContainsKey("Error")) {
                        Console.WriteLine("{0} <- {1}[{2}] = error", conn.RemoteEndPoint, pr.Message, token);

                        pr.Future.Fail(new Exception(Convert.ToString(msg["Error"])));
                    } else {
                        Console.WriteLine("{0} <- {1}[{2}] = error", conn.RemoteEndPoint, pr.Message, token);

                        pr.Future.Fail(new Exception("Unknown error"));
                    }
                }

                yield break;
            }

            if (MessageHandlers.TryGetValue(messageName, out handler)) {
                // Console.WriteLine("{0} -> {1} (handled by {2}.{3})", conn.RemoteEndPoint, messageName, handler.Target.GetType().Name, handler.Method.Name);

                var fHandler = Scheduler.Start(handler(conn, msg), TaskExecutionPolicy.RunAsBackgroundTask);
                yield return fHandler;

                if (msg.ContainsKey("_Token_")) {
                    int token = Convert.ToInt32(msg["_Token_"]);
                    var payload = new Dictionary<string, object> {
                        {"Token", token},
                    };

                    if (fHandler.Failed) {
                        payload["Error"] = fHandler.Error.ToString();
                    } else {
                        payload["Result"] = fHandler.Result;
                    }

                    yield return conn.PostMessage(
                        "_Result_", payload
                    );
                }
            } else {
                Console.WriteLine("{0} -> {1} (unhandled)", conn.RemoteEndPoint, messageName);
            }
        }

        private IEnumerator<object> HandleConnection (Connection conn) {
            if (Peers.ContainsKey(conn.RemoteEndPoint))
                throw new InvalidOperationException(String.Format("Got duplicate connection for {0}", conn.RemoteEndPoint));

            Peers.Add(conn.RemoteEndPoint, conn);
            try {
                while (true) {
                    var fMsg = conn.Input.ReadLine();
                    yield return fMsg;

                    if (fMsg.Failed)
                        break;

                    Scheduler.Start(ProcessMessage(conn, fMsg.Result), TaskExecutionPolicy.RunAsBackgroundTask);
                }
            } finally {
                Console.WriteLine("Disconnected from {0}", conn.RemoteEndPoint);
                Peers.Remove(conn.RemoteEndPoint);
                conn.Dispose();
            }
        }

        public IEnumerator<object> TryConnectTo (IPEndPoint endpoint) {
            if (Peers.ContainsKey(endpoint)) {
                // Console.WriteLine("Already connected to {0}", endpoint);
                yield break;
            } else if (Pending.Contains(endpoint)) {
                // Console.WriteLine("Already connecting to {0}", endpoint);
                yield break;
            }

            try {
                Pending.Add(endpoint);
                Console.WriteLine("Handshaking with {0}", endpoint);

                var req = WebRequest.CreateHttp(String.Format(
                    "http://{0}/connect?myAddress={1}&myPort={2}",
                    endpoint,
                    HttpUtility.UrlEncode(Program.Control.HostName),
                    Program.Control.Port
                ));
                var fResponse = req.IssueAsync(Scheduler);

                yield return fResponse;

                if (fResponse.Failed) {
                    Console.WriteLine("Connection to {0} failed: {1}", endpoint, fResponse.Error);
                    yield break;
                }

                var addressText = fResponse.Result.Body;
                var channelHost = addressText.Substring(0, addressText.IndexOf(":"));
                var channelPort = int.Parse(addressText.Substring(addressText.IndexOf(":") + 1));

                Console.WriteLine("Connecting to {0} at {1}", endpoint, addressText);

                var fClient = Network.ConnectTo(channelHost, channelPort);
                yield return fClient;

                if (fClient.Failed) {
                    Console.WriteLine("Connection to {0} failed: {1}", endpoint, fClient.Error);
                    yield break;
                }

                Console.WriteLine("Connection established with {0}", endpoint);

                var conn = new Connection(fClient.Result, endpoint);
                Scheduler.Start(HandleConnection(conn), TaskExecutionPolicy.RunAsBackgroundTask);
            } finally {
                Pending.Remove(endpoint);
            }
        }

        public IFuture Broadcast (string message, Dictionary<string, object> payload = null) {
            if (Peers.Count == 0)
                return new SignalFuture(true);

            var futures = new List<SignalFuture>();

            foreach (var peer in Peers.Values)
                futures.Add(peer.PostMessage(message, payload));

            return Future.WaitForAll(futures);
        }
    }
}
