using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using Squared.Task;
using Squared.Task.IO;

namespace Tsunagaro {
    public class DiscoveryService {
        public const int DiscoveryPort = 9887;

        public readonly TaskScheduler Scheduler;

        public UdpClient Listener;

        public DiscoveryService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            yield return Future.RunInThread(() =>
                Listener = new UdpClient(DiscoveryPort, AddressFamily.InterNetwork)
            );

            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Scheduler.Start(Announce(), TaskExecutionPolicy.RunAsBackgroundTask);
        }

        public IEnumerator<object> ListenTask () {
            while (true) {
                var fAnnouncement = Listener.AsyncReceive();
                yield return fAnnouncement;

                Scheduler.Start(ProcessAnnouncement(fAnnouncement.Result), TaskExecutionPolicy.RunAsBackgroundTask);
            }
        }

        public IEnumerator<object> Announce () {
            yield return Listener.AsyncSend(new byte[1], 1, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }

        private IEnumerator<object> ProcessAnnouncement (Network.UdpPacket packet) {
            var fAddresses = new Future<IPAddress[]>();
            Dns.BeginGetHostAddresses(
                Dns.GetHostName(), 
                (ar) => {
                    try {
                        fAddresses.Complete(Dns.EndGetHostAddresses(ar));
                    } catch (Exception exc) {
                        fAddresses.Fail(exc);
                    }
                },
                null
            );

            yield return fAddresses;

            if (fAddresses.Result.Contains(packet.EndPoint.Address)) {
                Console.WriteLine("Got announcement from myself", packet.EndPoint);
            } else {
                Console.WriteLine("Got announcement from {0}", packet.EndPoint);
            }
        }
    }
}
