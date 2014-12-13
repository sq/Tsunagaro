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
using Newtonsoft.Json.Linq;
using System.Linq;
using ICSharpCode.SharpZipLib.GZip;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;

namespace Tsunagaro {
    public class InputMonitorService {
        public const double ChildRestartTimeoutSeconds = 60;

        public readonly TaskScheduler Scheduler;

        public InputMonitorService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            Scheduler.Start(MainTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            // Program.Control.Handlers.Add("/input", ServeInput);

            yield break;
        }

        private IEnumerator<object> ChildHandler (Process proc, Future<TcpClient> fClient) {
            try {
                Console.WriteLine("Started input hook w/pid {0}; waiting for return connection", proc.Id);

                yield return fClient;
                var client = fClient.Result;

                const int PacketSize = 512;
                var buffer = new byte[PacketSize];

                int MessageSize = Marshal.SizeOf(typeof(Win32.InputEvent));

                var outboundPayload = new Dictionary<string, object> {
                    {"Events", null},
                    {"State",  null}
                };

                using (client)
                using (var adapter = new SocketDataAdapter(client.Client, false))
                while (true) {
                    // Read a single packet of up to PacketSize bytes.
                    // InputHook sets DontFragment so we are going to get a complete set of messages.
                    var fBytesRead = adapter.Read(buffer, 0, PacketSize);
                    yield return fBytesRead;

                    if (fBytesRead.Failed)
                        yield break;

                    outboundPayload["Events"] = Convert.ToBase64String(buffer, 0, fBytesRead.Result, Base64FormattingOptions.None);

                    // Now we rebroadcast the packet over RPC to our peers
                    yield return Program.Peer.Broadcast("RemoteInput", outboundPayload);
                }
            } finally {
                bool hasExited = true;
                try {
                    hasExited = proc.HasExited;
                } catch {
                }
                
                if (!hasExited) {
                    Console.WriteLine("Terminating input hook");
                    proc.Kill();
                } else {
                    Console.WriteLine("Input hook process exited");
                }

                proc.Dispose();
            }
        }

        public IEnumerator<object> MainTask () {
            var listener = new TcpListener(0);
            listener.Start();

            var port = ((IPEndPoint)listener.Server.LocalEndPoint).Port;

            var psi = new ProcessStartInfo("InputHook.exe", port.ToString()) {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            while (true) {
                var fProc = Future.RunInThread(() => Process.Start(psi));

                yield return fProc;

                var fClient = listener.AcceptIncomingConnection();
                using (var fChildHandler = Scheduler.Start(ChildHandler(fProc.Result, fClient))) {
                    // If the process is terminated prematurely, kill the child handler
                    var fTerminated = Future.RunInThread(fProc.Result.WaitForExit);
                    fTerminated.RegisterOnComplete(
                        (_) => fChildHandler.Dispose()
                    );

                    // Run the child handler until it completes
                    yield return fChildHandler;
                }                

                // The child died for some reason, let's wait a moment before restarting it...
                yield return new Sleep(ChildRestartTimeoutSeconds);
            }
        }
    }
}
