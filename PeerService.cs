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

namespace Tsunagaro {
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
            public readonly string     HostName;
            public readonly IPEndPoint RemoteEndPoint;
            public readonly int        Port;

            public readonly SocketDataAdapter Channel;
            public readonly TcpClient         TcpClient;

            public Connection (TcpClient tcpClient, IPEndPoint remoteEndPoint) {
                TcpClient = tcpClient;
                Channel = new SocketDataAdapter(tcpClient.Client, false);
                RemoteEndPoint = remoteEndPoint;
                HostName = Dns.GetHostByAddress(RemoteEndPoint.Address).HostName;
            }

            public void Dispose () {
                TcpClient.Close();
                Channel.Dispose();
            }
        }

        public readonly HashSet<PendingConnection> Pending = new HashSet<PendingConnection>();
        public readonly HashSet<Connection>        Peers = new HashSet<Connection>();

        public readonly TaskScheduler Scheduler;

        public PeerService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            Scheduler.Start(MainTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Program.Control.Handlers.Add("/connect", ServeConnect);

            yield break;
        }

        public IEnumerator<object> MainTask () {
            while (true) {
                yield break;
            }
        }

        public IEnumerator<object> ServeConnect (HttpServer.Request request) {
            var pc = new PendingConnection((IPEndPoint)request.RemoteEndPoint);
            Console.WriteLine("Establishing connection with {0}", pc.RemoteEndPoint);
            Scheduler.Start(AwaitConnection(pc), TaskExecutionPolicy.RunAsBackgroundTask);

            request.Response.ContentType = "text/plain";

            var address = String.Format("{0}:{1}", Dns.GetHostName(), pc.Port);
            yield return ControlService.WriteResponseBody(request, address);
        }

        private IEnumerator<object> AwaitConnection (PendingConnection pc) {
            Pending.Add(pc);
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
                Pending.Remove(pc);
                pc.Dispose();
            }
        }

        private IEnumerator<object> HandleConnection (Connection conn) {
            Peers.Add(conn);
            try {
                yield break;
            } finally {
                Console.WriteLine("Disconnected from {0}", conn.RemoteEndPoint);
                Peers.Remove(conn);
                conn.Dispose();
            }
        }

        public IEnumerator<object> TryConnectTo (IPEndPoint endpoint) {
            Console.WriteLine("Handshaking with {0}", endpoint);

            var req = WebRequest.CreateHttp(String.Format("http://{0}/connect", endpoint));
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
        }
    }
}
