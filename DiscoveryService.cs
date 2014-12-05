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
        public struct Host {
            public IPEndPoint EndPoint;
            public int Pid;
        }

        public const int DiscoveryPort = 9887;

        public readonly TaskScheduler Scheduler;
        public readonly HashSet<Host> KnownHosts = new HashSet<Host>();
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
                    Scheduler.Start(new Sleep(60))
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
            var payload = BitConverter.GetBytes(Process.GetCurrentProcess().Id);
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

            int pid = BitConverter.ToInt32(packet.Bytes, 0);
            var host = new Host {
                EndPoint = packet.EndPoint,
                Pid = pid
            };

            if (
                fAddresses.Result.Contains(packet.EndPoint.Address) &&
                (pid == Process.GetCurrentProcess().Id)
            ) {
            } else {
                bool isNew = !KnownHosts.Contains(host);
                KnownHosts.Add(host);

                if (isNew) {
                    Console.WriteLine("Discovered host {0} pid {1}", packet.EndPoint, pid);

                    EarlyAnnounceSignal.Set();
                } else {
                    Console.WriteLine("Got heartbeat from host {0} pid {1}", packet.EndPoint, pid);
                }
            }
        }
    }
}
