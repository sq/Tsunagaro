using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using Squared.Task;
using Squared.Task.IO;
using System.Diagnostics;

namespace Tsunagaro {
    public class DiscoveryService {
        public const int DiscoveryPort = 9887;
        public const double HeartbeatIntervalSeconds = 60 * 5;

        public readonly TaskScheduler Scheduler;
        private readonly Signal EarlyAnnounceSignal = new Signal();

        public UdpClient Listener;

        public DiscoveryService (TaskScheduler scheduler) {
            Scheduler = scheduler;
        }

        public IEnumerator<object> Initialize () {
            yield return Future.RunInThread(() => {
                Listener = new UdpClient() {
                    ExclusiveAddressUse = false,
                    EnableBroadcast = true,
                    MulticastLoopback = true,
                    DontFragment = true
                };

                Listener.Client.ExclusiveAddressUse = false;
                Listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); 

                Listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            });

            Scheduler.Start(ListenTask(), TaskExecutionPolicy.RunAsBackgroundTask);

            Scheduler.Start(HeartbeatTask(), TaskExecutionPolicy.RunAsBackgroundTask);
        }

        public IEnumerator<object> HeartbeatTask () {
            while (true) {
                yield return Announce();

                yield return Future.WaitForFirst(
                    EarlyAnnounceSignal.Wait(),
                    Scheduler.Start(new Sleep(HeartbeatIntervalSeconds))
                );
            }
        }

        public IEnumerator<object> ListenTask () {
            while (true) {
                var fAnnouncement = Listener.AsyncReceive();
                yield return fAnnouncement;

                Scheduler.Start(ProcessAnnouncement(fAnnouncement.Result), TaskExecutionPolicy.RunAsBackgroundTask);
            }
        }

        public IEnumerator<object> Announce () {
            var payload = BitConverter.GetBytes(Program.Control.Port);
            yield return Listener.AsyncSend(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
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

            var endpoint = new IPEndPoint(
                packet.EndPoint.Address, 
                BitConverter.ToInt32(packet.Bytes, 0)
            );

            if (
                fAddresses.Result.Contains(endpoint.Address) &&
                (endpoint.Port == Program.Control.Port)
            ) {
                // This discovery ping is from me
            } else {
                Console.WriteLine("Got peer announcement from {0}", endpoint);

                yield return Program.Peer.TryConnectTo(endpoint);
            }
        }
    }
}
