using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Squared.Task;
using Squared.Task.IO;

namespace Tsunagaro {
    class Program {
        public static readonly TaskScheduler           Scheduler = new TaskScheduler();
        public static          Win32.InputEventMonitor Monitor;

        public static readonly int                     MessageSize = Marshal.SizeOf(typeof(Win32.InputEvent));

        public static void Main (string[] args) {
            if (args.Length != 1) {
                Console.WriteLine("usage: InputHook port");
                Environment.Exit(1);
            }

            int port = int.Parse(args[0]);

            Monitor = new Win32.InputEventMonitor();

            Scheduler.Start(MainTask(port), TaskExecutionPolicy.RunAsBackgroundTask);
            Scheduler.Start(MonitorParent(), TaskExecutionPolicy.RunAsBackgroundTask);

            while (true) {
                Scheduler.Step();
                Scheduler.WaitForWorkItems();
            }
        }

        private static unsafe int FillBuffer (byte[] buffer, ArraySegment<Win32.InputEvent> events) {
            fixed (byte* pPacketBuffer = buffer) {
                for (int i = 0, l = events.Count; i < l; i++)
                    Marshal.StructureToPtr(events.Array[events.Offset + i], new IntPtr(&pPacketBuffer[(i * MessageSize)]), false);
            }

            return (events.Count * MessageSize);
        }

        public static IEnumerator<object> MonitorParent () {
            var fTerminated = Future.RunInThread<string>(Console.In.ReadToEnd);

            yield return fTerminated;

            Console.WriteLine("INPUTHOOK: Parent closed input stream");

            Environment.Exit(2);
        }

        public static IEnumerator<object> MainTask (int port) {
            var endpoint = new IPEndPoint(IPAddress.Loopback, port);

            const int PacketSize = 512;

            var packetBuffer = new byte[PacketSize];
            var maxMessagesPerPacket = PacketSize / MessageSize;
            var eventBuffer = new Win32.InputEvent[maxMessagesPerPacket];

            Console.WriteLine("INPUTHOOK: Connecting to " + endpoint);

            var fClient = Network.ConnectTo(endpoint.Address, endpoint.Port);
            yield return fClient;

            try {
                using (var client = fClient.Result)
                using (var adapter = new Squared.Task.IO.SocketDataAdapter(client.Client, false))
                using (Monitor) {
                    Console.WriteLine("INPUTHOOK: Forwarding input events to " + endpoint);

                    client.Client.DontFragment = true;
                    client.Client.NoDelay = true;

                    Monitor.Start();

                    while (true) {
                        var fNumEventsRead = Monitor.ReadEvents(eventBuffer, 0, maxMessagesPerPacket);
                        yield return fNumEventsRead;

                        var packetLength = FillBuffer(packetBuffer, new ArraySegment<Win32.InputEvent>(eventBuffer, 0, fNumEventsRead.Result));

                        yield return adapter.Write(packetBuffer, 0, packetLength);
                    }
                }
            } finally {
                Console.WriteLine("INPUTHOOK: Exiting");
                Environment.Exit(1);
            }
        }
    }
}
